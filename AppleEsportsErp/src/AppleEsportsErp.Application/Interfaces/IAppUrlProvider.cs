namespace AppleEsportsErp.Application.Interfaces;

/// <summary>
/// Single source of truth for this deployment's own public address — the base every
/// customer-facing link in an email is built from (password reset, welcome/setup).
///
/// Lives in one place because the link is useless if it points anywhere but the address
/// the recipient can actually reach: a member opens it on their phone, off-site, so it
/// must resolve to a publicly reachable host, never a machine-local or LAN-only one.
/// </summary>
public interface IAppUrlProvider
{
    /// <summary>
    /// Base URL of this deployment's frontend, with any trailing slash removed
    /// (e.g. "http://140.245.195.222:8081"). Never returns null or empty.
    /// </summary>
    string GetFrontendBaseUrl();

    /// <summary>
    /// Absolute password-reset/setup link for the given email + token, ready to drop
    /// into an email template.
    /// </summary>
    string BuildResetPasswordLink(string email, string token);
}
