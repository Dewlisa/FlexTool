using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FlexTool;

public partial class MainWindow
{
    // ── Pawn Extractor Tab Content ──────────────────────────────────────
    // LIVE extraction through the Debug Info mod DLL:
    //   • Extract: you must be IN the save you want to pull the pawn from —
    //     the running game serializes the colonist to the pawn library.
    //   • Spawn: you must be IN the save you want the pawn spawned into —
    //     the running game deserializes the pawn and drops it near the colony.

    private StackPanel? _pawnExtractorRoot;
    private DateTime _lastPawnResultUtc = DateTime.MinValue;
    private System.Windows.Threading.DispatcherTimer? _pawnResultTimer;

    private UIElement BuildPawnExtractorTabContent()
    {
        _pawnExtractorRoot = new StackPanel { Margin = new Thickness(8) };

        _pawnExtractorRoot.Children.Add(BuildPawnExtractorHeaderCard());
        _pawnExtractorRoot.Children.Add(BuildExtractPawnCard());
        _pawnExtractorRoot.Children.Add(BuildPawnLibraryCard());
        _pawnExtractorRoot.Children.Add(BuildSpawnPawnCard());

        StartPawnResultWatcher();
        return _pawnExtractorRoot;
    }

    private void RefreshPawnExtractorTab()
    {
        if (_pawnExtractorRoot is null) return;
        _pawnExtractorRoot.Children.Clear();
        _pawnExtractorRoot.Children.Add(BuildPawnExtractorHeaderCard());
        _pawnExtractorRoot.Children.Add(BuildExtractPawnCard());
        _pawnExtractorRoot.Children.Add(BuildPawnLibraryCard());
        _pawnExtractorRoot.Children.Add(BuildSpawnPawnCard());
    }

    /// <summary>Polls the in-game result file so toasts appear when the mod finishes.</summary>
    private void StartPawnResultWatcher()
    {
        if (_pawnResultTimer != null) return;
        // Skip results that predate this session
        var initial = RimWorldSaveReader.ReadLivePawnResult();
        if (initial is { } init) _lastPawnResultUtc = init.Utc;

        _pawnResultTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pawnResultTimer.Tick += (_, _) =>
        {
            var result = RimWorldSaveReader.ReadLivePawnResult();
            if (result is not { } r || r.Utc <= _lastPawnResultUtc) return;
            _lastPawnResultUtc = r.Utc;

            if (r.Status == "ok")
            {
                ShowToast("Pawn Extractor", r.Detail, ToastService.ToastType.Success);
                RefreshPawnExtractorTab();
            }
            else
            {
                ShowToast("Pawn Extractor", r.Detail, ToastService.ToastType.Error);
            }
        };
        _pawnResultTimer.Start();
    }

    private Border BuildPawnExtractorHeaderCard()
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
            Text = "Pawn Extractor (Live)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0xCC, 0x71), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x9B, 0x59, 0xB6), 1.0));
        titleBlock.Foreground = gradientBrush;
        stack.Children.Add(titleBlock);

