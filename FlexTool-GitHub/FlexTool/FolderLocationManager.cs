using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlexTool;

/// <summary>
/// Manages all folder locations used by FlexTool.
/// Provides persistence, defaults, and auto-detection capabilities with comprehensive error handling.
/// </summary>
public static class FolderLocationManager
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlexTool", "folder-locations.json");

    private static FolderLocations _current = null!;
    private static readonly FolderLocations _defaults = new();

    static FolderLocationManager()
    {
        LoadOrCreateConfig();
    }

    /// <summary>Gets the current folder configuration.</summary>
    public static FolderLocations Current => _current;

    /// <summary>Gets the default folder configuration.</summary>
    public static FolderLocations Defaults => _defaults;

    /// <summary>Loads configuration from disk or creates defaults if not found.</summary>
    private static void LoadOrCreateConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _current = JsonSerializer.Deserialize<FolderLocations>(json) ?? new FolderLocations();
            }
            else
            {
                _current = new FolderLocations();
            }

            // Validate and sanitize paths
            _current.ValidatePaths();
            SaveConfig(); // persist any corrections (e.g., renamed mod folders)
        }
        catch
        {
            _current = new FolderLocations();
        }
    }

    /// <summary>Saves current configuration to disk with error handling.</summary>
    public static bool SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Resets all folder locations to detected defaults.</summary>
    public static bool ResetToDefaults()
    {
        try
        {
            _current = new FolderLocations();
            _current.AutoDetectAll();
            return SaveConfig();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Forces auto-detection of all folder locations.</summary>
    public static bool ForceAutoDetect()
    {
        try
        {
            _current.AutoDetectAll();
            return SaveConfig();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Updates a specific folder location with validation.</summary>
    public static (bool success, string errorMessage) UpdateFolder(string folderKey, string newPath)
    {
        if (string.IsNullOrEmpty(newPath))
            return (false, "Path cannot be empty.");

        try
        {
            // Verify the path is valid and accessible
            try
            {
                var pathInfo = new DirectoryInfo(newPath);
                if (!pathInfo.Exists)
                {
                    try
                    {
                        Directory.CreateDirectory(newPath);
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Cannot create folder: {ex.Message}");
                    }
                }
            }
            catch (ArgumentException ex)
            {
                return (false, $"Invalid path format: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return (false, $"Access denied: {ex.Message}");
            }

            _current.SetPath(folderKey, newPath);
            if (!SaveConfig())
                return (false, "Failed to save configuration.");

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Error updating folder: {ex.Message}");
        }
    }

    /// <summary>Gets a folder location by key, with fallback to default.</summary>
    public static string? GetFolder(string folderKey)
    {
        try
        {
            var path = _current.GetPath(folderKey);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;

            // Fallback to default if current path doesn't exist
            var defaultPath = _defaults.GetPath(folderKey);
            if (!string.IsNullOrEmpty(defaultPath))
            {
                _current.SetPath(folderKey, defaultPath);
                SaveConfig();
                return defaultPath;
            }

            return path;
        }
        catch
        {
            return _current.GetPath(folderKey);
        }
    }

    /// <summary>Gets all available folder keys organized by category.</summary>
    public static List<string> GetAllFolderKeys()
    {
        return new List<string>
        {
            FolderLocations.KEY_GAME_INSTALL,
            FolderLocations.KEY_GAME_MODS,
            FolderLocations.KEY_SAVES,
            FolderLocations.KEY_USER_DATA,
            FolderLocations.KEY_MODSCONFIG,
            FolderLocations.KEY_BACKUPS,
            FolderLocations.KEY_APP_SETTINGS,
            FolderLocations.KEY_PAWN_EDITOR,
            FolderLocations.KEY_PERF_DATA,
            FolderLocations.KEY_CRASH_REPORTS,
            FolderLocations.KEY_AUTOLOAD_MOD,
            FolderLocations.KEY_SPEED_MOD,
            FolderLocations.KEY_CHEATS_MOD,
            FolderLocations.KEY_PERF_MOD,
            FolderLocations.KEY_DEBUG_MOD
        };
    }

    /// <summary>Gets folder metadata (label, description, category).</summary>
    public static (string label, string description, string category) GetFolderInfo(string folderKey)
    {
        return folderKey switch
        {
            FolderLocations.KEY_GAME_INSTALL => ("RimWorld Installation", "Game executable and core files", "Game"),
            FolderLocations.KEY_GAME_MODS => ("Game Mods Folder", "Where all RimWorld mods are stored", "Game"),
            FolderLocations.KEY_SAVES => ("RimWorld Saves", "Colony save files for current game", "Game"),
            FolderLocations.KEY_USER_DATA => ("RimWorld User Data", "Game configuration and settings", "Game"),
            FolderLocations.KEY_MODSCONFIG => ("ModsConfig.xml", "Active mod list configuration", "Game"),
            FolderLocations.KEY_BACKUPS => ("Save Backups", "FlexTool backup history and recovery", "FlexTool"),
            FolderLocations.KEY_APP_SETTINGS => ("FlexTool Settings", "Application state and preferences", "FlexTool"),
            FolderLocations.KEY_PAWN_EDITOR => ("Pawn Editor Data", "Temporary pawn editing files", "FlexTool"),
            FolderLocations.KEY_PERF_DATA => ("Performance Data", "Performance monitoring records", "FlexTool"),
            FolderLocations.KEY_CRASH_REPORTS => ("Crash Reports", "Crash and exception reports captured by the Crash Handler", "FlexTool"),
            FolderLocations.KEY_AUTOLOAD_MOD => ("AutoLoad Mod", "FlexTool AutoLoad mod deployment", "Mods"),
            FolderLocations.KEY_SPEED_MOD => ("Speed Mod", "FlexTool Speed mod deployment", "Mods"),
            FolderLocations.KEY_CHEATS_MOD => ("Cheats Mod", "FlexTool Cheats mod deployment", "Mods"),
            FolderLocations.KEY_PERF_MOD => ("Performance Mod", "FlexTool Performance mod deployment", "Mods"),
            FolderLocations.KEY_DEBUG_MOD => ("Debug Info Mod", "FlexTool Debug info mod deployment", "Mods"),
            _ => ("Unknown", "", "")
        };
    }

    /// <summary>Checks if a folder was created by FlexTool (not part of original RimWorld install).</summary>
    public static bool IsFlexToolFolder(string folderKey)
    {
        return folderKey switch
        {
            FolderLocations.KEY_BACKUPS => true,
            FolderLocations.KEY_APP_SETTINGS => true,
            FolderLocations.KEY_PAWN_EDITOR => true,
            FolderLocations.KEY_PERF_DATA => true,
            FolderLocations.KEY_CRASH_REPORTS => true,
            FolderLocations.KEY_AUTOLOAD_MOD => true,
            FolderLocations.KEY_SPEED_MOD => true,
            FolderLocations.KEY_CHEATS_MOD => true,
            FolderLocations.KEY_PERF_MOD => true,
            FolderLocations.KEY_DEBUG_MOD => true,
            _ => false
        };
    }

    /// <summary>Gets the category color for UI display.</summary>
    public static (byte r, byte g, byte b) GetCategoryColor(string category)
    {
        return category switch
        {
            "Game" => (0x3A, 0x7B, 0xD5),      // Blue
            "FlexTool" => (0x4C, 0xA8, 0x4C),  // Green
            "Mods" => (0xE8, 0x8B, 0x4D),      // Orange
            _ => (0x6B, 0x7B, 0x9B)            // Gray
        };
    }

    /// <summary>True when the key points at a file (not a directory).</summary>
    public static bool IsFilePath(string folderKey)
    {
        return folderKey switch
        {
            FolderLocations.KEY_MODSCONFIG => true,
            FolderLocations.KEY_PAWN_EDITOR => true,
            FolderLocations.KEY_PERF_DATA => true,
            _ => false
        };
    }

    /// <summary>True when the key is a mod deployment folder that only exists after the mod is installed.</summary>
    public static bool IsModDeploymentFolder(string folderKey)
    {
        return folderKey switch
        {
            FolderLocations.KEY_AUTOLOAD_MOD => true,
            FolderLocations.KEY_SPEED_MOD => true,
            FolderLocations.KEY_CHEATS_MOD => true,
            FolderLocations.KEY_PERF_MOD => true,
            FolderLocations.KEY_DEBUG_MOD => true,
            _ => false
        };
    }

    /// <summary>Creates FlexTool-owned folders that are safe to create up front.</summary>
    public static void EnsureFlexToolFolders()
    {
        foreach (var key in new[] { FolderLocations.KEY_BACKUPS, FolderLocations.KEY_APP_SETTINGS })
        {
            try
            {
                var path = _current.GetPath(key);
                if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }
    }
}

/// <summary>
/// Contains all folder locations used by FlexTool.
/// Serializable to JSON for persistence.
/// </summary>
public class FolderLocations
{
    // Game folders
    public const string KEY_GAME_INSTALL = "GameInstall";
    public const string KEY_GAME_MODS = "GameMods";
    public const string KEY_SAVES = "Saves";
    public const string KEY_USER_DATA = "UserData";
    public const string KEY_MODSCONFIG = "ModsConfig";

    // FlexTool folders
    public const string KEY_BACKUPS = "Backups";
    public const string KEY_APP_SETTINGS = "AppSettings";
    public const string KEY_PAWN_EDITOR = "PawnEditor";
    public const string KEY_PERF_DATA = "PerfData";
    public const string KEY_CRASH_REPORTS = "CrashReports";

    // Mod deployment folders
    public const string KEY_AUTOLOAD_MOD = "AutoLoadMod";
    public const string KEY_SPEED_MOD = "SpeedMod";
    public const string KEY_CHEATS_MOD = "CheatsMod";
    public const string KEY_PERF_MOD = "PerfMod";
    public const string KEY_DEBUG_MOD = "DebugMod";

    [JsonPropertyName("gameInstall")]
    public string GameInstall { get; set; } = "";

    [JsonPropertyName("gameMods")]
    public string GameMods { get; set; } = "";

    [JsonPropertyName("saves")]
    public string Saves { get; set; } = "";

    [JsonPropertyName("userData")]
    public string UserData { get; set; } = "";

    [JsonPropertyName("modsConfig")]
    public string ModsConfig { get; set; } = "";

    [JsonPropertyName("backups")]
    public string Backups { get; set; } = "";

    [JsonPropertyName("appSettings")]
    public string AppSettings { get; set; } = "";

    [JsonPropertyName("pawnEditor")]
    public string PawnEditor { get; set; } = "";

    [JsonPropertyName("perfData")]
    public string PerfData { get; set; } = "";

    [JsonPropertyName("crashReports")]
    public string CrashReports { get; set; } = "";

    [JsonPropertyName("autoLoadMod")]
    public string AutoLoadMod { get; set; } = "";

    [JsonPropertyName("speedMod")]
    public string SpeedMod { get; set; } = "";

    [JsonPropertyName("cheatsMod")]
    public string CheatsMod { get; set; } = "";

    [JsonPropertyName("perfMod")]
    public string PerfMod { get; set; } = "";

    [JsonPropertyName("debugMod")]
    public string DebugMod { get; set; } = "";

    /// <summary>Auto-detects all folder locations based on system and game installation.</summary>
    public void AutoDetectAll()
    {
        try
        {
            var gameExe = RimWorldSaveReader.FindGameExePath();
            var gameDir = gameExe != null ? Path.GetDirectoryName(gameExe) : "";

            // Game folders
            GameInstall = gameDir ?? "";
            GameMods = !string.IsNullOrEmpty(gameDir) ? Path.Combine(gameDir, "Mods") : "";
            Saves = RimWorldSaveReader.SavesPath;
            UserData = RimWorldSaveReader.UserDataPath;
            ModsConfig = RimWorldSaveReader.ModsConfigPath;

            // FlexTool folders
            AppSettings = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlexTool");
            Backups = RimWorldSaveReader.BackupsPath;
            PawnEditor = Path.Combine(UserData, "FlexToolPawnEdit.xml");
            PerfData = Path.Combine(UserData, "FlexToolPerf.xml");
            CrashReports = Path.Combine(AppSettings, "CrashReports");

            // Mod deployment folders (all in game Mods folder)
            if (!string.IsNullOrEmpty(GameMods))
            {
                AutoLoadMod = Path.Combine(GameMods, "FlexToolAutoLoad");
                SpeedMod = Path.Combine(GameMods, "FlexToolSpeed");
                CheatsMod = Path.Combine(GameMods, "FlexToolCheats");
                PerfMod = Path.Combine(GameMods, "FlexToolPerf");
                DebugMod = Path.Combine(GameMods, "FlexToolDebugInfo");
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>Validates and sanitizes all paths.</summary>
    public void ValidatePaths()
    {
        try
        {
            var gameExe = RimWorldSaveReader.FindGameExePath();
            var gameDir = gameExe != null ? Path.GetDirectoryName(gameExe) : "";

            // Validate game folders
            GameInstall = ValidatePath(GameInstall, gameDir ?? "");
            GameMods = ValidatePath(GameMods, !string.IsNullOrEmpty(gameDir) ? Path.Combine(gameDir, "Mods") : "");
            Saves = ValidatePath(Saves, RimWorldSaveReader.SavesPath);
            UserData = ValidatePath(UserData, RimWorldSaveReader.UserDataPath);
            ModsConfig = ValidateFilePath(ModsConfig, RimWorldSaveReader.ModsConfigPath);

            // Validate FlexTool folders
            var appDefault = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlexTool");
            AppSettings = ValidatePath(AppSettings, appDefault);
            Backups = ValidatePath(Backups, RimWorldSaveReader.BackupsPath);
            PawnEditor = ValidateFilePath(PawnEditor, Path.Combine(UserData, "FlexToolPawnEdit.xml"));
            PerfData = ValidateFilePath(PerfData, Path.Combine(UserData, "FlexToolPerf.xml"));
            CrashReports = ValidatePath(CrashReports, Path.Combine(AppSettings, "CrashReports"));

            // Validate mod folders
            if (!string.IsNullOrEmpty(GameMods))
            {
                AutoLoadMod = ValidatePath(AutoLoadMod, Path.Combine(GameMods, "FlexToolAutoLoad"));
                SpeedMod = ValidatePath(SpeedMod, Path.Combine(GameMods, "FlexToolSpeed"));
                CheatsMod = ValidatePath(CheatsMod, Path.Combine(GameMods, "FlexToolCheats"));
                PerfMod = ValidatePath(PerfMod, Path.Combine(GameMods, "FlexToolPerf"));
                DebugMod = ValidatePath(DebugMod, Path.Combine(GameMods, "FlexToolDebugInfo"));
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static string ValidatePath(string current, string defaultPath)
    {
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
            return current;
        return defaultPath;
    }

    private static string ValidateFilePath(string current, string defaultPath)
    {
        if (!string.IsNullOrEmpty(current) && File.Exists(current))
            return current;
        return defaultPath;
    }

    /// <summary>Gets a path by key.</summary>
    public string? GetPath(string key)
    {
        return key switch
        {
            FolderLocations.KEY_GAME_INSTALL => GameInstall,
            FolderLocations.KEY_GAME_MODS => GameMods,
            FolderLocations.KEY_SAVES => Saves,
            FolderLocations.KEY_USER_DATA => UserData,
            FolderLocations.KEY_MODSCONFIG => ModsConfig,
            FolderLocations.KEY_BACKUPS => Backups,
            FolderLocations.KEY_APP_SETTINGS => AppSettings,
            FolderLocations.KEY_PAWN_EDITOR => PawnEditor,
            FolderLocations.KEY_PERF_DATA => PerfData,
            FolderLocations.KEY_CRASH_REPORTS => CrashReports,
            FolderLocations.KEY_AUTOLOAD_MOD => AutoLoadMod,
            FolderLocations.KEY_SPEED_MOD => SpeedMod,
            FolderLocations.KEY_CHEATS_MOD => CheatsMod,
            FolderLocations.KEY_PERF_MOD => PerfMod,
            FolderLocations.KEY_DEBUG_MOD => DebugMod,
            _ => null
        };
    }

    /// <summary>Sets a path by key.</summary>
    public void SetPath(string key, string path)
    {
        switch (key)
        {
            case FolderLocations.KEY_GAME_INSTALL:
                GameInstall = path;
                break;
            case FolderLocations.KEY_GAME_MODS:
                GameMods = path;
                break;
            case FolderLocations.KEY_SAVES:
                Saves = path;
                break;
            case FolderLocations.KEY_USER_DATA:
                UserData = path;
                break;
            case FolderLocations.KEY_MODSCONFIG:
                ModsConfig = path;
                break;
            case FolderLocations.KEY_BACKUPS:
                Backups = path;
                break;
            case FolderLocations.KEY_APP_SETTINGS:
                AppSettings = path;
                break;
            case FolderLocations.KEY_PAWN_EDITOR:
                PawnEditor = path;
                break;
            case FolderLocations.KEY_PERF_DATA:
                PerfData = path;
                break;
            case FolderLocations.KEY_CRASH_REPORTS:
                CrashReports = path;
                break;
            case FolderLocations.KEY_AUTOLOAD_MOD:
                AutoLoadMod = path;
                break;
            case FolderLocations.KEY_SPEED_MOD:
                SpeedMod = path;
                break;
            case FolderLocations.KEY_CHEATS_MOD:
                CheatsMod = path;
                break;
            case FolderLocations.KEY_PERF_MOD:
                PerfMod = path;
                break;
            case FolderLocations.KEY_DEBUG_MOD:
                DebugMod = path;
                break;
        }
    }
}
