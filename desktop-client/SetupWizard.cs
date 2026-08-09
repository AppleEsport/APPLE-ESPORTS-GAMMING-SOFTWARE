namespace AppleEsports.Desktop;

/// <summary>
/// What staff see the first time Apple Esports runs on a new machine.
///
/// Deliberately only two questions. Everything else that used to be asked here — which
/// branch, which seat, operator or customer — belongs later, when the answer is actually
/// needed and the person answering knows it:
///
///   • Operators, admins and their permissions are created in the Super Admin portal, not
///     by whoever happens to be installing software on a PC.
///   • A gaming seat is claimed when someone first uses the machine as one, because that is
///     the moment it is standing in front of a real PC with a real number on it.
///
/// Asking all of it up front made the installer look like a configuration console and
/// invited wrong answers from the person least able to give them.
/// </summary>
public sealed class SetupWizard : Form
{
    private static readonly Color Backdrop = Color.FromArgb(14, 14, 20);
    private static readonly Color Field = Color.FromArgb(32, 32, 42);
    private static readonly Color Foreground = Color.FromArgb(230, 230, 234);
    private static readonly Color Muted = Color.FromArgb(140, 140, 155);
    private static readonly Color Accent = Color.FromArgb(200, 30, 40);
    private static readonly Color Good = Color.FromArgb(60, 180, 110);

    private const int Pad = 26;

    private readonly AppConfig _config;

    private readonly ComboBox _serverBox = new();
    private readonly Button _testButton = new();
    private readonly Label _serverStatus = new();

    private readonly TextBox _pinBox = new();
    private readonly Label _finishStatus = new();
    private readonly Button _finishButton = new();

    private bool _serverConfirmed;

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
        ClientSize = new Size(540, 430);

        try
        {
            if (Environment.ProcessPath is { } exe) Icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch { /* cosmetic only */ }

        Build();
    }

    private void Build()
    {
        var width = ClientSize.Width - (Pad * 2);
        var y = 22;

        var title = new Label
        {
            Text = "APPLE ESPORTS",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(Pad, y),
            AutoSize = true,
        };
        Controls.Add(title);
        y += title.PreferredHeight + 4;

        Controls.Add(new Label
        {
            Text = "Two quick things and this PC is ready.",
            ForeColor = Muted,
            Location = new Point(Pad, y),
            Size = new Size(width, 20),
        });
        y += 38;

        // ── 1. Server ──
        // Which server this is depends on what kind of PC this is, and the two are not the
        // same question. The counter PC runs the branch itself; a gaming PC is a screen onto
        // the counter PC across the shop LAN. Neither one talks to Head Office to do its
        // work - that is the branch's background sync, and the shop trades without it.
        var isCounterPc = !_config.Role.Equals("user", StringComparison.OrdinalIgnoreCase);

        y = AddStep(isCounterPc
            ? "1.  This PC runs the branch"
            : "1.  Which PC at this branch is the counter?", y);

        _serverBox.Location = new Point(Pad, y);
        _serverBox.Size = new Size(width - 110, 30);
        _serverBox.BackColor = Field;
        _serverBox.ForeColor = Foreground;
        _serverBox.FlatStyle = FlatStyle.Flat;
        _serverBox.Font = new Font("Segoe UI", 10F);
        // Editable, not a fixed list: a counter PC's address on the shop network is
        // different at every branch and nobody has typed it here before.
        _serverBox.DropDownStyle = ComboBoxStyle.DropDown;

        if (isCounterPc)
        {
            // The branch database and API are installed on this machine, so the answer is
            // already known and there is nothing to ask. An earlier build offered only a
            // public server address here, which is what made every click travel to the
            // cloud and the shop stop working the moment the internet did.
            _serverBox.Items.Add(AppConfig.LocalBranchUrl);
            _serverBox.Text = string.IsNullOrWhiteSpace(_config.ServerUrl) ? AppConfig.LocalBranchUrl : _config.ServerUrl;
        }
        else
        {
            // A gaming PC has no database of its own. It needs the counter PC's address on
            // the shop network - not localhost, and not Head Office.
            _serverBox.Text = PointsAtThisMachine(_config.ServerUrl) ? "" : _config.ServerUrl;
        }
        _serverBox.TextChanged += (_, _) =>
        {
            // Any edit invalidates a previous successful test, so the tick cannot linger
            // next to an address it was never checked against.
            _serverConfirmed = false;
            _serverStatus.Text = "";
        };
        Controls.Add(_serverBox);

        _testButton.Text = "Test";
        _testButton.Location = new Point(ClientSize.Width - Pad - 100, y);
        _testButton.Size = new Size(100, 30);
        StyleButton(_testButton, Accent, Color.White);
        _testButton.Click += async (_, _) => await TestServerAsync();
        Controls.Add(_testButton);
        y += 36;

        _serverStatus.ForeColor = Muted;
        _serverStatus.Location = new Point(Pad, y);
        _serverStatus.Size = new Size(width, 20);
        Controls.Add(_serverStatus);
        y += 40;

        // ── 2. PIN ──
        y = AddStep("2.  Choose an admin PIN", y);

        Controls.Add(new Label
        {
            // Worded as a choice, not a challenge. Labelled "enter the admin PIN" people
            // reasonably assume one already exists and go looking for it.
            Text = "Make one up now — staff will type it to change or undo this setup later.",
            ForeColor = Muted,
            Location = new Point(Pad, y),
            Size = new Size(width, 20),
        });
        y += 26;

        _pinBox.Location = new Point(Pad, y);
        _pinBox.Size = new Size(190, 34);
        _pinBox.BackColor = Field;
        _pinBox.ForeColor = Foreground;
        _pinBox.BorderStyle = BorderStyle.FixedSingle;
        _pinBox.Font = new Font("Segoe UI", 14F);
        _pinBox.TextAlign = HorizontalAlignment.Center;
        _pinBox.Text = _config.AdminPin;
        Controls.Add(_pinBox);
        y += 48;

        _finishStatus.ForeColor = Muted;
        _finishStatus.Location = new Point(Pad, y);
        _finishStatus.Size = new Size(width, 34);
        Controls.Add(_finishStatus);
        y += 40;

        _finishButton.Text = "Finish setup";
        _finishButton.Location = new Point(ClientSize.Width - Pad - 180, y);
        _finishButton.Size = new Size(180, 42);
        StyleButton(_finishButton, Accent, Color.White);
        _finishButton.Click += async (_, _) => await FinishAsync();
        Controls.Add(_finishButton);

        ClientSize = new Size(ClientSize.Width, _finishButton.Bottom + Pad);
    }

