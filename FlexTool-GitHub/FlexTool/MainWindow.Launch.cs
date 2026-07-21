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
    // ── Launch Page ─────────────────────────────────────────────

    private UIElement BuildLaunchPage()
    {
        var root = new StackPanel();

        // 1. Merged Launch & Control card (on top)
        root.Children.Add(BuildLaunchAndControlCard());

        // 2. Version + DLC cards in an evenly-split row
        var infoGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var versionCard = BuildGameVersionCard();
        Grid.SetColumn(versionCard, 0);
        var dlcCard = BuildDlcCard();
        Grid.SetColumn(dlcCard, 2);

        infoGrid.Children.Add(versionCard);
        infoGrid.Children.Add(dlcCard);
        root.Children.Add(infoGrid);

        // 3. Detailed save list
        root.Children.Add(BuildSaveDetailsSection());

        return root;
    }

    // ── Launch & Control (merged) ───────────────────────────────

    private Border BuildLaunchAndControlCard()
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

        // Status indicator
        bool isRunning = IsGameRunning();
        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14),
            ToolTip = isRunning
                ? "A RimWorld process is currently active"
                : "No RimWorld process detected"
        };
        statusRow.Children.Add(new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = isRunning
                ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
                : new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        statusRow.Children.Add(new TextBlock
        {
            Text = isRunning ? "RimWorld is running" : "RimWorld is not running",
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(statusRow);

        // Currently playing indicator — works regardless of how the game was launched
        if (isRunning && _launchedSaveName != null)
        {
            var playingRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            playingRow.Children.Add(new TextBlock
            {
                Text = "▶",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            playingRow.Children.Add(new TextBlock
            {
                Text = $"Currently playing \"{_launchedSaveName}\"",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(playingRow);
        }

        // Separator
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BorderBlueBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        });

        // Selected save indicator
        var selectedText = _selectedSaveName ?? "None — will launch to main menu";
        stack.Children.Add(BuildFieldRow("Selected Save", new TextBlock
        {
            Text = selectedText,
            FontSize = 13,
            Foreground = _selectedSaveName != null
                ? (Brush)FindResource("TextPrimaryBrush")
                : (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            FontStyle = _selectedSaveName != null ? FontStyles.Normal : FontStyles.Italic,
            ToolTip = _selectedSaveName != null
                ? $"\"{_selectedSaveName}\" is selected — pick a different save from the list below"
                : "Click a save file below to select it for launch"
        }));

        // Active save indicator (visible while game is running)
        if (isRunning && _launchedSaveName != null)
        {
            stack.Children.Add(BuildFieldRow("Active Save", new TextBlock
            {
                Text = _launchedSaveName,
                FontSize = 13,
                Foreground = (Brush)FindResource("BlueLightBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"The game is currently playing \"{_launchedSaveName}\""
            }));
        }

        // Display mode selector
        string[] modes = ["Default", "Windowed Borderless"];
        var modeCb = MakeComboBox(modes, _launchWindowedBorderless ? 1 : 0);
        modeCb.ToolTip = "Default uses your in-game display settings. Windowed Borderless launches in a borderless window.";
        modeCb.SelectionChanged += (_, _) =>
        {
            try
            {
                if (modeCb?.SelectedIndex >= 0)
                    _launchWindowedBorderless = modeCb.SelectedIndex == 1;
            }
            catch { /* Ignore selection change errors during initialization */ }
        };
        stack.Children.Add(BuildFieldRow("Display Mode", modeCb));

        // Game version selector
        string[] versions = ["Current", "1.5 (legacy)", "1.4 (legacy)", "1.3 (legacy)", "1.2 (legacy)", "1.1 (legacy)", "1.0 (legacy)"];
        int versionIdx = Array.IndexOf(versions, _selectedLaunchVersion);
        var versionCb = MakeComboBox(versions, versionIdx >= 0 ? versionIdx : 0);
        versionCb.ToolTip = "Pick which game version to launch. Legacy versions require the matching Steam beta branch to be installed. " +
                            "Older versions can only be launched to the main menu — saves from newer versions are not compatible.";
        versionCb.SelectionChanged += (_, _) =>
        {
            try
            {
                if (versionCb?.SelectedIndex >= 0)
                {
                    var newVersion = versions[versionCb.SelectedIndex];
                    // Only rebuild when the value actually changed — the initial
                    // SelectedIndex assignment also raises SelectionChanged, and
                    // rebuilding from it would loop forever.
                    if (newVersion != _selectedLaunchVersion)
                    {
                        _selectedLaunchVersion = newVersion;
                        if (_activeTab == "Dashboard" && _activeSidebarItem == "Launch")
                            UpdateContentArea();
                    }
                }
            }
            catch { /* Ignore selection change errors during initialization */ }
        };
        stack.Children.Add(BuildFieldRow("Game Version", versionCb));

        bool legacyVersion = _selectedLaunchVersion != "Current";
        if (legacyVersion)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"⚠  Older version selected ({_selectedLaunchVersion}) — the game will only launch to the main menu. " +
                       "Make sure the matching beta branch is selected in Steam (RimWorld → Properties → Betas).",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        stack.Children.Add(new Border { Height = 14 });

        // All action buttons in one row
        var btnRow = new WrapPanel();

        var launchSaveBtn = new Button
        {
            Content = "▶  Launch to Save",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 10, 16, 10),
            Style = (Style)FindResource("LaunchButtonStyle"),
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = _selectedSaveName != null && !legacyVersion,
            ToolTip = legacyVersion
                ? "Launching to a save is disabled while an older game version is selected — legacy versions can only launch to the main menu"
                : _selectedSaveName != null
                    ? $"Launch RimWorld and load \"{_selectedSaveName}\" from the main menu"
                    : "Select a save file first to enable this option"
        };
        launchSaveBtn.Click += (_, _) => LaunchGame(loadSave: true);
        btnRow.Children.Add(launchSaveBtn);

        var launchMenuBtn = new Button
        {
            Content = "▶  Launch to Menu",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 10, 16, 10),
            Style = (Style)FindResource("LaunchButtonStyle"),
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = "Launch RimWorld to the main menu"
        };
        launchMenuBtn.Click += (_, _) => LaunchGame(loadSave: false);
        btnRow.Children.Add(launchMenuBtn);

        var killBtn = new Button
        {
            Content = "⏹  Kill Game",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 10, 16, 10),
            Style = (Style)FindResource("CardDangerButtonStyle"),
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = isRunning,
            ToolTip = "Force-close the RimWorld process immediately — any unsaved progress will be lost"
        };
        killBtn.Click += (_, _) =>
        {
            if (!FlexToolDialog.ShowWarning(this,
                "Kill Game",
                "Any unsaved progress will be lost.\n\nAre you sure you want to kill the game?"))
                return;
            KillGame();
            RefreshAfterDelay(TimeSpan.FromMilliseconds(500));
        };
        btnRow.Children.Add(killBtn);

        var restartBtn = new Button
        {
            Content = "🔄  Restart",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 10, 16, 10),
            Style = (Style)FindResource("LaunchButtonStyle"),
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Kill the running game and relaunch it with the current display mode settings"
        };
        restartBtn.Click += (_, _) =>
        {
            if (!FlexToolDialog.ShowWarning(this,
                "Restart Game",
                "The game will be killed and relaunched.\nAny unsaved progress will be lost.\n\nContinue?"))
                return;
            RestartGame();
        };
        btnRow.Children.Add(restartBtn);

        stack.Children.Add(btnRow);

        card.Child = stack;
        return card;
    }

    // ── Version & DLC cards ─────────────────────────────────────

    private Border BuildGameVersionCard()
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Stretch,
            ToolTip = "Game version detected from the most recently modified save file"
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "GAME VERSION",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "RimWorld",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        stack.Children.Add(new TextBlock
        {
            Text = RimWorldSaveReader.GameVersion,
            FontSize = 14,
            Foreground = (Brush)FindResource("BlueLightBrush")
        });

        card.Child = stack;
        return card;
    }

    private Border BuildDlcCard()
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Stretch,
            ToolTip = "DLC ownership detected from your game installation"
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "OWNED DLC",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        string[] allDlcs = ["Royalty", "Ideology", "Biotech", "Anomaly"];

        // Prefer what's actually installed on disk; fall back to what the
        // active save has enabled if the install can't be found.
        var installed = RimWorldSaveReader.GetInstalledDlcs();
        var detected = (IReadOnlyList<string>?)installed ?? RimWorldSaveReader.DetectedDlcs;
        bool hasSaveData = installed is not null || RimWorldSaveReader.GameVersion != "Unknown";
        bool fromInstall = installed is not null;

        if (hasSaveData)
        {
            var dlcGrid = new Grid();
            dlcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlcGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < allDlcs.Length; i++)
            {
                var dlc = allDlcs[i];
                bool owned = detected.Contains(dlc);

                dlcGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 4),
                    ToolTip = owned
                        ? (fromInstall ? $"{dlc} DLC is installed" : $"{dlc} DLC is active in your save")
                        : (fromInstall ? $"{dlc} DLC was not found in your game installation" : $"{dlc} DLC was not detected in your save")
                };

                row.Children.Add(new TextBlock
                {
                    Text = owned ? "✓" : "✕",
                    FontSize = 13,
                    Foreground = owned
                        ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
                        : (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                row.Children.Add(new TextBlock
                {
                    Text = dlc,
                    FontSize = 13,
                    Foreground = owned
                        ? (Brush)FindResource("TextPrimaryBrush")
                        : (Brush)FindResource("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });

                Grid.SetRow(row, i / 2);
                Grid.SetColumn(row, i % 2);
                dlcGrid.Children.Add(row);
            }

            stack.Children.Add(dlcGrid);
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No save loaded — DLC will be\ndetected once a save is found",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });
        }

        card.Child = stack;
        return card;
    }

    // ── Save details list ───────────────────────────────────────

    private UIElement BuildSaveDetailsSection()
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "SAVE FILES",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Search bar
        var searchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBoxStyle"),
            Text = _saveSearchFilter,
            Tag = "Search saves...",
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 12
        };

        // Placeholder text overlay
        var placeholder = new TextBlock
        {
            Text = "🔍  Search by filename or colony...",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false,
            Margin = new Thickness(10, 7, 0, 0),
            Visibility = string.IsNullOrEmpty(_saveSearchFilter) ? Visibility.Visible : Visibility.Collapsed
        };

        var searchContainer = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        searchContainer.Children.Add(searchBox);
        searchContainer.Children.Add(placeholder);

        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;

            // Only refresh when the filter actually changes to avoid loops
            if (searchBox.Text != _saveSearchFilter)
            {
                _saveSearchFilter = searchBox.Text;
                UpdateContentArea();
            }
        };

        // Restore focus and caret after UI rebuild
        if (!string.IsNullOrEmpty(_saveSearchFilter))
        {
            searchBox.Loaded += (_, _) =>
            {
                searchBox.Focus();
                searchBox.CaretIndex = searchBox.Text.Length;
            };
        }

        stack.Children.Add(searchContainer);

        var saves = RimWorldSaveReader.GetSaveDetails();
        BuildColonyColorMap(saves);

        // Apply search filter
        var filter = _saveSearchFilter.Trim();
        if (!string.IsNullOrEmpty(filter))
        {
            saves = saves.Where(s =>
                s.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.ColonyName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (saves.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(filter) ? "No save files found" : $"No saves matching \"{filter}\"",
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        else
        {
            var saveList = new StackPanel();

            // Group by colony name; empty-name saves stay ungrouped
            var groups = saves
                .Where(s => !string.IsNullOrEmpty(s.ColonyName))
                .GroupBy(s => s.ColonyName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var ungrouped = saves.Where(s => string.IsNullOrEmpty(s.ColonyName)).ToList();
            var rendered = new HashSet<string>(); // track rendered file paths

            // A colony is "inactive" if its newest save is older than 30 days
            var inactiveThreshold = DateTime.Now.AddDays(-30);
            var inactiveColonies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (colony, group) in groups)
            {
                if (group.Max(s => s.LastModified) < inactiveThreshold)
                    inactiveColonies.Add(colony);
            }

            foreach (var save in saves)
            {
                if (rendered.Contains(save.FilePath)) continue;

                // Ungrouped (no colony name) — render individually
                if (string.IsNullOrEmpty(save.ColonyName))
                {
                    rendered.Add(save.FilePath);
                    bool isSelected = save.FilePath == _selectedSavePath;
                    bool inactive = save.LastModified < inactiveThreshold;
                    saveList.Children.Add(BuildSaveCard(save, isSelected, inactive: inactive));
                    continue;
                }

                // Already rendered as part of a colony group
                if (!groups.TryGetValue(save.ColonyName, out var group)) continue;

                // Mark all in group as rendered
                foreach (var g in group) rendered.Add(g.FilePath);

                bool colonyInactive = inactiveColonies.Contains(save.ColonyName);

                // Pick main save: most recent non-autosave, or most recent overall
                var main = group.FirstOrDefault(s =>
                    !s.FileName.StartsWith("Autosave", StringComparison.OrdinalIgnoreCase)) ?? group[0];
                var related = group.Where(s => s.FilePath != main.FilePath).ToList();

                bool mainSelected = main.FilePath == _selectedSavePath;
                var mainCard = BuildSaveCard(main, mainSelected, relatedCount: related.Count, inactive: colonyInactive);
                saveList.Children.Add(mainCard);

                if (related.Count > 0)
                {
                    bool expanded = _expandedSaveGroups.Contains(save.ColonyName);

                    // Collapsible panel for related saves
                    var childPanel = new StackPanel
                    {
                        Margin = new Thickness(18, 0, 0, 0),
                        Visibility = expanded ? Visibility.Visible : Visibility.Collapsed
                    };

                    foreach (var child in related)
                    {
                        bool childSelected = child.FilePath == _selectedSavePath;
                        childPanel.Children.Add(BuildCompactSaveCard(child, childSelected, inactive: colonyInactive));
                    }

                    saveList.Children.Add(childPanel);
                }
            }

            var scrollViewer = new ScrollViewer
            {
                Style = (Style)FindResource("DarkScrollViewerStyle"),
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = saveList
            };
            stack.Children.Add(scrollViewer);
        }

        card.Child = stack;
        return card;
    }

    private Border BuildSaveCard(RimWorldSaveReader.SaveFileInfo save, bool isSelected, int relatedCount = 0, bool inactive = false)
    {
        var card = new Border
        {
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(0x22, 0x3A, 0x7B, 0xD5))
                : Brushes.Transparent,
            BorderBrush = isSelected
                ? (Brush)FindResource("BluePrimaryBrush")
                : (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            Opacity = inactive && !isSelected ? 0.5 : 1.0,
            ToolTip = isSelected
                ? $"\"{save.FileName}\" is selected for launch"
                : inactive
                    ? $"\"{save.FileName}\" — inactive (not played in 30+ days)"
                    : $"Click to select \"{save.FileName}\" for launch"
        };

        // Colony accent colour
        var accentColor = GetColonyColor(save.ColonyName);

        var stack = new StackPanel();

        // Row 1: Save name + .rws extension + selected badge
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = save.FileName + ".rws",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = isSelected
                ? (Brush)FindResource("BlueLightBrush")
                : (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (isSelected)
        {
            nameRow.Children.Add(new Border
            {
                Background = (Brush)FindResource("BluePrimaryBrush"),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = "SELECTED",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            });
        }

        if (inactive)
        {
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x90, 0x90, 0x90)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = "INACTIVE",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90))
                }
            });
        }

        // Dropdown chevron for saves with related saves
        if (relatedCount > 0 && !string.IsNullOrEmpty(save.ColonyName))
        {
            bool expanded = _expandedSaveGroups.Contains(save.ColonyName);
            var chevron = new TextBlock
            {
                Text = expanded ? "▾" : "▸",
                FontSize = 13,
                Foreground = new SolidColorBrush(accentColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = expanded
                    ? $"Hide {relatedCount} related save{(relatedCount != 1 ? "s" : "")}"
                    : $"Show {relatedCount} related save{(relatedCount != 1 ? "s" : "")}"
            };

            var countBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"+{relatedCount}",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(accentColor)
                }
            };

            var capturedColony = save.ColonyName;
            chevron.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (_expandedSaveGroups.Contains(capturedColony))
                    _expandedSaveGroups.Remove(capturedColony);
                else
                    _expandedSaveGroups.Add(capturedColony);
                UpdateContentArea();
            };
            countBadge.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (_expandedSaveGroups.Contains(capturedColony))
                    _expandedSaveGroups.Remove(capturedColony);
                else
                    _expandedSaveGroups.Add(capturedColony);
                UpdateContentArea();
            };

            nameRow.Children.Add(chevron);
            nameRow.Children.Add(countBadge);
        }

        stack.Children.Add(nameRow);

        // Row 2: Colony · Days · Version
        var detailRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

        if (!string.IsNullOrEmpty(save.ColonyName))
        {
            detailRow.Children.Add(new TextBlock
            {
                Text = save.ColonyName,
                FontSize = 12,
                Foreground = new SolidColorBrush(accentColor),
                Margin = new Thickness(0, 0, 6, 0)
            });
            detailRow.Children.Add(MakeDot());
        }

        detailRow.Children.Add(new TextBlock
        {
            Text = $"Day {save.DaysSurvived}",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 6, 0)
        });
        detailRow.Children.Add(MakeDot());

        detailRow.Children.Add(new TextBlock
        {
            Text = save.GameVersion,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 0)
        });

        stack.Children.Add(detailRow);

        // Row 3: File size · Date
        var metaRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

        metaRow.Children.Add(new TextBlock
        {
            Text = FormatFileSize(save.FileSizeBytes),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 6, 0)
        });
        metaRow.Children.Add(MakeDot());
        metaRow.Children.Add(new TextBlock
        {
            Text = save.LastModified.ToString("MMM d, yyyy  h:mm tt"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        });

        stack.Children.Add(metaRow);

        // Wrap content in a DockPanel with a colored left accent strip
        var wrapper = new DockPanel();
        var accent = new Border
        {
            Width = 4,
            Background = new SolidColorBrush(accentColor),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(accent, Dock.Left);
        wrapper.Children.Add(accent);
        wrapper.Children.Add(stack);

        card.Child = wrapper;

        var capturedName = save.FileName;
        var capturedPath = save.FilePath;
        card.MouseLeftButtonDown += (_, _) =>
        {
            if (_selectedSavePath == capturedPath)
            {
                _selectedSaveName = null;
                _selectedSavePath = null;
            }
            else
            {
                _selectedSaveName = capturedName;
                _selectedSavePath = capturedPath;
            }
            UpdateContentArea();
        };

        return card;
    }

    private Border BuildCompactSaveCard(RimWorldSaveReader.SaveFileInfo save, bool isSelected, bool inactive = false)
    {
        var accentColor = GetColonyColor(save.ColonyName);

        var card = new Border
        {
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(0x18, 0x3A, 0x7B, 0xD5))
                : Brushes.Transparent,
            BorderBrush = isSelected
                ? (Brush)FindResource("BluePrimaryBrush")
                : new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x5A, 0x80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = Cursors.Hand,
            Opacity = inactive && !isSelected ? 0.5 : 1.0,
            ToolTip = isSelected
                ? $"\"{save.FileName}\" is selected for launch"
                : $"Click to select \"{save.FileName}\" for launch"
        };

        var wrapper = new DockPanel();
        var accent = new Border
        {
            Width = 3,
            Background = new SolidColorBrush(Color.FromArgb(0x80, accentColor.R, accentColor.G, accentColor.B)),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(accent, Dock.Left);
        wrapper.Children.Add(accent);

        var stack = new StackPanel();

        // Single row: filename · Day · Date
        var row = new WrapPanel();
        row.Children.Add(new TextBlock
        {
            Text = save.FileName + ".rws",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = isSelected
                ? (Brush)FindResource("BlueLightBrush")
                : (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        if (isSelected)
        {
            row.Children.Add(new Border
            {
                Background = (Brush)FindResource("BluePrimaryBrush"),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "SELECTED",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            });
        }

        row.Children.Add(MakeDot());
        row.Children.Add(new TextBlock
        {
            Text = $"Day {save.DaysSurvived}",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(MakeDot());
        row.Children.Add(new TextBlock
        {
            Text = save.LastModified.ToString("MMM d, h:mm tt"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        stack.Children.Add(row);
        wrapper.Children.Add(stack);
        card.Child = wrapper;

        var capturedName = save.FileName;
        var capturedPath = save.FilePath;
        card.MouseLeftButtonDown += (_, _) =>
        {
            if (_selectedSavePath == capturedPath)
            {
                _selectedSaveName = null;
                _selectedSavePath = null;
            }
            else
            {
                _selectedSaveName = capturedName;
                _selectedSavePath = capturedPath;
            }
            UpdateContentArea();
        };

        return card;
    }

    // ── Game process management ─────────────────────────────────

    private void LaunchGame(bool loadSave = false)
    {
        if (IsGameRunning())
        {
            // Game is already running — hot-load via IPC instead of restarting
            if (loadSave && _selectedSaveName != null)
            {
                // Skip the dialog if the selected save is already the active one
                if (string.Equals(_selectedSaveName, _launchedSaveName, StringComparison.OrdinalIgnoreCase))
                    return;

                if (!FlexToolDialog.ShowWarning(this,
                    "Switch Save",
                    $"RimWorld is already running.\n\nSwitch to \"{_selectedSaveName}\"?\nAny unsaved progress will be lost."))
                    return;

                // Write the IPC file — the in-game mod will pick it up within ~1 second
                RimWorldSaveReader.PrepareAutoLoad(_selectedSaveName);
                _launchedSaveName = _selectedSaveName;
                _launchedSavePath = _selectedSavePath;

                if (_activeTab == "Dashboard" && _activeSidebarItem == "Launch")
                    UpdateContentArea();
                return;
            }

            // Launch to menu — must kill and relaunch
            if (!FlexToolDialog.ShowWarning(this,
                "Game Already Running",
                "RimWorld is already running.\n\nPlease save your game before launching again. Continue anyway?"))
                return;

            KillGame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                LaunchGameInternal(loadSave);
            };
            timer.Start();
            return;
        }

        LaunchGameInternal(loadSave);
    }

    private void LaunchGameInternal(bool loadSave, string? overrideSaveName = null, string? overrideSavePath = null)
    {
        // Legacy game versions can only be launched to the main menu
        if (_selectedLaunchVersion != "Current")
            loadSave = false;

        var saveName = overrideSaveName ?? _selectedSaveName;
        var savePath = overrideSavePath ?? _selectedSavePath;
        _launchedSaveName = loadSave ? saveName : null;
        _launchedSavePath = loadSave ? savePath : null;

        // Deploy the Harmony auto-load mod and write the IPC command file
        if (loadSave && saveName != null)
            RimWorldSaveReader.PrepareAutoLoad(saveName);
        else
            RimWorldSaveReader.ClearAutoLoadCommand();

        var args = _launchWindowedBorderless ? "-popupwindow" : "";

        // Always launch through Steam so SteamAPI_RestartAppIfNecessary
        // doesn't interfere with the game starting properly.
        var steamExe = RimWorldSaveReader.FindSteamExePath();

        if (steamExe != null)
        {
            try
            {
                var steamArgs = string.IsNullOrEmpty(args)
                    ? "-applaunch 294100"
                    : $"-applaunch 294100 {args}";
                _gameProcess = Process.Start(new ProcessStartInfo(steamExe)
                {
                    Arguments = steamArgs,
                    UseShellExecute = true
                });
                if (_gameProcess is null)
                    ShowToast("Launch", "Failed to launch RimWorld via Steam. Check your installation.", ToastService.ToastType.Error);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Steam launch failed: {ex.Message}");
                ShowToast("Launch", $"Failed to launch RimWorld: {ex.Message}", ToastService.ToastType.Error);
            }
        }
        else
        {
            // No steam.exe found — try direct exe, then Steam protocol as last resort
            var exePath = RimWorldSaveReader.FindGameExePath();
            if (exePath != null)
            {
                try
                {
                    _gameProcess = Process.Start(new ProcessStartInfo(exePath)
                    {
                        Arguments = args,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? "",
                        UseShellExecute = true
                    });
                    if (_gameProcess is null)
                        ShowToast("Launch", "Failed to launch RimWorld. Check your installation.", ToastService.ToastType.Error);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Direct exe launch failed: {ex.Message}");
                    ShowToast("Launch", $"Failed to launch RimWorld: {ex.Message}", ToastService.ToastType.Error);
                }
            }
            else
            {
                try
                {
                    var steamUrl = string.IsNullOrEmpty(args)
                        ? "steam://rungameid/294100"
                        : $"steam://run/294100//{args}/";
                    Process.Start(new ProcessStartInfo(steamUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Steam protocol launch failed: {ex.Message}");
                    ShowToast("Launch", "Failed to launch RimWorld through Steam.", ToastService.ToastType.Error);
                }
            }
        }

        RefreshAfterDelay(TimeSpan.FromSeconds(3));
    }

    private static bool IsGameRunning()
    {
        try { return Process.GetProcessesByName("RimWorldWin64").Length > 0; }
        catch { return false; }
    }

    private void KillGame()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("RimWorldWin64"))
            {
                proc.Kill();
                proc.WaitForExit(3000);
            }

            _gameProcess = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KillGame failed: {ex.Message}");
        }
    }

    private void RestartGame()
    {
        var saveToReload = _launchedSaveName;
        var pathToReload = _launchedSavePath;
        KillGame();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            LaunchGameInternal(saveToReload != null, saveToReload, pathToReload);
        };
        timer.Start();
    }

    private void RefreshAfterDelay(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_activeTab == "Dashboard" && _activeSidebarItem == "Launch")
                UpdateContentArea();
        };
        timer.Start();
    }
}
