using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Interfaces;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// Resolves this deployment's public base URL from configuration.
///
/// Note on the fallback: it is deliberately noisy. A misconfigured base URL produces links
/// that look fine in the log but are dead for the recipient, so a silent default is the
/// worst outcome — it hides the failure until a customer reports it. Emails still send
/// (better than losing them entirely), but every send warns so it surfaces in the logs.
/// </summary>
public class AppUrlProvider : IAppUrlProvider
{
    private const string LastResortBaseUrl = "http://localhost:5173";

    private readonly IConfiguration _configuration;
    private readonly ILogger<AppUrlProvider> _logger;

    public AppUrlProvider(IConfiguration configuration, ILogger<AppUrlProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string GetFrontendBaseUrl()
    {
        // Docker Compose interpolates an undefined ${VAR} to an empty string, and an empty
        // env var still overrides the appsettings.json value — so "present but blank" has to
        // be treated as unset here, not just null.
        var configuredBaseUrl = _configuration["App:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            return configuredBaseUrl.Trim().TrimEnd('/');

        var frontendUrl = _configuration["FRONTEND_URL"];
        if (!string.IsNullOrWhiteSpace(frontendUrl))
            return frontendUrl.Trim().TrimEnd('/');

        _logger.LogWarning(
            "App:BaseUrl (env APP_BASE_URL) is not configured. Falling back to {Fallback}, which is " +
            "only reachable from this machine — any email link built from it will be dead for " +
            "recipients on other devices. Set APP_BASE_URL to this deployment's public address.",
            LastResortBaseUrl);

        return LastResortBaseUrl;
    }

    public string BuildResetPasswordLink(string email, string token)
    {
        var baseUrl = GetFrontendBaseUrl();
        return $"{baseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}
