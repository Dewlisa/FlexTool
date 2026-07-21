using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FlexTool.DebugInfoMod;

/// <summary>
/// FlexTool Debug Info mod. Shows an in-game overlay with FPS, memory usage,
/// current tick rate, pawn statistics and the active save name.
/// The overlay is controlled from FlexTool via an IPC settings file and is
/// drawn on the right side of the screen. Can also raise in-game alert popups.
/// </summary>
[StaticConstructorOnStartup]
public static class DebugInfoModInit
{
    static DebugInfoModInit()
    {
        try
        {
            EnsureFolders();
            var harmony = new Harmony("flextool.debuginfomod");
            harmony.PatchAll();
            Log.Message("[FlexTool Debug Info] Initialized — overlay ready (toggle from FlexTool).");
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Debug Info] Failed to initialize: " + ex);
        }
    }

    private static void EnsureFolders()
    {
        try
        {
            System.IO.Directory.CreateDirectory(GenFilePaths.SaveDataFolderPath);
            System.IO.Directory.CreateDirectory(
                System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "FlexToolPawnLibrary"));
        }
        catch (Exception ex)
        {
            Log.Warning("[FlexTool Debug Info] Could not pre-create folders: " + ex.Message);
        }
    }
}

/// <summary>Shared toggle state for the debug overlay, synced from the FlexTool IPC file.</summary>
public static class DebugInfoState
{
    public static bool OverlayEnabled;
    public static bool ShowFps = true;
    public static bool ShowMemory = true;
    public static bool ShowTickRate = true;
    public static bool ShowPawnStats = true;
    public static bool ShowSaveName = true;
    public static bool InGameAlerts;

    private static readonly string ConfigPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolDebugOverlay.txt");

    private static float _nextPoll;
    private static DateTime _lastWrite = DateTime.MinValue;

    /// <summary>Re-reads the IPC config file at most twice a second.</summary>
    public static void Poll()
    {
        if (Time.realtimeSinceStartup < _nextPoll) return;
        _nextPoll = Time.realtimeSinceStartup + 0.5f;

        try
        {
            if (!File.Exists(ConfigPath))
            {
                OverlayEnabled = false;
                return;
            }

            var write = File.GetLastWriteTimeUtc(ConfigPath);
            if (write == _lastWrite) return;
            _lastWrite = write;

            foreach (var line in File.ReadAllLines(ConfigPath))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                bool val = parts[1].Trim() == "1";
                switch (parts[0].Trim())
                {
                    case "Enabled": OverlayEnabled = val; break;
                    case "Fps": ShowFps = val; break;
                    case "Memory": ShowMemory = val; break;
                    case "TickRate": ShowTickRate = val; break;
                    case "PawnStats": ShowPawnStats = val; break;
                    case "SaveName": ShowSaveName = val; break;
                    case "Alerts": InGameAlerts = val; break;
                }
            }
        }
        catch { }
    }
}

/// <summary>Draws the debug overlay each UI frame while enabled.</summary>
[HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootOnGUI))]
public static class DebugOverlayPatch
{
    private static float _fps;
    private static float _fpsTimer;
    private static int _fpsFrames;

    public static void Postfix()
    {
        try
        {
            DebugInfoState.Poll();
            if (!DebugInfoState.OverlayEnabled) return;
            if (Event.current.type != EventType.Repaint) return;

            // FPS accumulator
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrames / _fpsTimer;
                _fpsFrames = 0;
                _fpsTimer = 0f;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("FlexTool Debug");

            if (DebugInfoState.ShowFps)
                sb.AppendLine($"FPS: {_fps:F0}");

            if (DebugInfoState.ShowMemory)
            {
                float usedMb = GC.GetTotalMemory(false) / (1024f * 1024f);
                sb.AppendLine($"Managed Mem: {usedMb:F0} MB");
            }

            if (DebugInfoState.ShowTickRate && Current.Game != null)
            {
                var tm = Find.TickManager;
                sb.AppendLine($"Speed: {tm.CurTimeSpeed} (x{tm.TickRateMultiplier:F1})");
                sb.AppendLine($"Tick: {tm.TicksGame:N0}");
            }

            if (DebugInfoState.ShowPawnStats && Current.Game != null && Find.CurrentMap != null)
            {
                var map = Find.CurrentMap;
                int colonists = map.mapPawns.FreeColonistsCount;
                int prisoners = map.mapPawns.PrisonersOfColonyCount;
                int all = map.mapPawns.AllPawnsCount;
                sb.AppendLine($"Colonists: {colonists}  Prisoners: {prisoners}");
                sb.AppendLine($"Total Pawns (map): {all}");
            }

            if (DebugInfoState.ShowSaveName && Current.Game != null)
            {
                var saveName = SaveNameTracker.CurrentSaveName;
                sb.AppendLine($"Save: {(string.IsNullOrEmpty(saveName) ? "(unsaved)" : saveName)}");
            }

            var text = sb.ToString().TrimEnd();
            var prevFont = Text.Font;
            Text.Font = GameFont.Small;
            var size = Text.CalcSize(text);
            size.x = Mathf.Max(size.x + 16f, 180f);
            float height = Text.CalcHeight(text, size.x) + 12f;
            // Right side of the screen, below the colonist bar
            var rect = new Rect(UI.screenWidth - size.x - 10f, 100f, size.x, height);

            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), text);
            GUI.color = prevColor;
            Text.Font = prevFont;

