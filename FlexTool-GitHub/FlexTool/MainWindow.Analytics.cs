using System.Diagnostics;
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
    // ── Analytics Page ──────────────────────────────────────────
    private string _analyticsPerfSearch = "";
    private string _analyticsPerfSort = "Total Size";
    private string _analyticsLogFilter = "All";
    private string _analyticsLogSearch = "";
    private bool _analyticsLiveLogs;
    private DispatcherTimer? _liveLogTimer;
    private DateTime? _liveLogLastModified;

    // Cached page data so search/sort/filter interactions rebuild the UI
    // synchronously without re-scanning mod folders or re-parsing Player.log
    private List<RimWorldSaveReader.ModPerformanceData>? _analyticsPerfData;
    private List<RimWorldSaveReader.GameLogEntry>? _analyticsLogEntries;
    private StackPanel? _analyticsRootPanel;
    private const string PerfMonitorSectionTag = "AnalyticsPerfMonitorSection";

    // ── Live Warning Alerts state ───────────────────────────────
    private bool _alertsEnabled;
    private double _alertFpsThreshold = 30;
    private double _alertMemThresholdMb = 4096;
    private bool _alertGcSpikes;
    private bool _alertFpsEnabled = true;
    private bool _alertMemEnabled = true;
    private bool _alertCrashGuardEnabled = true;
    private bool _alertEmergencySaveEnabled = true;
    private DispatcherTimer? _alertTimer;
    private DateTime _lastFpsAlert = DateTime.MinValue;
    private DateTime _lastMemAlert = DateTime.MinValue;
    private DateTime _lastGcAlert = DateTime.MinValue;
    private int _lastGc2Count = -1;
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromSeconds(60);

    // ── Analytics Page ──────────────────────────────────────────

    private UIElement BuildAnalyticsPage(
        List<RimWorldSaveReader.ModPerformanceData>? prefetchedPerf = null,
        List<RimWorldSaveReader.GameLogEntry>? prefetchedLogs = null)
    {
        // Cache the data so later interactions can rebuild without disk I/O
        _analyticsPerfData = prefetchedPerf ?? RimWorldSaveReader.GetModPerformanceData();
        _analyticsLogEntries = prefetchedLogs ?? RimWorldSaveReader.GetGameLogEntries();

        var root = new StackPanel();
        _analyticsRootPanel = root;

        // Live Performance Monitor (IPC)
        root.Children.Add(BuildPerformanceMonitorSection());

        // Live Warning Alerts moved to Configs tab.

        // Mod Performance section
        root.Children.Add(BuildModPerformanceSection(_analyticsPerfData));

        // Game Logs section
        root.Children.Add(BuildGameLogsSection(_analyticsLogEntries));

        return root;
    }

    /// <summary>
    /// Rebuilds the Analytics page synchronously from cached data.
    /// Used by search/sort/filter interactions so a keystroke never shows
    /// a loading spinner, re-scans mod folders, or re-parses Player.log.
    /// </summary>
    private void RefreshAnalyticsView()
    {
        if (_analyticsPerfData is null || _analyticsLogEntries is null)
        {
            UpdateContentArea();
            return;
        }

        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(BuildAnalyticsPage(_analyticsPerfData, _analyticsLogEntries));
    }

    // ── Mod Performance ─────────────────────────────────────────

    private UIElement BuildModPerformanceSection(
        List<RimWorldSaveReader.ModPerformanceData>? prefetchedData = null)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "MOD FOOTPRINT ANALYSIS",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Scans each mod's folder to measure XML definitions, patches, assemblies, and textures.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var perfData = prefetchedData ?? RimWorldSaveReader.GetModPerformanceData();

        // Summary stats
        int totalMods = perfData.Count;
        int activeMods = perfData.Count(m => m.IsActive);
        long totalXml = perfData.Sum(m => m.XmlTotalBytes);
        long totalDll = perfData.Sum(m => m.DllTotalBytes);
        long totalTex = perfData.Sum(m => m.TextureTotalBytes);
        long totalSize = perfData.Sum(m => m.TotalFolderBytes);
        int totalXmlFiles = perfData.Sum(m => m.XmlFileCount);
        int totalPatches = perfData.Sum(m => m.PatchesFileCount);

        var statsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

        void AddPerfStat(string label, string value, Color color, string? tooltip = null)
        {
            var stat = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 8, 8),
                ToolTip = tooltip
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            stat.Child = sp;
            statsRow.Children.Add(stat);
        }

        AddPerfStat("Total Size", FormatFileSize(totalSize), Color.FromRgb(0x3A, 0x7B, 0xD5),
            "Combined disk footprint of all mod folders");
        AddPerfStat("XML Files", totalXmlFiles.ToString(), Color.FromRgb(0xF3, 0x9C, 0x12),
            $"Total XML files across all mods ({FormatFileSize(totalXml)})");
        AddPerfStat("XML Size", FormatFileSize(totalXml), Color.FromRgb(0xFF, 0x70, 0x43),
            "Total size of all XML definitions and patches");
        AddPerfStat("DLL Size", FormatFileSize(totalDll), Color.FromRgb(0xE7, 0x4C, 0x3C),
            "Total size of all compiled mod assemblies");
        AddPerfStat("Textures", FormatFileSize(totalTex), Color.FromRgb(0x9B, 0x59, 0xB6),
            "Total size of texture assets (PNG, JPG, DDS)");
        AddPerfStat("Patches", totalPatches.ToString(), Color.FromRgb(0x1A, 0xBC, 0x9C),
            "Total XML patch operations — high counts can slow load times");

        stack.Children.Add(statsRow);

        // Toolbar: search + sort
        var toolBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBoxStyle"),
            Text = _analyticsPerfSearch,
            FontSize = 12
        };
        var placeholder = new TextBlock
        {
            Text = "🔍  Search mods...",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false,
            Margin = new Thickness(10, 7, 0, 0),
            Visibility = string.IsNullOrEmpty(_analyticsPerfSearch) ? Visibility.Visible : Visibility.Collapsed
        };

        var searchGrid = new Grid();
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(placeholder);
        Grid.SetColumn(searchGrid, 0);
        toolBar.Children.Add(searchGrid);

        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            if (searchBox.Text != _analyticsPerfSearch)
            {
                _analyticsPerfSearch = searchBox.Text;
                RefreshAnalyticsView();
            }
        };
        if (!string.IsNullOrEmpty(_analyticsPerfSearch))
        {
            searchBox.Loaded += (_, _) =>
            {
                searchBox.Focus();
                searchBox.CaretIndex = searchBox.Text.Length;
            };
        }

        // Sort dropdown
        var sortModes = new[] { "Total Size", "XML Size", "XML Count", "DLL Size", "Textures", "Patches", "Name" };

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
            ToolTip = "Changes how this analysis list is displayed only.\nTo change the actual in-game load order, use the Mod Manager tab.",
            Child = new TextBlock
            {
                Text = $"Sort: {_analyticsPerfSort} ▾",
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
            bool isCurrent = mode == _analyticsPerfSort;
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
                if (_analyticsPerfSort != m)
                {
                    _analyticsPerfSort = m;
                    RefreshAnalyticsView();
                }
            };

            dropdownPanel.Children.Add(item);
        }

        sortBtn.MouseLeftButtonUp += (_, _) => dropdown.IsOpen = !dropdown.IsOpen;
        Grid.SetColumn(sortBtn, 1);
        toolBar.Children.Add(sortBtn);

        stack.Children.Add(toolBar);

        // Filter
        var filter = _analyticsPerfSearch.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? perfData
            : perfData.Where(m =>
                m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                m.PackageId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                m.Author.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Sort
        IEnumerable<RimWorldSaveReader.ModPerformanceData> sorted = _analyticsPerfSort switch
        {
            "XML Size" => filtered.OrderByDescending(m => m.XmlTotalBytes),
            "XML Count" => filtered.OrderByDescending(m => m.XmlFileCount),
            "DLL Size" => filtered.OrderByDescending(m => m.DllTotalBytes),
            "Textures" => filtered.OrderByDescending(m => m.TextureTotalBytes),
            "Patches" => filtered.OrderByDescending(m => m.PatchesFileCount),
            "Name" => filtered.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(m => m.TotalFolderBytes)
        };

        var modList = new StackPanel();

        // Column headers
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        void AddHeader(string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(tb, col);
            headerGrid.Children.Add(tb);
        }

        var nameHeader = new TextBlock
        {
            Text = "MOD",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        };
        Grid.SetColumn(nameHeader, 0);
        headerGrid.Children.Add(nameHeader);
        AddHeader("XML", 1);
        AddHeader("DLLs", 2);
        AddHeader("TEXTURES", 3);
        AddHeader("PATCHES", 4);
        AddHeader("TOTAL", 5);

        modList.Children.Add(headerGrid);

        modList.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BorderBlueBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var mod in sorted)
        {
            modList.Children.Add(BuildModPerfRow(mod, totalSize, totalXml, totalDll, totalTex, totalPatches));
        }

        if (!filtered.Any())
        {
            modList.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(filter) ? "No mod data available" : $"No mods matching \"{filter}\"",
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        var scrollViewer = new ScrollViewer
        {
            Style = (Style)FindResource("DarkScrollViewerStyle"),
            MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = modList
        };
        stack.Children.Add(scrollViewer);

        card.Child = stack;
        return card;
    }

    private UIElement BuildModPerfRow(RimWorldSaveReader.ModPerformanceData mod,
        long grandTotal, long xmlTotal, long dllTotal, long texTotal, int patchesTotal)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x3A, 0x5A, 0x80)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 6, 4, 6),
            Opacity = mod.IsActive ? 1.0 : 0.5
        };

        // Name column with source badge
        var namePanel = new StackPanel();
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = mod.Name,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 250,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (ModSourceStyles.TryGetValue(mod.Source, out var sourceStyle))
        {
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, sourceStyle.Color.R, sourceStyle.Color.G, sourceStyle.Color.B)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = sourceStyle.Label,
                    FontSize = 7,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(sourceStyle.Color)
                }
            });
        }

        namePanel.Children.Add(nameRow);

        // Percentage bar showing relative size
        if (grandTotal > 0 && mod.TotalFolderBytes > 0)
        {
            double pct = (double)mod.TotalFolderBytes / grandTotal;
            var barBg = new Border
            {
                Height = 3,
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0x3A, 0x7B, 0xD5)),
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 3, 40, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var barColor = pct > 0.1 ? Color.FromRgb(0xE7, 0x4C, 0x3C)
                : pct > 0.05 ? Color.FromRgb(0xF3, 0x9C, 0x12)
                : Color.FromRgb(0x3A, 0x7B, 0xD5);
            var barFill = new Border
            {
                Height = 3,
                Background = new SolidColorBrush(barColor),
                CornerRadius = new CornerRadius(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(2, pct * 200)
            };
            var barGrid = new Grid { Margin = new Thickness(0, 3, 40, 0) };
            barGrid.Children.Add(barBg);
            barGrid.Children.Add(barFill);
            namePanel.Children.Add(barGrid);
        }

        Grid.SetColumn(namePanel, 0);
        row.Children.Add(namePanel);

        // XML column
        var xmlText = mod.XmlFileCount > 0
            ? $"{mod.XmlFileCount} ({FormatFileSize(mod.XmlTotalBytes)})"
            : "—";
        var xmlTb = new TextBlock
        {
            Text = xmlText,
            FontSize = 10,
            Foreground = IsOutlierShare(mod.XmlTotalBytes, xmlTotal)
                ? new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12))
                : (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(xmlTb, 1);
        row.Children.Add(xmlTb);

        // DLL column
        var dllText = mod.DllFileCount > 0
            ? $"{mod.DllFileCount} ({FormatFileSize(mod.DllTotalBytes)})"
            : "—";
        var dllTb = new TextBlock
        {
            Text = dllText,
            FontSize = 10,
            Foreground = IsOutlierShare(mod.DllTotalBytes, dllTotal)
                ? new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))
                : (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dllTb, 2);
        row.Children.Add(dllTb);

        // Textures column
        var texText = mod.TextureFileCount > 0
            ? $"{mod.TextureFileCount} ({FormatFileSize(mod.TextureTotalBytes)})"
            : "—";
        var texTb = new TextBlock
        {
            Text = texText,
            FontSize = 10,
            Foreground = IsOutlierShare(mod.TextureTotalBytes, texTotal)
                ? new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6))
                : (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(texTb, 3);
        row.Children.Add(texTb);

        // Patches column
        var patchText = mod.PatchesFileCount > 0 ? mod.PatchesFileCount.ToString() : "—";
        var patchTb = new TextBlock
        {
            Text = patchText,
            FontSize = 10,
            Foreground = IsOutlierShare(mod.PatchesFileCount, patchesTotal)
                ? new SolidColorBrush(Color.FromRgb(0x1A, 0xBC, 0x9C))
                : (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(patchTb, 4);
        row.Children.Add(patchTb);

        // Total column
        var totalText = mod.TotalFolderBytes > 0 ? FormatFileSize(mod.TotalFolderBytes) : "—";
        var totalTb = new TextBlock
        {
            Text = totalText,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = IsOutlierShare(mod.TotalFolderBytes, grandTotal)
                ? new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))
                : (Brush)FindResource("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(totalTb, 5);
        row.Children.Add(totalTb);

        card.Child = row;

        card.ToolTip = $"{mod.Name}\n" +
            $"XML: {mod.XmlFileCount} files ({FormatFileSize(mod.XmlTotalBytes)}) — Defs: {mod.DefsFileCount}, Patches: {mod.PatchesFileCount}\n" +
            $"DLLs: {mod.DllFileCount} ({FormatFileSize(mod.DllTotalBytes)})\n" +
            $"Textures: {mod.TextureFileCount} ({FormatFileSize(mod.TextureTotalBytes)})\n" +
            $"Total: {FormatFileSize(mod.TotalFolderBytes)}";

        return card;
    }

    // ── Game Logs ───────────────────────────────────────────────

    private UIElement BuildGameLogsSection(
        List<RimWorldSaveReader.GameLogEntry>? prefetchedLogs = null)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var stack = new StackPanel();

        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

        var liveBtn = new Button
        {
            Content = _analyticsLiveLogs ? "🔴  Live: ON" : "⚪  Live: OFF",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "When enabled, the log list refreshes automatically as RimWorld writes new entries to Player.log"
        };
        liveBtn.Click += (_, _) =>
        {
            _analyticsLiveLogs = !_analyticsLiveLogs;
            if (_analyticsLiveLogs) StartLiveLogWatcher(); else StopLiveLogWatcher();
            RefreshAnalyticsView();
        };
        DockPanel.SetDock(liveBtn, Dock.Right);
        headerRow.Children.Add(liveBtn);

        var openLogBtn = new Button
        {
            Content = "📄  Open Log File",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        openLogBtn.Click += (_, _) =>
        {
            var logPath = System.IO.Path.Combine(RimWorldSaveReader.UserDataPath, "Player.log");
            try
            {
                if (System.IO.File.Exists(logPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true
                    });
                else
                    ShowToast("Not Found", "Player.log not found", ToastService.ToastType.Warning);
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        };
        DockPanel.SetDock(openLogBtn, Dock.Right);
        headerRow.Children.Add(openLogBtn);

        var openFolderBtn = new Button
        {
            Content = "📁  Open Folder",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        openFolderBtn.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = RimWorldSaveReader.UserDataPath, UseShellExecute = true }); }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        };
        DockPanel.SetDock(openFolderBtn, Dock.Right);
        headerRow.Children.Add(openFolderBtn);

        headerRow.Children.Add(new TextBlock
        {
            Text = "GAME LOGS",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        stack.Children.Add(headerRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Parsed from Player.log — shows errors, exceptions, and warnings from the last game session.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var logEntries = prefetchedLogs ?? RimWorldSaveReader.GetGameLogEntries();

        // Log file metadata
        var logSize = RimWorldSaveReader.GetGameLogFileSize();
        var logModified = RimWorldSaveReader.GetGameLogLastModified();

        int errorCount = logEntries.Count(e => e.Level == RimWorldSaveReader.LogLevel.Error);
        int exceptionCount = logEntries.Count(e => e.Level == RimWorldSaveReader.LogLevel.Exception);
        int warningCount = logEntries.Count(e => e.Level == RimWorldSaveReader.LogLevel.Warning);
        int infoCount = logEntries.Count(e => e.Level == RimWorldSaveReader.LogLevel.Info);

        // Stats row
        var logStatsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

        void AddLogStat(string label, int count, Color color, string filterValue)
        {
            var stat = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                ToolTip = $"Click to filter by {label.ToLowerInvariant()}"
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString(),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            stat.Child = sp;

            // Highlight active filter
            if (_analyticsLogFilter == filterValue)
                stat.BorderBrush = new SolidColorBrush(color);
            else
                stat.BorderBrush = Brushes.Transparent;
            stat.BorderThickness = new Thickness(1);

            stat.MouseLeftButtonDown += (_, _) =>
            {
                _analyticsLogFilter = _analyticsLogFilter == filterValue ? "All" : filterValue;
                RefreshAnalyticsView();
            };

            logStatsRow.Children.Add(stat);
        }

        AddLogStat("Exceptions", exceptionCount, Color.FromRgb(0xE7, 0x4C, 0x3C), "Exception");
        AddLogStat("Errors", errorCount, Color.FromRgb(0xFF, 0x70, 0x43), "Error");
        AddLogStat("Warnings", warningCount, Color.FromRgb(0xF3, 0x9C, 0x12), "Warning");
        AddLogStat("Info", infoCount, Color.FromRgb(0x3A, 0x7B, 0xD5), "Info");

        // Log file info
        if (logModified.HasValue)
        {
            var fileInfoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
            fileInfoRow.Children.Add(new TextBlock
            {
                Text = $"Log size: {FormatFileSize(logSize)}",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 10, 0)
            });
            fileInfoRow.Children.Add(MakeDot());
            fileInfoRow.Children.Add(new TextBlock
            {
                Text = $"Last modified: {logModified.Value:MMM d, yyyy  h:mm tt}",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });
            logStatsRow.Children.Add(fileInfoRow);
        }

        stack.Children.Add(logStatsRow);

        // Search bar
        var logSearchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBoxStyle"),
            Text = _analyticsLogSearch,
            FontSize = 12
        };
        var logPlaceholder = new TextBlock
        {
            Text = "🔍  Search log messages...",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false,
            Margin = new Thickness(10, 7, 0, 0),
            Visibility = string.IsNullOrEmpty(_analyticsLogSearch) ? Visibility.Visible : Visibility.Collapsed
        };
        var logSearchGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        logSearchGrid.Children.Add(logSearchBox);
        logSearchGrid.Children.Add(logPlaceholder);

        logSearchBox.TextChanged += (_, _) =>
        {
            logPlaceholder.Visibility = string.IsNullOrEmpty(logSearchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            if (logSearchBox.Text != _analyticsLogSearch)
            {
                _analyticsLogSearch = logSearchBox.Text;
                RefreshAnalyticsView();
            }
        };
        if (!string.IsNullOrEmpty(_analyticsLogSearch))
        {
            logSearchBox.Loaded += (_, _) =>
            {
                logSearchBox.Focus();
                logSearchBox.CaretIndex = logSearchBox.Text.Length;
            };
        }

        stack.Children.Add(logSearchGrid);

        // Filter entries
        var filteredLogs = logEntries.AsEnumerable();

        if (_analyticsLogFilter != "All")
        {
            var level = _analyticsLogFilter switch
            {
                "Exception" => RimWorldSaveReader.LogLevel.Exception,
                "Error" => RimWorldSaveReader.LogLevel.Error,
                "Warning" => RimWorldSaveReader.LogLevel.Warning,
                "Info" => RimWorldSaveReader.LogLevel.Info,
                _ => (RimWorldSaveReader.LogLevel?)null
            };
            if (level.HasValue)
                filteredLogs = filteredLogs.Where(e => e.Level == level.Value);
        }

        var searchFilter = _analyticsLogSearch.Trim();
        if (!string.IsNullOrEmpty(searchFilter))
        {
            filteredLogs = filteredLogs.Where(e =>
                e.Message.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                e.StackTrace.Contains(searchFilter, StringComparison.OrdinalIgnoreCase));
        }

        var logList = filteredLogs.Take(201).ToList();
        bool hasMoreLogs = logList.Count > 200;
        if (hasMoreLogs)
            logList.RemoveAt(200);

        if (logEntries.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No Player.log found — launch RimWorld to generate logs.",
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        else if (logList.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"No log entries matching current filters.",
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        else
        {
            var logPanel = new StackPanel();

            foreach (var entry in logList)
            {
                logPanel.Children.Add(BuildLogEntryCard(entry));
            }

            if (hasMoreLogs)
            {
                logPanel.Children.Add(new TextBlock
                {
                    Text = "Showing first 200 entries — use search or filters to narrow results.",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            var logScroll = new ScrollViewer
            {
                Style = (Style)FindResource("DarkScrollViewerStyle"),
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = logPanel
            };
            stack.Children.Add(logScroll);
        }

        card.Child = stack;
        return card;
    }

    private void StartLiveLogWatcher()
    {
        StopLiveLogWatcher();
        _liveLogLastModified = RimWorldSaveReader.GetGameLogLastModified();
        _liveLogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _liveLogTimer.Tick += (_, _) =>
        {
            // Only refresh while the Analytics page is visible
            if (_activeTab != "Dashboard" || _activeSidebarItem != "Analytics")
                return;

            var modified = RimWorldSaveReader.GetGameLogLastModified();
            if (modified != _liveLogLastModified)
            {
                _liveLogLastModified = modified;
                _analyticsLogEntries = RimWorldSaveReader.GetGameLogEntries();
                RefreshAnalyticsView();
            }
        };
        _liveLogTimer.Start();
    }

    private void StopLiveLogWatcher()
    {
        _liveLogTimer?.Stop();
        _liveLogTimer = null;
    }

    private Border BuildLogEntryCard(RimWorldSaveReader.GameLogEntry entry)
    {
        var (accentColor, icon, levelLabel) = entry.Level switch
        {
            RimWorldSaveReader.LogLevel.Exception => (Color.FromRgb(0xE7, 0x4C, 0x3C), "💥", "EXCEPTION"),
            RimWorldSaveReader.LogLevel.Error => (Color.FromRgb(0xFF, 0x70, 0x43), "❌", "ERROR"),
            RimWorldSaveReader.LogLevel.Warning => (Color.FromRgb(0xF3, 0x9C, 0x12), "⚠️", "WARNING"),
            _ => (Color.FromRgb(0x3A, 0x7B, 0xD5), "ℹ️", "INFO")
        };

        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 2)
        };

        var wrapper = new DockPanel();

        // Left accent + icon
        var accent = new Border
        {
            Width = 3,
            Background = new SolidColorBrush(accentColor),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(accent, Dock.Left);
        wrapper.Children.Add(accent);

        // Level badge on the right
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = levelLabel,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor)
            }
        };
        DockPanel.SetDock(badge, Dock.Right);
        wrapper.Children.Add(badge);

        var content = new StackPanel();

        // Message
        content.Children.Add(new TextBlock
        {
            Text = entry.Message,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 40
        });

        // Stack trace (collapsed by default, shown on hover)
        if (!string.IsNullOrEmpty(entry.StackTrace))
        {
            var stackBlock = new TextBlock
            {
                Text = entry.StackTrace,
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x8E, 0x9E)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                MaxHeight = 80,
                Visibility = Visibility.Collapsed
            };

            var toggleText = new TextBlock
            {
                Text = "▸ Show stack trace",
                FontSize = 9,
                Foreground = new SolidColorBrush(accentColor),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 2, 0, 0)
            };

            toggleText.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (stackBlock.Visibility == Visibility.Collapsed)
                {
                    stackBlock.Visibility = Visibility.Visible;
                    toggleText.Text = "▾ Hide stack trace";
                }
                else
                {
                    stackBlock.Visibility = Visibility.Collapsed;
                    toggleText.Text = "▸ Show stack trace";
                }
            };

            content.Children.Add(toggleText);
            content.Children.Add(stackBlock);
        }

        wrapper.Children.Add(content);
        card.Child = wrapper;

        card.ToolTip = entry.Message + (string.IsNullOrEmpty(entry.StackTrace) ? "" : "\n\n" + entry.StackTrace);

        return card;
    }

    private TextBlock MakeDot() => new()
    {
        Text = "·",
        FontSize = 12,
        FontWeight = FontWeights.Bold,
        Foreground = (Brush)FindResource("TextSecondaryBrush"),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    // ── Performance Monitor (Live Graph) ────────────────────────

    private UIElement BuildPerformanceMonitorSection()
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16),
            Tag = PerfMonitorSectionTag
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "PERFORMANCE MONITOR",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Live FPS, memory usage, and GC stats from the running game (updates every ~2s).",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (!_perfMonitorEnabled)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Performance monitor is turned off. Enable it in the Configs tab.",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0)
            });
            card.Child = stack;
            return card;
        }

        if (!IsGameRunning())
        {
            stack.Children.Add(new TextBlock
            {
                Text = "⚠ Game is not running. Start RimWorld to see live performance data.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0)
            });
            card.Child = stack;
            return card;
        }

        // Current metrics row
        var latest = _perfHistory.Count > 0 ? _perfHistory[^1] : null;

        var metricsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        metricsRow.Children.Add(BuildStatBadge("FPS", latest?.Fps.ToString("F0") ?? "—",
            Color.FromRgb(0x2E, 0xCC, 0x71)));
        metricsRow.Children.Add(BuildStatBadge("Memory", latest != null ? $"{latest.MemUsedMB:F0} MB" : "—",
            Color.FromRgb(0x3A, 0x7B, 0xD5)));
        metricsRow.Children.Add(BuildStatBadge("Reserved", latest != null ? $"{latest.MemReservedMB:F0} MB" : "—",
            Color.FromRgb(0x9B, 0x59, 0xB6)));
        metricsRow.Children.Add(BuildStatBadge("GC Gen0", latest?.GC0.ToString() ?? "—",
            Color.FromRgb(0xF3, 0x9C, 0x12)));
        metricsRow.Children.Add(BuildStatBadge("GC Gen1", latest?.GC1.ToString() ?? "—",
            Color.FromRgb(0xE7, 0x4C, 0x3C)));
        metricsRow.Children.Add(BuildStatBadge("GC Gen2", latest?.GC2.ToString() ?? "—",
            Color.FromRgb(0xE9, 0x1E, 0x63)));
        stack.Children.Add(metricsRow);

        // FPS graph
        if (_perfHistory.Count > 1)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "FPS HISTORY",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(BuildLineGraph(_perfHistory.Select(p => (double)p.Fps).ToList(),
                Color.FromRgb(0x2E, 0xCC, 0x71), 120, "fps"));

            stack.Children.Add(new TextBlock
            {
                Text = "MEMORY USAGE (MB)",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 12, 0, 4)
            });
            stack.Children.Add(BuildLineGraph(_perfHistory.Select(p => (double)p.MemUsedMB).ToList(),
                Color.FromRgb(0x3A, 0x7B, 0xD5), 120, "MB"));
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Collecting data… graphs will appear after a few samples.",
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        card.Child = stack;
        return card;
    }

    // ── Live Warning Alerts ─────────────────────────────────────
    // Shows toast alerts when FPS drops below, memory rises above, or GC
    // spikes are detected, using the live perf data written by the Perf mod.

    private UIElement BuildLiveWarningAlertsSection()
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var stack = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Live Warning Alerts",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Live toast alerts during gameplay — performance warnings (low FPS, high memory, GC spikes) and crash/freeze protection events from the in-game CrashGuard. Each alert type can be turned on or off individually.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // Master on/off switch (layout only — visual toggle, defaults off, no backend wiring yet)
        var toggleRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggleLabelStack = new StackPanel();
        toggleLabelStack.Children.Add(new TextBlock
        {
            Text = "Live Warning Alerts",
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        });
        var toggleStatusText = new TextBlock
        {
            Text = _alertsEnabled ? "On" : "Off",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        toggleLabelStack.Children.Add(toggleStatusText);
        Grid.SetColumn(toggleLabelStack, 0);

        var toggleSwitch = BuildToggleSwitch(_alertsEnabled, isOn =>
        {
            _alertsEnabled = isOn;
            toggleStatusText.Text = isOn ? "On" : "Off";
            if (isOn) StartAlertMonitoring(); else StopAlertMonitoring();
            ShowToast("Live Alerts", isOn ? "Live warning alerts enabled." : "Live warning alerts disabled.",
                ToastService.ToastType.Info);
        });
        Grid.SetColumn(toggleSwitch, 1);

        toggleRow.Children.Add(toggleLabelStack);
        toggleRow.Children.Add(toggleSwitch);
        stack.Children.Add(toggleRow);

        // Threshold rows
        TextBox MakeThresholdBox(string text)
        {
            return new TextBox
            {
                Style = (Style)FindResource("DarkTextBoxStyle"),
                Text = text,
                FontSize = 12,
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        Grid MakeThresholdRow(string label, UIElement control, Thickness? margin = null)
        {
            var row = new Grid
            {
                Margin = margin ?? new Thickness(0, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(control, 1);

            row.Children.Add(lbl);
            row.Children.Add(control);
            return row;
        }

        var fpsBox = MakeThresholdBox(_alertFpsThreshold.ToString("0"));
        fpsBox.TextChanged += (_, _) =>
        {
            if (double.TryParse(fpsBox.Text, out var v) && v > 0) _alertFpsThreshold = v;
        };
        var fpsRowPanel = new StackPanel { Orientation = Orientation.Horizontal };
        fpsRowPanel.Children.Add(fpsBox);
        fpsRowPanel.Children.Add(WrapAlertToggle(_alertFpsEnabled, isOn => _alertFpsEnabled = isOn));
        stack.Children.Add(MakeThresholdRow("Alert if FPS below", fpsRowPanel));

        var memBox = MakeThresholdBox(_alertMemThresholdMb.ToString("0"));
        memBox.TextChanged += (_, _) =>
        {
            if (double.TryParse(memBox.Text, out var v) && v > 0) _alertMemThresholdMb = v;
        };
        var memRowPanel = new StackPanel { Orientation = Orientation.Horizontal };
        memRowPanel.Children.Add(memBox);
        memRowPanel.Children.Add(WrapAlertToggle(_alertMemEnabled, isOn => _alertMemEnabled = isOn));
        stack.Children.Add(MakeThresholdRow("Alert if Memory above", memRowPanel, new Thickness(0, 8, 0, 0)));

        // GC spike alerts use the shared toggle switch instead of a
        // styled CheckBox: DarkCheckBoxStyle's IsChecked trigger references
        // theme-replaced brush resources, which corrupts WPF's style system
        // (StyleHelper NRE during layout) and blanked this page.
        var gcStatusText = new TextBlock
        {
            Text = _alertGcSpikes ? "On" : "Off",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var gcTogglePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        gcTogglePanel.Children.Add(BuildToggleSwitch(_alertGcSpikes, isOn =>
        {
            _alertGcSpikes = isOn;
            gcStatusText.Text = isOn ? "On" : "Off";
        }));
        gcTogglePanel.Children.Add(gcStatusText);
        stack.Children.Add(MakeThresholdRow("Alert on GC spikes", gcTogglePanel, new Thickness(0, 8, 0, 0)));

        // ── Crash/freeze protection alert types (from the in-game CrashGuard) ──
        StackPanel MakeOnOffTogglePanel(bool initial, Action<bool> onChanged)
        {
            var statusText = new TextBlock
            {
                Text = initial ? "On" : "Off",
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(BuildToggleSwitch(initial, isOn =>
            {
                onChanged(isOn);
                statusText.Text = isOn ? "On" : "Off";
            }));
            panel.Children.Add(statusText);
            return panel;
        }

        stack.Children.Add(new TextBlock
        {
            Text = "Protection Alerts",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 14, 0, 4)
        });

        stack.Children.Add(MakeThresholdRow("Crash / freeze prevented",
            MakeOnOffTogglePanel(_alertCrashGuardEnabled, isOn => _alertCrashGuardEnabled = isOn),
            new Thickness(0, 4, 0, 0)));

        stack.Children.Add(MakeThresholdRow("Emergency save created",
            MakeOnOffTogglePanel(_alertEmergencySaveEnabled, isOn => _alertEmergencySaveEnabled = isOn),
            new Thickness(0, 8, 0, 0)));

        stack.Children.Add(new TextBlock
        {
            Text = "Performance alerts require the FlexTool Performance mod; protection alerts require the Debug Info mod and the Crash Handler to be enabled. Alerts repeat at most once per minute per category.",
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 12, 0, 0)
        });

        card.Child = stack;
        return card;
    }

    /// <summary>Small inline toggle placed next to a threshold box to enable/disable that alert type.</summary>
    private UIElement WrapAlertToggle(bool initial, Action<bool> onChanged)
    {
        var toggle = BuildToggleSwitch(initial, onChanged);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(10, 0, 0, 0);
        return toggle;
    }

    private void StartAlertMonitoring()
    {
        if (_alertTimer != null) return;
        _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _alertTimer.Tick += (_, _) => CheckPerformanceAlerts();
        _alertTimer.Start();
    }

    private void StopAlertMonitoring()
    {
        _alertTimer?.Stop();
        _alertTimer = null;
        _lastGc2Count = -1;
    }

    private void CheckPerformanceAlerts()
    {
        var snap = RimWorldSaveReader.ReadPerformanceData();
        if (snap is null) return;

        // Ignore stale data (game closed)
        if ((DateTime.Now - snap.Timestamp).TotalSeconds > 10) return;

        var now = DateTime.Now;

        if (_alertFpsEnabled && snap.Fps > 0 && snap.Fps < _alertFpsThreshold && now - _lastFpsAlert > AlertCooldown)
        {
            _lastFpsAlert = now;
            ShowToast("⚠ Low FPS", $"FPS dropped to {snap.Fps:F0} (threshold {_alertFpsThreshold:F0})",
                ToastService.ToastType.Warning);
        }

        if (_alertMemEnabled && snap.MemUsedMB > _alertMemThresholdMb && now - _lastMemAlert > AlertCooldown)
        {
            _lastMemAlert = now;
            ShowToast("⚠ High Memory", $"Memory usage {snap.MemUsedMB:F0} MB (threshold {_alertMemThresholdMb:F0} MB)",
                ToastService.ToastType.Warning);
        }

        if (_alertGcSpikes)
        {
            if (_lastGc2Count >= 0 && snap.GC2 > _lastGc2Count && now - _lastGcAlert > AlertCooldown)
            {
                _lastGcAlert = now;
                ShowToast("⚠ GC Spike", $"Gen-2 garbage collection occurred ({snap.GC2} total) — may cause stutter",
                    ToastService.ToastType.Warning);
            }
            _lastGc2Count = snap.GC2;
        }
    }

    private UIElement BuildLineGraph(List<double> values, Color lineColor, double height, string unit)
    {
        const double graphWidth = 600;
        var canvas = new Canvas
        {
            Width = graphWidth,
            Height = height,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 4)
        };

        // Background
        canvas.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = graphWidth,
            Height = height,
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            RadiusX = 4,
            RadiusY = 4
        });

        if (values.Count < 2) return canvas;

        double maxVal = values.Max();
        double minVal = values.Min();
        double range = maxVal - minVal;
        if (range < 1) range = 1;

        // Grid lines
        for (int i = 0; i <= 4; i++)
        {
            double y = height * i / 4.0;
            var gridLine = new Line
            {
                X1 = 0, Y1 = y, X2 = graphWidth, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 0.5
            };
            canvas.Children.Add(gridLine);
        }

        // Draw polyline
        var points = new PointCollection();
        int maxPoints = Math.Min(values.Count, 60);
        var recent = values.Skip(values.Count - maxPoints).ToList();

        for (int i = 0; i < recent.Count; i++)
        {
            double x = i * graphWidth / (maxPoints - 1);
            double y = height - ((recent[i] - minVal) / range * (height - 10) + 5);
            points.Add(new System.Windows.Point(x, y));
        }

        var polyline = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(lineColor),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(polyline);

        // Fill under curve
        var fillPoints = new PointCollection(points);
        fillPoints.Add(new System.Windows.Point(graphWidth, height));
        fillPoints.Add(new System.Windows.Point(0, height));
        var fillPolygon = new Polygon
        {
            Points = fillPoints,
            Fill = new SolidColorBrush(Color.FromArgb(0x33, lineColor.R, lineColor.G, lineColor.B))
        };
        canvas.Children.Add(fillPolygon);

        // Labels
        var labelPanel = new DockPanel { Width = graphWidth, Margin = new Thickness(0, 2, 0, 0) };
        labelPanel.Children.Add(new TextBlock
        {
            Text = $"{minVal:F0} {unit}",
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        });
        var maxLabel = new TextBlock
        {
            Text = $"{maxVal:F0} {unit}",
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        };
        DockPanel.SetDock(maxLabel, Dock.Right);
        labelPanel.Children.Add(maxLabel);

        var wrapper = new StackPanel();
        wrapper.Children.Add(canvas);
        wrapper.Children.Add(labelPanel);
        return wrapper;
    }

    private void StartPerfPolling()
    {
        _perfPollTimer?.Stop();
        _perfPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _perfPollTimer.Tick += (_, _) =>
        {
            if (_perfMonitorEnabled)
            {
                var snap = RimWorldSaveReader.ReadPerformanceData();
                if (snap != null)
                {
                    _perfHistory.Add(snap);
                    if (_perfHistory.Count > 120)
                        _perfHistory.RemoveRange(0, _perfHistory.Count - 120);
                }
            }

            // Refresh only the performance section in-place while the Analytics
            // page is visible. Rebuilding just this card keeps scroll position
            // and search boxes intact, and also picks up game start/stop.
            if (_activeTab != "Dashboard" || _activeSidebarItem != "Analytics"
                || _analyticsRootPanel is null)
                return;

            try
            {
                for (int idx = 0; idx < _analyticsRootPanel.Children.Count; idx++)
                {
                    if (_analyticsRootPanel.Children[idx] is FrameworkElement fe
                        && fe.Tag is string tag && tag == PerfMonitorSectionTag)
                    {
                        _analyticsRootPanel.Children.RemoveAt(idx);
                        _analyticsRootPanel.Children.Insert(idx, BuildPerformanceMonitorSection());
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Perf section refresh failed: {ex.Message}");
            }
        };
        _perfPollTimer.Start();
    }

    private void StopPerfPolling()
    {
        _perfPollTimer?.Stop();
        _perfPollTimer = null;
        _analyticsRootPanel = null;
    }

    /// <summary>
    /// True when a mod's metric is a significant share (>10%) of the list-wide
    /// total for that metric — highlights genuine outliers instead of fixed
    /// byte thresholds that most normal mods would exceed.
    /// </summary>
    private static bool IsOutlierShare(long value, long total) =>
        total > 0 && value > 0 && (double)value / total > 0.10;
}
