namespace StardewDedicatedServer;

/// <summary>
/// Configuration model for the dedicated server mod.
/// SMAPI auto-serializes this to/from config.json in the mod folder.
/// AMP maps its UI settings to these fields via stardewservermetaconfig.json.
/// </summary>
public sealed class ModConfig
{
    /*********
    ** Farm Settings
    *********/

    /// <summary>The farm name used when auto-creating a new save.</summary>
    public string FarmName { get; set; } = "Dedicated Farm";

    /// <summary>
    /// Farm type for auto-creation.
    /// Values: "standard", "riverland", "forest", "hilltop", "wilderness", "fourCorners", "beach"
    /// </summary>
    public string FarmType { get; set; } = "standard";

    /// <summary>The farmer's name for the server bot character.</summary>
    public string FarmerName { get; set; } = "ServerBot";

    /// <summary>Pet preference when the game asks. Values: "cat", "dog"</summary>
    public string PetPreference { get; set; } = "cat";

    /// <summary>Cave choice when Demetrius asks. Values: "bats", "mushrooms"</summary>
    public string CaveChoice { get; set; } = "mushrooms";

    /*********
    ** Server Settings
    *********/

    /// <summary>Maximum number of farmhand players (determines cabin count). Default game limit is 7 (+ 1 host = 8 total). Can be expanded beyond 8 with mod support.</summary>
    public int MaxPlayers { get; set; } = 12;

    /// <summary>Style of cabins to auto-build. Values: "wood", "stone", "plank"</summary>
    public string CabinStyle { get; set; } = "wood";

    /// <summary>Message displayed to players when they connect.</summary>
    public string ServerMessage { get; set; } = "Welcome to the Stardew Valley dedicated server!";

    /// <summary>Whether to pause the game when no players are connected.</summary>
    public bool PauseWhenEmpty { get; set; } = false;

    /// <summary>Delay in seconds before pausing after last player disconnects.</summary>
    public int PauseDelay { get; set; } = 30;

    /*********
    ** Automation Settings
    *********/

    /// <summary>Whether the bot auto-sleeps when all real players are in bed.</summary>
    public bool AutoSleep { get; set; } = true;

    /// <summary>Whether the bot auto-handles festival attendance.</summary>
    public bool AutoFestival { get; set; } = true;

    /// <summary>Whether to auto-dismiss menus/dialogs for the bot host.</summary>
    public bool AutoDismissMenus { get; set; } = true;

    /*********
    ** Gameplay Settings
    *********/

    /// <summary>Profit margin multiplier. 1.0 = normal, 0.75 = 75%, 0.5 = 50%, 0.25 = 25%</summary>
    public float ProfitMargin { get; set; } = 1.0f;

    /// <summary>Whether monsters spawn on the farm at night.</summary>
    public bool MonstersOnFarm { get; set; } = false;

    /// <summary>Starting cabin count when creating a new farm. Range: 0-3</summary>
    public int StartingCabins { get; set; } = 3;

    /*********
    ** Advanced Settings
    *********/

    /// <summary>Whether to auto-create a new farm if no save file exists.</summary>
    public bool AutoCreateFarm { get; set; } = true;

    /// <summary>Save file name to load. If empty, loads the most recent or auto-creates.</summary>
    public string SaveFileName { get; set; } = "";

    /// <summary>Whether to enable verbose server logging (debug output).</summary>
    public bool VerboseLogging { get; set; } = false;

    /// <summary>
    /// Whether to enable headless mode (Phase 2).
    /// Skips the entire rendering pipeline to dramatically reduce CPU usage.
    /// Disable this if you need to debug rendering issues or use VNC to view the server.
    /// </summary>
    public bool HeadlessMode { get; set; } = true;

    /// <summary>
    /// Whether to enable no-Xvfb mode (Phase 3).
    /// Patches GameRunner.Draw to prevent all GPU calls, allowing the server
    /// to run with SDL_VIDEODRIVER=dummy and no virtual display.
    /// Requires HeadlessMode to also be true.
    /// </summary>
    public bool NoXvfbMode { get; set; } = true;
}
