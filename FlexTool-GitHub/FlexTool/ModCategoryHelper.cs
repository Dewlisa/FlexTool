using System.Windows.Media;

namespace FlexTool;

/// <summary>
/// Helper class for mod source and category enum to display logic conversions.
/// Centralizes classification and styling logic.
/// </summary>
public static class ModCategoryHelper
{
    /// <summary>
    /// Gets the display label and color for a mod source.
    /// </summary>
    public static (string Label, Color Color) GetModSourceStyle(RimWorldSaveReader.ModSource source)
    {
        return source switch
        {
            RimWorldSaveReader.ModSource.Core => ("CORE", ModManagerConstants.ModSourceColors.Core),
            RimWorldSaveReader.ModSource.DLC => ("DLC", ModManagerConstants.ModSourceColors.DLC),
            RimWorldSaveReader.ModSource.Workshop => ("WORKSHOP", ModManagerConstants.ModSourceColors.Workshop),
            RimWorldSaveReader.ModSource.Local => ("LOCAL", ModManagerConstants.ModSourceColors.Local),
            _ => ("UNKNOWN", ModManagerConstants.ModSourceColors.Unknown),
        };
    }

    /// <summary>
    /// Gets the display label and color for a mod category.
    /// </summary>
    public static (string Label, Color Color) GetModCategoryStyle(RimWorldSaveReader.ModCategory category)
    {
        return category switch
        {
            RimWorldSaveReader.ModCategory.Framework => ("FRAMEWORK", ModManagerConstants.ModCategoryColors.Framework),
            RimWorldSaveReader.ModCategory.Visuals => ("VISUALS", ModManagerConstants.ModCategoryColors.Visuals),
            RimWorldSaveReader.ModCategory.Textures => ("TEXTURES", ModManagerConstants.ModCategoryColors.Textures),
            RimWorldSaveReader.ModCategory.Content => ("CONTENT", ModManagerConstants.ModCategoryColors.Content),
            RimWorldSaveReader.ModCategory.Gameplay => ("GAMEPLAY", ModManagerConstants.ModCategoryColors.Gameplay),
            RimWorldSaveReader.ModCategory.QoL => ("QOL", ModManagerConstants.ModCategoryColors.QoL),
            RimWorldSaveReader.ModCategory.UI => ("UI", ModManagerConstants.ModCategoryColors.UI),
            _ => ("MOD", ModManagerConstants.ModCategoryColors.Unknown),
        };
    }

    /// <summary>
    /// Gets the description for a mod category.
    /// </summary>
    public static string GetCategoryDescription(RimWorldSaveReader.ModCategory category)
    {
        return category switch
        {
            RimWorldSaveReader.ModCategory.Framework => "Core mod framework (essential for other mods)",
            RimWorldSaveReader.ModCategory.Visuals => "Visual/graphics enhancements",
            RimWorldSaveReader.ModCategory.Textures => "Texture and asset replacements",
            RimWorldSaveReader.ModCategory.Content => "New content (items, buildings, creatures, etc.)",
            RimWorldSaveReader.ModCategory.Gameplay => "Gameplay mechanics and balance changes",
            RimWorldSaveReader.ModCategory.QoL => "Quality of life improvements",
            RimWorldSaveReader.ModCategory.UI => "User interface improvements",
            _ => "Uncategorized mod",
        };
    }

    /// <summary>
    /// Checks if a mod search filter matches this category.
    /// </summary>
    public static bool CategoryMatchesFilter(RimWorldSaveReader.ModCategory category, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        var (label, _) = GetModCategoryStyle(category);
        return label.Contains(filter, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a mod source matches the search filter.
    /// </summary>
    public static bool SourceMatchesFilter(RimWorldSaveReader.ModSource source, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        var (label, _) = GetModSourceStyle(source);
        return label.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
               source.ToString().Contains(filter, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if a mod matches a search query across all searchable fields.
    /// </summary>
    public static bool ModMatchesSearchFilter(RimWorldSaveReader.ModInfo mod, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        return
            mod.Name.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            mod.PackageId.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            mod.Author.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            mod.Version.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            mod.SupportedVersions.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            CategoryMatchesFilter(mod.Category, filter);
    }

    /// <summary>
    /// Checks if the filter is a duplicate filter keyword.
    /// </summary>
    public static bool IsDuplicateFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return false;

        var lowerFilter = filter.ToLowerInvariant();
        return lowerFilter == "dupe" ||
               lowerFilter == "dupes" ||
               lowerFilter == "duplicate" ||
               lowerFilter == "duplicates";
    }
}
