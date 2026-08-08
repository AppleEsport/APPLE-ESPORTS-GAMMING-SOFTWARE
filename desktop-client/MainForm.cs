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

        if (_config.Kiosk)
        {
            FormBorderStyle = FormBorderStyle.None;
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

            case Keys.Control | Keys.Shift | Keys.S:
                OpenSettings();
                return true;

            case Keys.Escape when _isFullScreen:
                ToggleFullScreen();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleFullScreen()
    {
        if (_config.Kiosk) return;   // already borderless by design

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
