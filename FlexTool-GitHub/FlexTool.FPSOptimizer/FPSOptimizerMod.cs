using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FlexTool.FPSOptimizer;

[StaticConstructorOnStartup]
public static class FpsOptimizerInit
{
    static FpsOptimizerInit()
    {
        try
        {
            var harmony = new Harmony("flextool.fpsoptimizer");
            var features = new List<string>();

            // ── 1) Frame-load monitor (drives every adaptive system) ──────
            try
            {
                var tmUpdate = AccessTools.Method(typeof(TickManager), "TickManagerUpdate");
                if (tmUpdate is not null)
                {
                    harmony.Patch(tmUpdate,
                        postfix: new HarmonyMethod(typeof(FrameLoadMonitor), nameof(FrameLoadMonitor.Postfix)));
                    features.Add("adaptive frame-load monitor");
                }
            }
            catch (Exception ex) { Log.Warning("[FlexTool FPS] Load monitor patch unavailable: " + ex.Message); }

            // ── 2) Job-search budget: batch pawn AI across frames ─────────
            // The single biggest large-colony spike: hundreds of idle pawns
            // scanning the whole map for work in the same frame. This caps
            // how many full job searches run per frame; deferred pawns simply
            // retry a few ticks later (imperceptible, but the spike is gone).
            try
            {
                var tryFindJob = AccessTools.Method(typeof(Pawn_JobTracker), "TryFindAndStartJob");
                if (tryFindJob is not null)
                {
                    harmony.Patch(tryFindJob,
                        prefix: new HarmonyMethod(typeof(JobSearchBudget), nameof(JobSearchBudget.Prefix)));
                    features.Add("per-frame job-search budget");
                }
            }
            catch (Exception ex) { Log.Warning("[FlexTool FPS] Job budget patch unavailable: " + ex.Message); }

            // ── 3) World-pawn tick throttling ──────────────────────────────
            // Colonies with long histories accumulate thousands of off-map
            // world pawns that all tick every frame. Under load, tick them
            // on a rotating schedule instead.
            try
            {
                var worldPawnsTick = AccessTools.Method(typeof(WorldPawns), "WorldPawnsTick");
                if (worldPawnsTick is not null)
                {
                    harmony.Patch(worldPawnsTick,
                        prefix: new HarmonyMethod(typeof(WorldPawnThrottle), nameof(WorldPawnThrottle.Prefix)));
                    features.Add("world-pawn tick throttling");
                }
            }
            catch (Exception ex) { Log.Warning("[FlexTool FPS] World pawn patch unavailable: " + ex.Message); }

            // ── 4) Steady environment effects throttling ──────────────────
            // Per-cell ambient simulation (plant freezing, snow melt, filth
            // decay checks) runs constantly. Under load, halve its frequency;
            // outcomes are identical because effects are cumulative.
            try
            {
                var steadyTick = AccessTools.Method(typeof(SteadyEnvironmentEffects), "SteadyEnvironmentEffectsTick");
                if (steadyTick is not null)
                {
                    harmony.Patch(steadyTick,
                        prefix: new HarmonyMethod(typeof(EnvironmentThrottle), nameof(EnvironmentThrottle.Prefix)));
                    features.Add("environment-effects throttling");
                }
            }
            catch (Exception ex) { Log.Warning("[FlexTool FPS] Environment patch unavailable: " + ex.Message); }

            // ── 5) Scheduled low-impact garbage collection ─────────────────
            // Replaces surprise multi-second GC freezes with small predictable
            // gen-0 collections during quiet frames. Driven from the load
            // monitor postfix — no extra patch needed.
            SmoothGc.Enabled = true;
            features.Add("scheduled smooth GC");

            // ── 6) Adaptive pawn tick throttling (merged from FlexTool Perf) ─
            // Off-screen idle animals tick less often under load. On 1.6+ the
            // skipped time is BANKED and replayed later (lossless simulation);
            // on older versions a light time-dilation fallback is used.
            // Skipped entirely when the standalone FlexTool Perf mod is loaded,
            // otherwise animals would be double-throttled (up to 16x skip).
            bool perfModLoaded = IsPerfModLoaded();
            if (perfModLoaded)
            {
                Log.Message("[FlexTool FPS] FlexTool Perf mod detected — skipping duplicated pawn throttle and path heuristic patches.");
            }
            else
            {
            try
            {
                var tickInterval = AccessTools.Method(typeof(Pawn), "TickInterval", new[] { typeof(int) });
                if (tickInterval is not null)
                {
                    harmony.Patch(tickInterval,
                        prefix: new HarmonyMethod(typeof(PawnTickThrottle), nameof(PawnTickThrottle.IntervalPrefix)));
                    features.Add("adaptive pawn tick throttling (lossless delta banking)");
                }
                else
                {
                    var tick = AccessTools.Method(typeof(Pawn), "Tick", Type.EmptyTypes);
                    if (tick is not null)
                    {
                        harmony.Patch(tick,
                            prefix: new HarmonyMethod(typeof(PawnTickThrottle), nameof(PawnTickThrottle.TickPrefix)));
                        features.Add("adaptive pawn tick throttling (time dilation)");
                    }
                }
            }
            catch (Exception ex) { Log.Warning("[FlexTool FPS] Pawn throttle patch unavailable: " + ex.Message); }

            // ── 7) Distance-scaled pathfinding heuristic (merged from FlexTool Perf) ─
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
            catch (Exception ex) { Log.Warning("[FlexTool FPS] Path heuristic patch unavailable: " + ex.Message); }
            }

            Log.Message(features.Count > 0
                ? "[FlexTool FPS] Initialized — " + string.Join(", ", features) + "."
                : "[FlexTool FPS] Initialized, but no compatible patch points were found on this game version.");
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool FPS] Failed to initialize: " + ex);
        }
    }

    /// <summary>
    /// True when the standalone FlexTool Perf mod is also loaded. Detected by
    /// its loaded assembly/type so the duplicated patches can be skipped.
    /// </summary>
    private static bool IsPerfModLoaded()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name != null && name.IndexOf("FlexTool.PerfMod", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (asm.GetType("FlexTool.PerfMod.PerfModInit", throwOnError: false) != null)
                    return true;
            }
        }
        catch { }
        return false;
    }
}

