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

public partial class MainWindow : Window
{
    private string _activeTab = "Dashboard";
    private string? _activeSidebarItem;
    private string _pawnEditorTab = "Bio";

    private readonly List<PawnData> _colonyPawns = PawnData.CreateSampleColony();
    private PawnData? _selectedPawn;
    private readonly DispatcherTimer _barRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private string? _selectedSaveName;
    private string? _selectedSavePath;
    private bool _launchWindowedBorderless;
    private string _selectedLaunchVersion = "Current";
    private Process? _gameProcess;
    private string? _launchedSaveName;
    private string? _launchedSavePath;
    private string? _lastKnownInGameSave;
    private readonly HashSet<string> _expandedSaveGroups = [];
    private string _saveSearchFilter = "";
    private string _modSearchFilter = "";
    private string _modSortMode = "Load Order";
    private DateTime _lastModsConfigWrite;
    private ToastService? _toastService;
    private bool _isPageTransitioning;
    private string? _restoredTab;
    private string? _restoredSidebar;

    // Performance monitor history for live graph
    private readonly List<RimWorldSaveReader.PerformanceSnapshot> _perfHistory = [];
    private DispatcherTimer? _perfPollTimer;

    // ── Theme System ────────────────────────────────────────────
    private string _activeTheme = "Blue";

    private static readonly Dictionary<string, Dictionary<string, Color>> Themes = new()
    {
        ["Blue"] = new()
        {
            ["Primary"] = Color.FromRgb(0x3A, 0x7B, 0xD5),
            ["Light"] = Color.FromRgb(0x5A, 0x9A, 0xE6),
            ["Dark"] = Color.FromRgb(0x1E, 0x3A, 0x5F),
        },
        ["Green"] = new()
        {
            ["Primary"] = Color.FromRgb(0x2E, 0xCC, 0x71),
            ["Light"] = Color.FromRgb(0x4E, 0xE6, 0x91),
            ["Dark"] = Color.FromRgb(0x1A, 0x5F, 0x3A),
        },
        ["Red"] = new()
        {
            ["Primary"] = Color.FromRgb(0xE7, 0x4C, 0x3C),
            ["Light"] = Color.FromRgb(0xEC, 0x6C, 0x5C),
            ["Dark"] = Color.FromRgb(0x5F, 0x1E, 0x1A),
        },
        ["Purple"] = new()
        {
            ["Primary"] = Color.FromRgb(0x9B, 0x59, 0xB6),
            ["Light"] = Color.FromRgb(0xBB, 0x79, 0xD6),
            ["Dark"] = Color.FromRgb(0x4A, 0x2A, 0x5F),
        },
        ["Teal"] = new()
        {
            ["Primary"] = Color.FromRgb(0x1A, 0xBC, 0x9C),
            ["Light"] = Color.FromRgb(0x3A, 0xDC, 0xBC),
            ["Dark"] = Color.FromRgb(0x0E, 0x5F, 0x4E),
        },
    };

    // ── Window State Settings ───────────────────────────────────
    private static string SettingsPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlexTool", "settings.json");

    private sealed class AppSettings
    {
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 760;
        public double WindowHeight { get; set; } = 500;
        public bool IsMaximized { get; set; }
        public string Theme { get; set; } = "Blue";
        public string LastActiveTab { get; set; } = "Dashboard";
        public string? LastSidebarItem { get; set; }
        public string ModSortMode { get; set; } = "Load Order";
        public bool LaunchWindowedBorderless { get; set; }
        public bool PerfMonitorEnabled { get; set; }
        public bool CrashHandlerEnabled { get; set; }
        public bool CrashPreventionEnabled { get; set; }
        public bool WatchdogEnabled { get; set; }
    }