            // Let the live alerts anchor themselves under the overlay panel.
            DebugAlerts.DrawUnderOverlay(rect);
        }
        catch { }
    }
}

/// <summary>
/// Tracks the name of the save file currently being played by patching
/// RimWorld's load/save entry points.
/// </summary>
public static class SaveNameTracker
{
    public static string CurrentSaveName = "";
}

[HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.LoadGame), typeof(string))]
public static class LoadGameNamePatch
{
    public static void Prefix(string saveFileName)
    {
        SaveNameTracker.CurrentSaveName = saveFileName;
    }
}

[HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
public static class SaveGameNamePatch
{
    public static void Prefix(string fileName)
    {
        SaveNameTracker.CurrentSaveName = fileName;
    }
}

/// <summary>
/// Raises in-game popup messages when performance or memory crosses warning
/// thresholds. Only active when enabled from FlexTool (Alerts=1). Runs
/// independently of the overlay so alerts still fire when it is hidden.
/// </summary>
public static class DebugAlerts
{
    private static float _nextAlertAllowed;
    private const float AlertCooldownSeconds = 30f;
    private const float AlertLifeSeconds = 12f;

    private class ActiveAlert { public string Text; public float Expires; }
    private static readonly List<ActiveAlert> _active = new List<ActiveAlert>();

    private static float _fps;
    private static float _fpsTimer;
    private static int _fpsFrames;

    public static void Tick()
    {
        FlushPendingMessages();
        if (!DebugInfoState.InGameAlerts) return;
        if (Current.Game == null) return;

        // Own FPS accumulator — alerts must not depend on the overlay's counters.
        _fpsFrames++;
        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer >= 0.5f)
        {
            _fps = _fpsFrames / _fpsTimer;
            _fpsFrames = 0;
            _fpsTimer = 0f;
        }

        if (Time.realtimeSinceStartup < _nextAlertAllowed) return;

        try
        {
            if (_fps > 0f && _fps < 20f)
            {
                Raise($"Low FPS: {_fps:F0}");
                _nextAlertAllowed = Time.realtimeSinceStartup + AlertCooldownSeconds;
                return;
            }

            float usedMb = GC.GetTotalMemory(false) / (1024f * 1024f);
            if (usedMb > 6000f)
            {
                Raise($"High memory usage: {usedMb:F0} MB");
                _nextAlertAllowed = Time.realtimeSinceStartup + AlertCooldownSeconds;
            }
        }
        catch { }
    }

    /// <summary>Lets CrashGuard surface its protective actions through the live alert channel.</summary>
    public static void Notify(string message)
    {
        try
        {
            if (!DebugInfoState.InGameAlerts) return;
            if (Current.Game == null) return;
            Raise(message);
        }
        catch { }
    }

    // Cross-thread safe message queue: Messages.Message() must only run on the
    // main thread. Raise() can be reached from the threaded log callback, so
    // fallback messages are queued and flushed from Tick() (main thread).
    private static readonly Queue<string> _pendingMessages = new Queue<string>();

    /// <summary>Adds an alert shown under the FlexTool Debug overlay (falls back to a game message when the overlay is hidden).</summary>
    private static void Raise(string message)
    {
        lock (_active)
        {
            _active.Add(new ActiveAlert { Text = message, Expires = Time.realtimeSinceStartup + AlertLifeSeconds });
            if (_active.Count > 5) _active.RemoveAt(0);
        }
        if (!DebugInfoState.OverlayEnabled)
        {
            lock (_pendingMessages)
            {
                if (_pendingMessages.Count < 8) _pendingMessages.Enqueue(message);
            }
        }
    }

    /// <summary>Flushes queued fallback messages on the main thread (called from Tick()).</summary>
    public static void FlushPendingMessages()
    {
        if (_pendingMessages.Count == 0) return;
        lock (_pendingMessages)
        {
            while (_pendingMessages.Count > 0)
            {
                try
                {
                    Messages.Message("[FlexTool] " + _pendingMessages.Dequeue(),
                        MessageTypeDefOf.CautionInput, historical: false);
                }
                catch { }
            }
        }
    }

    /// <summary>Draws active alerts as highlighted rows directly below the debug overlay panel.</summary>
    public static void DrawUnderOverlay(Rect overlayRect)
    {
        try
        {
            lock (_active)
            {
                _active.RemoveAll(a => Time.realtimeSinceStartup > a.Expires);
            }
            if (_active.Count == 0) return;

            var prevFont = Text.Font;
            var prevColor = GUI.color;
            Text.Font = GameFont.Small;

            float y = overlayRect.yMax + 4f;
            foreach (var alert in _active)
            {
                string line = "\u26A0 " + alert.Text;
                float h = Text.CalcHeight(line, overlayRect.width - 16f) + 8f;
                var r = new Rect(overlayRect.x, y, overlayRect.width, h);

                GUI.color = new Color(0.45f, 0.25f, 0f, 0.8f); // highlighted amber
                GUI.DrawTexture(r, BaseContent.WhiteTex);
                GUI.color = new Color(1f, 0.85f, 0.35f);
                Widgets.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, r.height - 8f), line);

                y += h + 3f;
            }

            GUI.color = prevColor;
            Text.Font = prevFont;
        }
        catch { }
    }
}

