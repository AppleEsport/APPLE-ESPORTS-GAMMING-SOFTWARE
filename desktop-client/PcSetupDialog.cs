namespace AppleEsports.Desktop;

/// <summary>
/// Sets which PC this machine is, and whether it is the operator's counter PC or a
/// customer-facing gaming PC. Reached with Ctrl+Shift+P, behind the admin PIN.
/// </summary>
public sealed class PcSetupDialog : Form
{
    private static readonly Color Backdrop = Color.FromArgb(18, 18, 24);
    private static readonly Color Field = Color.FromArgb(32, 32, 42);
    private static readonly Color Foreground = Color.FromArgb(230, 230, 234);
    private static readonly Color Muted = Color.FromArgb(140, 140, 155);
    private static readonly Color Accent = Color.FromArgb(200, 30, 40);

    private readonly TextBox _pcNumberBox = new();
    private readonly RadioButton _operatorRadio = new();
    private readonly RadioButton _userRadio = new();

    public string PcNumber => _pcNumberBox.Text.Trim();
    public string Role => _userRadio.Checked ? "user" : "operator";

    public PcSetupDialog(AppConfig config)
    {
        Text = "PC setup";
        BackColor = Backdrop;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 9.5F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 320);

        try
        {
            if (Environment.ProcessPath is { } exePath)
                Icon = Icon.ExtractAssociatedIcon(exePath);
        }
        catch { /* cosmetic only */ }

        Controls.Add(new Label
        {
            Text = "PC number",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Location = new Point(24, 24),
            AutoSize = true,
        });
        Controls.Add(new Label
        {
            Text = "Which seat is this machine, for example  PC-1",
            ForeColor = Muted,
            Location = new Point(24, 46),
            Size = new Size(ClientSize.Width - 48, 18),
        });

        _pcNumberBox.Location = new Point(24, 70);
        _pcNumberBox.Size = new Size(ClientSize.Width - 48, 30);
        _pcNumberBox.BackColor = Field;
        _pcNumberBox.ForeColor = Foreground;
        _pcNumberBox.BorderStyle = BorderStyle.FixedSingle;
        _pcNumberBox.Font = new Font("Segoe UI", 11F);
        _pcNumberBox.Text = config.PcNumber;
        Controls.Add(_pcNumberBox);

        Controls.Add(new Label
        {
            Text = "What is this machine for?",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Location = new Point(24, 122),
            AutoSize = true,
        });

        _operatorRadio.Text = "Operator counter PC";
        _operatorRadio.Location = new Point(24, 150);
        _operatorRadio.Size = new Size(ClientSize.Width - 48, 24);
        _operatorRadio.ForeColor = Foreground;
        _operatorRadio.Checked = !config.IsUserPc;
        Controls.Add(_operatorRadio);

        Controls.Add(new Label
        {
            Text = "Full dashboard. Staff can close the app normally.",
            ForeColor = Muted,
            Location = new Point(44, 172),
            Size = new Size(ClientSize.Width - 68, 18),
        });

        _userRadio.Text = "Customer gaming PC";
        _userRadio.Location = new Point(24, 200);
        _userRadio.Size = new Size(ClientSize.Width - 48, 24);
        _userRadio.ForeColor = Foreground;
        _userRadio.Checked = config.IsUserPc;
        Controls.Add(_userRadio);

        Controls.Add(new Label
        {
            Text = "Locked down: no close button, Alt+F4 blocked. Needs the admin PIN to get out.",
            ForeColor = Muted,
            Location = new Point(44, 222),
            Size = new Size(ClientSize.Width - 68, 34),
        });

        var save = new Button
        {
            Text = "Save and restart",
            DialogResult = DialogResult.OK,
            Size = new Size(160, 38),
            Location = new Point(ClientSize.Width - 160 - 24, 262),
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
            Location = new Point(ClientSize.Width - 160 - 110 - 34, 262),
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
            if (PcNumber.Length != 0) return;

            MessageBox.Show(this, "Please enter a PC number.", "Apple Esports",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        };
    }
}
