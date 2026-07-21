using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FlexTool;

public partial class MainWindow
{
    // ── Mod Folder Tab Content ──────────────────────────────────

    private readonly List<string> _recentModTransfers = [];

    private UIElement BuildModFolderTabContent()
    {
        var root = new StackPanel { Margin = new Thickness(8) };

        root.Children.Add(BuildModFolderHeaderCard());
        root.Children.Add(BuildModFolderDropZoneCard());
        root.Children.Add(BuildModFolderTransferredCard());

        return root;
    }

    private Border BuildModFolderHeaderCard()
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
            Text = "Mod Folder",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var gradientBrush = new LinearGradientBrush();
        gradientBrush.StartPoint = new Point(0, 0);
        gradientBrush.EndPoint = new Point(1, 0);
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x7B, 0xD5), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0xCC, 0x71), 1.0));
        titleBlock.Foreground = gradientBrush;
        stack.Children.Add(titleBlock);

        stack.Children.Add(new TextBlock
        {
            Text = "Drag and drop mod files or folders below, or browse for them manually, to transfer mods straight into your RimWorld Mods folder.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        card.Child = stack;
        return card;
    }

    private Border BuildModFolderDropZoneCard()
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

        var outerStack = new StackPanel();

        outerStack.Children.Add(new TextBlock
        {
            Text = "1. Drag & Drop or Browse",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Dashed drop-zone visual (Grid + Rectangle, since Border has no dash-array support)
        var dropZone = new Grid
        {
            MinHeight = 140,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var dashedBorder = new Rectangle
        {
            Stroke = (Brush)FindResource("BorderBlueBrush"),
            StrokeThickness = 2,
            StrokeDashArray = [6, 4],
            RadiusX = 8,
            RadiusY = 8,
            Fill = (Brush)FindResource("PanelDarkBrush")
        };
        dropZone.Children.Add(dashedBorder);

        var innerContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20)
        };

        var iconViewbox = new Viewbox
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var iconCanvas = new Canvas { Width = 24, Height = 24 };
        var iconPath = new Path
        {
            Fill = (Brush)FindResource("BlueLightBrush"),
            Data = Geometry.Parse(
                "M12 2.4 6.4 8h3.6v6.4h4V8h3.6L12 2.4z" +
                "M4.8 16v3.2c0 .88.72 1.6 1.6 1.6h11.2c.88 0 1.6-.72 1.6-1.6V16h-2v3.2H6.8V16h-2z")
        };
        iconCanvas.Children.Add(iconPath);
        iconViewbox.Child = iconCanvas;
        innerContent.Children.Add(iconViewbox);

        innerContent.Children.Add(new TextBlock
        {
            Text = "Drag and drop mod files or folders here",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });

        innerContent.Children.Add(new TextBlock
        {
            Text = "Supports mod folders and .zip archives",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        dropZone.Children.Add(innerContent);

        // Real drag & drop
        dropZone.AllowDrop = true;
        dropZone.Background = Brushes.Transparent;
        dropZone.DragOver += (s, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        dropZone.Drop += (s, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
                TransferModsToGameFolder(paths);
            e.Handled = true;
        };

        outerStack.Children.Add(dropZone);

        // "or" divider
        outerStack.Children.Add(new TextBlock
        {
            Text = "or",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Browse buttons row (layout only — disabled until wired up)
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var browseFilesBtn = new Button
        {
            Content = "Browse Files…",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Pick .zip mod archives to copy into the game's Mods folder"
        };
        browseFilesBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select mod archives",
                Filter = "Mod archives (*.zip)|*.zip|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
                TransferModsToGameFolder(dlg.FileNames);
        };
        buttonRow.Children.Add(browseFilesBtn);

        var browseFolderBtn = new Button
        {
            Content = "Browse Folder…",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Pick a mod folder to copy into the game's Mods folder"
        };
        browseFolderBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select a mod folder" };
            if (dlg.ShowDialog(this) == true)
                TransferModsToGameFolder([dlg.FolderName]);
        };
        buttonRow.Children.Add(browseFolderBtn);

        outerStack.Children.Add(buttonRow);

        card.Child = outerStack;
        return card;
    }

    private Border BuildModFolderTransferredCard()
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

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerText = new TextBlock
        {
            Text = "2. Recently Transferred",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerText, 0);
        headerRow.Children.Add(headerText);

        var openFolderBtn = new Button
        {
            Content = "Open Mods Folder",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Open the game's Mods folder in File Explorer"
        };
        openFolderBtn.Click += (_, _) =>
        {
            var modsDir = GetGameModsFolder();
            if (modsDir is null)
            {
                ShowToast("Not Found", "Could not locate the RimWorld Mods folder", ToastService.ToastType.Warning);
                return;
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = modsDir, UseShellExecute = true }); }
            catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); }
        };
        Grid.SetColumn(openFolderBtn, 1);
        headerRow.Children.Add(openFolderBtn);

        headerRow.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(headerRow);

        if (_recentModTransfers.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No mods transferred yet — mods you drop or browse for will be listed here once copied to the Mods folder.",
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var name in _recentModTransfers.AsEnumerable().Reverse().Take(20))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"✓  {name}",
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }
        }

        card.Child = stack;
        return card;
    }

    private string? GetGameModsFolder()
    {
        var gameExe = RimWorldSaveReader.FindGameExePath();
        if (gameExe is null) return null;
        return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(gameExe)!, "Mods");
    }

    /// <summary>Copies dropped/browsed mod folders or .zip archives into the game's Mods folder.</summary>
    private void TransferModsToGameFolder(string[] paths)
    {
        var modsDir = GetGameModsFolder();
        if (modsDir is null)
        {
            ShowToast("Not Found", "Could not locate the RimWorld Mods folder", ToastService.ToastType.Warning);
            return;
        }

        int copied = 0;
        foreach (var path in paths)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    // Copy the mod folder recursively
                    var destDir = System.IO.Path.Combine(modsDir, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')));
                    CopyDirectory(path, destDir);
                    _recentModTransfers.Add(System.IO.Path.GetFileName(destDir));
                    copied++;
                }
                else if (System.IO.File.Exists(path) &&
                         System.IO.Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var destDir = System.IO.Path.Combine(modsDir, System.IO.Path.GetFileNameWithoutExtension(path));
                    System.IO.Compression.ZipFile.ExtractToDirectory(path, destDir, overwriteFiles: true);
                    _recentModTransfers.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                    copied++;
                }
                else
                {
                    ShowToast("Skipped", $"{System.IO.Path.GetFileName(path)} is not a mod folder or .zip archive", ToastService.ToastType.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Transfer Failed", $"{System.IO.Path.GetFileName(path)}: {ex.Message}", ToastService.ToastType.Error);
            }
        }

        if (copied > 0)
        {
            ShowToast("Mods Transferred", $"Copied {copied} mod(s) to the game's Mods folder", ToastService.ToastType.Success);
            if (_activeTab == "Mods" && _activeSidebarItem == "Mod Folder")
                UpdateContentArea();
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        System.IO.Directory.CreateDirectory(destDir);
        foreach (var file in System.IO.Directory.GetFiles(sourceDir))
            System.IO.File.Copy(file, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(file)), overwrite: true);
        foreach (var dir in System.IO.Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(dir)));
    }
}