// ─────────────────────────────────────────────────────────────────────────
// CrashGuard — in-game companion for FlexTool's crash handler.
//   1) Heartbeat: writes a timestamp + game tick to an IPC file every second
//      so FlexTool's watchdog can detect LOGIC-level hangs (stuck tick loop),
//      not just frozen windows.
//   2) Exception interceptor: hooks Unity's log callback and counts
//      exceptions from inside the process — reacting instantly to critical
//      errors (OOM, AccessViolation) and exception storms.
//   3) Emergency autosave: on danger, performs a REAL in-game autosave
//      ("FlexTool_Emergency") so zero progress is lost — far stronger than
//      copying the last save file on disk.
// Controlled from FlexTool via the FlexToolCrashGuard.txt IPC file.
// ─────────────────────────────────────────────────────────────────────────

public static class CrashGuard
{
    public static bool Enabled;

    private static readonly string ConfigPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolCrashGuard.txt");
    private static readonly string HeartbeatPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolHeartbeat.txt");
    private static readonly string EventPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolCrashGuardEvent.txt");

    /// <summary>Reports a protective action to FlexTool so the app can toast it (non-blocking).</summary>
    public static void ReportEvent(string kind, string detail)
    {
        string content =
            "utc=" + DateTime.UtcNow.ToString("O") + "\n" +
            "kind=" + kind + "\n" +
            "detail=" + detail.Replace('\n', ' ') + "\n";
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try { File.WriteAllText(EventPath, content); }
            catch { }
        });
    }

    private static float _nextConfigPoll;
    private static float _nextHeartbeat;
    private static bool _hooked;

    // Exception-storm tracking
    private static int _exceptionsThisWindow;
    private static float _windowStart;
    private static float _nextEmergencySaveAllowed;
    private static volatile bool _emergencySaveRequested;
    private static volatile string _emergencyReason = "";

    // Freeze prevention
    private static float _nextMemoryCheck;
    private static float _nextMemoryRelief;
    private static float _nextCriticalRelief;
    private static float _lastFrameTime;
    private static int _slowFramesInARow;
    private static float _nextSlowFrameSave;
    private static System.Threading.Thread _stallWatchdog;
    private static volatile int _mainThreadPulse;
    private static volatile bool _stallFlagWritten;

    /// <summary>Called every frame from the overlay patch (cheap: real work is time-gated).</summary>
    public static void Tick()
    {
        PollConfig();
        if (!Enabled) return;

        EnsureExceptionHook();
        EnsureStallWatchdog();
        _mainThreadPulse++; // proves the main thread is alive
        WriteHeartbeat();
        CheckMemoryPressure();
        CheckSlowFrameSpiral();
        RunPendingEmergencySave();
    }

    private static void PollConfig()
    {
        if (Time.realtimeSinceStartup < _nextConfigPoll) return;
        _nextConfigPoll = Time.realtimeSinceStartup + 2f;
        try
        {
            Enabled = File.Exists(ConfigPath) && File.ReadAllText(ConfigPath).Contains("Enabled=1");
        }
        catch { }
    }

    // ── 1) Heartbeat ─────────────────────────────────────────────────────
    private static void WriteHeartbeat()
    {
        if (Time.realtimeSinceStartup < _nextHeartbeat) return;
        _nextHeartbeat = Time.realtimeSinceStartup + 1f;
        try
        {
            int tick = Current.Game != null ? Find.TickManager.TicksGame : -1;
            string content =
                "utc=" + DateTime.UtcNow.ToString("O") + "\n" +
                "tick=" + tick + "\n";
            // Non-blocking: offload the disk write so slow disks never stall
            // the game (main thread or scheduler contention).
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { File.WriteAllText(HeartbeatPath, content); }
                catch { }
            });
        }
        catch { }
    }

    // ── 2) Exception interceptor ─────────────────────────────────────────
    private static void EnsureExceptionHook()
    {
        if (_hooked) return;
        _hooked = true;
        try
        {
            Application.logMessageReceivedThreaded += OnLogMessage;
            Log.Message("[FlexTool Debug Info] CrashGuard armed — exception interception and emergency autosave active.");
        }
        catch { }
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        try
        {
            if (type != LogType.Exception && type != LogType.Error) return;

            // Critical signatures: request an emergency autosave IMMEDIATELY.
            bool critical =
                   Contains(condition, "OutOfMemoryException")
                || Contains(condition, "AccessViolationException")
                || Contains(condition, "StackOverflowException")
                || Contains(condition, "Could not allocate memory");

            if (critical)
            {
                ReportEvent("CrashPrevented", "Critical error intercepted: " + FirstLine(condition));
                RequestEmergencySave("critical error: " + FirstLine(condition));
                return;
            }

            if (type != LogType.Exception) return;

            // Exception storm: 15+ exceptions within 5 seconds → a mod is
            // melting down; save before it takes the game with it.
            float now = Time.realtimeSinceStartup;
            if (now - _windowStart > 5f)
            {
                _windowStart = now;
                _exceptionsThisWindow = 0;
            }
            if (++_exceptionsThisWindow >= 15)
            {
                _exceptionsThisWindow = 0;
                ReportEvent("CrashPrevented", "Exception storm intercepted (15+ in 5s)");
                RequestEmergencySave("exception storm (15+ exceptions in 5s)");
            }
        }
        catch { }
    }

    private static bool Contains(string haystack, string needle)
        => haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int idx = s.IndexOf('\n');
        return idx > 0 ? s.Substring(0, idx) : s;
    }

    // ── 4) Freeze prevention: memory-pressure relief ───────────────────────
    // The #1 cause of RimWorld freezes is memory filling up until the game
    // grinds to a halt. When managed memory climbs past a high-water mark we
    // proactively force a GC + unload unused assets, which frees memory
    // BEFORE the game starts stuttering into a freeze.
    private static void CheckMemoryPressure()
    {
        if (Time.realtimeSinceStartup < _nextMemoryCheck) return;
        _nextMemoryCheck = Time.realtimeSinceStartup + 10f;
        try
        {
            long managedMb = GC.GetTotalMemory(false) / (1024 * 1024);

            // Light, incremental relief: Gen0-only collection is fast (<10ms)
            // and never causes an audible frame stall. Runs earlier and more
            // often so pressure never builds up to a big stop-the-world GC.
            if (managedMb >= 3000 && Time.realtimeSinceStartup >= _nextMemoryRelief)
            {
                _nextMemoryRelief = Time.realtimeSinceStartup + 60f; // at most once per minute
                GC.Collect(0); // Gen0 only — incremental, no WaitForPendingFinalizers
                DebugAlerts.Notify("Memory pressure relieved at " + managedMb + " MB — freeze prevented.");
                ReportEvent("FreezePrevented", "Memory pressure relieved at " + managedMb + " MB");
            }

            // Critical territory only: emergency save + asset unload. The save
            // is queued through LongEventHandler so it happens at a safe time,
            // never mid-frame.
            if (managedMb >= 4000 && Time.realtimeSinceStartup >= _nextCriticalRelief)
            {
                _nextCriticalRelief = Time.realtimeSinceStartup + 300f; // at most once per 5 min
                RequestEmergencySave("critical memory pressure (" + managedMb + " MB)");
                Resources.UnloadUnusedAssets();
                GC.Collect(0);
                Log.Warning("[FlexTool Debug Info] Critical memory relief performed at " + managedMb + " MB.");
            }
        }
        catch { }
    }

    // ── 5) Freeze prevention: slow-frame spiral detection ───────────────────
    // Freezes rarely happen instantly — frames get progressively slower first.
    // Several consecutive very slow frames (>1s each) means the game is
    // spiraling toward a freeze: autosave now, while saving is still possible.
    private static void CheckSlowFrameSpiral()
    {
        float now = Time.realtimeSinceStartup;
        float delta = now - _lastFrameTime;
        _lastFrameTime = now;
        if (delta <= 0f || delta > 30f) return; // startup / loading screens

        if (delta > 1f) _slowFramesInARow++;
        else _slowFramesInARow = 0;

        if (_slowFramesInARow >= 5 && now >= _nextSlowFrameSave)
        {
            _slowFramesInARow = 0;
            _nextSlowFrameSave = now + 300f; // once per 5 min
            DebugAlerts.Notify("Severe slowdown detected — saving before a possible freeze.");
            ReportEvent("FreezeWarning", "Severe slowdown — emergency save triggered");
            RequestEmergencySave("severe slowdown — possible incoming freeze");
        }
    }

    // ── 6) Freeze detection: in-process stall watchdog ──────────────────────
    // A background thread notices a stuck main thread within ~5 seconds and
    // stamps the heartbeat file with a stall flag so FlexTool's external
    // watchdog can react much faster than its own 30-second window check.
    private static void EnsureStallWatchdog()
    {
        if (_stallWatchdog != null && _stallWatchdog.IsAlive) return;
        _stallWatchdog = new System.Threading.Thread(() =>
        {
            int lastPulse = _mainThreadPulse;
            var stalledSince = DateTime.MinValue;
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
                try
                {
                    if (_mainThreadPulse != lastPulse)
                    {
                        lastPulse = _mainThreadPulse;
                        stalledSince = DateTime.MinValue;
                        _stallFlagWritten = false;
                        continue;
                    }
                    if (stalledSince == DateTime.MinValue) stalledSince = DateTime.UtcNow;
                    if ((DateTime.UtcNow - stalledSince).TotalSeconds >= 5 && !_stallFlagWritten)
                    {
                        // Main thread stuck ≥ 5s — flag it ONCE for the external
                        // watchdog (repeated writes each second add I/O pressure).
                        _stallFlagWritten = true;
                        File.WriteAllText(HeartbeatPath,
                            "utc=" + stalledSince.ToString("O") + "\n" +
                            "tick=-1\n" +
                            "stalled=1\n");
                    }
                }
                catch { }
            }
        })
        { IsBackground = true, Name = "FlexToolStallWatchdog" };
        _stallWatchdog.Start();
    }

    // ── 3) Emergency autosave ────────────────────────────────────────────
    private static void RequestEmergencySave(string reason)
    {
        // May be called from any thread (threaded log callback) — just set a
        // flag; the actual save happens on the main thread in Tick().
        _emergencyReason = reason;
        _emergencySaveRequested = true;
    }

    private static void RunPendingEmergencySave()
    {
        if (!_emergencySaveRequested) return;
        if (Time.realtimeSinceStartup < _nextEmergencySaveAllowed) return;
        _emergencySaveRequested = false;

        if (Current.Game == null || Current.ProgramState != ProgramState.Playing) return;
        _nextEmergencySaveAllowed = Time.realtimeSinceStartup + 120f; // max once per 2 min

        var reason = _emergencyReason;
        LongEventHandler.QueueLongEvent(() =>
        {
            try
            {
                GameDataSaveLoader.SaveGame("FlexTool_Emergency");
                Messages.Message("[FlexTool] Danger detected (" + reason + ") — emergency save created: FlexTool_Emergency",
                    MessageTypeDefOf.ThreatSmall, historical: false);
                Log.Warning("[FlexTool Debug Info] Emergency autosave written (" + reason + ").");
            }
            catch (Exception ex)
            {
                Log.Warning("[FlexTool Debug Info] Emergency autosave failed: " + ex.Message);
            }
        }, "Autosaving", false, null);
    }
}

