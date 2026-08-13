using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AppleEsports.Desktop;

public sealed class MainForm : Form
{
    // Palette lifted from the web dashboard's dark theme so the shell doesn't flash white.
    private static readonly Color Backdrop = Color.FromArgb(10, 10, 15);
    private static readonly Color Foreground = Color.FromArgb(230, 230, 234);
    private static readonly Color Muted = Color.FromArgb(140, 140, 155);
    private static readonly Color Accent = Color.FromArgb(200, 30, 40);

    private readonly AppConfig _config;
    private readonly WebView2 _web = new();

    private readonly Panel _overlay = new();
    private readonly Label _overlayTitle = new();
    private readonly Label _overlayMessage = new();
    private readonly Button _retryButton = new();
    private readonly Button _settingsButton = new();

    private bool _isFullScreen;
    private FormBorderStyle _borderBeforeFullScreen;
    private FormWindowState _stateBeforeFullScreen;

    /// <summary>Set only by the PIN-protected exit path, so a locked PC can still be closed.</summary>
    private bool _allowClose;

    /// <summary>
    /// How long a failure to reach the branch is treated as "still starting" rather than
    /// reported as a fault.
    ///
    /// On the counter PC the branch API is a Windows service on this same machine, and at
    /// boot Windows starts it and this app at once — the service has a database to bring up
    /// and migrations to apply, so the dashboard reliably gets there first. Without this the
    /// shop would meet an error screen every single morning and learn to click through it,
    /// which is exactly how a real fault later goes unnoticed.
    /// </summary>
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The same, while an update is being installed.
    ///
    /// Two minutes is right for an ordinary morning start. It is nowhere near enough for an
    /// upgrade: the installer stops the API and PostgreSQL, replaces 300 MB, brings the
    /// database back up and applies migrations. The old grace ran out somewhere in the middle
    /// of that and the operator got "Cannot reach the branch system" with a list of things to
    /// check, none of which were wrong - the software blaming the shop for something it was
    /// doing to itself.
    ///
    /// Twenty minutes covers a slow disk and a large migration with room to spare, and it only
    /// applies when an update is genuinely under way. A real fault at any other time is still
    /// reported in two minutes, which is the whole reason not to simply raise the grace for
    /// everything.
    /// </summary>
    private static readonly TimeSpan UpdateGrace = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Written the moment before the installer is launched, and deleted the moment the
    /// dashboard loads again.
    ///
    /// It has to be a file. The app deliberately exits so the installer can replace its own
    /// executable, so nothing held in memory survives to tell the next launch why it could
    /// not connect. Kept beside the config rather than in the program folder, because the
    /// program folder is exactly what is being overwritten.
    /// </summary>
    private static string UpdateMarkerPath =>
        Path.Combine(AppConfig.AppDataDirectory, "installing-update.txt");

