namespace AppleEsports.Desktop;

/// <summary>
/// What staff see the first time Apple Esports runs on a new machine.
///
/// Deliberately one screen rather than a multi-step wizard: there are only a handful of
/// decisions and each narrows the next, so showing them together makes the whole setup
/// legible at a glance. Nothing is saved until Head Office has confirmed, so an abandoned
/// setup leaves no misleading state behind.
///
/// Role is asked before the seat, because the answer decides whether a seat is even a
/// question. A counter PC is not a gaming station and has no PC number.
/// </summary>
public sealed class SetupWizard : Form
{
    private static readonly Color Backdrop = Color.FromArgb(14, 14, 20);
    private static readonly Color Field = Color.FromArgb(32, 32, 42);
    private static readonly Color Foreground = Color.FromArgb(230, 230, 234);
    private static readonly Color Muted = Color.FromArgb(140, 140, 155);
    private static readonly Color Accent = Color.FromArgb(200, 30, 40);
    private static readonly Color Good = Color.FromArgb(60, 180, 110);

    private const int Margin = 24;

    private readonly AppConfig _config;

    private readonly TextBox _serverBox = new();
    private readonly Button _connectButton = new();
    private readonly Label _connectStatus = new();

    private readonly ComboBox _branchBox = new();
    private readonly RadioButton _operatorRadio = new();
    private readonly RadioButton _userRadio = new();

    // The seat question lives in its own panel so it can disappear entirely for a counter
    // PC, rather than sitting there greyed out inviting the question "why can't I fill this in".
    private readonly Panel _seatPanel = new();
    private readonly ComboBox _pcBox = new();
    private readonly Label _seatHint = new();

    private readonly TextBox _pinBox = new();
    private readonly Label _pinHint = new();
    private readonly Label _finishStatus = new();
    private readonly Button _finishButton = new();

    private int _seatPanelTop;

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
        ClientSize = new Size(560, 640);

        try
        {
            if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch { /* cosmetic only */ }

        Build();
        ApplyRole();
    }

    private void Build()
    {
        var width = ClientSize.Width - (Margin * 2);
        var y = 20;

        var title = new Label
        {
            Text = "APPLE ESPORTS",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(Margin, y),
            AutoSize = true,
        };
        Controls.Add(title);
        // Measured rather than guessed — an 18pt line is taller than the 34px I first
        // assumed, which is what made the subtitle overlap the heading.
        y += title.PreferredHeight + 6;

        Controls.Add(new Label
        {
            Text = "This PC has not been set up yet. It cannot take customers until it has.",
            ForeColor = Muted,
            Location = new Point(Margin, y),
            Size = new Size(width, 20),
        });
        y += 34;

        // ── 1. Server ──
        y = AddStep("1.  Where is the server?", y);
        StyleField(_serverBox, y, width - 130);
        _serverBox.Text = _config.ServerUrl;
        Controls.Add(_serverBox);

        _connectButton.Text = "Connect";
        _connectButton.Location = new Point(ClientSize.Width - Margin - 120, y);
        _connectButton.Size = new Size(120, 30);
        StyleButton(_connectButton, Accent, Color.White);
        _connectButton.Click += async (_, _) => await ConnectAsync();
        Controls.Add(_connectButton);
        y += 36;

        _connectStatus.ForeColor = Muted;
        _connectStatus.Location = new Point(Margin, y);
        _connectStatus.Size = new Size(width, 20);
        Controls.Add(_connectStatus);
        y += 32;

        // ── 2. Branch ──
        y = AddStep("2.  Which branch is this PC in?", y);
        StyleCombo(_branchBox, y, width);
        _branchBox.SelectedIndexChanged += async (_, _) => await LoadPcsAsync();
        Controls.Add(_branchBox);
        y += 44;

        // ── 3. Role — asked before the seat, because it decides whether a seat applies ──
        y = AddStep("3.  What is this machine for?", y);

        _operatorRadio.Text = "Operator counter PC  —  full dashboard, staff can close it";
        _operatorRadio.Location = new Point(Margin, y);
        _operatorRadio.Size = new Size(width, 24);
        _operatorRadio.ForeColor = Foreground;
        _operatorRadio.Checked = true;
        _operatorRadio.CheckedChanged += (_, _) => ApplyRole();
        Controls.Add(_operatorRadio);
        y += 26;

        _userRadio.Text = "Customer gaming PC  —  locked, no close button";
        _userRadio.Location = new Point(Margin, y);
        _userRadio.Size = new Size(width, 24);
        _userRadio.ForeColor = Foreground;
        Controls.Add(_userRadio);
        y += 36;

        // ── 4. Seat — customer PCs only ──
        _seatPanelTop = y;
        _seatPanel.Location = new Point(0, y);
        _seatPanel.Size = new Size(ClientSize.Width, 76);
        _seatPanel.BackColor = Backdrop;

        _seatPanel.Controls.Add(new Label
        {
            Text = "4.  Which PC is this machine?",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Foreground,
            Location = new Point(Margin, 0),
            AutoSize = true,
        });

        StyleCombo(_pcBox, 24, width);
        _seatPanel.Controls.Add(_pcBox);

        _seatHint.ForeColor = Muted;
        _seatHint.Location = new Point(Margin, 56);
        _seatHint.Size = new Size(width, 18);
        _seatPanel.Controls.Add(_seatHint);

        Controls.Add(_seatPanel);

        // Everything below shifts depending on whether the seat panel is showing.
        var afterSeat = y + _seatPanel.Height + 8;

        Controls.Add(_pinHint);
        _pinHint.Text = "Admin PIN  (needed to change or undo this setup later)";
        _pinHint.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _pinHint.Location = new Point(Margin, afterSeat);
        _pinHint.AutoSize = true;

        StyleField(_pinBox, afterSeat + 22, 200);
        _pinBox.UseSystemPasswordChar = true;
        _pinBox.Text = _config.AdminPin;
        Controls.Add(_pinBox);

        _finishStatus.ForeColor = Muted;
        _finishStatus.Location = new Point(Margin, afterSeat + 62);
        _finishStatus.Size = new Size(width, 36);
        Controls.Add(_finishStatus);

        _finishButton.Text = "Finish setup";
        _finishButton.Location = new Point(ClientSize.Width - Margin - 180, afterSeat + 104);
        _finishButton.Size = new Size(180, 40);
        StyleButton(_finishButton, Accent, Color.White);
        _finishButton.Enabled = false;
        _finishButton.Click += async (_, _) => await FinishAsync();
        Controls.Add(_finishButton);

        SetStepsEnabled(false);
    }