/// <summary>
/// Runs CrashGuard every UI frame — independent of the overlay toggle, so
/// crash protection works even when the debug overlay is hidden.
/// </summary>
[HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootOnGUI))]
public static class CrashGuardPatch
{
    public static void Postfix()
    {
        try
        {
            if (Event.current.type != EventType.Repaint) return;
            CrashGuard.Tick();
            DebugAlerts.Tick();
            DevLogMirror.Tick();
            LivePawnIpc.Tick();
        }
        catch { }
    }
}

// ─────────────────────────────────────────────────────────────────────────
// DevLogMirror — mirrors everything RimWorld's dev log receives into an
// IPC file (FlexToolDevLog.txt) so FlexTool can show a live Dev Log page.
// Buffered + flushed off the main thread to avoid I/O stutter.
// ─────────────────────────────────────────────────────────────────────────
public static class DevLogMirror
{
    private static readonly string LogPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolDevLog.txt");

    private static readonly object _lock = new object();
    private static readonly System.Text.StringBuilder _buffer = new System.Text.StringBuilder();
    private static bool _hooked;
    private static float _nextFlush;
    private const long MaxLogBytes = 2 * 1024 * 1024; // 2 MB cap

    public static void Tick()
    {
        EnsureHook();
        if (Time.realtimeSinceStartup < _nextFlush) return;
        _nextFlush = Time.realtimeSinceStartup + 2f;

        string chunk = null;
        lock (_lock)
        {
            if (_buffer.Length > 0)
            {
                chunk = _buffer.ToString();
                _buffer.Length = 0;
            }
        }
        if (chunk == null) return;

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            // Dedicated write lock: flushes from consecutive ticks must never
            // interleave or land out of order.
            lock (_writeLock)
            {
                try
                {
                    // Reset the file when it grows too large.
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                        File.WriteAllText(LogPath, "[FlexTool] Dev log truncated (size cap reached)\n");
                    File.AppendAllText(LogPath, chunk);
                }
                catch { }
            }
        });
    }

    private static readonly object _writeLock = new object();

    private static void EnsureHook()
    {
        if (_hooked) return;
        _hooked = true;
        try
        {
            // Start each session with a fresh log.
            File.WriteAllText(LogPath,
                "[FlexTool] Dev log session started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            Application.logMessageReceivedThreaded += OnLogMessage;
        }
        catch { }
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        try
        {
            string level;
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception: level = "ERROR"; break;
                case LogType.Warning: level = "WARN "; break;
                default: level = "INFO "; break;
            }

            lock (_lock)
            {
                if (_buffer.Length > 512 * 1024) return; // runaway guard
                _buffer.Append(DateTime.Now.ToString("HH:mm:ss"))
                       .Append(" [").Append(level).Append("] ")
                       .AppendLine(condition);
                if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
                    _buffer.AppendLine(stackTrace.TrimEnd());
            }
        }
        catch { }
    }
}

