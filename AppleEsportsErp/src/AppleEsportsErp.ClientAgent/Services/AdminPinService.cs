using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace AppleEsportsErp.ClientAgent.Services;

/// <summary>
/// The single gate in front of anything that lets somebody out of a locked gaming PC.
///
/// There is deliberately one of these and every escape route goes through it — the quit
/// shortcut today, the close affordance and the setup dialog when they arrive. A second door
/// with its own guard is how one of them ends up unguarded, which is exactly the state this
/// class was written to fix: the quit shortcut used to call DisableLock() and Shutdown()
/// outright, so anyone who knew the key combination was out to the desktop.
///
/// The PIN is never stored, only a PBKDF2 hash of it. Where that hash is read from is behind a
/// delegate because it moves: it comes from the agent's config file today and from
/// pc_identity.admin_pin_hash in the local SQLite database once that exists. Nothing that calls
/// RequirePin needs to change when it does.
/// </summary>
public sealed class AdminPinService
{
    /// <summary>Set once at startup in App.OnStartup.</summary>
    public static AdminPinService Current { get; set; } = new(() => null);

    // Enough to make guessing a 4-6 digit PIN at a keyboard pointless without making a correct
    // PIN feel slow. Five wrong tries is well past a typo and well short of an honest mistake
    // locking staff out for long.
    private const int MaxAttemptsBeforeCooldown = 5;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    private readonly Func<string?> _storedHash;
    private int _failures;
    private DateTimeOffset _lockedOutUntil = DateTimeOffset.MinValue;

    public AdminPinService(Func<string?> storedHash) => _storedHash = storedHash;

    /// <summary>False when no PIN has ever been set on this machine.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_storedHash());

    /// <summary>
    /// Asks for the PIN and says whether the caller may proceed.
    ///
    /// With no PIN configured this refuses outright rather than passing through. An escape
    /// hatch that anybody can use on a machine sitting in front of the public is worse than no
    /// escape hatch at all — the same rule the counter shell already applies to a customer PC.
    /// </summary>
    /// <param name="owner">Window to centre the prompt on — the lock screen is topmost.</param>
    /// <param name="action">What the PIN is being asked for, in plain words, e.g. "quit Apple Esports".</param>
    public bool RequirePin(Window? owner, string action)
    {
        if (!IsConfigured)
        {
            MessageBox.Show(owner!,
                "No admin PIN is set on this PC, so it cannot be unlocked here.\n\n" +
                "Ask a Super Admin to set one during setup.",
                "Apple Esports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var waitFor = _lockedOutUntil - DateTimeOffset.UtcNow;
        if (waitFor > TimeSpan.Zero)
        {
            MessageBox.Show(owner!,
                $"Too many incorrect PINs.\n\nTry again in {Math.Ceiling(waitFor.TotalSeconds)} seconds.",
                "Apple Esports", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var prompt = new Views.PinPromptDialog(action);
        if (owner != null && owner.IsVisible) prompt.Owner = owner;

        if (prompt.ShowDialog() != true) return false;   // cancelled — not a failed attempt

        if (Verify(prompt.Pin, _storedHash()))
        {
            _failures = 0;
            return true;
        }

        _failures++;
        if (_failures >= MaxAttemptsBeforeCooldown)
        {
            _lockedOutUntil = DateTimeOffset.UtcNow.Add(Cooldown);
            _failures = 0;
            MessageBox.Show(owner!,
                $"Incorrect PIN.\n\nToo many attempts — locked for {Cooldown.TotalSeconds:0} seconds.",
                "Apple Esports", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(owner!, "Incorrect PIN.", "Apple Esports",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return false;
    }

    // ── Hashing ───────────────────────────────────────────────────────────
    //
    // Format: pbkdf2$<iterations>$<salt base64>$<hash base64>. Self-describing, so raising the
    // iteration count later does not strand PINs already set — an old hash still says how it
    // was made and still verifies.

    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt, Iterations);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrEmpty(pin)) return false;

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(pin, salt, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, length);
}
