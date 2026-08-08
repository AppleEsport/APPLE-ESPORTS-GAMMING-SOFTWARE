namespace AppleEsports.Desktop;

/// <summary>
/// What staff see the first time Apple Esports runs on a new machine.
///
/// Deliberately one screen rather than a multi-step wizard: there are only four decisions,
/// and each one narrows the next, so showing them together makes the whole setup legible at
/// a glance. Nothing is saved until Head Office has confirmed the claim, so a half-finished
/// setup leaves no misleading state behind.
/// </summary>
public sealed class SetupWizard : Form
{
    private static readonly Color Backdrop = Color.FromArgb(14, 14, 20);
    private static readonly Color Field = Color.FromArgb(32, 32, 42);
    private static readonly Color Foreground = Color.FromArgb(230, 230, 234);
    private static readonly Color Muted = Color.FromArgb(140, 140, 155);
    private static readonly Color Accent = Color.FromArgb(200, 30, 40);
    private static readonly Color Good = Color.FromArgb(60, 180, 110);

    private readonly AppConfig _config;

    private readonly TextBox _serverBox = new();
    private readonly Button _connectButton = new();
    private readonly Label _connectStatus = new();

    private readonly ComboBox _branchBox = new();
    private readonly ComboBox _pcBox = new();
    private readonly RadioButton _operatorRadio = new();
    private readonly RadioButton _userRadio = new();
    private readonly TextBox _pinBox = new();
    private readonly Button _finishButton = new();
    private readonly Label _finishStatus = new();

    public SetupWizard(AppConfig config)
    {
        _config = config;

        Text = "Apple Esports — Set up this PC";
        BackColor = Backdrop;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 9.5F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 610);

        try
        {
            if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch { /* cosmetic only */ }

        Build();
    }

    private void Build()
    {
        var y = 22;

        var title = new Label
        {
            Text = "APPLE ESPORTS",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(24, y),
            AutoSize = true,
        };
        Controls.Add(title);
        y += 34;

        Controls.Add(new Label
        {
            Text = "This PC has not been set up yet. It cannot take customers until it has.",
            ForeColor = Muted,
            Location = new Point(24, y),
            Size = new Size(ClientSize.Width - 48, 20),
        });
        y += 40;

        // ── 1. Server ──
        AddStep("1.  Where is the server?", ref y);
        StyleField(_serverBox, y, ClientSize.Width - 48 - 130);
        _serverBox.Text = _config.ServerUrl;
        Controls.Add(_serverBox);

        _connectButton.Text = "Connect";
        _connectButton.Location = new Point(ClientSize.Width - 24 - 120, y);
        _connectButton.Size = new Size(120, 30);
        StyleButton(_connectButton, Accent, Color.White);
        _connectButton.Click += async (_, _) => await ConnectAsync();
        Controls.Add(_connectButton);
        y += 36;

        _connectStatus.ForeColor = Muted;
        _connectStatus.Location = new Point(24, y);
        _connectStatus.Size = new Size(ClientSize.Width - 48, 20);
        Controls.Add(_connectStatus);
        y += 34;

        // ── 2. Branch ──
        AddStep("2.  Which branch is this PC in?", ref y);
        StyleCombo(_branchBox, y);
        _branchBox.SelectedIndexChanged += async (_, _) => await LoadPcsAsync();
        Controls.Add(_branchBox);
        y += 46;

        // ── 3. Seat ──
        AddStep("3.  Which PC is this machine?", ref y);
        StyleCombo(_pcBox, y);
        Controls.Add(_pcBox);
        y += 46;

        // ── 4. Role ──
        AddStep("4.  What is this machine for?", ref y);

        _operatorRadio.Text = "Operator counter PC  —  full dashboard, staff can close it";
        _operatorRadio.Location = new Point(24, y);
        _operatorRadio.Size = new Size(ClientSize.Width - 48, 24);
        _operatorRadio.ForeColor = Foreground;
        _operatorRadio.Checked = true;
        Controls.Add(_operatorRadio);
        y += 26;

        _userRadio.Text = "Customer gaming PC  —  locked, no close button";
        _userRadio.Location = new Point(24, y);
        _userRadio.Size = new Size(ClientSize.Width - 48, 24);
        _userRadio.ForeColor = Foreground;
        Controls.Add(_userRadio);
        y += 38;

        Controls.Add(new Label
        {
            Text = "Admin PIN  (needed to change or undo this setup later)",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Location = new Point(24, y),
            AutoSize = true,
        });
        y += 22;

        StyleField(_pinBox, y, 200);
        _pinBox.UseSystemPasswordChar = true;
        _pinBox.Text = _config.AdminPin;
        Controls.Add(_pinBox);
        y += 44;

        _finishStatus.ForeColor = Muted;
        _finishStatus.Location = new Point(24, y);
        _finishStatus.Size = new Size(ClientSize.Width - 48, 36);
        Controls.Add(_finishStatus);
        y += 42;

        _finishButton.Text = "Finish setup";
        _finishButton.Location = new Point(ClientSize.Width - 24 - 180, y);
        _finishButton.Size = new Size(180, 40);
        StyleButton(_finishButton, Accent, Color.White);
        _finishButton.Enabled = false;
        _finishButton.Click += async (_, _) => await FinishAsync();
        Controls.Add(_finishButton);

        SetStepsEnabled(false);
    }

    // ── Behaviour ──

