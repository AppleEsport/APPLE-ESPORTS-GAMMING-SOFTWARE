namespace AppleEsports.Desktop;

/// <summary>
/// Asks for the admin PIN before anything that could unbind this machine or let a customer
/// out to the Windows desktop.
/// </summary>
public sealed class PinPromptDialog : Form
{
    private static readonly Color Backdrop = Color.FromArgb(18, 18, 24);
    private static readonly Color Field = Color.FromArgb(32, 32, 42);
    private static readonly Color Foreground = Color.FromArgb(230, 230, 234);
    private static readonly Color Muted = Color.FromArgb(140, 140, 155);
    private static readonly Color Accent = Color.FromArgb(200, 30, 40);

    private readonly TextBox _pinBox = new();

    public string Pin => _pinBox.Text;

    public PinPromptDialog(string action)
    {
        Text = "Admin PIN required";
        BackColor = Backdrop;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 9.5F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 190);
        TopMost = true;

        try
        {
            if (Environment.ProcessPath is { } exePath)
                Icon = Icon.ExtractAssociatedIcon(exePath);
        }
        catch { /* cosmetic only */ }

        Controls.Add(new Label
        {
            Text = $"Enter the admin PIN to {action}.",
            ForeColor = Muted,
            Location = new Point(24, 24),
            Size = new Size(ClientSize.Width - 48, 40),
        });

        _pinBox.Location = new Point(24, 72);
        _pinBox.Size = new Size(ClientSize.Width - 48, 30);
        _pinBox.BackColor = Field;
        _pinBox.ForeColor = Foreground;
        _pinBox.BorderStyle = BorderStyle.FixedSingle;
        _pinBox.Font = new Font("Segoe UI", 13F);
        _pinBox.UseSystemPasswordChar = true;
        _pinBox.TextAlign = HorizontalAlignment.Center;
        Controls.Add(_pinBox);

        var ok = new Button
        {
            Text = "Unlock",
            DialogResult = DialogResult.OK,
            Size = new Size(130, 38),
            Location = new Point(ClientSize.Width - 130 - 24, 122),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        ok.FlatAppearance.BorderSize = 0;

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 38),
            Location = new Point(ClientSize.Width - 130 - 110 - 34, 122),
            FlatStyle = FlatStyle.Flat,
            BackColor = Field,
            ForeColor = Foreground,
            Cursor = Cursors.Hand,
        };
        cancel.FlatAppearance.BorderSize = 0;

        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) => _pinBox.Focus();
    }
}