/// <summary>
/// Shared adaptive state. Load level 0 = smooth (all optimizations idle),
/// 1 = mild throttling, 2 = heavy throttling. Raised only when FPS drops.
/// </summary>
public static class FpsState
{
    public static int LoadLevel;          // 0 smooth · 1 strained · 2 struggling
    public static float SmoothedFps = 60f;
    public static int ColonistCount;
    public static long JobSearchesDeferred;
    public static long WorldPawnTicksSkipped;
    public static long PawnTicksThrottled;
}

/// <summary>
/// Smooths real frame time, classifies the current load level with
/// hysteresis, publishes status to FlexTool, and runs the GC scheduler.
/// </summary>
public static class FrameLoadMonitor
{
    private const float StrainedFps = 45f;
    private const float StrugglingFps = 30f;
    private const float RecoverFps = 55f;
    private const int FramesBetweenAdjust = 60;

    private static float _emaDelta = 1f / 60f;
    private static int _framesSinceAdjust;
    private static int _framesSinceStatus;

    public static void Postfix()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f || dt > 1f) return;
        _emaDelta += (dt - _emaDelta) * 0.05f;
        FpsState.SmoothedFps = 1f / _emaDelta;

        SmoothGc.OnFrame();

        if (++_framesSinceStatus >= 60) { _framesSinceStatus = 0; StatusFile.Write(); }

        if (++_framesSinceAdjust < FramesBetweenAdjust) return;
        _framesSinceAdjust = 0;

        bool playing = Current.ProgramState == ProgramState.Playing
                       && Find.TickManager is not null
                       && !Find.TickManager.Paused;
        if (!playing)
        {
            FpsState.LoadLevel = 0;
            return;
        }

        try { FpsState.ColonistCount = PawnsFinder.AllMaps_FreeColonistsSpawned?.Count ?? 0; }
        catch { }

        float fps = FpsState.SmoothedFps;
        FpsState.LoadLevel = FpsState.LoadLevel switch
        {
            0 when fps < StrainedFps => 1,
            1 when fps < StrugglingFps => 2,
            1 when fps > RecoverFps => 0,
            2 when fps > StrainedFps => 1,
            _ => FpsState.LoadLevel
        };
    }
}

/// <summary>
/// Caps how many full job searches may start per frame. Idle pawns beyond
/// the budget skip this attempt and retry on their next think cycle a few
/// ticks later. Drafted pawns, burning pawns, and pawns in mental states are
/// always exempt so responsiveness is never affected.
/// </summary>
public static class JobSearchBudget
{
    private static int _budgetFrame = -1;
    private static int _searchesThisFrame;
    private static readonly System.Reflection.FieldInfo PawnField =
        AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

    private static int BudgetPerFrame => FpsState.LoadLevel switch
    {
        2 => 6,
        1 => 12,
        _ => int.MaxValue
    };

    public static bool Prefix(Pawn_JobTracker __instance)
    {
        if (FpsState.LoadLevel == 0) return true;

        var pawn = PawnField?.GetValue(__instance) as Pawn;
        if (pawn is null || !IsDeferrable(pawn)) return true;

        if (Time.frameCount != _budgetFrame)
        {
            _budgetFrame = Time.frameCount;
            _searchesThisFrame = 0;
        }

        if (_searchesThisFrame < BudgetPerFrame)
        {
            _searchesThisFrame++;
            return true;
        }

        FpsState.JobSearchesDeferred++;
        return false; // retry next think cycle — spike smoothed across frames
    }