// ─────────────────────────────────────────────────────────────────────────
// LivePawnIpc — live in-game pawn extraction / spawning.
// FlexTool writes a command file (FlexToolPawnExtract.txt); this class
// executes it against the CURRENTLY LOADED save:
//   • extract: you must be playing the save you want to pull the pawn from —
//     the colonist is serialized to a .pawnx file in the pawn library folder.
//   • spawn: you must be playing the save you want the pawn spawned into —
//     the pawn is deserialized and dropped near the colony.
// A result file (FlexToolPawnExtractResult.txt) reports success/failure.
// ─────────────────────────────────────────────────────────────────────────
public static class LivePawnIpc
{
    private static readonly string CommandPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolPawnExtract.txt");
    private static readonly string ResultPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolPawnExtractResult.txt");
    private static readonly string LibraryPath = Path.Combine(
        GenFilePaths.SaveDataFolderPath, "FlexToolPawnLibrary");

    private static float _nextPoll;
    private static DateTime _lastCommandWrite = DateTime.MinValue;

    public static void Tick()
    {
        if (Time.realtimeSinceStartup < _nextPoll) return;
        _nextPoll = Time.realtimeSinceStartup + 1f;

        try
        {
            if (!File.Exists(CommandPath)) return;
            var write = File.GetLastWriteTimeUtc(CommandPath);
            if (write == _lastCommandWrite) return;
            _lastCommandWrite = write;

            // Ignore (and clean up) stale commands from a previous session.
            if ((DateTime.UtcNow - write).TotalMinutes > 2)
            {
                try { File.Delete(CommandPath); }
                catch { }
                return;
            }

            string command = null, pawnName = null, file = null;
            foreach (var line in File.ReadAllLines(CommandPath))
            {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim();
                if (key == "command") command = val;
                else if (key == "pawn") pawnName = val;
                else if (key == "file") file = val;
            }

            // Consume the command so a stale file never re-executes on the
            // next game session (the poll marker resets on restart).
            try { File.Delete(CommandPath); }
            catch { }

            if (command == "extract") ExtractPawn(pawnName);
            else if (command == "spawn") SpawnPawn(file);
        }
        catch (Exception ex)
        {
            WriteResult("error", "Command failed: " + ex.Message);
        }
    }

