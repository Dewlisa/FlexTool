using System.Windows.Media;

namespace FlexTool;

/// <summary>
/// Centralized constants for the Mod Manager UI and logic.
/// Reduces magic numbers and improves maintainability.
/// </summary>
public static class ModManagerConstants
{
    // ── Opacity and Visibility ──
    public const double INACTIVE_MOD_OPACITY = 0.75;
    public const double ACTIVE_MOD_OPACITY = 1.0;
    public const double TOOLTIP_OPACITY = 0.95;

    // ── Colors: Core ──
    public static class Colors
    {
        // Primary theme colors
        public static readonly Color PrimaryBlue = Color.FromRgb(0x3A, 0x7B, 0xD5);
        public static readonly Color LightBlue = Color.FromRgb(0x5A, 0x9A, 0xE6);
        public static readonly Color DarkBlue = Color.FromRgb(0x1E, 0x3A, 0x5F);

        // Semantic colors
        public static readonly Color SuccessGreen = Color.FromRgb(0x2E, 0xCC, 0x71);
        public static readonly Color WarningOrange = Color.FromRgb(0xF3, 0x9C, 0x12);
        public static readonly Color ErrorRed = Color.FromRgb(0xE7, 0x4C, 0x3C);
        public static readonly Color DuplicateRed = Color.FromRgb(0xE7, 0x4C, 0x3C);
        public static readonly Color ConflictPink = Color.FromRgb(0xE9, 0x1E, 0x63);

        // Neutral grays
        public static readonly Color DisabledGray = Color.FromRgb(0x60, 0x60, 0x60);
        public static readonly Color DesaturatedGray = Color.FromRgb(0x60, 0x60, 0x60);
        public static readonly Color BorderGray = Color.FromRgb(0x2A, 0x2A, 0x2E);

        // Text colors
        public static readonly Color TextPrimary = Color.FromRgb(0xC8, 0xC8, 0xCC);
        public static readonly Color TextSecondary = Color.FromRgb(0x6E, 0x6E, 0x78);
        public static readonly Color TextTertiary = Color.FromRgb(0x7E, 0x8E, 0x9E);

        // Panel backgrounds
        public static readonly Color PanelDark = Color.FromRgb(0x0E, 0x0E, 0x10);
        public static readonly Color PanelMid = Color.FromRgb(0x14, 0x14, 0x16);
    }

    // ── Colors: Mod Sources ──
    public static class ModSourceColors
    {
        public static readonly Color Core = Color.FromRgb(0x78, 0x90, 0x9C);
        public static readonly Color DLC = Color.FromRgb(0xF3, 0x9C, 0x12);
        public static readonly Color Workshop = Color.FromRgb(0x3A, 0x7B, 0xD5);
        public static readonly Color Local = Color.FromRgb(0x2E, 0xCC, 0x71);
        public static readonly Color Unknown = Color.FromRgb(0x60, 0x60, 0x60);
    }

    // ── Colors: Mod Categories ──
    public static class ModCategoryColors
    {
        public static readonly Color Framework = Color.FromRgb(0x9B, 0x59, 0xB6);
        public static readonly Color Visuals = Color.FromRgb(0xE9, 0x1E, 0x63);
        public static readonly Color Textures = Color.FromRgb(0xFF, 0x57, 0x22);
        public static readonly Color Content = Color.FromRgb(0xFF, 0x98, 0x00);
        public static readonly Color Gameplay = Color.FromRgb(0x00, 0xBC, 0xD4);
        public static readonly Color QoL = Color.FromRgb(0x8B, 0xC3, 0x4A);
        public static readonly Color UI = Color.FromRgb(0x03, 0xA9, 0xF4);
        public static readonly Color Unknown = Color.FromRgb(0x60, 0x60, 0x60);
    }

    // ── UI Sizing ──
    public static class Sizing
    {
        // Padding and Margin
        public const double CARD_PADDING_HORIZONTAL = 12;
        public const double CARD_PADDING_VERTICAL = 8;
        public const double CARD_MARGIN = 4;
        public const double SPACING_SMALL = 4;
        public const double SPACING_MEDIUM = 8;
        public const double SPACING_LARGE = 12;
        public const double SPACING_XLARGE = 16;

        // Border thickness
        public const double BORDER_THIN = 1;
        public const double BORDER_NORMAL = 1.5;
        public const double BORDER_THICK = 2;

        // Corner radius
        public const double CORNER_RADIUS_SMALL = 3;
        public const double CORNER_RADIUS_MEDIUM = 4;
        public const double CORNER_RADIUS_LARGE = 5;
        public const double CORNER_RADIUS_XLARGE = 8;

        // Font sizes
        public const double FONT_SIZE_SMALL = 9;
        public const double FONT_SIZE_NORMAL = 10;
        public const double FONT_SIZE_MEDIUM = 11;
        public const double FONT_SIZE_LARGE = 12;
        public const double FONT_SIZE_XLARGE = 13;
        public const double FONT_SIZE_TITLE = 14;
        public const double FONT_SIZE_HEADER = 18;
    }

    // ── UI Thresholds ──
    public static class Thresholds
    {
        public const int MAX_CONFLICTS_TO_DISPLAY = 15;
        public const int MAX_WARNINGS_TO_DISPLAY = 20;
        public const int VIRTUALIZATION_THRESHOLD = 50; // Use virtual scrolling above 50 mods
        public const int LAZY_LOAD_BATCH_SIZE = 10; // Load 10 mods at a time
    }

    // ── Search and Filtering ──
    public static class Search
    {
        public const string DUPLICATE_FILTER_KEYWORD = "dupe";
        public const string DUPLICATE_FILTER_KEYWORD2 = "dupes";
        public const string DUPLICATE_FILTER_KEYWORD3 = "duplicate";
        public const string DUPLICATE_FILTER_KEYWORD4 = "duplicates";
    }

    // ── Performance ──
    public static class Performance
    {
        public const int CONFLICT_DETECTION_CACHE_MINUTES = 5;
        public const int MOD_LIST_CACHE_MINUTES = 1;
        public const int FILE_SIZE_CACHE_MINUTES = 10;
    }

    // ── Error Messages ──
    public static class ErrorMessages
    {
        public const string MOD_FOLDER_NOT_FOUND = "RimWorld Mods folder not found.";
        public const string MOD_NOT_INSTALLED = "Mod is not installed in RimWorld Mods folder.";
        public const string EXPORT_FAILED = "Failed to export mod list.";
        public const string IMPORT_FAILED = "Failed to import mod list.";
        public const string FILE_SIZE_CALCULATION_FAILED = "Could not calculate mod size.";
        public const string CONFLICT_DETECTION_FAILED = "Could not scan for conflicts.";
    }

    // ── Success Messages ──
    public static class SuccessMessages
    {
        public const string MOD_INSTALLED = "Mod installed! Restart RimWorld to load it.";
        public const string MOD_REMOVED = "Mod removed. Restart RimWorld to apply.";
        public const string MOD_TOGGLED = "Mod state changed.";
        public const string EXPORT_COMPLETE = "Mod list exported successfully.";
        public const string IMPORT_COMPLETE = "Mod list imported successfully.";
    }

    // ── Warning Messages ──
    public static class WarningMessages
    {
        public const string MOD_FOLDER_LOCKED = "Mod folder is locked (game running?).";
        public const string MOD_NOT_FOUND_IMPORT = "Some mods from import were not found.";
    }
}
