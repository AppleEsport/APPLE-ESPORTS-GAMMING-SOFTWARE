using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Interfaces;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// Works out what address to put in an email so the link actually opens something.
///
/// Every password reset and member welcome mail sent from this deployment pointed at
/// http://localhost:5173. On the server that is a perfectly valid address; in somebody's inbox
/// it means "this computer", so it opened nothing on the phone or laptop that received it. The
/// mail sent, the log looked clean, and the recipient just could not use it.
///
/// The configured value is still the right answer and is still preferred - it is the only one
/// that can be correct for mail sent by a background job, where there is no request to learn
/// from. What has changed is the last resort. Instead of a hardcoded localhost, it now uses the
/// address the caller themselves reached this server on: if an admin is looking at
/// https://erp.example.com and triggers a reset, that host is demonstrably reachable from
/// outside, because a request just arrived over it. It is not a guess.
/// </summary>
public class AppUrlProvider : IAppUrlProvider
{
    private const string LastResortBaseUrl = "http://localhost:5173";

    private readonly IConfiguration _configuration;
    private readonly ICurrentRequestUrl _currentRequest;
    private readonly ILogger<AppUrlProvider> _logger;

    public AppUrlProvider(
        IConfiguration configuration,
        ICurrentRequestUrl currentRequest,
        ILogger<AppUrlProvider> logger)
    {
        _configuration = configuration;
        _currentRequest = currentRequest;
        _logger = logger;
    }

    public string GetFrontendBaseUrl()
    {
        // Docker Compose interpolates an undefined ${VAR} to an empty string, and an empty
        // env var still overrides the appsettings.json value — so "present but blank" has to
        // be treated as unset here, not just null.
        //
        // The localhost default shipped in appsettings.json counts as unset too. It is there so
        // a developer can run the stack with no environment at all, and in production it is
        // indistinguishable from having forgotten to set APP_BASE_URL — which is exactly what
        // happened. Treating it as a real answer is what put localhost in customers' inboxes.
        var configuredBaseUrl = _configuration["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl) && !IsLoopback(configuredBaseUrl))
            return configuredBaseUrl.Trim().TrimEnd('/');

        var frontendUrl = _configuration["FRONTEND_URL"];
        if (!string.IsNullOrWhiteSpace(frontendUrl) && !IsLoopback(frontendUrl))
            return frontendUrl.Trim().TrimEnd('/');

        // Whatever address this request came in on. Correct by construction for anything a
        // person triggered from a screen, which is nearly all of this mail.
        var fromRequest = _currentRequest.BaseUrl;
        if (!string.IsNullOrWhiteSpace(fromRequest))
        {
            if (!IsLoopback(fromRequest))
            {
                _logger.LogInformation(
                    "App:BaseUrl is not set to a reachable address, so email links are being built " +
                    "from the address this request arrived on ({BaseUrl}). Set APP_BASE_URL to make " +
                    "this deterministic, and so that mail sent by background jobs is correct too.",
                    fromRequest);

                return fromRequest;
            }
        }

        _logger.LogWarning(
            "App:BaseUrl (env APP_BASE_URL) is not configured and there is no request to learn " +
            "from, so links fall back to {Fallback} - only reachable from the server itself, and " +
            "dead in the recipient's inbox. Set APP_BASE_URL to this deployment's public address.",
            LastResortBaseUrl);

        return LastResortBaseUrl;
    }

    /// <summary>
    /// Whether an address only means anything on the machine that produced it. These are the
    /// values that send a dead link without ever looking like a failure.
    /// </summary>
    private static bool IsLoopback(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;

        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.StartsWith("127.", StringComparison.Ordinal)
            || uri.Host == "0.0.0.0"
            || uri.Host == "[::1]";
    }

    public string BuildResetPasswordLink(string email, string token)
    {
        var baseUrl = GetFrontendBaseUrl();
        return $"{baseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}
