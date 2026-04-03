using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Automatically handles menus and dialogs that appear for the host bot.
/// This prevents the server from getting stuck on menus that require player interaction.
/// Only dismisses menus for the HOST — farmhand menus are never touched.
/// </summary>
public sealed class MenuManager
{
    /*********
    ** Fields
    *********/

    private readonly ServerLogger Logger;
    private readonly ModConfig Config;

    /// <summary>Tick counter to add slight delays before dismissing menus (avoids race conditions).</summary>
    private int menuDismissCountdown = -1;

    /*********
    ** Public Methods
    *********/

    public MenuManager(ServerLogger logger, ModConfig config)
    {
        this.Logger = logger;
        this.Config = config;
    }

    /// <summary>Register event handlers.</summary>
    public void RegisterEvents(IModEvents events)
    {
        events.Display.MenuChanged += this.OnMenuChanged;
        events.GameLoop.UpdateTicked += this.OnUpdateTicked;
    }

    /*********
    ** Private Methods
    *********/

    /// <summary>Handle menu changes — detect menus that need auto-dismissal.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (!this.Config.AutoDismissMenus || !Context.IsWorldReady)
            return;

        // Only handle menus for the host
        if (!Context.IsMainPlayer)
            return;

        var menu = e.NewMenu;
        if (menu == null)
            return;

        string menuType = menu.GetType().Name;
        this.Logger.Debug($"Menu opened: {menuType}");

        // Schedule dismissal with a short delay to let the menu fully initialize
        switch (menu)
        {
            case ShippingMenu:
                // Don't auto-dismiss — let the game's endOfNightMenus stack
                // ShippingMenu has an internal save flow: intro → click OK → outro → save.
                // We skip the intro and force outro to trigger the save.
                this.Logger.Debug("ShippingMenu detected — will fast-forward");
                this.menuDismissCountdown = 30;
                break;

            case LevelUpMenu:
                this.Logger.Debug("Auto-dismissing LevelUpMenu (auto-choosing profession)");
                this.menuDismissCountdown = 10; // slightly longer for level-up
                break;

            case LetterViewerMenu:
                this.Logger.Debug("Auto-dismissing LetterViewerMenu");
                this.menuDismissCountdown = 3;
                break;

            case DialogueBox:
                this.Logger.Debug("Auto-dismissing DialogueBox");
                this.menuDismissCountdown = 5;
                break;

            case SaveGameMenu:
                // Don't dismiss save menus — let them complete
                this.Logger.Debug("Save in progress, waiting...");
                break;

            default:
                // Log unhandled menus in verbose mode for debugging
                this.Logger.Debug($"Unhandled menu type: {menuType}");
                break;
        }
    }

    /// <summary>Per-tick handler to dismiss menus after countdown.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (this.menuDismissCountdown < 0)
            return;

        this.menuDismissCountdown--;

        if (this.menuDismissCountdown > 0)
            return;

        // Countdown reached zero — dismiss the current menu
        this.menuDismissCountdown = -1;
        this.TryDismissCurrentMenu();
    }

    /// <summary>Attempt to dismiss the current active menu.</summary>
    private void TryDismissCurrentMenu()
    {
        var menu = Game1.activeClickableMenu;
        if (menu == null)
            return;

        try
        {
            switch (menu)
            {
                case ShippingMenu shippingMenu:
                    // ShippingMenu has an internal outro → save → exit flow.
                    // Skip ALL animations and force the save to happen immediately.
                    try
                    {
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

                        // Skip intro and outro animations
                        typeof(ShippingMenu).GetField("introTimer", flags)?.SetValue(shippingMenu, 0);
                        typeof(ShippingMenu).GetField("outro", flags)?.SetValue(shippingMenu, true);
                        typeof(ShippingMenu).GetField("outroFadeTimer", flags)?.SetValue(shippingMenu, 0);
                        typeof(ShippingMenu).GetField("outroPauseBeforeDateChange", flags)?.SetValue(shippingMenu, 0);
                        typeof(ShippingMenu).GetField("newDayPlaque", flags)?.SetValue(shippingMenu, true);
                        typeof(ShippingMenu).GetField("savedYet", flags)?.SetValue(shippingMenu, false);

                        // Force dayPlaqueY to target so the animation check passes
                        var centerY = shippingMenu.yPositionOnScreen + shippingMenu.height / 2;
                        typeof(ShippingMenu).GetField("dayPlaqueY", flags)?.SetValue(shippingMenu, centerY);

                        // Set finalOutroTimer to 0 so _hasFinished triggers after save
                        typeof(ShippingMenu).GetField("finalOutroTimer", flags)?.SetValue(shippingMenu, 0);

                        // Create the internal SaveGameMenu that ShippingMenu manages
                        var saveField = typeof(ShippingMenu).GetField("saveGameMenu", flags);
                        if (saveField != null && saveField.GetValue(shippingMenu) == null)
                        {
                            saveField.SetValue(shippingMenu, new SaveGameMenu());
                            this.Logger.Debug("Created internal SaveGameMenu for ShippingMenu");
                        }

                        this.Logger.Debug("Fast-forwarded ShippingMenu to save phase");
                    }
                    catch (Exception ex)
                    {
                        this.Logger.Error($"Failed to fast-forward ShippingMenu: {ex.Message}");
                        shippingMenu.exitThisMenu(playSound: false);
                    }
                    break;

                case LevelUpMenu levelUp:
                    // Auto-select the first profession option
                    this.HandleLevelUpMenu(levelUp);
                    break;

                case LetterViewerMenu:
                    menu.exitThisMenu(playSound: false);
                    this.Logger.Debug("Dismissed LetterViewerMenu");
                    break;

                case DialogueBox dialogue:
                    // Close the dialogue by receiving a left click
                    dialogue.receiveLeftClick(0, 0);
                    this.Logger.Debug("Dismissed DialogueBox");
                    break;

                default:
                    // Generic dismissal attempt
                    menu.exitThisMenu(playSound: false);
                    this.Logger.Debug($"Dismissed {menu.GetType().Name}");
                    break;
            }
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to dismiss menu {menu.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Handle level-up menu by auto-selecting the first profession.</summary>
    private void HandleLevelUpMenu(LevelUpMenu menu)
    {
        try
        {
            // The LevelUpMenu has a list of profession choices
            // We auto-select the first option (index 0)
            // This uses reflection-free approach: simulate clicking the left profession
            if (menu.isProfessionChooser)
            {
                // leftProfession is typically at left side of the menu
                // The exact click position depends on menu layout, but we can use
                // the menu's built-in method to select profession at index
                menu.receiveLeftClick(menu.xPositionOnScreen + 64, menu.yPositionOnScreen + menu.height - 64);
                this.Logger.Debug("Auto-selected first profession in LevelUpMenu");
            }
            else
            {
                // Standard level-up notification (not a profession choice)
                menu.exitThisMenu(playSound: false);
                this.Logger.Debug("Dismissed non-profession LevelUpMenu");
            }
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to handle LevelUpMenu: {ex.Message}");
            // Fallback: force close
            Game1.activeClickableMenu = null;
        }
    }
}