        stack.Children.Add(new TextBlock
        {
            Text = "Version: Beta",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Move colonists between saves — LIVE, through the FlexTool Debug Info mod (no save-file editing). " +
                   "To EXTRACT: the game must be running and you must be playing the save you want to pull the pawn from. " +
                   "To SPAWN: the game must be running and you must be playing the save you want the pawn spawned into. " +
                   "Extracted pawns are stored in your pawn library and can be spawned into any save. " +
                   "Requires the Debug Info mod (Mod Manager page).",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        card.Child = stack;
        return card;
    }

    private Grid MakePawnRow(string label, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 320 });

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);

        row.Children.Add(lbl);
        row.Children.Add(control);
        return row;
    }

    private Border BuildExtractPawnCard()
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
            Text = "1. Extract From the Loaded Save",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Be IN the save you want to pull the pawn from, then enter the colonist's name (short name, e.g. \"Sparks\").",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var nameBox = new TextBox
        {
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12,
            Background = (Brush)FindResource("PanelDarkBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            ToolTip = "Colonist name as it appears in-game"
        };
        stack.Children.Add(MakePawnRow("Colonist", nameBox));

        var extractBtn = new Button
        {
            Content = "Extract Pawn (Live)",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Asks the running game to save this colonist to the pawn library"
        };
        extractBtn.Click += (_, _) =>
        {
            var pawnName = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(pawnName))
            {
                ShowToast("Pawn Extractor", "Enter the colonist's name first.", ToastService.ToastType.Warning);
                return;
            }
            if (RimWorldSaveReader.SendLivePawnExtract(pawnName))
                ShowToast("Pawn Extractor", $"Extract request sent for \"{pawnName}\" — make sure you're in the source save.", ToastService.ToastType.Info);
            else
                ShowToast("Pawn Extractor", "Failed to send the extract request.", ToastService.ToastType.Error);
        };
        stack.Children.Add(extractBtn);

        card.Child = stack;
        return card;
    }

    private Border BuildPawnLibraryCard()
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
            Text = "2. Extracted Pawn Library",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var pawns = RimWorldSaveReader.GetLiveExtractedPawns();
        if (pawns.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No pawns extracted yet — extracted colonists will be listed here, ready to spawn into the save you're playing.",
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                FontSize = 13,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            foreach (var pawn in pawns)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = pawn.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimaryBrush")
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"extracted {pawn.Extracted:g}",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextSecondaryBrush")
                });
                Grid.SetColumn(info, 0);
                row.Children.Add(info);

                var deleteBtn = new Button
                {
                    Content = "Delete",
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11,
                    Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    FontWeight = FontWeights.SemiBold,
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Remove this pawn from the library"
                };
                deleteBtn.Click += (_, _) =>
                {
                    if (RimWorldSaveReader.DeleteExtractedPawn(pawn.FilePath))
                    {
                        ShowToast("Pawn Extractor", $"Deleted \"{pawn.Name}\" from the library.", ToastService.ToastType.Info);
                        RefreshPawnExtractorTab();
                    }
                };
                Grid.SetColumn(deleteBtn, 1);
                row.Children.Add(deleteBtn);

                stack.Children.Add(row);
            }
        }

        card.Child = stack;
        return card;
    }

    private Border BuildSpawnPawnCard()
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
            Text = "3. Spawn Into the Loaded Save",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Be IN the save you want the pawn spawned into, then pick a pawn and hit spawn — it drops in near your colony.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var pawnCb = new ComboBox
        {
            Style = FindResource("DarkComboBoxStyle") as Style,
            FontSize = 12
        };
        var pawns = RimWorldSaveReader.GetLiveExtractedPawns();
        foreach (var p in pawns) pawnCb.Items.Add(p.Name);
        if (pawnCb.Items.Count > 0) pawnCb.SelectedIndex = 0;
        stack.Children.Add(MakePawnRow("Pawn", pawnCb));

        var spawnBtn = new Button
        {
            Content = "Spawn Selected Pawn (Live)",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Asks the running game to spawn this pawn into the loaded save"
        };
        spawnBtn.Click += (_, _) =>
        {
            if (pawnCb.SelectedIndex < 0)
            {
                ShowToast("Pawn Extractor", "Select a pawn from the library first.", ToastService.ToastType.Warning);
                return;
            }

            var pawn = pawns[pawnCb.SelectedIndex];
            if (RimWorldSaveReader.SendLivePawnSpawn(pawn.FilePath))
                ShowToast("Pawn Extractor", $"Spawn request sent for \"{pawn.Name}\" — make sure you're in the target save.", ToastService.ToastType.Info);
            else
                ShowToast("Pawn Extractor", "Failed to send the spawn request.", ToastService.ToastType.Error);
        };
        stack.Children.Add(spawnBtn);

        card.Child = stack;
        return card;
    }
}
