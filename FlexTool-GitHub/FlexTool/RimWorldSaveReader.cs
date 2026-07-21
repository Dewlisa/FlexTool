using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace FlexTool;

/// <summary>
/// Reads colonist data from the most recently modified RimWorld save file.
/// Supports save formats from RimWorld 1.3 through 1.6.
/// Polling-based: only re-parses when the file's write time changes.
/// </summary>
public static class RimWorldSaveReader
{
    // %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves
    public static readonly string SavesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow",
        "Ludeon Studios", "RimWorld by Ludeon Studios", "Saves");

    /// <summary>Root of the RimWorld user-data folder.</summary>
    public static readonly string UserDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow",
        "Ludeon Studios", "RimWorld by Ludeon Studios");

    public static readonly string ModsConfigPath = Path.Combine(UserDataPath, "Config", "ModsConfig.xml");

    private static readonly string AutoLoadFilePath = Path.Combine(UserDataPath, "FlexToolAutoLoad.txt");
    private static readonly string CurrentSaveFilePath = Path.Combine(UserDataPath, "FlexToolCurrentSave.txt");
    private static readonly string PerfDataPath = Path.Combine(UserDataPath, "FlexToolPerf.xml");
    private static readonly string PawnEditPath = Path.Combine(UserDataPath, "FlexToolPawnEdit.xml");
    private static readonly string PawnEditResultPath = Path.Combine(UserDataPath, "FlexToolPawnEditResult.xml");
    private const string ModPackageId = "flextool.autoload";
    private const string SpeedModPackageId = "flextool.speedmod";
    private const string CheatsModPackageId = "flextool.cheatsmod";
    private const string PerfModPackageId = "flextool.perfmod";
    private const string DebugInfoModPackageId = "flextool.debuginfomod";
    private const string FpsOptimizerPackageId = "flextool.fpsoptimizer";
    private const string TillDeathModPackageId = "flextool.tilldeathmod";
    private const string KeepItTogetherModPackageId = "flextool.keepittogethermod";
    private const string HarmonyPackageId = "brrainz.harmony";

    /// <summary>IPC protocol version shared between FlexTool and the in-game Harmony mod.</summary>
    public const string IpcProtocolVersion = "1";

    private static string? _lastFile;
    private static DateTime _lastWriteTime;
    private static string _cachedGameVersion = "Unknown";
    private static List<string> _cachedDlcs = [];

    /// <summary>Game version string from the most recently parsed save.</summary>
    public static string GameVersion => _cachedGameVersion;

    /// <summary>DLC names detected in the most recently parsed save.</summary>
    public static IReadOnlyList<string> DetectedDlcs => _cachedDlcs;

    // ──────────────────────────────────────────────────────────────
    // Save file metadata
    // ──────────────────────────────────────────────────────────────

    public sealed class SaveFileInfo
    {
        public string FileName { get; init; } = "";
        public string FilePath { get; init; } = "";
        public string GameVersion { get; init; } = "Unknown";
        public string ColonyName { get; init; } = "";
        public int DaysSurvived { get; init; }
        public long FileSizeBytes { get; init; }
        public DateTime LastModified { get; init; }
        public int ModCount { get; init; }
        public int ColonistCount { get; init; }
        public string Seed { get; init; } = "";
    }

    private static readonly CacheEntry<List<SaveFileInfo>> _saveDetailsCache = new(TimeSpan.MaxValue);

    /// <summary>
    /// Returns metadata for every save file, ordered by most recently modified.
    /// Results are cached and only re-parsed when the file list or latest timestamp changes.
    /// </summary>
    public static List<SaveFileInfo> GetSaveDetails()
    {
        if (!Directory.Exists(SavesPath)) return [];

        try
        {
            var files = Directory.EnumerateFiles(SavesPath, "*.rws", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();

            var key = files.Count > 0
                ? $"{files.Count}_{File.GetLastWriteTime(files[0]).Ticks}"
                : "empty";

            if (_saveDetailsCache.TryGet(key, out var cached))
                return cached;

            var result = files
                .Select(ReadSaveMetadata)
                .Where(s => s is not null)
                .Cast<SaveFileInfo>()
                .ToList();
            _saveDetailsCache.Set(result, key);

            return result;
        }
        catch
        {
            _saveDetailsCache.TryGet(out var fallback);
            return fallback ?? [];
        }
    }

    private static SaveFileInfo? ReadSaveMetadata(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            string version = "Unknown";
            int days = 0;
            string colonyName = "";
            int modCount = 0;
            int colonistCount = 0;
            string seed = "";

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = System.Xml.XmlReader.Create(fs);

            bool foundVersion = false;
            bool foundTicks = false;
            bool foundColony = false;
            bool foundMods = false;
            bool foundSeed = false;

            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;

                switch (reader.Name)
                {
                    case "gameVersion" when !foundVersion:
                        var raw = reader.ReadElementContentAsString();
                        version = raw.Split(' ')[0] is { Length: > 0 } v ? v : "Unknown";
                        foundVersion = true;
                        break;

                    case "modIds" when !foundMods:
                        using (var modSub = reader.ReadSubtree())
                        {
                            while (modSub.Read())
                            {
                                if (modSub.NodeType == System.Xml.XmlNodeType.Element && modSub.Name == "li")
                                    modCount++;
                            }
                        }
                        foundMods = true;
                        break;

                    case "ticksGame" when !foundTicks:
                        if (long.TryParse(reader.ReadElementContentAsString(), out long ticks))
                            days = (int)(ticks / 60000);
                        foundTicks = true;
                        break;

                    case "mapRandomSeed" when !foundSeed:
                        seed = reader.ReadElementContentAsString();
                        foundSeed = true;
                        break;

                    case "li" when !foundColony
                        && reader.GetAttribute("Class") is string cls
                        && (cls == "RimWorld.Settlement" || cls == "Settlement"):
                        using (var sub = reader.ReadSubtree())
                        {
                            string? label = null;
                            string? faction = null;
                            while (sub.Read())
                            {
                                if (sub.NodeType != System.Xml.XmlNodeType.Element) continue;
                                if (sub.Name is "label" or "nameInt" && label is null) label = sub.ReadElementContentAsString();
                                else if (sub.Name == "faction" && faction is null) faction = sub.ReadElementContentAsString();
                                if (label is not null && faction is not null) break;
                            }
                            if (faction == "Faction_0" && !string.IsNullOrEmpty(label))
                            {
                                colonyName = label;
                                foundColony = true;
                            }
                        }
                        break;

                    // Count colonists in spawned pawns
                    case "pawnsSpawned":
                        using (var pawnSub = reader.ReadSubtree())
                        {
                            while (pawnSub.Read())
                            {
                                if (pawnSub.NodeType != System.Xml.XmlNodeType.Element) continue;
                                if (pawnSub.Name == "li")
                                {
                                    using var pawnItem = pawnSub.ReadSubtree();
                                    string? def = null;
                                    string? kindDef = null;
                                    while (pawnItem.Read())
                                    {
                                        if (pawnItem.NodeType != System.Xml.XmlNodeType.Element) continue;
                                        if (pawnItem.Name == "def" && def is null) def = pawnItem.ReadElementContentAsString();
                                        else if (pawnItem.Name == "kindDef" && kindDef is null) kindDef = pawnItem.ReadElementContentAsString();
                                        if (def is not null && kindDef is not null) break;
                                    }
                                    if (def == "Human" && kindDef is not null &&
                                        kindDef.StartsWith("Colonist", StringComparison.OrdinalIgnoreCase))
                                        colonistCount++;
                                }
                            }
                        }
                        break;

                    // Stop scanning after maps
                    case "maps" when foundVersion && foundTicks && foundMods:
                        // We need to scan inside maps for colonist count, so don't break here
                        break;
                }
            }

            return new SaveFileInfo
            {
                FileName = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                GameVersion = version,
                ColonyName = colonyName,
                DaysSurvived = days,
                FileSizeBytes = fi.Length,
                LastModified = fi.LastWriteTime,
                ModCount = modCount,
                ColonistCount = colonistCount,
                Seed = seed
            };
        }
        catch { return null; }
    }

    // ──────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────

    /// <summary>Default backup directory next to the Saves folder.</summary>
    public static readonly string BackupsPath = Path.Combine(UserDataPath, "FlexToolBackups");

    /// <summary>
    /// Creates a timestamped backup copy of the given save file.
    /// An optional label (e.g. "FlexTool", "User", "Emergency", "Launch") is embedded
    /// in the file name so the origin of every backup is visible in the UI.
    /// Returns the backup file path, or null on failure.
    /// </summary>
    public static string? BackupSaveFile(string saveFilePath, string label = "User")
    {
        try
        {
            if (!File.Exists(saveFilePath)) return null;
            Directory.CreateDirectory(BackupsPath);

            var baseName = Path.GetFileNameWithoutExtension(saveFilePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeLabel = SanitizeLabel(label);
            var backupName = $"{baseName}__{safeLabel}__{timestamp}.rws";
            var dest = Path.Combine(BackupsPath, backupName);

            File.Copy(saveFilePath, dest, overwrite: false);
            return dest;
        }
        catch { return null; }
    }

    private static string SanitizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "User";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = label.Trim().Where(c => !invalid.Contains(c) && c != '_').ToArray();
        return chars.Length > 0 ? new string(chars) : "User";
    }

    /// <summary>
    /// Parses a backup file name into (original save name, label, timestamp text).
    /// Supports both the new "Name__Label__yyyyMMdd_HHmmss" format and the
    /// legacy "Name_yyyyMMdd_HHmmss" format (label defaults to "User").
    /// </summary>
    public static (string BaseName, string Label) ParseBackupName(string backupFilePath)
    {
        var name = Path.GetFileNameWithoutExtension(backupFilePath);

        // New format: Name__Label__timestamp
        var doubleParts = name.Split(["__"], StringSplitOptions.None);
        if (doubleParts.Length >= 3)
            return (string.Join("__", doubleParts.Take(doubleParts.Length - 2)), doubleParts[^2]);

        // Legacy format: Name_yyyyMMdd_HHmmss
        var parts = name.Split('_');
        if (parts.Length >= 3)
            return (string.Join("_", parts.Take(parts.Length - 2)), "User");

        return (name, "User");
    }

    /// <summary>
    /// Returns all backup files, ordered by most recent first.
    /// </summary>
    public static List<SaveFileInfo> GetBackups()
    {
        if (!Directory.Exists(BackupsPath)) return [];
        try
        {
            return Directory.EnumerateFiles(BackupsPath, "*.rws")
                .OrderByDescending(File.GetLastWriteTime)
                .Select(f =>
                {
                    var fi = new FileInfo(f);
                    return new SaveFileInfo
                    {
                        FileName = Path.GetFileNameWithoutExtension(f),
                        FilePath = f,
                        FileSizeBytes = fi.Length,
                        LastModified = fi.LastWriteTime
                    };
                })
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>
    /// Restores a backup by copying it into the saves folder.
    /// Never overwrites an existing save: when the original name is taken the
    /// restored file gets a " (Restored)" / " (Restored 2)" suffix instead.
    /// Returns the destination path, or null on failure.
    /// </summary>
    public static string? RestoreBackup(string backupFilePath)
    {
        try
        {
            if (!File.Exists(backupFilePath)) return null;
            Directory.CreateDirectory(SavesPath);

            var (restoredName, _) = ParseBackupName(backupFilePath);

            var dest = Path.Combine(SavesPath, restoredName + ".rws");
            if (File.Exists(dest))
            {
                int counter = 1;
                do
                {
                    var suffix = counter == 1 ? " (Restored)" : $" (Restored {counter})";
                    dest = Path.Combine(SavesPath, restoredName + suffix + ".rws");
                    counter++;
                } while (File.Exists(dest));
            }

            File.Copy(backupFilePath, dest, overwrite: false);
            InvalidateSaveCache();
            return dest;
        }
        catch { return null; }
    }

    /// <summary>
    /// Renames a save file within the Saves folder. Returns the new path, or null on failure
    /// (including when the target name already exists).
    /// </summary>
    public static string? RenameSaveFile(string saveFilePath, string newName)
    {
        try
        {
            if (!File.Exists(saveFilePath)) return null;
            var safe = SanitizeFileName(newName);
            if (string.IsNullOrWhiteSpace(safe)) return null;

            var dest = Path.Combine(Path.GetDirectoryName(saveFilePath)!, safe + ".rws");
            if (File.Exists(dest)) return null;

            File.Move(saveFilePath, dest);
            InvalidateSaveCache();
            return dest;
        }
        catch { return null; }
    }

    /// <summary>
    /// Renames a backup's base save name while preserving its label and timestamp.
    /// Returns the new path, or null on failure.
    /// </summary>
    public static string? RenameBackup(string backupFilePath, string newBaseName)
    {
        try
        {
            if (!File.Exists(backupFilePath)) return null;
            var safe = SanitizeFileName(newBaseName);
            if (string.IsNullOrWhiteSpace(safe)) return null;

            var name = Path.GetFileNameWithoutExtension(backupFilePath);
            var (_, label) = ParseBackupName(backupFilePath);

            // Preserve the trailing timestamp when present
            string timestamp;
            var doubleParts = name.Split(["__"], StringSplitOptions.None);
            if (doubleParts.Length >= 3)
                timestamp = doubleParts[^1];
            else
            {
                var parts = name.Split('_');
                timestamp = parts.Length >= 3
                    ? $"{parts[^2]}_{parts[^1]}"
                    : DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            var dest = Path.Combine(BackupsPath, $"{safe}__{SanitizeLabel(label)}__{timestamp}.rws");
            if (File.Exists(dest)) return null;

            File.Move(backupFilePath, dest);
            return dest;
        }
        catch { return null; }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray());
    }

    /// <summary>
    /// Rewrites the <gameVersion> tag inside a save file to the currently
    /// installed game version so older saves load without the version warning.
    /// A backup labelled "FlexTool" is created first. Returns true on success.
    /// </summary>
    public static bool ConvertSaveToCurrentVersion(string saveFilePath)
    {
        try
        {
            if (!File.Exists(saveFilePath)) return false;

            var currentVersion = GetInstalledGameVersion();
            if (string.IsNullOrEmpty(currentVersion)) return false;

            BackupSaveFile(saveFilePath, "FlexTool");

            var text = File.ReadAllText(saveFilePath);
            var updated = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"<gameVersion>[^<]*</gameVersion>",
                $"<gameVersion>{currentVersion}</gameVersion>",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(5));

            if (updated == text) return false;
            File.WriteAllText(saveFilePath, updated);
            InvalidateSaveCache();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Reads the installed game's version from Version.txt next to the exe.</summary>
    public static string? GetInstalledGameVersion()
    {
        try
        {
            var gameExe = FindGameExePath();
            if (gameExe is null) return null;
            var versionFile = Path.Combine(Path.GetDirectoryName(gameExe)!, "Version.txt");
            if (!File.Exists(versionFile)) return null;
            var v = File.ReadAllText(versionFile).Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    /// <summary>Forces the save detail cache to refresh on the next call.</summary>
    public static void InvalidateSaveCache() => _saveDetailsCache.Invalidate();

    /// <summary>
    /// Permanently deletes a save file and invalidates the cache.
    /// Returns true on success, false on failure.
    /// </summary>
    public static bool DeleteSaveFile(string saveFilePath)
    {
        try
        {
            if (!File.Exists(saveFilePath)) return false;
            File.Delete(saveFilePath);
            InvalidateSaveCache();
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Permanently deletes a backup file.
    /// Returns true on success, false on failure.
    /// </summary>
    public static bool DeleteBackup(string backupFilePath)
    {
        try
        {
            if (!File.Exists(backupFilePath)) return false;
            File.Delete(backupFilePath);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Imports a .rws file by copying it into the Saves folder.
    /// Returns the destination path, or null on failure.
    /// </summary>
    public static string? ImportSaveFile(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return null;
            Directory.CreateDirectory(SavesPath);

            var fileName = Path.GetFileName(sourcePath);
            var dest = Path.Combine(SavesPath, fileName);

            // Avoid overwriting — append a number if needed
            if (File.Exists(dest))
            {
                var baseName = Path.GetFileNameWithoutExtension(sourcePath);
                var ext = Path.GetExtension(sourcePath);
                int counter = 1;
                do
                {
                    dest = Path.Combine(SavesPath, $"{baseName}_{counter}{ext}");
                    counter++;
                } while (File.Exists(dest));
            }

            File.Copy(sourcePath, dest);
            InvalidateSaveCache();
            return dest;
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns the colonists from the latest save file when it has changed since
    /// the last poll. Returns ([], false) when nothing has changed or no save exists.
    /// </summary>
    public static (List<PawnData> pawns, bool changed) Poll()
    {
        if (!Directory.Exists(SavesPath))
            return ([], false);

        string? latest;
        try
        {
            latest = Directory.EnumerateFiles(SavesPath, "*.rws", SearchOption.AllDirectories)
                               .OrderByDescending(File.GetLastWriteTime)
                               .FirstOrDefault();
        }
        catch { return ([], false); }

        if (latest is null)
            return ([], false);

        var writeTime = File.GetLastWriteTime(latest);

        if (latest == _lastFile && writeTime == _lastWriteTime)
            return ([], false);

        _lastFile = latest;
        _lastWriteTime = writeTime;

        return (ParseSave(latest), true);
    }

    /// <summary>
    /// Returns the file names (without extension) of all save files,
    /// ordered by most recently modified first.
    /// </summary>
    public static List<string> GetSaveFileNames()
    {
        if (!Directory.Exists(SavesPath)) return [];
        try
        {
            return Directory.EnumerateFiles(SavesPath, "*.rws", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n is not null)
                .Cast<string>()
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns the name (without extension) of the most recently modified save file,
    /// optionally excluding autosave files.
    /// </summary>
    public static string? GetMostRecentSaveName(bool excludeAutosaves = false)
    {
        if (!Directory.Exists(SavesPath)) return null;
        try
        {
            var latest = Directory.EnumerateFiles(SavesPath, "*.rws", SearchOption.AllDirectories)
                .Where(f => !excludeAutosaves ||
                    !Path.GetFileNameWithoutExtension(f)!
                        .StartsWith("Autosave", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            return latest != null ? Path.GetFileNameWithoutExtension(latest) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Searches the Saves directory tree for a save file by name and returns its full path.
    /// </summary>
    public static string? FindSaveFilePath(string saveName)
    {
        if (!Directory.Exists(SavesPath)) return null;
        try
        {
            return Directory.EnumerateFiles(SavesPath, saveName + ".rws", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>
    /// Attempts to locate the RimWorld executable via Steam registry key,
    /// Steam library folders, or common installation paths.
    /// </summary>
    public static string? FindGameExePath()
    {
        string? steamPath = null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                         ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            steamPath = key?.GetValue("InstallPath") as string;
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

        // Check main Steam install
        if (steamPath != null)
        {
            var candidate = Path.Combine(steamPath, "steamapps", "common", "RimWorld", "RimWorldWin64.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // Check additional Steam library folders (games installed on other drives)
        if (steamPath != null)
        {
            try
            {
                var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (var line in File.ReadLines(vdf))
                    {
                        var trimmed = line.Trim();
                        if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var parts = trimmed.Split('"');
                        if (parts.Length < 4) continue;
                        var libPath = parts[3].Replace("\\\\", "\\");
                        var candidate = Path.Combine(libPath, "steamapps", "common", "RimWorld", "RimWorldWin64.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "RimWorld", "RimWorldWin64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Steam", "steamapps", "common", "RimWorld", "RimWorldWin64.exe"),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Returns the path to steam.exe, or null if not found.
    /// </summary>
    public static string? FindSteamExePath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                         ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var steamPath = key?.GetValue("InstallPath") as string;
            if (steamPath != null)
            {
                var exe = Path.Combine(steamPath, "steam.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe"),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    // ──────────────────────────────────────────────────────────────
    // Version detection
    // ──────────────────────────────────────────────────────────────

    private readonly record struct SaveVersion(int Major, int Minor)
    {
        // 1.4+ introduced Biotech (biological vs chronological age, genes)
        public bool Is14OrLater => Major > 1 || (Major == 1 && Minor >= 4);
        // 1.5+ changed <part> inside hediffs to a nested element
        public bool Is15OrLater => Major > 1 || (Major == 1 && Minor >= 5);
    }

    private static SaveVersion DetectVersion(XDocument doc)
    {
        // Format examples: "1.3.3200 rev123", "1.4.3613", "1.5.4153 rev7", "1.6.0"
        var raw = doc.Root?.Element("meta")?.Element("gameVersion")?.Value ?? "";
        var parts = raw.Split('.', ' ');
        int.TryParse(parts.ElementAtOrDefault(0), out int major);
        int.TryParse(parts.ElementAtOrDefault(1), out int minor);
        return new SaveVersion(major == 0 ? 1 : major, minor);
    }

    private static readonly Dictionary<string, string> KnownDlcIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ludeon.rimworld.royalty"]   = "Royalty",
        ["ludeon.rimworld.ideology"] = "Ideology",
        ["ludeon.rimworld.biotech"]  = "Biotech",
        ["ludeon.rimworld.anomaly"]  = "Anomaly"
    };

    private static void CacheMetadata(XDocument doc)
    {
        var meta = doc.Root?.Element("meta");

        var rawVersion = meta?.Element("gameVersion")?.Value ?? "";
        _cachedGameVersion = rawVersion.Split(' ')[0] is { Length: > 0 } v ? v : "Unknown";

        var modIds = meta?.Element("modIds")?.Elements("li")
            .Select(e => e.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        _cachedDlcs = KnownDlcIds
            .Where(kv => modIds.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();
    }

    /// <summary>
    /// Detects DLCs actually installed on disk by looking at the game's Data folder
    /// (each official expansion ships as a subfolder, e.g. Data\Royalty).
    /// This reflects true ownership regardless of what a save has enabled.
    /// Returns null when the game install can't be located.
    /// </summary>
    public static List<string>? GetInstalledDlcs()
    {
        try
        {
            var gameExe = FindGameExePath();
            if (gameExe is null) return null;

            var dataDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Data");
            if (!Directory.Exists(dataDir)) return null;

            string[] allDlcs = ["Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey"];
            return allDlcs
                .Where(d => Directory.Exists(Path.Combine(dataDir, d)))
                .ToList();
        }
        catch { return null; }
    }

    // ──────────────────────────────────────────────────────────────
    // Save-level parsing
    // ──────────────────────────────────────────────────────────────

    private static List<PawnData> ParseSave(string path)
    {
        try
        {
            XDocument doc;
            // Share ReadWrite so we never conflict with RimWorld's own file handle
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                doc = XDocument.Load(fs);

            var saveVer = DetectVersion(doc);
            CacheMetadata(doc);

            // Current game tick
            long.TryParse(
                doc.Descendants("tickManager").FirstOrDefault()?.Element("ticksGame")?.Value,
                out long currentTick);

            var pawns = new List<PawnData>();

            foreach (var mapEl in doc.Descendants("maps").Elements("li"))
            {
                foreach (var pawnEl in mapEl.Descendants("pawnsSpawned").Elements("li"))
                {
                    if (!IsPlayerColonist(pawnEl))
                        continue;

                    var pawn = ParsePawn(pawnEl, saveVer, currentTick);
                    if (pawn is not null)
                        pawns.Add(pawn);
                }
            }

            return pawns;
        }
        catch { return []; }
    }

    // ──────────────────────────────────────────────────────────────
    // Colonist identification
    // ──────────────────────────────────────────────────────────────

    private static bool IsPlayerColonist(XElement pawnEl)
    {
        // Must be a human pawn
        if (pawnEl.Element("def")?.Value != "Human")
            return false;

        var kindDef = pawnEl.Element("kindDef")?.Value ?? "";

        // Accept standard colonists and mod variants (ColonistWithBed, etc.)
        // Also accept Slaves added by Ideology DLC (1.3+)
        bool isColonistKind =
            kindDef.StartsWith("Colonist", StringComparison.OrdinalIgnoreCase) ||
            kindDef.Equals("Slave", StringComparison.OrdinalIgnoreCase);

        // Fallback for heavily modded games: <playerSettings> marks player-owned pawns
        bool hasPlayerSettings = pawnEl.Element("playerSettings") is not null;

        if (!isColonistKind && !hasPlayerSettings)
            return false;

        // Exclude actual prisoners
        if (pawnEl.Element("guest")?.Element("isPrisoner")?.Value
                  .Equals("True", StringComparison.OrdinalIgnoreCase) == true)
            return false;

        return true;
    }

    // ──────────────────────────────────────────────────────────────
    // Pawn parsing
    // ──────────────────────────────────────────────────────────────

    private static PawnData? ParsePawn(XElement el, SaveVersion ver, long currentTick)
    {
        try
        {
            var pawn = new PawnData();
            ParseName(el, pawn);
            ParseGender(el, pawn);
            ParseAge(el, pawn, ver, currentTick);
            ParseStory(el, pawn, ver);
            ParseSkills(el, pawn);
            ParseHealth(el, pawn, ver);
            ParseGear(el, pawn);
            return pawn;
        }
        catch { return null; }
    }

    // Gear ──────────────────────────────────────────────────────────

    private static void ParseGear(XElement el, PawnData pawn)
    {
        try
        {
            void AddThings(XElement? container, string kind)
            {
                if (container is null) return;
                foreach (var thing in container.Descendants("li").Where(t => t.Element("def") is not null))
                {
                    var def = thing.Element("def")?.Value ?? "";
                    if (string.IsNullOrEmpty(def)) continue;
                    pawn.Gear.Add(new GearItem
                    {
                        Name = FormatDefName(def),
                        Kind = kind,
                        HitPoints = int.TryParse(thing.Element("health")?.Value, out var hp) ? hp : -1,
                        Quality = thing.Element("quality")?.Value ?? ""
                    });
                }
            }

            AddThings(el.Element("apparel")?.Element("wornApparel")?.Element("innerList"), "Apparel");
            AddThings(el.Element("equipment")?.Element("equipment")?.Element("innerList"), "Weapon");
            AddThings(el.Element("inventory")?.Element("innerContainer")?.Element("innerList"), "Inventory");
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>Converts "Apparel_BasicShirt" → "Basic Shirt".</summary>
    private static string FormatDefName(string def)
    {
        var idx = def.IndexOf('_');
        var name = idx >= 0 && idx < def.Length - 1 ? def[(idx + 1)..] : def;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    // Name ──────────────────────────────────────────────────────────

    private static void ParseName(XElement el, PawnData pawn)
    {
        var n = el.Element("name");
        pawn.FirstName = n?.Element("first")?.Value ?? "";
        pawn.Nickname  = n?.Element("nick")?.Value  ?? "";
        pawn.LastName  = n?.Element("last")?.Value  ?? "";
    }

    // Gender ────────────────────────────────────────────────────────

    private static void ParseGender(XElement el, PawnData pawn) =>
        pawn.Gender = el.Element("gender")?.Value == "Female" ? "Female" : "Male";

    // Age ───────────────────────────────────────────────────────────
    // 1 RimWorld year = 3,600,000 ticks

    private const long TicksPerYear = 3_600_000L;

    private static void ParseAge(XElement el, PawnData pawn, SaveVersion ver, long currentTick)
    {
        var ageEl = el.Element("ageTracker");
        if (ageEl is null) return;

        // Biological age — present and consistent in all versions
        if (long.TryParse(ageEl.Element("ageBiologicalTicksInt")?.Value, out long bioTicks))
            pawn.BioAge = (int)(bioTicks / TicksPerYear);

        // Chronological age
        // 1.4+ (Biotech): bio and chrono can diverge for xenotypes/mechanitors.
        // Derive from birthAbsTicks + current game tick when both are available.
        if (ver.Is14OrLater
            && currentTick > 0
            && long.TryParse(ageEl.Element("birthAbsTicks")?.Value, out long birthTick))
        {
            long chronoTicks = currentTick - birthTick;
            if (chronoTicks > 0)
            {
                pawn.ChronoAge = (int)(chronoTicks / TicksPerYear);
                return;
            }
        }

        // 1.3 or fallback: chronological == biological
        pawn.ChronoAge = pawn.BioAge;
    }

    // Story / appearance / traits ───────────────────────────────────

    private static void ParseStory(XElement el, PawnData pawn, SaveVersion ver)
    {
        var story = el.Element("story");
        if (story is null) return;

        pawn.Childhood = story.Element("childhood")?.Value ?? "";
        pawn.Adulthood = story.Element("adulthood")?.Value ?? "";
        pawn.HeadType  = story.Element("headType")?.Value  ?? "";
        pawn.Hair      = story.Element("hairDef")?.Value   ?? "";
        pawn.Beard     = story.Element("beardDef")?.Value  ?? "NoBeard";

        // Body type: 1.3/1.4 stored a gender prefix ("Male_Thin", "Female_Hulk").
        // 1.5/1.6 dropped the prefix. Normalize to the unprefixed form.
        pawn.BodyType = NormalizeBodyType(story.Element("bodyType")?.Value ?? "");

        // Skin colour — named string ("Light", "Dark", etc.) in all versions
        pawn.SkinColor = story.Element("skinColorBase")?.Value ?? "";

        // Hair colour — plain named string in 1.3; RGBA float struct in 1.4+
        pawn.HairColor = ParseColorElement(story.Element("hairColor"), ver);

        // Traits
        foreach (var t in story.Descendants("allTraits").Elements("li"))
        {
            var def = t.Element("def")?.Value;
            if (!string.IsNullOrEmpty(def))
                pawn.Traits.Add(def);
        }
    }

    // Skills ────────────────────────────────────────────────────────
    // Consistent across all versions:
    // <skills><skills><li><def/><level/><passion/></li>...</skills></skills>

    private static void ParseSkills(XElement el, PawnData pawn)
    {
        var innerSkills = el.Element("skills")?.Element("skills");
        if (innerSkills is null) return;

        foreach (var li in innerSkills.Elements("li"))
        {
            var def = li.Element("def")?.Value ?? "";
            if (string.IsNullOrEmpty(def)) continue;

            int.TryParse(li.Element("level")?.Value, out int level);
            int passion = li.Element("passion")?.Value switch
            {
                "Major" => 2,
                "Minor" => 1,
                _       => 0
            };

            pawn.Skills.Add(new SkillData { Name = def, Level = level, Passion = passion });
        }
    }

    // Health ────────────────────────────────────────────────────────
    // 1.3/1.4: <part>BodyPartDefName</part>  (plain string)
    // 1.5/1.6: <part><def>BodyPartDefName</def>...</part>  (nested element)

    private static void ParseHealth(XElement el, PawnData pawn, SaveVersion ver)
    {
        var hediffs = el.Element("health")?.Element("hediffSet")?.Element("hediffs");
        if (hediffs is null) return;

        foreach (var li in hediffs.Elements("li"))
        {
            var def = li.Element("def")?.Value ?? "";
            if (string.IsNullOrEmpty(def)) continue;

            var partEl = li.Element("part");
            // Try nested <def> first (1.5/1.6), fall back to plain value (1.3/1.4)
            var part = partEl?.Element("def")?.Value ?? partEl?.Value ?? "";

            pawn.HealthConditions.Add(new HealthCondition { Condition = def, BodyPart = part });
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips the gender prefix that 1.3/1.4 prepended to body-type def names
    /// (e.g. "Male_Thin" → "Thin", "Female_Hulk" → "Hulk").
    /// 1.5/1.6 already store the bare name; this is a no-op for them.
    /// </summary>
    private static string NormalizeBodyType(string raw)
    {
        foreach (var prefix in (ReadOnlySpan<string>)["Male_", "Female_"])
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return raw[prefix.Length..];
        }
        return raw;
    }

    /// <summary>
    /// Parses a colour stored either as a named string (1.3) or as an RGBA
    /// float struct with child elements r/g/b/a (1.4+).
    /// Returns "#RRGGBB" for RGBA input, or the raw string for named colours.
    /// </summary>
    private static string ParseColorElement(XElement? colorEl, SaveVersion ver)
    {
        if (colorEl is null) return "";

        if (ver.Is14OrLater && colorEl.HasElements)
        {
            static int ToByte(XElement? e) =>
                double.TryParse(e?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    ? Math.Clamp((int)Math.Round(v * 255), 0, 255)
                    : 0;

            return $"#{ToByte(colorEl.Element("r")):X2}" +
                   $"{ToByte(colorEl.Element("g")):X2}" +
                   $"{ToByte(colorEl.Element("b")):X2}";
        }

        return colorEl.Value;
    }

    // ──────────────────────────────────────────────────────────────
    // AutoLoad mod deployment (IPC)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Deploys the FlexTool AutoLoad Harmony mod into the local Mods folder,
    /// ensures it is activated in ModsConfig.xml (after brrainz.harmony),
    /// and writes the IPC command file so the game loads the requested save.
    /// Call this before launching RimWorld.
    /// </summary>
    public static void PrepareAutoLoad(string saveName)
    {
        DeployAutoLoadMod();
        ActivateModInConfig();
        WriteAutoLoadCommand(saveName);
    }

    /// <summary>
    /// Clears the IPC command file so the game boots to the main menu.
    /// </summary>
    public static void ClearAutoLoadCommand()
    {
        try { if (File.Exists(AutoLoadFilePath)) File.Delete(AutoLoadFilePath); }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>
    /// Returns the save name the in-game mod reported as currently loaded,
    /// or null if the status file doesn't exist (game not running / mod not active).
    /// </summary>
    public static string? GetCurrentSaveFromGame()
    {
        try
        {
            if (!File.Exists(CurrentSaveFilePath)) return null;
            using var fs = new FileStream(CurrentSaveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var name = sr.ReadToEnd().Trim();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch { return null; }
    }

    /// <summary>Path of the AutoLoad mod folder inside the game's Mods directory, or null when the game isn't found.</summary>
    public static string? GetAutoLoadModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolAutoLoad");
    }

    public static bool IsAutoLoadModInstalled()
    {
        try
        {
            var modDir = GetAutoLoadModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.AutoLoadMod.dll"));
        }
        catch { return false; }
    }

    /// <summary>Deploys the AutoLoad mod on demand (same files used by PrepareAutoLoad). Returns true when installed.</summary>
    public static bool InstallAutoLoadMod()
    {
        DeployAutoLoadMod();
        return IsAutoLoadModInstalled();
    }

    /// <summary>
    /// Removes the AutoLoad mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemoveAutoLoadMod()
    {
        try
        {
            // Deactivate in ModsConfig.xml first so the game won't look for it
            if (File.Exists(ModsConfigPath))
            {
                var doc = XDocument.Load(ModsConfigPath);
                var activeMods = doc.Root?.Element("activeMods");
                var entry = activeMods?.Elements("li")
                    .FirstOrDefault(e => string.Equals(e.Value, ModPackageId, StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    entry.Remove();
                    doc.Save(ModsConfigPath);
                }
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

        try
        {
            var modDir = GetAutoLoadModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsAutoLoadModInstalled();
    }

    // ────────────────────────────────────────────────────────────
    // Harmony detection (required dependency for all FlexTool mods)
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// True when the Harmony mod (brrainz.harmony) is present either in the
    /// game's local Mods folder or in the Steam Workshop content folder.
    /// </summary>
    public static bool IsHarmonyInstalled()
    {
        try
        {
            // Check local Mods folder next to the game exe
            var gameExe = FindGameExePath();
            if (gameExe is not null)
            {
                var gameModsDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods");
                if (Directory.Exists(gameModsDir) && HasHarmonyAbout(gameModsDir))
                    return true;
            }

            // Check Steam Workshop folder(s)
            string? steamPath = null;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                             ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                steamPath = key?.GetValue("InstallPath") as string;
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

            if (steamPath is not null)
            {
                var workshopDir = Path.Combine(steamPath, "steamapps", "workshop", "content", "294100");
                if (Directory.Exists(workshopDir) && HasHarmonyAbout(workshopDir))
                    return true;

                // Additional Steam library folders
                var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (var line in File.ReadLines(vdf))
                    {
                        var trimmed = line.Trim();
                        if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
                        var parts = trimmed.Split('"');
                        if (parts.Length < 4) continue;
                        var libPath = parts[3].Replace(@"\\", @"\");
                        var libWorkshop = Path.Combine(libPath, "steamapps", "workshop", "content", "294100");
                        if (Directory.Exists(libWorkshop) && HasHarmonyAbout(libWorkshop))
                            return true;
                    }
                }
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        return false;
    }

    /// <summary>True when Harmony is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsHarmonyActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => e.Value.StartsWith(HarmonyPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    private static bool HasHarmonyAbout(string modsRoot)
    {
        foreach (var dir in Directory.EnumerateDirectories(modsRoot))
        {
            try
            {
                var about = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(about)) continue;
                var doc = XDocument.Load(about);
                var packageId = doc.Root?.Element("packageId")?.Value?.Trim();
                if (string.Equals(packageId, HarmonyPackageId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }
        return false;
    }

    private static void DeployAutoLoadMod()
    {
        // the user-data Mods folder is NOT scanned. Deploy next to the game exe.
        var gameExe = FindGameExePath();
        if (gameExe is null) return;

        var gameModsDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods");
        var modDir = Path.Combine(gameModsDir, "FlexToolAutoLoad");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        // Locate bundled files next to the running exe
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var srcDll = Path.Combine(appDir, "AutoLoadMod", "FlexTool.AutoLoadMod.dll");
        var srcAbout = Path.Combine(appDir, "AutoLoadMod", "About.xml");
        var destDll = Path.Combine(assembliesDir, "FlexTool.AutoLoadMod.dll");
        var destAbout = Path.Combine(aboutDir, "About.xml");

        // Copy each file, but skip gracefully if locked (game is running and
        // already loaded the mod — the existing copy is fine).
        TryCopyFile(srcDll, destDll);
        TryCopyFile(srcAbout, destAbout);
    }

    private static void TryCopyFile(string src, string dest)
    {
        if (!File.Exists(src)) return;
        try { File.Copy(src, dest, overwrite: true); }
        catch (IOException) { } // target locked by game — existing copy is fine
    }

    /// <summary>
    /// Re-copies every installed FlexTool in-game mod whose bundled DLL is newer
    /// than the deployed copy. Without this, mod updates shipped with the tool
    /// never reach the game after the initial install.
    /// </summary>
    public static void RefreshDeployedMods()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return;
        var gameModsDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods");
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // (bundled subfolder, deployed mod folder, dll name)
        (string Bundle, string ModFolder, string Dll)[] mods =
        [
            ("AutoLoadMod", "FlexToolAutoLoad", "FlexTool.AutoLoadMod.dll"),
            ("SpeedMod", "FlexToolSpeed", "FlexTool.SpeedMod.dll"),
            ("CheatsMod", "FlexToolCheats", "FlexTool.CheatsMod.dll"),
            ("PerfMod", "FlexToolPerf", "FlexTool.PerfMod.dll"),
            ("DebugInfoMod", "FlexToolDebugInfo", "FlexTool.DebugInfoMod.dll"),
            ("FPSOptimizer", "FlexToolFpsOptimizer", "FlexTool.FPSOptimizer.dll"),
        ];

        foreach (var (bundle, modFolder, dll) in mods)
        {
            try
            {
                var deployedDll = Path.Combine(gameModsDir, modFolder, "Assemblies", dll);
                if (!File.Exists(deployedDll)) continue; // not installed — nothing to refresh

                var srcDll = Path.Combine(appDir, bundle, dll);
                if (!File.Exists(srcDll)) continue;

                if (File.GetLastWriteTimeUtc(srcDll) > File.GetLastWriteTimeUtc(deployedDll))
                {
                    TryCopyFile(srcDll, deployedDll);
                    TryCopyFile(Path.Combine(appDir, bundle, "About.xml"),
                                Path.Combine(gameModsDir, modFolder, "About", "About.xml"));
                }
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Speed mod deployment (4x / 5x time controls)
    // ──────────────────────────────────────────────────────────────

    /// <summary>Returns the speed mod folder inside the game's Mods directory, or null if the game isn't found.</summary>
    public static string? GetSpeedModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolSpeed");
    }

    /// <summary>True when the speed mod DLL and About.xml are deployed in the game's Mods folder.</summary>
    public static bool IsSpeedModInstalled()
    {
        try
        {
            var modDir = GetSpeedModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.SpeedMod.dll"));
        }
        catch { return false; }
    }

    /// <summary>True when the speed mod is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsSpeedModActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => string.Equals(e.Value, SpeedModPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deploys the speed mod into the game's Mods folder (like the AutoLoad mod)
    /// and activates it in ModsConfig.xml. Returns true when the files are in place.
    /// </summary>
    public static bool InstallSpeedMod()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return false;

        var modDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolSpeed");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        TryCopyFile(Path.Combine(appDir, "SpeedMod", "FlexTool.SpeedMod.dll"),
                    Path.Combine(assembliesDir, "FlexTool.SpeedMod.dll"));
        TryCopyFile(Path.Combine(appDir, "SpeedMod", "About.xml"),
                    Path.Combine(aboutDir, "About.xml"));

        SetSpeedModActiveInConfig(active: true);
        return IsSpeedModInstalled();
    }

    /// <summary>
    /// Removes the speed mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemoveSpeedMod()
    {
        SetSpeedModActiveInConfig(active: false);

        try
        {
            var modDir = GetSpeedModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsSpeedModInstalled();
    }

    private static void SetSpeedModActiveInConfig(bool active)
    {
        if (!File.Exists(ModsConfigPath)) return;

        try
        {
            var doc = XDocument.Load(ModsConfigPath);
            var activeMods = doc.Root?.Element("activeMods");
            if (activeMods is null) return;

            var existing = activeMods.Elements("li")
                .FirstOrDefault(e => string.Equals(e.Value, SpeedModPackageId, StringComparison.OrdinalIgnoreCase));

            if (active)
            {
                if (existing is not null) return; // already active

                // Insert right after brrainz.harmony so Harmony is loaded first
                var harmonyEntry = activeMods.Elements("li")
                    .FirstOrDefault(e => string.Equals(e.Value, HarmonyPackageId, StringComparison.OrdinalIgnoreCase));

                var newEntry = new XElement("li", SpeedModPackageId);
                if (harmonyEntry is not null)
                    harmonyEntry.AddAfterSelf(newEntry);
                else
                    activeMods.AddFirst(newEntry);
            }
            else
            {
                if (existing is null) return; // already inactive
                existing.Remove();
            }

            doc.Save(ModsConfigPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    // ============ CHEATS MOD HELPERS ============

    /// <summary>Path of the cheats mod folder inside the game's Mods directory, or null when the game isn't found.</summary>
    public static string? GetCheatsModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolCheats");
    }

    public static bool IsCheatsModInstalled()
    {
        try
        {
            var modDir = GetCheatsModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.CheatsMod.dll"));
        }
        catch { return false; }
    }

    /// <summary>True when the cheats mod is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsCheatsModActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => string.Equals(e.Value, CheatsModPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deploys the cheats mod into the game's Mods folder (like the speed mod)
    /// and activates it in ModsConfig.xml. Returns true when the files are in place.
    /// </summary>
    public static bool InstallCheatsMod()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return false;

        var modDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolCheats");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        TryCopyFile(Path.Combine(appDir, "CheatsMod", "FlexTool.CheatsMod.dll"),
                    Path.Combine(assembliesDir, "FlexTool.CheatsMod.dll"));
        TryCopyFile(Path.Combine(appDir, "CheatsMod", "About.xml"),
                    Path.Combine(aboutDir, "About.xml"));

        SetCheatsModActiveInConfig(active: true);
        return IsCheatsModInstalled();
    }

    /// <summary>
    /// Removes the cheats mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemoveCheatsMod()
    {
        SetCheatsModActiveInConfig(active: false);

        try
        {
            var modDir = GetCheatsModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsCheatsModInstalled();
    }

    private static void SetCheatsModActiveInConfig(bool active)
    {
        if (!File.Exists(ModsConfigPath)) return;

        try
        {
            var doc = XDocument.Load(ModsConfigPath);
            var activeMods = doc.Root?.Element("activeMods");
            if (activeMods is null) return;

            var existing = activeMods.Elements("li")
                .FirstOrDefault(e => string.Equals(e.Value, CheatsModPackageId, StringComparison.OrdinalIgnoreCase));

            if (active)
            {
                if (existing is not null) return; // already active

                // Insert right after brrainz.harmony so Harmony is loaded first
                var harmonyEntry = activeMods.Elements("li")
                    .FirstOrDefault(e => string.Equals(e.Value, HarmonyPackageId, StringComparison.OrdinalIgnoreCase));

                var newEntry = new XElement("li", CheatsModPackageId);
                if (harmonyEntry is not null)
                    harmonyEntry.AddAfterSelf(newEntry);
                else
                    activeMods.AddFirst(newEntry);
            }
            else
            {
                if (existing is null) return; // already inactive
                existing.Remove();
            }

            doc.Save(ModsConfigPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    // ============ TILL DEATH MOD HELPERS ============

    /// <summary>Path of the Till Death Do Us Part mod folder inside the game's Mods directory, or null when the game isn't found.</summary>
    public static string? GetTillDeathModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolTillDeath");
    }

    public static bool IsTillDeathModInstalled()
    {
        try
        {
            var modDir = GetTillDeathModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.TillDeathMod.dll"));
        }
        catch { return false; }
    }

    /// <summary>True when the Till Death mod is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsTillDeathModActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => string.Equals(e.Value, TillDeathModPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deploys the Till Death mod into the game's Mods folder and activates it in ModsConfig.xml.
    /// Returns true when the files are in place.
    /// </summary>
    public static bool InstallTillDeathMod()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return false;

        var modDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolTillDeath");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        TryCopyFile(Path.Combine(appDir, "TillDeathMod", "FlexTool.TillDeathMod.dll"),
                    Path.Combine(assembliesDir, "FlexTool.TillDeathMod.dll"));
        TryCopyFile(Path.Combine(appDir, "TillDeathMod", "About.xml"),
                    Path.Combine(aboutDir, "About.xml"));

        SetModActiveInConfig(TillDeathModPackageId, active: true);
        return IsTillDeathModInstalled();
    }

    /// <summary>
    /// Removes the Till Death mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemoveTillDeathMod()
    {
        SetModActiveInConfig(TillDeathModPackageId, active: false);

        try
        {
            var modDir = GetTillDeathModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsTillDeathModInstalled();
    }

    // ============ KEEP IT TOGETHER MOD HELPERS ============

    /// <summary>Path of the Keep It Together mod folder inside the game's Mods directory, or null when the game isn't found.</summary>
    public static string? GetKeepItTogetherModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolKeepItTogether");
    }

    public static bool IsKeepItTogetherModInstalled()
    {
        try
        {
            var modDir = GetKeepItTogetherModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.KeepItTogetherMod.dll"));
        }
        catch { return false; }
    }

    /// <summary>True when the Keep It Together mod is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsKeepItTogetherModActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => string.Equals(e.Value, KeepItTogetherModPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deploys the Keep It Together mod into the game's Mods folder and activates it in ModsConfig.xml.
    /// Returns true when the files are in place.
    /// </summary>
    public static bool InstallKeepItTogetherMod()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return false;

        var modDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolKeepItTogether");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        TryCopyFile(Path.Combine(appDir, "KeepItTogetherMod", "FlexTool.KeepItTogetherMod.dll"),
                    Path.Combine(assembliesDir, "FlexTool.KeepItTogetherMod.dll"));
        TryCopyFile(Path.Combine(appDir, "KeepItTogetherMod", "About.xml"),
                    Path.Combine(aboutDir, "About.xml"));

        SetModActiveInConfig(KeepItTogetherModPackageId, active: true);
        return IsKeepItTogetherModInstalled();
    }

    /// <summary>
    /// Removes the Keep It Together mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemoveKeepItTogetherMod()
    {
        SetModActiveInConfig(KeepItTogetherModPackageId, active: false);

        try
        {
            var modDir = GetKeepItTogetherModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsKeepItTogetherModInstalled();
    }

    /// <summary>Adds or removes a package id in ModsConfig.xml's active mods list.</summary>
    private static void SetModActiveInConfig(string packageId, bool active)
    {
        if (!File.Exists(ModsConfigPath)) return;

        try
        {
            var doc = XDocument.Load(ModsConfigPath);
            var activeMods = doc.Root?.Element("activeMods");
            if (activeMods is null) return;

            var existing = activeMods.Elements("li")
                .FirstOrDefault(e => string.Equals(e.Value, packageId, StringComparison.OrdinalIgnoreCase));

            if (active)
            {
                if (existing is not null) return; // already active

                // Insert right after brrainz.harmony so Harmony is loaded first
                var harmonyEntry = activeMods.Elements("li")
                    .FirstOrDefault(e => string.Equals(e.Value, HarmonyPackageId, StringComparison.OrdinalIgnoreCase));

                var newEntry = new XElement("li", packageId);
                if (harmonyEntry is not null)
                    harmonyEntry.AddAfterSelf(newEntry);
                else
                    activeMods.AddFirst(newEntry);
            }
            else
            {
                if (existing is null) return; // already inactive
                existing.Remove();
            }

            doc.Save(ModsConfigPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    // ============ PERF MOD HELPERS ============

    /// <summary>Path of the performance mod folder inside the game's Mods directory, or null when the game isn't found.</summary>
    public static string? GetPerfModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolPerf");
    }

    public static bool IsPerfModInstalled()
    {
        try
        {
            var modDir = GetPerfModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.PerfMod.dll"));
        }
        catch { return false; }
    }

    /// <summary>True when the performance mod is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsPerfModActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => string.Equals(e.Value, PerfModPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deploys the performance mod into the game's Mods folder (like the speed mod)
    /// and activates it in ModsConfig.xml. Returns true when the files are in place.
    /// </summary>
    public static bool InstallPerfMod()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return false;

        var modDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolPerf");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        TryCopyFile(Path.Combine(appDir, "PerfMod", "FlexTool.PerfMod.dll"),
                    Path.Combine(assembliesDir, "FlexTool.PerfMod.dll"));
        TryCopyFile(Path.Combine(appDir, "PerfMod", "About.xml"),
                    Path.Combine(aboutDir, "About.xml"));

        SetPerfModActiveInConfig(active: true);
        return IsPerfModInstalled();
    }

    /// <summary>
    /// Removes the performance mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemovePerfMod()
    {
        SetPerfModActiveInConfig(active: false);

        try
        {
            var modDir = GetPerfModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsPerfModInstalled();
    }

    private static void SetPerfModActiveInConfig(bool active)
    {
        if (!File.Exists(ModsConfigPath)) return;

        try
        {
            var doc = XDocument.Load(ModsConfigPath);
            var activeMods = doc.Root?.Element("activeMods");
            if (activeMods is null) return;

            var existing = activeMods.Elements("li")
                .FirstOrDefault(e => string.Equals(e.Value, PerfModPackageId, StringComparison.OrdinalIgnoreCase));

            if (active)
            {
                if (existing is not null) return; // already active

                // Insert right after brrainz.harmony so Harmony is loaded first
                var harmonyEntry = activeMods.Elements("li")
                    .FirstOrDefault(e => string.Equals(e.Value, HarmonyPackageId, StringComparison.OrdinalIgnoreCase));

                var newEntry = new XElement("li", PerfModPackageId);
                if (harmonyEntry is not null)
                    harmonyEntry.AddAfterSelf(newEntry);
                else
                    activeMods.AddFirst(newEntry);
            }
            else
            {
                if (existing is null) return; // already inactive
                existing.Remove();
            }

            doc.Save(ModsConfigPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    // ============ FPS OPTIMIZER MOD HELPERS ============

    /// <summary>Path of the FPS optimizer mod folder inside the game's Mods directory, or null when the game isn't found.</summary>
    public static string? GetFpsOptimizerModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolFpsOptimizer");
    }

    public static bool IsFpsOptimizerInstalled()
    {
        try
        {
            var modDir = GetFpsOptimizerModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.FPSOptimizer.dll"));
        }
        catch { return false; }
    }

    /// <summary>True when the FPS optimizer mod is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsFpsOptimizerActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => string.Equals(e.Value, FpsOptimizerPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deploys the FPS optimizer mod into the game's Mods folder and activates
    /// it in ModsConfig.xml. Returns true when the files are in place.
    /// </summary>
    public static bool InstallFpsOptimizer()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return false;

        var modDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolFpsOptimizer");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        TryCopyFile(Path.Combine(appDir, "FPSOptimizer", "FlexTool.FPSOptimizer.dll"),
                    Path.Combine(assembliesDir, "FlexTool.FPSOptimizer.dll"));
        TryCopyFile(Path.Combine(appDir, "FPSOptimizer", "About.xml"),
                    Path.Combine(aboutDir, "About.xml"));

        // The FPS Optimizer now includes all former Performance-mod features;
        // remove the legacy mod so the same patches don't apply twice.
        try { if (IsPerfModInstalled()) RemovePerfMod(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

        SetFpsOptimizerActiveInConfig(active: true);
        return IsFpsOptimizerInstalled();
    }

    /// <summary>
    /// Removes the FPS optimizer mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemoveFpsOptimizer()
    {
        SetFpsOptimizerActiveInConfig(active: false);

        try
        {
            var modDir = GetFpsOptimizerModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsFpsOptimizerInstalled();
    }

    private static void SetFpsOptimizerActiveInConfig(bool active)
    {
        if (!File.Exists(ModsConfigPath)) return;

        try
        {
            var doc = XDocument.Load(ModsConfigPath);
            var activeMods = doc.Root?.Element("activeMods");
            if (activeMods is null) return;

            var existing = activeMods.Elements("li")
                .FirstOrDefault(e => string.Equals(e.Value, FpsOptimizerPackageId, StringComparison.OrdinalIgnoreCase));

            if (active)
            {
                if (existing is not null) return; // already active

                // Insert right after brrainz.harmony so Harmony is loaded first
                var harmonyEntry = activeMods.Elements("li")
                    .FirstOrDefault(e => string.Equals(e.Value, HarmonyPackageId, StringComparison.OrdinalIgnoreCase));

                var newEntry = new XElement("li", FpsOptimizerPackageId);
                if (harmonyEntry is not null)
                    harmonyEntry.AddAfterSelf(newEntry);
                else
                    activeMods.AddFirst(newEntry);
            }
            else
            {
                if (existing is null) return; // already inactive
                existing.Remove();
            }

            doc.Save(ModsConfigPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>
    /// Reads the live status file written by the in-game FPS optimizer mod.
    /// Returns null when the mod isn't running or the file is stale (>10s old).
    /// </summary>
    public static Dictionary<string, string>? ReadFpsOptimizerStatus()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlexTool", "FlexToolFpsOptimizer.txt");
            if (!File.Exists(path)) return null;
            if (DateTime.Now - File.GetLastWriteTime(path) > TimeSpan.FromSeconds(10)) return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(path))
            {
                var idx = line.IndexOf('=');
                if (idx > 0) result[line[..idx]] = line[(idx + 1)..];
            }
            return result;
        }
        catch { return null; }
    }

    // ============ DEBUG INFO MOD HELPERS ============

    /// <summary>Path of the debug info mod folder inside the game's Mods directory, or null when the game isn't found.</summary>
    public static string? GetDebugInfoModDir()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return null;
        return Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolDebugInfo");
    }

    public static bool IsDebugInfoModInstalled()
    {
        try
        {
            var modDir = GetDebugInfoModDir();
            if (modDir is null) return false;
            return File.Exists(Path.Combine(modDir, "About", "About.xml"))
                && File.Exists(Path.Combine(modDir, "Assemblies", "FlexTool.DebugInfoMod.dll"));
        }
        catch { return false; }
    }

    /// <summary>True when the debug info mod is listed in ModsConfig.xml's active mods.</summary>
    public static bool IsDebugInfoModActive()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return false;
            var doc = XDocument.Load(ModsConfigPath);
            return doc.Root?.Element("activeMods")?.Elements("li")
                .Any(e => string.Equals(e.Value, DebugInfoModPackageId, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deploys the debug info mod into the game's Mods folder (like the performance mod)
    /// and activates it in ModsConfig.xml. Returns true when the files are in place.
    /// </summary>
    public static bool InstallDebugInfoMod()
    {
        var gameExe = FindGameExePath();
        if (gameExe is null) return false;

        var modDir = Path.Combine(Path.GetDirectoryName(gameExe)!, "Mods", "FlexToolDebugInfo");
        var aboutDir = Path.Combine(modDir, "About");
        var assembliesDir = Path.Combine(modDir, "Assemblies");

        Directory.CreateDirectory(aboutDir);
        Directory.CreateDirectory(assembliesDir);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        TryCopyFile(Path.Combine(appDir, "DebugInfoMod", "FlexTool.DebugInfoMod.dll"),
                    Path.Combine(assembliesDir, "FlexTool.DebugInfoMod.dll"));
        TryCopyFile(Path.Combine(appDir, "DebugInfoMod", "About.xml"),
                    Path.Combine(aboutDir, "About.xml"));

        SetDebugInfoModActiveInConfig(active: true);
        return IsDebugInfoModInstalled();
    }

    /// <summary>
    /// Removes the debug info mod folder from the game's Mods directory and
    /// deactivates it in ModsConfig.xml. Returns true when the folder is gone.
    /// </summary>
    public static bool RemoveDebugInfoMod()
    {
        SetDebugInfoModActiveInConfig(active: false);

        try
        {
            var modDir = GetDebugInfoModDir();
            if (modDir is not null && Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } // folder locked by running game — config deactivation still applies

        return !IsDebugInfoModInstalled();
    }

    private static void SetDebugInfoModActiveInConfig(bool active)
    {
        if (!File.Exists(ModsConfigPath)) return;

        try
        {
            var doc = XDocument.Load(ModsConfigPath);
            var activeMods = doc.Root?.Element("activeMods");
            if (activeMods is null) return;

            var existing = activeMods.Elements("li")
                .FirstOrDefault(e => string.Equals(e.Value, DebugInfoModPackageId, StringComparison.OrdinalIgnoreCase));

            if (active)
            {
                if (existing is not null) return; // already active

                // Insert right after brrainz.harmony so Harmony is loaded first
                var harmonyEntry = activeMods.Elements("li")
                    .FirstOrDefault(e => string.Equals(e.Value, HarmonyPackageId, StringComparison.OrdinalIgnoreCase));

                var newEntry = new XElement("li", DebugInfoModPackageId);
                if (harmonyEntry is not null)
                    harmonyEntry.AddAfterSelf(newEntry);
                else
                    activeMods.AddFirst(newEntry);
            }
            else
            {
                if (existing is null) return; // already inactive
                existing.Remove();
            }

            doc.Save(ModsConfigPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static void ActivateModInConfig()
    {
        if (!File.Exists(ModsConfigPath)) return;

        try
        {
            var doc = XDocument.Load(ModsConfigPath);
            var activeMods = doc.Root?.Element("activeMods");
            if (activeMods is null) return;

            // Already present?
            if (activeMods.Elements("li")
                .Any(e => string.Equals(e.Value, ModPackageId, StringComparison.OrdinalIgnoreCase)))
                return;

            // Insert right after brrainz.harmony so Harmony is loaded first
            var harmonyEntry = activeMods.Elements("li")
                .FirstOrDefault(e => string.Equals(e.Value, HarmonyPackageId, StringComparison.OrdinalIgnoreCase));

            var newEntry = new XElement("li", ModPackageId);

            if (harmonyEntry is not null)
                harmonyEntry.AddAfterSelf(newEntry);
            else
                activeMods.AddFirst(newEntry); // fallback: put it first

            doc.Save(ModsConfigPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static void WriteAutoLoadCommand(string saveName)
    {
        try { File.WriteAllText(AutoLoadFilePath, saveName); }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    // ──────────────────────────────────────────────────────────────
    // Mod reading
    // ──────────────────────────────────────────────────────────────

    public enum ModSource { Core, DLC, Workshop, Local, Unknown }

    public enum ModCategory { Framework, Visuals, Textures, Content, Gameplay, QoL, UI, Unknown }

    public sealed class ModInfo
    {
        public string PackageId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Author { get; init; } = "";
        public string Version { get; init; } = "";
        public string SupportedVersions { get; init; } = "";
        public string FolderPath { get; init; } = "";
        public ModSource Source { get; init; }
        public ModCategory Category { get; init; }
        public int LoadOrder { get; init; }
        public bool IsActive { get; init; }
    }

    private static readonly CacheEntry<List<ModInfo>> _allModsCache = new(TimeSpan.MaxValue);

    private static readonly HashSet<string> CoreExpansionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "ludeon.rimworld",
        "ludeon.rimworld.royalty",
        "ludeon.rimworld.ideology",
        "ludeon.rimworld.biotech",
        "ludeon.rimworld.anomaly",
        "ludeon.rimworld.odyssey"
    };

    /// <summary>
    /// Returns all known mods (active + inactive) with display names, versions,
    /// and categories resolved from Workshop, local Mods, and Data folders.
    /// Active mods appear first in load order. Cached until ModsConfig.xml changes.
    /// </summary>
    public static List<ModInfo> GetAllMods()
    {
        try
        {
            if (!File.Exists(ModsConfigPath)) return [];

            var key = File.GetLastWriteTime(ModsConfigPath).Ticks.ToString();
            if (_allModsCache.TryGet(key, out var cached))
                return cached;

            var doc = XDocument.Load(ModsConfigPath);
            var activeIds = doc.Root?.Element("activeMods")?.Elements("li")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList() ?? [];

            var activeSet = new HashSet<string>(activeIds, StringComparer.OrdinalIgnoreCase);
            var lookup = BuildModLookup();
            var result = new List<ModInfo>();

            for (int i = 0; i < activeIds.Count; i++)
            {
                var id = activeIds[i];
                var source = ClassifyModSource(id, lookup);
                if (lookup.TryGetValue(id, out var info))
                    result.Add(new ModInfo
                    {
                        PackageId = id,
                        Name = info.Name,
                        Author = info.Author,
                        Version = info.Version,
                        SupportedVersions = info.SupportedVersions,
                        FolderPath = info.FolderPath,
                        Source = source,
                        Category = ClassifyCategory(info.Name, info.Description, id),
                        LoadOrder = i,
                        IsActive = true
                    });
                else
                    result.Add(new ModInfo
                    {
                        PackageId = id,
                        Name = FormatPackageIdAsName(id),
                        Source = source,
                        Category = ModCategory.Unknown,
                        LoadOrder = i,
                        IsActive = true
                    });
            }

            foreach (var (id, info) in lookup)
            {
                if (activeSet.Contains(id)) continue;
                result.Add(new ModInfo
                {
                    PackageId = id,
                    Name = info.Name,
                    Author = info.Author,
                    Version = info.Version,
                    SupportedVersions = info.SupportedVersions,
                    FolderPath = info.FolderPath,
                    Source = info.Source,
                    Category = ClassifyCategory(info.Name, info.Description, id),
                    LoadOrder = -1,
                    IsActive = false
                });
            }

            _allModsCache.Set(result, key);
            return result;
        }
        catch
        {
            _allModsCache.TryGet(out var fallback);
            return fallback ?? [];
        }
    }

    /// <summary>Returns only active mods in load order.</summary>
    public static List<ModInfo> GetActiveMods() => GetAllMods().Where(m => m.IsActive).ToList();

    private record struct ModLookupEntry(string Name, string Author, ModSource Source,
        string Version, string SupportedVersions, string Description, string FolderPath);

    private static Dictionary<string, ModLookupEntry> BuildModLookup()
    {
        var lookup = new Dictionary<string, ModLookupEntry>(StringComparer.OrdinalIgnoreCase);

        var gameExe = FindGameExePath();
        if (gameExe is not null)
        {
            var gameDir = Path.GetDirectoryName(gameExe)!;

            // Core + DLC (Data folder)
            ScanModFolder(Path.Combine(gameDir, "Data"), ModSource.DLC, lookup);
            // Override Core specifically
            if (lookup.TryGetValue("ludeon.rimworld", out var core))
                lookup["ludeon.rimworld"] = core with { Source = ModSource.Core };

            // Local mods
            ScanModFolder(Path.Combine(gameDir, "Mods"), ModSource.Local, lookup);
        }

        // Steam Workshop mods
        string? steamPath = null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                         ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            steamPath = key?.GetValue("InstallPath") as string;
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

        if (steamPath is not null)
        {
            ScanModFolder(Path.Combine(steamPath, "steamapps", "workshop", "content", "294100"),
                ModSource.Workshop, lookup);

            // Also check additional library folders
            try
            {
                var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (var line in File.ReadLines(vdf))
                    {
                        var trimmed = line.Trim();
                        if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
                        var parts = trimmed.Split('"');
                        if (parts.Length < 4) continue;
                        var libPath = parts[3].Replace("\\\\", "\\");
                        ScanModFolder(Path.Combine(libPath, "steamapps", "workshop", "content", "294100"),
                            ModSource.Workshop, lookup);
                    }
                }
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }

        return lookup;
    }

    private static void ScanModFolder(string folder, ModSource source,
        Dictionary<string, ModLookupEntry> lookup)
    {
        if (!Directory.Exists(folder)) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                var aboutPath = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(aboutPath)) continue;

                try
                {
                    using var fs = new FileStream(aboutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = System.Xml.XmlReader.Create(fs);

                    string? packageId = null, name = null, author = null;
                    string? version = null, supportedVersions = null, description = null;

                    while (reader.Read())
                    {
                        if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;
                        switch (reader.Name)
                        {
                            case "packageId" when packageId is null:
                                packageId = reader.ReadElementContentAsString().Trim();
                                break;
                            case "name" when name is null:
                                name = reader.ReadElementContentAsString().Trim();
                                break;
                            case "author" when author is null:
                                author = reader.ReadElementContentAsString().Trim();
                                break;
                            case "modVersion" when version is null:
                                version = reader.ReadElementContentAsString().Trim();
                                break;
                            case "supportedVersions" when supportedVersions is null:
                                var vList = new List<string>();
                                if (!reader.IsEmptyElement)
                                {
                                    while (reader.Read())
                                    {
                                        if (reader.NodeType == System.Xml.XmlNodeType.EndElement
                                            && reader.Name == "supportedVersions") break;
                                        if (reader.NodeType == System.Xml.XmlNodeType.Element
                                            && reader.Name == "li")
                                            vList.Add(reader.ReadElementContentAsString().Trim());
                                    }
                                }
                                supportedVersions = string.Join(", ", vList);
                                break;
                            case "description" when description is null:
                                description = reader.ReadElementContentAsString().Trim();
                                if (description.Length > 500) description = description[..500];
                                break;
                        }
                    }

                    if (!string.IsNullOrEmpty(packageId) && !lookup.ContainsKey(packageId))
                        lookup[packageId] = new ModLookupEntry(
                            name ?? packageId, author ?? "", source,
                            version ?? "", supportedVersions ?? "", description ?? "", dir);
                }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static ModSource ClassifyModSource(string packageId,
        Dictionary<string, ModLookupEntry> lookup)
    {
        if (string.Equals(packageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase))
            return ModSource.Core;
        if (CoreExpansionIds.Contains(packageId))
            return ModSource.DLC;
        if (lookup.TryGetValue(packageId, out var entry))
            return entry.Source;
        return ModSource.Unknown;
    }

    /// <summary>
    /// Enables or disables a mod by adding/removing it from ModsConfig.xml.
    /// </summary>
    public static void SetModEnabled(string packageId, bool enabled)
    {
        if (!File.Exists(ModsConfigPath)) return;

        var doc = XDocument.Load(ModsConfigPath);
        var activeMods = doc.Root?.Element("activeMods");
        if (activeMods is null) return;

        var existing = activeMods.Elements("li")
            .FirstOrDefault(e => string.Equals(e.Value.Trim(), packageId, StringComparison.OrdinalIgnoreCase));

        if (enabled && existing is null)
            activeMods.Add(new XElement("li", packageId));
        else if (!enabled && existing is not null)
            existing.Remove();
        else
            return;

        doc.Save(ModsConfigPath);
        _allModsCache.Invalidate();
    }

    /// <summary>
    /// Reorders mods in the ModsConfig.xml active mods list.
    /// </summary>
    public static void ReorderMods(List<string> packageIdOrder)
    {
        if (!File.Exists(ModsConfigPath) || packageIdOrder == null || packageIdOrder.Count == 0) 
            return;

        var doc = XDocument.Load(ModsConfigPath);
        var activeMods = doc.Root?.Element("activeMods");
        if (activeMods is null) return;

        // Remove all current mod entries
        activeMods.Elements("li").Remove();

        // Add them back in the new order
        foreach (var packageId in packageIdOrder)
        {
            activeMods.Add(new XElement("li", packageId));
        }

        doc.Save(ModsConfigPath);
        _allModsCache.Invalidate();
    }

    private static ModCategory ClassifyCategory(string name, string description, string packageId)
    {
        var text = $" {name} {description} {packageId} ".ToLowerInvariant();

        if (ContainsAny(text, "harmony", "hugslib", "library", "framework", "mod manager", "modmanager", "prepatcher"))
            return ModCategory.Framework;
        if (ContainsAny(text, "texture", "retexture", " hd ", "high res"))
            return ModCategory.Textures;
        if (ContainsAny(text, "visual", "graphic", "shader", "appearance", "hair style", "apparel", "animation"))
            return ModCategory.Visuals;
        if (ContainsAny(text, " ui ", "interface", " hud", "tooltip", "menu", " tab ", "numbers"))
            return ModCategory.UI;
        if (ContainsAny(text, "quality of life", " qol", "allow tool", "replace stuff", "pickup", "designat"))
            return ModCategory.QoL;
        if (ContainsAny(text, "combat", "mechanoid", "difficulty", "storyteller", "trait", "gene", "ritual", "incident"))
            return ModCategory.Gameplay;
        if (ContainsAny(text, "expanded", "expansion", "race", "faction", "biome", "animal", "weapon", "armor",
            "furniture", "prosthetic", "implant", "food", "crop", "drug"))
            return ModCategory.Content;

        return ModCategory.Unknown;
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.Ordinal));

    /// <summary>
    /// Converts "some.author.modname" → "Modname" as a fallback display name.
    /// </summary>
    private static string FormatPackageIdAsName(string packageId)
    {
        var lastDot = packageId.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < packageId.Length - 1)
        {
            var segment = packageId[(lastDot + 1)..];
            return char.ToUpper(segment[0]) + segment[1..];
        }
        return packageId;
    }

    // ──────────────────────────────────────────────────────────────
    // Mod ThingDef scanning
    // ──────────────────────────────────────────────────────────────

    public enum ModItemCategory { Weapon, Apparel, Resource, Building, Animal, Drug, Plant, Other }

    public sealed record ModDefItem(string Label, string DefName, ModItemCategory Category);

    /// <summary>
    /// Parses a mod's Defs XML files and returns all ThingDefs with labels.
    /// Items are categorized by examining ParentName attributes and child elements.
    /// </summary>
    public static List<ModDefItem> GetModThingDefs(string packageId)
    {
        var mod = GetAllMods().FirstOrDefault(m =>
            string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        if (mod is null || string.IsNullOrEmpty(mod.FolderPath))
            return [];

        var items = new List<ModDefItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scan Defs/ folder and versioned folders like 1.5/Defs/
        var defsRoots = new List<string>();
        var mainDefs = Path.Combine(mod.FolderPath, "Defs");
        if (Directory.Exists(mainDefs))
            defsRoots.Add(mainDefs);

        // Check versioned folders (e.g. 1.4/Defs, 1.5/Defs)
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(mod.FolderPath))
            {
                var name = Path.GetFileName(dir);
                if (name.Length >= 3 && char.IsDigit(name[0]) && name[1] == '.')
                {
                    var vDefs = Path.Combine(dir, "Defs");
                    if (Directory.Exists(vDefs))
                        defsRoots.Add(vDefs);
                }
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

        foreach (var defsRoot in defsRoots)
        {
            try
            {
                foreach (var xmlFile in Directory.EnumerateFiles(defsRoot, "*.xml", SearchOption.AllDirectories))
                {
                    try { ParseDefsFile(xmlFile, items, seen); }
                    catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
                }
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }

        return items.OrderBy(i => i.Category).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ParseDefsFile(string xmlPath, List<ModDefItem> items, HashSet<string> seen)
    {
        var doc = XDocument.Load(xmlPath);
        if (doc.Root is null) return;

        foreach (var el in doc.Root.Elements())
        {
            var elName = el.Name.LocalName;
            // Only process ThingDef and derived types
            if (!elName.Equals("ThingDef", StringComparison.OrdinalIgnoreCase)
                && !elName.EndsWith("Def", StringComparison.OrdinalIgnoreCase))
                continue;

            var label = el.Element("label")?.Value.Trim();
            var defName = el.Element("defName")?.Value.Trim();
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(defName))
                continue;

            // Capitalize first letter of label
            label = char.ToUpper(label[0]) + label[1..];

            if (!seen.Add(defName)) continue;

            var category = ClassifyDefCategory(el, elName);
            items.Add(new ModDefItem(label, defName, category));
        }
    }

    private static ModItemCategory ClassifyDefCategory(XElement el, string elName)
    {
        var parentName = el.Attribute("ParentName")?.Value ?? "";
        var parentLower = parentName.ToLowerInvariant();
        var elNameLower = elName.ToLowerInvariant();

        // Check explicit category element
        var thingCategory = el.Element("category")?.Value ?? "";

        // Check thingClass
        var thingClass = el.Element("thingClass")?.Value ?? "";

        // Building
        if (thingCategory.Equals("Building", StringComparison.OrdinalIgnoreCase)
            || thingClass.Contains("Building", StringComparison.OrdinalIgnoreCase)
            || parentLower.Contains("building"))
            return ModItemCategory.Building;

        // Weapons
        if (parentLower.Contains("weapon") || parentLower.Contains("gun") || parentLower.Contains("melee")
            || el.Element("weaponTags") is not null
            || el.Element("tools") is not null && parentLower.Contains("base"))
            return ModItemCategory.Weapon;

        // Apparel
        if (parentLower.Contains("apparel") || parentLower.Contains("armor") || parentLower.Contains("hat")
            || parentLower.Contains("headgear") || parentLower.Contains("vest")
            || el.Element("apparel") is not null)
            return ModItemCategory.Apparel;

        // Animals
        if (parentLower.Contains("animal") || parentLower.Contains("pawn")
            || thingClass.Contains("Pawn") && el.Element("race") is not null)
            return ModItemCategory.Animal;

        // Drugs
        if (parentLower.Contains("drug") || el.Element("ingestible")?.Element("drugCategory") is not null)
            return ModItemCategory.Drug;

        // Plants
        if (parentLower.Contains("plant") || elNameLower.Contains("plant")
            || thingClass.Contains("Plant"))
            return ModItemCategory.Plant;

        // Resources — items with stackLimit or thingCategories containing resource hints
        var categories = el.Element("thingCategories")?.Elements("li")
            .Select(li => li.Value.ToLowerInvariant()).ToList() ?? [];
        if (categories.Any(c => c.Contains("resource") || c.Contains("chunk") || c.Contains("manufactured"))
            || parentLower.Contains("resource"))
            return ModItemCategory.Resource;

        // Only include ThingDefs (not all *Def types) as items
        if (elNameLower == "thingdef")
            return ModItemCategory.Other;

        // Non-ThingDef elements (ResearchProjectDef, etc.) — skip
        return ModItemCategory.Other;
    }

    // ──────────────────────────────────────────────────────────────
    // Mod performance / footprint analysis
    // ──────────────────────────────────────────────────────────────

    public sealed class ModPerformanceData
    {
        public string PackageId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Author { get; init; } = "";
        public ModSource Source { get; init; }
        public bool IsActive { get; init; }

        // File counts
        public int XmlFileCount { get; init; }
        public int DllFileCount { get; init; }
        public int TextureFileCount { get; init; }

        // Byte sizes
        public long XmlTotalBytes { get; init; }
        public long DllTotalBytes { get; init; }
        public long TextureTotalBytes { get; init; }
        public long TotalFolderBytes { get; init; }

        // Subdirectory file counts
        public int DefsFileCount { get; init; }
        public int PatchesFileCount { get; init; }
        public int AssembliesFileCount { get; init; }
    }

    private static readonly CacheEntry<List<ModPerformanceData>> _perfDataCache = new(TimeSpan.FromSeconds(30));

    /// <summary>
    /// Scans every known mod folder and returns footprint metrics
    /// (XML count/size, DLL count/size, texture count/size, total folder size).
    /// Results are cached for 30 seconds.
    /// </summary>
    public static List<ModPerformanceData> GetModPerformanceData()
    {
        if (_perfDataCache.TryGet(out var cached))
            return cached;

        var mods = GetAllMods();
        var result = new List<ModPerformanceData>(mods.Count);

        foreach (var mod in mods)
        {
            if (string.IsNullOrEmpty(mod.FolderPath) || !Directory.Exists(mod.FolderPath))
            {
                result.Add(new ModPerformanceData
                {
                    PackageId = mod.PackageId,
                    Name = mod.Name,
                    Author = mod.Author,
                    Source = mod.Source,
                    IsActive = mod.IsActive
                });
                continue;
            }

            try
            {
                int xmlCount = 0, dllCount = 0, texCount = 0;
                long xmlBytes = 0, dllBytes = 0, texBytes = 0, totalBytes = 0;
                int defsCount = 0, patchesCount = 0, asmCount = 0;

                var folderNorm = mod.FolderPath.Replace('/', '\\').TrimEnd('\\') + '\\';

                foreach (var file in Directory.EnumerateFiles(mod.FolderPath, "*", SearchOption.AllDirectories))
                {
                    long size;
                    try { size = new FileInfo(file).Length; }
                    catch { continue; }

                    totalBytes += size;
                    var ext = Path.GetExtension(file);

                    if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        xmlCount++;
                        xmlBytes += size;

                        // Classify by subfolder
                        var rel = file.Replace('/', '\\');
                        if (rel.Contains("\\Defs\\", StringComparison.OrdinalIgnoreCase))
                            defsCount++;
                        else if (rel.Contains("\\Patches\\", StringComparison.OrdinalIgnoreCase))
                            patchesCount++;
                    }
                    else if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        dllCount++;
                        dllBytes += size;
                        asmCount++;
                    }
                    else if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                          || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                          || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                          || ext.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        texCount++;
                        texBytes += size;
                    }
                }

                result.Add(new ModPerformanceData
                {
                    PackageId = mod.PackageId,
                    Name = mod.Name,
                    Author = mod.Author,
                    Source = mod.Source,
                    IsActive = mod.IsActive,
                    XmlFileCount = xmlCount,
                    DllFileCount = dllCount,
                    TextureFileCount = texCount,
                    XmlTotalBytes = xmlBytes,
                    DllTotalBytes = dllBytes,
                    TextureTotalBytes = texBytes,
                    TotalFolderBytes = totalBytes,
                    DefsFileCount = defsCount,
                    PatchesFileCount = patchesCount,
                    AssembliesFileCount = asmCount
                });
            }
            catch
            {
                result.Add(new ModPerformanceData
                {
                    PackageId = mod.PackageId,
                    Name = mod.Name,
                    Author = mod.Author,
                    Source = mod.Source,
                    IsActive = mod.IsActive
                });
            }
        }

        _perfDataCache.Set(result);
        return result;
    }

    // ──────────────────────────────────────────────────────────────
    // Game log parsing (Player.log)
    // ──────────────────────────────────────────────────────────────

    public enum LogLevel { Info, Warning, Error, Exception }

    public sealed class GameLogEntry
    {
        public LogLevel Level { get; init; }
        public string Message { get; init; } = "";
        public string StackTrace { get; init; } = "";
        public int LineNumber { get; init; }
    }

    private static readonly CacheEntry<List<GameLogEntry>> _gameLogCache = new(TimeSpan.FromSeconds(5));

    /// <summary>
    /// Reads RimWorld's Player.log and extracts log entries categorized by severity.
    /// Focuses on errors, exceptions, and warnings. Returns most recent entries first.
    /// Cached for 5 seconds, invalidated when the file's write time changes.
    /// </summary>
    public static List<GameLogEntry> GetGameLogEntries()
    {
        var logPath = Path.Combine(UserDataPath, "Player.log");
        if (!File.Exists(logPath)) return [];

        var key = File.GetLastWriteTime(logPath).Ticks.ToString();
        if (_gameLogCache.TryGet(key, out var cached))
            return cached;

        try
        {
            string text;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();

            var lines = text.Split('\n');
            var entries = new List<GameLogEntry>();
            int i = 0;

            while (i < lines.Length)
            {
                var line = lines[i].TrimEnd('\r');
                int lineNo = i + 1; // 1-based line number of this entry
                i++;

                if (string.IsNullOrWhiteSpace(line)) continue;

                // Skip Unity metadata lines like "(Filename: ... Line: ...)"
                if (line.TrimStart().StartsWith("(Filename:")) continue;

                // Detect exception lines: "SomeException: message"
                if (IsExceptionLine(line))
                {
                    var stackTrace = CollectStackTrace(lines, ref i);
                    entries.Add(new GameLogEntry
                    {
                        Level = LogLevel.Exception,
                        Message = line,
                        StackTrace = stackTrace,
                        LineNumber = lineNo
                    });
                    continue;
                }

                // Detect error lines
                if (line.StartsWith("Error ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("] ERROR ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Could not ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Failed to ", StringComparison.OrdinalIgnoreCase)
                    || (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        && line.Contains("loading", StringComparison.OrdinalIgnoreCase)))
                {
                    var stackTrace = CollectStackTrace(lines, ref i);
                    entries.Add(new GameLogEntry
                    {
                        Level = LogLevel.Error,
                        Message = line,
                        StackTrace = stackTrace,
                        LineNumber = lineNo
                    });
                    continue;
                }

                // Detect warning lines
                if (line.StartsWith("Warning ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("] WARNING ", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("] WARN ", StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(new GameLogEntry
                    {
                        Level = LogLevel.Warning,
                        Message = line,
                        LineNumber = lineNo
                    });
                    continue;
                }

                // Config / version / startup lines are Info
                if (line.StartsWith("RimWorld ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Mono path", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Loading ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Initializ", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("GfxDevice", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Renderer:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Vendor:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("VRAM:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("SystemInfo", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Unloading ", StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(new GameLogEntry
                    {
                        Level = LogLevel.Info,
                        Message = line,
                        LineNumber = lineNo
                    });
                }
            }

            entries.Reverse();
            _gameLogCache.Set(entries, key);
            return entries;
        }
        catch { return []; }
    }

    /// <summary>
    /// Returns the file size of Player.log, or 0 if not found.
    /// </summary>
    public static long GetGameLogFileSize()
    {
        var logPath = Path.Combine(UserDataPath, "Player.log");
        try { return File.Exists(logPath) ? new FileInfo(logPath).Length : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// Returns the last write time of Player.log.
    /// </summary>
    public static DateTime? GetGameLogLastModified()
    {
        var logPath = Path.Combine(UserDataPath, "Player.log");
        try { return File.Exists(logPath) ? File.GetLastWriteTime(logPath) : null; }
        catch { return null; }
    }

    private static bool IsExceptionLine(string line)
    {
        // Matches patterns like "NullReferenceException: ..." or "System.IO.IOException: ..."
        int colonIdx = line.IndexOf(':');
        if (colonIdx <= 0) return false;
        var prefix = line[..colonIdx];
        return prefix.EndsWith("Exception", StringComparison.Ordinal)
            && !prefix.Contains(' ');
    }

    private static string CollectStackTrace(string[] lines, ref int i)
    {
        var sb = new System.Text.StringBuilder();
        while (i < lines.Length)
        {
            var next = lines[i].TrimEnd('\r');
            // Stack trace lines start with whitespace + "at " or are metadata
            if (next.TrimStart().StartsWith("at ", StringComparison.Ordinal)
                || next.TrimStart().StartsWith("(Filename:", StringComparison.Ordinal)
                || (next.StartsWith("  ") && next.Contains(" in ")))
            {
                if (!next.TrimStart().StartsWith("(Filename:"))
                    sb.AppendLine(next);
                i++;
            }
            else break;
        }
        return sb.ToString().TrimEnd();
    }

    // ──────────────────────────────────────────────────────────────
    // Export / Import mod lists
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Exports the current active mod list. Format "text" writes one packageId per line.
    /// Format "xml" writes a ModsConfig-compatible XML file.
    /// </summary>
    public static void ExportModList(string filePath, string format = "text")
    {
        var mods = GetAllMods().Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();

        if (format.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            var doc = new XDocument(
                new XElement("FlexToolModList",
                    new XElement("ExportedAt", DateTime.Now.ToString("o")),
                    new XElement("ModCount", mods.Count),
                    new XElement("Mods",
                        mods.Select(m => new XElement("Mod",
                            new XElement("PackageId", m.PackageId),
                            new XElement("Name", m.Name),
                            new XElement("Author", m.Author),
                            new XElement("Source", m.Source.ToString()),
                            new XElement("LoadOrder", m.LoadOrder))))));
            doc.Save(filePath);
        }
        else
        {
            var lines = new List<string>
            {
                $"# FlexTool Mod List — {DateTime.Now:yyyy-MM-dd HH:mm}",
                $"# {mods.Count} active mods",
                ""
            };
            foreach (var m in mods)
                lines.Add($"{m.PackageId}  # {m.Name}");
            File.WriteAllLines(filePath, lines);
        }
    }

    /// <summary>
    /// Imports a mod list from a file and sets ModsConfig.xml to match.
    /// Returns (imported count, not-found package IDs).
    /// </summary>
    public static (int Imported, List<string> NotFound) ImportModList(string filePath)
    {
        if (!File.Exists(filePath)) return (0, []);

        var ext = Path.GetExtension(filePath);
        var packageIds = new List<string>();

        if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var doc = XDocument.Load(filePath);
                // Support both FlexTool export format and raw ModsConfig.xml
                var modsEl = doc.Root?.Element("Mods") ?? doc.Root?.Element("activeMods");
                if (modsEl != null)
                {
                    foreach (var el in modsEl.Elements())
                    {
                        var id = el.Element("PackageId")?.Value ?? el.Element("packageId")?.Value ?? el.Value;
                        id = id.Trim();
                        // Strip inline comments
                        var commentIdx = id.IndexOf('#');
                        if (commentIdx >= 0) id = id[..commentIdx].Trim();
                        if (!string.IsNullOrEmpty(id))
                            packageIds.Add(id);
                    }
                }
            }
            catch { return (0, []); }
        }
        else
        {
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                // Strip inline comment
                var commentIdx = trimmed.IndexOf('#');
                if (commentIdx >= 0) trimmed = trimmed[..commentIdx].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    packageIds.Add(trimmed);
            }
        }

        if (packageIds.Count == 0) return (0, []);

        // Always ensure Core is first
        if (!packageIds.Any(id => id.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)))
            packageIds.Insert(0, "ludeon.rimworld");

        // Write to ModsConfig.xml
        if (!File.Exists(ModsConfigPath)) return (0, []);

        var configDoc = XDocument.Load(ModsConfigPath);
        var activeMods = configDoc.Root?.Element("activeMods");
        if (activeMods is null) return (0, []);

        activeMods.RemoveAll();
        foreach (var id in packageIds)
            activeMods.Add(new XElement("li", id));

        configDoc.Save(ModsConfigPath);
        _allModsCache.Invalidate();

        // Check which mods weren't found in installed mods
        var lookup = BuildModLookup();
        var notFound = packageIds
            .Where(id => !lookup.ContainsKey(id) && !CoreExpansionIds.Contains(id))
            .ToList();

        return (packageIds.Count, notFound);
    }

    // ──────────────────────────────────────────────────────────────
    // Mod conflict detection & load-order warnings
    // ──────────────────────────────────────────────────────────────

    public sealed class ModConflict
    {
        public string TargetDef { get; init; } = "";
        public string TargetXPath { get; init; } = "";
        public string Operation { get; init; } = "";
        public List<(string PackageId, string ModName, string PatchFile)> InvolvedMods { get; init; } = [];
    }

    private static readonly CacheEntry<List<ModConflict>> _conflictsCache = new(TimeSpan.FromSeconds(60));

    /// <summary>
    /// Scans the Patches/ folder of every active mod and detects cases where
    /// multiple mods patch the same XPath target. Returns potential conflicts.
    /// Cached for 60 seconds.
    /// </summary>
    public static List<ModConflict> GetModConflicts()
    {
        if (_conflictsCache.TryGet(out var cached))
            return cached;

        var mods = GetAllMods().Where(m => m.IsActive && !string.IsNullOrEmpty(m.FolderPath)).ToList();

        // Map: xpath → list of (packageId, modName, patchFile)
        var patchMap = new Dictionary<string, List<(string PackageId, string ModName, string PatchFile, string Op)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            // Extra safety: skip if FolderPath is null/empty (defensive check)
            if (string.IsNullOrEmpty(mod.FolderPath) || !Directory.Exists(mod.FolderPath))
                continue;

            var patchRoots = new List<string>();
            var mainPatches = Path.Combine(mod.FolderPath, "Patches");
            if (Directory.Exists(mainPatches))
                patchRoots.Add(mainPatches);

            // Versioned patch folders (e.g. 1.5/Patches)
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(mod.FolderPath))
                {
                    var name = Path.GetFileName(dir);
                    if (name.Length >= 3 && char.IsDigit(name[0]) && name[1] == '.')
                    {
                        var vPatches = Path.Combine(dir, "Patches");
                        if (Directory.Exists(vPatches))
                            patchRoots.Add(vPatches);
                    }
                }
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

            foreach (var root in patchRoots)
            {
                try
                {
                    foreach (var xmlFile in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
                    {
                        try { ScanPatchFile(xmlFile, mod.PackageId, mod.Name, patchMap); }
                        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
                    }
                }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
            }
        }

        // Find conflicts: any xpath targeted by 2+ different mods
        var conflicts = new List<ModConflict>();
        foreach (var (xpath, entries) in patchMap)
        {
            var distinctMods = entries
                .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctMods.Count < 2) continue;

            // Extract a human-readable def name from the xpath
            var defName = ExtractDefNameFromXPath(xpath);

            conflicts.Add(new ModConflict
            {
                TargetDef = defName,
                TargetXPath = xpath.Length > 120 ? xpath[..117] + "..." : xpath,
                Operation = entries.First().Op,
                InvolvedMods = entries
                    .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => (g.Key, g.First().ModName, g.First().PatchFile))
                    .ToList()
            });
        }

        var sorted = conflicts.OrderByDescending(c => c.InvolvedMods.Count).ToList();
        _conflictsCache.Set(sorted);
        return sorted;
    }

    private static void ScanPatchFile(string xmlPath, string packageId, string modName,
        Dictionary<string, List<(string PackageId, string ModName, string PatchFile, string Op)>> map)
    {
        var doc = XDocument.Load(xmlPath);
        if (doc.Root is null) return;
        var relPath = Path.GetFileName(xmlPath);

        foreach (var op in doc.Root.Elements())
        {
            var opName = op.Name.LocalName;
            // Standard RimWorld patch operations
            if (opName is not ("PatchOperationReplace" or "PatchOperationAdd" or "PatchOperationRemove"
                or "PatchOperationInsert" or "PatchOperationAttributeSet" or "PatchOperationAttributeAdd"
                or "PatchOperationSetName"))
                continue;

            var xpath = op.Element("xpath")?.Value?.Trim();
            if (string.IsNullOrEmpty(xpath)) continue;

            if (!map.TryGetValue(xpath, out var list))
            {
                list = [];
                map[xpath] = list;
            }
            list.Add((packageId, modName, relPath, opName.Replace("PatchOperation", "")));
        }
    }

    private static string ExtractDefNameFromXPath(string xpath)
    {
        // Try to extract def name from common patterns like:
        // Defs/ThingDef[defName="Steel"]/...
        var idx = xpath.IndexOf("defName=\"", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var start = idx + 9;
            var end = xpath.IndexOf('"', start);
            if (end > start)
                return xpath[start..end];
        }
        // Try defName='...' (single quotes)
        idx = xpath.IndexOf("defName='", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var start = idx + 9;
            var end = xpath.IndexOf('\'', start);
            if (end > start)
                return xpath[start..end];
        }
        return "";
    }

    public sealed class LoadOrderWarning
    {
        public enum Severity { Error, Warning, Info }
        public Severity Level { get; init; }
        public string Message { get; init; } = "";
        public string Details { get; init; } = "";
        public string? PackageId { get; init; }
    }

    /// <summary>
    /// Checks the active mod load order for common mistakes.
    /// </summary>
    public static List<LoadOrderWarning> GetLoadOrderWarnings()
    {
        var mods = GetAllMods().Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();
        var warnings = new List<LoadOrderWarning>();
        if (mods.Count == 0) return warnings;

        // Known framework mods that should be loaded early
        var frameworkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "brrainz.harmony",
            "unlimitedhugs.hugslib",
            "zetrith.prepatcher",
            "smashphil.xmlextensions",
        };

        // Known mods that should be loaded late
        var lateMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hahkethomemah.rimhud",
            "fluffy.modmanager",
        };

        // Rule 1: Core should be first
        if (!mods[0].PackageId.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new LoadOrderWarning
            {
                Level = LoadOrderWarning.Severity.Error,
                Message = "Core is not first in load order",
                Details = "ludeon.rimworld must be the first mod loaded. Many mods will break otherwise.",
                PackageId = "ludeon.rimworld"
            });
        }

        // Rule 2: Harmony should be loaded before all non-Core, non-DLC mods
        var harmonyMod = mods.FirstOrDefault(m =>
            m.PackageId.Equals("brrainz.harmony", StringComparison.OrdinalIgnoreCase));
        if (harmonyMod != null)
        {
            var firstNonCoreMod = mods.FirstOrDefault(m =>
                !CoreExpansionIds.Contains(m.PackageId) && !frameworkIds.Contains(m.PackageId)
                && m.PackageId != "brrainz.harmony");

            if (firstNonCoreMod != null && harmonyMod.LoadOrder > firstNonCoreMod.LoadOrder)
            {
                warnings.Add(new LoadOrderWarning
                {
                    Level = LoadOrderWarning.Severity.Error,
                    Message = "Harmony loaded too late",
                    Details = $"Harmony (#{harmonyMod.LoadOrder + 1}) should load before \"{firstNonCoreMod.Name}\" (#{firstNonCoreMod.LoadOrder + 1}). Place Harmony right after Core/DLCs.",
                    PackageId = "brrainz.harmony"
                });
            }
        }
        else
        {
            // Check if any mod likely needs Harmony
            bool hasWorkshopMods = mods.Any(m => m.Source == ModSource.Workshop);
            if (hasWorkshopMods)
            {
                warnings.Add(new LoadOrderWarning
                {
                    Level = LoadOrderWarning.Severity.Warning,
                    Message = "Harmony not detected",
                    Details = "Most Workshop mods require Harmony. If mods are failing, install Harmony from the Workshop.",
                });
            }
        }

        // Rule 3: HugsLib should be after Harmony but before content mods
        var hugsLib = mods.FirstOrDefault(m =>
            m.PackageId.Equals("unlimitedhugs.hugslib", StringComparison.OrdinalIgnoreCase));
        if (hugsLib != null && harmonyMod != null && hugsLib.LoadOrder < harmonyMod.LoadOrder)
        {
            warnings.Add(new LoadOrderWarning
            {
                Level = LoadOrderWarning.Severity.Error,
                Message = "HugsLib loaded before Harmony",
                Details = "HugsLib depends on Harmony and must be loaded after it.",
                PackageId = "unlimitedhugs.hugslib"
            });
        }

        // Rule 4: Framework mods loaded after content mods
        var lastFrameworkOrder = -1;
        foreach (var mod in mods)
        {
            if (frameworkIds.Contains(mod.PackageId))
                lastFrameworkOrder = Math.Max(lastFrameworkOrder, mod.LoadOrder);
        }

        if (lastFrameworkOrder >= 0)
        {
            var contentBeforeFramework = mods
                .Where(m => m.LoadOrder < lastFrameworkOrder
                         && !frameworkIds.Contains(m.PackageId)
                         && !CoreExpansionIds.Contains(m.PackageId)
                         && m.Category is ModCategory.Content or ModCategory.Gameplay)
                .ToList();

            if (contentBeforeFramework.Count > 0)
            {
                warnings.Add(new LoadOrderWarning
                {
                    Level = LoadOrderWarning.Severity.Warning,
                    Message = $"{contentBeforeFramework.Count} content mod{(contentBeforeFramework.Count != 1 ? "s" : "")} loaded before framework mods",
                    Details = $"Mods like \"{contentBeforeFramework[0].Name}\" should load after all framework/library mods.",
                });
            }
        }

        // Rule 5: DLCs should be right after Core
        var dlcMods = mods.Where(m => CoreExpansionIds.Contains(m.PackageId) && m.PackageId != "ludeon.rimworld").ToList();
        if (dlcMods.Count > 0)
        {
            var firstNonDlc = mods.FirstOrDefault(m => !CoreExpansionIds.Contains(m.PackageId));
            if (firstNonDlc != null)
            {
                var dlcAfterMods = dlcMods.Where(d => d.LoadOrder > firstNonDlc.LoadOrder).ToList();
                if (dlcAfterMods.Count > 0)
                {
                    warnings.Add(new LoadOrderWarning
                    {
                        Level = LoadOrderWarning.Severity.Warning,
                        Message = $"DLC \"{dlcAfterMods[0].Name}\" not in expected position",
                        Details = "DLC expansions should be loaded right after Core, before other mods.",
                        PackageId = dlcAfterMods[0].PackageId
                    });
                }
            }
        }

        return warnings;
    }

    // ──────────────────────────────────────────────────────────────
    // Performance Monitor IPC
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the debug overlay settings IPC file consumed by the
    /// FlexTool Debug Info in-game mod.
    /// </summary>
    public static void WriteDebugOverlaySettings(bool enabled, bool fps, bool memory, bool tickRate, bool pawnStats, bool saveName = true, bool alerts = false)
    {
        try
        {
            var path = Path.Combine(UserDataPath, "FlexToolDebugOverlay.txt");
            File.WriteAllLines(path, new[]
            {
                $"Enabled={(enabled ? 1 : 0)}",
                $"Fps={(fps ? 1 : 0)}",
                $"Memory={(memory ? 1 : 0)}",
                $"TickRate={(tickRate ? 1 : 0)}",
                $"PawnStats={(pawnStats ? 1 : 0)}",
                $"SaveName={(saveName ? 1 : 0)}",
                $"Alerts={(alerts ? 1 : 0)}"
            });
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>Reads the debug overlay settings, returning defaults if missing.</summary>
    public static (bool Enabled, bool Fps, bool Memory, bool TickRate, bool PawnStats, bool SaveName, bool Alerts) ReadDebugOverlaySettings()
    {
        bool enabled = false, fps = true, memory = true, tickRate = true, pawnStats = true, saveName = true, alerts = false;
        try
        {
            var path = Path.Combine(UserDataPath, "FlexToolDebugOverlay.txt");
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    bool val = parts[1].Trim() == "1";
                    switch (parts[0].Trim())
                    {
                        case "Enabled": enabled = val; break;
                        case "Fps": fps = val; break;
                        case "Memory": memory = val; break;
                        case "TickRate": tickRate = val; break;
                        case "PawnStats": pawnStats = val; break;
                        case "SaveName": saveName = val; break;
                        case "Alerts": alerts = val; break;
                    }
                }
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        return (enabled, fps, memory, tickRate, pawnStats, saveName, alerts);
    }

    // ─────────────────────────────────────────────────────────────────────
    // CrashGuard IPC — in-game companion of the crash handler
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Enables/disables the in-game CrashGuard (heartbeat + exception interception + emergency autosave).</summary>
    public static void WriteCrashGuardSettings(bool enabled)
    {
        try
        {
            File.WriteAllText(Path.Combine(UserDataPath, "FlexToolCrashGuard.txt"),
                $"Enabled={(enabled ? 1 : 0)}\n");
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>
    /// Reads the latest protective-action event reported by the in-game
    /// CrashGuard (crash prevented, freeze prevented, freeze warning...).
    /// Returns null when no event file exists.
    /// </summary>
    public static (DateTime Utc, string Kind, string Detail)? ReadCrashGuardEvent()
    {
        try
        {
            var path = Path.Combine(UserDataPath, "FlexToolCrashGuardEvent.txt");
            if (!File.Exists(path)) return null;

            DateTime utc = default;
            string kind = "", detail = "";
            foreach (var line in File.ReadAllLines(path))
            {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var key = line.Substring(0, idx);
                var val = line.Substring(idx + 1);
                if (key == "utc") DateTime.TryParse(val, null, System.Globalization.DateTimeStyles.RoundtripKind, out utc);
                else if (key == "kind") kind = val;
                else if (key == "detail") detail = val;
            }
            return utc == default ? null : (utc, kind, detail);
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads the in-game heartbeat written every second by the Debug Info mod.
    /// Returns the UTC timestamp, game tick, and whether the mod's in-process
    /// stall watchdog has flagged a stuck main thread; null when unavailable.
    /// </summary>
    public static (DateTime Utc, long Tick, bool Stalled)? ReadHeartbeat()
    {
        try
        {
            var path = Path.Combine(UserDataPath, "FlexToolHeartbeat.txt");
            if (!File.Exists(path)) return null;

            DateTime utc = default;
            long tick = -1;
            bool stalled = false;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                if (parts[0] == "utc") DateTime.TryParse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out utc);
                else if (parts[0] == "tick") long.TryParse(parts[1], out tick);
                else if (parts[0] == "stalled") stalled = parts[1].Trim() == "1";
            }
            return utc == default ? null : (utc, tick, stalled);
        }
        catch { return null; }
    }

    public sealed class PerformanceSnapshot
    {
        public float Fps { get; init; }
        public float MemUsedMB { get; init; }
        public float MemReservedMB { get; init; }
        public int GC0 { get; init; }
        public int GC1 { get; init; }
        public int GC2 { get; init; }
        public DateTime Timestamp { get; init; }
    }

    /// <summary>
    /// Reads the performance data file written by the in-game PerformanceMonitorPatch.
    /// Returns null if the file doesn't exist or can't be read.
    /// </summary>
    public static PerformanceSnapshot? ReadPerformanceData()
    {
        try
        {
            if (!File.Exists(PerfDataPath)) return null;

            using var fs = new FileStream(PerfDataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var doc = XDocument.Parse(sr.ReadToEnd());
            var root = doc.Root;
            if (root is null) return null;

            return new PerformanceSnapshot
            {
                Fps = float.TryParse(root.Element("FPS")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) ? fps : 0,
                MemUsedMB = float.TryParse(root.Element("MemUsedMB")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mem) ? mem : 0,
                MemReservedMB = float.TryParse(root.Element("MemReservedMB")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var res) ? res : 0,
                GC0 = int.TryParse(root.Element("GC0")?.Value, out var gc0) ? gc0 : 0,
                GC1 = int.TryParse(root.Element("GC1")?.Value, out var gc1) ? gc1 : 0,
                GC2 = int.TryParse(root.Element("GC2")?.Value, out var gc2) ? gc2 : 0,
                Timestamp = DateTime.TryParse(root.Element("Timestamp")?.Value, out var ts) ? ts : DateTime.Now
            };
        }
        catch { return null; }
    }

    // ──────────────────────────────────────────────────────────────
    // Pawn Edit IPC
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a pawn edit command to the in-game PawnEditPatch via IPC XML file.
    /// </summary>
    public static void SendPawnEdit(string targetName, string? firstName, string? nickname, string? lastName,
        List<(string SkillName, int Level)>? skills)
    {
        try
        {
            var root = new XElement("FlexToolPawnEdit",
                new XElement("Version", IpcProtocolVersion),
                new XElement("TargetName", targetName));

            if (firstName != null) root.Add(new XElement("FirstName", firstName));
            if (nickname != null) root.Add(new XElement("Nickname", nickname));
            if (lastName != null) root.Add(new XElement("LastName", lastName));

            if (skills is { Count: > 0 })
            {
                var skillsEl = new XElement("Skills");
                foreach (var (name, level) in skills)
                {
                    skillsEl.Add(new XElement("Skill",
                        new XElement("Name", name),
                        new XElement("Level", level)));
                }
                root.Add(skillsEl);
            }

            new XDocument(root).Save(PawnEditPath);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>
    /// Reads and clears the pawn edit result file written by the in-game mod.
    /// </summary>
    public static (string Target, string Result)? ReadPawnEditResult()
    {
        try
        {
            if (!File.Exists(PawnEditResultPath)) return null;

            using var fs = new FileStream(PawnEditResultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var doc = XDocument.Parse(sr.ReadToEnd());
            fs.Close();

            File.Delete(PawnEditResultPath);

            var target = doc.Root?.Element("Target")?.Value ?? "";
            var result = doc.Root?.Element("Result")?.Value ?? "";
            return (target, result);
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns the total size in bytes of the Saves folder.
    /// </summary>
    public static long GetSavesFolderSize()
    {
        try
        {
            if (!Directory.Exists(SavesPath)) return 0;
            return Directory.EnumerateFiles(SavesPath, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }

    // ──────────────────────────────────────────────────────────────
    // Pawn Extractor — works directly on save files (.rws XML)
    // ──────────────────────────────────────────────────────────────

    public static readonly string PawnLibraryPath = Path.Combine(UserDataPath, "FlexToolPawnLibrary");

    public sealed class ExtractedPawnInfo
    {
        public string Name { get; init; } = "";
        public string SourceSave { get; init; } = "";
        public DateTime Extracted { get; init; }
        public string FilePath { get; init; } = "";
    }

    /// <summary>Returns the display names of colonists found in a save file.</summary>
    public static List<string> GetColonistsInSave(string savePath)
    {
        var result = new List<string>();
        try
        {
            var doc = XDocument.Load(savePath);
            foreach (var pawn in EnumerateColonistPawns(doc))
            {
                var name = GetPawnDisplayName(pawn);
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        return result;
    }

    private static IEnumerable<XElement> EnumerateColonistPawns(XDocument doc)
    {
        return doc.Descendants("thing")
            .Where(t => (string?)t.Attribute("Class") == "Pawn"
                && string.Equals(t.Element("def")?.Value, "Human", StringComparison.OrdinalIgnoreCase)
                && (t.Element("kindDef")?.Value?.Contains("Colonist", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private static string GetPawnDisplayName(XElement pawn)
    {
        var name = pawn.Element("name");
        if (name is null) return "";
        var nick = name.Element("nick")?.Value;
        var first = name.Element("first")?.Value;
        var last = name.Element("last")?.Value;
        if (!string.IsNullOrEmpty(nick)) return string.IsNullOrEmpty(last) ? nick : $"{nick} ({first} {last})".Trim();
        var full = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(full) ? (name.Value ?? "") : full;
    }

    /// <summary>
    /// Extracts the named colonist's XML from the save into the pawn library.
    /// Returns the library file path, or null on failure.
    /// </summary>
    public static string? ExtractPawn(string savePath, string colonistName)
    {
        try
        {
            var doc = XDocument.Load(savePath);
            var pawn = EnumerateColonistPawns(doc)
                .FirstOrDefault(p => GetPawnDisplayName(p) == colonistName);
            if (pawn is null) return null;

            Directory.CreateDirectory(PawnLibraryPath);
            var safeName = string.Concat(colonistName.Split(Path.GetInvalidFileNameChars()));
            var file = Path.Combine(PawnLibraryPath, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.flexpawn");

            var wrapper = new XElement("FlexToolPawn",
                new XElement("Name", colonistName),
                new XElement("SourceSave", Path.GetFileNameWithoutExtension(savePath)),
                new XElement("Extracted", DateTime.Now.ToString("o")),
                new XElement("PawnData", new XElement(pawn)));

            new XDocument(wrapper).Save(file);
            return file;
        }
        catch { return null; }
    }

    /// <summary>Lists all pawns in the extraction library, newest first.</summary>
    public static List<ExtractedPawnInfo> GetExtractedPawns()
    {
        var result = new List<ExtractedPawnInfo>();
        try
        {
            if (!Directory.Exists(PawnLibraryPath)) return result;
            foreach (var file in Directory.EnumerateFiles(PawnLibraryPath, "*.flexpawn"))
            {
                try
                {
                    var doc = XDocument.Load(file);
                    result.Add(new ExtractedPawnInfo
                    {
                        Name = doc.Root?.Element("Name")?.Value ?? Path.GetFileNameWithoutExtension(file),
                        SourceSave = doc.Root?.Element("SourceSave")?.Value ?? "",
                        Extracted = DateTime.TryParse(doc.Root?.Element("Extracted")?.Value, out var dt) ? dt : File.GetLastWriteTime(file),
                        FilePath = file
                    });
                }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        return result.OrderByDescending(p => p.Extracted).ToList();
    }

    /// <summary>Deletes an extracted pawn from the library.</summary>
    public static bool DeleteExtractedPawn(string filePath)
    {
        try { File.Delete(filePath); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Injects a COPY of an extracted pawn into the target save next to an
    /// existing colonist. All thing ids inside the pawn subtree (the pawn
    /// itself plus its apparel, equipment and inventory) are renumbered to
    /// values unused by the target save, and every internal reference is
    /// rewritten to match, so nothing collides with or dangles into the old
    /// save. A labeled backup of the target save is created first.
    /// Returns null on success, otherwise an error message.
    /// </summary>
    public static string? SpawnPawnIntoSave(string pawnFilePath, string targetSavePath)
    {
        try
        {
            var pawnDoc = XDocument.Load(pawnFilePath);
            var srcPawn = pawnDoc.Root?.Element("PawnData")?.Element("thing");
            if (srcPawn is null) return "Pawn file is invalid.";

            var saveDoc = XDocument.Load(targetSavePath);
            var anchor = EnumerateColonistPawns(saveDoc).FirstOrDefault();
            if (anchor is null) return "No existing colonist found in the target save to anchor the spawn.";

            // Backup before modifying so the original can always be restored
            BackupSaveFile(targetSavePath, "PawnImport");

            // Work on a deep copy — the library file is never modified
            var newPawn = new XElement(srcPawn);

            // ── 1) Renumber every thing id in the pawn subtree ──────────
            // RimWorld ids look like "Human12345" / "Apparel_Duster67".
            // References to them elsewhere use the "Thing_" prefix
            // ("Thing_Human12345"). Both forms must be rewritten together.
            long nextId = 1;
            foreach (var idEl in saveDoc.Descendants("id"))
            {
                var m = System.Text.RegularExpressions.Regex.Match(idEl.Value ?? "", @"(\d+)$");
                if (m.Success && long.TryParse(m.Value, out var n) && n >= nextId)
                    nextId = n + 1;
            }

            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var idEl in newPawn.DescendantsAndSelf().Where(e => e.Name.LocalName == "id"))
            {
                var oldId = idEl.Value;
                if (string.IsNullOrEmpty(oldId) || idMap.ContainsKey(oldId)) continue;
                var prefix = System.Text.RegularExpressions.Regex.Replace(oldId, @"\d+$", "");
                idMap[oldId] = prefix + nextId++;
            }

            // ── 2) Rewrite ids and all internal references consistently ─
            foreach (var el in newPawn.DescendantsAndSelf().Where(e => !e.HasElements))
            {
                var value = el.Value;
                if (string.IsNullOrEmpty(value)) continue;
                foreach (var pair in idMap)
                {
                    if (value == pair.Key) { value = pair.Value; break; }
                    if (value == "Thing_" + pair.Key) { value = "Thing_" + pair.Value; break; }
                }
                if (!ReferenceEquals(value, el.Value) && value != el.Value) el.Value = value;
            }

            // ── 3) Adopt the target colony's faction and map position ───
            var faction = anchor.Element("faction")?.Value;
            if (faction is not null) newPawn.SetElementValue("faction", faction);
            var map = anchor.Element("map")?.Value;
            if (map is not null) newPawn.SetElementValue("map", map);
            var pos = anchor.Element("pos")?.Value;
            if (pos is not null) newPawn.SetElementValue("pos", pos);

            // ── 4) Sanitize state that points into the old save ─────────
            // Clear tracker CONTENTS instead of deleting the elements —
            // RimWorld expects the nodes to exist for spawned pawns and
            // regenerates their internals on load.
            newPawn.Element("jobs")?.RemoveNodes();
            newPawn.Element("pather")?.RemoveNodes();
            newPawn.Element("stances")?.RemoveNodes();
            newPawn.Element("mindState")?.RemoveNodes();
            newPawn.Element("roping")?.Remove();
            newPawn.Element("duty")?.Remove();
            newPawn.Element("connections")?.Remove();

            // Social relations and thought memories reference pawns from the
            // old save by loadID — those pawns don't exist here, so drop them.
            newPawn.Element("social")?.Element("directRelations")?.RemoveNodes();
            newPawn.Element("needs")?.Element("mood")?.Element("thoughts")?
                .Element("memories")?.Element("memories")?.RemoveNodes();

            // Guest/ownership state belongs to the old colony
            newPawn.Element("guest")?.RemoveNodes();
            newPawn.Element("ownership")?.RemoveNodes();

            anchor.AddAfterSelf(newPawn);
            saveDoc.Save(targetSavePath);
            InvalidateSaveCache();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Live Pawn Extractor IPC — talks to the Debug Info mod DLL.
    // Extraction/spawning happens INSIDE the running game against the
    // currently loaded save.
    // ─────────────────────────────────────────────────────────────

    public static readonly string LivePawnLibraryPath = Path.Combine(UserDataPath, "FlexToolPawnLibrary");

    /// <summary>Asks the in-game mod to extract the named colonist from the CURRENTLY LOADED save.</summary>
    public static bool SendLivePawnExtract(string pawnName)
    {
        try
        {
            File.WriteAllText(Path.Combine(UserDataPath, "FlexToolPawnExtract.txt"),
                "command=extract\n" +
                $"pawn={pawnName}\n" +
                $"utc={DateTime.UtcNow:O}\n");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendLivePawnExtract failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Asks the in-game mod to spawn a library pawn into the CURRENTLY LOADED save.</summary>
    public static bool SendLivePawnSpawn(string pawnFilePath)
    {
        try
        {
            File.WriteAllText(Path.Combine(UserDataPath, "FlexToolPawnExtract.txt"),
                "command=spawn\n" +
                $"file={pawnFilePath}\n" +
                $"utc={DateTime.UtcNow:O}\n");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendLivePawnSpawn failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Reads the latest result written by the in-game pawn extractor; null when none.</summary>
    public static (DateTime Utc, string Status, string Detail)? ReadLivePawnResult()
    {
        try
        {
            var path = Path.Combine(UserDataPath, "FlexToolPawnExtractResult.txt");
            if (!File.Exists(path)) return null;

            DateTime utc = default;
            string status = "", detail = "";
            foreach (var line in File.ReadAllLines(path))
            {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var key = line.Substring(0, idx);
                var val = line.Substring(idx + 1);
                if (key == "utc") DateTime.TryParse(val, null, System.Globalization.DateTimeStyles.RoundtripKind, out utc);
                else if (key == "status") status = val;
                else if (key == "detail") detail = val;
            }
            return utc == default ? null : (utc, status, detail);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReadLivePawnResult failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Lists .pawnx pawns extracted live by the in-game mod, newest first.</summary>
    public static List<ExtractedPawnInfo> GetLiveExtractedPawns()
    {
        var result = new List<ExtractedPawnInfo>();
        try
        {
            if (!Directory.Exists(LivePawnLibraryPath)) return result;
            foreach (var file in Directory.EnumerateFiles(LivePawnLibraryPath, "*.pawnx"))
            {
                var raw = Path.GetFileNameWithoutExtension(file);
                // Strip the trailing _yyyyMMdd_HHmmss stamp for display
                var name = System.Text.RegularExpressions.Regex.Replace(raw, @"_\d{8}_\d{6}$", "");
                result.Add(new ExtractedPawnInfo
                {
                    Name = name,
                    SourceSave = "(extracted in-game)",
                    Extracted = File.GetLastWriteTime(file),
                    FilePath = file
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetLiveExtractedPawns failed: {ex.Message}");
        }
        return result.OrderByDescending(p => p.Extracted).ToList();
    }

    /// <summary>Path of the live dev log mirrored from the game by the Debug Info mod.</summary>
    public static string DevLogPath => Path.Combine(UserDataPath, "FlexToolDevLog.txt");

    /// <summary>Reads the tail of the in-game dev log (last maxChars characters); empty when unavailable.</summary>
    public static string ReadDevLogTail(int maxChars = 200_000)
    {
        try
        {
            var path = DevLogPath;
            if (!File.Exists(path)) return "";
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length > maxChars) fs.Seek(-maxChars, SeekOrigin.End);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReadDevLogTail failed: {ex.Message}");
            return "";
        }
    }
}
