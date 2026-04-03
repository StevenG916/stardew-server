using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using StardewDedicatedServer.Framework.Patches;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Manages day transitions for the dedicated server.
/// Monitors when all real players are in bed and triggers the host bot to sleep
/// using the game's actual multiplayer sleep coordination system:
/// SetLocalReady("sleep") + ReadyCheckDialog.
/// </summary>
public sealed class DayManager
{
    private readonly ServerLogger Logger;
    private readonly ModConfig Config;
    private readonly PlayerManager Players;
    private readonly IModHelper Helper;

    private SleepState sleepState = SleepState.Awake;
    private int bedCheckTimer;
    private int sleepDebounce;
    private int lastSaveTime;
    private bool isSaving;

    private const int BedCheckInterval = 60; // ~1 second
    private const int SleepDebounceTicks = 300; // ~5 seconds between attempts

    private enum SleepState
    {
        Awake,
        WarpingToFarmHouse,
        ReadyToSleep,
        Sleeping,
        Debouncing
    }

    /// <summary>Whether the host is currently in any stage of the sleep process.</summary>
    public bool IsSleeping => this.sleepState != SleepState.Awake && this.sleepState != SleepState.Debouncing;

    public DayManager(ServerLogger logger, ModConfig config, PlayerManager players, IModHelper helper)
    {
        this.Logger = logger;
        this.Config = config;
        this.Players = players;
        this.Helper = helper;
    }

    public void RegisterEvents(IModEvents events)
    {
        events.GameLoop.DayStarted += this.OnDayStarted;
        events.GameLoop.DayEnding += this.OnDayEnding;
        events.GameLoop.Saving += this.OnSaving;
        events.GameLoop.Saved += this.OnSaved;
        events.GameLoop.UpdateTicked += this.OnUpdateTicked;
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.sleepState = SleepState.Awake;
        this.sleepDebounce = 0;
        this.lastSaveTime = Game1.timeOfDay;

        // Re-enable headless mode after day transition completes
        if (this.Config.HeadlessMode)
        {
            HeadlessPatches.SetEnabled(true);
            if (this.Config.NoXvfbMode)
                NoXvfbPatches.SetEnabled(true);
            this.Logger.Debug("Rendering disabled again (headless mode restored)");
        }

        string season = Game1.currentSeason ?? "unknown";
        season = char.ToUpper(season[0]) + season[1..];
        this.Logger.DayStarted(season, Game1.dayOfMonth, Game1.year);
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.Logger.Debug("Day ending...");
        this.sleepState = SleepState.Awake;
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.Logger.Saving();
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        this.Logger.SaveComplete();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        // Periodic auto-save without advancing the day.
        // Uses SaveGame.getSaveEnumerator() directly on a background thread
        // instead of SaveGameMenu, which requires multiplayer sync and rendering.
        if (this.Config.AutoSaveInterval > 0 && this.sleepState == SleepState.Awake && this.Players.AnyConnected)
        {
            int elapsed = Game1.timeOfDay - this.lastSaveTime;
            if (elapsed >= this.Config.AutoSaveInterval && !this.isSaving)
            {
                this.lastSaveTime = Game1.timeOfDay;
                this.TriggerBackgroundSave();
            }
        }

        if (!this.Config.AutoSleep)
            return;

        switch (this.sleepState)
        {
            case SleepState.Awake:
                this.bedCheckTimer++;
                if (this.bedCheckTimer < BedCheckInterval)
                    return;
                this.bedCheckTimer = 0;

                if (this.ShouldSleep())
                    this.StartSleepProcess();
                break;

            case SleepState.WarpingToFarmHouse:
                if (Game1.currentLocation is FarmHouse)
                {
                    this.sleepState = SleepState.ReadyToSleep;
                    this.DoSleep();
                }
                break;

            case SleepState.ReadyToSleep:
            case SleepState.Sleeping:
                // If we're in save flow, stay in Sleeping state
                if (Game1.activeClickableMenu is SaveGameMenu)
                    break;

                // If sleep was cancelled (dialog closed, not in bed anymore)
                if (!(Game1.activeClickableMenu is ReadyCheckDialog) && !Game1.player.isInBed.Value)
                {
                    this.Logger.Debug("Sleep was cancelled or interrupted, debouncing...");
                    this.sleepState = SleepState.Debouncing;
                    this.sleepDebounce = SleepDebounceTicks;
                }
                break;

            case SleepState.Debouncing:
                this.sleepDebounce--;
                if (this.sleepDebounce <= 0)
                {
                    this.sleepState = SleepState.Awake;
                    this.Logger.Debug("Sleep debounce complete, ready to retry");
                }
                break;
        }
    }

