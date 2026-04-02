using StardewModdingAPI;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Structured logger that produces AMP-parseable output.
/// All server-status messages use the [SERVER] prefix so AMP's regex patterns
/// can reliably detect player joins, leaves, chat, and server state transitions.
/// </summary>
public sealed class ServerLogger
{
    /*********
    ** Fields
    *********/

    /// <summary>The SMAPI monitor for writing to console/log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>Whether verbose logging is enabled.</summary>
    private bool Verbose;

    /*********
    ** Public Methods
    *********/

    /// <summary>Construct a new server logger.</summary>
    /// <param name="monitor">The SMAPI monitor.</param>
    /// <param name="verbose">Whether to enable verbose output.</param>
    public ServerLogger(IMonitor monitor, bool verbose = false)
    {
        this.Monitor = monitor;
        this.Verbose = verbose;
    }

    /// <summary>Update the verbose logging setting.</summary>
    public void SetVerbose(bool verbose)
    {
        this.Verbose = verbose;
    }

    // ---- AMP-parsed messages (these match regex patterns in the .kvp) ----

    /// <summary>Log that the server is ready and listening. Triggers AMP's AppReadyRegex.</summary>
    /// <param name="port">The port the server is listening on.</param>
    public void ServerReady(int port)
    {
        this.Monitor.Log($"[SERVER] Ready: Listening on port {port}", LogLevel.Info);
    }

    /// <summary>Log a player connection. Triggers AMP's UserJoinRegex.</summary>
    /// <param name="playerName">The player's display name.</param>
    /// <param name="playerId">The player's unique ID.</param>
    public void PlayerJoined(string playerName, long playerId)
    {
        this.Monitor.Log($"[SERVER] Player joined: {playerName} (ID: {playerId})", LogLevel.Info);
    }

    /// <summary>Log a player disconnection. Triggers AMP's UserLeaveRegex.</summary>
    /// <param name="playerName">The player's display name.</param>
    /// <param name="playerId">The player's unique ID.</param>
    public void PlayerLeft(string playerName, long playerId)
    {
        this.Monitor.Log($"[SERVER] Player left: {playerName} (ID: {playerId})", LogLevel.Info);
    }

    /// <summary>Log a chat message. Triggers AMP's UserChatRegex.</summary>
    /// <param name="playerName">The sender's display name.</param>
    /// <param name="message">The chat message content.</param>
    public void ChatMessage(string playerName, string message)
    {
        this.Monitor.Log($"[SERVER] Chat {playerName}: {message}", LogLevel.Info);
    }

    /// <summary>Log current player count.</summary>
    /// <param name="current">Current connected players.</param>
    /// <param name="max">Maximum players allowed.</param>
    public void PlayerCount(int current, int max)
    {
        this.Monitor.Log($"[SERVER] Players online: {current}/{max}", LogLevel.Info);
    }

    // ---- Server lifecycle messages ----

    /// <summary>Log server startup.</summary>
    public void ServerStarting()
    {
        this.Monitor.Log("[SERVER] Starting Stardew Dedicated Server...", LogLevel.Info);
    }

    /// <summary>Log that the server is loading a save file.</summary>
    /// <param name="saveName">The save file name.</param>
    public void LoadingSave(string saveName)
    {
        this.Monitor.Log($"[SERVER] Loading save: {saveName}", LogLevel.Info);
    }

    /// <summary>Log that a new farm is being created.</summary>
    /// <param name="farmName">The farm name.</param>
    /// <param name="farmType">The farm type.</param>
    public void CreatingFarm(string farmName, string farmType)
    {
        this.Monitor.Log($"[SERVER] Creating new farm: {farmName} ({farmType})", LogLevel.Info);
    }

    /// <summary>Log a save operation.</summary>
    public void Saving()
    {
        this.Monitor.Log("[SERVER] Saving...", LogLevel.Info);
    }

    /// <summary>Log save completion.</summary>
    public void SaveComplete()
    {
        this.Monitor.Log("[SERVER] Save complete", LogLevel.Info);
    }

    /// <summary>Log server shutdown.</summary>
    public void ServerStopping()
    {
        this.Monitor.Log("[SERVER] Shutting down...", LogLevel.Info);
    }

    // ---- Game state messages ----

    /// <summary>Log a new day starting.</summary>
    /// <param name="season">Current season.</param>
    /// <param name="day">Current day number.</param>
    /// <param name="year">Current year.</param>
    public void DayStarted(string season, int day, int year)
    {
        this.Monitor.Log($"[SERVER] Day started: {season} {day}, Year {year}", LogLevel.Info);
    }

    /// <summary>Log that the server is paused (no players).</summary>
    public void ServerPaused()
    {
        this.Monitor.Log("[SERVER] Paused: No players connected", LogLevel.Info);
    }

    /// <summary>Log that the server is resumed.</summary>
    public void ServerResumed()
    {
        this.Monitor.Log("[SERVER] Resumed: Player connected", LogLevel.Info);
    }

    /// <summary>Log a festival event.</summary>
    /// <param name="festivalName">The festival name.</param>
    public void FestivalActive(string festivalName)
    {
        this.Monitor.Log($"[SERVER] Festival active: {festivalName}", LogLevel.Info);
    }

    // ---- General logging ----

    /// <summary>Log an informational message.</summary>
    public void Info(string message)
    {
        this.Monitor.Log($"[SERVER] {message}", LogLevel.Info);
    }

    /// <summary>Log a warning.</summary>
    public void Warn(string message)
    {
        this.Monitor.Log($"[SERVER] {message}", LogLevel.Warn);
    }

    /// <summary>Log an error.</summary>
    public void Error(string message)
    {
        this.Monitor.Log($"[SERVER] {message}", LogLevel.Error);
    }

    /// <summary>Log a debug/verbose message (only shown when verbose mode is on).</summary>
    public void Debug(string message)
    {
        if (this.Verbose)
            this.Monitor.Log($"[SERVER] {message}", LogLevel.Debug);
    }
}
