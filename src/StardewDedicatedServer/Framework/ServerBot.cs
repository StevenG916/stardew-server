using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

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

    /// <summary>Whether we've already attempted to auto-load.</summary>
    private bool autoLoadAttempted;

    /// <summary>Tick counter for delayed auto-load (give title menu time to initialize).</summary>
    private int autoLoadDelay = -1;

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

        // Patch checkFarmhandRequest to handle null values (LAN without Galaxy SDK)
        harmony.Patch(
            original: AccessTools.Method(typeof(GameServer), "checkFarmhandRequest"),
            prefix: new HarmonyMethod(typeof(ServerBot), nameof(BeforeCheckFarmhandRequest)),
            finalizer: new HarmonyMethod(typeof(ServerBot), nameof(FinalizerCheckFarmhandRequest))
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
        this.autoLoadAttempted = false;
        this.autoLoadDelay = -1;
        this.Players.Reset();

        this.Logger.Warn("Returned to title screen — will attempt auto-reload");
    }

    /// <summary>Per-tick update for bot management.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        // Handle auto-load from title screen (before world is ready)
        if (!this.farmSetupComplete && !this.autoLoadAttempted)
        {
            if (Game1.activeClickableMenu is TitleMenu && this.autoLoadDelay < 0)
            {
                // Start countdown — give title menu 3 seconds to fully initialize
                this.autoLoadDelay = 180;
                this.Logger.Debug("Title menu detected, waiting to auto-load...");
            }

            if (this.autoLoadDelay > 0)
            {
                this.autoLoadDelay--;
            }
            else if (this.autoLoadDelay == 0)
            {
                this.autoLoadDelay = -1;
                this.TryAutoLoadFarm();
            }
        }

        // Everything below requires world to be ready
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        // Keep bot farmer healthy
        if (e.IsMultipleOf(60)) // Every second
        {
            this.MaintainBotHealth();

            // Periodically try to resolve player names (they may not be available at connect time)
            this.Players.RefreshPlayerNames();
        }

        // Handle pause countdown
        this.UpdatePauseState();
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
            // Enable the server
            Game1.options.enableServer = true;
            Game1.options.serverPrivacy = ServerPrivacy.FriendsOnly;

            // The game should auto-start the server when enableServer is true and a co-op save is loaded.
            // If it hasn't started yet, try toggling the option to trigger it.
            if (Game1.server == null)
            {
                this.Logger.Info("Server not yet initialized, toggling enableServer...");
                Game1.options.enableServer = false;
                Game1.options.enableServer = true;
            }

            // If still null after a short delay, try StartServer via reflection
            if (Game1.server == null)
            {
                this.Logger.Info("Attempting StartServer() via reflection...");
                var multiplayer = typeof(Game1)
                    .GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic)?
                    .GetValue(null) as Multiplayer;

                multiplayer?.StartServer();
            }
            else
            {
                this.Logger.Info($"Game server already running, type: {Game1.server.GetType().Name}");
            }

            // Build cabins if needed
            this.EnsureCabins();

            this.Logger.Info("Multiplayer hosting enabled");

            // Log connection info
            if (Game1.server != null)
            {
                this.Logger.Info($"Server type: {Game1.server.GetType().Name}");
                this.Logger.Info("Players can connect via LAN or invite code");
            }
            else
            {
                this.Logger.Warn("Server object is still null — multiplayer may not be fully initialized");
            }
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
        this.autoLoadAttempted = true;

        try
        {
            // Find the save to load
            string? saveName = this.FindSaveToLoad();
            if (saveName == null)
            {
                this.Logger.Warn("No save file found. Please create a farm first or set SaveFileName in config.");
                return;
            }

            this.Logger.Info($"Auto-loading save: {saveName}");

            // Use the game's save loading mechanism
            // SaveGame.Load triggers the full load pipeline
            Game1.activeClickableMenu = null;
            SaveGame.Load(saveName);

            this.Logger.Info($"Save load initiated for: {saveName}");
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to auto-load farm: {ex.Message}");
        }
    }

    /// <summary>Find a save file to load based on config or most recent.</summary>
    /// <returns>The save folder name, or null if none found.</returns>
    private string? FindSaveToLoad()
    {
        // If a specific save is configured, use that
        if (!string.IsNullOrWhiteSpace(this.Config.SaveFileName))
        {
            string savePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StardewValley", "Saves", this.Config.SaveFileName
            );

            // Also check XDG config path (Linux)
            string linuxSavePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "StardewValley", "Saves", this.Config.SaveFileName
            );

            if (Directory.Exists(savePath) || Directory.Exists(linuxSavePath))
            {
                this.Logger.Debug($"Found configured save: {this.Config.SaveFileName}");
                return this.Config.SaveFileName;
            }

            this.Logger.Warn($"Configured save '{this.Config.SaveFileName}' not found");
        }

        // Otherwise, find the most recent save
        string[] searchPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "StardewValley", "Saves")
        };

        DirectoryInfo? mostRecent = null;

        foreach (string searchPath in searchPaths)
        {
            if (!Directory.Exists(searchPath))
                continue;

            var saves = new DirectoryInfo(searchPath)
                .GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, "SaveGameInfo")))
                .OrderByDescending(d => d.LastWriteTimeUtc);

            var newest = saves.FirstOrDefault();
            if (newest != null && (mostRecent == null || newest.LastWriteTimeUtc > mostRecent.LastWriteTimeUtc))
                mostRecent = newest;
        }

        if (mostRecent != null)
        {
            this.Logger.Debug($"Found most recent save: {mostRecent.Name}");
            return mostRecent.Name;
        }

        return null;
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

    /// <summary>
    /// Prefix patch on checkFarmhandRequest to guard against null references.
    /// The original method's Check() local function accesses:
    ///   - Game1.netWorldState.Value.farmhandData[...]  (line 528)
    ///   - Game1.serverHost.Value.UniqueMultiplayerID   (line 543)
    /// Either can be null on a headless Linux server without Galaxy SDK.
    /// If critical objects are null, we reject the request gracefully instead of crashing.
    /// </summary>
    [HarmonyPrefix]
    private static bool BeforeCheckFarmhandRequest(
        GameServer __instance, ref string userId, string connectionId,
        NetFarmerRoot farmer, Action<OutgoingMessage> sendMessage, Action approve)
    {
        // Fix empty userId (LidgrenServer always passes "")
        if (string.IsNullOrEmpty(userId))
        {
            userId = "";
        }

        // Check for null farmer
        if (farmer?.Value == null)
        {
            System.Console.WriteLine("[SERVER] Rejecting farmhand request: farmer is null");
            return true; // Let original handle it (it has a null check)
        }

        // Guard against null netWorldState — the critical null that causes the crash
        if (Game1.netWorldState?.Value == null)
        {
            System.Console.WriteLine("[SERVER] Rejecting farmhand request: netWorldState not ready yet");
            // Send available farmhands instead (which shows the cabin selection screen)
            // This is what the game does when isGameAvailable() returns false
            try
            {
                typeof(GameServer)
                    .GetMethod("sendAvailableFarmhands", BindingFlags.NonPublic | BindingFlags.Instance)?
                    .Invoke(__instance, new object[] { userId, connectionId, sendMessage });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[SERVER] sendAvailableFarmhands fallback failed: {ex.Message}");
            }
            return false; // Skip original
        }

        // Guard against null serverHost
        if (Game1.serverHost?.Value == null)
        {
            System.Console.WriteLine("[SERVER] Initializing serverHost before farmhand check...");
            if (Game1.serverHost == null)
            {
                Game1.serverHost = new NetFarmerRoot();
            }
            Game1.serverHost.Value = Game1.player;
        }

        return true; // Let original run — all null guards passed
    }

    /// <summary>
    /// Finalizer as safety net — catches any remaining NullReferenceException
    /// and suppresses it so the server doesn't crash.
    /// </summary>
    [HarmonyFinalizer]
    private static Exception? FinalizerCheckFarmhandRequest(Exception? __exception)
    {
        if (__exception != null)
        {
            System.Console.WriteLine($"[SERVER] checkFarmhandRequest error caught: {__exception.GetType().Name}: {__exception.Message}");
            return null; // Suppress — don't crash the server
        }
        return null;
    }
}
