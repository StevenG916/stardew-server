using System;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Handles auto-creation of a new farm save when no existing save is found.
/// Uses the game's internal APIs to create a proper co-op farm with cabins.
/// </summary>
public sealed class SaveCreator
{
    private readonly ServerLogger Logger;
    private readonly ModConfig Config;

    private static readonly Random Rng = new();

    public SaveCreator(ServerLogger logger, ModConfig config)
    {
        this.Logger = logger;
        this.Config = config;
    }

    /// <summary>
    /// Create a new farm save using the game's character creation pipeline.
    /// Sets up the farmer with random appearance and config-driven farm settings,
    /// then triggers the game's new-game flow.
    /// </summary>
    /// <returns>True if creation was initiated successfully.</returns>
    public bool TryCreateFarm()
    {
        try
        {
            this.Logger.Info("No existing save found — creating new farm...");

            // Set the farm type before character creation
            Game1.whichFarm = FarmTypeFromString(this.Config.FarmType);

            // Set multiplayer mode to server
            Game1.multiplayerMode = 2;

            // Starting cabins (0-3 supported by the game's new-game flow)
            Game1.startingCabins = Math.Clamp(this.Config.StartingCabins, 0, 3);
            Game1.cabinsSeparate = true; // Place cabins separately on the farm

            // Profit margin
            Game1.player.difficultyModifier = this.Config.ProfitMargin;

            // Create a fresh farmer via the game's reset
            Game1.resetPlayer();

            // Configure the farmer
            var farmer = Game1.player;
            farmer.Name = this.Config.FarmerName;
            farmer.farmName.Value = this.Config.FarmName;
            farmer.favoriteThing.Value = "Hosting";

            // Randomize appearance so the bot doesn't look generic
            this.RandomizeAppearance(farmer);

            // Set gender randomly (Gender is a net field in 1.6+)
            farmer.Gender = Rng.Next(2) == 0 ? Gender.Male : Gender.Female;

            // Pet preference (whichPetType is the 1.6+ field)
            farmer.whichPetType = this.Config.PetPreference?.ToLowerInvariant() == "dog" ? "Dog" : "Cat";

            this.Logger.Info($"Farm: '{this.Config.FarmName}', Type: {this.Config.FarmType}, Cabins: {Game1.startingCabins}");
            this.Logger.Info($"Farmer: '{this.Config.FarmerName}', Gender: {(farmer.IsMale ? "Male" : "Female")}");

            // Trigger the new game creation flow.
            // This is the same path the game uses when you click "Create" on the character creation screen.
            // It calls Game1.loadForNewGame() internally and sets up the world.
            try
            {
                // Try using CharacterCustomization's static/internal creation method
                // The game's flow is: CharacterCustomization menu -> user clicks OK -> createdNewCharacter()
                // We can bypass the menu entirely by calling loadForNewGame directly.
                Game1.game1.loadForNewGame();

                this.Logger.Info("Game1.loadForNewGame() called — new farm world created");

                // After loadForNewGame, we need to trigger the save to actually persist it.
                // The game will save at the end of the first day.
                // We need to set multiplayerMode again since loadForNewGame may reset it.
                Game1.multiplayerMode = 2;
            }
            catch (Exception ex)
            {
                this.Logger.Error($"loadForNewGame failed: {ex.Message}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to create farm: {ex.Message}");
            return false;
        }
    }

    /// <summary>Randomize the farmer's visual appearance.</summary>
    private void RandomizeAppearance(Farmer farmer)
    {
        farmer.hair.Value = Rng.Next(73); // 73 hair styles in vanilla
        farmer.skin.Value = Rng.Next(24); // 24 skin tones
        farmer.shirt.Value = Rng.Next(112).ToString(); // shirt options (string ID in 1.6+)
        farmer.pants.Value = Rng.Next(4).ToString(); // pants style (string ID in 1.6+)

        // Random hair color
        farmer.hairstyleColor.Value = new Microsoft.Xna.Framework.Color(
            Rng.Next(256), Rng.Next(256), Rng.Next(256)
        );

        // Random eye color
        farmer.newEyeColor.Value = new Microsoft.Xna.Framework.Color(
            Rng.Next(256), Rng.Next(256), Rng.Next(256)
        );

        // Random pants color
        farmer.pantsColor.Value = new Microsoft.Xna.Framework.Color(
            Rng.Next(256), Rng.Next(256), Rng.Next(256)
        );

        // Accessories (0 = none, 1-19 = various)
        farmer.accessory.Value = Rng.Next(-1, 19);
    }

    /// <summary>Map farm type config string to the game's integer index.</summary>
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
            _ => 0 // Default to standard
        };
    }
}
