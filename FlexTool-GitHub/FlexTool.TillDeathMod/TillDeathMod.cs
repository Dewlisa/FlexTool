using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FlexTool.TillDeathMod;

[StaticConstructorOnStartup]
public static class TillDeathModInit
{
    static TillDeathModInit()
    {
        try
        {
            var harmony = new Harmony("flextool.tilldeathmod");
            harmony.PatchAll();
            Log.Message("[FlexTool Till Death] Initialized - breakups and divorces are disabled.");
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Till Death] Failed to initialize: " + ex);
        }
    }
}

/// <summary>
/// Blocks the breakup/divorce social interaction from ever executing.
/// In vanilla both lover breakups and spouse divorces run through this worker.
/// </summary>
[HarmonyPatch(typeof(InteractionWorker_Breakup), "Interacted")]
public static class BlockBreakupInteractionPatch
{
    public static bool Prefix() => false;
}

/// <summary>
/// Removes the breakup interaction from random social interaction selection,
/// so pawns never even attempt it (no wasted interaction rolls).
/// </summary>
[HarmonyPatch(typeof(InteractionWorker_Breakup), nameof(InteractionWorker_Breakup.RandomSelectionWeight))]
public static class BlockBreakupSelectionPatch
{
    public static void Postfix(ref float __result)
    {
        __result = 0f;
    }
}
