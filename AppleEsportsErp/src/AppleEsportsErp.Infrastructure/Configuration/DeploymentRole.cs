using Microsoft.Extensions.Configuration;

namespace AppleEsportsErp.Infrastructure.Configuration;

/// <summary>
/// Whether this instance is Head Office or a branch.
///
/// The same build runs in both roles, so several things need to ask, and they must all get
/// the same answer. They did not: email asked for a `Deployment:IsHeadOffice` flag that was
/// never set anywhere — not in appsettings, not in compose, not in any .env — so it read
/// false at Head Office too. Head Office therefore queued every email for "Head Office" to
/// send, and nobody ever did. Nothing errored; the mail simply never arrived.
///
/// The role is now derived from something that is always configured and cannot be forgotten:
/// a branch has an address to report to, and Head Office does not. One fact, one answer,
/// nothing to remember to set on a new deployment.
/// </summary>
public static class DeploymentRole
{
    /// <summary>
    /// True when this instance is Head Office — the one that reports to nobody, holds the
    /// master records, and is the only one that sends email out to the world.
    /// </summary>
    public static bool IsHeadOffice(this IConfiguration configuration)
    {
        // An explicit setting still wins, for the case where someone deliberately wants a
        // branch to behave as Head Office, or the reverse, while testing.
        if (bool.TryParse(configuration["Deployment:IsHeadOffice"], out var declared))
            return declared;

        return string.IsNullOrWhiteSpace(configuration["Sync:HeadOfficeUrl"]);
    }
}
