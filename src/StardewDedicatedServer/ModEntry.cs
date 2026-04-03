using HarmonyLib;
using StardewDedicatedServer.Framework;
using StardewDedicatedServer.Framework.Patches;
using StardewModdingAPI;

namespace StardewDedicatedServer;

/// <summary>
/// Entry point for the Stardew Dedicated Server mod.
/// Coordinates all server subsystems: bot automation, player tracking,
/// console commands, day/festival management, and AMP-compatible logging.
/// </summary>
public sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/

    private ModConfig Config = null!;
    private ServerLogger Logger = null!;
    private PlayerManager Players = null!;
    private ConsoleCommands Commands = null!;
    private MenuManager Menus = null!;
    private DayManager Days = null!;
    private FestivalManager Festivals = null!;
    private ServerBot Bot = null!;

    /*********
    ** Public Methods
    *********/

    /// <summary>The mod entry point, called after the mod is first loaded.</summary>
    /// <param name="helper">Provides simplified APIs for writing mods.</param>
    public override void Entry(IModHelper helper)
    {
        // Load config
        this.Config = helper.ReadConfig<ModConfig>();

        // Initialize subsystems
        this.Logger = new ServerLogger(this.Monitor, this.Config.VerboseLogging);
        this.Players = new PlayerManager(this.Logger, this.Config);
        this.Menus = new MenuManager(this.Logger, this.Config);
        this.Days = new DayManager(this.Logger, this.Config, this.Players, helper);
        this.Festivals = new FestivalManager(this.Logger, this.Config, this.Players);
        this.Bot = new ServerBot(this.Logger, this.Config, this.Players);

        this.Commands = new ConsoleCommands(
            this.Logger,
            this.Players,
            this.Config,
            helper,
            this.RequestShutdown
        );

        // Register console commands
        this.Commands.Register();

        // Register SMAPI events for all subsystems
        this.Players.RegisterEvents(helper.Events);
        this.Menus.RegisterEvents(helper.Events);
        this.Days.RegisterEvents(helper.Events);
        this.Festivals.RegisterEvents(helper.Events);
        this.Bot.RegisterEvents(helper.Events);

        // Apply Harmony patches
        var harmony = new Harmony(this.ModManifest.UniqueID);
        this.Bot.ApplyPatches(harmony);

        // Phase 2: Apply headless rendering patches
        HeadlessPatches.Apply(harmony, this.Logger, this.Config.HeadlessMode);

        // Phase 2: Expand player limit if configured above default
        PlayerLimitPatch.Apply(harmony, this.Logger, this.Config.MaxPlayers);

        // Phase 3: Patch GameRunner.Draw to eliminate Xvfb dependency
        NoXvfbPatches.Apply(harmony, this.Logger, this.Config.HeadlessMode && this.Config.NoXvfbMode);

        this.Logger.Info($"Stardew Dedicated Server v{this.ModManifest.Version} loaded");
        this.Logger.Info("Type 'server_help' for available commands");
    }

    /*********
    ** Private Methods
    *********/

    /// <summary>Request a graceful server shutdown.</summary>
    private void RequestShutdown()
    {
        this.Logger.ServerStopping();

        // Give a brief moment for the message to be logged, then exit
        // The game's exit handler will clean up networking
        StardewValley.Game1.exitActiveMenu();
        StardewValley.Game1.quit = true;
    }
}
