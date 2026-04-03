using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace StardewDedicatedServer.Framework.Patches;

/// <summary>
/// Phase 3 patches to eliminate the Xvfb dependency entirely.
///
/// Strategy:
/// 1. Set SDL_VIDEODRIVER=dummy via environment before game starts (in launch script)
/// 2. Patch GameRunner.Draw to skip all GraphicsDevice calls
/// 3. The Phase 2 Game1.Draw patch already skips individual instance rendering
/// 4. Together these mean zero GPU work after initialization
///
/// Note: We still need Xvfb (or SDL dummy) during INITIALIZATION because
/// MonoGame's GraphicsDeviceManager creates a GraphicsDevice in Initialize().
/// With SDL_VIDEODRIVER=dummy, SDL creates a fake window with no real display.
/// After initialization, these patches prevent any further GPU calls.
/// </summary>
public static class NoXvfbPatches
{
    private static ServerLogger? Logger;
    private static bool isEnabled;

    /// <summary>Apply Phase 3 patches.</summary>
    public static void Apply(Harmony harmony, ServerLogger logger, bool enabled)
    {
        Logger = logger;
        isEnabled = enabled;

        if (!enabled)
        {
            logger.Info("No-Xvfb mode DISABLED — using virtual display");
            return;
        }

        logger.Info("No-Xvfb mode ENABLED — patching GameRunner rendering");

        // Patch GameRunner.Draw to skip all GPU operations
        // GameRunner.Draw accesses GraphicsDevice.Viewport directly
        // which will crash without a real GPU context
        // GameRunner is in the global namespace (no namespace prefix)
        var gameRunnerType = typeof(Game1).Assembly.GetType("GameRunner")
            ?? typeof(Game1).Assembly.GetType("StardewValley.GameRunner")
            ?? Type.GetType("GameRunner, Stardew Valley");

        // Also try searching all types
        if (gameRunnerType == null)
        {
            foreach (var type in typeof(Game1).Assembly.GetTypes())
            {
                if (type.Name == "GameRunner")
                {
                    gameRunnerType = type;
                    break;
                }
            }
        }

        if (gameRunnerType != null)
        {
            var drawMethod = AccessTools.Method(gameRunnerType, "Draw", new[] { typeof(GameTime) });
            if (drawMethod != null)
            {
                harmony.Patch(
                    original: drawMethod,
                    prefix: new HarmonyMethod(typeof(NoXvfbPatches), nameof(BeforeGameRunnerDraw))
                );
                logger.Info("Patched GameRunner.Draw");
            }
            else
            {
                logger.Warn("Could not find GameRunner.Draw method");
            }
        }
        else
        {
            logger.Warn("Could not find GameRunner type");
        }

        logger.Info("No-Xvfb patches applied");
    }

    /// <summary>Enable or disable No-Xvfb mode at runtime.</summary>
    public static void SetEnabled(bool enabled)
    {
        isEnabled = enabled;
        Logger?.Info($"No-Xvfb mode {(enabled ? "ENABLED" : "DISABLED")}");
    }

    /// <summary>
    /// Skip GameRunner.Draw entirely.
    /// GameRunner.Draw iterates game instances and calls GraphicsDevice.Viewport
    /// which requires a real GPU context. With SDL_VIDEODRIVER=dummy there is
    /// no real GPU, so we skip it all.
    ///
    /// The _windowSizeChanged handling is irrelevant for headless operation.
    /// </summary>
    [HarmonyPrefix]
    private static bool BeforeGameRunnerDraw(GameTime gameTime)
    {
        if (!isEnabled)
            return true;

        // Don't call base.Draw either — it would try to Present() which needs GPU
        return false;
    }
}
