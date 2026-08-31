using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using System.Windows.Input;

namespace AppleEsportsErp.ClientAgent.Views;

/// <summary>
/// Lock Screen code-behind — manages UI state, timer display, and connection status.
/// The actual session control and dual-connection logic lives in the Services.
/// </summary>
public partial class LockScreen : Window
{
    private readonly DispatcherTimer _sessionTimer;
    private readonly Services.SystemLockService _systemLock;
    private readonly Services.DualConnectionService _dualConnection;
    private readonly Services.SessionControlService _sessionControl;
    private int _remainingSeconds = 0;

    public LockScreen()
    {
        InitializeComponent();

        // Set PC number from config
        PcNumberText.Text = App.AgentConfig.PcNumber;

        // Read straight from the assembly, never a hand-typed string - see the comment on
        // VersionText in the XAML for what this replaces.
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? "© Apple Esports ERP — Gaming PC Agent"
            : $"© Apple Esports ERP — Gaming PC Agent v{version.Major}.{version.Minor}.{version.Build}";

        // Start glow animation
        var storyboard = (Storyboard)FindResource("PulseAnimation");
        storyboard.Begin();

        // Initialize services
        _systemLock = new Services.SystemLockService();
        _sessionControl = new Services.SessionControlService(this);
        _dualConnection = new Services.DualConnectionService(this, _sessionControl);

        // Session countdown timer
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += SessionTimer_Tick;

        // Lock the system on startup
        _systemLock.EnableLock();

        // Start dual connection
        _ = _dualConnection.StartAsync();

        // Prevent closing via Alt+F4
        Closing += (s, e) => e.Cancel = true;

        // The one way out of a locked machine (Ctrl+Shift+Alt+U), behind the admin PIN.
        KeyDown += LockScreen_KeyDown;
    }

    /// <summary>
    /// The escape hatch. It used to call DisableLock() and Shutdown() the moment the keys were
    /// pressed, with no check of any kind — anyone who knew the combination was out to the
    /// Windows desktop of a machine sitting in front of the public. It now goes through the same
    /// PIN gate as every other way out, and refuses outright when no PIN has been set.
    /// </summary>
    private void LockScreen_KeyDown(object sender, KeyEventArgs e)
    {
        // With Alt held, WPF reports the key as Key.System and puts the real one in SystemKey.
        // Reading e.Key alone means an Alt combination is never recognised — the same class of
        // fault as the counter shell's quit shortcut, which silently did nothing for weeks.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key != Key.U) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;

        e.Handled = true;

        if (!Services.AdminPinService.Current.RequirePin(this, "quit Apple Esports and unlock this PC"))
            return;

        _systemLock.DisableLock();
        Application.Current.Shutdown();
    }

    /// <summary>Called by SessionControlService when an unlock command is received</summary>
    public void UnlockPc(int durationMinutes, string? customerName)
    {
        Dispatcher.Invoke(() =>
        {
            _systemLock.DisableLock();

            if (durationMinutes > 0)
            {
                _remainingSeconds = durationMinutes * 60;
                TimerText.Visibility = Visibility.Visible;
                _sessionTimer.Start();
            }

            // Hide the lock screen (don't close — we need to show it again later)
            Hide();
        });
    }

    /// <summary>Called by SessionControlService when a lock command is received</summary>
    public void LockPc()
    {
        Dispatcher.Invoke(() =>
        {
            _sessionTimer.Stop();
            TimerText.Visibility = Visibility.Collapsed;
            _remainingSeconds = 0;

            _systemLock.EnableLock();
            Show();
            Activate();
            Topmost = true;
        });
    }

    /// <summary>Update the connection status indicator</summary>
    public void UpdateConnectionStatus(string mode, bool isConnected)
    {
        Dispatcher.Invoke(() =>
        {
            if (isConnected)
            {
                StatusDot.Fill = mode == "LAN" 
                    ? new SolidColorBrush(Color.FromRgb(0, 255, 136))   // Green for LAN
                    : new SolidColorBrush(Color.FromRgb(255, 165, 0));  // Orange for Cloud
                StatusText.Text = mode == "LAN" 
                    ? "Connected — LAN Mode" 
                    : "Connected — ☁️ Cloud Mode (Operator offline)";
            }
            else
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 50, 50)); // Red
                StatusText.Text = "Disconnected — Attempting to reconnect...";
            }
        });
    }

    private void SessionTimer_Tick(object? sender, EventArgs e)
    {
        _remainingSeconds--;

        if (_remainingSeconds <= 0)
        {
            // Time's up — auto-lock
            _sessionTimer.Stop();
            LockPc();
            _ = _dualConnection.NotifySessionExpired();
            return;
        }

        // Update timer display
        var hours = _remainingSeconds / 3600;
        var minutes = (_remainingSeconds % 3600) / 60;
        var seconds = _remainingSeconds % 60;

        TimerText.Text = hours > 0 
            ? $"{hours:D2}:{minutes:D2}:{seconds:D2}" 
            : $"{minutes:D2}:{seconds:D2}";

        // Flash timer red when less than 5 minutes remain
        if (_remainingSeconds <= 300)
        {
            TimerText.Foreground = _remainingSeconds % 2 == 0
                ? new SolidColorBrush(Color.FromRgb(255, 50, 50))
                : new SolidColorBrush(Color.FromRgb(255, 215, 0));
        }
    }
}
