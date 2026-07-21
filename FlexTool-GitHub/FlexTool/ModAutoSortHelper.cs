using System;
using System.Collections.Generic;
using System.Linq;

namespace FlexTool;

/// <summary>
/// Smart auto-sort system for mods with intelligent categorization and ordering.
/// Automatically groups and sorts mods by dependency and category relationships.
/// </summary>
public static class ModAutoSortHelper
{
    /// <summary>
    /// Known framework mods in their required load position, by exact package ID.
    /// Must match the framework IDs checked by RimWorldSaveReader.GetLoadOrderWarnings().
    /// </summary>
    private static readonly Dictionary<string, int> KnownFrameworkOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["brrainz.harmony"] = 0,
        ["zetrith.prepatcher"] = 1,
        ["unlimitedhugs.hugslib"] = 2,
        ["smashphil.xmlextensions"] = 3,
    };

    /// <summary>
    /// Official DLC expansions in release order (loaded right after Core).
    /// </summary>
    private static readonly Dictionary<string, int> DlcOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ludeon.rimworld.royalty"] = 0,
        ["ludeon.rimworld.ideology"] = 1,
        ["ludeon.rimworld.biotech"] = 2,
        ["ludeon.rimworld.anomaly"] = 3,
        ["ludeon.rimworld.odyssey"] = 4,
    };

    /// <summary>
    /// Framework keyword identifiers for fuzzy matching (library/framework mods only —
    /// deliberately excludes broad terms like "expansion" or "rimworld" that
    /// would misclassify content mods).
    /// </summary>
    private static readonly HashSet<string> FrameworkMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "harmony",
        "hugslib",
        "prepatcher",
        "xmlextensions",
        "framework",
        "modmanager",
        "modlistbackup",
    };

    /// <summary>
    /// Performance and essential infrastructure mods that should come early.
    /// </summary>
    private static readonly HashSet<string> EssentialMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "prepatcher",
        "hugslib",
        "harmony",
        "performance",
        "optimization",
        "fixes",
        "patch",
        "bugfix",
    };

    /// <summary>
    /// Cosmetic and non-critical mods that can go later.
    /// </summary>
    private static readonly HashSet<string> CosmeticMods = new(StringComparer.OrdinalIgnoreCase)
    {
        "texture",
        "retexture",
        "visual",
        "graphic",
        "shader",
        "appearance",
        "hair",
        "apparel",
        "color",
        "theme",
        "ui",
        "interface",
        "menu",
    };

    /// <summary>
    /// Sorts mods intelligently by category and dependency relationships.
    /// Order: Core → DLCs → Known Frameworks (Harmony first) → Other Frameworks
    /// → Essential → Content → Other → Cosmetic.
    /// Produces an order that satisfies RimWorldSaveReader.GetLoadOrderWarnings().
    /// </summary>
    public static List<RimWorldSaveReader.ModInfo> AutoSort(IEnumerable<RimWorldSaveReader.ModInfo> mods)
    {
        var modList = mods.ToList();
        if (modList.Count == 0) return modList;

        // Group mods by load-order tier
        var core = new List<RimWorldSaveReader.ModInfo>();
        var dlc = new List<RimWorldSaveReader.ModInfo>();
        var knownFramework = new List<RimWorldSaveReader.ModInfo>();
        var framework = new List<RimWorldSaveReader.ModInfo>();
        var essential = new List<RimWorldSaveReader.ModInfo>();
        var content = new List<RimWorldSaveReader.ModInfo>();
        var cosmetic = new List<RimWorldSaveReader.ModInfo>();
        var other = new List<RimWorldSaveReader.ModInfo>();

        foreach (var mod in modList)
        {
            if (mod.Source == RimWorldSaveReader.ModSource.Core)
                core.Add(mod);
            else if (DlcOrder.ContainsKey(mod.PackageId) || mod.Source == RimWorldSaveReader.ModSource.DLC)
                dlc.Add(mod);
            else if (KnownFrameworkOrder.ContainsKey(mod.PackageId))
                knownFramework.Add(mod);
            else if (IsFrameworkMod(mod))
                framework.Add(mod);
            else if (IsEssentialMod(mod))
                essential.Add(mod);
            else if (IsCosmeticMod(mod))
                cosmetic.Add(mod);
            else if (mod.Category == RimWorldSaveReader.ModCategory.Content)
                content.Add(mod);
            else
                other.Add(mod);
        }

        // Sort each tier: DLCs by release order, known frameworks by dependency
        // order (Harmony before HugsLib), everything else A-Z by name
        dlc.Sort((a, b) => GetDlcPriority(a.PackageId).CompareTo(GetDlcPriority(b.PackageId)));
        knownFramework.Sort((a, b) => KnownFrameworkOrder[a.PackageId].CompareTo(KnownFrameworkOrder[b.PackageId]));
        framework.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        essential.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        content.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        other.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        cosmetic.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // Combine in optimal order
        var result = new List<RimWorldSaveReader.ModInfo>();
        result.AddRange(core);           // Core first (required)
        result.AddRange(dlc);            // DLCs right after Core (required)
        result.AddRange(knownFramework); // Harmony → Prepatcher → HugsLib → XmlExtensions
        result.AddRange(framework);      // Other framework/library mods
        result.AddRange(essential);      // Essential infrastructure
        result.AddRange(content);        // Content mods
        result.AddRange(other);          // Other mods
        result.AddRange(cosmetic);       // Cosmetic last (lowest priority)

        return result;
    }

    private static int GetDlcPriority(string packageId) =>
        DlcOrder.TryGetValue(packageId, out var order) ? order : int.MaxValue;

    /// <summary>
    /// Intelligently sorts mods with respect to known dependency relationships.
    /// Prioritizes: Core → DLC → Framework → By Category → By Name
    /// </summary>
    public static List<RimWorldSaveReader.ModInfo> SmartSort(IEnumerable<RimWorldSaveReader.ModInfo> mods)
    {
        return mods
            // First: Core mods
            .OrderBy(m => m.Source != RimWorldSaveReader.ModSource.Core)
            // Second: DLC expansions in release order
            .ThenBy(m => m.Source != RimWorldSaveReader.ModSource.DLC)
            .ThenBy(m => GetDlcPriority(m.PackageId))
            // Third: Known frameworks in dependency order (Harmony first)
            .ThenBy(m => KnownFrameworkOrder.TryGetValue(m.PackageId, out var o) ? o : int.MaxValue)
            // Fourth: Framework and essential
            .ThenBy(m => !(IsFrameworkMod(m) || IsEssentialMod(m)))
            // Fifth: Category importance
            .ThenBy(m => GetCategoryPriority(m.Category))
            // Sixth: Cosmeticity
            .ThenBy(m => IsCosmeticMod(m))
            // Finally: By name
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Analyzes mod dependencies and suggests optimal load order.
    /// This is a heuristic-based approach without full dependency resolution.
    /// </summary>
    public static List<RimWorldSaveReader.ModInfo> AnalyzeDependencies(
        IEnumerable<RimWorldSaveReader.ModInfo> mods,
        List<RimWorldSaveReader.ModConflict>? conflicts = null)
    {
        var result = AutoSort(mods);

        // If conflicts are found, push conflicting mods later so their patches win.
        // Never move Core, DLC, or framework mods — that would break the required
        // load order and re-introduce load-order warnings.
        if (conflicts != null && conflicts.Count > 0)
        {
            var conflictingMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var conflict in conflicts)
            {
                foreach (var (packageId, _, _) in conflict.InvolvedMods)
                {
                    conflictingMods.Add(packageId);
                }
            }

            bool IsPinned(RimWorldSaveReader.ModInfo m) =>
                m.Source is RimWorldSaveReader.ModSource.Core or RimWorldSaveReader.ModSource.DLC
                || DlcOrder.ContainsKey(m.PackageId)
                || KnownFrameworkOrder.ContainsKey(m.PackageId)
                || IsFrameworkMod(m);

            var movable = result.Where(m => conflictingMods.Contains(m.PackageId) && !IsPinned(m)).ToList();
            if (movable.Count > 0)
            {
                var remaining = result.Where(m => !movable.Contains(m)).ToList();
                remaining.AddRange(movable);
                result = remaining;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the sort priority for a mod category (lower = higher priority).
    /// </summary>
    private static int GetCategoryPriority(RimWorldSaveReader.ModCategory category) =>
        category switch
        {
            RimWorldSaveReader.ModCategory.Framework => 0,
            RimWorldSaveReader.ModCategory.Content => 1,
            RimWorldSaveReader.ModCategory.Gameplay => 2,
            RimWorldSaveReader.ModCategory.QoL => 3,
            RimWorldSaveReader.ModCategory.Visuals => 4,
            RimWorldSaveReader.ModCategory.UI => 5,
            RimWorldSaveReader.ModCategory.Textures => 6,
            _ => 7
        };

    /// <summary>
    /// Checks if a mod is a framework or core dependency.
    /// </summary>
    private static bool IsFrameworkMod(RimWorldSaveReader.ModInfo mod)
    {
        var text = $"{mod.Name} {mod.PackageId} {mod.Author}".ToLowerInvariant();
        return FrameworkMods.Any(fw => text.Contains(fw)) || 
               mod.Category == RimWorldSaveReader.ModCategory.Framework;
    }

    /// <summary>
    /// Checks if a mod is essential infrastructure or fixes.
    /// </summary>
    private static bool IsEssentialMod(RimWorldSaveReader.ModInfo mod)
    {
        var text = $"{mod.Name} {mod.PackageId}".ToLowerInvariant();
        return EssentialMods.Any(ess => text.Contains(ess));
    }

    /// <summary>
    /// Checks if a mod is primarily cosmetic and non-critical.
    /// </summary>
    private static bool IsCosmeticMod(RimWorldSaveReader.ModInfo mod)
    {
        var text = $"{mod.Name} {mod.PackageId} {mod.Category}".ToLowerInvariant();
        return CosmeticMods.Any(cos => text.Contains(cos));
    }

    /// <summary>
    /// Gets a description of the auto-sort strategy.
    /// </summary>
    public static string GetSortStrategy() =>
        """
        Smart Auto-Sort Strategy:

        1. CORE (Immutable)
           └─ RimWorld core files

        2. DLC EXPANSIONS (Release order)
           └─ Royalty, Ideology, Biotech, Anomaly, Odyssey

        3. FRAMEWORK & DEPENDENCIES
           └─ Harmony, Prepatcher, HugsLib, XML Extensions, etc.

        4. ESSENTIAL INFRASTRUCTURE
           └─ Performance fixes, patches, optimizations

        5. CONTENT MODS
           └─ New features, gameplay additions

        6. OTHER MODS
           └─ Miscellaneous additions

        7. COSMETIC MODS (Last)
           └─ Textures, UI, appearances

        Within each tier: sorted A-Z by name
        """;
}
