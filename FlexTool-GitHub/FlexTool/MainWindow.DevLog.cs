using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FlexTool;

public partial class MainWindow
{
    // ── Dev Log Tab Content ─────────────────────────────────────────────
    // Live mirror of RimWorld's dev log (messages, warnings, errors,
    // exceptions with stack traces) streamed from the FlexTool Debug Info
    // mod DLL via FlexToolDevLog.txt.

    private TextBox? _devLogBox;
    private System.Windows.Threading.DispatcherTimer? _devLogTimer;
    private int _devLogLastLength = -1;
    private bool _devLogAutoScroll = true;

    private UIElement BuildDevLogTabContent()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildDevLogHeaderCard();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var logCard = new Border
        {
            Background = (Brush)FindResource("PanelDarkBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4)
        };

        _devLogBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8)
        };
        logCard.Child = _devLogBox;
        Grid.SetRow(logCard, 1);
        root.Children.Add(logCard);

        RefreshDevLog(force: true);
        StartDevLogTimer();

        return root;
    }

    private Border BuildDevLogHeaderCard()
    {
        var card = new Border
        {
            Background = (Brush)FindResource("PanelMidBrush"),
            BorderBrush = (Brush)FindResource("BorderBlueBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();

        var titleBlock = new TextBlock
        {
            Text = "Dev Log",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF3, 0x9C, 0x12), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE7, 0x4C, 0x3C), 1.0));
        titleBlock.Foreground = gradientBrush;
        stack.Children.Add(titleBlock);

        stack.Children.Add(new TextBlock
        {
            Text = "Live mirror of RimWorld's dev log — everything the in-game log receives (messages, warnings, errors and exception stack traces). " +
                   "Requires the FlexTool Debug Info mod to be installed and the game running.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };

        var refreshBtn = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0)
        };
        refreshBtn.Click += (_, _) => RefreshDevLog(force: true);
        btnRow.Children.Add(refreshBtn);

        var clearBtn = new Button
        {
            Content = "Clear",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Clears the mirrored dev log file"
        };
        clearBtn.Click += (_, _) =>
        {
            try
            {
                System.IO.File.WriteAllText(RimWorldSaveReader.DevLogPath, "");
                RefreshDevLog(force: true);
                ShowToast("Dev Log", "Dev log cleared.", ToastService.ToastType.Info);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dev log clear failed: {ex.Message}");
                ShowToast("Dev Log", "Could not clear the dev log.", ToastService.ToastType.Error);
            }
        };
        btnRow.Children.Add(clearBtn);

        var autoScrollCheck = new CheckBox
        {
            Content = "Auto-scroll",
            IsChecked = _devLogAutoScroll,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        autoScrollCheck.Checked += (_, _) => _devLogAutoScroll = true;
        autoScrollCheck.Unchecked += (_, _) => _devLogAutoScroll = false;
        btnRow.Children.Add(autoScrollCheck);

        stack.Children.Add(btnRow);

        card.Child = stack;
        return card;
    }

    private void StartDevLogTimer()
    {
        StopDevLogTimer();
        _devLogTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _devLogTimer.Tick += (_, _) => RefreshDevLog(force: false);
        _devLogTimer.Start();
    }

    private void StopDevLogTimer()
    {
        _devLogTimer?.Stop();
        _devLogTimer = null;
    }

    private void RefreshDevLog(bool force)
    {
        if (_devLogBox is null) return;
        // Stop polling when the page has been swapped out of the content area.
        if (!_devLogBox.IsLoaded && !force && _devLogTimer != null && _devLogLastLength >= 0)
        {
            StopDevLogTimer();
            return;
        }

        var text = RimWorldSaveReader.ReadDevLogTail();
        if (string.IsNullOrEmpty(text))
            text = "No dev log data yet.\n\nInstall the FlexTool Debug Info mod and launch RimWorld — the game's dev log will stream here live.";

        if (!force && text.Length == _devLogLastLength) return;
        _devLogLastLength = text.Length;

        _devLogBox.Text = text;
        if (_devLogAutoScroll)
        {
            _devLogBox.CaretIndex = _devLogBox.Text.Length;
            _devLogBox.ScrollToEnd();
        }
    }
}
