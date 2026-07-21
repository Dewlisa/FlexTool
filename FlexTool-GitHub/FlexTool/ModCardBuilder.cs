using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FlexTool;

/// <summary>
/// Responsible for building mod card UI elements.
/// Separates UI construction logic from business logic.
/// </summary>
public class ModCardBuilder
{
    private readonly Dictionary<RimWorldSaveReader.ModSource, (string Label, Color Color)> _modSourceStyles;
    private readonly Dictionary<RimWorldSaveReader.ModCategory, (string Label, Color Color)> _modCategoryStyles;

    public ModCardBuilder()
    {
        // Initialize mod source styles
        _modSourceStyles = new()
        {
            [RimWorldSaveReader.ModSource.Core] = ("CORE", ModManagerConstants.ModSourceColors.Core),
            [RimWorldSaveReader.ModSource.DLC] = ("DLC", ModManagerConstants.ModSourceColors.DLC),
            [RimWorldSaveReader.ModSource.Workshop] = ("WORKSHOP", ModManagerConstants.ModSourceColors.Workshop),
            [RimWorldSaveReader.ModSource.Local] = ("LOCAL", ModManagerConstants.ModSourceColors.Local),
            [RimWorldSaveReader.ModSource.Unknown] = ("UNKNOWN", ModManagerConstants.ModSourceColors.Unknown),
        };

        // Initialize mod category styles
        _modCategoryStyles = new()
        {
            [RimWorldSaveReader.ModCategory.Framework] = ("FRAMEWORK", ModManagerConstants.ModCategoryColors.Framework),
            [RimWorldSaveReader.ModCategory.Visuals] = ("VISUALS", ModManagerConstants.ModCategoryColors.Visuals),
            [RimWorldSaveReader.ModCategory.Textures] = ("TEXTURES", ModManagerConstants.ModCategoryColors.Textures),
            [RimWorldSaveReader.ModCategory.Content] = ("CONTENT", ModManagerConstants.ModCategoryColors.Content),
            [RimWorldSaveReader.ModCategory.Gameplay] = ("GAMEPLAY", ModManagerConstants.ModCategoryColors.Gameplay),
            [RimWorldSaveReader.ModCategory.QoL] = ("QOL", ModManagerConstants.ModCategoryColors.QoL),
            [RimWorldSaveReader.ModCategory.UI] = ("UI", ModManagerConstants.ModCategoryColors.UI),
            [RimWorldSaveReader.ModCategory.Unknown] = ("MOD", ModManagerConstants.ModCategoryColors.Unknown),
        };
    }

    /// <summary>
    /// Builds a mod card UI element with all necessary styling and functionality.
    /// </summary>
    public Border BuildModCard(
        RimWorldSaveReader.ModInfo mod,
        bool isDuplicate,
        Brush primaryBrush,
        Brush borderBrush,
        Brush textPrimaryBrush,
        Brush textSecondaryBrush,
        Action<string, bool> onToggleMod)
    {
        var (sourceLabel, sourceColor) = _modSourceStyles.TryGetValue(mod.Source, out var s)
            ? s : ("?", ModManagerConstants.Colors.DisabledGray);
        var (catLabel, catColor) = _modCategoryStyles.TryGetValue(mod.Category, out var c)
            ? c : ("MOD", ModManagerConstants.Colors.DisabledGray);

        // Build comprehensive tooltip
        var tooltipText = BuildTooltipText(mod);

        // Main card border
        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = mod.IsActive 
                ? borderBrush
                : new SolidColorBrush(Color.FromArgb(0x99, 0x60, 0x60, 0x60)),
            BorderThickness = new Thickness(ModManagerConstants.Sizing.BORDER_THIN),
            CornerRadius = new CornerRadius(ModManagerConstants.Sizing.CORNER_RADIUS_LARGE),
            Padding = new Thickness(
                ModManagerConstants.Sizing.CARD_PADDING_HORIZONTAL,
                ModManagerConstants.Sizing.CARD_PADDING_VERTICAL,
                ModManagerConstants.Sizing.CARD_PADDING_HORIZONTAL,
                ModManagerConstants.Sizing.CARD_PADDING_VERTICAL),
            Margin = new Thickness(0, 0, 0, ModManagerConstants.Sizing.CARD_MARGIN),
            ToolTip = tooltipText
        };

        var wrapper = new DockPanel();