    /// <summary>The version being installed, when one is. Null the rest of the time.</summary>
    private static string? UpdateInProgress()
    {
        try
        {
            if (!File.Exists(UpdateMarkerPath)) return null;

            // A stale marker must not excuse a genuine outage for ever. If an install was
            // interrupted - power cut mid-upgrade - the shop is back to reporting faults
            // normally within the hour rather than sitting on a reassuring message that has
            // stopped being true.
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(UpdateMarkerPath) > TimeSpan.FromHours(1))
            {
                ClearUpdateMarker();
                return null;
            }

            var version = File.ReadAllText(UpdateMarkerPath).Trim();
            return string.IsNullOrWhiteSpace(version) ? "a new version" : version;
        }
        catch
        {
            return null;   // unreadable marker is not a reason to fail to start
        }
    }

    private static void MarkUpdateStarting(string version)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.AppDataDirectory);
            File.WriteAllText(UpdateMarkerPath, version);
        }
        catch { /* best effort - the update still proceeds, the message is just less specific */ }
    }

    private static void ClearUpdateMarker()
    {
        try { if (File.Exists(UpdateMarkerPath)) File.Delete(UpdateMarkerPath); } catch { }
    }

    private readonly System.Windows.Forms.Timer _retryTimer = new() { Interval = 3000 };
    private DateTime? _firstFailureAt;

    public MainForm(AppConfig config)
    {
        _config = config;

        // Says where this window is pointed from the moment it opens. Previously this was
        // only filled in once a page finished loading, so a PC that could not reach its
        // branch — exactly when someone needs to know what it is trying to talk to — showed
        // a bare title and gave them nothing to go on.
        Text = $"Apple Esports ERP  —  {HostLabel()}";
        BackColor = Backdrop;
        MinimumSize = new Size(1024, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1440, 900);

        // Use the icon baked into this executable, so the window, taskbar and
        // Alt-Tab entry all show the same Apple Esports logo as the file itself.
        try
        {
            if (Environment.ProcessPath is { } exePath)
                Icon = Icon.ExtractAssociatedIcon(exePath);
        }
        catch
        {
            // Cosmetic only — a missing icon must not stop the app.
        }

        // A customer-facing PC is sealed: no close button, no minimise, no border to drag.
        // If a customer can shut this window they are looking at the Windows desktop, and
        // the kiosk is worthless. Getting out requires the admin PIN (Ctrl+Alt+Q).
        if (_config.IsUserPc)
        {
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = !_config.IsUserPc;
            WindowState = FormWindowState.Maximized;
        }
        else if (_config.StartMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }

        BuildOverlay();

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = Backdrop;
        _web.Visible = false;

        Controls.Add(_web);
        Controls.Add(_overlay);
        _overlay.BringToFront();

        Load += OnLoadAsync;
    }

    // ── UI ────────────────────────────────────────────────────────────────

    private void BuildOverlay()
    {
        _overlay.Dock = DockStyle.Fill;
        _overlay.BackColor = Backdrop;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Backdrop,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,   // centres the block inside its cell
            WrapContents = false,
            BackColor = Backdrop,
        };

        _overlayTitle.Text = "APPLE ESPORTS";
        _overlayTitle.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
        _overlayTitle.ForeColor = Accent;
        _overlayTitle.AutoSize = true;
        _overlayTitle.Margin = new Padding(0, 0, 0, 10);

        _overlayMessage.Font = new Font("Segoe UI", 11F);
        _overlayMessage.ForeColor = Muted;
        _overlayMessage.AutoSize = true;
        _overlayMessage.MaximumSize = new Size(560, 0);
        _overlayMessage.Margin = new Padding(0, 0, 0, 22);

        StyleButton(_retryButton, "Try again", Accent, Color.White);
        _retryButton.Visible = false;
        _retryButton.Click += (_, _) => Connect();

        StyleButton(_settingsButton, "Change server…", Color.FromArgb(38, 38, 48), Foreground);
        _settingsButton.Visible = false;
        _settingsButton.Click += (_, _) => OpenSettings();

        stack.Controls.Add(_overlayTitle);
        stack.Controls.Add(_overlayMessage);
        stack.Controls.Add(_retryButton);
        stack.Controls.Add(_settingsButton);

        layout.Controls.Add(stack, 0, 1);
        _overlay.Controls.Add(layout);
    }

    private static void StyleButton(Button button, string text, Color back, Color fore)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Size = new Size(220, 42);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 0, 10);
    }

    private void ShowOverlay(string message, bool showActions)
    {
        _overlayMessage.Text = message;
        _retryButton.Visible = showActions;
        _settingsButton.Visible = showActions;
        _overlay.Visible = true;
        _overlay.BringToFront();
        _web.Visible = false;
    }

    private void HideOverlay()
    {
        _overlay.Visible = false;
        _web.Visible = true;
        _web.BringToFront();
        _web.Focus();
    }

    // ── Startup / navigation ──────────────────────────────────────────────

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        ShowOverlay("Starting…", showActions: false);

        try
        {
            // WebView2 needs a writable profile folder — Program Files is not writable,
            // which is exactly the kind of thing that makes an installed app die on launch.
            Directory.CreateDirectory(AppConfig.WebViewDataDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppConfig.WebViewDataDirectory);

            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            ShowOverlay($"Could not start the embedded browser.\n\n{ex.Message}", showActions: true);
            return;
        }

        var core = _web.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;

        // Answer the nginx Basic Auth gate in front of the dashboard automatically,
        // so operators are not asked for a second password every launch.
        core.BasicAuthenticationRequested += (_, args) =>
        {
            if (string.IsNullOrEmpty(_config.GateUsername)) return;
            args.Response.UserName = _config.GateUsername;
            args.Response.Password = _config.GatePassword;
        };

        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
            {
                _firstFailureAt = null;

                // The dashboard loaded, so whatever was being installed has finished and the
                // branch is back. Clearing it here rather than on launch means an update that
                // needs a second restart still gets the patient message.
                ClearUpdateMarker();

                HideOverlay();
                return;
            }

            _firstFailureAt ??= DateTime.UtcNow;

            // An update in progress is not a fault, and must never be dressed up as one. The
            // installer has deliberately stopped the branch to replace it; saying "Cannot
            // reach the branch system" and asking the operator to check the network is both
            // untrue and alarming, and it teaches them to click through error screens.
            var installing = UpdateInProgress();
            var grace = installing is null ? StartupGrace : UpdateGrace;
            var remaining = grace - (DateTime.UtcNow - _firstFailureAt.Value);

            if (remaining > TimeSpan.Zero)
            {
                // No buttons while this is running: there is nothing for anyone to do, and
                // offering "Change server..." to an operator whose branch is merely still
                // booting invites them to break a correct setting.
                ShowOverlay(
                    installing is null
                        ? $"Starting the branch system…  ({(int)remaining.TotalSeconds}s)"
                        : $"Installing update {installing}.\n\n" +
                          "This takes a few minutes and finishes on its own. Nothing is wrong, " +
                          "and there is nothing you need to do — the screen comes back by itself.\n\n" +
                          "Please do not switch this PC off.",
                    showActions: false);

                _retryTimer.Start();
                return;
            }

            // Past even the update grace. Say so plainly rather than silently reverting to a
            // network error, because "the update did not finish" is a different problem with a
            // different answer, and the person reading this needs to know which one they have.
            if (installing is not null)
            {
                ClearUpdateMarker();
                ShowOverlay(
                    $"The update to {installing} has not finished, and it is taking longer than it should.\n\n" +
                    "Your takings and records are safe — an update never touches them. " +
                    "Tell the owner, and try switching this PC off and on again.",
                    showActions: true);
                return;
            }

            ShowOverlay(DescribeError(args.WebErrorStatus), showActions: true);
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            var title = core.DocumentTitle;
            Text = string.IsNullOrWhiteSpace(title)
                ? $"Apple Esports ERP  —  {HostLabel()}"
                : $"{title}  —  {HostLabel()}";
        };

        core.ProcessFailed += (_, _) =>
            ShowOverlay("The embedded browser stopped responding.", showActions: true);

        // The one thing the dashboard is allowed to ask of the machine it runs on.
        //
        // The Updates page can see that a newer version exists - it asks Head Office directly -
        // but only this process can install one. Without a way across, the best that page could
        // ever offer was "it will happen on its own eventually", which is no use to somebody
        // standing at a counter wondering whether updates work at all.
        //
        // Deliberately not a general bridge. It takes one fixed word and grants nothing else:
        // the page cannot name a file, a version or a URL, so the worst a compromised dashboard
        // achieves here is asking Head Office for the update it was going to take anyway.
        core.WebMessageReceived += (_, args) =>
        {
            string message;
            try { message = args.TryGetWebMessageAsString() ?? string.Empty; }
            catch { return; }   // not a string - not ours

            if (!string.Equals(message, "check-for-updates", StringComparison.Ordinal)) return;

            // Wakes the loop rather than starting a second check, so pressing the button twice
            // cannot have two downloads writing the same file.
            try { _checkNow.Cancel(); } catch { /* already awake */ }
        };

        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer.Stop();
            Connect();
        };

        Connect();

        // Fire and forget. An update check must never delay the dashboard appearing — the
        // shop opens whether or not Head Office is reachable.
        _ = Task.Run(UpdateLoopAsync);
    }

    /// <summary>
    /// Checks Head Office for an approved update, then keeps checking every few minutes.
    ///
    /// Runs for the life of the app rather than only at launch: a branch PC can stay on for
    /// days, and a fix nobody restarts to collect is a fix nobody has.
    ///
    /// This used to wait four hours between looks, which made every release feel broken. A
    /// version published at nine in the evening would not be noticed until the small hours,
    /// and in the meantime the branch sat on the old build with no sign that anything was
    /// coming - indistinguishable, from the counter, from updates not working at all. During
    /// development that is worse still: a fix goes out, the branch does not take it, and the
    /// next hour goes on hunting a bug that was already fixed.
    ///
    /// Thirty seconds instead, so a fix published at Head Office is on its way to every shop
    /// before anyone thinks to ask whether it worked.
    ///
    /// Frugality was the wrong thing to optimise for. A check is one request answered with a
    /// version number and a hash - a few hundred bytes, far less than a single page of the
    /// dashboard. The branch already reports its own status to Head Office every three
    /// seconds, so this adds about a tenth to a conversation that is happening anyway. The
    /// 172 MB download still only happens when there is genuinely something new.
    /// </summary>
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long after the app opens before the first look.
    ///
    /// This is the one that matters for a shop that was closed. A counter PC switched on in
    /// the morning should collect whatever was published overnight straight away, while the
    /// operator is still logging in - not two minutes later, in the middle of their first
    /// customer. Ten seconds is enough for the dashboard to finish loading first.
    /// </summary>
    private static readonly TimeSpan FirstCheckAfter = TimeSpan.FromSeconds(10);

    private async Task UpdateLoopAsync()
    {
        // Let the dashboard finish loading first, so the two are not competing for a branch
        // connection at the one moment somebody is watching the screen.
        await Task.Delay(FirstCheckAfter);

        while (!IsDisposed)
        {
            try
            {
                await CheckForUpdateOnceAsync();
            }
            catch
            {
                // Never let an update check take the app down with it.
            }

            // Woken early when somebody presses "Check for updates now", so the button is a
            // real check rather than a message saying one will happen eventually.
            try { await Task.Delay(CheckEvery, _checkNow.Token); }
            catch (OperationCanceledException) { /* asked to look now */ }

            if (_checkNow.IsCancellationRequested)
            {
                var old = _checkNow;
                _checkNow = new CancellationTokenSource();
                old.Dispose();
            }
        }
    }

    /// <summary>
    /// Cancelled to cut the wait short when the Updates page asks for a check right now.
    /// Replaced each time, because a CancellationTokenSource cannot be un-cancelled.
    /// </summary>
    private CancellationTokenSource _checkNow = new();

    private async Task CheckForUpdateOnceAsync()
    {
        using var updates = new UpdateService(_config);

        var available = await updates.CheckAsync();
        if (available is null) return;

        var installer = await updates.DownloadAndVerifyAsync(available);
        if (installer is null) return;   // failed or, more importantly, failed verification

        // An update must never interrupt a customer mid-session. On a locked gaming PC that
        // means waiting: the next check comes round in four hours, and the verified download
        // is already cached, so nothing is wasted by deferring.
        if (_config.IsUserPc && await IsSessionRunningAsync()) return;

        BeginInvoke(() =>
        {
            // Written before anything is launched, and it has to be before: this process is
            // about to exit so the installer can replace its own executable, and the next
            // launch has no other way of knowing why the branch is unreachable. Without it
            // the operator meets "Cannot reach the branch system" and a list of things to
            // check, none of which are wrong.
            MarkUpdateStarting(available.Version);

            _allowClose = true;   // the installer needs this process gone to replace the exe

            if (!UpdateService.Install(installer))
            {
                // Refused at the UAC prompt or blocked by policy - nothing was stopped and
                // nothing will restart, so the marker would only produce a patient message
                // about an update that is not happening.
                ClearUpdateMarker();
                _allowClose = false;
                return;
            }

            Application.Exit();
        });
    }

    /// <summary>
    /// Whether someone is currently playing at this PC. Asked of the dashboard itself rather
    /// than guessed, and a failed answer counts as "yes" — interrupting a paying customer is
    /// far worse than postponing an update by four hours.
    /// </summary>
    private async Task<bool> IsSessionRunningAsync()
    {
        try
        {
            var result = await _web.CoreWebView2!.ExecuteScriptAsync(
                "(function(){try{return !!document.querySelector('[data-session-active=\"true\"]');}catch(e){return true;}})()");
            return result?.Trim() != "false";
        }
        catch
        {
            return true;
        }
    }

    private void Connect()
    {
        if (_web.CoreWebView2 is null) return;

        ShowOverlay($"Connecting to {HostLabel()}…", showActions: false);

        try
        {
            _web.CoreWebView2.Navigate(_config.NormalisedUrl());
        }
        catch (Exception ex)
        {
            ShowOverlay($"That server address doesn't look valid.\n\n{ex.Message}", showActions: true);
        }
    }

    /// <summary>
    /// What the title bar says about where this window is pointed.
    ///
    /// Deliberately says whether the branch is being run locally rather than just printing
    /// an address: a bare public IP in the title was what made it look — correctly, at the
    /// time — as though the shop were being run out of the cloud.
    /// </summary>
    private string HostLabel()
    {
        try
        {
            var uri = new Uri(_config.NormalisedUrl());
            var host = uri.Host;

            if (host is "localhost" or "127.0.0.1" or "::1")
                return "This PC";

            return IsShopNetwork(host)
                ? $"Counter PC · {uri.Authority}"
                : $"Head Office · {uri.Authority}";
        }
        catch
        {
            return _config.ServerUrl;
        }
    }

    /// <summary>
    /// True for the private address ranges a shop LAN uses. Anything else is out on the
    /// internet, which for day-to-day work means something is misconfigured.
    /// </summary>
    private static bool IsShopNetwork(string host)
    {
        if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;

        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;

        return b[0] == 10
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }

    private string DescribeError(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved or
        CoreWebView2WebErrorStatus.CannotConnect or
        CoreWebView2WebErrorStatus.ServerUnreachable =>
            $"Can't reach {HostLabel()}.\n\n" +
            "Check that this PC is on the network and that the server address below is right.",

        CoreWebView2WebErrorStatus.Timeout =>
            $"{HostLabel()} took too long to answer.\n\nThe server may be starting up — try again in a moment.",

        CoreWebView2WebErrorStatus.ConnectionAborted or
        CoreWebView2WebErrorStatus.ConnectionReset or
        CoreWebView2WebErrorStatus.Disconnected =>
            $"The connection to {HostLabel()} dropped.",

        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
        CoreWebView2WebErrorStatus.CertificateExpired or
        CoreWebView2WebErrorStatus.CertificateIsInvalid or
        CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or
        CoreWebView2WebErrorStatus.CertificateRevoked =>
            $"There's a problem with the security certificate on {HostLabel()}.",

        CoreWebView2WebErrorStatus.OperationCanceled => "Loading was cancelled.",

        _ => $"Couldn't load the dashboard from {HostLabel()}.\n\n({status})",
    };

    // ── Settings ──────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        using var dialog = new SettingsDialog(_config);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config.ServerUrl = dialog.ServerUrl;
        _config.GateUsername = dialog.GateUsername;
        _config.GatePassword = dialog.GatePassword;

        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save settings.\n\n{ex.Message}", "Apple Esports",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Connect();
    }

    // ── Keyboard ──────────────────────────────────────────────────────────

    // Keep this in step with SHORTCUT_KEYS.md — that file is what the branch staff are given.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                _web.CoreWebView2?.Reload();
                return true;

            case Keys.F11:
                ToggleFullScreen();
                return true;

            case Keys.Escape when _isFullScreen:
                ToggleFullScreen();
                return true;

            // ── Protected: each needs the admin PIN on a customer-facing PC ──

            case Keys.Control | Keys.Shift | Keys.S:
                if (Unlocked("change the server address")) OpenSettings();
                return true;

            case Keys.Control | Keys.Shift | Keys.P:
                if (Unlocked("change which PC this machine is set up as")) ChangePcSetup();
                return true;

            case Keys.Control | Keys.Shift | Keys.U:
                if (Unlocked("un-configure this machine")) Unconfigure();
                return true;

            case Keys.Control | Keys.Alt | Keys.Q:
                if (Unlocked("close Apple Esports")) ForceExit();
                return true;

            // Swallow Alt+F4 on a locked PC so a customer cannot reach the desktop.
            case Keys.Alt | Keys.F4 when _config.IsUserPc:
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Gate for anything that could unbind this machine or let a customer out to Windows.
    /// On an operator PC with no PIN set there is nothing to protect against, so it passes
    /// straight through. On a customer PC with no PIN set it refuses outright — an
    /// unprotected escape hatch on a public machine is worse than no shortcut at all.
    /// </summary>
    private bool Unlocked(string action)
    {
        if (string.IsNullOrEmpty(_config.AdminPin))
        {
            if (!_config.IsUserPc) return true;

            MessageBox.Show(this,
                $"No admin PIN is set on this PC, so it cannot be unlocked here.\n\n" +
                "Set one in AppleEsports.config.json, or ask a Super Admin.",
                "Apple Esports", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        using var prompt = new PinPromptDialog(action);
        if (prompt.ShowDialog(this) != DialogResult.OK) return false;

        if (prompt.Pin == _config.AdminPin) return true;

        MessageBox.Show(this, "Incorrect PIN.", "Apple Esports",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    private void ChangePcSetup()
    {
        using var dialog = new PcSetupDialog(_config);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config.PcNumber = dialog.PcNumber;
        _config.Role = dialog.Role;
        SaveConfig();

        MessageBox.Show(this,
            $"This machine is now set up as {_config.PcNumber} ({_config.Role}).\n\n" +
            "Apple Esports will restart to apply it.",
            "Apple Esports", MessageBoxButtons.OK, MessageBoxIcon.Information);

        Restart();
    }

    private void Unconfigure()
    {
        var confirm = MessageBox.Show(this,
            "Remove this machine's setup?\n\n" +
            "It will stop being assigned to a PC number and will ask to be set up again " +
            "next time it starts. Any session running here should be stopped first.",
            "Apple Esports", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes) return;

        _config.PcNumber = "";
        _config.Role = "operator";
        SaveConfig();

        Restart();
    }

    private void SaveConfig()
    {
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save settings.\n\n{ex.Message}", "Apple Esports",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Restart()
    {
        if (Environment.ProcessPath is { } exe)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });

        ForceExit();
    }

    private void ForceExit()
    {
        _allowClose = true;
        Application.Exit();
    }

    // Blocks the window's own close path (Alt+F4, taskbar, task switcher) on a locked PC.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_config.IsUserPc && !_allowClose && e.CloseReason is CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private void ToggleFullScreen()
    {
        if (_config.IsUserPc) return;   // already borderless and locked by design

        if (_isFullScreen)
        {
            FormBorderStyle = _borderBeforeFullScreen;
            WindowState = _stateBeforeFullScreen;
            _isFullScreen = false;
        }
        else
        {
            _borderBeforeFullScreen = FormBorderStyle;
            _stateBeforeFullScreen = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;   // forces a re-maximise below
            WindowState = FormWindowState.Maximized;
            _isFullScreen = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _web.Dispose();
        base.Dispose(disposing);
    }
}
