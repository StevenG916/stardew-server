using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Controls the host farmer "bot" that keeps the server alive.
/// Handles keeping the bot invisible, maintaining energy/health,
/// managing game pausing when empty, and coordinating with other managers.
/// </summary>
public sealed class ServerBot
{
    /*********
    ** Fields
    *********/

    private readonly ServerLogger Logger;
    private readonly ModConfig Config;
    private readonly PlayerManager Players;

    /// <summary>Whether the server is currently paused due to no players.</summary>
    private bool isPausedEmpty;

    /// <summary>Countdown ticks before pausing when empty.</summary>
    private int pauseCountdown = -1;

    /// <summary>Whether the initial farm setup is complete.</summary>
    private bool farmSetupComplete;

    /*********
    ** Public Methods
    *********/

    public ServerBot(ServerLogger logger, ModConfig config, PlayerManager players)
    {
        this.Logger = logger;
        this.Config = config;
        this.Players = players;

        // Listen for player count changes to handle pause/resume
        this.Players.PlayerCountChanged += this.OnPlayerCountChanged;
    }

    /// <summary>Register event handlers.</summary>
    public void RegisterEvents(IModEvents events)
    {
        events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        events.GameLoop.GameLaunched += this.OnGameLaunched;
    }

    /// <summary>Apply Harmony patches for the server bot.</summary>
    /// <param name="harmony">The Harmony instance.</param>
    public void ApplyPatches(Harmony harmony)
    {
        // Patch to prevent the host farmer from losing energy
        harmony.Patch(
            original: AccessTools.Method(typeof(Farmer), nameof(Farmer.doneEating)),
            prefix: new HarmonyMethod(typeof(ServerBot), nameof(BeforeDoneEating))
        );
    }

    /*********
    ** Event Handlers
    *********/

    /// <summary>Handle game launched — initial setup.</summary>
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.Logger.ServerStarting();
    }

    /// <summary>Handle save loaded — farm is ready for play.</summary>
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (!Context.IsMainPlayer)
            return;

        this.farmSetupComplete = true;

        // Reset bot state
        this.ResetBotFarmer();

        // Enable multiplayer
        this.EnableMultiplayer();

        // Log server ready with port
        int port = 24642; // Default Stardew Valley port
        this.Logger.ServerReady(port);
    }

    /// <summary>Handle returning to title (server crash recovery or intentional).</summary>
    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.farmSetupComplete = false;
        this.isPausedEmpty = false;
        this.pauseCountdown = -1;
        this.Players.Reset();

        this.Logger.Warn("Returned to title screen");

        // If AutoCreateFarm is on, we could try to reload here
        if (this.Config.AutoCreateFarm)
        {
            this.Logger.Info("Attempting to reload farm...");
            // Title menu handling will be done by the title screen automation
        }
    }

    /// <summary>Per-tick update for bot management.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        // Keep bot farmer healthy
        if (e.IsMultipleOf(60)) // Every second
            this.MaintainBotHealth();

        // Handle pause countdown
        this.UpdatePauseState();

        // Handle title menu (auto-load farm)
        if (!this.farmSetupComplete && Game1.activeClickableMenu is TitleMenu)
        {
            this.TryAutoLoadFarm();
        }
    }

    /*********
    ** Private Methods
    *********/

    /// <summary>Called when the connected player count changes.</summary>
    private void OnPlayerCountChanged(int playerCount)
    {
        if (!this.Config.PauseWhenEmpty)
            return;

        if (playerCount == 0 && !this.isPausedEmpty)
        {
            // Start pause countdown
            this.pauseCountdown = this.Config.PauseDelay * 60; // Convert seconds to ticks
            this.Logger.Debug($"No players connected, pausing in {this.Config.PauseDelay}s...");
        }
        else if (playerCount > 0 && (this.isPausedEmpty || this.pauseCountdown >= 0))
        {
            // Cancel pause or resume
            this.pauseCountdown = -1;

            if (this.isPausedEmpty)
            {
                this.isPausedEmpty = false;
                if (Game1.netWorldState?.Value != null)
                    Game1.netWorldState.Value.IsPaused = false;
                this.Logger.ServerResumed();
            }
        }
    }

    /// <summary>Update pause countdown state.</summary>
    private void UpdatePauseState()
    {
        if (this.pauseCountdown < 0)
            return;

        this.pauseCountdown--;

        if (this.pauseCountdown <= 0)
        {
            this.pauseCountdown = -1;
            this.isPausedEmpty = true;

            if (Game1.netWorldState?.Value != null)
                Game1.netWorldState.Value.IsPaused = true;

            this.Logger.ServerPaused();
        }
    }

    /// <summary>Keep the bot farmer at full energy and health.</summary>
    private void MaintainBotHealth()
    {
        var farmer = Game1.player;
        if (farmer == null)
            return;

        farmer.stamina = farmer.MaxStamina;
        farmer.health = farmer.maxHealth;
    }

    /// <summary>Reset bot farmer state for a clean server experience.</summary>
    private void ResetBotFarmer()
    {
        var farmer = Game1.player;
        if (farmer == null)
            return;

        // Max out energy/health
        farmer.stamina = farmer.MaxStamina;
        farmer.health = farmer.maxHealth;

        this.Logger.Debug("Bot farmer state reset");
    }

    /// <summary>Enable multiplayer hosting on the current save.</summary>
    private void EnableMultiplayer()
    {
        try
        {
            // Set the game to allow connections
            Game1.options.enableServer = true;
            Game1.options.serverPrivacy = ServerPrivacy.FriendsOnly; // Can be made configurable

            // Ensure the server is started
            if (Game1.server == null)
            {
                Game1.options.enableServer = true;
            }

            // Build cabins if needed
            this.EnsureCabins();

            this.Logger.Info("Multiplayer hosting enabled");
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to enable multiplayer: {ex.Message}");
        }
    }

    /// <summary>Ensure enough cabins exist for the configured max players.</summary>
    private void EnsureCabins()
    {
        if (!Context.IsWorldReady)
            return;

        int existingCabins = 0;
        foreach (var building in Game1.getFarm().buildings)
        {
            if (building.buildingType.Value?.Contains("Cabin") == true)
                existingCabins++;
        }

        int needed = this.Config.MaxPlayers - existingCabins;
        if (needed > 0)
        {
            this.Logger.Info($"Farm has {existingCabins} cabins, need {needed} more for {this.Config.MaxPlayers} max players");
            // Note: Auto-building cabins programmatically is complex and may be added in a future update.
            // For now, log the requirement so the admin knows to build them.
        }
    }

    /// <summary>Attempt to auto-load a farm from the title menu.</summary>
    private void TryAutoLoadFarm()
    {
        // This is a simplified handler. Full implementation would:
        // 1. Check if a save exists matching Config.SaveFileName
        // 2. If yes, load it
        // 3. If no, create a new farm with Config settings
        // For now, we log the intent for manual testing

        this.Logger.Debug("Title menu detected — auto-load will be implemented during testing");
    }

    /*********
    ** Harmony Patches
    *********/

    /// <summary>Prevent the host farmer from actually eating (energy is maintained separately).</summary>
    [HarmonyPrefix]
    private static bool BeforeDoneEating(Farmer __instance)
    {
        // Only skip for the main player (host bot)
        if (__instance.IsMainPlayer)
            return false; // Skip original method

        return true; // Let farmhands eat normally
    }
}