        // Left accent strip
        var accent = new Border
        {
            Width = 3,
            Background = new SolidColorBrush(sourceColor),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 0, ModManagerConstants.Sizing.SPACING_MEDIUM, 0)
        };
        DockPanel.SetDock(accent, Dock.Left);
        wrapper.Children.Add(accent);

        // Right panel: toggle + load order
        var rightPanel = BuildRightPanel(mod, onToggleMod, primaryBrush, textSecondaryBrush);
        DockPanel.SetDock(rightPanel, Dock.Right);
        wrapper.Children.Add(rightPanel);

        // Content: name, badges, details
        var content = BuildContentPanel(mod, sourceLabel, sourceColor, catLabel, catColor, isDuplicate, textPrimaryBrush, textSecondaryBrush);
        wrapper.Children.Add(content);

        card.Child = wrapper;
        return card;
    }

    /// <summary>
    /// Builds the right panel containing toggle button and load order.
    /// </summary>
    private StackPanel BuildRightPanel(
        RimWorldSaveReader.ModInfo mod,
        Action<string, bool> onToggleMod,
        Brush primaryBrush,
        Brush textSecondaryBrush)
    {
        var rightPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(ModManagerConstants.Sizing.SPACING_MEDIUM, 0, 0, 0)
        };

        // Status toggle button
        bool isCore = mod.Source == RimWorldSaveReader.ModSource.Core;
        var toggleBg = mod.IsActive
            ? ModManagerConstants.Colors.SuccessGreen
            : ModManagerConstants.Colors.DisabledGray;

        var toggleBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, toggleBg.R, toggleBg.G, toggleBg.B)),
            BorderBrush = new SolidColorBrush(toggleBg),
            BorderThickness = new Thickness(ModManagerConstants.Sizing.BORDER_NORMAL),
            CornerRadius = new CornerRadius(ModManagerConstants.Sizing.CORNER_RADIUS_MEDIUM),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = isCore ? null : System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = isCore ? "Core mod (cannot disable)" : (mod.IsActive ? "Click to disable" : "Click to enable"),
            Child = new TextBlock
            {
                Text = mod.IsActive ? "● ON" : "○ OFF",
                FontSize = ModManagerConstants.Sizing.FONT_SIZE_MEDIUM,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(toggleBg),
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };

        if (!isCore)
        {
            var pkgId = mod.PackageId;
            var active = mod.IsActive;
            toggleBtn.MouseLeftButtonUp += (_, _) =>
            {
                onToggleMod(pkgId, !active);
            };
        }

        rightPanel.Children.Add(toggleBtn);

        // Load order badge (if active)
        if (mod.IsActive && mod.LoadOrder >= 0)
        {
            var loadOrderBg = new SolidColorBrush(Color.FromArgb(0x44, 0x3A, 0x7B, 0xD5));
            rightPanel.Children.Add(new Border
            {
                Background = loadOrderBg,
                BorderBrush = primaryBrush,
                BorderThickness = new Thickness(ModManagerConstants.Sizing.BORDER_THIN),
                CornerRadius = new CornerRadius(ModManagerConstants.Sizing.CORNER_RADIUS_MEDIUM),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, ModManagerConstants.Sizing.SPACING_SMALL, 0, 0),
                Child = new TextBlock
                {
                    Text = $"#{mod.LoadOrder + 1}",
                    FontSize = ModManagerConstants.Sizing.FONT_SIZE_MEDIUM,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(ModManagerConstants.Colors.LightBlue),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });
        }

        return rightPanel;
    }

    /// <summary>
    /// Builds the main content panel with name, badges, and details.
    /// </summary>
    private StackPanel BuildContentPanel(
        RimWorldSaveReader.ModInfo mod,
        string sourceLabel,
        Color sourceColor,
        string catLabel,
        Color catColor,
        bool isDuplicate,
        Brush textPrimaryBrush,
        Brush textSecondaryBrush)
    {
        var content = new StackPanel();

        // Row 1: Name + badges
        var nameRow = new WrapPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = mod.Name,
            FontSize = ModManagerConstants.Sizing.FONT_SIZE_XLARGE,
            FontWeight = FontWeights.Medium,
            Foreground = textPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Source badge
        nameRow.Children.Add(CreateBadge(sourceLabel, sourceColor, ModManagerConstants.Sizing.SPACING_MEDIUM));

        // Category badge
        if (mod.Category != RimWorldSaveReader.ModCategory.Unknown)
        {
            nameRow.Children.Add(CreateBadge(catLabel, catColor, ModManagerConstants.Sizing.SPACING_SMALL));
        }

        // Duplicate badge
        if (isDuplicate)
        {
            nameRow.Children.Add(CreateBadge("DUPE", ModManagerConstants.Colors.DuplicateRed, ModManagerConstants.Sizing.SPACING_SMALL));
        }

        content.Children.Add(nameRow);

        // Row 2: Author · PackageID · Version · Size
        var detailRow = new WrapPanel { Margin = new Thickness(0, ModManagerConstants.Sizing.SPACING_SMALL, 0, 0) };

        if (!string.IsNullOrEmpty(mod.Author))
        {
            detailRow.Children.Add(new TextBlock
            {
                Text = mod.Author,
                FontSize = ModManagerConstants.Sizing.FONT_SIZE_MEDIUM,
                Foreground = textSecondaryBrush,
                Margin = new Thickness(0, 0, ModManagerConstants.Sizing.SPACING_SMALL, 0)
            });
            detailRow.Children.Add(MakeDot(textSecondaryBrush));
        }

        detailRow.Children.Add(new TextBlock
        {
            Text = mod.PackageId,
            FontSize = ModManagerConstants.Sizing.FONT_SIZE_MEDIUM,
            Foreground = textSecondaryBrush,
            FontStyle = FontStyles.Italic
        });

        // Version
        var versionText = !string.IsNullOrEmpty(mod.Version) ? $"v{mod.Version}"
            : !string.IsNullOrEmpty(mod.SupportedVersions) ? $"RW {mod.SupportedVersions}"
            : null;

        if (versionText is not null)
        {
            detailRow.Children.Add(MakeDot(textSecondaryBrush));
            detailRow.Children.Add(new TextBlock
            {
                Text = versionText,
                FontSize = ModManagerConstants.Sizing.FONT_SIZE_MEDIUM,
                Foreground = new SolidColorBrush(ModManagerConstants.Colors.TextTertiary)
            });
        }

        // File size
        if (!string.IsNullOrEmpty(mod.FolderPath) && Directory.Exists(mod.FolderPath))
        {
            try
            {
                var sizeStr = CalculateModFolderSize(mod.FolderPath);
                detailRow.Children.Add(MakeDot(textSecondaryBrush));
                detailRow.Children.Add(new TextBlock
                {
                    Text = sizeStr,
                    FontSize = ModManagerConstants.Sizing.FONT_SIZE_MEDIUM,
                    Foreground = new SolidColorBrush(ModManagerConstants.Colors.TextTertiary),
                    ToolTip = "Mod folder size"
                });
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }

        content.Children.Add(detailRow);
        return content;
    }

    /// <summary>
    /// Creates a colored badge for displaying labels.
    /// </summary>
    private Border CreateBadge(string text, Color color, double leftMargin)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(ModManagerConstants.Sizing.CORNER_RADIUS_SMALL),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(leftMargin, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = ModManagerConstants.Sizing.FONT_SIZE_SMALL,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            }
        };
    }

    /// <summary>
    /// Creates a separator dot between detail items.
    /// </summary>
    private TextBlock MakeDot(Brush brush)
    {
        return new TextBlock
        {
            Text = "·",
            FontSize = ModManagerConstants.Sizing.FONT_SIZE_MEDIUM,
            Foreground = brush,
            Margin = new Thickness(ModManagerConstants.Sizing.SPACING_SMALL, 0, ModManagerConstants.Sizing.SPACING_SMALL, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>
    /// Builds comprehensive tooltip text with full mod metadata.
    /// </summary>
    private string BuildTooltipText(RimWorldSaveReader.ModInfo mod)
    {
        var tooltipText = $"Name: {mod.Name}\nPackageID: {mod.PackageId}";

        if (!string.IsNullOrEmpty(mod.Author))
            tooltipText += $"\nAuthor: {mod.Author}";

        if (!string.IsNullOrEmpty(mod.Version))
            tooltipText += $"\nVersion: {mod.Version}";

        if (!string.IsNullOrEmpty(mod.SupportedVersions))
            tooltipText += $"\nSupported RimWorld: {mod.SupportedVersions}";

        var srcLabel = _modSourceStyles.TryGetValue(mod.Source, out var srcInfo)
            ? srcInfo.Label
            : "Unknown";
        tooltipText += $"\nSource: {srcLabel}";

        if (mod.FolderPath != null && Directory.Exists(mod.FolderPath))
            tooltipText += $"\nPath: {mod.FolderPath}";

        return tooltipText;
    }

    /// <summary>
    /// Calculates mod folder size and returns a human-readable string.
    /// </summary>
    public string CalculateModFolderSize(string folderPath)
    {
        try
        {
            long totalSize = 0;
            var di = new DirectoryInfo(folderPath);

            foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                totalSize += file.Length;
            }

            if (totalSize >= 1024 * 1024 * 1024)
                return $"{totalSize / (1024.0 * 1024 * 1024):F1}GB";
            else if (totalSize >= 1024 * 1024)
                return $"{totalSize / (1024.0 * 1024):F0}MB";
            else if (totalSize >= 1024)
                return $"{totalSize / 1024.0:F0}KB";
            else
                return $"{totalSize}B";
        }
        catch
        {
            return "?";
        }
    }
}
