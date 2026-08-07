// Mirrors AppleEsportsErp.Application.Constants.MemberWalletRules (backend) so the overlay
// stops/warns at exactly the same balances the server refuses to start a session at.
// Change both together.

// A member session cannot start below this Gaming balance — and is auto-stopped once the
// remaining balance falls under it, so a session can never start only to stop immediately.
export const MIN_GAMING_BALANCE_TO_START = 1;
export const AUTO_STOP_REMAINING_BALANCE = 1;

// Two reminders, in the order they fire. The first also triggers the member email +
// operator alert on the backend.
export const FIRST_REMINDER_REMAINING_BALANCE = 20;
export const SECOND_REMINDER_REMAINING_BALANCE = 10;

// Minutes of play a remaining balance still buys at the PC's hourly rate.
export function minutesRemainingFor(remainingBalance, ratePerHour) {
  const rate = Number(ratePerHour) || 0;
  const remaining = Number(remainingBalance) || 0;
  if (rate <= 0 || remaining <= 0) return 0;
  return Math.max(0, Math.floor((remaining / rate) * 60));
}
