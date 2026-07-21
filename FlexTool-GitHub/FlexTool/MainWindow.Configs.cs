using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlexTool;

public partial class MainWindow
{
    // ── Configs Tab ──────────────────────────────────────────────────
    // Configuration management interface for profiles and settings.

    private DispatcherTimer? _autoBackupTimer;
    private bool _autoBackupEnabled = false;
    private int _autoBackupIntervalSeconds = 300; // 5 minutes default
    private DateTime? _lastAutoBackupTime;
    private TextBlock? _autoBackupStatusLabel;

    // ── Crash Handler state ───────────────────────────────────
    private DispatcherTimer? _crashWatchTimer;
    private bool _crashHandlerEnabled;
    private bool _gameWasRunning;
    private bool _crashPreventionEnabled;
    private bool _watchdogEnabled;
    private System.Diagnostics.Process? _watchedGameProcess;
    private DateTime? _gameUnresponsiveSince;
    private DateTime _lastCrashHandled = DateTime.MinValue;
    private DateTime _lastPreventionBackup = DateTime.MinValue;
    private long _lastPlayerLogLength;
    private bool _watchdogRestartInProgress;
    private DateTime _lastEmergencySaveSeen = DateTime.MinValue;
    private DateTime _lastCrashGuardEventSeen = DateTime.MinValue;

    // ── Performance Monitor (Analytics) master switch ────────────────
    private bool _perfMonitorEnabled;

    private UIElement BuildConfigsTabContent()
    {
        // The outer ContentPanel ScrollViewer (MainWindow.xaml) handles scrolling.
        // Never nest a ScrollViewer here — it captures the mouse wheel and breaks scrolling.
        var root = new StackPanel { Margin = new Thickness(8) };

        root.Children.Add(BuildConfigsHeaderCard());
        root.Children.Add(BuildPerfMonitorToggleCard());
        root.Children.Add(BuildModpackCard());
        root.Children.Add(BuildLiveWarningAlertsCard());
        root.Children.Add(BuildCrashHandlerCard());
        root.Children.Add(BuildConfigsPlaceholderCard());

        return root;
    }