    // ── Behaviour ──

    private string NormalisedServer()
    {
        var url = _serverBox.Text.Trim();
        if (url.Length == 0) return "";

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }
        return url.TrimEnd('/');
    }

    /// <summary>True when the address resolves to this machine rather than a real server.</summary>
    private static bool PointsAtThisMachine(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.Ordinal)
                || host.Equals("::1", StringComparison.Ordinal)
                || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TestServerAsync()
    {
        var url = NormalisedServer();
        if (url.Length == 0)
        {
            _serverStatus.ForeColor = Accent;
            _serverStatus.Text = "Enter the server address first.";
            return false;
        }

        _testButton.Enabled = false;
        _serverStatus.ForeColor = Muted;
        _serverStatus.Text = "Checking…";

        try
        {
            using var client = new HeadOfficeClient(url, _config.GateUsername, _config.GatePassword);
            var (reachable, message) = await client.PingAsync();

            _serverConfirmed = reachable;
            if (reachable) _serverBox.Text = url;

            // Whether a local address is right depends entirely on which kind of PC this is,
            // so the same answer is confirmed on one and refused on the other.
            if (PointsAtThisMachine(url))
            {
                if (_config.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    // A gaming PC pointed at itself has no database behind it. It would set
                    // up cleanly and then fail the first time anyone seated a customer.
                    _serverConfirmed = false;
                    _serverStatus.ForeColor = Accent;
                    _serverStatus.Text = "That is this PC. Enter the counter PC's address on the shop network.";
                    return false;
                }

                if (reachable)
                {
                    _serverStatus.ForeColor = Good;
                    _serverStatus.Text = "Connected. The branch runs on this PC, so the shop works with no internet.";
                    return true;
                }
            }

            _serverStatus.ForeColor = reachable ? Good : Accent;
            _serverStatus.Text = message;
            return reachable;
        }
        catch (Exception ex)
        {
            _serverStatus.ForeColor = Accent;
            _serverStatus.Text = ex.Message;
            _serverConfirmed = false;
            return false;
        }
        finally
        {
            _testButton.Enabled = true;
        }
    }

    private async Task FinishAsync()
    {
        var pin = _pinBox.Text.Trim();
        if (pin.Length < 4)
        {
            _finishStatus.ForeColor = Accent;
            _finishStatus.Text = "Choose a PIN of at least 4 characters.";
            return;
        }

        _finishButton.Enabled = false;

        try
        {
            // Check the address rather than take it on trust, but only if it has not already
            // been confirmed — nobody should have to press Test to be allowed to continue.
            if (!_serverConfirmed && !await TestServerAsync())
            {
                _finishStatus.ForeColor = Accent;
                _finishStatus.Text = "That server could not be reached. Fix the address, or check the network.";
                return;
            }

            _config.ServerUrl = NormalisedServer();
            _config.AdminPin = pin;
            _config.IsSetUp = true;
            // Role and seat are deliberately left unset. This machine is a plain client until
            // someone uses it as a gaming PC, and that is when it claims a seat.
            _config.Save();

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _finishStatus.ForeColor = Accent;
            _finishStatus.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed) _finishButton.Enabled = true;
        }
    }

    // ── Styling ──

    private int AddStep(string text, int y)
    {
        var label = new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Foreground,
            Location = new Point(Pad, y),
            AutoSize = true,
        };
        Controls.Add(label);
        return y + label.PreferredHeight + 8;
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
