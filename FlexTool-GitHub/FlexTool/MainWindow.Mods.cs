using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FlexTool;

public partial class MainWindow
{
    // ── Mod Manager Helpers ─────────────────────────────────────
    private ModCardBuilder _cardBuilder;
    private ModConflictAnalyzer _conflictAnalyzer;
    private ModManagerErrorHandler _errorHandler;
    private VirtualizedModList _virtualizedList;

    // ── Mod Manager Page ────────────────────────────────────────
    // Note: These are kept for now for compatibility but delegates to ModCategoryHelper where possible
    private static readonly Dictionary<RimWorldSaveReader.ModSource, (string Label, Color Color)> ModSourceStyles = new()
    {
        [RimWorldSaveReader.ModSource.Core]     = ("CORE",     Color.FromRgb(0x78, 0x90, 0x9C)),
        [RimWorldSaveReader.ModSource.DLC]      = ("DLC",      Color.FromRgb(0xF3, 0x9C, 0x12)),
        [RimWorldSaveReader.ModSource.Workshop] = ("WORKSHOP", Color.FromRgb(0x3A, 0x7B, 0xD5)),
        [RimWorldSaveReader.ModSource.Local]    = ("LOCAL",    Color.FromRgb(0x2E, 0xCC, 0x71)),
        [RimWorldSaveReader.ModSource.Unknown]  = ("UNKNOWN",  Color.FromRgb(0x60, 0x60, 0x60)),
    };

    private static readonly Dictionary<RimWorldSaveReader.ModCategory, (string Label, Color Color)> ModCategoryStyles = new()
    {
        [RimWorldSaveReader.ModCategory.Framework] = ("FRAMEWORK", Color.FromRgb(0x9B, 0x59, 0xB6)),
        [RimWorldSaveReader.ModCategory.Visuals]   = ("VISUALS",   Color.FromRgb(0xE9, 0x1E, 0x63)),
        [RimWorldSaveReader.ModCategory.Textures]  = ("TEXTURES",  Color.FromRgb(0xFF, 0x57, 0x22)),
        [RimWorldSaveReader.ModCategory.Content]   = ("CONTENT",   Color.FromRgb(0xFF, 0x98, 0x00)),
        [RimWorldSaveReader.ModCategory.Gameplay]  = ("GAMEPLAY",  Color.FromRgb(0x00, 0xBC, 0xD4)),
        [RimWorldSaveReader.ModCategory.QoL]       = ("QOL",       Color.FromRgb(0x8B, 0xC3, 0x4A)),
        [RimWorldSaveReader.ModCategory.UI]        = ("UI",        Color.FromRgb(0x03, 0xA9, 0xF4)),
        [RimWorldSaveReader.ModCategory.Unknown]   = ("MOD",       Color.FromRgb(0x60, 0x60, 0x60)),
    };

    /// <summary>
    /// Initialize all Mod Manager helper classes.
    /// Called from MainWindow_Loaded to set up the refactored mod manager infrastructure.
    /// </summary>
    private void InitializeModManagerHelpers()
    {
        _cardBuilder = new ModCardBuilder();
        _conflictAnalyzer = new ModConflictAnalyzer();
        _errorHandler = new ModManagerErrorHandler(ShowErrorToast);
        // VirtualizedModList initialized lazily when needed
    }

    /// <summary>
    /// Show an error toast notification.
    /// Used as callback for ModManagerErrorHandler.
    /// </summary>
    private void ShowErrorToast(string title, string message, ToastService.ToastType type)
    {
        ShowToast(title, message, type);
    }

    private UIElement BuildModManagerPage()
    {
        var root = new StackPanel();

        var mods = RimWorldSaveReader.GetAllMods();
        int activeCount = mods.Count(m => m.IsActive);
        int inactiveCount = mods.Count - activeCount;

        // Detect duplicate mods (only same packageId active multiple times)
        // Changed logic: Only mark as duplicate if SAME packageId appears multiple times while active
        var dupePackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in mods.Where(m => m.IsActive)
            .GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            // Only add packageId if it appears more than once while active
            dupePackageIds.Add(g.Key);
        }
        int dupeCount = mods.Count(m => m.IsActive && dupePackageIds.Contains(m.PackageId));

