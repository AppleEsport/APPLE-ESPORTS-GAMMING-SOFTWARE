// Turns one audit row into a plain-English sentence, so the Audit Trail screen reads like a
// story of the day instead of a table of codes and JSON.
//
// Deliberately not exhaustive. The action types below cover what actually happens dozens of
// times a day - sessions, payments, wallets, members, logins, remote commands. Anything else
// falls back to a readable title made from its own action code plus its raw details, which is
// less polished but never blank and never wrong.

const money = (v) => (v === undefined || v === null || v === '') ? '?' : `₹${Number(v).toFixed(0)}`;

const REMOTE_COMMAND_LABELS = {
  stop_session: 'stop a session',
  start_session: 'start a session',
  transfer_session: 'move a session to another PC',
  set_pc_state: "change a PC's status",
};

// One function per action code. Each takes the row's own `details` (already-parsed JSON,
// possibly null) and returns the sentence's body - the subject ("X ") is added by the caller,
// since who did it is shown as its own column and repeating the name in every sentence would
// just be noise.
const SUMMARIES = {
  login: () => 'logged in',
  logout: () => 'logged out',
  failed_login: (d) => `tried to log in and failed${d?.reason ? ` (${d.reason})` : ''}`,
  account_locked: (d) => `was locked out${d?.reason ? ` — ${d.reason}` : ''}`,
  password_reset: () => 'reset their password',
  forced_logout: () => 'was signed out by an admin',
  admin_switch_in: (d) => `switched into ${d?.operatorName ?? 'an operator'}'s session`,
  admin_switch_out: () => 'ended an admin override session',

  session_start: (d) => `started a session on ${d?.PcNumber ?? 'a PC'}${d?.DurationMinutes ? ` for ${d.DurationMinutes} min` : ''}${d?.ExpectedAmount ? `, ${money(d.ExpectedAmount)}` : ''}`,
  session_stop: (d) => `stopped the session on ${d?.PcNumber ?? 'a PC'}${d?.TotalAmount !== undefined ? ` — billed ${money(d.TotalAmount)}` : ''}`,
  session_extend: (d) => `extended ${d?.PcNumber ?? 'a PC'} by ${d?.AdditionalMinutes ?? '?'} min (+${money(d?.AdditionalAmount)})`,
  session_transfer: (d) => `moved a session from ${d?.from ?? '?'} to ${d?.to ?? '?'}`,

  reservation_create: (d) => `booked ${d?.PcNumber ?? 'a PC'} for ${d?.CustomerName ?? 'a customer'}`,
  reservation_cancel: (d) => `cancelled a reservation${d?.Reason ? ` (${d.Reason})` : ''}`,
  reservation_override: () => 'overrode a reservation hold to start a walk-in session',
  reservation_expire: (d) => `let a reservation expire${d?.PcNumber ? ` on ${d.PcNumber}` : ''}`,

  bill_create: () => 'opened a bill',
  bill_complete: (d) => `completed bill ${d?.BillNumber ?? ''}`.trim(),
  payment_process: (d) => `took a payment — ${d?.PaymentType ?? 'payment'}, ${money(d?.Total)}`,
  discount_apply: (d) => `applied a ${d?.DiscountType === 'Percentage' ? `${d?.Value}%` : money(d?.Value)} discount${d?.Reason ? ` (${d.Reason})` : ''}`,

  cash_opening: () => 'opened the cash drawer',
  cash_verification: () => 'counted the cash drawer',
  cash_mismatch: (d) => `found the drawer ${d?.difference ? `off by ${money(Math.abs(d.difference))}` : 'did not match'}`,
  denomination_count: () => 'counted the drawer\'s notes and coins',

  member_create: (d) => `registered member ${d?.FullName ?? ''} (${d?.MemberNumber ?? '?'})`,
  wallet_recharge: (d) => `topped up a wallet by ${money(d?.Amount)}${d?.PaymentType ? ` (${d.PaymentType})` : ''}`,
  wallet_deduction: (d) => `deducted ${money(d?.Amount)} from a wallet${d?.Reason ? ` (${d.Reason})` : ''}`,
  points_redeem: (d) => `redeemed ${d?.Points ?? ''} loyalty points`.trim(),

  operator_create: (d) => `added operator ${d?.FullName ?? ''}`.trim(),
  operator_remove: (d) => `removed operator ${d?.FullName ?? ''}`.trim(),
  operator_suspend: (d) => `suspended operator ${d?.FullName ?? ''}`.trim(),
  access_grant: (d) => `granted ${d?.Dashboard ?? 'a permission'}`,
  access_revoke: (d) => `revoked ${d?.Dashboard ?? 'a permission'}`,

  stock_refill: (d) => `refilled stock on ${d?.ItemName ?? 'an item'}`,
  price_change: (d) => `changed the price of ${d?.ItemName ?? 'an item'}${d?.NewPrice ? ` to ${money(d.NewPrice)}` : ''}`,
  item_disable: (d) => `took ${d?.ItemName ?? 'an item'} off the menu`,
  wastage_log: (d) => `logged wastage on ${d?.ItemName ?? 'an item'}`,

  shift_start: () => 'started a shift',
  shift_end: () => 'ended a shift',
  shift_takeover: (d) => `closed a shift left open by ${d?.previousOperatorName ?? 'someone else'} and counted its drawer`,
  eod_finalize: () => 'finalised End of Day',
  force_close: (d) => `force-closed a shift${d?.reason ? ` (${d.reason})` : ''}`,
  settings_change: (d) => `changed a setting${d?.Key ? `: ${d.Key}` : ''}`,

  remote_command_issued: (d) => {
    const label = REMOTE_COMMAND_LABELS[d?.commandType] ?? d?.commandType ?? 'do something';
    const status = d?.branchReporting === false ? ' (branch was offline at the time)' : '';
    return `asked a branch, from Head Office, to ${label}${status}`;
  },
};

const titleCase = (action) =>
  action.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());

/** Parses the row's Details JSON once, tolerating null/invalid without throwing. */
export function parseDetails(rawDetails) {
  if (!rawDetails) return null;
  try {
    return JSON.parse(rawDetails);
  } catch {
    return null;
  }
}

/** One plain-English sentence body ("started a session on...") for a row. Never throws. */
export function summarize(action, details) {
  const fn = SUMMARIES[action];
  if (fn) {
    try {
      return fn(details ?? {});
    } catch {
      // A summary function reaching for a field this particular row does not have - fall
      // through to the generic form rather than showing nothing.
    }
  }

  if (!details || Object.keys(details).length === 0) return titleCase(action);

  const pairs = Object.entries(details)
    .filter(([, v]) => v !== null && v !== undefined && v !== '')
    .slice(0, 4)
    .map(([k, v]) => `${k}: ${typeof v === 'object' ? JSON.stringify(v) : v}`)
    .join(', ');

  return pairs ? `${titleCase(action)} — ${pairs}` : titleCase(action);
}

/** For the action filter dropdown - every code this file knows how to describe, readably labelled. */
export const KNOWN_ACTIONS = Object.keys(SUMMARIES).map((value) => ({ value, label: titleCase(value) }));
