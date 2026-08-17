using System.Text;
using System.Text.Json;

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

    private readonly ComboBox _branchBox = new();
    private readonly Label _branchStatus = new();
    private readonly Label _branchPrompt = new();

    private readonly TextBox _pinBox = new();
    private readonly Label _finishStatus = new();
    private readonly Button _finishButton = new();

    private bool _serverConfirmed;
    private bool _isCounterPc;

    /// <summary>
    /// True once this machine has been told which branch it is, and holds only that one.
    /// A fresh database seeds all four, which means it has not been told yet.
    /// </summary>
    private bool _alreadyAdopted;

    private readonly List<(Guid Id, string Name)> _headOfficeBranches = new();

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
        _isCounterPc = !_config.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
        var isCounterPc = _isCounterPc;

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

        // ── 2. Which branch (counter PC only) ──
        //
        // The one question a counter PC genuinely cannot answer for itself. Both databases
        // seed independently, so each invents its own identifier for the same shop - and a
        // branch that never asks ends up with a private "Adajan" that Head Office has never
        // heard of, reporting sessions on PCs that, as far as Head Office is concerned, do
        // not exist. Asking here is what makes the two sides talk about the same things.
        if (isCounterPc)
        {
            y = AddStep("2.  Which branch is this?", y);

            _branchPrompt.Text = "Taken from Head Office, so this shop's records match theirs.";
            _branchPrompt.ForeColor = Muted;
            _branchPrompt.Location = new Point(Pad, y);
            _branchPrompt.Size = new Size(width, 20);
            Controls.Add(_branchPrompt);
            y += 26;

            _branchBox.Location = new Point(Pad, y);
            _branchBox.Size = new Size(width, 30);
            _branchBox.BackColor = Field;
            _branchBox.ForeColor = Foreground;
            _branchBox.FlatStyle = FlatStyle.Flat;
            _branchBox.Font = new Font("Segoe UI", 10F);
            // Fixed list, unlike the server box: these are real branches at Head Office and
            // a typed one would be a branch that does not exist.
            _branchBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _branchBox.Enabled = false;
            _branchBox.Items.Add("Press Test above first");
            _branchBox.SelectedIndex = 0;
            Controls.Add(_branchBox);
            y += 36;

            _branchStatus.ForeColor = Muted;
            _branchStatus.Location = new Point(Pad, y);
            _branchStatus.Size = new Size(width, 34);
            Controls.Add(_branchStatus);
            y += 40;
        }

        // ── 3. PIN ──
        y = AddStep(isCounterPc ? "3.  Choose an admin PIN" : "2.  Choose an admin PIN", y);

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

                    // Only now can the branch be asked what it is - it has to be answering first.
                    await LoadBranchesAsync();
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

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Asks the branch on this machine what it is, and — if it has not been told yet — asks it
    /// for Head Office's list of real branches.
    ///
    /// Routed through the branch rather than straight to Head Office so that Head Office's
    /// address stays in one place. The branch already has it, for sync.
    /// </summary>
    private async Task LoadBranchesAsync()
    {
        if (!_isCounterPc) return;

        _branchBox.Items.Clear();
        _branchBox.Enabled = false;
        _headOfficeBranches.Clear();
        _branchStatus.ForeColor = Muted;
        _branchStatus.Text = "Asking Head Office which branches exist…";

        var baseUrl = NormalisedServer();

        try
        {
            using var identity = JsonDocument.Parse(
                await Http.GetStringAsync($"{baseUrl}/api/provisioning/identity"));
            var data = identity.RootElement.GetProperty("data");
            _alreadyAdopted = data.GetProperty("adopted").GetBoolean();

            if (_alreadyAdopted)
            {
                var name = data.GetProperty("branches")[0].GetProperty("name").GetString() ?? "(already set)";
                _branchBox.Items.Add(name);
                _branchBox.SelectedIndex = 0;
                _branchStatus.ForeColor = Good;
                _branchStatus.Text = $"Already set up as {name}. This cannot be changed once the shop has traded.";
                return;
            }

            using var branches = JsonDocument.Parse(
                await Http.GetStringAsync($"{baseUrl}/api/provisioning/head-office/branches"));

            foreach (var b in branches.RootElement.GetProperty("data").EnumerateArray())
            {
                _headOfficeBranches.Add((
                    b.GetProperty("id").GetGuid(),
                    b.GetProperty("name").GetString() ?? "(unnamed)"));
            }

            if (_headOfficeBranches.Count == 0)
            {
                _branchStatus.ForeColor = Accent;
                _branchStatus.Text = "Head Office has no branches recorded. Add them there first.";
                return;
            }

            foreach (var (_, name) in _headOfficeBranches) _branchBox.Items.Add(name);
            _branchBox.SelectedIndex = 0;
            _branchBox.Enabled = true;
            _branchStatus.ForeColor = Muted;
            _branchStatus.Text = "Pick the shop this PC sits in.";
        }
        catch (Exception ex)
        {
            // Not fatal, and deliberately so. The whole point of the branch running locally is
            // that the shop can open without the internet — refusing to finish setup because
            // Head Office is unreachable would defeat that on the one day it matters most.
            // It is said plainly instead, because an unadopted branch trades perfectly and
            // reports nothing, which is the failure that hides.
            _branchStatus.ForeColor = Accent;
            _branchStatus.Text =
                "Could not reach Head Office, so this PC cannot be linked to a branch yet. " +
                "The shop will work, but nothing will reach Head Office until this is done.";
            _ = ex;
        }
    }

    /// <summary>
    /// Takes Head Office's identifiers for this branch, its PCs, pricing and operators.
    /// Returns null on success, or why it could not be done.
    /// </summary>
    private async Task<string?> AdoptChosenBranchAsync()
    {
        if (!_isCounterPc || _alreadyAdopted) return null;
        if (_headOfficeBranches.Count == 0 || _branchBox.SelectedIndex < 0) return null;

        var chosen = _headOfficeBranches[_branchBox.SelectedIndex];

        _finishStatus.ForeColor = Muted;
        _finishStatus.Text = $"Setting this PC up as {chosen.Name}…";

        var body = new StringContent(
            JsonSerializer.Serialize(new { branchId = chosen.Id }),
            Encoding.UTF8);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await Http.PostAsync($"{NormalisedServer()}/api/provisioning/adopt", body);
        var text = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode) return null;

        // "error" is the field ApiResponse.Fail actually writes. This looked for "message",
        // which nothing on the other end has ever produced, so every refusal was thrown away
        // and replaced with a bare status code. What was being discarded was not a detail: a
        // second PC adopting a branch that is already live is answered with the reason, the
        // machine currently running it, and what to do about it - and the operator was shown
        // "Head Office link failed (400)" instead, which names no cause and suggests no fix.
        // "message" is still accepted in case something upstream ever sends one.
        try
        {
            var root = JsonDocument.Parse(text).RootElement;

            foreach (var field in new[] { "error", "message" })
            {
                if (root.TryGetProperty(field, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }
        }
        catch { /* not JSON - fall through to the status code, which is all we have */ }

        return $"Head Office link failed ({(int)response.StatusCode}).";
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

            // Before anything is saved: if this fails the machine should stay unset up, not
            // end up half configured and looking finished.
            var adoptionError = await AdoptChosenBranchAsync();
            if (adoptionError is not null)
            {
                _finishStatus.ForeColor = Accent;
                _finishStatus.Text = adoptionError;
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
