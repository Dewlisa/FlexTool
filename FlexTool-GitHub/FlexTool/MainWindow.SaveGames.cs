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
    // ── Save Games Page ─────────────────────────────────────────
    private string _saveGamesSearch = "";

    private UIElement BuildSaveGamesPage()
    {
        var root = new StackPanel();

        var saves = RimWorldSaveReader.GetSaveDetails();
        BuildColonyColorMap(saves);

        // ── Stats row ──────────────────────────────────────────
        var statsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };

        void AddStat(string label, string value, Color color, string? tooltip = null)
        {
            var stat = new Border
            {
                Background = (Brush)FindResource("PanelMidBrush"),
                BorderBrush = (Brush)FindResource("BorderBlueBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 8, 8),
                ToolTip = tooltip
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 20,
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

        var colonies = saves.Select(s => s.ColonyName).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var totalSize = saves.Sum(s => s.FileSizeBytes);
        var backups = RimWorldSaveReader.GetBackups();

        AddStat("Total Saves", saves.Count.ToString(), Color.FromRgb(0x3A, 0x7B, 0xD5), "Total number of save files found");
        AddStat("Colonies", colonies.ToString(), Color.FromRgb(0x2E, 0xCC, 0x71), "Number of unique colonies detected");
        AddStat("Total Size", FormatFileSize(totalSize), Color.FromRgb(0xF3, 0x9C, 0x12), "Combined disk size of all save files");
        AddStat("Backups", backups.Count.ToString(), Color.FromRgb(0x9B, 0x59, 0xB6), "Number of backed-up save files");

        root.Children.Add(statsRow);

        // ── Search bar ─────────────────────────────────────────
        var searchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBoxStyle"),
            Text = _saveGamesSearch,
            FontSize = 12,
            Margin = new Thickness(0),
            ToolTip = "Search saves by filename, colony name, or seed"
        };
        var searchPlaceholder = new TextBlock
        {
            Text = "🔍  Search by filename, colony, or seed...",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            IsHitTestVisible = false,
            Margin = new Thickness(10, 7, 0, 0),
            Visibility = string.IsNullOrEmpty(_saveGamesSearch) ? Visibility.Visible : Visibility.Collapsed
        };
        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(searchPlaceholder);

        searchBox.TextChanged += (_, _) =>
        {
            searchPlaceholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            if (searchBox.Text != _saveGamesSearch)
            {
                _saveGamesSearch = searchBox.Text;
                UpdateContentArea();
            }
        };
        if (!string.IsNullOrEmpty(_saveGamesSearch))
        {
            searchBox.Loaded += (_, _) =>
            {
                searchBox.Focus();
                searchBox.CaretIndex = searchBox.Text.Length;
            };
        }

        root.Children.Add(searchGrid);

        // ── Filter saves ───────────────────────────────────────
        var filter = _saveGamesSearch.Trim();
        if (!string.IsNullOrEmpty(filter))
        {
            saves = saves.Where(s =>
                s.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.ColonyName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.Seed.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // ── Save list (grouped by colony) ──────────────────────
        var allSavesHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        var openSavesFolderBtn = new Button
        {
            Content = "📁  Open Folder",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        openSavesFolderBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = RimWorldSaveReader.SavesPath, UseShellExecute = true }); }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        };
        DockPanel.SetDock(openSavesFolderBtn, Dock.Right);
        allSavesHeader.Children.Add(openSavesFolderBtn);

        var importBtn = new Button
        {
            Content = "📂  Import Save",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        importBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import RimWorld Save",
                Filter = "RimWorld Save Files (*.rws)|*.rws",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                int imported = 0;
                foreach (var file in dlg.FileNames)
                {
                    if (RimWorldSaveReader.ImportSaveFile(file) != null)
                        imported++;
                }
                if (imported > 0)
                {
                    ShowToast("Import Complete",
                        $"Imported {imported} save file{(imported != 1 ? "s" : "")}.",
                        ToastService.ToastType.Success);
                    UpdateContentArea();
                }
            }
        };
        DockPanel.SetDock(importBtn, Dock.Right);
        allSavesHeader.Children.Add(importBtn);

        allSavesHeader.Children.Add(new TextBlock
        {
            Text = "ALL SAVES",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        root.Children.Add(allSavesHeader);

        if (saves.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(filter) ? "No save files found" : $"No saves matching \"{filter}\"",
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });
        }
        else
        {
            var saveList = new StackPanel();
            var groups = saves
                .Where(s => !string.IsNullOrEmpty(s.ColonyName))
                .GroupBy(s => s.ColonyName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastModified).ToList(), StringComparer.OrdinalIgnoreCase);

            var ungrouped = saves.Where(s => string.IsNullOrEmpty(s.ColonyName)).ToList();
            var rendered = new HashSet<string>();
            var inactiveThreshold = DateTime.Now.AddDays(-30);

            foreach (var save in saves)
            {
                if (rendered.Contains(save.FilePath)) continue;

                if (string.IsNullOrEmpty(save.ColonyName))
                {
                    rendered.Add(save.FilePath);
                    saveList.Children.Add(BuildSaveGamesCard(save, save.LastModified < inactiveThreshold));
                    continue;
                }

                if (!groups.TryGetValue(save.ColonyName, out var group)) continue;
                foreach (var g in group) rendered.Add(g.FilePath);

                bool inactive = group.Max(s => s.LastModified) < inactiveThreshold;
                var main = group.FirstOrDefault(s =>
                    !s.FileName.StartsWith("Autosave", StringComparison.OrdinalIgnoreCase)) ?? group[0];

                saveList.Children.Add(BuildSaveGamesCard(main, inactive, group.Count));

                // Show other saves in the group
                bool expanded = _expandedSaveGroups.Contains(save.ColonyName);
                if (group.Count > 1)
                {
                    var childPanel = new StackPanel
                    {
                        Margin = new Thickness(18, 0, 0, 0),
                        Visibility = expanded ? Visibility.Visible : Visibility.Collapsed
                    };
                    foreach (var child in group.Where(s => s.FilePath != main.FilePath))
                    {
                        childPanel.Children.Add(BuildSaveGamesCard(child, inactive, isCompact: true));
                    }
                    saveList.Children.Add(childPanel);
                }
            }

            root.Children.Add(saveList);
        }

        // ── Backups section ────────────────────────────────────
        if (backups.Count > 0)
        {
            var backupsHeader = new DockPanel { Margin = new Thickness(0, 20, 0, 8) };

            var openBackupsFolderBtn = new Button
            {
                Content = "📁  Open Folder",
                Style = (Style)FindResource("CardSmallButtonStyle"),
                FontSize = 10,
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            openBackupsFolderBtn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = RimWorldSaveReader.BackupsPath, UseShellExecute = true }); }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
            };
            DockPanel.SetDock(openBackupsFolderBtn, Dock.Right);
            backupsHeader.Children.Add(openBackupsFolderBtn);

            backupsHeader.Children.Add(new TextBlock
            {
                Text = "BACKUPS",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

            root.Children.Add(backupsHeader);

            foreach (var backup in backups.Take(10))
            {
                var bCard = new Border
                {
                    Background = (Brush)FindResource("PanelMidBrush"),
                    BorderBrush = (Brush)FindResource("BorderBlueBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var bWrapper = new DockPanel();

                // Restore button
                var restoreBtn = new Button
                {
                    Content = "Restore",
                    Style = (Style)FindResource("CardSmallButtonStyle"),
                    FontSize = 10,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                var capturedBackup = backup.FilePath;
                restoreBtn.Click += (_, _) =>
                {
                    var result = RimWorldSaveReader.RestoreBackup(capturedBackup);
                    if (result != null)
                    {
                        ShowToast("Backup Restored",
                            $"Restored as \"{System.IO.Path.GetFileName(result)}\" — original save untouched.",
                            ToastService.ToastType.Success);
                        UpdateContentArea();
                    }
                };
                DockPanel.SetDock(restoreBtn, Dock.Right);
                bWrapper.Children.Add(restoreBtn);

                var renameBtn = new Button
                {
                    Content = "Rename",
                    Style = (Style)FindResource("CardSmallButtonStyle"),
                    FontSize = 10,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                var capturedBackupForRename = backup.FilePath;
                var (bBaseName, bLabel) = RimWorldSaveReader.ParseBackupName(backup.FilePath);
                renameBtn.Click += (_, _) =>
                {
                    var newName = FlexToolDialog.ShowInput(this, "Rename Backup",
                        "Enter a new name for this backup:", bBaseName);
                    if (string.IsNullOrWhiteSpace(newName) || newName == bBaseName) return;
                    var renamed = RimWorldSaveReader.RenameBackup(capturedBackupForRename, newName);
                    if (renamed != null)
                    {
                        ShowToast("Backup Renamed", System.IO.Path.GetFileName(renamed), ToastService.ToastType.Success);
                        UpdateContentArea();
                    }
                    else
                    {
                        ShowToast("Rename Failed", "That name is invalid or already in use.", ToastService.ToastType.Error);
                    }
                };
                DockPanel.SetDock(renameBtn, Dock.Right);
                bWrapper.Children.Add(renameBtn);

                var delBackupBtn = new Button
                {
                    Content = "Delete",
                    Style = (Style)FindResource("CardDangerButtonStyle"),
                    FontSize = 10,
                    Padding = new Thickness(8, 3, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                var capturedBackupForDelete = backup.FilePath;
                delBackupBtn.Click += (_, _) =>
                {
                    var confirm = FlexToolDialog.ShowWarning(this, "Delete Backup",
                        $"Permanently delete backup \"{backup.FileName}.rws\"?");
                    if (confirm)
                    {
                        RimWorldSaveReader.DeleteBackup(capturedBackupForDelete);
                        ShowToast("Backup Deleted", $"{backup.FileName}.rws", ToastService.ToastType.Info);
                        UpdateContentArea();
                    }
                };
                DockPanel.SetDock(delBackupBtn, Dock.Right);
                bWrapper.Children.Add(delBackupBtn);

                var bContent = new StackPanel();
                var bNameRow = new WrapPanel { Orientation = Orientation.Horizontal };
                bNameRow.Children.Add(new TextBlock
                {
                    Text = bBaseName + ".rws",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });

                bool isEmergency = string.Equals(bLabel, "Emergency", StringComparison.OrdinalIgnoreCase);
                var labelColor = isEmergency
                    ? Color.FromRgb(0xE0, 0x5A, 0x5A)
                    : string.Equals(bLabel, "AutoBackup", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromRgb(0x3A, 0x7B, 0xD5)
                        : string.Equals(bLabel, "FlexTool", StringComparison.OrdinalIgnoreCase)
                            ? Color.FromRgb(0x8A, 0x5A, 0xD5)
                            : Color.FromRgb(0x4C, 0xAF, 0x50);
                bNameRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, labelColor.R, labelColor.G, labelColor.B)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = isEmergency
                        ? "Created automatically by the crash handler"
                        : $"Backup source: {bLabel}",
                    Child = new TextBlock
                    {
                        Text = isEmergency ? "⚠ EMERGENCY" : bLabel.ToUpperInvariant(),
                        FontSize = 8,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(labelColor)
                    }
                });
                bContent.Children.Add(bNameRow);
                var bMeta = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
                bMeta.Children.Add(new TextBlock
                {
                    Text = FormatFileSize(backup.FileSizeBytes),
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                bMeta.Children.Add(MakeDot());
                bMeta.Children.Add(new TextBlock
                {
                    Text = backup.LastModified.ToString("MMM d, yyyy  h:mm tt"),
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextSecondaryBrush")
                });
                bContent.Children.Add(bMeta);
                bWrapper.Children.Add(bContent);
                bCard.Child = bWrapper;
                root.Children.Add(bCard);
            }
        }

        return root;
    }

    private Border BuildSaveGamesCard(RimWorldSaveReader.SaveFileInfo save, bool inactive, int groupCount = 0, bool isCompact = false)
    {
        var accentColor = GetColonyColor(save.ColonyName);

        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(isCompact ? 10 : 14, isCompact ? 6 : 10, isCompact ? 10 : 14, isCompact ? 6 : 10),
            Margin = new Thickness(0, 0, 0, isCompact ? 3 : 6),
            Opacity = inactive ? 0.5 : 1.0
        };

        var wrapper = new DockPanel();

        // Accent strip
        var accent = new Border
        {
            Width = 4,
            Background = new SolidColorBrush(accentColor),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(accent, Dock.Left);
        wrapper.Children.Add(accent);

        // Right side: Backup button
        var rightPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var backupBtn = new Button
        {
            Content = "Backup",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(8, 3, 8, 3),
            ToolTip = "Create a backup copy of this save file"
        };
        var capturedPath = save.FilePath;
        backupBtn.Click += (_, _) =>
        {
            var result = RimWorldSaveReader.BackupSaveFile(capturedPath, "User");
            if (result != null)
            {
                ShowToast("Backup Created",
                    System.IO.Path.GetFileName(result),
                    ToastService.ToastType.Success);
                UpdateContentArea();
            }
        };
        rightPanel.Children.Add(backupBtn);

        var deleteBtn = new Button
        {
            Content = "Delete",
            Style = (Style)FindResource("CardDangerButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "Permanently delete this save file"
        };
        deleteBtn.Click += (_, _) =>
        {
            var confirm = FlexToolDialog.ShowWarning(this, "Delete Save",
                $"Permanently delete \"{save.FileName}.rws\"?\nThis cannot be undone.");
            if (confirm)
            {
                RimWorldSaveReader.DeleteSaveFile(capturedPath);
                ShowToast("Save Deleted", $"{save.FileName}.rws", ToastService.ToastType.Info);
                UpdateContentArea();
            }
        };
        rightPanel.Children.Add(deleteBtn);

        DockPanel.SetDock(rightPanel, Dock.Right);
        wrapper.Children.Add(rightPanel);

        // Content
        var content = new StackPanel();

        // Row 1: Name + badges
        var nameRow = new WrapPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = save.FileName + ".rws",
            FontSize = isCompact ? 12 : 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        if (inactive)
        {
            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x90, 0x90, 0x90)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "INACTIVE",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90))
                }
            });
        }

        // Group dropdown chevron
        if (groupCount > 1 && !string.IsNullOrEmpty(save.ColonyName))
        {
            bool expanded = _expandedSaveGroups.Contains(save.ColonyName);
            var chevron = new TextBlock
            {
                Text = expanded ? "▾" : "▸",
                FontSize = 13,
                Foreground = new SolidColorBrush(accentColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            var countBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = $"+{groupCount - 1}",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(accentColor)
                }
            };

            var capturedColony = save.ColonyName;
            chevron.MouseLeftButtonDown += (_, e) => { e.Handled = true; ToggleSaveGroup(capturedColony); };
            countBadge.MouseLeftButtonDown += (_, e) => { e.Handled = true; ToggleSaveGroup(capturedColony); };
            nameRow.Children.Add(chevron);
            nameRow.Children.Add(countBadge);
        }

        content.Children.Add(nameRow);

        if (!isCompact)
        {
            // Row 2: Colony · Day · Version
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
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });

            content.Children.Add(detailRow);

            // Row 3: Colonists · Mods · Seed · Size · Date
            var metaRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            if (save.ColonistCount > 0)
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = $"{save.ColonistCount} colonist{(save.ColonistCount != 1 ? "s" : "")}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                metaRow.Children.Add(MakeDot());
            }

            if (save.ModCount > 0)
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = $"{save.ModCount} mod{(save.ModCount != 1 ? "s" : "")}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x9A, 0xE6)),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                metaRow.Children.Add(MakeDot());
            }

            if (!string.IsNullOrEmpty(save.Seed))
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = $"Seed: {save.Seed}",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                metaRow.Children.Add(MakeDot());
            }

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

            content.Children.Add(metaRow);
        }
        else
        {
            // Compact: just date + size
            var metaRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            metaRow.Children.Add(new TextBlock
            {
                Text = $"Day {save.DaysSurvived}",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 6, 0)
            });
            metaRow.Children.Add(MakeDot());
            metaRow.Children.Add(new TextBlock
            {
                Text = FormatFileSize(save.FileSizeBytes),
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 6, 0)
            });
            metaRow.Children.Add(MakeDot());
            metaRow.Children.Add(new TextBlock
            {
                Text = save.LastModified.ToString("MMM d, h:mm tt"),
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });
            content.Children.Add(metaRow);
        }

        wrapper.Children.Add(content);
        card.Child = wrapper;
        return card;
    }

    private void ToggleSaveGroup(string colonyName)
    {
        if (_expandedSaveGroups.Contains(colonyName))
            _expandedSaveGroups.Remove(colonyName);
        else
            _expandedSaveGroups.Add(colonyName);
        UpdateContentArea();
    }

}
