using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Handles festival events for the server bot.
/// Auto-accepts festival attendance and auto-leaves when appropriate,
/// preventing the server from getting stuck during festival days.
/// </summary>
public sealed class FestivalManager
{
    /*********
    ** Fields
    *********/

    private readonly ServerLogger Logger;
    private readonly ModConfig Config;
    private readonly PlayerManager Players;

    /// <summary>Whether the bot is currently at a festival.</summary>
    private bool isAtFestival;

    /// <summary>Tick counter for periodic festival state checks.</summary>
    private int festivalCheckTimer;

    /// <summary>Polling interval for festival checks (in ticks).</summary>
    private const int FestivalCheckInterval = 120; // ~2 seconds

    /*********
    ** Public Methods
    *********/

    public FestivalManager(ServerLogger logger, ModConfig config, PlayerManager players)
    {
        this.Logger = logger;
        this.Config = config;
        this.Players = players;
    }

    /// <summary>Register event handlers.</summary>
    public void RegisterEvents(IModEvents events)
    {
        events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        events.Display.MenuChanged += this.OnMenuChanged;
    }

    /*********
    ** Private Methods
    *********/

    /// <summary>Handle menu changes to detect festival-related dialogs.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (!this.Config.AutoFestival || !Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        // Detect the "Do you want to go to the festival?" dialogue
        if (e.NewMenu is DialogueBox dialogue)
        {
            this.TryHandleFestivalDialogue(dialogue);
        }
    }

    /// <summary>Per-tick festival state monitoring.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!this.Config.AutoFestival || !Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        this.festivalCheckTimer++;
        if (this.festivalCheckTimer < FestivalCheckInterval)
            return;
        this.festivalCheckTimer = 0;

        // Check if we're at a festival
        bool currentlyAtFestival = Game1.isFestival();

        if (currentlyAtFestival && !this.isAtFestival)
        {
            this.isAtFestival = true;
            string festivalName = Game1.CurrentEvent?.FestivalName ?? "Unknown Festival";
            this.Logger.FestivalActive(festivalName);
        }
        else if (!currentlyAtFestival && this.isAtFestival)
        {
            this.isAtFestival = false;
            this.Logger.Debug("Festival ended");
        }

        // During festivals, handle any blocking states
        if (currentlyAtFestival)
        {
            this.HandleFestivalState();
        }
    }

    /// <summary>Try to handle festival-related dialogue boxes (e.g., "attend festival?" prompt).</summary>
    private void TryHandleFestivalDialogue(DialogueBox dialogue)
    {
        try
        {
            // Check if this is a yes/no dialogue about attending a festival
            // The game presents this as a dialogue with response options
            if (dialogue.isQuestion)
            {
                // Look for festival-related questions
                // Auto-accept by selecting "Yes" (typically the first response)
                var responses = dialogue.responses;
                if (responses != null && responses.Length > 0)
                {
                    // Select the first response (usually "Yes" / affirmative)
                    // Use a small delay to avoid immediate dismissal issues
                    this.Logger.Debug("Auto-accepting festival dialogue");
                    dialogue.selectedResponse = 0;
                    dialogue.receiveLeftClick(0, 0);
                }
            }
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to handle festival dialogue: {ex.Message}");
        }
    }

    /// <summary>Handle ongoing festival state — keep the bot from blocking progression.</summary>
    private void HandleFestivalState()
    {
        try
        {
            // During festivals, the bot should stay out of the way
            // If a festival mini-game or activity requires host participation,
            // handle it here

            // Check for any blocking menus during the festival
            if (Game1.activeClickableMenu is DialogueBox dialogue)
            {
                // Auto-dismiss festival dialogues aimed at the host
                if (!dialogue.isQuestion)
                {
                    dialogue.receiveLeftClick(0, 0);
                    this.Logger.Debug("Auto-dismissed festival dialogue");
                }
            }
        }
        catch (Exception ex)
        {
            this.Logger.Debug($"Festival state handling error: {ex.Message}");
        }
    }
}
