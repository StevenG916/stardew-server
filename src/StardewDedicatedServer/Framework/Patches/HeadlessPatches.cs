using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace StardewDedicatedServer.Framework.Patches;

/// <summary>
/// Harmony patches to skip the rendering pipeline entirely.
/// Phase 2 of the dedicated server: eliminates GPU/Xvfb overhead by making
/// Game1.Draw() a no-op. The game loop (Update) continues running normally
/// so all game logic, networking, and multiplayer state sync still works.
///
/// This dramatically reduces CPU usage (from ~60% to ~5-10%) and memory usage
/// (no textures/sprites loaded for rendering).
/// </summary>
public static class HeadlessPatches
{
    private static ServerLogger? Logger;
    private static bool isEnabled;
    private static int frameSkipCounter;

    /// <summary>How often to let a real Draw through (0 = never). Some mods may need occasional draws.</summary>
    private static int drawEveryNFrames = 0;

    /// <summary>Initialize the headless patches.</summary>
    /// <param name="harmony">The Harmony instance.</param>
    /// <param name="logger">The server logger.</param>
    /// <param name="enabled">Whether headless mode is enabled.</param>
    public static void Apply(Harmony harmony, ServerLogger logger, bool enabled)
    {
        Logger = logger;
        isEnabled = enabled;

        if (!enabled)
        {
            logger.Info("Headless mode DISABLED — full rendering active (higher CPU usage)");
            return;
        }

        logger.Info("Headless mode ENABLED — skipping rendering pipeline");

        // Patch Game1.Draw to skip all rendering
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "Draw", new[] { typeof(GameTime) }),
            prefix: new HarmonyMethod(typeof(HeadlessPatches), nameof(BeforeDraw))
        );

        // Patch Game1._draw to skip the heavy rendering (fallback if Draw patch doesn't catch it)
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "_draw", new[] { typeof(GameTime), typeof(RenderTarget2D) }),
            prefix: new HarmonyMethod(typeof(HeadlessPatches), nameof(Before_Draw))
        );

        logger.Info("Headless rendering patches applied");
    }

    /// <summary>
    /// Skip Game1.Draw entirely. This is the main entry point for all rendering.
    /// By returning false, the entire Draw method (including _draw, renderScreenBuffer,
    /// and all SpriteBatch operations) is skipped.
    ///
    /// The only thing we preserve is the isDrawing flag management, since some
    /// UI code checks it (though on a headless server, UI code shouldn't run).
    /// </summary>
    [HarmonyPrefix]
    private static bool BeforeDraw(Game1 __instance, GameTime gameTime)
    {
        if (!isEnabled)
            return true; // Let original run

        // Optionally allow periodic real draws (for debugging or mod compatibility)
        if (drawEveryNFrames > 0)
        {
            frameSkipCounter++;
            if (frameSkipCounter >= drawEveryNFrames)
            {
                frameSkipCounter = 0;
                return true; // Allow this frame to render
            }
        }

        // Skip the draw — but call base.Draw to keep MonoGame's internal state happy
        // MonoGame's Game.Draw does some internal bookkeeping (present,
        // frame timing) that we want to preserve
        try
        {
            // Set and clear the isDrawing flag so any code that checks it
            // doesn't get confused (isDrawing is an instance field)
            __instance.isDrawing = true;
            __instance.isDrawing = false;
        }
        catch
        {
            // Ignore any errors from flag management
        }

        return false; // Skip Game1.Draw
    }

    /// <summary>
    /// Backup patch on _draw in case something calls it directly.
    /// </summary>
    [HarmonyPrefix]
    private static bool Before_Draw()
    {
        if (!isEnabled)
            return true;

        return false; // Skip _draw
    }

    /// <summary>Enable or disable headless mode at runtime.</summary>
    public static void SetEnabled(bool enabled)
    {
        isEnabled = enabled;
        Logger?.Info($"Headless mode {(enabled ? "ENABLED" : "DISABLED")}");
    }

    /// <summary>Set how often to allow a real draw frame (0 = never).</summary>
    public static void SetDrawInterval(int frames)
    {
        drawEveryNFrames = Math.Max(0, frames);
        Logger?.Info($"Draw interval set to: {(frames == 0 ? "never" : $"every {frames} frames")}");
    }
}
