namespace AppleEsportsErp.Application.Interfaces;

/// <summary>
/// Sends an email to whoever ought to know — the owner and any admins.
///
/// Exists because there were two separate answers to "who is an admin", and both were wrong
/// on the live system. Operator alerts went to a `receivers` string in system settings that
/// nobody had filled in. Member alerts went to operators flagged IsGlobalAdmin, of which
/// there are none, because the super admin is a row in `users`, not in `operators`. Every
/// one of those emails was addressed to an empty list and dropped without a word.
/// </summary>
public interface IAdminNotifier
{
    /// <summary>
    /// Emails every admin recipient. Never throws: an alert that cannot be delivered must
    /// not roll back the takings, the shift close, or whatever prompted it.
    /// </summary>
    Task NotifyAsync(string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emails the people who run the branches — every active operator, plus the admins.
    ///
    /// Separate from <see cref="NotifyAsync"/> because the audiences genuinely differ. Alerts
    /// travel upward: a shortfall or an outage is the owner's business, not something to copy
    /// to sixteen operators. This one travels downward — "an update is waiting for you" is
    /// useless to the owner alone, because the owner is not the person who has to act on it.
    ///
    /// Also never throws.
    /// </summary>
    Task NotifyOperatorsAsync(string subject, string htmlBody, CancellationToken cancellationToken = default);
}