    /// <summary>
    /// Shows or hides the seat question and closes the gap behind it. A counter PC is not a
    /// gaming station, so asking which PC number it is has no meaning — and leaving the
    /// control visible but disabled just invites the question.
    /// </summary>
    private void ApplyRole()
    {
        var needsSeat = _userRadio.Checked;
        _seatPanel.Visible = needsSeat;

        var shift = needsSeat ? _seatPanel.Height + 8 : 0;
        var top = _seatPanelTop + shift;

        _pinHint.Top = top;
        _pinBox.Top = top + 22;
        _finishStatus.Top = top + 62;
        _finishButton.Top = top + 104;

        ClientSize = new Size(ClientSize.Width, _finishButton.Bottom + 24);
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
        _finishButton.Enabled = false;

        try
        {
            using var client = new HeadOfficeClient(_serverBox.Text.Trim(), _config.GateUsername, _config.GatePassword);
            var provisioning = await client.GetBranchAsync(branch.Id);
            if (provisioning is null) return;

            foreach (var pc in provisioning.Pcs) _pcBox.Items.Add(pc);

            // Land on the first free seat rather than a claimed one, so the obvious action
            // is also the correct one.
            var firstFree = provisioning.Pcs.FindIndex(p => !p.IsProvisioned);
            if (firstFree >= 0) _pcBox.SelectedIndex = firstFree;

            var free = provisioning.Pcs.Count(p => !p.IsProvisioned);
            _seatHint.Text = $"{free} of {provisioning.Pcs.Count} PCs at {branch.Name} still need setting up.";

            _finishStatus.ForeColor = Muted;
            _finishStatus.Text = "";
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

        var isUserPc = _userRadio.Checked;

        if (isUserPc && _pcBox.SelectedItem is not HeadOfficeClient.PcOption)
        {
            _finishStatus.ForeColor = Accent;
            _finishStatus.Text = "Pick which PC this machine is.";
            return;
        }

        if (isUserPc && _pinBox.Text.Trim().Length == 0)
        {
            // A customer PC with no PIN cannot be unlocked or undone from the machine itself,
            // so refusing here saves a site visit later.
            _finishStatus.ForeColor = Accent;
            _finishStatus.Text = "A customer gaming PC needs an admin PIN, or it cannot be unlocked later.";
            return;
        }

        _finishButton.Enabled = false;
        _finishStatus.ForeColor = Muted;

        try
        {
            var pcNumber = "";

            if (isUserPc)
            {
                var pc = (HeadOfficeClient.PcOption)_pcBox.SelectedItem!;
                pcNumber = pc.PcNumber;
                _finishStatus.Text = $"Claiming {pcNumber} at {branch.Name}…";

                using var client = new HeadOfficeClient(_serverBox.Text.Trim(), _config.GateUsername, _config.GatePassword);
                var (ok, message, _) = await client.ProvisionAsync(branch.Id, pcNumber, MachineIdentity.Current());

                if (!ok)
                {
                    _finishStatus.ForeColor = Accent;
                    _finishStatus.Text = message;
                    _finishButton.Enabled = true;
                    return;
                }

                _finishStatus.ForeColor = Good;
                _finishStatus.Text = message;
            }
            else
            {
                // A counter PC claims no seat. The pcs table holds gaming stations, and
                // registering the counter as one would leave a phantom seat an operator
                // could try to sell.
                _finishStatus.ForeColor = Good;
                _finishStatus.Text = $"Set up as the operator counter PC for {branch.Name}.";
            }

            // Only written once the claim (if any) succeeded, so a failed setup leaves no
            // misleading configuration on the machine.
            _config.ServerUrl = _serverBox.Text.Trim();
            _config.BranchId = branch.Id.ToString();
            _config.BranchName = branch.Name;
            _config.PcNumber = pcNumber;
            _config.Role = isUserPc ? "user" : "operator";
            _config.AdminPin = _pinBox.Text.Trim();
            _config.IsSetUp = true;
            _config.Save();

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

    private int AddStep(string text, int y)
    {
        var label = new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Foreground,
            Location = new Point(Margin, y),
            AutoSize = true,
        };
        Controls.Add(label);
        return y + label.PreferredHeight + 6;
    }

    private void StyleField(TextBox box, int y, int width)
    {
        box.Location = new Point(Margin, y);
        box.Size = new Size(width, 30);
        box.BackColor = Field;
        box.ForeColor = Foreground;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10F);
    }

    private void StyleCombo(ComboBox box, int y, int width)
    {
        box.Location = new Point(Margin, y);
        box.Size = new Size(width, 30);
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