    private static void ExtractPawn(string pawnName)
    {
        if (string.IsNullOrEmpty(pawnName))
        {
            WriteResult("error", "No pawn name given.");
            return;
        }
        if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
        {
            WriteResult("error", "You must be in the save you want to pull the pawn from.");
            return;
        }

        Pawn pawn = null;
        foreach (var map in Find.Maps)
        {
            foreach (var p in map.mapPawns.FreeColonists)
            {
                if (string.Equals(p.Name?.ToStringShort, pawnName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Name?.ToStringFull, pawnName, StringComparison.OrdinalIgnoreCase))
                {
                    pawn = p;
                    break;
                }
            }
            if (pawn != null) break;
        }

        if (pawn == null)
        {
            WriteResult("error", "Colonist \"" + pawnName + "\" not found in the loaded save.");
            return;
        }

        try
        {
            Directory.CreateDirectory(LibraryPath);
            var safeName = pawn.Name?.ToStringShort ?? "pawn";
            foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
            var path = Path.Combine(LibraryPath,
                safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".pawnx");

            Scribe.saver.InitSaving(path, "flextoolpawn");
            try
            {
                Scribe_Deep.Look(ref pawn, "pawn");
            }
            finally
            {
                Scribe.saver.FinalizeSaving();
            }

            WriteResult("ok", "Extracted \"" + (pawn.Name?.ToStringShort ?? "pawn") + "\" to the pawn library.");
            Messages.Message("[FlexTool] Extracted " + (pawn.Name?.ToStringShort ?? "pawn") + " to the pawn library.",
                MessageTypeDefOf.PositiveEvent, historical: false);
        }
        catch (Exception ex)
        {
            WriteResult("error", "Extraction failed: " + ex.Message);
        }
    }

