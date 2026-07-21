using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace FlexTool;

/// <summary>
/// Analyzes mod conflicts and patches.
/// Separates conflict detection logic from UI code.
/// </summary>
public class ModConflictAnalyzer
{
    private readonly IModLogger _logger;

    public ModConflictAnalyzer(IModLogger logger = null)
    {
        _logger = logger ?? new NoOpLogger();
    }

    /// <summary>
    /// Scans all active mods for patches and detects conflicts.
    /// </summary>
    public List<RimWorldSaveReader.ModConflict> AnalyzeConflicts(List<RimWorldSaveReader.ModInfo> activeMods)
    {
        try
        {
            var conflicts = new List<RimWorldSaveReader.ModConflict>();
            var patchMap = new Dictionary<string, List<(string PackageId, string ModName, string PatchFile, string Op)>>(
                StringComparer.OrdinalIgnoreCase);

            // Scan all patch files
            foreach (var mod in activeMods)
            {
                if (string.IsNullOrEmpty(mod.FolderPath) || !Directory.Exists(mod.FolderPath))
                {
                    _logger.LogWarning($"Mod folder not found for {mod.PackageId}: {mod.FolderPath}");
                    continue;
                }

                ScanModForPatches(mod, patchMap);
            }

            // Find conflicts (any xpath targeted by 2+ mods)
            foreach (var (xpath, entries) in patchMap)
            {
                var distinctMods = entries
                    .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (distinctMods.Count < 2)
                    continue;

                var defName = ExtractDefNameFromXPath(xpath);

                conflicts.Add(new RimWorldSaveReader.ModConflict
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
            _logger.LogInfo($"Found {sorted.Count} mod conflicts");
            return sorted;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error analyzing conflicts: {ex.Message}");
            return new List<RimWorldSaveReader.ModConflict>();
        }
    }

    /// <summary>
    /// Scans a single mod for patch files and adds them to the patch map.
    /// </summary>
    private void ScanModForPatches(
        RimWorldSaveReader.ModInfo mod,
        Dictionary<string, List<(string PackageId, string ModName, string PatchFile, string Op)>> patchMap)
    {
        try
        {
            var patchRoots = new List<string>();

            // Main Patches folder
            var mainPatches = Path.Combine(mod.FolderPath, "Patches");
            if (Directory.Exists(mainPatches))
                patchRoots.Add(mainPatches);

            // Versioned patch folders (e.g., 1.5/Patches)
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(mod.FolderPath))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.Length >= 3 && char.IsDigit(dirName[0]) && dirName[1] == '.')
                    {
                        var versionPatches = Path.Combine(dir, "Patches");
                        if (Directory.Exists(versionPatches))
                            patchRoots.Add(versionPatches);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error scanning versioned patches for {mod.PackageId}: {ex.Message}");
            }

            // Scan each patch root
            foreach (var patchRoot in patchRoots)
            {
                try
                {
                    foreach (var xmlFile in Directory.EnumerateFiles(patchRoot, "*.xml", SearchOption.AllDirectories))
                    {
                        try
                        {
                            ScanPatchFile(xmlFile, mod.PackageId, mod.Name, patchMap);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error scanning patch file {xmlFile}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error enumerating patch files in {patchRoot}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error scanning mod {mod.PackageId} for patches: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a patch XML file and extracts XPath targets.
    /// </summary>
    private void ScanPatchFile(
        string xmlPath,
        string packageId,
        string modName,
        Dictionary<string, List<(string PackageId, string ModName, string PatchFile, string Op)>> patchMap)
    {
        try
        {
            var doc = XDocument.Load(xmlPath);
            if (doc.Root?.Elements() == null)
                return;

            var fileName = Path.GetFileName(xmlPath);

            foreach (var operation in doc.Root.Elements())
            {
                var opName = operation.Name.LocalName;

                // Valid RimWorld patch operations
                if (!IsValidPatchOperation(opName))
                    continue;

                var xpathElement = operation.Element("xpath");
                var xpath = xpathElement?.Value?.Trim();

                if (string.IsNullOrEmpty(xpath))
                    continue;

                if (!patchMap.TryGetValue(xpath, out var list))
                {
                    list = new List<(string, string, string, string)>();
                    patchMap[xpath] = list;
                }

                list.Add((packageId, modName, fileName, opName));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error parsing patch file {xmlPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if an operation is a valid RimWorld patch operation.
    /// </summary>
    private static bool IsValidPatchOperation(string opName)
    {
        return opName is
            "PatchOperationReplace" or
            "PatchOperationAdd" or
            "PatchOperationRemove" or
            "PatchOperationInsert" or
            "PatchOperationAttributeSet" or
            "PatchOperationAttributeAdd" or
            "PatchOperationSetName";
    }

    /// <summary>
    /// Extracts a human-readable def name from an XPath expression.
    /// </summary>
    private static string ExtractDefNameFromXPath(string xpath)
    {
        try
        {
            // Pattern: /Defs/ThingDef[@Name="Apparel_BasicShirt"]/...
            if (xpath.Contains("@Name=\""))
            {
                var startIdx = xpath.IndexOf("@Name=\"") + 7;
                var endIdx = xpath.IndexOf("\"", startIdx);
                if (endIdx > startIdx)
                    return xpath[startIdx..endIdx];
            }

            // Pattern: /Defs/ThingDef[defName="Apparel_BasicShirt"]/...
            if (xpath.Contains("defName=\""))
            {
                var startIdx = xpath.IndexOf("defName=\"") + 9;
                var endIdx = xpath.IndexOf("\"", startIdx);
                if (endIdx > startIdx)
                    return xpath[startIdx..endIdx];
            }

            // Fallback: extract last element
            var parts = xpath.Split('/');
            return parts.LastOrDefault() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
}

/// <summary>
/// Logger interface for mod conflict analysis.
/// </summary>
public interface IModLogger
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
}

/// <summary>
/// No-op logger implementation (does nothing).
/// </summary>
public class NoOpLogger : IModLogger
{
    public void LogInfo(string message) { }
    public void LogWarning(string message) { }
    public void LogError(string message) { }
}

/// <summary>
/// Console logger implementation (for debugging).
/// </summary>
public class ConsoleModLogger : IModLogger
{
    public void LogInfo(string message) => Console.WriteLine($"[INFO] {message}");
    public void LogWarning(string message) => Console.WriteLine($"[WARN] {message}");
    public void LogError(string message) => Console.WriteLine($"[ERROR] {message}");
}
