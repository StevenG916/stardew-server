using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Manages day transitions for the dedicated server.
/// Monitors when all real players are in bed and triggers the host bot to sleep,
/// advancing the day. Uses the same approach as SMAPIDedicatedServerMod:
/// warp to FarmHouse → set bed flags → call startSleep() → create ReadyCheckDialog.
/// </summary>
public sealed class DayManager
{
    /*********
    ** Fields
    *********/

    private readonly ServerLogger Logger;
    private readonly ModConfig Config;
    private readonly PlayerManager Players;
    private readonly IModHelper Helper;

    /// <summary>Current sleep state of the bot.</summary>
    private SleepState sleepState = SleepState.Awake;

    /// <summary>Tick counter for periodic bed-check polling.</summary>
    private int bedCheckTimer;

    /// <summary>How often (in ticks) to check if all players are in bed. 60 ticks = ~1 second.</summary>
    private const int BedCheckInterval = 60;

    /*********
    ** Sleep States
    *********/

    private enum SleepState
    {
        Awake,
        WarpingToFarmHouse,
        ReadyToSleep,
        Sleeping
    }

    /*********
    ** Public Methods
    *********/

    public DayManager(ServerLogger logger, ModConfig config, PlayerManager players, IModHelper helper)
    {
        this.Logger = logger;
        this.Config = config;
        this.Players = players;
        this.Helper = helper;
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
        this.sleepState = SleepState.Awake;

        string season = Game1.currentSeason ?? "unknown";
        season = char.ToUpper(season[0]) + season[1..];

        this.Logger.DayStarted(season, Game1.dayOfMonth, Game1.year);
    }

    /// <summary>Handle end of day (before save).</summary>
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.Logger.Debug("Day ending...");
        this.sleepState = SleepState.Awake;
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

        // Handle sleep state machine
        switch (this.sleepState)
        {
            case SleepState.Awake:
                // Poll periodically
                this.bedCheckTimer++;
                if (this.bedCheckTimer < BedCheckInterval)
                    return;
                this.bedCheckTimer = 0;

                if (this.ShouldSleep())
                {
                    this.StartSleepProcess();
                }
                break;

            case SleepState.WarpingToFarmHouse:
                // Wait until we're in the FarmHouse
                if (Game1.currentLocation is FarmHouse)
                {
                    this.sleepState = SleepState.ReadyToSleep;
                    this.DoSleep();
                }
                break;

            case SleepState.ReadyToSleep:
            case SleepState.Sleeping:
                // Waiting for game to process sleep / day transition
                // If we somehow got un-slept (cancelled), go back to Awake
                if (!(Game1.activeClickableMenu is ReadyCheckDialog) && !Game1.player.isInBed.Value)
                {
                    this.Logger.Debug("Sleep was cancelled or interrupted, returning to Awake");
                    this.sleepState = SleepState.Awake;
                }
                break;
        }
    }

    /// <summary>Check if the bot should go to sleep.</summary>
    private bool ShouldSleep()
    {
        // If no one is connected, don't auto-sleep
        if (!this.Players.AnyConnected)
            return false;

        // Don't try to sleep before 6pm game time — players just connected
        if (Game1.timeOfDay < 1800)
            return false;

        // Count real players and how many are in bed
        int totalFarmhands = 0;
        int inBedCount = 0;

        foreach (var farmer in Game1.getOnlineFarmers())
        {
            if (farmer.IsMainPlayer)
                continue;

            totalFarmhands++;

            // Only count as "in bed" if they're actually in a bed location
            // not just because the flag happens to be set
            if (farmer.isInBed.Value && farmer.currentLocation is FarmHouse)
                inBedCount++;
        }

        // Need at least 1 farmhand AND all must be in bed
        if (totalFarmhands == 0)
            return false;

        bool shouldSleep = inBedCount == totalFarmhands;

        if (shouldSleep)
            this.Logger.Info($"Sleep check: {inBedCount}/{totalFarmhands} farmhands in bed (time: {Game1.timeOfDay})");

        return shouldSleep;
    }

    /// <summary>Start the sleep process — warp to FarmHouse first if needed.</summary>
    private void StartSleepProcess()
    {
        this.Logger.Info("All players in bed — initiating host sleep");

        if (Game1.currentLocation is FarmHouse)
        {
            // Already in FarmHouse, go directly to sleep
            this.sleepState = SleepState.ReadyToSleep;
            this.DoSleep();
        }
        else
        {
            // Warp to FarmHouse first
            this.sleepState = SleepState.WarpingToFarmHouse;
            var farmHouse = Game1.getLocationFromName("FarmHouse") as FarmHouse;
            if (farmHouse != null)
            {
                var entry = farmHouse.getEntryLocation();
                Game1.warpFarmer("FarmHouse", entry.X, entry.Y, false);
                this.Logger.Debug("Warping to FarmHouse...");
            }
            else
            {
                this.Logger.Error("Could not find FarmHouse location");
                this.sleepState = SleepState.Awake;
            }
        }
    }

    /// <summary>
    /// Trigger the actual sleep. Sets bed flags only — no ReadyCheckDialog
    /// (it crashes in headless mode). The game's network sync should detect
    /// that all players have isInBed=true and advance the day.
    /// </summary>
    private void DoSleep()
    {
        try
        {
            var host = Game1.player;

            // Set all the bed state flags
            host.isInBed.Value = true;
            host.sleptInTemporaryBed.Value = true;
            host.timeWentToBed.Value = Game1.timeOfDay;

            // Announce the host is sleeping
            if (!host.team.announcedSleepingFarmers.Contains(host))
            {
                host.team.announcedSleepingFarmers.Add(host);
            }

            this.sleepState = SleepState.Sleeping;
            this.Logger.Info("Host bot bed flags set — waiting for game sync");
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to trigger host sleep: {ex.Message}");
            this.sleepState = SleepState.Awake;
        }
    }
}
