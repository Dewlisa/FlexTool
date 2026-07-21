using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FlexTool;

public partial class MainWindow
{
    // ── Support Tab Content ─────────────────────────────────

    private UIElement BuildSupportTabContent()
    {
        var root = new StackPanel { Margin = new Thickness(8) };

        root.Children.Add(BuildSupportHeaderCard());
        root.Children.Add(BuildSupportContactCard());
        root.Children.Add(BuildSupportUpdatesCard());
        root.Children.Add(BuildSupportRequirementsCard());

        return root;
    }

    private Border BuildSupportHeaderCard()
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
            Text = "Support",
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
        gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x7B, 0xD5), 1.0));
        titleBlock.Foreground = gradientBrush;
        stack.Children.Add(titleBlock);

        stack.Children.Add(new TextBlock
        {
            Text = "Help, reporting problems and requirements for FlexTool.",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        card.Child = stack;
        return card;
    }

    private Border BuildSupportContactCard()
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
            Text = "Report a Problem",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "If you run into any problems, report them on the Nexus mod page or message me on Discord:",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var discordRow = new StackPanel { Orientation = Orientation.Horizontal };
        discordRow.Children.Add(new TextBlock
        {
            Text = "Discord: ",
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        discordRow.Children.Add(new TextBlock
        {
            Text = "dew.xex",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(discordRow);

        card.Child = stack;
        return card;
    }

    private Border BuildSupportUpdatesCard()
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
            Text = "Updates",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Any and all updates are on Nexus.",
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        card.Child = stack;
        return card;
    }

    private Border BuildSupportRequirementsCard()
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
            Text = "Requirements",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BlueLightBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "FlexTool requires the .NET 10 runtime (Windows only). If the tool doesn't start, install it from the link below:",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var linkBtn = new Button
        {
            Content = "Download .NET 10 Runtime",
            Padding = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            ToolTip = "Opens dotnet.microsoft.com in your browser"
        };
        linkBtn.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/download/dotnet/10.0")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open .NET download page: {ex.Message}");
                ShowToast("Support", "Could not open the browser. Go to dotnet.microsoft.com/download/dotnet/10.0", ToastService.ToastType.Warning);
            }
        };
        stack.Children.Add(linkBtn);

        card.Child = stack;
        return card;
    }
}
