using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AppleEsportsErp.ClientAgent.Views;

/// <summary>
/// Asks for the admin PIN. Built in code rather than XAML deliberately: it has to be able to
/// appear over the lock screen, which is topmost and owns the whole screen, and keeping it in
/// one file makes it obvious that nothing here reads or stores the PIN itself — it hands the
/// typed characters straight back to <see cref="Services.AdminPinService"/> and forgets them.
/// </summary>
public sealed class PinPromptDialog : Window
{
    private static readonly Brush Backdrop = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x1A));
    private static readonly Brush Field = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
    private static readonly Brush Edge = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x4E));
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEA));
    private static readonly Brush InkMuted = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x6C, 0x3C, 0xE0));

    private readonly PasswordBox _pin = new();

    /// <summary>What was typed. Only meaningful when ShowDialog returned true.</summary>
    public string Pin { get; private set; } = string.Empty;

    public PinPromptDialog(string action)
    {
        Title = "Admin PIN";
        Background = Backdrop;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // The lock screen is topmost and covers everything. A prompt that opens behind it is a
        // machine that appears to have frozen.
        Topmost = true;

        var stack = new StackPanel { Margin = new Thickness(28) };

        stack.Children.Add(new TextBlock
        {
            Text = "Admin PIN",
            Foreground = Ink,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"Enter the admin PIN to {action}.",
            Foreground = InkMuted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18),
        });

        _pin.Background = Field;
        _pin.Foreground = Ink;
        _pin.CaretBrush = Ink;
        _pin.BorderBrush = Edge;
        _pin.BorderThickness = new Thickness(1);
        _pin.FontSize = 22;
        _pin.Padding = new Thickness(10, 8, 10, 8);
        _pin.MaxLength = 32;
        stack.Children.Add(_pin);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };

        var cancel = Button("Cancel", Field, InkMuted);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;

        var ok = Button("Unlock", Accent, Ink);
        ok.IsDefault = true;
        ok.Margin = new Thickness(10, 0, 0, 0);
        ok.Click += (_, _) => Submit();

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        Content = new System.Windows.Controls.Border
        {
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            Child = stack,
        };

        Loaded += (_, _) =>
        {
            Activate();
            _pin.Focus();
        };
    }

    private void Submit()
    {
        Pin = _pin.Password;
        DialogResult = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private static Button Button(string text, Brush background, Brush foreground) => new()
    {
        Content = text,
        Background = background,
        Foreground = foreground,
        BorderBrush = Edge,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(20, 8, 20, 8),
        FontSize = 14,
        Cursor = Cursors.Hand,
    };
}
