using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace StardewDedicatedServer.Framework.Patches;

/// <summary>
/// Patches the game's multiplayer player limit to support more than 8 players.
/// The default limit is 8 (1 host + 7 farmhands). This patch increases it
/// to the configured MaxPlayers + 1 (for the host bot).
///
/// Note: Players beyond 8 need additional cabins built on the farm.
/// The game supports arbitrary cabin counts; only the player limit is hardcoded.
/// </summary>
public static class PlayerLimitPatch
{
    private static ServerLogger? Logger;
    private static int targetLimit;

    /// <summary>Apply the player limit patch.</summary>
    /// <param name="harmony">The Harmony instance.</param>
    /// <param name="logger">The server logger.</param>
    /// <param name="maxFarmhands">Maximum number of farmhand players (not counting host).</param>
    public static void Apply(Harmony harmony, ServerLogger logger, int maxFarmhands)
    {
        Logger = logger;
        // Total slots = farmhands + 1 host bot
        targetLimit = maxFarmhands + 1;

        if (targetLimit <= 8)
        {
            logger.Info($"Player limit: {targetLimit} (default, no patch needed)");
            return;
        }

        logger.Info($"Expanding player limit from 8 to {targetLimit} ({maxFarmhands} farmhands + 1 host)");

        // Patch the Multiplayer constructor to set our higher limit
        harmony.Patch(
            original: AccessTools.Constructor(typeof(Multiplayer)),
            postfix: new HarmonyMethod(typeof(PlayerLimitPatch), nameof(AfterMultiplayerConstructor))
        );

        // Also patch it via SaveLoaded in case Multiplayer is already constructed
        SetPlayerLimit();
    }

    /// <summary>After the Multiplayer constructor runs (which sets playerLimit=8), override it.</summary>
    [HarmonyPostfix]
    private static void AfterMultiplayerConstructor(Multiplayer __instance)
    {
        __instance.playerLimit = targetLimit;
        Logger?.Debug($"Multiplayer.playerLimit set to {targetLimit} (post-constructor)");
    }

    /// <summary>Set the player limit on the existing Multiplayer instance.</summary>
    public static void SetPlayerLimit()
    {
        try
        {
            var multiplayer = typeof(Game1)
                .GetField("multiplayer", BindingFlags.Static | BindingFlags.NonPublic)?
                .GetValue(null) as Multiplayer;

            if (multiplayer != null)
            {
                multiplayer.playerLimit = targetLimit;
                Logger?.Debug($"Multiplayer.playerLimit updated to {targetLimit}");
            }
        }
        catch
        {
            // Multiplayer may not be initialized yet — the constructor postfix will catch it
        }
    }
}
