using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FlexTool.AutoLoadMod;

/// <summary>IPC protocol version. Must match FlexTool's RimWorldSaveReader.IpcProtocolVersion.</summary>
internal static class IpcProtocol
{
    public const string Version = "1";
}

[StaticConstructorOnStartup]
public static class FlexToolAutoLoadInit
{
    static FlexToolAutoLoadInit()
    {
        try
        {
            Directory.CreateDirectory(GenFilePaths.SaveDataFolderPath);
        }
        catch (Exception ex)
        {
            Log.Warning("[FlexTool] Could not pre-create save-data folder: " + ex.Message);
        }

        var harmony = new Harmony("flextool.autoload");

        // Apply each patch independently so one failure doesn't block the others
        try
        {
            harmony.PatchAll();
            Log.Message("[FlexTool] AutoLoad mod initialized, all Harmony patches applied.");
        }
        catch (Exception ex)
        {
            Log.Warning("[FlexTool] PatchAll failed, applying patches individually: " + ex.Message);

            TryPatch(harmony, typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI),
                typeof(MainMenuAutoLoadPatch), nameof(MainMenuAutoLoadPatch.Prefix), "MainMenuAutoLoadPatch");

            TryPatch(harmony, typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame),
                typeof(SaveLoadTracker), nameof(SaveLoadTracker.Prefix), "SaveLoadTracker", [typeof(string)]);

            TryPatch(harmony, typeof(Root_Play), "Update",
                typeof(InGameLoadPatch), nameof(InGameLoadPatch.Postfix), "InGameLoadPatch", isPostfix: true);

            TryPatch(harmony, typeof(Root_Play), "Update",
                typeof(PerformanceMonitorPatch), nameof(PerformanceMonitorPatch.Postfix), "PerformanceMonitorPatch", isPostfix: true);

            TryPatch(harmony, typeof(Root_Play), "Update",
                typeof(PawnEditPatch), nameof(PawnEditPatch.Postfix), "PawnEditPatch", isPostfix: true);
        }
    }

    private static void TryPatch(Harmony harmony, Type targetType, string targetMethod,
        Type patchType, string patchMethod, string label, Type[]? parameters = null, bool isPostfix = false)
    {
        try
        {
            var original = parameters != null
                ? AccessTools.Method(targetType, targetMethod, parameters)
                : AccessTools.Method(targetType, targetMethod);
            var hm = new HarmonyMethod(patchType, patchMethod);
            harmony.Patch(original, prefix: isPostfix ? null : hm, postfix: isPostfix ? hm : null);
            Log.Message($"[FlexTool] {label} applied.");
        }
        catch (Exception e) { Log.Error($"[FlexTool] {label} failed: " + e); }
    }
}

/// <summary>
/// One-shot: reads FlexToolAutoLoad.txt on first main-menu render and loads the requested save.
/// </summary>
[HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
public static class MainMenuAutoLoadPatch
{
    private static bool _handled;

    public static void Prefix()
    {
        if (_handled) return;
        _handled = true;

        try
        {
            var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "FlexToolAutoLoad.txt");
            Log.Message("[FlexTool] Checking for auto-load file: " + path);

            if (!File.Exists(path))
            {
                Log.Message("[FlexTool] No auto-load file found.");
                return;
            }

            var saveName = File.ReadAllText(path).Trim();
            File.Delete(path);

            if (string.IsNullOrEmpty(saveName))
            {
                Log.Message("[FlexTool] Auto-load file was empty.");
                return;
            }

            Log.Message("[FlexTool] Loading save: " + saveName);
            LongEventHandler.QueueLongEvent(
                () => GameDataSaveLoader.LoadGame(saveName),
                "LoadingLongEvent", true, null);
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool] AutoLoad error: " + ex);
        }
    }
}

/// <summary>
/// Polls for the IPC command file while the game is running so FlexTool can
/// hot-load a different save without restarting the game.
/// Checks every ~60 frames (~1 second at 60 fps) to keep overhead minimal.
/// </summary>
[HarmonyPatch(typeof(Root_Play), "Update")]
public static class InGameLoadPatch
{
    private static int _frameCounter;

    public static void Postfix()
    {
        if (++_frameCounter < 60) return;
        _frameCounter = 0;

        try
        {
            var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "FlexToolAutoLoad.txt");
            if (!File.Exists(path)) return;

            var saveName = File.ReadAllText(path).Trim();
            File.Delete(path);

            if (string.IsNullOrEmpty(saveName)) return;

            Log.Message("[FlexTool] Hot-loading save: " + saveName);
            LongEventHandler.QueueLongEvent(
                () => GameDataSaveLoader.LoadGame(saveName),
                "LoadingLongEvent", true, null);
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool] InGame load error: " + ex);
        }
    }
}

/// <summary>
/// Tracks every save load (manual, autosave, or FlexTool-initiated) and writes the
/// active save name to FlexToolCurrentSave.txt so FlexTool can poll it.
/// </summary>
[HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), typeof(string))]
public static class SaveLoadTracker
{
    public static void Prefix(string saveFileName)
    {
        try
        {
            var statusPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "FlexToolCurrentSave.txt");
            File.WriteAllText(statusPath, saveFileName);
            Log.Message("[FlexTool] Save loaded: " + saveFileName);
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool] Failed to write current-save status: " + ex);
        }
    }
}

/// <summary>
/// Writes FPS, memory usage, and GC collection counts to an IPC file every ~120 frames (~2 seconds).
/// FlexTool reads this file to display a live performance graph in the Analytics page.
/// Uses managed GC APIs instead of Unity Profiler to avoid JIT resolution issues.
/// </summary>
[HarmonyPatch(typeof(Root_Play), "Update")]
public static class PerformanceMonitorPatch
{
    private static int _frame;
    private static float _fpsAccum;
    private static int _fpsSamples;
    private static string? _perfPath;

