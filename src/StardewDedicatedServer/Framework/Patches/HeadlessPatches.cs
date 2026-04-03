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
/// CRITICAL: chatBox.update() is normally called ONLY from the Draw path
/// (inside DrawOverlays). We relocate it to a postfix on Update to prevent
/// an unbounded memory leak from chat messages never expiring.
///
/// This dramatically reduces CPU usage (from ~60% to ~5-10%).
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

        // Patch 1: Skip Game1.Draw entirely (the main rendering entry point)
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "Draw", new[] { typeof(GameTime) }),
            prefix: new HarmonyMethod(typeof(HeadlessPatches), nameof(BeforeDraw))
        );

        // Patch 2: Skip Game1._draw as a fallback
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "_draw", new[] { typeof(GameTime), typeof(RenderTarget2D) }),
            prefix: new HarmonyMethod(typeof(HeadlessPatches), nameof(Before_Draw))
        );

        // Patch 3: Relocate chatBox.update() to the Update loop
        // CRITICAL: Without this, chat messages never expire = memory leak!
        // chatBox.update() is normally called only from DrawOverlays in the Draw path.
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), "Update", new[] { typeof(GameTime) }),
            postfix: new HarmonyMethod(typeof(HeadlessPatches), nameof(AfterUpdate))
        );

        logger.Info("Headless rendering patches applied (chatBox.update relocated to Update loop)");
    }

    /// <summary>
    /// Skip Game1.Draw entirely. This is the main entry point for all rendering.
    /// By returning false, the entire Draw method (including _draw, renderScreenBuffer,
    /// and all SpriteBatch operations) is skipped.
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

    /// <summary>
    /// Postfix on Game1.Update — runs chatBox.update() which is normally
    /// only called from the Draw path (DrawOverlays). Without this,
    /// chat messages never expire and leak memory indefinitely.
    ///
    /// This is safe because chatBox.update() only:
    /// - Decrements message display timers
    /// - Updates message alpha (visual, irrelevant headless)
    /// - Processes keyboard input (irrelevant headless)
    /// </summary>
    [HarmonyPostfix]
    private static void AfterUpdate(Game1 __instance, GameTime gameTime)
    {
        if (!isEnabled)
            return;

        try
        {
            Game1.chatBox?.update(Game1.currentGameTime);
        }
        catch
        {
            // Ignore — chatBox may not be initialized yet
        }
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
