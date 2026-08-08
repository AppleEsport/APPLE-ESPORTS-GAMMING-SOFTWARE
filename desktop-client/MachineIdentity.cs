using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace AppleEsports.Desktop;

/// <summary>
/// A stable fingerprint for this physical machine.
///
/// It has to survive a reinstall of the app, otherwise a repaired PC looks like a brand new
/// machine and Head Office refuses to give it back its own seat — the operator would be
/// stuck, because that seat is already claimed by what is really the same computer.
///
/// So it is derived from Windows' own installation id, which lives in the registry and
/// outlives our app, and falls back to a value we persist ourselves if that is unreadable.
/// Hashed rather than sent raw: Head Office only needs to tell machines apart, not to know
/// anything identifying about them.
/// </summary>
public static class MachineIdentity
{
    private static string FallbackPath => Path.Combine(AppConfig.AppDataDirectory, "machine-id");

    public static string Current()
    {
        var seed = ReadWindowsMachineGuid() ?? ReadOrCreateFallback();
        return Hash(seed);
    }

    /// <summary>
    /// Windows' own MachineGuid — written at install time and untouched by our installer,
    /// so it is the same before and after we are reinstalled.
    /// </summary>
    private static string? ReadWindowsMachineGuid()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

            var value = key?.GetValue("MachineGuid") as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            // Locked-down machine or a policy blocking the read — fall through.
            return null;
        }
    }

    /// <summary>
    /// Last resort. Kept in AppData rather than beside the executable so it survives an
    /// upgrade that replaces Program Files.
    /// </summary>
    private static string ReadOrCreateFallback()
    {
        try
        {
            if (File.Exists(FallbackPath))
            {
                var existing = File.ReadAllText(FallbackPath).Trim();
                if (existing.Length > 0) return existing;
            }

            Directory.CreateDirectory(AppConfig.AppDataDirectory);
            var generated = Guid.NewGuid().ToString();
            File.WriteAllText(FallbackPath, generated);
            return generated;
        }
        catch
        {
            // Cannot read or write anything: better a per-run id than a crash during setup.
            // The operator will be asked to set the PC up again, which is recoverable.
            return Guid.NewGuid().ToString();
        }
    }

    private static string Hash(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("AppleEsports:" + seed));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }
}
