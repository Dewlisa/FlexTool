using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FlexTool;

/// <summary>
/// A dark-themed modal dialog that replaces system MessageBox
/// to match FlexTool's visual style.
/// </summary>
public sealed class FlexToolDialog : Window
{
    private bool _result;

    private static readonly SolidColorBrush BluePrimaryBrush = new(Color.FromRgb(0x3A, 0x7B, 0xD5));
    private static readonly SolidColorBrush BlueLightBrush = new(Color.FromRgb(0x5A, 0x9A, 0xE6));
    private static readonly SolidColorBrush PanelDarkBrush = new(Color.FromRgb(0x0E, 0x0E, 0x10));
    private static readonly SolidColorBrush BorderBlueBrush = new(Color.FromRgb(0x2A, 0x2A, 0x2E));
    private static readonly SolidColorBrush TextPrimaryBrush = new(Color.FromRgb(0xC8, 0xC8, 0xCC));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xFA, 0xCC, 0x15));
    private static readonly SolidColorBrush SubtleBrush = new(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush SubtleHoverBrush = new(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    private FlexToolDialog() { }

    /// <summary>
    /// Shows a themed Yes/No warning dialog. Returns true if the user clicked Yes.
    /// </summary>
    public static bool ShowWarning(Window owner, string title, string message)
    {
        var dialog = new FlexToolDialog
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            MinWidth = 380,
            MaxWidth = 460
        };

        dialog.Content = dialog.BuildContent(title, message);
        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>
    /// Shows a themed informational dialog with a single OK button.
    /// </summary>
    public static void ShowInfo(Window owner, string title, string message)
    {
        var dialog = new FlexToolDialog
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            MinWidth = 380,
            MaxWidth = 520
        };

        dialog.Content = dialog.BuildContent(title, message, okOnly: true);
        dialog.ShowDialog();
    }

    /// <summary>
    /// Shows a themed text-input dialog. Returns the entered text,
    /// or null if the user cancelled.
    /// </summary>
    public static string? ShowInput(Window owner, string title, string message, string initialText = "")
    {
        var dialog = new FlexToolDialog
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            MinWidth = 380,
            MaxWidth = 460
        };

        var inputBox = new TextBox
        {
            Text = initialText,
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
            Foreground = TextPrimaryBrush,
            BorderBrush = BorderBlueBrush,
            BorderThickness = new Thickness(1),
            CaretBrush = TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 20)
        };
        inputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                dialog._result = true;
                dialog.Close();
            }
        };

        dialog.Content = dialog.BuildContent(title, message, okOnly: false, input: inputBox);
        dialog.Loaded += (_, _) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };
        dialog.ShowDialog();
        return dialog._result ? inputBox.Text : null;
    }

    private UIElement BuildContent(string title, string message, bool okOnly = false, TextBox? input = null)
    {
        var outerBorder = new Border
        {
            BorderBrush = BluePrimaryBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Background = PanelDarkBrush,
            Margin = new Thickness(8),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x3A, 0x7B, 0xD5),
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.25
            }
        };

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // Title bar with warning icon (draggable)
        var titleBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };
        titleBar.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (Exception __ex) { System.Diagnostics.Debug.WriteLine($"Swallowed: {__ex.Message}"); } };

        var iconCanvas = new Canvas { Width = 24, Height = 24 };
        iconCanvas.Children.Add(new Path
        {
            Fill = WarningBrush,
            Data = Geometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z")
        });
        titleBar.Children.Add(new Viewbox
        {
            Width = 22, Height = 22,
            Margin = new Thickness(0, 0, 10, 0),
            Child = iconCanvas
        });

        titleBar.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });

        stack.Children.Add(titleBar);

        // Separator
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = BorderBlueBrush,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // Message
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, input is null ? 24 : 12),
            LineHeight = 20
        });

        if (input is not null)
            stack.Children.Add(input);

        // Buttons
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (okOnly)
        {
            btnRow.Children.Add(BuildButton("OK", isAccept: true));
        }
        else if (input is not null)
        {
            var cancelBtn = BuildButton("Cancel", isAccept: false);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(BuildButton("OK", isAccept: true));
        }
        else
        {
            var noBtn = BuildButton("No", isAccept: false);
            noBtn.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(noBtn);
            btnRow.Children.Add(BuildButton("Yes", isAccept: true));
        }

        stack.Children.Add(btnRow);
        outerBorder.Child = stack;
        return outerBorder;
    }

    private Button BuildButton(string text, bool isAccept)
    {
        var bgNormal = isAccept ? BluePrimaryBrush : SubtleBrush;
        var bgHover = isAccept ? BlueLightBrush : SubtleHoverBrush;

        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border), "Bd");
        borderFactory.SetValue(Border.BackgroundProperty, (Brush)bgNormal);
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(20, 8, 20, 8));

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentFactory);
        template.VisualTree = borderFactory;

        var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, (Brush)bgHover, "Bd"));
        template.Triggers.Add(hoverTrigger);

        var btn = new Button
        {
            Content = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Cursor = Cursors.Hand,
            Template = template
        };

        btn.Click += (_, _) =>
        {
            _result = isAccept;
            Close();
        };

        return btn;
    }
}
