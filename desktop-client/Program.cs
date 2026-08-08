using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace AppleEsports.Desktop;

internal static class Program
{
    private const string WebView2DownloadUrl =
        "https://developer.microsoft.com/microsoft-edge/webview2/?form=MA13LH#download";

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Never fail silently. A crash the user can't see reads as "the exe won't open".
        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        if (!WebViewRuntimeIsPresent()) return;

        try
        {
            var config = AppConfig.Load();

            // An unclaimed machine must never reach the dashboard. Head Office keeps its PC
            // in "awaiting setup" and will not let it hold a session, so showing the
            // dashboard would only invite an operator to seat a customer at a screen that
            // can never unlock. Setup first, every time, until it succeeds.
            if (!config.IsSetUp)
            {
                using var wizard = new SetupWizard(config);
                if (wizard.ShowDialog() != DialogResult.OK)
                    return;   // cancelled — nothing was written, so it will ask again next time

                config = AppConfig.Load();
            }

            Application.Run(new MainForm(config));
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    /// <summary>
    /// WebView2 ships with Windows 11 and current Windows 10, but a stripped or very old
    /// image may not have it. Detect that up front and say so plainly.
    /// </summary>
    private static bool WebViewRuntimeIsPresent()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrEmpty(version)) return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // fall through to the prompt below
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
            return false;
        }

        var answer = MessageBox.Show(
            "Apple Esports needs the Microsoft Edge WebView2 Runtime, which is not installed " +
            "on this PC.\n\nIt is a free Microsoft component and installs in about a minute.\n\n" +
            "Open the download page now?",
            "Apple Esports — one-time setup needed",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (answer == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(WebView2DownloadUrl) { UseShellExecute = true });
            }
            catch
            {
                // No default browser configured — nothing useful left to do.
            }
        }
        return false;
    }

    private static void ReportFatal(Exception? ex)
    {
        try
        {
            var log = Path.Combine(AppConfig.AppDataDirectory, "crash.log");
            Directory.CreateDirectory(AppConfig.AppDataDirectory);
            File.AppendAllText(log, $"[{DateTime.Now:u}] {ex}{Environment.NewLine}{Environment.NewLine}");

            MessageBox.Show(
                $"Apple Esports hit an unexpected problem and has to close.\n\n{ex?.Message}\n\n" +
                $"Details were written to:\n{log}",
                "Apple Esports",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Reporting the failure must not itself throw.
        }
    }
}
