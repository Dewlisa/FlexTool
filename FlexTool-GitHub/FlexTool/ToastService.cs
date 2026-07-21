using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FlexTool;

/// <summary>
/// Provides in-app toast notifications that auto-dismiss and a notification history
/// accessible via a header bell button. Toasts do not stack continuously — at most
/// <see cref="MaxVisibleToasts"/> are shown at once, and older ones are replaced.
/// </summary>
public sealed class ToastService
{
    public const int MaxVisibleToasts = 3;
    public const int MaxHistory = 50;
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    public enum ToastType { Info, Success, Warning, Error }

    public sealed record ToastEntry(string Title, string Message, ToastType Type, DateTime Timestamp)
    {
        public bool IsRead { get; set; }
    }

    private readonly StackPanel _toastContainer;
    private readonly List<ToastEntry> _history = [];
    private readonly Action _onHistoryChanged;

    public IReadOnlyList<ToastEntry> History => _history;
    public int UnreadCount => _history.Count(e => !e.IsRead);

    public ToastService(StackPanel toastContainer, Action onHistoryChanged)
    {
        _toastContainer = toastContainer;
        _onHistoryChanged = onHistoryChanged;
    }

    public void Show(string title, string message, ToastType type = ToastType.Info)
    {
        var entry = new ToastEntry(title, message, type, DateTime.Now);
        _history.Insert(0, entry);
        if (_history.Count > MaxHistory) _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
        _onHistoryChanged();

        // Evict oldest visible toast if at capacity
        while (_toastContainer.Children.Count >= MaxVisibleToasts)
            _toastContainer.Children.RemoveAt(0);

        var toast = BuildToast(entry);
        _toastContainer.Children.Add(toast);

        // Auto-dismiss after duration
        var timer = new DispatcherTimer { Interval = DefaultDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DismissToast(toast);
        };
        timer.Start();
    }

    public void MarkAllRead()
    {
        foreach (var e in _history) e.IsRead = true;
        _onHistoryChanged();
    }

    public void ClearHistory()
    {
        _history.Clear();
        _onHistoryChanged();
    }

    private Border BuildToast(ToastEntry entry)
    {
        var (accentColor, icon) = entry.Type switch
        {
            ToastType.Success => (Color.FromRgb(0x2E, 0xCC, 0x71), "✓"),
            ToastType.Warning => (Color.FromRgb(0xF3, 0x9C, 0x12), "⚠"),
            ToastType.Error   => (Color.FromRgb(0xE7, 0x4C, 0x3C), "✕"),
            _                 => (Color.FromRgb(0x3A, 0x7B, 0xD5), "ℹ"),
        };

        var content = new DockPanel { Margin = new Thickness(0) };

        // Icon
        content.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accentColor),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 10, 0)
        });

        // Close button
        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(8, 0, 0, 0)
        };
        closeBtn.Template = CreateCloseBtnTemplate();
        DockPanel.SetDock(closeBtn, Dock.Right);
        content.Children.Add(closeBtn);

        // Text
        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCC))
        });
        if (!string.IsNullOrEmpty(entry.Message))
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = entry.Message,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x78)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        content.Children.Add(textPanel);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 6),
            MaxWidth = 340,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = content,
            RenderTransform = new TranslateTransform(0, 0),
            Opacity = 0
        };

        // Accent strip on the left via a clip trick — use a nested border instead
        var wrapper = new Border
        {
            Margin = new Thickness(0, 0, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    new Border
                    {
                        Width = 3,
                        Background = new SolidColorBrush(accentColor),
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(0, 0, 10, 0),
                    }
                }
            }
        };

        // Actually, just use the card directly with accent via left border
        card.BorderThickness = new Thickness(3, 1, 1, 1);
        card.BorderBrush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(accentColor, 0),
                new(Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B), 0.01),
                new(Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B), 1)
            },
            new Point(0, 0.5), new Point(1, 0.5));

        closeBtn.Click += (_, _) => DismissToast(card);

        // Slide-in + fade-in animation
        var slideIn = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

        card.Loaded += (_, _) =>
        {
            card.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            card.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        return card;
    }

    private void DismissToast(Border toast)
    {
        if (!_toastContainer.Children.Contains(toast)) return;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) =>
        {
            if (_toastContainer.Children.Contains(toast))
                _toastContainer.Children.Remove(toast);
        };
        toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static ControlTemplate CreateCloseBtnTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        factory.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));
        factory.SetValue(Border.CursorProperty, System.Windows.Input.Cursors.Hand);
        factory.Name = "Bd";

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(contentFactory);

        template.VisualTree = factory;

        var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), "Bd"));
        template.Triggers.Add(mouseOverTrigger);

        return template;
    }
}
