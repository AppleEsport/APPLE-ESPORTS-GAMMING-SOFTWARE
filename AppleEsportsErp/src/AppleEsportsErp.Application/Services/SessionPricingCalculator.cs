namespace AppleEsportsErp.Application.Services;

/// <summary>
/// Single source of truth for "what does this session cost right now".
/// Used identically by: final billing on session stop, the live PC-status feed
/// (operator PC cards, member overlay), and the billing counter — so every
/// screen always shows the same number for the same session.
/// </summary>
public static class SessionPricingCalculator
{
    // No fabricated fallback rate — a PC is required to have a Pricing Profile (enforced at
    // session-start and PC-creation time). If one is ever missing anyway, showing/charging ₹0
    // is the honest behavior — inventing a number is worse than admitting it's unconfigured.
    public const decimal DefaultRatePerHour = 0m;
    public const int DefaultBufferMinutes = 10;

    /// <summary>
    /// Gaming charge only (excludes food/add-ons). Free during the buffer window,
    /// then billed for exact elapsed time at the given hourly rate — regardless of
    /// whether the session was booked as a fixed package or open/Pay-As-You-Go.
    /// </summary>
    public static decimal CalculateGamingAmount(decimal ratePerHour, int bufferMinutes, decimal elapsedMinutes)
    {
        if (elapsedMinutes <= bufferMinutes)
            return 0m;

        decimal hours = elapsedMinutes / 60m;
        return Math.Round(hours * ratePerHour, 2);
    }

    /// <summary>
    /// Rounds a final bill total to the nearest ₹10 — down for a remainder of 0-5,
    /// up for 6-9 — so the amount actually charged never lands on an awkward figure.
    /// Applied only at the point a bill's TotalAmount is finalized; never on line
    /// items, Subtotal, DiscountAmount, or wallet top-ups.
    /// </summary>
    public static decimal RoundBillTotal(decimal amount)
    {
        if (amount <= 0m) return 0m;

        decimal remainder = amount % 10m;
        return remainder <= 5m
            ? amount - remainder
            : amount + (10m - remainder);
    }
}
