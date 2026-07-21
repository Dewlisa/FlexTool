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
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Notification bell & toast helpers ────────────────────────

    private void NotificationBell_Click(object sender, RoutedEventArgs e)
    {
        if (NotificationHistoryPanel.Visibility == Visibility.Visible)
        {
            NotificationHistoryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RefreshNotificationHistoryList();
        NotificationHistoryPanel.Visibility = Visibility.Visible;
        _toastService?.MarkAllRead();
    }

    private void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        _toastService?.MarkAllRead();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _toastService?.ClearHistory();
        NotificationHistoryList.Children.Clear();
        NotificationHistoryPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateNotificationBadge()
    {
        var count = _toastService?.UnreadCount ?? 0;
        NotificationBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationBadgeText.Text = count > 99 ? "99+" : count.ToString();

        if (NotificationHistoryPanel.Visibility == Visibility.Visible)
            RefreshNotificationHistoryList();
    }

    private void RefreshNotificationHistoryList()
    {
        NotificationHistoryList.Children.Clear();
        var history = _toastService?.History;
        if (history is null || history.Count == 0)
        {
            NotificationHistoryList.Children.Add(new TextBlock
            {
                Text = "No notifications yet",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(6, 12, 6, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var entry in history)
        {
            var (accentColor, icon) = entry.Type switch
            {
                ToastService.ToastType.Success => (Color.FromRgb(0x2E, 0xCC, 0x71), "✓"),
                ToastService.ToastType.Warning => (Color.FromRgb(0xF3, 0x9C, 0x12), "⚠"),
                ToastService.ToastType.Error   => (Color.FromRgb(0xE7, 0x4C, 0x3C), "✕"),
                _                              => (Color.FromRgb(0x3A, 0x7B, 0xD5), "ℹ"),
            };

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };

            row.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 8, 0)
            });

            var timeAgo = FormatTimeAgo(entry.Timestamp);
            var timeBlock = new TextBlock
            {
                Text = timeAgo,
                FontSize = 9,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 3, 0, 0)
            };
            DockPanel.SetDock(timeBlock, Dock.Right);
            row.Children.Add(timeBlock);

            var textPanel = new StackPanel();
            textPanel.Children.Add(new TextBlock
            {
                Text = entry.Title,
                FontSize = 11,
                FontWeight = entry.IsRead ? FontWeights.Normal : FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            });
            if (!string.IsNullOrEmpty(entry.Message))
            {
                textPanel.Children.Add(new TextBlock
                {
                    Text = entry.Message,
                    FontSize = 10,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            row.Children.Add(textPanel);

            var card = new Border
            {
                Background = entry.IsRead
                    ? Brushes.Transparent
                    : new SolidColorBrush(Color.FromArgb(0x0C, 0x3A, 0x7B, 0xD5)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Child = row
            };
            NotificationHistoryList.Children.Add(card);
        }
    }

    private static string FormatTimeAgo(DateTime timestamp)
    {
        var elapsed = DateTime.Now - timestamp;
        if (elapsed.TotalSeconds < 60) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }

    private void ShowToast(string title, string message = "",
        ToastService.ToastType type = ToastService.ToastType.Info)
    {
        _toastService?.Show(title, message, type);
    }

    // --- Native resize support for borderless window ---


    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const int ResizeBorder = 8;

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            var screenPoint = new System.Windows.Point(
                (short)(lParam.ToInt32() & 0xFFFF),
                (short)(lParam.ToInt32() >> 16));
            var windowPoint = PointFromScreen(screenPoint);

            double w = ActualWidth;
            double h = ActualHeight;

            bool left = windowPoint.X < ResizeBorder;
            bool right = windowPoint.X > w - ResizeBorder;
            bool top = windowPoint.Y < ResizeBorder;
            bool bottom = windowPoint.Y > h - ResizeBorder;

            if (top && left) { handled = true; return HTTOPLEFT; }
            if (top && right) { handled = true; return HTTOPRIGHT; }
            if (bottom && left) { handled = true; return HTBOTTOMLEFT; }
            if (bottom && right) { handled = true; return HTBOTTOMRIGHT; }
            if (left) { handled = true; return HTLEFT; }
            if (right) { handled = true; return HTRIGHT; }
            if (top) { handled = true; return HTTOP; }
            if (bottom) { handled = true; return HTBOTTOM; }
        }

        return nint.Zero;
    }
}
