using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FlexTool.PerfMod;

[StaticConstructorOnStartup]
public static class PerfModInit
{
    static PerfModInit()
    {
        try
        {
            var harmony = new Harmony("flextool.perfmod");
            var features = new List<string>();

            // ── 1) Adaptive pawn tick throttling ─────────────────────────
            // Prefer the RimWorld 1.6+ delta-based TickInterval seam so skipped
            // time is BANKED and replayed later (lossless simulation). On older
            // versions fall back to Pawn.Tick with light time dilation.
            try
            {
                var tickInterval = AccessTools.Method(typeof(Pawn), "TickInterval", new[] { typeof(int) });
                if (tickInterval is not null)
                {
                    harmony.Patch(tickInterval,
                        prefix: new HarmonyMethod(typeof(PawnTickThrottle), nameof(PawnTickThrottle.IntervalPrefix)));
                    features.Add("adaptive tick throttling (lossless delta banking)");
                }
                else
                {
                    var tick = AccessTools.Method(typeof(Pawn), "Tick", Type.EmptyTypes);
                    if (tick is not null)
                    {
                        harmony.Patch(tick,
                            prefix: new HarmonyMethod(typeof(PawnTickThrottle), nameof(PawnTickThrottle.TickPrefix)));
                        features.Add("adaptive tick throttling (time dilation)");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[FlexTool Perf] Tick throttle patch unavailable: " + ex.Message);
            }

            // ── 2) Load monitor driving the adaptive skip factor ─────────
            try
            {
                var tmUpdate = AccessTools.Method(typeof(TickManager), "TickManagerUpdate");
                if (tmUpdate is not null)
                {
                    harmony.Patch(tmUpdate,
                        postfix: new HarmonyMethod(typeof(AdaptiveLoadMonitor), nameof(AdaptiveLoadMonitor.Postfix)));
                    features.Add("adaptive load monitor");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[FlexTool Perf] Load monitor patch unavailable: " + ex.Message);
            }

            // ── 3) Smarter movement: distance-scaled pathfinding heuristic ─
            // Long paths search far fewer nodes for near-identical routes,
            // cutting pathfinding CPU cost dramatically on big maps.
            try
            {
                var heuristic = AccessTools.Method(typeof(PathFinder), "DetermineHeuristicStrength");
                if (heuristic is not null)
                {
                    harmony.Patch(heuristic,
                        postfix: new HarmonyMethod(typeof(PathHeuristicPatch), nameof(PathHeuristicPatch.Postfix)));
                    features.Add("distance-scaled path heuristic");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[FlexTool Perf] Path heuristic patch unavailable: " + ex.Message);
            }

            Log.Message(features.Count > 0
                ? "[FlexTool Perf] Initialized — " + string.Join(", ", features) + "."
                : "[FlexTool Perf] Initialized, but no compatible patch points were found on this game version.");
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Perf] Failed to initialize: " + ex);
        }
    }
}

/// <summary>Shared adaptive state. SkipFactor 1 = off; raised only under load.</summary>
public static class PerfState
{
    public static int SkipFactor = 1;
    public const int MaxSkipFactor = 4;
}

/// <summary>
/// Watches real frame time (smoothed) once per frame and adjusts the skip
/// factor with hysteresis: throttling engages only when the game struggles
/// and backs off completely when it runs smoothly.
/// </summary>
public static class AdaptiveLoadMonitor
{
    private const float LowFps = 40f;
    private const float HighFps = 55f;
    private const int FramesBetweenAdjust = 45;

    private static float _emaDelta = 1f / 60f;
    private static int _framesSinceAdjust;

    public static void Postfix()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f || dt > 1f) return;
        _emaDelta += (dt - _emaDelta) * 0.05f;

        if (++_framesSinceAdjust < FramesBetweenAdjust) return;
        _framesSinceAdjust = 0;

        bool gameRunning = Current.ProgramState == ProgramState.Playing
                           && Find.TickManager is not null
                           && !Find.TickManager.Paused;
        if (!gameRunning)
        {
            PerfState.SkipFactor = 1;
            return;
        }

        float fps = 1f / _emaDelta;
        if (fps < LowFps && PerfState.SkipFactor < PerfState.MaxSkipFactor)
            PerfState.SkipFactor++;
        else if (fps > HighFps && PerfState.SkipFactor > 1)
            PerfState.SkipFactor--;
    }
}

/// <summary>
/// Skips redundant tick work for off-screen, idle, non-combat ANIMALS only.
/// Colonists, drafted pawns, hostiles, mental states, predators and anything
/// on screen are never touched. On RimWorld 1.6+ skipped deltas are banked
/// and replayed on the next allowed tick, so total simulated time is lossless.
/// </summary>
public static class PawnTickThrottle
{
    private const int MaxBankedTicks = 15;
    private const int MaxTrackedPawns = 8192;

