using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Manages day transitions for the dedicated server.
/// Monitors when all real players are in bed and triggers the host bot to sleep,
/// advancing the day. Also handles day-start logging and save events.
/// </summary>
public sealed class DayManager
{
    /*********
    ** Fields
    *********/

    private readonly ServerLogger Logger;
    private readonly ModConfig Config;
    private readonly PlayerManager Players;

    /// <summary>Whether we're currently trying to initiate sleep.</summary>
    private bool isTryingToSleep;

    /// <summary>Tick counter for periodic bed-check polling.</summary>
    private int bedCheckTimer;

    /// <summary>How often (in ticks) to check if all players are in bed. 60 ticks = ~1 second.</summary>
    private const int BedCheckInterval = 60;

    /*********
    ** Public Methods
    *********/

    public DayManager(ServerLogger logger, ModConfig config, PlayerManager players)
    {
        this.Logger = logger;
        this.Config = config;
        this.Players = players;
    }

    /// <summary>Register event handlers.</summary>
    public void RegisterEvents(IModEvents events)
    {
        events.GameLoop.DayStarted += this.OnDayStarted;
        events.GameLoop.DayEnding += this.OnDayEnding;
        events.GameLoop.Saving += this.OnSaving;
        events.GameLoop.Saved += this.OnSaved;
        events.GameLoop.UpdateTicked += this.OnUpdateTicked;
    }

    /*********
    ** Private Methods
    *********/

    /// <summary>Handle the start of a new day.</summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.isTryingToSleep = false;

        string season = Game1.currentSeason ?? "unknown";
        // Capitalize first letter
        season = char.ToUpper(season[0]) + season[1..];

        this.Logger.DayStarted(season, Game1.dayOfMonth, Game1.year);
    }

    /// <summary>Handle end of day (before save).</summary>
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.Logger.Debug("Day ending...");
        this.isTryingToSleep = false;
    }

    /// <summary>Handle save starting.</summary>
    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.Logger.Saving();
    }

    /// <summary>Handle save completed.</summary>
    private void OnSaved(object? sender, SavedEventArgs e)
    {
        this.Logger.SaveComplete();
    }

    /// <summary>Per-tick check: if all real players are in bed, put the host to bed too.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        if (!this.Config.AutoSleep)
            return;

        // Don't check every tick — poll periodically
        this.bedCheckTimer++;
        if (this.bedCheckTimer < BedCheckInterval)
            return;
        this.bedCheckTimer = 0;

        // Don't re-trigger if we're already trying to sleep
        if (this.isTryingToSleep)
            return;

        // Check if all connected farmhands are in bed
        if (this.AreAllPlayersInBed())
        {
            this.TriggerHostSleep();
        }
    }

    /// <summary>Check if all connected farmhand players are in bed.</summary>
    /// <returns>True if all farmhands are in bed (or no farmhands connected and no players means skip).</returns>
    private bool AreAllPlayersInBed()
    {
        // If no one is connected, don't auto-sleep (PauseWhenEmpty handles this)
        if (!this.Players.AnyConnected)
            return false;

        // Check all online farmhands
        foreach (var farmer in Game1.getOnlineFarmers())
        {
            // Skip the host (that's us)
            if (farmer.IsMainPlayer)
                continue;

            // If any player is NOT in bed, we can't sleep yet
            if (!farmer.isInBed.Value)
                return false;
        }

        return true;
    }

    /// <summary>Put the host bot character to bed to trigger day advancement.</summary>
    private void TriggerHostSleep()
    {
        this.isTryingToSleep = true;
        this.Logger.Debug("All players in bed — initiating host sleep");

        try
        {
            var host = Game1.player;
            if (host == null)
                return;

            // Warp the host to the farmhouse bed location
            var farmHouse = Utility.getHomeOfFarmer(host);
            if (farmHouse == null)
            {
                this.Logger.Warn("Cannot find host farmhouse for sleep");
                this.isTryingToSleep = false;
                return;
            }

            // Get bed spot
            var bedSpot = farmHouse.GetPlayerBedSpot();

            // Warp host to farmhouse
            Game1.warpFarmer("FarmHouse", bedSpot.X, bedSpot.Y, false);

            // Set the host as in bed
            host.isInBed.Value = true;

            // Trigger the sleep dialogue/process
            // The game checks if all players are in bed during its update loop
            // and automatically advances the day when they are
            this.Logger.Debug("Host bot is now in bed");
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to trigger host sleep: {ex.Message}");
            this.isTryingToSleep = false;
        }
    }
}
