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

    public MainForm(AppConfig config)
    {
        _config = config;

        Text = "Apple Esports ERP";
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
                HideOverlay();
            }
            else
            {
                ShowOverlay(DescribeError(args.WebErrorStatus), showActions: true);
            }
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

        Connect();

        // Fire and forget. An update check must never delay the dashboard appearing — the
        // shop opens whether or not Head Office is reachable.
        _ = Task.Run(UpdateLoopAsync);
    }

    /// <summary>
    /// Checks Head Office for an approved update, then again every few hours.
    ///
    /// Runs for the life of the app rather than only at launch: a branch PC can stay on for
    /// days, and a fix nobody restarts to collect is a fix nobody has.
    /// </summary>
    private async Task UpdateLoopAsync()
    {
        // Let the dashboard settle first. Competing with page load for bandwidth on a branch
        // connection is a poor trade for something with no deadline.
        await Task.Delay(TimeSpan.FromMinutes(2));

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

            await Task.Delay(TimeSpan.FromHours(4));
        }
    }

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
            _allowClose = true;   // the installer needs this process gone to replace the exe
            UpdateService.Install(installer);
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

    private string HostLabel()
    {
        try
        {
            return new Uri(_config.NormalisedUrl()).Authority;
        }
        catch
        {
            return _config.ServerUrl;
        }
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