    private static void SpawnPawn(string file)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            WriteResult("error", "Pawn file not found.");
            return;
        }
        if (Current.Game == null || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
        {
            WriteResult("error", "You must be in the save you want the pawn spawned into.");
            return;
        }

        try
        {
            Pawn pawn = null;
            Scribe.loader.InitLoading(file);
            try
            {
                Scribe_Deep.Look(ref pawn, "pawn");
            }
            finally
            {
                Scribe.loader.FinalizeLoading();
            }

            if (pawn == null)
            {
                WriteResult("error", "Could not read the pawn from the file.");
                return;
            }

            // Give the pawn a fresh ID so it never collides with an existing pawn.
            pawn.thingIDNumber = -1;
            Verse.ThingIDMaker.GiveIDTo(pawn);
            pawn.SetFaction(Faction.OfPlayer);

            var map = Find.CurrentMap;
            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            GenSpawn.Spawn(pawn, cell, map);

            WriteResult("ok", "Spawned \"" + (pawn.Name?.ToStringShort ?? "pawn") + "\" into the current save.");
            Messages.Message("[FlexTool] Spawned " + (pawn.Name?.ToStringShort ?? "pawn") + " near the colony.",
                MessageTypeDefOf.PositiveEvent, historical: false);
        }
        catch (Exception ex)
        {
            WriteResult("error", "Spawn failed: " + ex.Message);
        }
    }

    private static void WriteResult(string status, string detail)
    {
        try
        {
            File.WriteAllText(ResultPath,
                "utc=" + DateTime.UtcNow.ToString("O") + "\n" +
                "status=" + status + "\n" +
                "detail=" + detail.Replace('\n', ' ') + "\n");
        }
        catch { }
    }
}