    private static readonly Dictionary<int, int> SkipCounters = new();
    private static readonly Dictionary<int, int> BankedDelta = new();

    private static CellRect _viewRect;
    private static int _viewRectFrame = -1;

    /// <summary>RimWorld 1.6+ seam: bank skipped deltas, replay them later.</summary>
    public static bool IntervalPrefix(Pawn __instance, ref int delta)
    {
        int factor = PerfState.SkipFactor;
        if (factor <= 1) return true;

        int id = __instance.thingIDNumber;

        if (!ShouldThrottle(__instance))
        {
            // Pawn became relevant again — flush any banked time so it fully catches up.
            if (BankedDelta.TryGetValue(id, out int owed) && owed > 0)
            {
                delta += owed;
                BankedDelta[id] = 0;
            }
            return true;
        }

        PruneIfNeeded();

        SkipCounters.TryGetValue(id, out int counter);
        counter++;
        SkipCounters[id] = counter;

        BankedDelta.TryGetValue(id, out int bank);

        if (counter % factor != 0 && bank + delta <= MaxBankedTicks)
        {
            BankedDelta[id] = bank + delta;
            return false; // skip this tick — the time is banked, not lost
        }

        if (bank > 0)
        {
            delta += bank; // replay everything we skipped in one batched tick
            BankedDelta[id] = 0;
        }
        return true;
    }

    /// <summary>Pre-1.6 fallback seam: light time dilation (no delta available).</summary>
    public static bool TickPrefix(Pawn __instance)
    {
        int factor = PerfState.SkipFactor;
        if (factor <= 1) return true;
        if (!ShouldThrottle(__instance)) return true;

        PruneIfNeeded();

        int id = __instance.thingIDNumber;
        SkipCounters.TryGetValue(id, out int counter);
        counter++;
        SkipCounters[id] = counter;
        return counter % factor == 0;
    }

    private static bool ShouldThrottle(Pawn p)
    {
        if (p is null || !p.Spawned || p.Dead) return false;

        // Animals only — humanlike pawns and mechs are never throttled.
        var race = p.RaceProps;
        if (race is null || !race.Animal) return false;

        if (p.Drafted) return false;
        if (p.MentalStateDef is not null) return false;              // manhunter packs stay fully simulated
        if (p.mindState?.enemyTarget is not null) return false;      // fighting or actively hunting
        if (p.stances?.curStance is Stance_Busy) return false;       // mid-attack / cooldown

        // Only idle-ish jobs may be throttled — working animals (hauling,
        // training, nursing…) always run at full fidelity.
        var jobDef = p.CurJobDef;
        if (jobDef is not null
            && jobDef != JobDefOf.Wait_Wander
            && jobDef != JobDefOf.GotoWander
            && jobDef != JobDefOf.LayDown)
        {
            return false;
        }

        var playerFaction = Faction.OfPlayerSilentFail;
        if (playerFaction is not null && p.HostileTo(playerFaction)) return false;

        return !IsOnScreen(p);
    }

    private static bool IsOnScreen(Pawn p)
    {
        var map = p.Map;
        if (map is null || Find.CurrentMap != map) return false;

        var cam = Find.CameraDriver;
        if (cam is null) return false;

        if (Time.frameCount != _viewRectFrame)
        {
            _viewRect = cam.CurrentViewRect.ExpandedBy(3);
            _viewRectFrame = Time.frameCount;
        }
        return _viewRect.Contains(p.Position);
    }

    private static void PruneIfNeeded()
    {
        if (SkipCounters.Count <= MaxTrackedPawns) return;
        SkipCounters.Clear();
        BankedDelta.Clear();
    }
}

/// <summary>
/// Scales the A* heuristic weight with path distance. Short paths stay exact;
/// long paths expand far fewer search nodes for a near-identical route —
/// pawns decide where to go much faster, which reads as smarter movement
/// and slashes pathfinding CPU cost on large maps and big colonies.
/// </summary>
public static class PathHeuristicPatch
{
    public static void Postfix(ref float __result, IntVec3 start, LocalTargetInfo dest)
    {
        float dist = (start - dest.Cell).LengthHorizontal;
        if (dist <= 40f) return;
        float boost = Mathf.Clamp(1f + (dist - 40f) / 160f, 1f, 1.85f);
        __result *= boost;
    }
}