    private bool ShouldSleep()
    {
        if (!this.Players.AnyConnected)
            return false;

        if (Game1.timeOfDay < 1800)
            return false;

        int totalFarmhands = 0;
        int inBedCount = 0;

        foreach (var farmer in Game1.getOnlineFarmers())
        {
            if (farmer.IsMainPlayer)
                continue;

            totalFarmhands++;

            if (farmer.isInBed.Value && farmer.currentLocation is FarmHouse)
                inBedCount++;
        }

        if (totalFarmhands == 0)
            return false;

        bool shouldSleep = inBedCount == totalFarmhands;

        if (shouldSleep)
            this.Logger.Info($"Sleep check: {inBedCount}/{totalFarmhands} farmhands in bed (time: {Game1.timeOfDay})");

        return shouldSleep;
    }

    private void StartSleepProcess()
    {
        this.Logger.Info("All players in bed — initiating host sleep");

        if (Game1.currentLocation is FarmHouse)
        {
            this.sleepState = SleepState.ReadyToSleep;
            this.DoSleep();
        }
        else
        {
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
    /// Trigger sleep using the game's actual multiplayer sleep coordination.
    /// Sets bed flags, marks the host as ready for sleep via FarmerTeam,
    /// and creates a ReadyCheckDialog so the game's sync detects all-sleeping.
    /// </summary>
    private void DoSleep()
    {
        try
        {
            var host = Game1.player;

            // Set bed state flags
            host.isInBed.Value = true;
            host.sleptInTemporaryBed.Value = true;
            host.timeWentToBed.Value = Game1.timeOfDay;

            // Add to announced sleeping farmers
            if (!host.team.announcedSleepingFarmers.Contains(host))
                host.team.announcedSleepingFarmers.Add(host);

            // Mark host as ready for sleep via the game's ready check system.
            // Game1.netReady is the ReadySynchronizer — this is the same call
            // that FarmHouse.startSleep() makes in the normal game flow.
            Game1.netReady.SetLocalReady("sleep", true);
            Game1.dialogueUp = false;
            this.Logger.Debug("Game1.netReady.SetLocalReady('sleep', true) called");

            // Create ReadyCheckDialog with the same pattern as FarmHouse.startSleep().
            // The dialog's update() calls SetLocalReady every frame and checks IsReady.
            // When all players are ready, it calls the confirm callback which triggers doSleep.
            // The draw method is patched out in HeadlessPatches so this won't crash.
            Game1.activeClickableMenu = new ReadyCheckDialog("sleep", true, _ =>
            {
                this.Logger.Info("ReadyCheckDialog confirm — triggering day transition");

                // Temporarily re-enable rendering for the day transition.
                // Game1.NewDay() triggers screen fades whose completion callbacks
                // drive the actual day advancement. In headless mode, fades never
                // complete because Draw is skipped. Enabling rendering lets the
                // game's normal transition run, then we disable it again on DayStarted.
                HeadlessPatches.SetEnabled(false);
                NoXvfbPatches.SetEnabled(false);
                this.Logger.Debug("Rendering temporarily enabled for day transition");

                // Set player sleep metadata
                Game1.player.lastSleepLocation.Value = Game1.currentLocation?.NameOrUniqueName;
                Game1.player.mostRecentBed = Game1.player.Position;

                // Let the game's normal day transition handle everything
                Game1.NewDay(0f);
                this.Logger.Info("Game1.NewDay(0f) called — rendering enabled for transition");
            });

            this.sleepState = SleepState.Sleeping;
            this.Logger.Info("Host sleep initiated — ReadyCheckDialog created, waiting for day transition");
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to trigger host sleep: {ex.Message}");
            this.sleepState = SleepState.Debouncing;
            this.sleepDebounce = SleepDebounceTicks;
        }
    }

    /// <summary>
    /// Save the game on a background thread without blocking gameplay.
    /// Uses SaveGame.getSaveEnumerator() directly, bypassing SaveGameMenu
    /// which freezes the game and requires multiplayer sync.
    /// </summary>
    private void TriggerBackgroundSave()
    {
        this.isSaving = true;
        this.Logger.Info($"Auto-saving at {Game1.timeOfDay}...");

        Task.Run(() =>
        {
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                IEnumerator<int> enumerator = SaveGame.getSaveEnumerator();
                while (enumerator.MoveNext())
                {
                    // getSaveEnumerator yields progress values 1-100
                }
                this.Logger.Info("Auto-save complete");
            }
            catch (Exception ex)
            {
                this.Logger.Error($"Auto-save failed: {ex.Message}");
            }
            finally
            {
                this.isSaving = false;
            }
        });
    }

}
