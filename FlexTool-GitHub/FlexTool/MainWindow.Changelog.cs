using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FlexTool;

public partial class MainWindow
{
    // ── Changelog Tab Content ───────────────────────────────────────────

    private UIElement BuildChangelogTabContent()
    {
        var root = new StackPanel { Margin = new Thickness(8) };
        root.Children.Add(BuildPreviewReleaseCard());
        return root;
    }

    private Border BuildPreviewReleaseCard()
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

        var titleBlock = new TextBlock
        {
            Text = "Preview Release",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var gradientBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x9B, 0x59, 0xB6), 0.0));
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x7B, 0xD5), 1.0));
        titleBlock.Foreground = gradientBrush;
        stack.Children.Add(titleBlock);

        stack.Children.Add(new TextBlock
        {
            Text = "This is a preview release of FlexTool. Full release notes will appear here starting with the first public version on Nexus.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        card.Child = stack;
        return card;
    }
}