    private async Task ConnectAsync()
    {
        var url = _serverBox.Text.Trim();
        if (url.Length == 0)
        {
            _connectStatus.ForeColor = Accent;
            _connectStatus.Text = "Enter the server address first.";
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }

        _connectButton.Enabled = false;
        _connectStatus.ForeColor = Muted;
        _connectStatus.Text = "Connecting…";

        try
        {
            using var client = new HeadOfficeClient(url, _config.GateUsername, _config.GatePassword);
            var (reachable, message) = await client.PingAsync();

            if (!reachable)
            {
                _connectStatus.ForeColor = Accent;
                _connectStatus.Text = message;
                SetStepsEnabled(false);
                return;
            }

            var branches = await client.GetBranchesAsync();
            _branchBox.Items.Clear();
            foreach (var branch in branches) _branchBox.Items.Add(branch);

            _serverBox.Text = url;
            _connectStatus.ForeColor = Good;
            _connectStatus.Text = $"{message}  {branches.Count} branches found.";
            SetStepsEnabled(true);

            if (_branchBox.Items.Count > 0) _branchBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _connectStatus.ForeColor = Accent;
            _connectStatus.Text = ex.Message;
            SetStepsEnabled(false);
        }
        finally
        {
            _connectButton.Enabled = true;
        }
    }

    private async Task LoadPcsAsync()
    {
        if (_branchBox.SelectedItem is not HeadOfficeClient.BranchOption branch) return;

        _pcBox.Items.Clear();
        _pcBox.Text = "";
        _finishButton.Enabled = false;

        try
        {
            using var client = new HeadOfficeClient(_serverBox.Text.Trim(), _config.GateUsername, _config.GatePassword);
            var provisioning = await client.GetBranchAsync(branch.Id);
            if (provisioning is null) return;

            foreach (var pc in provisioning.Pcs) _pcBox.Items.Add(pc);

            // Land on the first free seat rather than a claimed one, so the obvious
            // action is also the correct one.
            var firstFree = provisioning.Pcs.FindIndex(p => !p.IsProvisioned);
            if (firstFree >= 0) _pcBox.SelectedIndex = firstFree;

            var free = provisioning.Pcs.Count(p => !p.IsProvisioned);
            _finishStatus.ForeColor = Muted;
            _finishStatus.Text = $"{branch.Name}: {free} of {provisioning.Pcs.Count} PCs still need setting up.";
            _finishButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _finishStatus.ForeColor = Accent;
            _finishStatus.Text = $"Could not load PCs. {ex.Message}";
        }
    }

    private async Task FinishAsync()
    {
        if (_branchBox.SelectedItem is not HeadOfficeClient.BranchOption branch) return;
        if (_pcBox.SelectedItem is not HeadOfficeClient.PcOption pc) return;

        if (_userRadio.Checked && _pinBox.Text.Trim().Length == 0)
        {
            // A customer PC with no PIN cannot be unlocked or undone from the machine
            // itself, so refusing here saves a site visit later.
            _finishStatus.ForeColor = Accent;
            _finishStatus.Text = "A customer gaming PC needs an admin PIN, or it cannot be unlocked later.";
            return;
        }

        _finishButton.Enabled = false;
        _finishStatus.ForeColor = Muted;
        _finishStatus.Text = $"Claiming {pc.PcNumber} at {branch.Name}…";

        try
        {
            using var client = new HeadOfficeClient(_serverBox.Text.Trim(), _config.GateUsername, _config.GatePassword);
            var (ok, message, _) = await client.ProvisionAsync(branch.Id, pc.PcNumber, MachineIdentity.Current());

            if (!ok)
            {
                _finishStatus.ForeColor = Accent;
                _finishStatus.Text = message;
                _finishButton.Enabled = true;
                return;
            }

            // Only now is anything written locally — a failed claim leaves no misleading
            // configuration on the machine.
            _config.ServerUrl = _serverBox.Text.Trim();
            _config.BranchId = branch.Id.ToString();
            _config.BranchName = branch.Name;
            _config.PcNumber = pc.PcNumber;
            _config.Role = _userRadio.Checked ? "user" : "operator";
            _config.AdminPin = _pinBox.Text.Trim();
            _config.IsSetUp = true;
            _config.Save();

            _finishStatus.ForeColor = Good;
            _finishStatus.Text = message;

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _finishStatus.ForeColor = Accent;
            _finishStatus.Text = ex.Message;
            _finishButton.Enabled = true;
        }
    }

    private void SetStepsEnabled(bool enabled)
    {
        _branchBox.Enabled = enabled;
        _pcBox.Enabled = enabled;
        _operatorRadio.Enabled = enabled;
        _userRadio.Enabled = enabled;
        _pinBox.Enabled = enabled;
        if (!enabled) _finishButton.Enabled = false;
    }

    // ── Styling ──

    private void AddStep(string text, ref int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Foreground,
            Location = new Point(24, y),
            AutoSize = true,
        });
        y += 24;
    }

    private void StyleField(TextBox box, int y, int width)
    {
        box.Location = new Point(24, y);
        box.Size = new Size(width, 30);
        box.BackColor = Field;
        box.ForeColor = Foreground;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10F);
    }

    private void StyleCombo(ComboBox box, int y)
    {
        box.Location = new Point(24, y);
        box.Size = new Size(ClientSize.Width - 48, 30);
        box.BackColor = Field;
        box.ForeColor = Foreground;
        box.FlatStyle = FlatStyle.Flat;
        box.Font = new Font("Segoe UI", 10F);
        box.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    private static void StyleButton(Button button, Color back, Color fore)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }
}
