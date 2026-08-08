namespace AppleEsports.Desktop;

/// <summary>
/// Lets an operator re-point this PC at a different server without editing JSON by hand.
/// Reached with Ctrl+Shift+S, or from the "Change server…" button on the error screen.
/// </summary>
public sealed class SettingsDialog : Form
{
    private static readonly Color Backdrop = Color.FromArgb(18, 18, 24);
    private static readonly Color Field = Color.FromArgb(32, 32, 42);
    private static readonly Color Foreground = Color.FromArgb(230, 230, 234);
    private static readonly Color Muted = Color.FromArgb(140, 140, 155);
    private static readonly Color Accent = Color.FromArgb(200, 30, 40);

    private readonly TextBox _serverBox = new();
    private readonly TextBox _userBox = new();
    private readonly TextBox _passwordBox = new();

    public string ServerUrl => _serverBox.Text.Trim();
    public string GateUsername => _userBox.Text.Trim();
    public string GatePassword => _passwordBox.Text;

    public SettingsDialog(AppConfig config)
    {
        Text = "Server settings";
        BackColor = Backdrop;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 9.5F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 330);

        try
        {
            if (Environment.ProcessPath is { } exePath)
                Icon = Icon.ExtractAssociatedIcon(exePath);
        }
        catch { /* cosmetic only */ }

        var y = 24;

        AddLabel("Server address", ref y);
        AddHint("The Apple Esports server this PC connects to, for example  140.245.195.222:8081", ref y);
        StyleField(_serverBox, y);
        _serverBox.Text = config.ServerUrl;
        Controls.Add(_serverBox);
        y += 46;

        AddLabel("Dashboard gate username", ref y);
        StyleField(_userBox, y);
        _userBox.Text = config.GateUsername;
        Controls.Add(_userBox);
        y += 46;

        AddLabel("Dashboard gate password", ref y);
        StyleField(_passwordBox, y);
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.Text = config.GatePassword;
        Controls.Add(_passwordBox);
        y += 52;

        var save = new Button
        {
            Text = "Save and reconnect",
            DialogResult = DialogResult.OK,
            Size = new Size(180, 38),
            Location = new Point(ClientSize.Width - 180 - 24, y),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        save.FlatAppearance.BorderSize = 0;

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 38),
            Location = new Point(ClientSize.Width - 180 - 110 - 34, y),
            FlatStyle = FlatStyle.Flat,
            BackColor = Field,
            ForeColor = Foreground,
            Cursor = Cursors.Hand,
        };
        cancel.FlatAppearance.BorderSize = 0;

        Controls.Add(save);
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (ServerUrl.Length != 0) return;

            MessageBox.Show(this, "Please enter a server address.", "Apple Esports",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        };
    }

    private void AddLabel(string text, ref int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            ForeColor = Foreground,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Location = new Point(24, y),
            AutoSize = true,
        });
        y += 22;
    }

    private void AddHint(string text, ref int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            ForeColor = Muted,
            Location = new Point(24, y),
            AutoSize = false,
            Size = new Size(ClientSize.Width - 48, 18),
        });
        y += 22;
    }

    private void StyleField(TextBox box, int y)
    {
        box.Location = new Point(24, y);
        box.Size = new Size(ClientSize.Width - 48, 28);
        box.BackColor = Field;
        box.ForeColor = Foreground;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10F);
    }
}