    /// <summary>
    /// Save Settings sub-tab: everything related to protecting, backing up
    /// and repairing save files, grouped in one place.
    /// </summary>
    private UIElement BuildSaveSettingsTabContent()
    {
        var root = new StackPanel { Margin = new Thickness(8) };

        // Header — themed like the Configs header card
        var headerCard = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var headerStack = new StackPanel();
        var titleBlock = new TextBlock
        {
            Text = "Save Settings",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x7B, 0xD5), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x4B, 0xA5), 1.0));
        titleBlock.Foreground = gradientBrush;
        headerStack.Children.Add(titleBlock);
        headerStack.Children.Add(new TextBlock
        {
            Text = "Everything for protecting your saves — automatic backups, corrupted save recovery and save file validation.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        headerCard.Child = headerStack;
        root.Children.Add(headerCard);

        root.Children.Add(BuildAutoGameSaveBackupCard());
        root.Children.Add(BuildSaveRecoveryCard());
        root.Children.Add(BuildSaveValidationCard());

        return root;
    }

    private Border BuildPerfMonitorToggleCard()
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

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelStack = new StackPanel();
        labelStack.Children.Add(new TextBlock
        {
            Text = "📊 Performance Monitor",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        });
        var statusText = new TextBlock
        {
            Text = _perfMonitorEnabled
                ? "On — live FPS/memory graphs shown in Analytics"
                : "Off — performance monitor hidden in Analytics",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        labelStack.Children.Add(statusText);
        Grid.SetColumn(labelStack, 0);
        row.Children.Add(labelStack);

        var toggle = BuildToggleSwitch(_perfMonitorEnabled, isOn =>
        {
            _perfMonitorEnabled = isOn;
            statusText.Text = isOn
                ? "On — live FPS/memory graphs shown in Analytics"
                : "Off — performance monitor hidden in Analytics";
            SaveWindowState();
            ShowToast("Performance Monitor",
                isOn ? "Performance monitor enabled." : "Performance monitor disabled.",
                ToastService.ToastType.Info);
        });
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);

        stack.Children.Add(row);
        card.Child = stack;
        return card;
    }

    private Border BuildConfigsHeaderCard()
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

        var titleBlock = new TextBlock
        {
            Text = "Configuration Profiles",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x7B, 0xD5), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x4B, 0xA5), 1.0));
        titleBlock.Foreground = gradientBrush;
        stack.Children.Add(titleBlock);

        stack.Children.Add(new TextBlock
        {
            Text = "Save and restore your preferred mod sets, game settings, and custom configurations.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 0)
        });

        card.Child = stack;
        return card;
    }

    private Border BuildSaveRecoveryCard()
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
            Text = "💾 Corrupted Save Recovery",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        });
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Recover a broken save file from an automatic backup when RimWorld cannot load it.",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var btnStack = new StackPanel { Orientation = Orientation.Horizontal };

        var recoverBtn = new Button
        {
            Content = "Attempt Recovery",
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        recoverBtn.Click += (s, e) =>
        {
            AttemptSaveRecovery();
        };
        btnStack.Children.Add(recoverBtn);

        var viewBackupsBtn = new Button
        {
            Content = "View Backups",
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        viewBackupsBtn.Click += (s, e) =>
        {
            var backupPath = RimWorldSaveReader.BackupsPath;
            if (Directory.Exists(backupPath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{backupPath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ShowToast("Error", $"Cannot open backups folder: {ex.Message}", ToastService.ToastType.Error);
                }
            }
            else
            {
                ShowToast("Warning", "Backups folder not found. Check folder settings.", ToastService.ToastType.Warning);
            }
        };
        btnStack.Children.Add(viewBackupsBtn);

        stack.Children.Add(btnStack);
        card.Child = stack;
        return card;
    }

    private void AttemptSaveRecovery()
    {
        var backupPath = RimWorldSaveReader.BackupsPath;
        var savesPath = RimWorldSaveReader.SavesPath;

        if (!Directory.Exists(savesPath))
        {
            ShowToast("Error", "Saves folder not found. Check folder settings.", ToastService.ToastType.Error);
            return;
        }

        try
        {
            var backups = Directory.Exists(backupPath)
                ? Directory.GetFiles(backupPath, "*.rws")
                    .Concat(Directory.GetFiles(backupPath, "*.zip"))
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList()
                : [];

            if (backups.Count == 0)
            {
                ShowToast("Info", "No backups found yet. Enable Auto-Backup below to start creating them.", ToastService.ToastType.Info);
                return;
            }

            // Show recovery dialog
            ShowSaveRecoveryDialog(backups, backupPath, savesPath);
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Failed to access backups: {ex.Message}", ToastService.ToastType.Error);
        }
    }

    private void ShowSaveRecoveryDialog(List<string> backups, string backupPath, string savesPath)
    {
        var dialog = new Window
        {
            Title = "Recover Save File",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var outerBorder = new Border
        {
            Background = (Brush)FindResource("PanelDarkBrush"),
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0x3A, 0x7B, 0xD5),
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.25
            }
        };

        var mainStack = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

        // Draggable title bar
        var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        titleBar.MouseLeftButtonDown += (_, _) => { try { dialog.DragMove(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "💾 Recover Save File",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeBtn.Click += (_, _) => dialog.Close();
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);
        mainStack.Children.Add(titleBar);

        // Separator
        mainStack.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BorderBlueBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        mainStack.Children.Add(new TextBlock
        {
            Text = "Select backup to restore:",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var listBox = new ListBox
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Height = 200,
            Margin = new Thickness(0, 0, 0, 12)
        };

        foreach (var backup in backups)
        {
            var fileInfo = new FileInfo(backup);
            var display = Path.GetFileNameWithoutExtension(backup);
            // Strip trailing _yyyyMMdd_HHmmss timestamp for readability
            var m = System.Text.RegularExpressions.Regex.Match(display, @"^(.*)_\d{8}_\d{6}$");
            if (m.Success) display = m.Groups[1].Value;
            var item = new TextBlock
            {
                Text = $"{display}  —  {fileInfo.LastWriteTime:g}",
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Padding = new Thickness(8),
                ToolTip = backup
            };
            listBox.Items.Add(item);
        }

        if (listBox.Items.Count > 0)
            listBox.SelectedIndex = 0;

        mainStack.Children.Add(listBox);

        mainStack.Children.Add(new TextBlock
        {
            Text = "⚠️ This will replace your current save file. Make sure you have a backup!",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var btnStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 0)
        };

        var recoverBtn = new Button
        {
            Content = "✓ Recover",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        recoverBtn.Click += (s, e) =>
        {
            if (listBox.SelectedIndex < 0)
            {
                ShowToast("Warning", "Please select a backup", ToastService.ToastType.Warning);
                return;
            }

            var selectedBackup = backups[listBox.SelectedIndex];
            if (PerformSaveRecovery(selectedBackup, savesPath))
            {
                dialog.Close();
                ShowToast("Success", "Save file recovered successfully!", ToastService.ToastType.Success);
            }
        };
        btnStack.Children.Add(recoverBtn);

        var cancelBtn = new Button
        {
            Content = "✕ Cancel",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x4C, 0x4C)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        cancelBtn.Click += (s, e) => dialog.Close();
        btnStack.Children.Add(cancelBtn);

        mainStack.Children.Add(btnStack);
        outerBorder.Child = mainStack;
        dialog.Content = outerBorder;
        dialog.ShowDialog();
    }

    private bool PerformSaveRecovery(string backupFile, string savesPath)
    {
        try
        {
            Directory.CreateDirectory(savesPath);

            if (backupFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(backupFile, savesPath, overwriteFiles: true);
            }
            else
            {
                // .rws backup — restore under the original save name (strip _yyyyMMdd_HHmmss)
                var name = Path.GetFileNameWithoutExtension(backupFile);
                var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*)_\d{8}_\d{6}$");
                if (m.Success) name = m.Groups[1].Value;
                File.Copy(backupFile, Path.Combine(savesPath, name + ".rws"), overwrite: true);
            }
            return true;
        }
        catch (Exception ex)
        {
            ShowToast("Error", $"Recovery failed: {ex.Message}", ToastService.ToastType.Error);
            return false;
        }
    }

    private Border BuildSaveValidationCard()
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
            Text = "✓ Save Validation Tool",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        });
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Scan your save files for errors and potential issues before loading them.",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var validateBtn = new Button
        {
            Content = "Validate Saves",
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        validateBtn.Click += (s, e) =>
        {
            var savesPath = RimWorldSaveReader.SavesPath;
            if (!System.IO.Directory.Exists(savesPath))
            {
                ShowToast("Not Found", "Saves folder not found", ToastService.ToastType.Warning);
                return;
            }

            validateBtn.IsEnabled = false;
            validateBtn.Content = "Validating…";

            Task.Run(() =>
            {
                var results = new List<string>();
                int ok = 0, bad = 0;
                foreach (var file in System.IO.Directory.EnumerateFiles(savesPath, "*.rws", System.IO.SearchOption.AllDirectories))
                {
                    try
                    {
                        using var fs = new System.IO.FileStream(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                        using var reader = System.Xml.XmlReader.Create(fs);
                        while (reader.Read()) { } // full parse — throws on malformed XML
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        bad++;
                        results.Add($"✗ {System.IO.Path.GetFileNameWithoutExtension(file)} — {ex.Message}");
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    validateBtn.IsEnabled = true;
                    validateBtn.Content = "Validate Saves";

                    if (bad == 0)
                        ShowToast("Validation Complete", $"All {ok} save file(s) are valid", ToastService.ToastType.Success);
                    else
                        FlexToolDialog.ShowInfo(this, "Save Validation Results",
                            $"{ok} valid, {bad} corrupted:\n\n{string.Join("\n", results.Take(10))}" +
                            (results.Count > 10 ? $"\n…and {results.Count - 10} more" : "") +
                            "\n\nUse Save Recovery above to restore corrupted saves from backups.");
                });
            });
        };

        stack.Children.Add(validateBtn);
        card.Child = stack;
        return card;
    }

    private Border BuildAutoGameSaveBackupCard()
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
            Text = "⏱ Auto-Backup Active Save",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        });
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Automatically backs up your most recent save on a timer to protect against crashes and corruption.",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Toggle and status
        var toggleStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        var statusText = new TextBlock
        {
            Text = _autoBackupEnabled ? "● Enabled" : "● Disabled",
            FontSize = 11,
            Foreground = _autoBackupEnabled
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var lastBackupText = new TextBlock
        {
            Text = _lastAutoBackupTime is null ? "" : $"Last backup: {_lastAutoBackupTime:g}",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _autoBackupStatusLabel = lastBackupText;

        var toggleSwitch = BuildToggleSwitch(_autoBackupEnabled, (isOn) =>
        {
            _autoBackupEnabled = isOn;
            statusText.Text = isOn ? "● Enabled" : "● Disabled";
            statusText.Foreground = isOn
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            if (isOn)
            {
                StartAutoBackupTimer();
                PerformAutoBackup(); // immediate first backup
                ShowToast("Auto-Backup", "Enabled — first backup created now.", ToastService.ToastType.Success);
            }
            else
            {
                StopAutoBackupTimer();
                ShowToast("Auto-Backup", "Disabled.", ToastService.ToastType.Info);
            }
        });
        toggleStack.Children.Add(toggleSwitch);
        toggleStack.Children.Add(statusText);
        stack.Children.Add(toggleStack);
        stack.Children.Add(lastBackupText);

        // Backup interval settings
        var intervalStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        intervalStack.Children.Add(new TextBlock
        {
            Text = "Backup Interval (minutes):",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var intervalPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var intervalOptions = new[] { 1, 5, 10, 15, 30 };
        var intervalButtons = new List<Button>();
        var selectedBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5));
        var normalBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4F));

        foreach (var minutes in intervalOptions)
        {
            var btn = new Button
            {
                Content = $"{minutes}m",
                Tag = minutes,
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 10,
                Background = _autoBackupIntervalSeconds == minutes * 60 ? selectedBrush : normalBrush,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 0),
                FontWeight = FontWeights.SemiBold
            };
            btn.Click += (s, e) =>
            {
                _autoBackupIntervalSeconds = minutes * 60;
                foreach (var b in intervalButtons)
                    b.Background = (int)b.Tag == minutes ? selectedBrush : normalBrush;

                // Restart the timer with the new interval when running
                if (_autoBackupTimer != null)
                {
                    _autoBackupTimer.Stop();
                    _autoBackupTimer.Interval = TimeSpan.FromSeconds(_autoBackupIntervalSeconds);
                    _autoBackupTimer.Start();
                }
                ShowToast("Auto-Backup", $"Interval set to {minutes} minute(s).", ToastService.ToastType.Info);
            };
            intervalButtons.Add(btn);
            intervalPanel.Children.Add(btn);
        }
        intervalStack.Children.Add(intervalPanel);
        stack.Children.Add(intervalStack);

        // Manual backup button
        var backupNowBtn = new Button
        {
            Content = "Backup Now",
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Immediately back up the most recent save"
        };
        backupNowBtn.Click += (_, _) =>
        {
            if (PerformAutoBackup())
                ShowToast("Auto-Backup", "Backup created.", ToastService.ToastType.Success);
            else
                ShowToast("Auto-Backup", "No save file found to back up.", ToastService.ToastType.Warning);
        };
        stack.Children.Add(backupNowBtn);

        stack.Children.Add(new TextBlock
        {
            Text = "Backups are stored in: " + RimWorldSaveReader.BackupsPath,
            FontSize = 9,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas")
        });

        card.Child = stack;
        return card;
    }

    private Border BuildToggleSwitch(bool initialOn, Action<bool> onToggled)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x3A, 0x7B, 0xD5)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0x3A, 0x7B, 0xD5)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(1),
            Width = 50,
            Height = 24,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var isOn = initialOn;

        var toggleBackground = new Border
        {
            Background = isOn
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
            CornerRadius = new CornerRadius(3)
        };
        container.Child = toggleBackground;

        var updateToggle = new Action(() =>
        {
            toggleBackground.Background = isOn
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
        });

        container.MouseDown += (s, e) =>
        {
            isOn = !isOn;
            updateToggle();
            onToggled(isOn);
        };

        return container;
    }

    private void StartAutoBackupTimer()
    {
        if (_autoBackupTimer != null)
            return;

        _autoBackupTimer = new DispatcherTimer();
        _autoBackupTimer.Interval = TimeSpan.FromSeconds(_autoBackupIntervalSeconds);
        _autoBackupTimer.Tick += (s, e) =>
        {
            PerformAutoBackup();
        };
        _autoBackupTimer.Start();
    }

    private void StopAutoBackupTimer()
    {
        if (_autoBackupTimer != null)
        {
            _autoBackupTimer.Stop();
            _autoBackupTimer = null;
        }
    }

    private bool PerformAutoBackup()
    {
        try
        {
            var recent = RimWorldSaveReader.GetMostRecentSaveName();
            if (recent is null) return false;

            var savePath = RimWorldSaveReader.FindSaveFilePath(recent);
            if (savePath is null) return false;

            // Skip if the save hasn't changed since it was last backed up —
            // this also stops deleted backups from being recreated endlessly.
            var saveTime = File.GetLastWriteTime(savePath);
            if (saveTime <= _lastAutoBackupSaveTime) return false;

            var backup = RimWorldSaveReader.BackupSaveFile(savePath, "AutoBackup");
            if (backup is null) return false;

            _lastAutoBackupSaveTime = saveTime;
            _lastAutoBackupTime = DateTime.Now;
            if (_autoBackupStatusLabel is not null)
                _autoBackupStatusLabel.Text = $"Last backup: {_lastAutoBackupTime:g} — {recent}";

            // Keep only the 30 most recent auto-backups; never prune
            // user, emergency, or FlexTool backups.
            var old = Directory.GetFiles(RimWorldSaveReader.BackupsPath, "*.rws")
                .Where(f => string.Equals(
                    RimWorldSaveReader.ParseBackupName(f).Label, "AutoBackup", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTime)
                .Skip(30);
            foreach (var f in old)
            {
                try { File.Delete(f); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
            }

            return true;
        }
        catch { return false; }
    }

    private DateTime _lastAutoBackupSaveTime = DateTime.MinValue;

    // ── Live Warning Alerts (moved from Analytics) ──────────────
    // UI scaffold only — reuses the shared builder in MainWindow.Analytics.cs.

    private UIElement BuildLiveWarningAlertsCard()
    {
        return BuildLiveWarningAlertsSection();
    }

    // ── Crash Handler ────────────────────────────────────────
    // Watches the game process, captures crash logs and auto-creates a
    // recovery point when RimWorld exits unexpectedly.

    private Border BuildCrashHandlerCard()
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
            Text = "🛡 Crash Handler",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(titleRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Full crash & freeze protection stack. External layer: detects crashes within a second (process exit codes + live log scanning), captures crash reports, and creates emergency recovery backups. In-game layer (via the Debug Info mod): exception interception, real emergency autosaves, memory-pressure freeze prevention, slowdown detection, and a 5-second stall watchdog with heartbeat. Every protective action shows both an in-game alert and a FlexTool toast.",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Toggle row — reflects the persisted state so tab switches don't reset the label
        var toggleStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var statusText = new TextBlock
        {
            Text = _crashHandlerEnabled ? "● Enabled" : "● Disabled",
            FontSize = 11,
            Foreground = _crashHandlerEnabled
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        toggleStack.Children.Add(BuildToggleSwitch(_crashHandlerEnabled, isOn =>
        {
            _crashHandlerEnabled = isOn;
            statusText.Text = isOn ? "● Enabled" : "● Disabled";
            statusText.Foreground = isOn
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xA8, 0x4C))
                : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            if (isOn) StartCrashWatcher(); else StopCrashWatcher();
            SaveWindowState();
            ShowToast("Crash Handler", isOn ? "Crash detection enabled." : "Crash detection disabled.",
                ToastService.ToastType.Info);
        }));
        toggleStack.Children.Add(statusText);
        stack.Children.Add(toggleStack);

        // ── Crash Prevention sub-toggle ───────────────────────────
        Border BuildSubToggleRow(string title, string desc, bool initial, Action<bool> onChanged)
        {
            var row = new Border
            {
                Background = (Brush)FindResource("PanelDarkBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textCol.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            });
            textCol.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 8, 0)
            });
            Grid.SetColumn(textCol, 0);
            grid.Children.Add(textCol);
            var toggle = BuildToggleSwitch(initial, onChanged);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            row.Child = grid;
            return row;
        }

        stack.Children.Add(BuildSubToggleRow(
            "🔒 Crash Prevention",
            "Proactively backs up your current save the moment early warning signs appear in the game log (out-of-memory, failed asset loads, mod exceptions) — before a crash can take your progress with it.",
            _crashPreventionEnabled,
            isOn =>
            {
                _crashPreventionEnabled = isOn;
                SaveWindowState();
                ShowToast("Crash Prevention", isOn
                    ? "Proactive save protection enabled."
                    : "Proactive save protection disabled.", ToastService.ToastType.Info);
            }));

        stack.Children.Add(BuildSubToggleRow(
            "⏱ Watchdog (Auto-Restart)",
            "Monitors the game for hangs. If RimWorld stops responding for over 30 seconds, the frozen process is terminated and the game is automatically restarted.",
            _watchdogEnabled,
            isOn =>
            {
                _watchdogEnabled = isOn;
                SaveWindowState();
                ShowToast("Watchdog", isOn
                    ? "Hang detection and auto-restart enabled."
                    : "Hang detection disabled.", ToastService.ToastType.Info);
            }));

        // Feature summary rows (visual only)
        void AddFeatureRow(string text)
        {
            stack.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 4)
            });
        }
        AddFeatureRow("✓ 1-second crash detection via process exit codes + live log scanning");
        AddFeatureRow("✓ Exception handler — captures mod exceptions and error spikes from Player.log");
        AddFeatureRow("✓ Create emergency save backup on crash detection");
        AddFeatureRow("✓ Full crash report with exit code, session info and log tail");

        var reportsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlexTool", "CrashReports");

        var openReportsBtn = new Button
        {
            Content = "📁  Open Crash Reports Folder",
            Style = (Style)FindResource("CardSmallButtonStyle"),
            FontSize = 10,
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        openReportsBtn.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(reportsDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = reportsDir,
                    UseShellExecute = true
                });
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        };
        stack.Children.Add(openReportsBtn);

        stack.Children.Add(new TextBlock
        {
            Text = "Crash reports are saved to " + reportsDir,
            FontSize = 9,
            FontStyle = FontStyles.Italic,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = stack;
        return card;
    }

    private void StartCrashWatcher()
    {
        if (_crashWatchTimer != null) return;
        _gameWasRunning = IsRimWorldRunning();
        _watchedGameProcess = TryGetGameProcess();
        _gameUnresponsiveSince = null;
        _lastPlayerLogLength = GetPlayerLogLength();

        // Arm the in-game CrashGuard companion (heartbeat + exception
        // interception + emergency in-game autosave via the Debug Info mod).
        RimWorldSaveReader.WriteCrashGuardSettings(enabled: true);

        // 1-second polling: crashes are detected almost immediately, and the
        // per-tick work (a process lookup + a file-length check) is tiny.
        _crashWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _crashWatchTimer.Tick += (_, _) => CrashWatcherTick();
        _crashWatchTimer.Start();
    }

    private void StopCrashWatcher()
    {
        _crashWatchTimer?.Stop();
        _crashWatchTimer = null;
        _watchedGameProcess?.Dispose();
        _watchedGameProcess = null;
        _gameUnresponsiveSince = null;
        RimWorldSaveReader.WriteCrashGuardSettings(enabled: false);
    }

    private void CrashWatcherTick()
    {
        try
        {
            bool running = IsRimWorldRunning();

            // Acquire a handle to the live process so we can read its exit
            // code and responsiveness — far more reliable than log scraping.
            if (running && (_watchedGameProcess is null || _watchedGameProcess.HasExited))
            {
                _watchedGameProcess?.Dispose();
                _watchedGameProcess = TryGetGameProcess();
                _gameUnresponsiveSince = null;
            }

            // ── Exception handler + crash prevention: live log scanning ─
            if (running) ScanLiveLogForWarningSigns();

            // ── In-game CrashGuard: surface its emergency autosaves here too ─
            if (running) CheckForInGameEmergencySave();
            if (running) CheckForCrashGuardEvents();

            // ── Watchdog: hang detection with auto-restart ────────────
            if (running && _watchdogEnabled) CheckForHang();

            // ── Crash detection on exit ───────────────────────────
            if (_gameWasRunning && !running)
                HandlePossibleCrash();

            _gameWasRunning = running;
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static System.Diagnostics.Process? TryGetGameProcess()
    {
        try
        {
            var procs = System.Diagnostics.Process.GetProcessesByName("RimWorldWin64");
            for (int i = 1; i < procs.Length; i++) procs[i].Dispose();
            return procs.Length > 0 ? procs[0] : null;
        }
        catch { return null; }
    }

    private static bool IsRimWorldRunning()
    {
        try { return System.Diagnostics.Process.GetProcessesByName("RimWorldWin64").Length > 0; }
        catch { return false; }
    }

    /// <summary>
    /// Detects when the in-game CrashGuard (Debug Info mod) has just written
    /// its FlexTool_Emergency save and surfaces it as a toast in the app, so
    /// the in-game protection and the app notifications stay in sync.
    /// </summary>
    private void CheckForInGameEmergencySave()
    {
        try
        {
            var path = Path.Combine(RimWorldSaveReader.SavesPath, "FlexTool_Emergency.rws");
            if (!File.Exists(path)) return;

            var written = File.GetLastWriteTime(path);
            if (_lastEmergencySaveSeen == DateTime.MinValue)
            {
                _lastEmergencySaveSeen = written; // baseline — don't toast for old saves
                return;
            }
            if (written <= _lastEmergencySaveSeen) return;
            _lastEmergencySaveSeen = written;

            if (!_alertEmergencySaveEnabled) return;
            ShowToast("🛡 In-Game CrashGuard",
                "The game detected danger and created an emergency save: FlexTool_Emergency",
                ToastService.ToastType.Warning);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    /// <summary>
    /// Bridges in-game CrashGuard protective actions (crash prevented, freeze
    /// prevented, freeze warning) into FlexTool toasts, so every in-game live
    /// alert has a matching desktop notification.
    /// </summary>
    private void CheckForCrashGuardEvents()
    {
        try
        {
            var evt = RimWorldSaveReader.ReadCrashGuardEvent();
            if (evt is null) return;

            if (_lastCrashGuardEventSeen == DateTime.MinValue)
            {
                _lastCrashGuardEventSeen = evt.Value.Utc; // baseline — skip stale events
                return;
            }
            if (evt.Value.Utc <= _lastCrashGuardEventSeen) return;
            _lastCrashGuardEventSeen = evt.Value.Utc;

            if (!_alertCrashGuardEnabled) return;
            var (title, type) = evt.Value.Kind switch
            {
                "CrashPrevented" => ("🛡 Crash Prevented", ToastService.ToastType.Warning),
                "FreezePrevented" => ("❄ Freeze Prevented", ToastService.ToastType.Success),
                "FreezeWarning" => ("⚠ Freeze Warning", ToastService.ToastType.Warning),
                _ => ("🛡 CrashGuard", ToastService.ToastType.Info)
            };
            ShowToast(title, evt.Value.Detail, type);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static long GetPlayerLogLength()
    {
        try
        {
            var logPath = Path.Combine(RimWorldSaveReader.UserDataPath, "Player.log");
            return File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Exception handler: incrementally reads NEW Player.log content each
    /// second and reacts to early warning signs before a full crash —
    /// capturing an exception report and (when Crash Prevention is on)
    /// immediately backing up the current save.
    /// </summary>
    private void ScanLiveLogForWarningSigns()
    {
        try
        {
            var logPath = Path.Combine(RimWorldSaveReader.UserDataPath, "Player.log");
            if (!File.Exists(logPath)) return;

            long length = new FileInfo(logPath).Length;
            if (length < _lastPlayerLogLength) _lastPlayerLogLength = 0; // log rotated — game restarted
            if (length <= _lastPlayerLogLength) return;                 // nothing new

            string newContent;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(_lastPlayerLogLength, SeekOrigin.Begin);
                // Cap the read so a huge burst can't stall the UI thread
                var toRead = (int)Math.Min(length - _lastPlayerLogLength, 256 * 1024);
                var buffer = new byte[toRead];
                int read = fs.Read(buffer, 0, toRead);
                newContent = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            }
            _lastPlayerLogLength = length;

            bool danger =
                   newContent.Contains("OutOfMemoryException", StringComparison.OrdinalIgnoreCase)
                || newContent.Contains("StackOverflowException", StringComparison.OrdinalIgnoreCase)
                || newContent.Contains("AccessViolationException", StringComparison.OrdinalIgnoreCase)
                || newContent.Contains("Could not allocate memory", StringComparison.OrdinalIgnoreCase)
                || newContent.Contains("System.ExecutionEngineException", StringComparison.OrdinalIgnoreCase);

            bool exceptionSpike = CountOccurrences(newContent, "Exception") >= 10;

            if (!danger && !exceptionSpike) return;

            // Capture an exception report (throttled with the same cooldown
            // as crash reports so a repeating error can't flood the folder)
            if ((DateTime.Now - _lastCrashHandled).TotalSeconds > 60)
            {
                _lastCrashHandled = DateTime.Now;
                var reportsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlexTool", "CrashReports");
                Directory.CreateDirectory(reportsDir);
                var kind = danger ? "critical" : "exceptions";
                File.WriteAllText(
                    Path.Combine(reportsDir, $"{kind}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"),
                    $"FlexTool live exception capture — {DateTime.Now:F}\r\n" +
                    $"Trigger: {(danger ? "critical error signature" : "exception spike (10+ in one second)")}\r\n" +
                    "────────── new log content ──────────\r\n" + newContent);
            }

            // Crash prevention: snapshot the save right now, before the game dies
            if (_crashPreventionEnabled && danger
                && (DateTime.Now - _lastPreventionBackup).TotalSeconds > 120)
            {
                _lastPreventionBackup = DateTime.Now;
                var recent = RimWorldSaveReader.GetMostRecentSaveName();
                var savePath = recent is not null ? RimWorldSaveReader.FindSaveFilePath(recent) : null;
                if (savePath is not null && RimWorldSaveReader.BackupSaveFile(savePath, "CrashPrevention") is not null)
                {
                    ShowToast("🔒 Crash Prevention",
                        $"Critical error detected in the game log — \"{recent}\" was backed up before any damage could occur.",
                        ToastService.ToastType.Warning);
                }
            }
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(token, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += token.Length;
        }
        return count;
    }

    /// <summary>
    /// Watchdog: two independent hang signals.
    ///  • OS-level: the game window's Responding flag (frozen message pump).
    ///  • Logic-level: the in-game heartbeat written every second by the
    ///    Debug Info mod's CrashGuard — catches stuck tick loops where the
    ///    window still pumps messages but the game logic is dead.
    /// If either stays unhealthy for 30 continuous seconds, the process is
    /// killed (after an emergency backup) and relaunched.
    /// </summary>
    private void CheckForHang()
    {
        try
        {
            var proc = _watchedGameProcess;
            if (proc is null || proc.HasExited || _watchdogRestartInProgress) return;
            if (proc.MainWindowHandle == IntPtr.Zero) return; // still loading — no window yet

            proc.Refresh();
            bool unhealthy = !proc.Responding;
            bool fastStall = false;

            // Logic-level checks via the in-game CrashGuard heartbeat:
            //  • stalled=1 — the mod's in-process stall watchdog caught a
            //    stuck main thread within ~5s (much faster than any external
            //    signal), so we can act on a shorter grace period.
            //  • stale heartbeat — no writes for 30+ seconds while the window
            //    is alive means the game loop itself is stuck. Only trusted
            //    when the heartbeat was recently fresh (mod installed).
            var hb = RimWorldSaveReader.ReadHeartbeat();
            if (hb is not null)
            {
                var age = DateTime.UtcNow - hb.Value.Utc;
                if (hb.Value.Stalled && age.TotalMinutes < 5)
                {
                    unhealthy = true;
                    fastStall = true;
                }
                else if (age.TotalSeconds > 30 && age.TotalMinutes < 5)
                {
                    unhealthy = true;
                }
            }

            if (!unhealthy)
            {
                _gameUnresponsiveSince = null;
                return;
            }

            _gameUnresponsiveSince ??= DateTime.Now;
            // The in-game stall flag is high-confidence, so recovery can start
            // after 10s instead of the conservative 30s external window.
            int graceSeconds = fastStall ? 10 : 30;
            if ((DateTime.Now - _gameUnresponsiveSince.Value).TotalSeconds < graceSeconds) return;

            _watchdogRestartInProgress = true;
            _gameUnresponsiveSince = null;

            // Emergency backup before killing the frozen process
            var recent = RimWorldSaveReader.GetMostRecentSaveName();
            var savePath = recent is not null ? RimWorldSaveReader.FindSaveFilePath(recent) : null;
            if (savePath is not null) RimWorldSaveReader.BackupSaveFile(savePath, "Watchdog");

            try { proc.Kill(); proc.WaitForExit(10000); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
            _watchedGameProcess = null;
            proc.Dispose();

            ShowToast("⏱ Watchdog",
                "RimWorld was frozen for 30+ seconds — the hung process was terminated and the game is restarting.",
                ToastService.ToastType.Warning);

            // Give the OS a moment to release file locks, then relaunch
            var restartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            restartTimer.Tick += (_, _) =>
            {
                restartTimer.Stop();
                try { LaunchGame(loadSave: false); }
                catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
                finally { _watchdogRestartInProgress = false; }
            };
            restartTimer.Start();
        }
        catch { _watchdogRestartInProgress = false; }
    }

    private void HandlePossibleCrash()
    {
        try
        {
            // Throttle so one crash doesn't produce multiple reports
            if ((DateTime.Now - _lastCrashHandled).TotalSeconds < 15) return;

            // ── Signal 1: process exit code (catches silent native crashes
            //    that never write anything to Player.log) ───────────────
            int? exitCode = null;
            try
            {
                if (_watchedGameProcess is not null && _watchedGameProcess.HasExited)
                    exitCode = _watchedGameProcess.ExitCode;
            }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
            _watchedGameProcess?.Dispose();
            _watchedGameProcess = null;
            bool crashedByExitCode = exitCode.HasValue && exitCode.Value != 0;

            // ── Signal 2: crash signatures in the log tail ──────────────
            var logPath = Path.Combine(RimWorldSaveReader.UserDataPath, "Player.log");
            bool crashedByLog = false;
            string tail = "";

            if (File.Exists(logPath))
            {
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = sr.ReadToEnd();
                tail = content.Length > 8000 ? content[^8000..] : content;
                crashedByLog = tail.Contains("Crash!!!", StringComparison.OrdinalIgnoreCase)
                    || tail.Contains("Native Crash", StringComparison.OrdinalIgnoreCase)
                    || tail.Contains("Unhandled Exception", StringComparison.OrdinalIgnoreCase)
                    || tail.Contains("Fatal error", StringComparison.OrdinalIgnoreCase)
                    || tail.Contains("OutOfMemoryException", StringComparison.OrdinalIgnoreCase)
                    || tail.Contains("AccessViolationException", StringComparison.OrdinalIgnoreCase)
                    || tail.Contains("Segmentation fault", StringComparison.OrdinalIgnoreCase);
            }

            if (!crashedByExitCode && !crashedByLog) return; // normal exit
            _lastCrashHandled = DateTime.Now;

            // 1) Capture crash report with full context
            var reportsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlexTool", "CrashReports");
            Directory.CreateDirectory(reportsDir);
            var reportPath = Path.Combine(reportsDir, $"crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            File.WriteAllText(reportPath,
                $"FlexTool crash report — {DateTime.Now:F}\r\n" +
                $"Detected via: {(crashedByExitCode ? $"process exit code {exitCode}" : "crash signature in Player.log")}\r\n" +
                $"Exit code: {(exitCode.HasValue ? exitCode.Value.ToString() : "unknown")}\r\n" +
                "────────── Player.log tail ──────────\r\n" +
                (tail.Length > 0 ? tail : "(Player.log unavailable — crash detected from process exit code only)"));

            // 2) Emergency save backup
            string? backupNote = null;
            var recent = RimWorldSaveReader.GetMostRecentSaveName();
            if (recent is not null)
            {
                var savePath = RimWorldSaveReader.FindSaveFilePath(recent);
                if (savePath is not null)
                {
                    var backup = RimWorldSaveReader.BackupSaveFile(savePath, "Emergency");
                    if (backup is not null) backupNote = $" Emergency backup of \"{recent}\" created — see Save Games › Backups.";
                }
            }

            ShowToast("🛡 Crash Detected",
                $"RimWorld exited unexpectedly. Crash log saved.{backupNote ?? ""}",
                ToastService.ToastType.Error);
        }
        catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
    }

    private static string ProfilesPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlexTool", "Profiles");

    private StackPanel? _profileListPanel;

    private Border BuildConfigsPlaceholderCard()
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

        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        headerRow.Children.Add(new TextBlock
        {
            Text = "Config Profiles",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var saveBtn = new Button
        {
            Content = "Save Current Mod List",
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Snapshot the current ModsConfig.xml as a named profile"
        };
        DockPanel.SetDock(saveBtn, Dock.Right);
        saveBtn.Click += (_, _) => SaveConfigProfile();
        headerRow.Children.Add(saveBtn);
        stack.Children.Add(headerRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Save and restore snapshots of your active mod list (ModsConfig.xml).",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        _profileListPanel = new StackPanel();
        stack.Children.Add(_profileListPanel);
        RebuildProfileList();

        card.Child = stack;
        return card;
    }

    private void RebuildProfileList()
    {
        if (_profileListPanel is null) return;
        _profileListPanel.Children.Clear();

        string[] files;
        try
        {
            Directory.CreateDirectory(ProfilesPath);
            files = Directory.GetFiles(ProfilesPath, "*.xml");
        }
        catch { files = []; }

        if (files.Length == 0)
        {
            _profileListPanel.Children.Add(new TextBlock
            {
                Text = "No profiles saved yet.",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });
            return;
        }

        foreach (var file in files.OrderByDescending(File.GetLastWriteTime))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            var delBtn = new Button
            {
                Content = "Delete",
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            var capturedFile = file;
            delBtn.Click += (_, _) =>
            {
                try { File.Delete(capturedFile); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
                RebuildProfileList();
            };
            DockPanel.SetDock(delBtn, Dock.Right);
            row.Children.Add(delBtn);

            var loadBtn = new Button
            {
                Content = "Load",
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Replace the game's ModsConfig.xml with this profile"
            };
            loadBtn.Click += (_, _) => LoadConfigProfile(capturedFile);
            DockPanel.SetDock(loadBtn, Dock.Right);
            row.Children.Add(loadBtn);

            row.Children.Add(new TextBlock
            {
                Text = $"{Path.GetFileNameWithoutExtension(file)}  ({File.GetLastWriteTime(file):g})",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });

            _profileListPanel.Children.Add(row);
        }
    }

    private void SaveConfigProfile()
    {
        try
        {
            if (!File.Exists(RimWorldSaveReader.ModsConfigPath))
            {
                ShowToast("Profiles", "ModsConfig.xml not found.", ToastService.ToastType.Error);
                return;
            }

            Directory.CreateDirectory(ProfilesPath);
            var name = $"Profile {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
            File.Copy(RimWorldSaveReader.ModsConfigPath, Path.Combine(ProfilesPath, name + ".xml"), true);
            ShowToast("Profiles", $"Saved \"{name}\".", ToastService.ToastType.Success);
            RebuildProfileList();
        }
        catch (Exception ex)
        {
            ShowToast("Profiles", ex.Message, ToastService.ToastType.Error);
        }
    }

    private void LoadConfigProfile(string profileFile)
    {
        var confirmed = FlexToolDialog.ShowWarning(this,
            "Load Config Profile",
            $"Replace the current mod list with \"{Path.GetFileNameWithoutExtension(profileFile)}\"?\n\nThe current ModsConfig.xml will be overwritten. Restart the game for changes to take effect.");
        if (!confirmed) return;

        try
        {
            File.Copy(profileFile, RimWorldSaveReader.ModsConfigPath, true);
            ShowToast("Profiles", "Profile loaded — restart the game to apply.", ToastService.ToastType.Success);
        }
        catch (Exception ex)
        {
            ShowToast("Profiles", ex.Message, ToastService.ToastType.Error);
        }
    }

    // ── Folders Tab ──────────────────────────────────────────────────
    // Simple, clean folder management with OS folder browser.

    private UIElement BuildFoldersTabContent()
    {
        FolderLocationManager.EnsureFlexToolFolders();

        var root = new StackPanel { Margin = new Thickness(8) };

        // Header — themed like the Configs header card
        var headerCard = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var headerStack = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = "Folder Management",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x7B, 0xD5), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x4B, 0xA5), 1.0));
        titleBlock.Foreground = gradientBrush;
        headerStack.Children.Add(titleBlock);

        headerStack.Children.Add(new TextBlock
        {
            Text = "View and manage all folders FlexTool uses. Open any folder in Explorer or change its location.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var actionStack = new StackPanel { Orientation = Orientation.Horizontal };

        var autoDetectBtn = new Button
        {
            Content = "Auto-Detect Folders",
            Padding = new Thickness(14, 8, 14, 8),
            FontSize = 11,
            Background = (Brush)FindResource("BluePrimaryBrush"),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Rescan your system and update all folder locations automatically"
        };
        autoDetectBtn.Click += (s, e) =>
        {
            if (FlexToolDialog.ShowWarning(this, "Auto-Detect Folders",
                "Rescan system and update all folder locations to defaults?"))
            {
                if (!FolderLocationManager.ForceAutoDetect())
                {
                    ShowToast("Error", "Failed to auto-detect folders", ToastService.ToastType.Error);
                }
                else
                {
                    RefreshFoldersTab();
                    ShowToast("Success", "Folders updated", ToastService.ToastType.Success);
                }
            }
        };
        actionStack.Children.Add(autoDetectBtn);

        var resetBtn = new Button
        {
            Content = "Reset to Defaults",
            Padding = new Thickness(14, 8, 14, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4F)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Reset all folders to default locations without rescanning"
        };
        resetBtn.Click += (s, e) =>
        {
            if (FlexToolDialog.ShowWarning(this, "Reset Folders",
                "Reset all folders to default locations without rescanning?"))
            {
                if (!FolderLocationManager.ResetToDefaults())
                {
                    ShowToast("Error", "Failed to reset folders", ToastService.ToastType.Error);
                }
                else
                {
                    RefreshFoldersTab();
                    ShowToast("Success", "Folders reset", ToastService.ToastType.Success);
                }
            }
        };
        actionStack.Children.Add(resetBtn);

        headerStack.Children.Add(actionStack);
        headerCard.Child = headerStack;
        root.Children.Add(headerCard);

        // Folder list — the outer ContentPanel ScrollViewer (MainWindow.xaml)
        // handles scrolling exactly like the Configs tab. A nested ScrollViewer
        // here captured the mouse wheel and prevented mid-page scrolling.
        var categories = new Dictionary<string, List<string>>();

        // Group folders by category
        foreach (var key in FolderLocationManager.GetAllFolderKeys())
        {
            var (_, _, category) = FolderLocationManager.GetFolderInfo(key);
            if (!categories.ContainsKey(category))
                categories[category] = new List<string>();
            categories[category].Add(key);
        }

        // Render each category as a themed card matching the Configs tab
        var categoryOrder = new[] { "Game", "FlexTool", "Mods" };
        foreach (var categoryName in categoryOrder)
        {
            if (!categories.TryGetValue(categoryName, out var folderKeys))
                continue;

            var categoryCard = new Border
            {
                Background = (Brush)FindResource("PanelMidBrush"),
                BorderBrush = (Brush)FindResource("BorderBlueBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var categoryStack = new StackPanel();
            categoryStack.Children.Add(new TextBlock
            {
                Text = categoryName switch
                {
                    "Game" => "🎮 Game Folders",
                    "FlexTool" => "🛠 FlexTool Folders",
                    "Mods" => "📦 Mod Folders",
                    _ => categoryName
                },
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BlueLightBrush"),
                Margin = new Thickness(0, 0, 0, 12)
            });

            foreach (var folderKey in folderKeys)
                categoryStack.Children.Add(BuildSimpleFolderRow(folderKey));

            categoryCard.Child = categoryStack;
            root.Children.Add(categoryCard);
        }

        return root;
    }

    private Border BuildSimpleFolderRow(string folderKey)
    {
        var (label, description, _) = FolderLocationManager.GetFolderInfo(folderKey);
        var isFlexToolFolder = FolderLocationManager.IsFlexToolFolder(folderKey);
        var isFile = FolderLocationManager.IsFilePath(folderKey);
        var isModDeploy = FolderLocationManager.IsModDeploymentFolder(folderKey);
        var folderPath = FolderLocationManager.Current.GetPath(folderKey) ?? "";
        var pathExists = !string.IsNullOrEmpty(folderPath)
            && (isFile ? File.Exists(folderPath) : Directory.Exists(folderPath));

        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0x3A, 0x7B, 0xD5)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0x3A, 0x7B, 0xD5)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left content
        var leftStack = new StackPanel();

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        titleRow.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        Border MakeBadge(string text, Color color) => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color)
            }
        };

        if (isFlexToolFolder)
            titleRow.Children.Add(MakeBadge("FLEXTOOL", Color.FromRgb(0x5A, 0x9A, 0xE6)));
        if (isFile)
            titleRow.Children.Add(MakeBadge("FILE", Color.FromRgb(0x9B, 0x7B, 0xD5)));

        if (!pathExists)
        {
            titleRow.Children.Add(isModDeploy
                ? MakeBadge("NOT INSTALLED", Color.FromRgb(0xE8, 0x8B, 0x4D))
                : MakeBadge("NOT FOUND", Color.FromRgb(0xE7, 0x4C, 0x3C)));
        }
        else
        {
            titleRow.Children.Add(MakeBadge("OK", Color.FromRgb(0x2E, 0xCC, 0x71)));
        }

        leftStack.Children.Add(titleRow);

        leftStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(folderPath) ? "(no path set)" : folderPath,
            FontSize = 9,
            FontFamily = new FontFamily("Consolas"),
            Foreground = pathExists ? (Brush)FindResource("TextSecondaryBrush") : new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        if (!string.IsNullOrEmpty(description))
        {
            leftStack.Children.Add(new TextBlock
            {
                Text = isModDeploy && !pathExists
                    ? description + " — install the mod from the Mods tab to create this folder."
                    : description,
                FontSize = 9,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        Grid.SetColumn(leftStack, 0);

        // Right buttons
        var btnStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var openBtn = new Button
        {
            Content = "Open",
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 11,
            Background = (Brush)FindResource("BluePrimaryBrush"),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            ToolTip = isFile ? "Show file in Explorer" : "Open folder in Explorer",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        openBtn.Click += (s, e) =>
        {
            try
            {
                if (isFile && File.Exists(folderPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{folderPath}\"",
                        UseShellExecute = true
                    });
                else if (!isFile && Directory.Exists(folderPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{folderPath}\"",
                        UseShellExecute = true
                    });
                else
                    ShowToast("Warning", isFile ? "File does not exist" : "Folder does not exist", ToastService.ToastType.Warning);
            }
            catch (Exception ex)
            {
                ShowToast("Error", $"Cannot open: {ex.Message}", ToastService.ToastType.Error);
            }
        };
        btnStack.Children.Add(openBtn);

        // Missing plain folders (not files, not mod deployments) get a quick Create action
        if (!pathExists && !isFile && !isModDeploy && !string.IsNullOrEmpty(folderPath))
        {
            var createBtn = new Button
            {
                Content = "Create",
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                ToolTip = "Create this folder now",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0)
            };
            createBtn.Click += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(folderPath);
                    ShowToast("Folders", $"Created: {folderPath}", ToastService.ToastType.Success);
                    RefreshFoldersTab();
                }
                catch (Exception ex)
                {
                    ShowToast("Error", $"Cannot create folder: {ex.Message}", ToastService.ToastType.Error);
                }
            };
            btnStack.Children.Add(createBtn);
        }

        // Files and mod deployment folders are managed automatically — no manual re-pointing
        if (!isFile && !isModDeploy)
        {
            var editBtn = new Button
            {
                Content = "Change",
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x4F)),
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                ToolTip = "Change folder location",
                FontWeight = FontWeights.SemiBold
            };
            editBtn.Click += (s, e) =>
            {
                OpenFolderBrowser(folderKey, label);
            };
            btnStack.Children.Add(editBtn);
        }

        Grid.SetColumn(btnStack, 1);
        grid.Children.Add(leftStack);
        grid.Children.Add(btnStack);
        row.Child = grid;

        return row;
    }

    private void OpenFolderBrowser(string folderKey, string folderLabel)
    {
        var currentPath = FolderLocationManager.Current.GetPath(folderKey) ?? "";

        // Borderless themed dialog — no OS white chrome
        var dialog = new Window
        {
            Title = $"Change Folder: {folderLabel}",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var outerBorder = new Border
        {
            Background = (Brush)FindResource("PanelDarkBrush"),
            BorderBrush = (Brush)FindResource("BluePrimaryBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0x3A, 0x7B, 0xD5),
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.25
            }
        };

        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

        // Draggable title bar
        var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        titleBar.MouseLeftButtonDown += (_, _) => { try { dialog.DragMove(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = $"📁 Change Folder: {folderLabel}",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeBtn.Click += (_, _) => dialog.Close();
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);
        stack.Children.Add(titleBar);

        // Separator
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BorderBlueBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Enter the new folder path or browse for one:",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };

        var browseBtn = new Button
        {
            Content = "Browse…",
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(browseBtn, Dock.Right);

        var pathTextBox = new TextBox
        {
            Text = currentPath,
            Padding = new Thickness(8),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Background = (Brush)FindResource("PanelMidBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            CaretBrush = (Brush)FindResource("TextPrimaryBrush"),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1)
        };

        browseBtn.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog
            {
                Title = $"Select folder for {folderLabel}",
                InitialDirectory = Directory.Exists(pathTextBox.Text.Trim()) ? pathTextBox.Text.Trim() : ""
            };
            if (picker.ShowDialog(dialog) == true)
                pathTextBox.Text = picker.FolderName;
        };

        pathRow.Children.Add(browseBtn);
        pathRow.Children.Add(pathTextBox);
        stack.Children.Add(pathRow);

        var btnStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okBtn = new Button
        {
            Content = "✓ Save",
            Padding = new Thickness(14, 8, 14, 8),
            FontSize = 11,
            Background = (Brush)FindResource("BluePrimaryBrush"),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        okBtn.Click += (s, e) =>
        {
            var newPath = pathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(newPath))
            {
                ShowToast("Error", "Path cannot be empty", ToastService.ToastType.Error);
                return;
            }

            if (!Directory.Exists(newPath))
            {
                if (!FlexToolDialog.ShowWarning(this, "Create Folder",
                    $"The folder does not exist:\n{newPath}\n\nCreate it now?"))
                    return;
                try { Directory.CreateDirectory(newPath); }
                catch (Exception ex)
                {
                    ShowToast("Error", $"Cannot create folder: {ex.Message}", ToastService.ToastType.Error);
                    return;
                }
            }

            var (success, errorMessage) = FolderLocationManager.UpdateFolder(folderKey, newPath);
            if (success)
            {
                dialog.Close();
                RefreshFoldersTab();
                ShowToast("Success", $"Updated: {folderLabel}", ToastService.ToastType.Success);
            }
            else
            {
                ShowToast("Error", errorMessage, ToastService.ToastType.Error);
            }
        };
        btnStack.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 8, 14, 8),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        cancelBtn.Click += (s, e) => dialog.Close();
        btnStack.Children.Add(cancelBtn);

        stack.Children.Add(btnStack);
        outerBorder.Child = stack;
        dialog.Content = outerBorder;
        dialog.ShowDialog();
    }

    private void RefreshFoldersTab()
    {
        UpdateContentArea();
    }
}