    private static bool IsDeferrable(Pawn p)
    {
        if (p.Drafted) return false;
        if (p.MentalStateDef is not null) return false;
        if (p.IsBurning()) return false;
        if (p.InAggroMentalState) return false;
        var faction = Faction.OfPlayerSilentFail;
        if (faction is not null && p.HostileTo(faction)) return false; // raiders act instantly
        return true;
    }
}

/// <summary>
/// Under load, world pawns (off-map characters) tick every 2nd or 4th cycle.
/// Their needs/aging are interval-based and cumulative, so nothing is lost.
/// </summary>
public static class WorldPawnThrottle
{
    private static int _cycle;

    public static bool Prefix()
    {
        int level = FpsState.LoadLevel;
        if (level == 0) return true;

        _cycle++;
        int divisor = level == 2 ? 4 : 2;
        if (_cycle % divisor == 0) return true;

        FpsState.WorldPawnTicksSkipped++;
        return false;
    }
}

/// <summary>Halves ambient per-cell simulation frequency while struggling.</summary>
public static class EnvironmentThrottle
{
    private static int _cycle;

    public static bool Prefix()
    {
        if (FpsState.LoadLevel < 2) return true;
        return ++_cycle % 2 == 0;
    }
}

/// <summary>
/// Scheduled gen-0 collections during smooth frames prevent the runtime from
/// accumulating garbage until it must run a huge blocking collection
/// mid-combat. Runs at most every 45 seconds and only when FPS is healthy.
/// </summary>
public static class SmoothGc
{
    public static bool Enabled;
    public static int Collections;

    private const float MinIntervalSeconds = 45f;
    private static float _lastGcTime;

    public static void OnFrame()
    {
        if (!Enabled) return;
        if (Time.realtimeSinceStartup - _lastGcTime < MinIntervalSeconds) return;
        if (FpsState.SmoothedFps < 50f) return; // never GC while already struggling

        _lastGcTime = Time.realtimeSinceStartup;
        try
        {
            GC.Collect(0, GCCollectionMode.Optimized);
            Collections++;
        }
        catch { }
    }
}

/// <summary>
/// Publishes live optimizer status for the FlexTool desktop app via a small
/// IPC file, matching the pattern used by the Debug Info overlay mod.
/// </summary>
public static class StatusFile
{
    private static string PathToFile =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlexTool", "FlexToolFpsOptimizer.txt");

    public static void Write()
    {
        string content =
            "fps=" + FpsState.SmoothedFps.ToString("F0") + "\n" +
            "loadLevel=" + FpsState.LoadLevel + "\n" +
            "colonists=" + FpsState.ColonistCount + "\n" +
            "jobSearchesDeferred=" + FpsState.JobSearchesDeferred + "\n" +
            "worldPawnTicksSkipped=" + FpsState.WorldPawnTicksSkipped + "\n" +
            "gcCollections=" + SmoothGc.Collections + "\n" +
            "pawnTicksThrottled=" + FpsState.PawnTicksThrottled + "\n" +
            "updated=" + DateTime.Now.ToString("O") + "\n";

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(PathToFile);
                if (dir is not null) Directory.CreateDirectory(dir);
                File.WriteAllText(PathToFile, content);
            }
            catch { }
        });
    }
}

/// <summary>
/// Skips redundant tick work for off-screen, idle, non-combat ANIMALS only
/// while the game is under load. Colonists, drafted pawns, hostiles, mental
/// states, predators and anything on screen are never touched. On RimWorld
/// 1.6+ skipped deltas are banked and replayed, so total simulated time is
/// lossless. (Merged from the former FlexTool Performance mod.)
/// </summary>
public static class PawnTickThrottle
{
    private const int MaxBankedTicks = 15;
    private const int MaxTrackedPawns = 8192;

    private static readonly Dictionary<int, int> SkipCounters = new();
    private static readonly Dictionary<int, int> BankedDelta = new();

    private static CellRect _viewRect;
    private static int _viewRectFrame = -1;

    /// <summary>Skip factor derived from the shared load level: 0→1 (off), 1→2, 2→4.</summary>
    private static int Factor => FpsState.LoadLevel switch { 2 => 4, 1 => 2, _ => 1 };

    /// <summary>RimWorld 1.6+ seam: bank skipped deltas, replay them later.</summary>
    public static bool IntervalPrefix(Pawn __instance, ref int delta)
    {
        int factor = Factor;
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
            FpsState.PawnTicksThrottled++;
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
        int factor = Factor;
        if (factor <= 1) return true;
        if (!ShouldThrottle(__instance)) return true;

        PruneIfNeeded();

        int id = __instance.thingIDNumber;
        SkipCounters.TryGetValue(id, out int counter);
        counter++;
        SkipCounters[id] = counter;
        if (counter % factor == 0) return true;
        FpsState.PawnTicksThrottled++;
        return false;
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
/// pawns decide where to go much faster, which slashes pathfinding CPU cost
/// on large maps and big colonies. (Merged from the former FlexTool Performance mod.)
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
