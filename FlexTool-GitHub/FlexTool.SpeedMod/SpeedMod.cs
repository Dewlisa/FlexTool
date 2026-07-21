using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FlexTool.SpeedMod;

[StaticConstructorOnStartup]
public static class SpeedModInit
{
    static SpeedModInit()
    {
        try
        {
            var harmony = new Harmony("flextool.speedmod");
            harmony.PatchAll();
            Log.Message("[FlexTool Speed] Initialized — 4x / 5x / 10x speed buttons added to time controls.");
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Speed] Failed to initialize: " + ex);
        }
    }
}

/// <summary>Holds the current extra speed multiplier (1 = off).</summary>
public static class SpeedModState
{
    public static int Multiplier = 1;

    /// <summary>The vanilla speed the game was at before boosting, restored on toggle-off.</summary>
    public static TimeSpeed PreBoostSpeed = TimeSpeed.Normal;

    public static void Engage(int multiplier)
    {
        var tm = Find.TickManager;
        if (Multiplier == 1 && tm.CurTimeSpeed != TimeSpeed.Paused)
            PreBoostSpeed = tm.CurTimeSpeed;
        Multiplier = multiplier;
        tm.CurTimeSpeed = TimeSpeed.Superfast;
    }

    public static void Disengage()
    {
        Multiplier = 1;
        Find.TickManager.CurTimeSpeed = PreBoostSpeed;
    }
}

/// <summary>
/// Applies the extra multiplier to the final tick rate. Skips the boost while
/// combat/forced-slowdown is active so events stay playable.
/// </summary>
[HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
public static class TickRateMultiplierPatch
{
    public static void Postfix(TickManager __instance, ref float __result)
    {
        if (SpeedModState.Multiplier <= 1 || __result <= 0f) return;

        // Respect forced slowdown (raids, threats) — the game returns 1x
        // there and boosting through it makes combat unmanageable.
        if (__instance.slower != null && __instance.slower.ForcedNormalSpeed) return;

        __result *= SpeedModState.Multiplier;
    }
}

/// <summary>
/// Resets the boost whenever a map/save loads so a boosted state never
/// carries over into a freshly loaded game.
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
public static class ResetOnLoadPatch
{
    public static void Postfix()
    {
        SpeedModState.Multiplier = 1;
        SpeedModState.PreBoostSpeed = TimeSpeed.Normal;
    }
}

/// <summary>
/// Draws a clean 4x / 5x / 10x button strip aligned with the vanilla time
/// controls. One boost active at a time; clicking the active button turns it
/// off and restores the previous speed. Buttons dim while combat forces
/// normal speed, and the boost auto-cancels if the player pauses.
/// </summary>
[HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
public static class TimeControlsPatch
{
    private static readonly int[] Boosts = { 4, 5, 10 };

    public static void Postfix(Rect timerRect)
    {
        try
        {
            var tm = Find.TickManager;

            // Auto-cancel the boost when the player pauses — resuming should
            // never surprise them with a hidden 10x multiplier.
            if (SpeedModState.Multiplier > 1 && tm.CurTimeSpeed == TimeSpeed.Paused)
            {
                SpeedModState.Multiplier = 1;
                SpeedModState.PreBoostSpeed = TimeSpeed.Normal;
            }

            const float btnW = 34f;
            const float btnH = 22f;
            const float gap = 3f;

            bool slowedByCombat = tm.slower != null && tm.slower.ForcedNormalSpeed;

            // Single vertical strip to the left of the time controls —
            // compact, aligned, and consistent with the vanilla UI.
            float x = timerRect.x - btnW - 6f;
            float y = timerRect.y;

            for (int i = 0; i < Boosts.Length; i++)
            {
                int boost = Boosts[i];
                var rect = new Rect(x, y + i * (btnH + gap), btnW, btnH);
                bool active = SpeedModState.Multiplier == boost;

                if (DrawToggle(rect, boost + "x", active, slowedByCombat))
                {
                    if (active) SpeedModState.Disengage();
                    else SpeedModState.Engage(boost);
                    SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, null);
                }

                TooltipHandler.TipRegion(rect, active
                    ? $"Click to turn off the {boost}x boost and restore the previous speed."
                    : $"Run the game at {boost}x speed. Automatically pauses the boost during raids and turns off when you pause.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[FlexTool Speed] Error drawing buttons: {ex}");
        }
    }

    private static bool DrawToggle(Rect rect, string label, bool active, bool dimmed)
    {
        var prevColor = GUI.color;
        var prevFont = Text.Font;
        var prevAnchor = Text.Anchor;

        // Subtle panel background so the strip reads as one control group
        Widgets.DrawWindowBackground(rect);
        if (active)
        {
            Widgets.DrawHighlightSelected(rect);
            GUI.color = new Color(0.4f, 1f, 0.55f);   // green = engaged
        }
        else if (dimmed)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.45f); // combat slowdown active
        }

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, label);

        Text.Anchor = prevAnchor;
        Text.Font = prevFont;
        GUI.color = prevColor;

        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
        return Widgets.ButtonInvisible(rect);
    }
}