    // Accent palette for colony color-coding (16 distinct colors for dark backgrounds)
    private static readonly Color[] ColonyColors =
    [
        Color.FromRgb(0x3A, 0x7B, 0xD5), // Blue
        Color.FromRgb(0x2E, 0xCC, 0x71), // Green
        Color.FromRgb(0xE7, 0x4C, 0x3C), // Red
        Color.FromRgb(0xF3, 0x9C, 0x12), // Orange
        Color.FromRgb(0x9B, 0x59, 0xB6), // Purple
        Color.FromRgb(0x1A, 0xBC, 0x9C), // Teal
        Color.FromRgb(0xE9, 0x1E, 0x63), // Pink
        Color.FromRgb(0x00, 0xBC, 0xD4), // Cyan
        Color.FromRgb(0xFF, 0xC1, 0x07), // Amber
        Color.FromRgb(0x8B, 0xC3, 0x4A), // Lime
        Color.FromRgb(0xAB, 0x47, 0xBC), // Deep Purple
        Color.FromRgb(0xFF, 0x70, 0x43), // Deep Orange
        Color.FromRgb(0x26, 0xA6, 0x9A), // Dark Teal
        Color.FromRgb(0x42, 0xA5, 0xF5), // Light Blue
        Color.FromRgb(0xEC, 0x40, 0x7A), // Rose
        Color.FromRgb(0x78, 0x90, 0x9C), // Blue Grey
    ];

    // Maps each colony name to a unique color index, rebuilt each time the save list renders
    private Dictionary<string, Color> _colonyColorMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the colony→color map from the current save list.
    /// Assigns colors sequentially so no two colonies share a color
    /// (up to 16 colonies; beyond that, wraps around with maximum spacing).
    /// </summary>
    private void BuildColonyColorMap(List<RimWorldSaveReader.SaveFileInfo> saves)
    {
        _colonyColorMap = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (var save in saves)
        {
            if (string.IsNullOrEmpty(save.ColonyName)) continue;
            if (_colonyColorMap.ContainsKey(save.ColonyName)) continue;
            _colonyColorMap[save.ColonyName] = ColonyColors[index % ColonyColors.Length];
            index++;
        }
    }

    /// <summary>
    /// Returns the accent colour assigned to a colony.
    /// Uses the pre-built map for guaranteed uniqueness; falls back to gray.
    /// </summary>
    private Color GetColonyColor(string colonyName)
    {
        if (string.IsNullOrEmpty(colonyName))
            return Color.FromRgb(0x60, 0x60, 0x60);

        return _colonyColorMap.TryGetValue(colonyName, out var color)
            ? color
            : Color.FromRgb(0x60, 0x60, 0x60);
    }

     private static readonly Dictionary<string, List<string>> TabSidebarItems = new()
    {
        ["Dashboard"] = ["Launch", "Mod Manager", "Save Games", "Analytics", "Dev Log"],
        ["Mods"] = ["Mods", "Pawn Extractor", "Mod Folder"],
        ["Configs"] = ["Configs", "Save Settings", "Folders"],
        ["Changelog"] = ["Changelog"],
        ["Support"] = ["Support"],
    };

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowState();
        // Apply theme before the visual tree renders so ControlTemplate triggers
        // pick up the correct brush values on first application. Doing this in
        // Loaded (after styles are already applied) corrupts the style system and
        // causes a NullReferenceException in StyleHelper.GetInstanceValue.
        ApplyTheme(_activeTheme);
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _toastService = new ToastService(ToastContainer, UpdateNotificationBadge);

        // Initialize Mod Manager helpers
        InitializeModManagerHelpers();

        // Push updated in-game mod DLLs bundled with this build to the game's
        // Mods folder so installed mods always run the latest version.
        _ = Task.Run(RimWorldSaveReader.RefreshDeployedMods);

        // Enable drag-and-drop for .rws save imports
        AllowDrop = true;
        Drop += MainWindow_Drop;
        DragOver += MainWindow_DragOver;

        SetActiveTab(_restoredTab ?? "Dashboard");
        if (_restoredSidebar != null && TabSidebarItems.TryGetValue(_activeTab, out var items) && items.Contains(_restoredSidebar))
        {
            _activeSidebarItem = _restoredSidebar;
            // Update sidebar visuals
            foreach (var child in SidebarPanel.Children)
            {
                if (child is Button btn)
                {
                    bool isActive = (string)btn.Tag == _restoredSidebar;
                    btn.Style = isActive
                        ? (Style)FindResource("SidebarButtonActiveStyle")
                        : (Style)FindResource("SidebarButtonStyle");
                }
            }
            UpdateContentArea();
        }

