namespace AppleEsportsErp.Application.Constants;

/// <summary>
/// Thresholds that govern a member's Gaming-wallet-funded session. The customer overlay
/// mirrors these in client/src/utils/memberWalletRules.js — change both together.
/// </summary>
public static class MemberWalletRules
{
    /// <summary>
    /// A member session cannot be started below this Gaming balance. Matches the auto-stop
    /// floor, so a session can never start only to be stopped on its first tick.
    /// </summary>
    public const decimal MinimumGamingBalanceToStart = 1m;

    /// <summary>Remaining Gaming balance at which the session is stopped automatically.</summary>
    public const decimal AutoStopRemainingBalance = 1m;

    /// <summary>First low-balance reminder — also triggers the member email + operator alert.</summary>
    public const decimal FirstReminderRemainingBalance = 20m;

    /// <summary>Second (final) low-balance reminder to the member.</summary>
    public const decimal SecondReminderRemainingBalance = 10m;
}
