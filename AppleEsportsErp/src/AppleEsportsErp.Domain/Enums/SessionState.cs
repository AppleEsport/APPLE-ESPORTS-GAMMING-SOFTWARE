namespace AppleEsportsErp.Domain.Enums;

/// <summary>SOP §8: Session lifecycle states</summary>
public enum SessionState
{
    Active,
    Reserved,
    AwaitingBilling,
    Completed,
    Expired,

    /// <summary>
    /// The session was running when the system went down (power cut, restart) and has
    /// been put on hold. Its clock is stopped and no charge accrues. An operator either
    /// resumes it — the customer came back and still has their paid time — or stops it,
    /// billing only the minutes actually played. Never resumes on its own: the customer
    /// may well have gone home, and restarting the timer for an empty seat would charge
    /// them for nothing.
    ///
    /// Persisted as the lowercase string "interrupted" (see SessionConfiguration), which
    /// fits the column's 20-character limit.
    /// </summary>
    Interrupted
}