    public static void Postfix()
    {
        try
        {
            // Accumulate FPS each frame
            float dt = UnityEngine.Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                _fpsAccum += 1f / dt;
                _fpsSamples++;
            }

            if (++_frame < 120) return;
            _frame = 0;

            float avgFps = _fpsSamples > 0 ? _fpsAccum / _fpsSamples : 0f;
            _fpsAccum = 0f;
            _fpsSamples = 0;

            // Use managed GC API — always available, no Unity Profiler dependency
            long memBytes = GC.GetTotalMemory(false);
            long memReserved = memBytes; // safe default
            // Try Unity profiler for a more accurate reserved-memory figure
            try { memReserved = GetUnityReservedMemory(); }
            catch { memReserved = memBytes; }

            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);

            _perfPath ??= Path.Combine(GenFilePaths.SaveDataFolderPath, "FlexToolPerf.xml");

            var doc = new XDocument(new XElement("FlexToolPerf",
                new XElement("Version", IpcProtocol.Version),
                new XElement("FPS", avgFps.ToString("F1")),
                new XElement("MemUsedMB", (memBytes / (1024.0 * 1024.0)).ToString("F1")),
                new XElement("MemReservedMB", (memReserved / (1024.0 * 1024.0)).ToString("F1")),
                new XElement("GC0", gc0),
                new XElement("GC1", gc1),
                new XElement("GC2", gc2),
                new XElement("Timestamp", DateTime.Now.ToString("o"))));

            // Off the main thread: synchronous XML saves every ~2s cause
            // visible micro-stutter on slow disks.
            var path = _perfPath;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { doc.Save(path); }
                catch { }
            });
        }
        catch { }
    }

    /// <summary>
    /// Isolated in its own method so a JIT failure resolving UnityEngine.Profiling.Profiler
    /// is caught by the caller's try-catch instead of preventing the entire Postfix from compiling.
    /// </summary>
    private static long GetUnityReservedMemory()
    {
        return UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
    }
}

/// <summary>
/// Polls for FlexToolPawnEdit.xml every ~60 frames (~1 second).
/// Applies pawn name/age edits and skill level changes sent from FlexTool.
/// </summary>
[HarmonyPatch(typeof(Root_Play), "Update")]
public static class PawnEditPatch
{
    private static int _frame;
    private static string? _editPath;
    private static string? _resultPath;

    public static void Postfix()
    {
        try
        {
        if (++_frame < 60) return;
        _frame = 0;

        _editPath ??= Path.Combine(GenFilePaths.SaveDataFolderPath, "FlexToolPawnEdit.xml");
        _resultPath ??= Path.Combine(GenFilePaths.SaveDataFolderPath, "FlexToolPawnEditResult.xml");

            if (!File.Exists(_editPath)) return;

            var xml = File.ReadAllText(_editPath);
            File.Delete(_editPath);

            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return;

            var targetName = root.Element("TargetName")?.Value ?? "";
            var ipcVer = root.Element("Version")?.Value ?? "0";
            Log.Message($"[FlexTool] Pawn edit received for: {targetName} (IPC v{ipcVer})");

            string result;
            try { result = ApplyEdit(targetName, root); }
            catch (Exception ex)
            {
                result = "ERROR: " + ex.Message;
                Log.Error("[FlexTool] Pawn edit error: " + ex);
            }

            var resultDoc = new XDocument(new XElement("FlexToolPawnEditResult",
                new XElement("Version", IpcProtocol.Version),
                new XElement("Target", targetName),
                new XElement("Result", result),
                new XElement("Timestamp", DateTime.Now.ToString("o"))));
            resultDoc.Save(_resultPath);
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool] PawnEdit poll error: " + ex);
        }
    }

    private static string ApplyEdit(string targetName, XElement root)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        var pawn = map.mapPawns.FreeColonists
            .FirstOrDefault(p =>
                p.Name is NameTriple nt &&
                (nt.Nick == targetName || $"{nt.First} {nt.Last}" == targetName || nt.ToString() == targetName));

        if (pawn is null)
            return $"Pawn not found: {targetName}";

        int changes = 0;

        // Name edits
        if (pawn.Name is NameTriple name)
        {
            var first = root.Element("FirstName")?.Value;
            var nick = root.Element("Nickname")?.Value;
            var last = root.Element("LastName")?.Value;

            if (first != null || nick != null || last != null)
            {
                pawn.Name = new NameTriple(
                    first ?? name.First,
                    nick ?? name.Nick,
                    last ?? name.Last);
                changes++;
            }
        }

        // Skill edits
        var skillsEl = root.Element("Skills");
        if (skillsEl != null && pawn.skills != null)
        {
            foreach (var s in skillsEl.Elements("Skill"))
            {
                var skillName = s.Element("Name")?.Value;
                if (skillName is null) continue;
                if (!int.TryParse(s.Element("Level")?.Value, out int level)) continue;

                var skillDef = DefDatabase<SkillDef>.AllDefs.FirstOrDefault(
                    d => d.defName.Equals(skillName, StringComparison.OrdinalIgnoreCase)
                      || (d.label != null && d.label.Equals(skillName, StringComparison.OrdinalIgnoreCase)));

                if (skillDef is null) continue;
                var skillRecord = pawn.skills.GetSkill(skillDef);
                if (skillRecord is null || skillRecord.TotallyDisabled) continue;

                skillRecord.Level = Math.Max(0, Math.Min(20, level));
                changes++;
            }
        }

        return changes > 0
            ? $"Applied {changes} edit(s) to {pawn.LabelShort}"
            : "No edits applied (values unchanged or invalid)";
    }
}
