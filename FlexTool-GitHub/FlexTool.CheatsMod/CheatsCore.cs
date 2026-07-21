using System;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FlexTool.CheatsMod;

[StaticConstructorOnStartup]
public static class CheatsModInit
{
    static CheatsModInit()
    {
        try
        {
            var harmony = new Harmony("flextool.cheatsmod");
            harmony.PatchAll();
            TryPatchFishing(harmony);
            Log.Message("[FlexTool Cheats] Initialized - OPEN button added under the speed controls.");
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] Failed to initialize: " + ex);
        }
    }

    /// <summary>Fish population patch (Odyssey only) - applied via reflection so it is safe without the DLC.</summary>
    private static void TryPatchFishing(Harmony harmony)
    {
        try
        {
            var tracker = AccessTools.TypeByName("RimWorld.WaterBodyTracker");
            if (tracker is null) return;

            var notify = AccessTools.Method(tracker, "Notify_Fished");
            if (notify is null) return;

            harmony.Patch(notify, prefix: new HarmonyMethod(typeof(CheatsModInit), nameof(SkipFishDepletion)));
            CheatsState.FishPatchApplied = true;
        }
        catch { }
    }

    public static bool SkipFishDepletion() => !CheatsState.FishNeverDeplete;
}

/// <summary>Shared toggle state for the persistent cheats.</summary>
public static class CheatsState
{
    // Main
    public static bool GodMode;
    public static bool AutoFillNeeds;
    public static bool AutoRemoveBadMemories;

    // Colony
    public static bool InstantBuild;
    public static bool UnlimitedPower;
    public static bool AutoGrowCrops;
    public static bool AutoCleanFilth;
    public static bool AutoRepairBuildings;
    public static bool InstantCraft;
    public static bool SkillNoDecay;
    public static bool NeverBreakup;
    public static bool BuildingLegendary;

    // World
    public static bool WeatherClear;
    public static bool AlwaysDay;
    public static bool AlwaysNight;
    public static bool FastCaravans;
    public static bool NoBadEvents;
    public static bool FishNeverDeplete;
    public static bool FishPatchApplied;
    public static bool ForceWeather;
    public static WeatherDef ForcedWeather;

    public static bool AnyUpkeepActive =>
        GodMode || AutoFillNeeds || AutoRemoveBadMemories
        || InstantBuild || UnlimitedPower || AutoGrowCrops || AutoCleanFilth
        || AutoRepairBuildings || InstantCraft
        || WeatherClear || ForceWeather;

    public static bool AnyActive =>
        AnyUpkeepActive || SkillNoDecay || NeverBreakup || BuildingLegendary
        || AlwaysDay || AlwaysNight || FastCaravans || NoBadEvents || FishNeverDeplete;
}

/// <summary>
/// Draws the OPEN button under the speed controls.
/// Clicking it opens (or closes) the tabbed cheats window.
/// </summary>
[HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
public static class CheatsButtonPatch
{
    public static void Postfix(Rect timerRect)
    {
        try
        {
            // Matches the FlexTool Speed mod button strip (34x22, 3px gap) and
            // sits directly below its 4x/5x/10x buttons so nothing overlaps.
            const float btnW = 34f;
            const float btnH = 22f;
            const float gap = 3f;
            var btn = new Rect(timerRect.x - btnW - 6f, timerRect.y + 3f * (btnH + gap), btnW, btnH);

            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            var prev = GUI.color;

            Widgets.DrawWindowBackground(btn);
            if (CheatsWindow.IsOpen)
                Widgets.DrawHighlightSelected(btn);
            if (CheatsState.AnyActive)
                Widgets.DrawHighlight(btn);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.4f, 1f, 0.55f); // green = OPEN
            Widgets.Label(btn, "OPEN");

            GUI.color = prev;
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;

            if (Mouse.IsOver(btn)) Widgets.DrawHighlight(btn);

            if (Widgets.ButtonInvisible(btn))
            {
                CheatsWindow.Toggle();
                SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] Error drawing OPEN button: " + ex);
        }
    }
}