        // Defer expensive polling to after the window renders
        Dispatcher.InvokeAsync(() =>
        {
            PollSaveAndRefresh();

            // Auto-select the most recent save so "Launch to Save" works immediately
            if (_selectedSaveName is null)
            {
                var firstSave = RimWorldSaveReader.GetSaveDetails().FirstOrDefault();
                if (firstSave != null)
                {
                    _selectedSaveName = firstSave.FileName;
                    _selectedSavePath = firstSave.FilePath;
                }
            }

            _barRefreshTimer.Tick += (_, _) => PollSaveAndRefresh();
            _barRefreshTimer.Start();

            // Resume crash protection if it was enabled last session
            if (_crashHandlerEnabled) StartCrashWatcher();

            // Don't use a timer that rebuilds the entire UI - will be fixed with direct updates instead
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    // ── Window State Persistence ────────────────────────────────

    private void RestoreWindowState()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsPath)) return;
            var json = System.IO.File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            if (s is null) return;

            // Restore the last-used window size (clamped to the screen's work
            // area so it can never open larger than the display) and position
            // when it's still visible on-screen.
            double maxW = SystemParameters.WorkArea.Width;
            double maxH = SystemParameters.WorkArea.Height;
            double w = double.IsNaN(s.WindowWidth) || s.WindowWidth <= 0 ? 760 : s.WindowWidth;
            double h = double.IsNaN(s.WindowHeight) || s.WindowHeight <= 0 ? 500 : s.WindowHeight;
            Width = Math.Min(Math.Max(MinWidth, w), maxW);
            Height = Math.Min(Math.Max(MinHeight, h), maxH);