        // Stats summary row
        var statsCard = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 14, 20, 14),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var statsRow = new WrapPanel();
        int workshopCount = mods.Count(m => m.Source == RimWorldSaveReader.ModSource.Workshop);
        int localCount = mods.Count(m => m.Source == RimWorldSaveReader.ModSource.Local);
        int dlcCount = mods.Count(m => m.Source == RimWorldSaveReader.ModSource.DLC);

        void AddStat(string label, int count, Color color, string? tooltip = null)
        {
            if (statsRow.Children.Count > 0)
            {
                statsRow.Children.Add(new TextBlock
                {
                    Text = "·",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(10, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var sp = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = tooltip };
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            statsRow.Children.Add(sp);
        }

        AddStat("Active", activeCount, Color.FromRgb(0x2E, 0xCC, 0x71), "Mods currently enabled in your load order");
        AddStat("Inactive", inactiveCount, Color.FromRgb(0x78, 0x90, 0x9C), "Installed mods not currently active");
        AddStat("Workshop", workshopCount, ModSourceStyles[RimWorldSaveReader.ModSource.Workshop].Color, "Mods installed from Steam Workshop");
        AddStat("DLC", dlcCount, ModSourceStyles[RimWorldSaveReader.ModSource.DLC].Color, "Official DLC content packs");
        AddStat("Local", localCount, ModSourceStyles[RimWorldSaveReader.ModSource.Local].Color, "Locally installed mods");
        if (dupeCount > 0)
            AddStat("Dupes", dupeCount, Color.FromRgb(0xE7, 0x4C, 0x3C), "Duplicate mods detected (same mod from multiple sources)");

        statsCard.Child = statsRow;
        root.Children.Add(statsCard);

        // Import toolbar (Export .txt / .xml removed — Modpack export replaces them)
        var exportImportRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var importBtn = new Button
        {
            Content = "Import Mod List",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            Margin = new Thickness(0, 0, 6, 0)
        };
        importBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Mod List",
                Filter = "Mod list files (*.txt;*.xml)|*.txt;*.xml|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var (imported, notFound) = RimWorldSaveReader.ImportModList(dlg.FileName);
                    if (imported > 0)
                    {
                        string msg = $"Imported {imported} mods from {System.IO.Path.GetFileName(dlg.FileName)}";
                        if (notFound.Count > 0)
                            msg += $"\n{notFound.Count} not found: {string.Join(", ", notFound.Take(5))}";
                        ShowToast("Import Complete", msg, notFound.Count > 0
                            ? ToastService.ToastType.Warning : ToastService.ToastType.Success);
                        UpdateContentArea();
                    }
                    else
                    {
                        ShowToast("Import Failed", "No mods found in file", ToastService.ToastType.Error);
                    }
                }
                catch (Exception ex)
                {
                    ShowErrorToast("Import Error", ModManagerConstants.ErrorMessages.IMPORT_FAILED, ToastService.ToastType.Error);
                    System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
                }
            }
        };
        exportImportRow.Children.Add(importBtn);
        root.Children.Add(exportImportRow);

        // Load order warnings
        var warnings = RimWorldSaveReader.GetLoadOrderWarnings();
        if (warnings.Count > 0)
        {
            var warningsCard = new Border
            {
                Background = (Brush)FindResource("PanelMidBrush"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var warningsStack = new StackPanel();

            var warningsHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            warningsHeader.Children.Add(new TextBlock
            {
                Text = "⚠  LOAD ORDER WARNINGS",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
                VerticalAlignment = VerticalAlignment.Center
            });
            warningsHeader.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF3, 0x9C, 0x12)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = warnings.Count.ToString(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12))
                }
            });
            warningsStack.Children.Add(warningsHeader);

            foreach (var w in warnings)
            {
                var severityColor = w.Level switch
                {
                    RimWorldSaveReader.LoadOrderWarning.Severity.Error => Color.FromRgb(0xE7, 0x4C, 0x3C),
                    RimWorldSaveReader.LoadOrderWarning.Severity.Warning => Color.FromRgb(0xF3, 0x9C, 0x12),
                    _ => Color.FromRgb(0x3A, 0x7B, 0xD5)
                };
                var icon = w.Level switch
                {
                    RimWorldSaveReader.LoadOrderWarning.Severity.Error => "✕",
                    RimWorldSaveReader.LoadOrderWarning.Severity.Warning => "⚠",
                    _ => "ℹ"
                };

                var wCard = new Border
                {
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = new SolidColorBrush(Color.FromArgb(0x18, severityColor.R, severityColor.G, severityColor.B)),
                    CornerRadius = new CornerRadius(4)
                };

                var wDock = new DockPanel();
                wDock.Children.Add(new TextBlock
                {
                    Text = icon,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(severityColor),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                DockPanel.SetDock(wDock.Children[^1], Dock.Left);

                var wContent = new StackPanel();
                wContent.Children.Add(new TextBlock
                {
                    Text = w.Message,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(severityColor)
                });
                if (!string.IsNullOrEmpty(w.Details))
                {
                    wContent.Children.Add(new TextBlock
                    {
                        Text = w.Details,
                        FontSize = 10,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                wDock.Children.Add(wContent);
                wCard.Child = wDock;
                warningsStack.Children.Add(wCard);
            }

            warningsCard.Child = warningsStack;
            root.Children.Add(warningsCard);
        }

        // Mod conflict detection
        var conflicts = new List<RimWorldSaveReader.ModConflict>();
        try
        {
            var activeMods = mods.Where(m => m.IsActive).ToList();
            conflicts = _conflictAnalyzer.AnalyzeConflicts(activeMods);
        }
        catch (Exception ex)
        {
            ShowErrorToast("Conflict Detection", ModManagerConstants.ErrorMessages.CONFLICT_DETECTION_FAILED, ToastService.ToastType.Warning);
            System.Diagnostics.Debug.WriteLine($"Conflict analysis failed: {ex.Message}");
            conflicts = new List<RimWorldSaveReader.ModConflict>();
        }

        if (conflicts.Count > 0)
        {
            var conflictCard = new Border
            {
                Background = (Brush)FindResource("PanelMidBrush"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0x1E, 0x63)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var conflictStack = new StackPanel();

            var conflictHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            conflictHeader.Children.Add(new TextBlock
            {
                Text = "⚔  MOD CONFLICTS",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0x1E, 0x63)),
                VerticalAlignment = VerticalAlignment.Center
            });
            conflictHeader.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE9, 0x1E, 0x63)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = conflicts.Count.ToString(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0x1E, 0x63))
                }
            });
            conflictStack.Children.Add(conflictHeader);

            int maxDisplay = Math.Min(conflicts.Count, 15);
            for (int ci = 0; ci < maxDisplay; ci++)
            {
                var cf = conflicts[ci];
                var cCard = new Border
                {
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = new SolidColorBrush(Color.FromArgb(0x18, 0xE9, 0x1E, 0x63)),
                    CornerRadius = new CornerRadius(4)
                };

                var cContent = new StackPanel();

                var xpathText = cf.TargetXPath;
                if (!string.IsNullOrEmpty(cf.TargetDef))
                    xpathText = $"{cf.TargetDef}  —  {cf.Operation}";

                cContent.Children.Add(new TextBlock
                {
                    Text = xpathText,
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    TextWrapping = TextWrapping.Wrap
                });

                var modsRow = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
                foreach (var (pkgId, modName, patchFile) in cf.InvolvedMods)
                {
                    modsRow.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE9, 0x1E, 0x63)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 4, 2),
                        ToolTip = $"{pkgId}\n{patchFile}",
                        Child = new TextBlock
                        {
                            Text = modName,
                            FontSize = 9,
                            Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0x1E, 0x63))
                        }
                    });
                }
                cContent.Children.Add(modsRow);
                cCard.Child = cContent;
                conflictStack.Children.Add(cCard);
            }

            if (conflicts.Count > maxDisplay)
            {
                conflictStack.Children.Add(new TextBlock
                {
                    Text = $"+ {conflicts.Count - maxDisplay} more conflicts",
                    FontSize = 10,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            conflictCard.Child = conflictStack;
            root.Children.Add(conflictCard);
        }

        // Mod list card
        var listCard = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20)
        };

        var listStack = new StackPanel();

        // Header row with dupe indicator
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        headerRow.Children.Add(new TextBlock
        {
            Text = "ALL MODS",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        });
        if (dupeCount > 0)
        {
            var dupeColor = Color.FromRgb(0xE7, 0x4C, 0x3C);
            headerRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, dupeColor.R, dupeColor.G, dupeColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{dupeCount} DUPES",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(dupeColor)
                }
            });
        }
        listStack.Children.Add(headerRow);

        // Toolbar: search + smart sort + sort button
        var toolBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBoxStyle"),
            Text = _modSearchFilter,
            FontSize = 12
        };
        var placeholder = new TextBlock
        {
            Text = "🔍  Search mods...",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false,
            Margin = new Thickness(10, 7, 0, 0),
            Visibility = string.IsNullOrEmpty(_modSearchFilter) ? Visibility.Visible : Visibility.Collapsed
        };

        var searchGrid = new Grid();
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(placeholder);
        Grid.SetColumn(searchGrid, 0);
        toolBar.Children.Add(searchGrid);

        // Auto Smart Sort button - applies the smart load order to ModsConfig.xml
        var smartSortColor = Color.FromRgb(0x2E, 0xCC, 0x71);
        var smartSortBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, smartSortColor.R, smartSortColor.G, smartSortColor.B)),
            BorderBrush = new SolidColorBrush(smartSortColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Reorders your active mods into a recommended load order:\nCore → Frameworks → Essentials → Content → Cosmetics.\nWrites the new order to ModsConfig.xml.",
            Child = new TextBlock
            {
                Text = "⚡ Auto Smart Sort",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(smartSortColor),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        smartSortBtn.MouseEnter += (_, _) =>
            smartSortBtn.Background = new SolidColorBrush(Color.FromArgb(0x55, smartSortColor.R, smartSortColor.G, smartSortColor.B));
        smartSortBtn.MouseLeave += (_, _) =>
            smartSortBtn.Background = new SolidColorBrush(Color.FromArgb(0x33, smartSortColor.R, smartSortColor.G, smartSortColor.B));
        smartSortBtn.MouseLeftButtonUp += (_, _) => ApplyAutoSmartSort();
        Grid.SetColumn(smartSortBtn, 1);
        toolBar.Children.Add(smartSortBtn);

        // Sort dropdown button
        var sortModes = new[] { "Auto Sort", "Load Order", "Name", "Author", "Source", "Category" };

        var sortBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x3A, 0x7B, 0xD5)),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"Sort: {_modSortMode} ▾",
                FontSize = 11,
                Foreground = (Brush)FindResource("BlueLightBrush"),
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var dropdownPanel = new StackPanel();
        var dropdown = new Popup
        {
            PlacementTarget = sortBtn,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0, 4, 0, 4),
                MinWidth = 150,
                Child = dropdownPanel
            }
        };

        foreach (var mode in sortModes)
        {
            bool isCurrent = mode == _modSortMode;
            var selectedBg = new SolidColorBrush(Color.FromArgb(0x33, 0x3A, 0x7B, 0xD5));
            var hoverBg = new SolidColorBrush(Color.FromArgb(0x22, 0x5A, 0x9A, 0xE6));

            var item = new Border
            {
                Padding = new Thickness(14, 7, 20, 7),
                Background = isCurrent ? selectedBg : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = isCurrent ? $"✓  {mode}" : $"     {mode}",
                    FontSize = 11,
                    FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isCurrent
                        ? (Brush)FindResource("BlueLightBrush")
                        : (Brush)FindResource("TextPrimaryBrush")
                }
            };

            Brush restBg = isCurrent ? selectedBg : Brushes.Transparent;
            item.MouseEnter += (_, _) => item.Background = hoverBg;
            item.MouseLeave += (_, _) => item.Background = restBg;

            var m = mode;
            item.MouseLeftButtonUp += (_, _) =>
            {
                dropdown.IsOpen = false;
                if (_modSortMode != m)
                {
                    _modSortMode = m;
                    UpdateContentArea();
                }
            };

            dropdownPanel.Children.Add(item);
        }

        sortBtn.MouseLeftButtonUp += (_, _) => dropdown.IsOpen = !dropdown.IsOpen;
        Grid.SetColumn(sortBtn, 2);
        toolBar.Children.Add(sortBtn);

        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            if (searchBox.Text != _modSearchFilter)
            {
                _modSearchFilter = searchBox.Text;
                UpdateContentArea();
            }
        };
        if (!string.IsNullOrEmpty(_modSearchFilter))
        {
            searchBox.Loaded += (_, _) =>
            {
                searchBox.Focus();
                searchBox.CaretIndex = searchBox.Text.Length;
            };
        }

        listStack.Children.Add(toolBar);

        // Filter mods (typing "dupe" / "dupes" filters to duplicates only)
        var filter = _modSearchFilter.Trim();
        bool isDupeFilter = filter.Equals("dupe", StringComparison.OrdinalIgnoreCase)
            || filter.Equals("dupes", StringComparison.OrdinalIgnoreCase)
            || filter.Equals("duplicate", StringComparison.OrdinalIgnoreCase)
            || filter.Equals("duplicates", StringComparison.OrdinalIgnoreCase);

        var filtered = isDupeFilter
            ? mods.Where(m => dupePackageIds.Contains(m.PackageId)).ToList()
            : string.IsNullOrEmpty(filter)
                ? mods
                : mods.Where(m =>
                    m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    m.PackageId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    m.Author.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    m.Version.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    m.SupportedVersions.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    (ModCategoryStyles.TryGetValue(m.Category, out var cs)
                        && cs.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();

        if (filtered.Count == 0)
        {
            listStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(filter) ? "No mods found"
                    : isDupeFilter ? "No duplicate mods detected"
                    : $"No mods matching \"{filter}\"",
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        else
        {
            var modList = new StackPanel();

            IEnumerable<RimWorldSaveReader.ModInfo> ApplySort(IEnumerable<RimWorldSaveReader.ModInfo> list) =>
                _modSortMode switch
                {
                    "Auto Sort" => ModAutoSortHelper.AutoSort(list),
                    "Load Order" => list.OrderBy(m => m.LoadOrder),
                    "Name" => list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
                    "Author" => list.OrderBy(m => m.Author, StringComparer.OrdinalIgnoreCase)
                                    .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
                    "Source" => list.OrderBy(m => m.Source).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
                    "Category" => list.OrderBy(m => m.Category).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
                    _ => list.OrderBy(m => m.LoadOrder)
                };

            // Harmony always pins to the top of the active list — it must load
            // before everything else in-game, so the UI mirrors that guarantee.
            static IEnumerable<RimWorldSaveReader.ModInfo> PinHarmonyFirst(IEnumerable<RimWorldSaveReader.ModInfo> list)
            {
                var mods = list.ToList();
                var harmony = mods.Where(m =>
                    string.Equals(m.PackageId, "brrainz.harmony", StringComparison.OrdinalIgnoreCase)).ToList();
                if (harmony.Count == 0) return mods;
                return harmony.Concat(mods.Except(harmony));
            }

            // In "Load Order" mode the list must mirror ModsConfig.xml exactly —
            // the same order RimWorld shows in-game — so no visual re-pinning.
            var sortedActive = ApplySort(filtered.Where(m => m.IsActive));
            var activeMods = (_modSortMode == "Load Order" ? sortedActive : PinHarmonyFirst(sortedActive)).ToList();
            var inactiveMods = ApplySort(filtered.Where(m => !m.IsActive)).ToList();

            // Determine which panel to use based on mod count
            Panel contentPanel;
            int totalModsToDisplay = activeMods.Count + inactiveMods.Count;

            if (totalModsToDisplay > ModManagerConstants.Thresholds.VIRTUALIZATION_THRESHOLD)
            {
                // For large lists, use VirtualizingStackPanel for better performance
                var virtualizingPanel = new VirtualizingStackPanel();
                VirtualizingStackPanel.SetIsVirtualizing(virtualizingPanel, true);
                VirtualizingStackPanel.SetCacheLengthUnit(virtualizingPanel, VirtualizationCacheLengthUnit.Item);
                VirtualizingStackPanel.SetCacheLength(virtualizingPanel, new VirtualizationCacheLength(10));
                contentPanel = virtualizingPanel;
            }
            else
            {
                // Use regular StackPanel for small lists
                contentPanel = new StackPanel();
            }

            // Build mod cards into the appropriate panel with drag-drop support for active mods
            foreach (var mod in activeMods)
            {
                var card = BuildModCard(mod, dupePackageIds.Contains(mod.PackageId));
                contentPanel.Children.Add(card);

                // Enable drag-drop reordering for active mods only
                ModDragDropHelper.EnableDragForCard(card, mod, HandleModReorder);
            }

            if (activeMods.Count > 0 && inactiveMods.Count > 0)
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"INACTIVE ({inactiveMods.Count})",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 12, 0, 6)
                });
            }

            foreach (var mod in inactiveMods)
                contentPanel.Children.Add(BuildModCard(mod, dupePackageIds.Contains(mod.PackageId)));

            // Create scroll viewer
            var scrollViewer = new ScrollViewer
            {
                Style = (Style)FindResource("DarkScrollViewerStyle"),
                MaxHeight = 500,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = contentPanel
            };

            listStack.Children.Add(scrollViewer);
        }

        listCard.Child = listStack;
        root.Children.Add(listCard);

        return root;
    }

    /// <summary>
    /// Handles a drag-drop mod reorder. Works against the full persisted active mod
    /// order (never the filtered/sorted view) so a search filter or sort mode can
    /// never drop mods from ModsConfig.xml.
    /// </summary>
    private void HandleModReorder(RimWorldSaveReader.ModInfo dragged, RimWorldSaveReader.ModInfo target, bool insertBelow)
    {
        try
        {
            var order = RimWorldSaveReader.GetActiveMods()
                .Select(m => m.PackageId)
                .ToList();

            int fromIndex = order.FindIndex(id => id.Equals(dragged.PackageId, StringComparison.OrdinalIgnoreCase));
            int targetIndex = order.FindIndex(id => id.Equals(target.PackageId, StringComparison.OrdinalIgnoreCase));
            if (fromIndex < 0 || targetIndex < 0 || fromIndex == targetIndex)
                return;

            order.RemoveAt(fromIndex);

            int insertIndex = order.FindIndex(id => id.Equals(target.PackageId, StringComparison.OrdinalIgnoreCase));
            if (insertBelow)
                insertIndex++;

            order.Insert(Math.Clamp(insertIndex, 0, order.Count), dragged.PackageId);

            RimWorldSaveReader.ReorderMods(order);

            // Show the result of the manual drag: switch the view to load order,
            // otherwise the active sort mode would immediately re-sort the list.
            _modSortMode = "Load Order";

            ShowToast("Load Order", $"Moved \"{dragged.Name}\" {(insertBelow ? "below" : "above")} \"{target.Name}\"",
                ToastService.ToastType.Success);
            UpdateContentArea();
        }
        catch (Exception ex)
        {
            ShowErrorToast("Reorder Failed", "Could not update the mod load order.", ToastService.ToastType.Error);
            System.Diagnostics.Debug.WriteLine($"Mod reorder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the smart auto-sort to the actual active mod load order and
    /// persists it to ModsConfig.xml. Uses conflict analysis when available
    /// to push conflicting mods apart. No-ops if the order is already optimal.
    /// </summary>
    private void ApplyAutoSmartSort()
    {
        try
        {
            var activeMods = RimWorldSaveReader.GetActiveMods();
            if (activeMods.Count == 0)
            {
                ShowToast("Auto Smart Sort", "No active mods to sort", ToastService.ToastType.Info);
                return;
            }

            List<RimWorldSaveReader.ModConflict>? conflicts = null;
            try
            {
                conflicts = _conflictAnalyzer?.AnalyzeConflicts(activeMods);
            }
            catch
            {
                // Conflict analysis is best-effort; smart sort still works without it
            }

            var sorted = ModAutoSortHelper.AnalyzeDependencies(activeMods, conflicts);
            var newOrder = sorted.Select(m => m.PackageId).ToList();
            var currentOrder = activeMods.Select(m => m.PackageId).ToList();

            if (newOrder.SequenceEqual(currentOrder, StringComparer.OrdinalIgnoreCase))
            {
                ShowToast("Auto Smart Sort", "Load order is already optimal", ToastService.ToastType.Info);
                return;
            }

            RimWorldSaveReader.ReorderMods(newOrder);

            // Show the applied order in the list
            _modSortMode = "Load Order";

            int moved = currentOrder.Where((id, i) =>
                !id.Equals(newOrder[i], StringComparison.OrdinalIgnoreCase)).Count();
            ShowToast("Auto Smart Sort",
                $"Reordered {moved} of {activeMods.Count} active mods",
                ToastService.ToastType.Success);
            UpdateContentArea();
        }
        catch (Exception ex)
        {
            ShowErrorToast("Auto Smart Sort Failed", "Could not apply the smart load order.", ToastService.ToastType.Error);
            System.Diagnostics.Debug.WriteLine($"Auto smart sort failed: {ex.Message}");
        }
    }

    private Border BuildModCard(RimWorldSaveReader.ModInfo mod, bool isDuplicate = false)
    {
        var (sourceLabel, sourceColor) = ModSourceStyles.TryGetValue(mod.Source, out var s)
            ? s : ("?", Color.FromRgb(0x60, 0x60, 0x60));
        var (catLabel, catColor) = ModCategoryStyles.TryGetValue(mod.Category, out var c)
            ? c : ("MOD", Color.FromRgb(0x60, 0x60, 0x60));

        // Build comprehensive tooltip with full mod details
        var tooltipText = $"Name: {mod.Name}\nPackageID: {mod.PackageId}";
        if (!string.IsNullOrEmpty(mod.Author))
            tooltipText += $"\nAuthor: {mod.Author}";
        if (!string.IsNullOrEmpty(mod.Version))
            tooltipText += $"\nVersion: {mod.Version}";
        if (!string.IsNullOrEmpty(mod.SupportedVersions))
            tooltipText += $"\nSupported RimWorld: {mod.SupportedVersions}";
        var srcLabel = ModSourceStyles.TryGetValue(mod.Source, out var srcInfo) 
            ? srcInfo.Label 
            : "Unknown";
        tooltipText += $"\nSource: {srcLabel}";
        if (mod.FolderPath != null && Directory.Exists(mod.FolderPath))
            tooltipText += $"\nPath: {mod.FolderPath}";

        // Use better visual distinction for inactive mods: desaturated border + reduced opacity on border only
        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = mod.IsActive 
                ? (Brush)FindResource("BorderBlueBrush") 
                : new SolidColorBrush(Color.FromArgb(0x99, 0x60, 0x60, 0x60)), // Desaturated gray for inactive
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = tooltipText
        };

        var wrapper = new DockPanel();

        // Colored left accent strip
        var accent = new Border
        {
            Width = 3,
            Background = new SolidColorBrush(sourceColor),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(accent, Dock.Left);
        wrapper.Children.Add(accent);

        // Right side: toggle + load order
        var rightPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        bool isCore = mod.Source == RimWorldSaveReader.ModSource.Core;
        var toggleBg = mod.IsActive
            ? Color.FromRgb(0x2E, 0xCC, 0x71)
            : Color.FromRgb(0x60, 0x60, 0x60);
        // More prominent toggle button with better visibility
        var toggleBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, toggleBg.R, toggleBg.G, toggleBg.B)),
            BorderBrush = new SolidColorBrush(toggleBg),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = isCore ? null : System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = isCore ? "Core mod (cannot disable)" : (mod.IsActive ? "Click to disable" : "Click to enable"),
            Child = new TextBlock
            {
                Text = mod.IsActive ? "● ON" : "○ OFF",
                FontSize = 10,
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
                RimWorldSaveReader.SetModEnabled(pkgId, !active);
                UpdateContentArea();
            };
        }

        rightPanel.Children.Add(toggleBtn);

        // Improved load order display - more prominent
        if (mod.IsActive && mod.LoadOrder >= 0)
        {
            var loadOrderBg = new SolidColorBrush(Color.FromArgb(0x44, 0x3A, 0x7B, 0xD5));
            rightPanel.Children.Add(new Border
            {
                Background = loadOrderBg,
                BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 3, 0, 0),
                Child = new TextBlock
                {
                    Text = $"#{mod.LoadOrder + 1}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("BlueLightBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });
        }

        DockPanel.SetDock(rightPanel, Dock.Right);
        wrapper.Children.Add(rightPanel);

        // Content
        var content = new StackPanel();

        // Row 1: Name + source badge + category badge
        var nameRow = new WrapPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = mod.Name,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        nameRow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, sourceColor.R, sourceColor.G, sourceColor.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = sourceLabel,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(sourceColor)
            }
        });

        if (mod.Category != RimWorldSaveReader.ModCategory.Unknown)
        {
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, catColor.R, catColor.G, catColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = catLabel,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(catColor)
                }
            });
        }

        if (isDuplicate)
        {
            var dupeColor = Color.FromRgb(0xE7, 0x4C, 0x3C);
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, dupeColor.R, dupeColor.G, dupeColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "DUPE",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(dupeColor)
                }
            });
        }

        content.Children.Add(nameRow);

        // Row 2: Author · Package ID · Version
        var detailRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

        if (!string.IsNullOrEmpty(mod.Author))
        {
            detailRow.Children.Add(new TextBlock
            {
                Text = mod.Author,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 6, 0)
            });
            detailRow.Children.Add(MakeDot());
        }

        detailRow.Children.Add(new TextBlock
        {
            Text = mod.PackageId,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontStyle = FontStyles.Italic
        });

        var versionText = !string.IsNullOrEmpty(mod.Version) ? $"v{mod.Version}"
            : !string.IsNullOrEmpty(mod.SupportedVersions) ? $"RW {mod.SupportedVersions}"
            : null;

        if (versionText is not null)
        {
            detailRow.Children.Add(MakeDot());
            detailRow.Children.Add(new TextBlock
            {
                Text = versionText,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x8E, 0x9E))
            });
        }

        // Add file size if folder path is available
        if (!string.IsNullOrEmpty(mod.FolderPath) && Directory.Exists(mod.FolderPath))
        {
            try
            {
                var sizeStr = GetModFolderSizeString(mod.FolderPath);
                detailRow.Children.Add(MakeDot());
                detailRow.Children.Add(new TextBlock
                {
                    Text = sizeStr,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x8E, 0x9E)),
                    ToolTip = "Mod folder size"
                });
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }

        content.Children.Add(detailRow);
        wrapper.Children.Add(content);
        card.Child = wrapper;

        return card;
    }

    /// <summary>
    /// Calculates the total size of a folder and returns a human-readable string.
    /// </summary>
    private string GetModFolderSizeString(string folderPath)
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

    // ── Mods Tab Content ────────────────────────────────────────

    private UIElement BuildModsTabContent()
    {
        var innerStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(8)
        };

        // Harmony requirement card (all FlexTool mods depend on it)
        innerStack.Children.Add(CreateHarmonyRequirementCard());

        // Warn once per session if Harmony is missing
        ShowHarmonyMissingPopupIfNeeded();

        // Add Launch DLL card (on top)
        innerStack.Children.Add(CreateLaunchDLLCard());

        // Add Cheats mod card (above Speed per user preference)
        innerStack.Children.Add(CreateCheatsModCard());

        // Add Speed mod card
        innerStack.Children.Add(CreateSpeedModCard());

        // Add FPS Optimizer mod card (includes the former Performance mod features)
        innerStack.Children.Add(CreateFpsOptimizerCard());

        // Add Debug Info mod card
        innerStack.Children.Add(CreateDebugInfoModCard());

        // Add Till Death Do Us Part mod card
        innerStack.Children.Add(CreateTillDeathModCard());

        // Add Keep It Together mod card
        innerStack.Children.Add(CreateKeepItTogetherModCard());

        // Add Pawn Extractor mod card
        innerStack.Children.Add(CreatePawnExtractorModCard());

        // Return just the content - ContentPanel already has a ScrollViewer in the XAML
        return innerStack;
    }

    // ── Harmony Requirement ─────────────────────────────────────────────

    private static bool _harmonyPopupShown;

    private void ShowHarmonyMissingPopupIfNeeded()
    {
        if (_harmonyPopupShown) return;
        _harmonyPopupShown = true;

        if (RimWorldSaveReader.IsHarmonyInstalled()) return;

        Dispatcher.BeginInvoke(() =>
        {
            var result = MessageBox.Show(
                this,
                "Harmony is REQUIRED for all FlexTool in-game mods to work.\n\n" +
                "It was not found in your RimWorld Mods folder or Steam Workshop.\n\n" +
                "Would you like to open the Harmony download page now?\n\n" +
                "(Steam Workshop is recommended — subscribe and it installs automatically.)",
                "Harmony Mod Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
                OpenHarmonyDownloadPage();
        }, DispatcherPriority.Background);
    }

    private void OpenHarmonyDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open Harmony page: {ex.Message}");
            ShowToast("Harmony", "Could not open the browser. Search 'Harmony' on the Steam Workshop for RimWorld.", ToastService.ToastType.Warning);
        }
    }

    private Border CreateHarmonyRequirementCard()
    {
        bool installed = RimWorldSaveReader.IsHarmonyInstalled();
        bool active = RimWorldSaveReader.IsHarmonyActive();

        var card = new Border
        {
            BorderBrush = installed
                ? (Brush)FindResource("BluePrimaryBrush")
                : new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel { Orientation = Orientation.Vertical }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "Harmony (Required Dependency)",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF3, 0x9C, 0x12), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE7, 0x4C, 0x3C), 1.0));
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var statusColor = installed
            ? Color.FromRgb(0x2E, 0xCC, 0x71)
            : Color.FromRgb(0xE7, 0x4C, 0x3C);
        statusPanel.Children.Add(new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        statusPanel.Children.Add(new TextBlock
        {
            Text = installed
                ? (active ? "Installed & Active" : "Installed (activate it in the game's Mods menu)")
                : "NOT FOUND — required for all FlexTool mods!",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center
        });
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Harmony is a modding library that all FlexTool in-game mods depend on. " +
                   "Without it, none of the mods below will load. " +
                   "The easiest way to get it is subscribing on the Steam Workshop \u2014 Steam installs it automatically.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var workshopBtn = new Button
        {
            Content = installed ? "Open Workshop Page" : "Get Harmony (Steam Workshop)",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(installed
                ? Color.FromRgb(0x3A, 0x7B, 0xD5)
                : Color.FromRgb(0xE7, 0x4C, 0x3C)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Opens the Harmony Steam Workshop page in your browser"
        };
        workshopBtn.Click += (_, _) => OpenHarmonyDownloadPage();
        buttonPanel.Children.Add(workshopBtn);

        var githubBtn = new Button
        {
            Content = "Manual Download (GitHub)",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x55, 0x5F, 0x6E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "For non-Steam installs: download the Harmony mod ZIP from GitHub and extract into RimWorld/Mods"
        };
        githubBtn.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/pardeike/HarmonyRimWorld/releases/latest")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open Harmony GitHub page: {ex.Message}");
                ShowToast("Harmony", "Could not open the browser. Go to github.com/pardeike/HarmonyRimWorld/releases", ToastService.ToastType.Warning);
            }
        };
        buttonPanel.Children.Add(githubBtn);

        stackPanel.Children.Add(buttonPanel);
        return card;
    }

    private Border CreateSpeedModCard()
    {
        bool isInstalled = RimWorldSaveReader.IsSpeedModInstalled();

        var modDir = RimWorldSaveReader.GetSpeedModDir();
        var dllPath = modDir is not null
            ? System.IO.Path.Combine(modDir, "Assemblies", "FlexTool.SpeedMod.dll")
            : "RimWorld installation not found";

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "FlexTool Speed Mod",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 0.0));    // Red
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 1.0));    // White
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator (updated in place after install/remove)
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        var statusLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusLabel);
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Adds a clean 4x / 5x / 10x boost strip next to the in-game time controls. One click to engage, one click to restore your previous speed — boosts auto-pause during raids and cancel when you pause",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: 1.0.0",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // DLL Path
        stackPanel.Children.Add(new TextBlock
        {
            Text = "DLL Path:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = dllPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        // Info message (updated in place after install/remove)
        var infoText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        stackPanel.Children.Add(infoText);

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Single action button that flips between Install and Remove in place —
        // no panel rebuild, so scroll position and click handling stay stable.
        var actionBtn = new Button
        {
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(actionBtn);

        // Open Folder button
        var openFolderBtn = new Button
        {
            Content = "Open Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        openFolderBtn.Click += (s, e) => OpenSpeedModFolder();
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        // Applies installed/not-installed visuals to the changing elements
        void ApplyState(bool installed)
        {
            var statusColor = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
                : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

            statusDot.Fill = statusColor;
            statusLabel.Text = installed ? "Installed" : "Not Installed";
            statusLabel.Foreground = statusColor;

            infoText.Text = installed
                ? "✓ Speed mod is installed in RimWorld Mods folder"
                : "⚠ Speed mod is not installed in RimWorld Mods folder";
            infoText.Foreground = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

            actionBtn.Content = installed ? "Remove Mod" : "Install Mod";
            actionBtn.Background = installed
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D))   // Red
                : new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));  // Green
        }

        actionBtn.Click += (s, e) =>
        {
            try
            {
                if (RimWorldSaveReader.IsSpeedModInstalled())
                {
                    if (RimWorldSaveReader.RemoveSpeedMod())
                        ShowToast("Speed Mod", "Mod removed. Restart RimWorld to apply.", ToastService.ToastType.Success);
                    else
                        ShowToast("Speed Mod", "Mod deactivated, but the folder is locked (game running?).", ToastService.ToastType.Warning);
                }
                else
                {
                    if (RimWorldSaveReader.InstallSpeedMod())
                        ShowToast("Speed Mod", "Mod installed! Restart RimWorld to load it.", ToastService.ToastType.Success);
                    else
                        ShowToast("Error", "Could not deploy the mod. Is RimWorld installed?", ToastService.ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"Speed mod action failed: {ex.Message}", ToastService.ToastType.Error);
            }

            ApplyState(RimWorldSaveReader.IsSpeedModInstalled());
        };

        ApplyState(isInstalled);

        return card;
    }

    private void OpenSpeedModFolder()
    {
        try
        {
            var modDir = RimWorldSaveReader.GetSpeedModDir();

            // Fall back to the parent Mods folder when the mod isn't deployed yet
            string? target = modDir is not null && Directory.Exists(modDir)
                ? modDir
                : modDir is not null ? System.IO.Path.GetDirectoryName(modDir) : null;

            if (target is not null && Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowToast("Error", "RimWorld Mods folder not found.", ToastService.ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private void OpenAutoLoadModFolder()
    {
        try
        {
            var modDir = RimWorldSaveReader.GetAutoLoadModDir();
            string? target = modDir is not null && Directory.Exists(modDir)
                ? modDir
                : modDir is not null ? System.IO.Path.GetDirectoryName(modDir) : null;

            if (target is not null && Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowToast("Error", "RimWorld Mods folder not found.", ToastService.ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    // ── Modpack Export/Import ───────────────────────────────────
    // Bundles the full mod list + metadata into a single ".modpack" file so a
    // player can replicate their exact setup on another PC.

    private Border BuildModpackCard()
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Modpack",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Bundle your entire mod list and load order into a single .modpack file. Import it on another PC to replicate this exact setup.",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        // Modpack name field (layout only — disabled until wired up)
        var nameRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 280 });

        var nameLbl = new TextBlock
        {
            Text = "Pack Name",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameLbl, 0);

        var nameBox = new TextBox
        {
            Style = FindResource("DarkTextBoxStyle") as Style,
            Text = "My Modpack",
            FontSize = 12
        };
        Grid.SetColumn(nameBox, 1);

        nameRow.Children.Add(nameLbl);
        nameRow.Children.Add(nameBox);
        stack.Children.Add(nameRow);

        // Export / Import buttons
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal };

        var exportModpackBtn = new Button
        {
            Content = "Export Modpack…",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Save your active mod list and load order to a .modpack file"
        };
        exportModpackBtn.Click += (s, e) => ExportModpack(nameBox.Text);
        buttonRow.Children.Add(exportModpackBtn);

        var importModpackBtn = new Button
        {
            Content = "Import Modpack…",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Load a .modpack file and apply its mod list and load order"
        };
        importModpackBtn.Click += (s, e) => ImportModpack();
        buttonRow.Children.Add(importModpackBtn);

        var shareZipBtn = new Button
        {
            Content = "Share as Zip…",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Create a shareable .zip containing the modpack file plus all your locally-installed mod folders.\nPlayers without FlexTool can extract the folders straight into their RimWorld Mods directory."
        };
        shareZipBtn.Click += (s, e) => ShareModpackAsZip(nameBox.Text);
        buttonRow.Children.Add(shareZipBtn);

        stack.Children.Add(buttonRow);

        card.Child = stack;
        return card;
    }

    private void ExportModpack(string packName)
    {
        try
        {
            var activeMods = RimWorldSaveReader.GetActiveMods();
            if (activeMods.Count == 0)
            {
                ShowToast("Modpack", "No active mods to export.", ToastService.ToastType.Info);
                return;
            }

            if (string.IsNullOrWhiteSpace(packName)) packName = "My Modpack";
            var safeName = string.Concat(packName.Split(System.IO.Path.GetInvalidFileNameChars()));

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Modpack",
                Filter = "FlexTool Modpack (*.modpack)|*.modpack",
                FileName = safeName
            };
            if (dlg.ShowDialog(this) != true) return;

            var root = new System.Xml.Linq.XElement("FlexToolModpack",
                new System.Xml.Linq.XElement("Name", packName),
                new System.Xml.Linq.XElement("Created", DateTime.Now.ToString("o")),
                new System.Xml.Linq.XElement("GameVersion", RimWorldSaveReader.GameVersion),
                new System.Xml.Linq.XElement("Mods",
                    activeMods.Select(m => new System.Xml.Linq.XElement("Mod",
                        new System.Xml.Linq.XAttribute("packageId", m.PackageId),
                        new System.Xml.Linq.XAttribute("name", m.Name),
                        new System.Xml.Linq.XAttribute("version", m.Version ?? "")))));

            new System.Xml.Linq.XDocument(root).Save(dlg.FileName);
            ShowToast("Modpack", $"Exported {activeMods.Count} mods to {System.IO.Path.GetFileName(dlg.FileName)}",
                ToastService.ToastType.Success);
        }
        catch (Exception ex)
        {
            ShowToast("Modpack", $"Export failed: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    /// <summary>
    /// Creates a shareable .zip: the .modpack manifest plus every locally-installed
    /// active mod folder. Players without FlexTool can extract the folders directly
    /// into their RimWorld Mods directory. Workshop/DLC mods are listed but not bundled.
    /// </summary>
    private void ShareModpackAsZip(string packName)
    {
        try
        {
            var activeMods = RimWorldSaveReader.GetActiveMods();
            if (activeMods.Count == 0)
            {
                ShowToast("Modpack", "No active mods to share.", ToastService.ToastType.Info);
                return;
            }

            if (string.IsNullOrWhiteSpace(packName)) packName = "My Modpack";
            var safeName = string.Concat(packName.Split(System.IO.Path.GetInvalidFileNameChars()));

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Share Modpack as Zip",
                Filter = "Zip Archive (*.zip)|*.zip",
                FileName = safeName + ".zip"
            };
            if (dlg.ShowDialog(this) != true) return;

            if (System.IO.File.Exists(dlg.FileName)) System.IO.File.Delete(dlg.FileName);

            int bundled = 0;
            var notBundled = new List<string>();

            using (var zip = System.IO.Compression.ZipFile.Open(dlg.FileName, System.IO.Compression.ZipArchiveMode.Create))
            {
                // 1) Manifest so FlexTool users can import the load order
                var root = new System.Xml.Linq.XElement("FlexToolModpack",
                    new System.Xml.Linq.XElement("Name", packName),
                    new System.Xml.Linq.XElement("Created", DateTime.Now.ToString("o")),
                    new System.Xml.Linq.XElement("GameVersion", RimWorldSaveReader.GameVersion),
                    new System.Xml.Linq.XElement("Mods",
                        activeMods.Select(m => new System.Xml.Linq.XElement("Mod",
                            new System.Xml.Linq.XAttribute("packageId", m.PackageId),
                            new System.Xml.Linq.XAttribute("name", m.Name),
                            new System.Xml.Linq.XAttribute("version", m.Version ?? "")))));

                var manifestEntry = zip.CreateEntry(safeName + ".modpack");
                using (var ms = manifestEntry.Open())
                    new System.Xml.Linq.XDocument(root).Save(ms);

                // 2) Bundle locally-installed (non-Workshop, non-DLC/Core) mod folders
                foreach (var mod in activeMods)
                {
                    bool isLocalFolder = mod.Source == RimWorldSaveReader.ModSource.Local
                        && !string.IsNullOrEmpty(mod.FolderPath)
                        && System.IO.Directory.Exists(mod.FolderPath);

                    if (!isLocalFolder)
                    {
                        if (mod.Source != RimWorldSaveReader.ModSource.Core &&
                            mod.Source != RimWorldSaveReader.ModSource.DLC)
                            notBundled.Add(mod.Name);
                        continue;
                    }

                    var folderName = System.IO.Path.GetFileName(mod.FolderPath.TrimEnd('\\', '/'));
                    foreach (var file in System.IO.Directory.EnumerateFiles(mod.FolderPath, "*", System.IO.SearchOption.AllDirectories))
                    {
                        var relative = file.Substring(mod.FolderPath.Length).TrimStart('\\', '/');
                        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(
                            zip, file, $"Mods/{folderName}/{relative.Replace('\\', '/')}");
                    }
                    bundled++;
                }

                // 3) Readme for players without FlexTool
                var readme = zip.CreateEntry("README.txt");
                using (var sw = new System.IO.StreamWriter(readme.Open()))
                {
                    sw.WriteLine($"FlexTool Modpack: {packName}");
                    sw.WriteLine($"Created: {DateTime.Now:g}");
                    sw.WriteLine();
                    sw.WriteLine("How to install (no FlexTool needed):");
                    sw.WriteLine("1. Extract the \"Mods\" folder into your RimWorld installation directory,");
                    sw.WriteLine("   merging it with the existing Mods folder.");
                    sw.WriteLine("2. Enable the mods in RimWorld's mod menu, in the order listed below.");
                    sw.WriteLine();
                    sw.WriteLine("If you have FlexTool, import the .modpack file instead to apply the");
                    sw.WriteLine("exact load order automatically.");
                    sw.WriteLine();
                    sw.WriteLine("Load order:");
                    foreach (var m in activeMods)
                        sw.WriteLine($"  {m.Name} ({m.PackageId})");
                    if (notBundled.Count > 0)
                    {
                        sw.WriteLine();
                        sw.WriteLine("Not bundled (Steam Workshop mods — subscribe to these on the Workshop):");
                        foreach (var n in notBundled)
                            sw.WriteLine($"  {n}");
                    }
                }
            }

            ShowToast("Modpack",
                $"Zip created with {bundled} bundled mod folder{(bundled == 1 ? "" : "s")}" +
                (notBundled.Count > 0 ? $" ({notBundled.Count} Workshop mods listed in README)" : ""),
                ToastService.ToastType.Success);
        }
        catch (Exception ex)
        {
            ShowToast("Modpack", $"Zip export failed: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private void ImportModpack()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Modpack",
                Filter = "FlexTool Modpack (*.modpack)|*.modpack|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            var doc = System.Xml.Linq.XDocument.Load(dlg.FileName);
            var packName = doc.Root?.Element("Name")?.Value ?? "Unknown";
            var mods = doc.Root?.Element("Mods")?.Elements("Mod")
                .Select(m => (Id: m.Attribute("packageId")?.Value ?? "", Name: m.Attribute("name")?.Value ?? ""))
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .ToList() ?? [];

            if (mods.Count == 0)
            {
                ShowToast("Modpack", "The file contains no mods.", ToastService.ToastType.Warning);
                return;
            }

            // Check which mods are locally available
            var installed = RimWorldSaveReader.GetAllMods()
                .Select(m => m.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = mods.Where(m => !installed.Contains(m.Id)).ToList();

            var msg = $"Modpack \"{packName}\" contains {mods.Count} mods.";
            if (missing.Count > 0)
            {
                msg += $"\n\n{missing.Count} mods are NOT installed and will be skipped:\n"
                    + string.Join("\n", missing.Take(10).Select(m => "  • " + (string.IsNullOrEmpty(m.Name) ? m.Id : m.Name)));
                if (missing.Count > 10) msg += $"\n  …and {missing.Count - 10} more";
            }
            msg += "\n\nApply this mod list and load order?";

            if (!FlexToolDialog.ShowWarning(this, "Import Modpack", msg)) return;

            var newOrder = mods.Where(m => installed.Contains(m.Id)).Select(m => m.Id).ToList();
            if (newOrder.Count == 0)
            {
                ShowToast("Modpack", "None of the modpack's mods are installed.", ToastService.ToastType.Warning);
                return;
            }

            RimWorldSaveReader.ReorderMods(newOrder);
            ShowToast("Modpack", $"Applied {newOrder.Count} mods from \"{packName}\"" +
                (missing.Count > 0 ? $" ({missing.Count} missing skipped)" : ""),
                ToastService.ToastType.Success);
            UpdateContentArea();
        }
        catch (Exception ex)
        {
            ShowToast("Modpack", $"Import failed: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private Border CreateModCard(string modName, string version, string status)
    {
        var statusColor = status == "Active"
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
            : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(6),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Mod name
        stackPanel.Children.Add(new TextBlock
        {
            Text = modName,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"Version: {version}",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Status
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        statusPanel.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = statusColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        statusPanel.Children.Add(new TextBlock
        {
            Text = status,
            FontSize = 11,
            Foreground = statusColor,
            VerticalAlignment = VerticalAlignment.Center
        });

        stackPanel.Children.Add(statusPanel);

        return card;
    }

    private Border CreateLaunchDLLCard()
    {
        // The AutoLoad mod is deployed into the GAME's Mods folder (next to
        // the exe) — RimWorld does not scan the user-data Mods folder.
        bool isInstalled = RimWorldSaveReader.IsAutoLoadModInstalled();
        var modDir = RimWorldSaveReader.GetAutoLoadModDir();
        var autoLoadDllPath = modDir is not null
            ? System.IO.Path.Combine(modDir, "Assemblies", "FlexTool.AutoLoadMod.dll")
            : "RimWorld installation not found";

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "FlexTool AutoLoad Launcher",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x7B, 0xD5), 0.0));    // Blue
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x9B, 0x59, 0xB6), 1.0));    // Purple
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator (updated in place after install)
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        var statusLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusLabel);
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Enables automatic save loading and quick launch from FlexTool",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: 1.0.0",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // DLL Path
        stackPanel.Children.Add(new TextBlock
        {
            Text = "DLL Path:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = autoLoadDllPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        // Saves with AutoLoad
        var savesPath = RimWorldSaveReader.SavesPath;
        int saveCount = 0;
        if (Directory.Exists(savesPath))
        {
            try
            {
                saveCount = Directory.EnumerateFiles(savesPath, "*.rws", SearchOption.AllDirectories).Count();
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        }

        stackPanel.Children.Add(new TextBlock
        {
            Text = $"Available Saves: {saveCount}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // Info message (updated in place)
        var infoText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        stackPanel.Children.Add(infoText);

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Install button (only relevant when not installed — the mod is also
        // deployed automatically the first time a save is launched)
        var installBtn = new Button
        {
            Content = "Install Mod",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(installBtn);

        // Remove button (only relevant when installed)
        var removeBtn = new Button
        {
            Content = "Remove Mod",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(removeBtn);

        // Open Folder button
        var openFolderBtn = new Button
        {
            Content = "Open Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        openFolderBtn.Click += (s, e) => OpenAutoLoadModFolder();
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        void ApplyState(bool installed)
        {
            var statusColor = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
                : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

            statusDot.Fill = statusColor;
            statusLabel.Text = installed ? "Installed" : "Not Installed";
            statusLabel.Foreground = statusColor;

            infoText.Text = installed
                ? "✓ AutoLoad mod is installed and ready to use with RimWorld"
                : "⚠ AutoLoad mod is not installed — it will be deployed automatically when you launch a save, or install it now";
            infoText.Foreground = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

            installBtn.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
            removeBtn.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        }

        installBtn.Click += (s, e) =>
        {
            try
            {
                if (RimWorldSaveReader.InstallAutoLoadMod())
                    ShowToast("AutoLoad Launcher", "Mod installed! Launch-to-save is ready.", ToastService.ToastType.Success);
                else
                    ShowToast("Error", "Could not deploy the mod. Is RimWorld installed?", ToastService.ToastType.Error);
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"AutoLoad install failed: {ex.Message}", ToastService.ToastType.Error);
            }
            ApplyState(RimWorldSaveReader.IsAutoLoadModInstalled());
        };

        removeBtn.Click += (s, e) =>
        {
            try
            {
                if (RimWorldSaveReader.RemoveAutoLoadMod())
                    ShowToast("AutoLoad Launcher", "Mod removed. It will be redeployed automatically next time you launch a save.", ToastService.ToastType.Success);
                else
                    ShowToast("AutoLoad Launcher", "Mod deactivated, but the folder is locked (game running?).", ToastService.ToastType.Warning);
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"AutoLoad remove failed: {ex.Message}", ToastService.ToastType.Error);
            }
            ApplyState(RimWorldSaveReader.IsAutoLoadModInstalled());
        };

        ApplyState(isInstalled);

        return card;
    }

    private Border CreateCheatsModCard()
    {
        bool isInstalled = RimWorldSaveReader.IsCheatsModInstalled();

        var modDir = RimWorldSaveReader.GetCheatsModDir();
        var dllPath = modDir is not null
            ? System.IO.Path.Combine(modDir, "Assemblies", "FlexTool.CheatsMod.dll")
            : "RimWorld installation not found";

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "FlexTool Cheats",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE7, 0x41, 0x41), 0.0));    // Red
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xA5, 0x00), 1.0));    // Orange
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator (updated in place after install/remove)
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        var statusLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusLabel);
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "In-game cheats menu opened from a Cheats button below the speed controls, with Main, Pawn, Resources, Colony, and World tabs. Main tab includes god mode and auto-fill needs toggles, healing, mental break recovery, and reviving colonists.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: Beta",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // DLL Path
        stackPanel.Children.Add(new TextBlock
        {
            Text = "DLL Path:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = dllPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        // Info message
        var infoText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        stackPanel.Children.Add(infoText);

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Action button (Install/Remove)
        var actionBtn = new Button
        {
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(actionBtn);

        // Open Folder button
        var openFolderBtn = new Button
        {
            Content = "Open Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        openFolderBtn.Click += (s, e) => OpenCheatsModFolder();
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        // Apply state to UI elements
        void ApplyState(bool installed)
        {
            var statusColor = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
                : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

            statusDot.Fill = statusColor;
            statusLabel.Text = installed ? "Installed" : "Not Installed";
            statusLabel.Foreground = statusColor;

            infoText.Text = installed
                ? "✓ Cheats mod is installed in RimWorld Mods folder"
                : "⚠ Cheats mod is not installed in RimWorld Mods folder";
            infoText.Foreground = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

            actionBtn.Content = installed ? "Remove Mod" : "Install Mod";
            actionBtn.Background = installed
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D))   // Red
                : new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));  // Green
        }

        actionBtn.Click += (s, e) =>
        {
            try
            {
                if (RimWorldSaveReader.IsCheatsModInstalled())
                {
                    if (RimWorldSaveReader.RemoveCheatsMod())
                        ShowToast("Cheats Mod", "Mod removed. Restart RimWorld to apply.", ToastService.ToastType.Success);
                    else
                        ShowToast("Cheats Mod", "Mod deactivated, but the folder is locked (game running?).", ToastService.ToastType.Warning);
                }
                else
                {
                    if (RimWorldSaveReader.InstallCheatsMod())
                        ShowToast("Cheats Mod", "Mod installed! Restart RimWorld to load it.", ToastService.ToastType.Success);
                    else
                        ShowToast("Error", "Could not deploy the mod. Is RimWorld installed?", ToastService.ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"Cheats mod action failed: {ex.Message}", ToastService.ToastType.Error);
            }

            ApplyState(RimWorldSaveReader.IsCheatsModInstalled());
        };

        ApplyState(isInstalled);

        return card;
    }

    private void OpenCheatsModFolder()
    {
        try
        {
            var modDir = RimWorldSaveReader.GetCheatsModDir();

            string? target = modDir is not null && Directory.Exists(modDir)
                ? modDir
                : modDir is not null ? System.IO.Path.GetDirectoryName(modDir) : null;

            if (target is not null && Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowToast("Error", "RimWorld Mods folder not found.", ToastService.ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private Border CreateTillDeathModCard()
    {
        return CreateSimpleModCard(
            title: "FlexTool Till Death Do Us Part",
            titleColors: (Color.FromRgb(0xE7, 0x41, 0x9E), Color.FromRgb(0xFF, 0x6B, 0x6B)),
            description: "Colonist couples stay together forever. Disables the social interactions and random events that trigger breakups and divorces. Safe to add or remove from existing saves.",
            dllFileName: "FlexTool.TillDeathMod.dll",
            toastTitle: "Till Death Mod",
            getModDir: RimWorldSaveReader.GetTillDeathModDir,
            isInstalled: RimWorldSaveReader.IsTillDeathModInstalled,
            install: RimWorldSaveReader.InstallTillDeathMod,
            remove: RimWorldSaveReader.RemoveTillDeathMod);
    }

    private Border CreateKeepItTogetherModCard()
    {
        return CreateSimpleModCard(
            title: "FlexTool Keep It Together",
            titleColors: (Color.FromRgb(0x9B, 0x59, 0xB6), Color.FromRgb(0x3D, 0xAE, 0xFF)),
            description: "Adds a Relationship Health tab to the bottom bar with a health bar for every colonist couple. When a relationship gets dangerously low, unlocks a Forced Reconciliation toggle that makes the couple spend recreation time together until they are out of the danger zone.",
            dllFileName: "FlexTool.KeepItTogetherMod.dll",
            toastTitle: "Keep It Together Mod",
            getModDir: RimWorldSaveReader.GetKeepItTogetherModDir,
            isInstalled: RimWorldSaveReader.IsKeepItTogetherModInstalled,
            install: RimWorldSaveReader.InstallKeepItTogetherMod,
            remove: RimWorldSaveReader.RemoveKeepItTogetherMod);
    }

    private Border CreateSimpleModCard(
        string title,
        (Color start, Color end) titleColors,
        string description,
        string dllFileName,
        string toastTitle,
        Func<string?> getModDir,
        Func<bool> isInstalled,
        Func<bool> install,
        Func<bool> remove)
    {
        var modDir = getModDir();
        var dllPath = modDir is not null
            ? System.IO.Path.Combine(modDir, "Assemblies", dllFileName)
            : "RimWorld installation not found";

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(titleColors.start, 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(titleColors.end, 1.0));
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator (updated in place after install/remove)
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        var statusLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusLabel);
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: 1.0.0",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // DLL Path
        stackPanel.Children.Add(new TextBlock
        {
            Text = "DLL Path:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = dllPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        // Info message
        var infoText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        stackPanel.Children.Add(infoText);

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Action button (Install/Remove)
        var actionBtn = new Button
        {
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(actionBtn);

        // Open Folder button
        var openFolderBtn = new Button
        {
            Content = "Open Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        openFolderBtn.Click += (s, e) => OpenModFolder(getModDir());
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        // Apply state to UI elements
        void ApplyState(bool installed)
        {
            var statusColor = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
                : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

            statusDot.Fill = statusColor;
            statusLabel.Text = installed ? "Installed" : "Not Installed";
            statusLabel.Foreground = statusColor;

            infoText.Text = installed
                ? "✓ Mod is installed in RimWorld Mods folder"
                : "⚠ Mod is not installed in RimWorld Mods folder";
            infoText.Foreground = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

            actionBtn.Content = installed ? "Remove Mod" : "Install Mod";
            actionBtn.Background = installed
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D))   // Red
                : new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));  // Green
        }

        actionBtn.Click += (s, e) =>
        {
            try
            {
                if (isInstalled())
                {
                    if (remove())
                        ShowToast(toastTitle, "Mod removed. Restart RimWorld to apply.", ToastService.ToastType.Success);
                    else
                        ShowToast(toastTitle, "Mod deactivated, but the folder is locked (game running?).", ToastService.ToastType.Warning);
                }
                else
                {
                    if (install())
                        ShowToast(toastTitle, "Mod installed! Restart RimWorld to load it.", ToastService.ToastType.Success);
                    else
                        ShowToast("Error", "Could not deploy the mod. Is RimWorld installed?", ToastService.ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"{toastTitle} action failed: {ex.Message}", ToastService.ToastType.Error);
            }

            ApplyState(isInstalled());
        };

        ApplyState(isInstalled());

        return card;
    }

    private void OpenModFolder(string? modDir)
    {
        try
        {
            string? target = modDir is not null && Directory.Exists(modDir)
                ? modDir
                : modDir is not null ? System.IO.Path.GetDirectoryName(modDir) : null;

            if (target is not null && Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowToast("Error", "RimWorld Mods folder not found.", ToastService.ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private Border CreatePerfModCard()
    {
        bool isInstalled = RimWorldSaveReader.IsPerfModInstalled();
        var modDir = RimWorldSaveReader.GetPerfModDir();
        var dllPath = modDir is not null
            ? System.IO.Path.Combine(modDir, "Assemblies", "FlexTool.PerfMod.dll")
            : "RimWorld installation not found";

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "FlexTool Performance",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0xCC, 0x71), 0.0));    // Green
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0xAE, 0xFF), 1.0));    // Blue
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator (updated in place after install/remove)
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        var statusLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusLabel);
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Boosts TPS with adaptive pawn tick throttling (off-screen idle animals only, lossless time banking) and smarter movement via distance-scaled pathfinding — long routes compute up to 5x faster. Auto-engages only when FPS drops; zero impact on colonists and combat.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: 1.0.0.0",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // DLL Path
        stackPanel.Children.Add(new TextBlock
        {
            Text = "DLL Path:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = dllPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        // Info message
        var infoText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        stackPanel.Children.Add(infoText);

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Action button (Install/Remove)
        var actionBtn = new Button
        {
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(actionBtn);

        // Open Folder button
        var openFolderBtn = new Button
        {
            Content = "Open Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        openFolderBtn.Click += (s, e) => OpenPerfModFolder();
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        // Apply state to UI elements
        void ApplyState(bool installed)
        {
            var statusColor = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
                : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

            statusDot.Fill = statusColor;
            statusLabel.Text = installed ? "Installed" : "Not Installed";
            statusLabel.Foreground = statusColor;

            infoText.Text = installed
                ? "✓ Performance mod is installed in RimWorld Mods folder"
                : "⚠ Performance mod is not installed in RimWorld Mods folder";
            infoText.Foreground = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

            actionBtn.Content = installed ? "Remove Mod" : "Install Mod";
            actionBtn.Background = installed
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D))   // Red
                : new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));  // Green
        }

        actionBtn.Click += (s, e) =>
        {
            try
            {
                if (RimWorldSaveReader.IsPerfModInstalled())
                {
                    if (RimWorldSaveReader.RemovePerfMod())
                        ShowToast("Performance Mod", "Mod removed. Restart RimWorld to apply.", ToastService.ToastType.Success);
                    else
                        ShowToast("Performance Mod", "Mod deactivated, but the folder is locked (game running?).", ToastService.ToastType.Warning);
                }
                else
                {
                    if (RimWorldSaveReader.InstallPerfMod())
                        ShowToast("Performance Mod", "Mod installed! Restart RimWorld to load it.", ToastService.ToastType.Success);
                    else
                        ShowToast("Error", "Could not deploy the mod. Is RimWorld installed?", ToastService.ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"Performance mod action failed: {ex.Message}", ToastService.ToastType.Error);
            }

            ApplyState(RimWorldSaveReader.IsPerfModInstalled());
        };

        ApplyState(isInstalled);

        return card;
    }

    private void OpenPerfModFolder()
    {
        try
        {
            var modDir = RimWorldSaveReader.GetPerfModDir();

            string? target = modDir is not null && Directory.Exists(modDir)
                ? modDir
                : modDir is not null ? System.IO.Path.GetDirectoryName(modDir) : null;

            if (target is not null && Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowToast("Error", "RimWorld Mods folder not found.", ToastService.ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private Border CreateFpsOptimizerCard()
    {
        bool isInstalled = RimWorldSaveReader.IsFpsOptimizerInstalled();

        var modDir = RimWorldSaveReader.GetFpsOptimizerModDir();
        var dllPath = modDir is not null
            ? System.IO.Path.Combine(modDir, "Assemblies", "FlexTool.FPSOptimizer.dll")
            : "RimWorld installation not found";

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel { Orientation = Orientation.Vertical }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "FlexTool FPS Optimizer",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xAA, 0x00), 0.0));    // Orange
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0xAE, 0xFF), 1.0));    // Blue
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator (updated in place after install/remove)
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        var statusLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusLabel);
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "All-in-one adaptive performance mod (now includes everything from FlexTool Performance). Batches pawn AI job searches across frames to kill CPU spikes, throttles off-map world pawns, ambient map effects and off-screen idle animals under load (lossless — skipped time is banked and replayed), speeds up long-distance pathfinding, and schedules small predictable garbage collections instead of multi-second freezes. Always active: idle when FPS is healthy, engages automatically under load, never changes gameplay outcomes.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: 2.0.0",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // Live status (populated while the game runs with the mod active)
        var liveStatusText = new TextBlock
        {
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Visibility = Visibility.Collapsed
        };
        stackPanel.Children.Add(liveStatusText);

        var status = RimWorldSaveReader.ReadFpsOptimizerStatus();
        if (status is not null)
        {
            var loadLevel = status.TryGetValue("loadLevel", out var ll) ? ll : "0";
            var loadName = loadLevel switch { "2" => "Heavy throttling", "1" => "Mild throttling", _ => "Idle (smooth)" };
            liveStatusText.Text =
                $"LIVE  •  FPS: {status.GetValueOrDefault("fps", "?")}  •  Colonists: {status.GetValueOrDefault("colonists", "?")}\n" +
                $"Mode: {loadName}  •  Job searches smoothed: {status.GetValueOrDefault("jobSearchesDeferred", "0")}\n" +
                $"World-pawn ticks saved: {status.GetValueOrDefault("worldPawnTicksSkipped", "0")}  •  Pawn ticks throttled: {status.GetValueOrDefault("pawnTicksThrottled", "0")}  •  Smooth GCs: {status.GetValueOrDefault("gcCollections", "0")}";
            liveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            liveStatusText.Visibility = Visibility.Visible;
        }

        // DLL Path
        stackPanel.Children.Add(new TextBlock
        {
            Text = "DLL Path:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = dllPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        // Info message
        var infoText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        stackPanel.Children.Add(infoText);

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Action button (Install/Remove)
        var actionBtn = new Button
        {
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(actionBtn);

        // Open Folder button
        var openFolderBtn = new Button
        {
            Content = "Open Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        openFolderBtn.Click += (s, e) =>
        {
            try
            {
                var dir = RimWorldSaveReader.GetFpsOptimizerModDir();
                string? target = dir is not null && Directory.Exists(dir)
                    ? dir
                    : dir is not null ? System.IO.Path.GetDirectoryName(dir) : null;

                if (target is not null && Directory.Exists(target))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = target,
                        UseShellExecute = true
                    });
                else
                    ShowToast("Error", "RimWorld Mods folder not found.", ToastService.ToastType.Error);
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
            }
        };
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        // Apply state to UI elements
        void ApplyState(bool installed)
        {
            var statusColor = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
                : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

            statusDot.Fill = statusColor;
            statusLabel.Text = installed ? "Installed" : "Not Installed";
            statusLabel.Foreground = statusColor;

            infoText.Text = installed
                ? "✓ FPS Optimizer is installed in RimWorld Mods folder"
                : "⚠ FPS Optimizer is not installed in RimWorld Mods folder";
            infoText.Foreground = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

            actionBtn.Content = installed ? "Remove Mod" : "Install Mod";
            actionBtn.Background = installed
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D))   // Red
                : new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));  // Green
        }

        actionBtn.Click += (s, e) =>
        {
            try
            {
                if (RimWorldSaveReader.IsFpsOptimizerInstalled())
                {
                    if (RimWorldSaveReader.RemoveFpsOptimizer())
                        ShowToast("FPS Optimizer", "Mod removed. Restart RimWorld to apply.", ToastService.ToastType.Success);
                    else
                        ShowToast("FPS Optimizer", "Mod deactivated, but the folder is locked (game running?).", ToastService.ToastType.Warning);
                }
                else
                {
                    if (RimWorldSaveReader.InstallFpsOptimizer())
                        ShowToast("FPS Optimizer", "Mod installed! Restart RimWorld to load it.", ToastService.ToastType.Success);
                    else
                        ShowToast("Error", "Could not deploy the mod. Is RimWorld installed?", ToastService.ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"FPS Optimizer action failed: {ex.Message}", ToastService.ToastType.Error);
            }

            ApplyState(RimWorldSaveReader.IsFpsOptimizerInstalled());
        };

        ApplyState(isInstalled);

        return card;
    }

    private Border CreateDebugInfoModCard()
    {
        bool isInstalled = RimWorldSaveReader.IsDebugInfoModInstalled();

        var modDir = RimWorldSaveReader.GetDebugInfoModDir();
        var dllPath = modDir is not null
            ? System.IO.Path.Combine(modDir, "Assemblies", "FlexTool.DebugInfoMod.dll")
            : "RimWorld installation not found";

        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "FlexTool Debug Info",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x9B, 0x59, 0xB6), 0.0));    // Purple
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0xAE, 0xFF), 1.0));    // Blue
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator (updated in place after install/remove)
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        var statusLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(statusLabel);
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Displays a live info overlay on the right side of the screen — FPS, memory usage, current tick rate, pawn statistics and the save you're playing. Includes CrashGuard: in-game crash & freeze protection with exception interception, emergency autosaves, memory-pressure relief and a stall watchdog (armed by the Crash Handler in Configs). Optional in-game alert popups warn about low FPS, high memory and protective actions.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: 1.0.0",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // DLL Path
        stackPanel.Children.Add(new TextBlock
        {
            Text = "DLL Path:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = dllPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        // Overlay options label
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Overlay Options:",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // Load persisted overlay state
        var overlaySettings = RimWorldSaveReader.ReadDebugOverlaySettings();
        bool ovEnabled = overlaySettings.Enabled;
        bool ovFps = overlaySettings.Fps;
        bool ovMemory = overlaySettings.Memory;
        bool ovTickRate = overlaySettings.TickRate;
        bool ovPawnStats = overlaySettings.PawnStats;
        bool ovSaveName = overlaySettings.SaveName;
        bool ovAlerts = overlaySettings.Alerts;

        void SaveOverlaySettings() =>
            RimWorldSaveReader.WriteDebugOverlaySettings(ovEnabled, ovFps, ovMemory, ovTickRate, ovPawnStats, ovSaveName, ovAlerts);

        // Overlay toggle buttons panel (FPS / Memory / Tick Rate / Pawn Stats)
        var overlayTogglePanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        Button MakeOverlayToggleButton(string label, bool initial, Action<bool> onChanged)
        {
            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 11,
                Background = initial
                    ? new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6))
                    : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 6),
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Tag = initial,
                ToolTip = $"Toggle {label} in the in-game overlay"
            };

            btn.Click += (s, e) =>
            {
                bool pressed = !(bool)btn.Tag;
                btn.Tag = pressed;
                btn.Background = pressed
                    ? new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6))   // Purple = "on"
                    : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42));  // Gray = "off"
                onChanged(pressed);
                SaveOverlaySettings();
            };

            return btn;
        }

        overlayTogglePanel.Children.Add(MakeOverlayToggleButton("FPS", ovFps, v => ovFps = v));
        overlayTogglePanel.Children.Add(MakeOverlayToggleButton("Memory Usage", ovMemory, v => ovMemory = v));
        overlayTogglePanel.Children.Add(MakeOverlayToggleButton("Tick Rate", ovTickRate, v => ovTickRate = v));
        overlayTogglePanel.Children.Add(MakeOverlayToggleButton("Pawn Statistics", ovPawnStats, v => ovPawnStats = v));
        overlayTogglePanel.Children.Add(MakeOverlayToggleButton("Current Save", ovSaveName, v => ovSaveName = v));
        overlayTogglePanel.Children.Add(MakeOverlayToggleButton("In-Game Alerts", ovAlerts, v => ovAlerts = v));

        stackPanel.Children.Add(overlayTogglePanel);

        // Master overlay on/off button
        var overlayMasterBtn = new Button
        {
            Content = ovEnabled ? "Hide Overlay In-Game" : "Show Overlay In-Game",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = ovEnabled
                ? new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6))
                : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Toggle the debug overlay in a running game (mod must be installed)"
        };
        overlayMasterBtn.Click += (s, e) =>
        {
            ovEnabled = !ovEnabled;
            overlayMasterBtn.Content = ovEnabled ? "Hide Overlay In-Game" : "Show Overlay In-Game";
            overlayMasterBtn.Background = ovEnabled
                ? new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6))
                : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42));
            SaveOverlaySettings();
            ShowToast("Debug Overlay", ovEnabled
                ? "Overlay enabled — visible in-game within a second."
                : "Overlay disabled.", ToastService.ToastType.Info);
        };
        stackPanel.Children.Add(overlayMasterBtn);

        // Info message (updated in place after install/remove)
        var infoText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        stackPanel.Children.Add(infoText);

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Action button (Install/Remove)
        var actionBtn = new Button
        {
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        buttonPanel.Children.Add(actionBtn);

        // Open Folder button
        var openFolderBtn = new Button
        {
            Content = "Open Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0)
        };
        openFolderBtn.Click += (s, e) => OpenDebugInfoModFolder();
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        // Apply state to UI elements
        void ApplyState(bool installed)
        {
            var statusColor = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))  // Green
                : new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)); // Gray

            statusDot.Fill = statusColor;
            statusLabel.Text = installed ? "Installed" : "Not Installed";
            statusLabel.Foreground = statusColor;

            infoText.Text = installed
                ? "✓ Debug info mod is installed in RimWorld Mods folder"
                : "⚠ Debug info mod is not installed in RimWorld Mods folder";
            infoText.Foreground = installed
                ? new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));

            actionBtn.Content = installed ? "Remove Mod" : "Install Mod";
            actionBtn.Background = installed
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D))   // Red
                : new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));  // Green
        }

        actionBtn.Click += (s, e) =>
        {
            try
            {
                if (RimWorldSaveReader.IsDebugInfoModInstalled())
                {
                    if (RimWorldSaveReader.RemoveDebugInfoMod())
                        ShowToast("Debug Info Mod", "Mod removed. Restart RimWorld to apply.", ToastService.ToastType.Success);
                    else
                        ShowToast("Debug Info Mod", "Mod deactivated, but the folder is locked (game running?).", ToastService.ToastType.Warning);
                }
                else
                {
                    if (RimWorldSaveReader.InstallDebugInfoMod())
                        ShowToast("Debug Info Mod", "Mod installed! Restart RimWorld to load it.", ToastService.ToastType.Success);
                    else
                        ShowToast("Error", "Could not deploy the mod. Is RimWorld installed?", ToastService.ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"Debug info mod action failed: {ex.Message}", ToastService.ToastType.Error);
            }

            ApplyState(RimWorldSaveReader.IsDebugInfoModInstalled());
        };

        ApplyState(isInstalled);

        return card;
    }

    private void OpenDebugInfoModFolder()
    {
        try
        {
            var modDir = RimWorldSaveReader.GetDebugInfoModDir();

            string? target = modDir is not null && Directory.Exists(modDir)
                ? modDir
                : modDir is not null ? System.IO.Path.GetDirectoryName(modDir) : null;

            if (target is not null && Directory.Exists(target))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = target,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowToast("Error", "RimWorld Mods folder not found.", ToastService.ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private Border CreatePawnExtractorModCard()
    {
        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("PanelDarkBrush"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical
            }
        };

        var stackPanel = (StackPanel)card.Child;

        // Title with gradient
        var titleBlock = new TextBlock
        {
            Text = "FlexTool Pawn Extractor",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0xCC, 0x71), 0.0));    // Green
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x9B, 0x59, 0xB6), 1.0));    // Purple
        titleBlock.Foreground = gradientBrush;
        stackPanel.Children.Add(titleBlock);

        // Status indicator — built-in feature, always available
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var statusDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusPanel.Children.Add(statusDot);

        statusPanel.Children.Add(new TextBlock
        {
            Text = "Built-In — Ready",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            VerticalAlignment = VerticalAlignment.Center
        });
        stackPanel.Children.Add(statusPanel);

        // Description
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Pull a colonist out of one save and drop them into another, gear and all — LIVE through the FlexTool Debug Info mod. Be in the save you want to extract from, then load the save you want to spawn into. Use the Pawn Extractor tab under Mods to extract and spawn pawns.",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        // Version
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Version: Beta",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Supported Game Versions
        stackPanel.Children.Add(new TextBlock
        {
            Text = "Supported Versions: 1.3, 1.4, 1.5, 1.6",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xAE, 0xFF)),
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        // Info message
        stackPanel.Children.Add(new TextBlock
        {
            Text = "✓ Requires the FlexTool Debug Info mod — works live while the game is running",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Buttons panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // Open Pawn Extractor tab
        var openTabBtn = new Button
        {
            Content = "Open Pawn Extractor",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Go to the Pawn Extractor tab"
        };
        openTabBtn.Click += (s, e) =>
        {
            var items = TabSidebarItems.GetValueOrDefault(_activeTab, []);
            BuildSidebar(items, "Pawn Extractor");
        };
        buttonPanel.Children.Add(openTabBtn);

        // Open pawn library folder
        var openFolderBtn = new Button
        {
            Content = "Open Library Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Open the extracted pawn library folder"
        };
        openFolderBtn.Click += (s, e) =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(RimWorldSaveReader.PawnLibraryPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = RimWorldSaveReader.PawnLibraryPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"Error opening folder: {ex.Message}", ToastService.ToastType.Error);
            }
        };
        buttonPanel.Children.Add(openFolderBtn);

        stackPanel.Children.Add(buttonPanel);

        return card;
    }
}