/// <summary>Blocks death for player colonists while God Mode is enabled.</summary>
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class GodModeKillPatch
{
    public static bool Prefix(Pawn __instance)
    {
        try
        {
            if (!CheatsState.GodMode) return true;
            if (__instance?.Faction != Faction.OfPlayerSilentFail || !__instance.IsColonist) return true;

            CheatActions.HealPawn(__instance);
            return false;
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>Absorbs all incoming damage on player colonists while God Mode is enabled.</summary>
[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PreApplyDamage))]
public static class GodModeDamagePatch
{
    public static bool Prefix(Pawn ___pawn, ref bool absorbed)
    {
        try
        {
            if (!CheatsState.GodMode) return true;
            if (___pawn?.Faction != Faction.OfPlayerSilentFail || !___pawn.IsColonist) return true;

            absorbed = true;
            return false;
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>Everything built or crafted comes out Legendary while enabled.</summary>
[HarmonyPatch(typeof(CompQuality), nameof(CompQuality.SetQuality))]
public static class LegendaryQualityPatch
{
    public static void Prefix(ref QualityCategory q)
    {
        try
        {
            if (CheatsState.BuildingLegendary)
                q = QualityCategory.Legendary;
        }
        catch { }
    }
}

/// <summary>Skills never decay while enabled (skips the saturation XP loss interval).</summary>
[HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Interval))]
public static class SkillNoDecayPatch
{
    public static bool Prefix() => !CheatsState.SkillNoDecay;
}

/// <summary>Blocks all lover breakups and divorces while enabled.</summary>
[HarmonyPatch(typeof(InteractionWorker_Breakup), "Interacted")]
public static class NeverBreakupPatch
{
    public static bool Prefix() => !CheatsState.NeverBreakup;
}

/// <summary>Blocks raids and other negative incidents while enabled (manually triggered events still work).</summary>
[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
public static class NoBadEventsPatch
{
    public static bool Prefix(IncidentWorker __instance, IncidentParms parms, ref bool __result)
    {
        try
        {
            if (!CheatsState.NoBadEvents) return true;
            if (parms != null && parms.forced) return true; // user-triggered events pass through

            var def = __instance?.def;
            if (def is null) return true;

            bool bad = def.category == IncidentCategoryDefOf.ThreatBig
                    || def.category == IncidentCategoryDefOf.ThreatSmall
                    || def.letterDef == LetterDefOf.ThreatBig
                    || def.letterDef == LetterDefOf.ThreatSmall
                    || def.letterDef == LetterDefOf.NegativeEvent;
            if (!bad) return true;

            __result = false;
            return false;
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>Forces permanent day or permanent night lighting while enabled.</summary>
[HarmonyPatch(typeof(GenCelestial), nameof(GenCelestial.CelestialSunGlow), typeof(Map), typeof(int))]
public static class AlwaysDayNightPatch
{
    public static void Postfix(ref float __result)
    {
        try
        {
            if (CheatsState.AlwaysDay) __result = 1f;
            else if (CheatsState.AlwaysNight) __result = 0f;
        }
        catch { }
    }
}

/// <summary>Caravans move nearly instantly across the world map while enabled.</summary>
[HarmonyPatch(typeof(CaravanTicksPerMoveUtility), nameof(CaravanTicksPerMoveUtility.GetTicksPerMove),
    typeof(Caravan), typeof(StringBuilder))]
public static class FastCaravansPatch
{
    public static void Postfix(ref int __result)
    {
        try
        {
            if (CheatsState.FastCaravans)
                __result = 1;
        }
        catch { }
    }
}

/// <summary>
/// Periodic upkeep for the continuous toggles: healing, needs, memories,
/// construction, power, crops, filth, repairs, crafting, and weather locking.
/// </summary>
[HarmonyPatch(typeof(Root_Play), "Update")]
public static class CheatsUpkeepPatch
{
    private static int _frame;

    public static void Postfix()
    {
        try
        {
            if (!CheatsState.AnyUpkeepActive) return;
            if (Current.ProgramState != ProgramState.Playing) return;
            if (++_frame < 30) return; // ~0.5s at 60fps
            _frame = 0;

            var map = Find.CurrentMap;
            if (map is null) return;

            if (CheatsState.InstantBuild)
                try { CheatActions.CompleteAllConstruction(map); } catch { }

            if (CheatsState.UnlimitedPower)
                try { CheatActions.FillAllBatteries(map); } catch { }

            if (CheatsState.AutoGrowCrops)
                try { CheatActions.GrowAllCrops(); } catch { }

            if (CheatsState.AutoCleanFilth)
                try { CheatActions.CleanAllFilth(); } catch { }

            if (CheatsState.AutoRepairBuildings)
                try { CheatActions.RepairAllBuildings(); } catch { }

            if (CheatsState.InstantCraft)
                try { CheatActions.InstantCompleteCrafting(map); } catch { }

            var clear = WeatherDefOf.Clear;
            if (CheatsState.WeatherClear && clear != null && map.weatherManager.curWeather != clear)
                try { map.weatherManager.TransitionTo(clear); } catch { }
            else if (CheatsState.ForceWeather && CheatsState.ForcedWeather != null
                && map.weatherManager.curWeather != CheatsState.ForcedWeather)
                try { map.weatherManager.TransitionTo(CheatsState.ForcedWeather); } catch { }

            if (CheatsState.GodMode || CheatsState.AutoFillNeeds || CheatsState.AutoRemoveBadMemories)
            {
                var colonists = map.mapPawns.FreeColonists;
                for (int i = colonists.Count - 1; i >= 0; i--)
                {
                    var p = colonists[i];
                    try
                    {
                        if (CheatsState.GodMode)
                            CheatActions.HealPawn(p);

                        if (CheatsState.AutoFillNeeds)
                            CheatActions.FillNeeds(p);

                        if (CheatsState.AutoRemoveBadMemories)
                            CheatActions.RemoveBadMoodEffects(p);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}

/// <summary>
/// While Unlimited Power is enabled, every player device counts as powered -
/// no generators, conduits, or batteries required.
/// </summary>
[HarmonyPatch(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn), MethodType.Getter)]
public static class UnlimitedPowerPatch
{
    private sealed class CompCache
    {
        public readonly CompFlickable Flickable;
        public readonly CompBreakdownable Breakdown;

        public CompCache(ThingWithComps t)
        {
            Flickable = t.GetComp<CompFlickable>();
            Breakdown = t.GetComp<CompBreakdownable>();
        }
    }

    private static readonly ConditionalWeakTable<ThingWithComps, CompCache> Cache =
        new ConditionalWeakTable<ThingWithComps, CompCache>();

    private static readonly ConditionalWeakTable<ThingWithComps, CompCache>.CreateValueCallback CreateCache =
        t => new CompCache(t);

    public static void Postfix(CompPowerTrader __instance, ref bool __result)
    {
        try
        {
            if (__result || !CheatsState.UnlimitedPower) return;

            var parent = __instance?.parent;
            var faction = parent?.Faction;
            if (faction is null || !faction.IsPlayer) return;

            var comps = Cache.GetValue(parent, CreateCache);
            if (comps.Breakdown != null && comps.Breakdown.BrokenDown) return;
            if (comps.Flickable != null && !comps.Flickable.SwitchIsOn) return;

            __result = true;
        }
        catch
        {
        }
    }
}
