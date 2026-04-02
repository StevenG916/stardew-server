using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewDedicatedServer.Framework;

/// <summary>
/// Tracks player connections/disconnections and manages the player list.
/// Produces structured log output for AMP to parse.
/// </summary>
public sealed class PlayerManager
{
    /*********
    ** Fields
    *********/

    /// <summary>The server logger.</summary>
    private readonly ServerLogger Logger;

    /// <summary>The mod configuration.</summary>
    private readonly ModConfig Config;

    /// <summary>Connected players keyed by their unique multiplayer ID.</summary>
    private readonly Dictionary<long, PlayerInfo> ConnectedPlayers = new();

    /// <summary>Callback invoked when the player count changes (current count passed).</summary>
    public event Action<int>? PlayerCountChanged;

    /*********
    ** Accessors
    *********/

    /// <summary>Number of currently connected farmhand players (excludes host).</summary>
    public int Count => this.ConnectedPlayers.Count;

    /// <summary>Whether any farmhand players are connected.</summary>
    public bool AnyConnected => this.ConnectedPlayers.Count > 0;

    /*********
    ** Public Methods
    *********/

    /// <summary>Construct a new player manager.</summary>
    /// <param name="logger">The server logger.</param>
    /// <param name="config">The mod configuration.</param>
    public PlayerManager(ServerLogger logger, ModConfig config)
    {
        this.Logger = logger;
        this.Config = config;
    }

    /// <summary>Register event handlers with SMAPI.</summary>
    /// <param name="events">The SMAPI event helper.</param>
    public void RegisterEvents(IModEvents events)
    {
        events.Multiplayer.PeerConnected += this.OnPeerConnected;
        events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
    }

    /// <summary>Get a snapshot of all connected players.</summary>
    public IReadOnlyList<PlayerInfo> GetPlayers()
    {
        return this.ConnectedPlayers.Values.ToList().AsReadOnly();
    }

    /// <summary>Try to find a connected player by name (case-insensitive partial match).</summary>
    /// <param name="nameQuery">The name to search for.</param>
    /// <param name="player">The matched player info, if found.</param>
    /// <returns>True if exactly one match was found.</returns>
    public bool TryFindPlayer(string nameQuery, out PlayerInfo? player)
    {
        var matches = this.ConnectedPlayers.Values
            .Where(p => p.Name.Contains(nameQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 1)
        {
            player = matches[0];
            return true;
        }

        player = null;
        return false;
    }

    /// <summary>Kick a player by their multiplayer ID.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    public void KickPlayer(long playerId)
    {
        if (!Context.IsWorldReady || Game1.server == null)
        {
            this.Logger.Warn("Cannot kick player: server not ready");
            return;
        }

        try
        {
            // Game1.server.kick() removes the player from the server
            Game1.server.kick(playerId);
            this.Logger.Info($"Kicked player ID {playerId}");
        }
        catch (Exception ex)
        {
            this.Logger.Error($"Failed to kick player {playerId}: {ex.Message}");
        }
    }

    /// <summary>Send a chat message to all connected players.</summary>
    /// <param name="message">The message to broadcast.</param>
    public void BroadcastMessage(string message)
    {
        if (!Context.IsWorldReady)
            return;

        // Use the game's built-in chat system
        Game1.chatBox?.addInfoMessage(message);
        this.Logger.Debug($"Broadcast: {message}");
    }

    /// <summary>Clear tracked players (e.g., when returning to title).</summary>
    public void Reset()
    {
        this.ConnectedPlayers.Clear();
    }

    /*********
    ** Private Methods
    *********/

    /// <summary>Handle a peer connecting.</summary>
    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        // Skip the host (that's us, the bot)
        if (e.Peer.IsHost)
            return;

        long playerId = e.Peer.PlayerID;
        string playerName = this.ResolvePlayerName(playerId);

        var info = new PlayerInfo
        {
            Id = playerId,
            Name = playerName,
            JoinedAt = DateTime.UtcNow,
            HasSmapi = e.Peer.HasSmapi,
            Platform = e.Peer.Platform?.ToString() ?? "Unknown"
        };

        this.ConnectedPlayers[playerId] = info;

        // Log for AMP parsing
        this.Logger.PlayerJoined(playerName, playerId);
        this.Logger.PlayerCount(this.Count, this.Config.MaxPlayers);

        // Send welcome message
        if (!string.IsNullOrWhiteSpace(this.Config.ServerMessage))
        {
            Game1.chatBox?.addInfoMessage(this.Config.ServerMessage);
        }

        this.PlayerCountChanged?.Invoke(this.Count);
    }

    /// <summary>Handle a peer disconnecting.</summary>
    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        if (e.Peer.IsHost)
            return;

        long playerId = e.Peer.PlayerID;
        string playerName = this.ConnectedPlayers.TryGetValue(playerId, out var info)
            ? info.Name
            : this.ResolvePlayerName(playerId);

        this.ConnectedPlayers.Remove(playerId);

        // Log for AMP parsing
        this.Logger.PlayerLeft(playerName, playerId);
        this.Logger.PlayerCount(this.Count, this.Config.MaxPlayers);

        this.PlayerCountChanged?.Invoke(this.Count);
    }

    /// <summary>Resolve a player's display name from their multiplayer ID.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <returns>The player's name, or "Unknown" if not found.</returns>
    private string ResolvePlayerName(long playerId)
    {
        // Try to find the farmer by their multiplayer ID
        if (Context.IsWorldReady)
        {
            foreach (var farmer in Game1.getAllFarmhands())
            {
                if (farmer.UniqueMultiplayerID == playerId)
                    return farmer.Name;
            }
        }

        return $"Player_{playerId}";
    }
}

/// <summary>Snapshot of a connected player's info.</summary>
public sealed class PlayerInfo
{
    /// <summary>The player's unique multiplayer ID.</summary>
    public long Id { get; init; }

    /// <summary>The player's display name.</summary>
    public string Name { get; set; } = "Unknown";

    /// <summary>When the player connected (UTC).</summary>
    public DateTime JoinedAt { get; init; }

    /// <summary>Whether the player has SMAPI installed.</summary>
    public bool HasSmapi { get; init; }

    /// <summary>The player's platform (Windows, Linux, macOS).</summary>
    public string Platform { get; init; } = "Unknown";
}