            if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop)
                && s.WindowLeft + Width > SystemParameters.VirtualScreenLeft + 40
                && s.WindowTop >= SystemParameters.VirtualScreenTop
                && s.WindowLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40
                && s.WindowTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowLeft;
                Top = s.WindowTop;
            }

            if (s.IsMaximized) WindowState = WindowState.Maximized;
            _activeTheme = Themes.ContainsKey(s.Theme) ? s.Theme : "Blue";
            // Always start on Dashboard > Launch regardless of the last tab used.
            _restoredTab = "Dashboard";
            _restoredSidebar = "Launch";
            _modSortMode = s.ModSortMode ?? "Load Order";
            _launchWindowedBorderless = s.LaunchWindowedBorderless;
            _perfMonitorEnabled = s.PerfMonitorEnabled;
            _crashHandlerEnabled = s.CrashHandlerEnabled;
            _crashPreventionEnabled = s.CrashPreventionEnabled;
            _watchdogEnabled = s.WatchdogEnabled;
        }
        catch { /* ignore corrupt settings */ }
    }

    private void SaveWindowState()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(SettingsPath)!;
            System.IO.Directory.CreateDirectory(dir);
            var s = new AppSettings
            {
                WindowLeft = WindowState == WindowState.Normal ? Left : RestoreBounds.Left,
                WindowTop = WindowState == WindowState.Normal ? Top : RestoreBounds.Top,
                WindowWidth = WindowState == WindowState.Normal ? Width : RestoreBounds.Width,
                WindowHeight = WindowState == WindowState.Normal ? Height : RestoreBounds.Height,
                IsMaximized = WindowState == WindowState.Maximized,
                Theme = _activeTheme,
                LastActiveTab = _activeTab,
                LastSidebarItem = _activeSidebarItem,
                ModSortMode = _modSortMode,
                LaunchWindowedBorderless = _launchWindowedBorderless,
                PerfMonitorEnabled = _perfMonitorEnabled,
                CrashHandlerEnabled = _crashHandlerEnabled,
                CrashPreventionEnabled = _crashPreventionEnabled,
                WatchdogEnabled = _watchdogEnabled
            };
            System.IO.File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s));
        }
        catch { /* best effort */ }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _barRefreshTimer?.Stop();
        StopLiveLogWatcher();
        SaveWindowState();
    }

    // ── Theme System ────────────────────────────────────────────

    private void ApplyTheme(string themeName)
    {
        if (!Themes.TryGetValue(themeName, out var colors)) return;
        _activeTheme = themeName;

        // Freeze brushes so they're safe for cross-thread use and style triggers
        var primary = new SolidColorBrush(colors["Primary"]); primary.Freeze();
        var light = new SolidColorBrush(colors["Light"]); light.Freeze();
        var dark = new SolidColorBrush(colors["Dark"]); dark.Freeze();

        Resources["BluePrimary"] = colors["Primary"];
        Resources["BlueLight"] = colors["Light"];
        Resources["BlueDark"] = colors["Dark"];
        Resources["BluePrimaryBrush"] = primary;
        Resources["BlueLightBrush"] = light;
        Resources["BlueDarkBrush"] = dark;
    }

    private void SwitchTheme(string themeName)
    {
        if (!Themes.ContainsKey(themeName)) return;
        _activeTheme = themeName;

        // Rebuild the entire visual tree by recreating styles with the new theme.
        // Modifying resources after the visual tree is rendered can corrupt
        // ControlTemplate trigger data (NullReferenceException in StyleHelper).
        var colors = Themes[themeName];
        var primary = new SolidColorBrush(colors["Primary"]); primary.Freeze();
        var light = new SolidColorBrush(colors["Light"]); light.Freeze();
        var dark = new SolidColorBrush(colors["Dark"]); dark.Freeze();

        Resources["BluePrimary"] = colors["Primary"];
        Resources["BlueLight"] = colors["Light"];
        Resources["BlueDark"] = colors["Dark"];
        Resources["BluePrimaryBrush"] = primary;
        Resources["BlueLightBrush"] = light;
        Resources["BlueDarkBrush"] = dark;

        // Force a full rebuild of the current page
        UpdateContentArea();
        ShowToast("Theme Changed", $"Switched to {themeName} theme");
    }

    // ── Drag-and-Drop Save Import ───────────────────────────────

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            e.Effects = files.Any(f => f.EndsWith(".rws", StringComparison.OrdinalIgnoreCase))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var rwsFiles = files.Where(f => f.EndsWith(".rws", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (rwsFiles.Length == 0)
        {
            ShowToast("Invalid File", "Only .rws save files can be imported", ToastService.ToastType.Warning);
            return;
        }

        int imported = 0;
        foreach (var file in rwsFiles)
        {
            if (RimWorldSaveReader.ImportSaveFile(file) != null)
                imported++;
        }

        if (imported > 0)
        {
            ShowToast("Drop Import",
                $"Imported {imported} save file{(imported != 1 ? "s" : "")}",
                ToastService.ToastType.Success);
            if (_activeTab == "Dashboard" && _activeSidebarItem is "Save Games" or "Launch")
                UpdateContentArea();
        }
    }

    // ── Async Page Loading ──────────────────────────────────────

    private UIElement BuildLoadingSpinner(string message = "Loading...")
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 60)
        };

        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 200,
            Height = 3,
            Background = (Brush)FindResource("PanelMidBrush"),
            Foreground = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(progress);

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        return panel;
    }

    private async void LoadPageAsync(string spinnerMessage, Func<UIElement> buildPage)
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(BuildLoadingSpinner(spinnerMessage));

        try
        {
            var page = await Task.Run(buildPage);

            ContentPanel.Children.Clear();
            ContentPanel.Children.Add(page);
            AnimatePageIn();
        }
        catch (Exception ex)
        {
            ContentPanel.Children.Clear();
            ContentPanel.Children.Add(new TextBlock
            {
                Text = $"Error loading page: {ex.Message}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            ShowToast("UI Error", ex.Message, ToastService.ToastType.Error);
        }
    }

    // ── Animated Page Transitions ───────────────────────────────

    private void AnimatePageIn()
    {
        if (_isPageTransitioning) return;
        _isPageTransitioning = true;

        ContentPanel.Opacity = 0;
        ContentPanel.RenderTransform = new TranslateTransform(0, 8);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var slideIn = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeIn.Completed += (_, _) => _isPageTransitioning = false;

        ContentPanel.BeginAnimation(OpacityProperty, fadeIn);
        ((TranslateTransform)ContentPanel.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty, slideIn);
    }

    // ── Reusable UI Helpers ─────────────────────────────────────

    private TextBlock BuildPageTitle(string text) => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 0, 0, 16)
    };

    private Border BuildSectionCard(UIElement content, Thickness? margin = null) => new()
    {
        Background = (Brush)FindResource("PanelMidBrush"),
        BorderBrush = (Brush)FindResource("BorderBlueBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(20),
        Margin = margin ?? new Thickness(0, 0, 0, 16),
        Child = content
    };

    private Border BuildBadge(string text, Color color, string? tooltip = null) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(5, 1, 5, 1),
        Margin = new Thickness(4, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = tooltip,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(color)
        }
    };

    private Grid BuildSearchBar(string currentText, string placeholder, Action<string> onChanged)
    {
        var searchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBoxStyle"),
            Text = currentText,
            FontSize = 12
        };
        var placeholderTb = new TextBlock
        {
            Text = $"\ud83d\udd0d  {placeholder}",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false,
            Margin = new Thickness(10, 7, 0, 0),
            Visibility = string.IsNullOrEmpty(currentText) ? Visibility.Visible : Visibility.Collapsed
        };

        var grid = new Grid();
        grid.Children.Add(searchBox);
        grid.Children.Add(placeholderTb);

        searchBox.TextChanged += (_, _) =>
        {
            placeholderTb.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            onChanged(searchBox.Text);
        };

        if (!string.IsNullOrEmpty(currentText))
        {
            searchBox.Loaded += (_, _) =>
            {
                searchBox.Focus();
                searchBox.CaretIndex = searchBox.Text.Length;
            };
        }

        return grid;
    }

    private (Border button, Popup popup) BuildSortDropdown(
        string currentMode, string[] modes, Action<string> onSelect)
    {
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
                Text = $"Sort: {currentMode} \u25be",
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

        foreach (var mode in modes)
        {
            bool isCurrent = mode == currentMode;
            var selectedBg = new SolidColorBrush(Color.FromArgb(0x33, 0x3A, 0x7B, 0xD5));
            var hoverBg = new SolidColorBrush(Color.FromArgb(0x22, 0x5A, 0x9A, 0xE6));

            var item = new Border
            {
                Padding = new Thickness(14, 7, 20, 7),
                Background = isCurrent ? selectedBg : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = isCurrent ? $"\u2713  {mode}" : $"     {mode}",
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
                onSelect(m);
            };

            dropdownPanel.Children.Add(item);
        }

        sortBtn.MouseLeftButtonUp += (_, _) => dropdown.IsOpen = !dropdown.IsOpen;

        return (sortBtn, dropdown);
    }

    private Border BuildStatBadge(string label, string value, Color color, string? tooltip = null)
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
        return stat;
    }

    private void PollSaveAndRefresh()
    {
        try
        {
            PollSaveAndRefreshCore();
        }
        catch (Exception ex)
        {
            ShowToast("Poll Error", ex.Message, ToastService.ToastType.Error);
        }
    }

    private void PollSaveAndRefreshCore()
    {
        bool needsUiRefresh = false;

        // 1. Poll save-file changes on disk (new saves, modified saves)
        var (pawns, changed) = RimWorldSaveReader.Poll();
        if (changed)
        {
            _colonyPawns.Clear();
            _colonyPawns.AddRange(pawns);
            _selectedPawn = _colonyPawns.FirstOrDefault() ?? _selectedPawn;

            if (_activeTab == "Cheats" && _activeSidebarItem == "Pawn Editor")
                UpdateContentArea();

            // Also refresh cheats pages when save changes (may affect colonist counts etc.)
            if (_activeTab == "Cheats" && _activeSidebarItem is "Quick Cheats" or "Resources" or "Colony" or "World")
                UpdateContentArea();

            needsUiRefresh = true;
        }

        // 2. Poll the in-game mod's status file to detect save changes
        //    regardless of whether the game was launched via FlexTool
        if (IsGameRunning())
        {
            var currentInGame = RimWorldSaveReader.GetCurrentSaveFromGame();
            if (currentInGame != null
                && !string.Equals(currentInGame, _lastKnownInGameSave, StringComparison.OrdinalIgnoreCase))
            {
                _lastKnownInGameSave = currentInGame;
                _launchedSaveName = currentInGame;
                _launchedSavePath = RimWorldSaveReader.FindSaveFilePath(currentInGame);

                // Keep the selected save in sync so "Launch to Save" and
                // the save-list highlight follow the in-game save switch
                _selectedSaveName = currentInGame;
                _selectedSavePath = _launchedSavePath;

                needsUiRefresh = true;
            }
        }
        else if (_lastKnownInGameSave != null)
        {
            // Game closed — clear tracking
            _lastKnownInGameSave = null;
            _launchedSaveName = null;
            _launchedSavePath = null;
            needsUiRefresh = true;
        }

        // 3. Poll ModsConfig.xml so mod list/order changes made in-game
        //    (or externally) show up in the mod manager immediately
        try
        {
            var configWrite = System.IO.File.GetLastWriteTime(RimWorldSaveReader.ModsConfigPath);
            if (_lastModsConfigWrite == default)
            {
                _lastModsConfigWrite = configWrite;
            }
            else if (configWrite != _lastModsConfigWrite)
            {
                _lastModsConfigWrite = configWrite;
                if (_activeTab == "Mods" && (_activeSidebarItem is null or "Mods"))
                    UpdateContentArea();
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }

        // 4. Refresh the launch page when anything changed
        if (needsUiRefresh && _activeTab == "Dashboard" && _activeSidebarItem == "Launch")
            UpdateContentArea();

        // 5. Always refresh the status bar
        RefreshStatusBar();
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            SetActiveTab(tag);
        }
    }

    private void SetActiveTab(string tab)
    {
        _activeTab = tab;
        _activeSidebarItem = null;
        NotificationHistoryPanel.Visibility = Visibility.Collapsed;

        // Update nav button styles
        foreach (var child in NavGrid.Children)
        {
            if (child is Button btn)
            {
                btn.Style = (string)btn.Tag == tab
                    ? (Style)FindResource("NavButtonActiveStyle")
                    : (Style)FindResource("NavButtonStyle");
            }
        }

         // Rebuild sidebar
        var items = TabSidebarItems.GetValueOrDefault(tab, []);
        BuildSidebar(items);

        // Update content area
        ContentLabel.Text = items.Count == 0
            ? $"{tab} — no sidebar items"
            : "";
    }

    private void BuildSidebar(List<string> items, string? activeOverride = null)
    {
        SidebarPanel.Children.Clear();

        string? active = activeOverride ?? (items.Count > 0 ? items[0] : null);

        for (int i = 0; i < items.Count; i++)
        {
            var name = items[i];
            bool isActive = name == active;

            var btn = new Button
            {
                Tag = name,
                Style = isActive
                    ? (Style)FindResource("SidebarButtonActiveStyle")
                    : (Style)FindResource("SidebarButtonStyle"),
                Margin = i == 0 ? new Thickness(0) : new Thickness(0, 2, 0, 0)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(CreateSidebarIcon(name, isActive));
            sp.Children.Add(new TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center
            });

            btn.Content = sp;
            btn.Click += SidebarButton_Click;
            SidebarPanel.Children.Add(btn);
        }

        if (active != null)
        {
            _activeSidebarItem = active;
            ContentLabel.Text = $"{_activeTab} > {_activeSidebarItem}";
        }

        UpdateContentArea();
    }

    private void SidebarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clicked || clicked.Tag is not string name)
            return;

        _activeSidebarItem = name;
        ContentLabel.Text = $"{_activeTab} > {name}";

        // Update sidebar button styles
        foreach (var child in SidebarPanel.Children)
        {
            if (child is Button btn)
            {
                bool isActive = (string)btn.Tag == name;
                btn.Style = isActive
                    ? (Style)FindResource("SidebarButtonActiveStyle")
                    : (Style)FindResource("SidebarButtonStyle");

                // Update icon fill color
                if (btn.Content is StackPanel sp && sp.Children[0] is Viewbox vb
                    && vb.Child is Canvas canvas && canvas.Children[0] is Path path)
                {
                    path.Fill = isActive
                        ? Brushes.White
                        : (Brush)FindResource("TextSecondaryBrush");
                }
            }
        }

        UpdateContentArea();
    }

    private void UpdateContentArea()
    {
        try
        {
            ContentPanel.Children.Clear();
            StopPerfPolling();

            if (_activeTab == "Dashboard" && _activeSidebarItem == "Launch")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildLaunchPage());
                AnimatePageIn();
            }
            else if (_activeTab == "Dashboard" && _activeSidebarItem == "Mod Manager")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildModManagerPage());
                AnimatePageIn();
            }
            else if (_activeTab == "Dashboard" && _activeSidebarItem == "Save Games")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildSaveGamesPage());
                AnimatePageIn();
            }
             else if (_activeTab == "Dashboard" && _activeSidebarItem == "Analytics")
            {
                ContentLabel.Text = "";
                LoadAnalyticsAsync();
                return; // async — don't call RefreshStatusBar yet, LoadAnalyticsAsync will
            }
            else if (_activeTab == "Mods" && _activeSidebarItem == "Mods")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildModsTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Mods" && _activeSidebarItem == "Pawn Extractor")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildPawnExtractorTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Mods" && _activeSidebarItem == "Mod Folder")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildModFolderTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Configs" && _activeSidebarItem == "Configs")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildConfigsTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Configs" && _activeSidebarItem == "Save Settings")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildSaveSettingsTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Configs" && _activeSidebarItem == "Folders")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildFoldersTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Changelog" && _activeSidebarItem == "Changelog")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildChangelogTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Support" && _activeSidebarItem == "Support")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildSupportTabContent());
                AnimatePageIn();
            }
            else if (_activeTab == "Dashboard" && _activeSidebarItem == "Dev Log")
            {
                ContentLabel.Text = "";
                ContentPanel.Children.Add(BuildDevLogTabContent());
                AnimatePageIn();
            }
        }
        catch (Exception ex)
        {
            ContentPanel.Children.Clear();
            ContentPanel.Children.Add(new TextBlock
            {
                Text = $"Error loading page: {ex.Message}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            ShowToast("UI Error", ex.Message, ToastService.ToastType.Error);
        }

        RefreshStatusBar();
    }

    // ── Status Bar ──────────────────────────────────────────────

    private void RefreshStatusBar()
    {
        StatusBarPanel.Children.Clear();

        void AddItem(string text, Brush foreground)
        {
            StatusBarPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
        }

        void AddSeparator()
        {
            StatusBarPanel.Children.Add(new TextBlock
            {
                Text = "·",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0)
            });
        }

        // Game status indicator
        bool running = IsGameRunning();
        var dotColor = running
            ? Color.FromRgb(0x2E, 0xCC, 0x71)
            : Color.FromRgb(0x78, 0x90, 0x9C);
        StatusBarPanel.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        AddItem(running ? "RimWorld Running" : "RimWorld Stopped",
            new SolidColorBrush(dotColor));

        AddSeparator();

        // Active save
        if (!string.IsNullOrEmpty(_selectedSaveName))
            AddItem($"▶ {_selectedSaveName}", (Brush)FindResource("BlueLightBrush"));
        else
            AddItem("No save selected", (Brush)FindResource("TextSecondaryBrush"));

        AddSeparator();

        // Mod count
        try
        {
            var mods = RimWorldSaveReader.GetAllMods();
            int active = mods.Count(m => m.IsActive);
            AddItem($"{active} mods active", (Brush)FindResource("TextSecondaryBrush"));
        }
        catch
        {
            AddItem("Mods: ?", (Brush)FindResource("TextSecondaryBrush"));
        }

        AddSeparator();

        // Save count
        try
        {
            int saveCount = RimWorldSaveReader.GetSaveDetails().Count;
            AddItem($"{saveCount} saves", (Brush)FindResource("TextSecondaryBrush"));
        }
        catch
        {
            AddItem("Saves: ?", (Brush)FindResource("TextSecondaryBrush"));
        }
    }

    private async void LoadAnalyticsAsync()
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(BuildLoadingSpinner("Scanning mod folders and parsing logs…"));

        try
        {
            // Pre-fetch heavy data on background thread
            var perfData = await Task.Run(() => RimWorldSaveReader.GetModPerformanceData());
            var logEntries = await Task.Run(() => RimWorldSaveReader.GetGameLogEntries());

            // Build UI on dispatcher with pre-fetched data
            ContentPanel.Children.Clear();
            ContentPanel.Children.Add(BuildAnalyticsPage(perfData, logEntries));
            AnimatePageIn();
            StartPerfPolling();
        }
        catch (Exception ex)
        {
            ContentPanel.Children.Clear();
            ContentPanel.Children.Add(new TextBlock
            {
                Text = $"Error loading analytics: {ex.Message}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            ShowToast("UI Error", ex.Message, ToastService.ToastType.Error);
        }

        RefreshStatusBar();
    }
}
