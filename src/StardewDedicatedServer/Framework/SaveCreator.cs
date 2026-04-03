using System;
using Microsoft.Xna.Framework;
using StardewDedicatedServer.Framework.Patches;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Handles auto-creation of a new farm save when no existing save is found.
/// Uses a state machine to spread creation across multiple game frames,
/// matching the same flow as TitleMenu.createdNewCharacter(skipIntro: true).
/// </summary>
public sealed class SaveCreator
{
    private readonly ServerLogger Logger;
    private readonly ModConfig Config;

    private static readonly Random Rng = new();

    private CreateState state = CreateState.Idle;
    private int frameDelay;

    private enum CreateState
    {
        Idle,
        WaitingForTitleMenu,
        ConfiguringFarm,
        LoadingNewGame,
        WaitingForLoad,
        StartingFirstDay,
        Done
    }

    public SaveCreator(ServerLogger logger, ModConfig config)
    {
        this.Logger = logger;
        this.Config = config;
    }

    /// <summary>Register event handlers for frame-based creation.</summary>
    public void RegisterEvents(IModEvents events)
    {
        events.GameLoop.UpdateTicked += this.OnUpdateTicked;
    }

    /// <summary>Begin the farm creation process.</summary>
    public bool TryCreateFarm()
    {
        if (this.state != CreateState.Idle)
            return false;

        this.Logger.Info("No existing save found — auto-creating new farm...");
        this.state = CreateState.WaitingForTitleMenu;
        this.frameDelay = 0;
        return true;
    }

    /// <summary>Whether farm creation is currently in progress.</summary>
    public bool IsCreating => this.state != CreateState.Idle && this.state != CreateState.Done;

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        switch (this.state)
        {
            case CreateState.Idle:
            case CreateState.Done:
                return;

            case CreateState.WaitingForTitleMenu:
                // Wait for the title menu to be fully ready
                if (Game1.activeClickableMenu is TitleMenu)
                {
                    this.frameDelay++;
                    if (this.frameDelay >= 120) // Wait 2 seconds
                    {
                        this.state = CreateState.ConfiguringFarm;
                        this.frameDelay = 0;
                    }
                }
                break;

            case CreateState.ConfiguringFarm:
                this.ConfigureFarm();
                this.state = CreateState.LoadingNewGame;
                this.frameDelay = 0;
                break;

            case CreateState.LoadingNewGame:
                // Enable rendering for the creation/transition process
                // Both HeadlessPatches AND NoXvfbPatches must be disabled
                // so GameRunner.Draw runs and fade callbacks fire.
                HeadlessPatches.SetEnabled(false);
                NoXvfbPatches.SetEnabled(false);
                this.Logger.Debug("Rendering fully enabled for farm creation");

                try
                {
                    // Follow TitleMenu.createdNewCharacter(skipIntro: true) exactly
                    Game1.game1.loadForNewGame();
                    Game1.saveOnNewDay = true;
                    Game1.player.eventsSeen.Add("60367"); // Skip intro event

                    // Place player in farmhouse
                    Game1.player.currentLocation = Utility.getHomeOfFarmer(Game1.player);
                    Game1.player.Position = new Vector2(9f, 9f) * 64f;
                    Game1.player.isInBed.Value = true;

                    this.Logger.Info("loadForNewGame complete — waiting for game to stabilize");
                    this.state = CreateState.WaitingForLoad;
                    this.frameDelay = 0;
                }
                catch (Exception ex)
                {
                    this.Logger.Error($"loadForNewGame failed: {ex.Message}\n{ex.StackTrace}");
                    HeadlessPatches.SetEnabled(true);
                    NoXvfbPatches.SetEnabled(true);
                    this.state = CreateState.Done;
                }
                break;

            case CreateState.WaitingForLoad:
                // Give the game loop time to process the new world
                this.frameDelay++;
                if (this.frameDelay >= 60) // Wait 1 second
                {
                    this.state = CreateState.StartingFirstDay;
                    this.frameDelay = 0;
                }
                break;

            case CreateState.StartingFirstDay:
                try
                {
                    // Trigger the first day transition — this creates the initial save
                    Game1.NewDay(0f);
                    Game1.exitActiveMenu();
                    Game1.setGameMode(3); // Playing mode
                    Game1.multiplayerMode = 2;

                    this.Logger.Info("NewDay(0f) called — first save will be written");
                    // Headless mode will be restored by DayManager.OnDayStarted
                }
                catch (Exception ex)
                {
                    this.Logger.Error($"NewDay failed: {ex.Message}\n{ex.StackTrace}");
                    HeadlessPatches.SetEnabled(true);
                    NoXvfbPatches.SetEnabled(true);
                }
                this.state = CreateState.Done;
                break;
        }
    }

    /// <summary>Configure farm settings before creation.</summary>
    private void ConfigureFarm()
    {
        Game1.whichFarm = FarmTypeFromString(this.Config.FarmType);
        Game1.multiplayerMode = 2;
        Game1.startingCabins = Math.Clamp(this.Config.StartingCabins, 0, 3);
        Game1.cabinsSeparate = true;

        // Reset and configure farmer
        Game1.resetPlayer();
        var farmer = Game1.player;
        farmer.Name = this.Config.FarmerName;
        farmer.farmName.Value = this.Config.FarmName;
        farmer.favoriteThing.Value = "Hosting";
        farmer.Gender = Rng.Next(2) == 0 ? Gender.Male : Gender.Female;
        farmer.whichPetType = this.Config.PetPreference?.ToLowerInvariant() == "dog" ? "Dog" : "Cat";
        this.RandomizeAppearance(farmer);

        this.Logger.Info($"Farm: '{this.Config.FarmName}', Type: {this.Config.FarmType}, Cabins: {Game1.startingCabins}");
        this.Logger.Info($"Farmer: '{this.Config.FarmerName}', Gender: {(farmer.IsMale ? "Male" : "Female")}");
    }

    private void RandomizeAppearance(Farmer farmer)
    {
        farmer.hair.Value = Rng.Next(73);
        farmer.skin.Value = Rng.Next(24);
        farmer.shirt.Value = Rng.Next(112).ToString();
        farmer.pants.Value = Rng.Next(4).ToString();
        farmer.hairstyleColor.Value = new Color(Rng.Next(256), Rng.Next(256), Rng.Next(256));
        farmer.newEyeColor.Value = new Color(Rng.Next(256), Rng.Next(256), Rng.Next(256));
        farmer.pantsColor.Value = new Color(Rng.Next(256), Rng.Next(256), Rng.Next(256));
        farmer.accessory.Value = Rng.Next(-1, 19);
    }

    private static int FarmTypeFromString(string farmType)
    {
        return farmType?.ToLowerInvariant() switch
        {
            "standard" => 0,
            "riverland" => 1,
            "forest" => 2,
            "hilltop" => 3,
            "wilderness" => 4,
            "fourcorners" or "four_corners" => 5,
            "beach" => 6,
            "meadowlands" => 7,
            _ => 0
        };
    }
}
