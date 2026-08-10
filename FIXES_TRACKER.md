# Fixes & Errors Tracker

How to use this file:
- Every time you find something broken or want something changed, add a new numbered entry under "New Issues" using the template below.
- Keep each entry SHORT and in your own words — you don't need technical language, just describe what you saw and what you expected.
- When you give me this file, I'll go through the entries **one at a time**, ask you questions if anything is unclear, fix it, tell you in plain English what was wrong and what I changed, then move to the next one.
- Once an issue is fixed and confirmed by you, I'll move it down to "Fixed" so this file stays a clean history.

---

## Template (copy this for each new issue)

```
### Issue #__
- Where: (e.g. "Adajan branch, PC 2, Operator dashboard")
- What I did: (steps you took, e.g. "clicked Start > Pay As You Go")
- What happened: (the bug/error you saw)
- What should happen instead:
- Priority: (Urgent / Normal / Whenever)
```

---

## New Issues (not fixed yet)

(none currently)

## Fixed (history log)

### Issue #23 — Member "Forgot Password" was silently resetting the wrong account (2026-08-11)
- **Problem:** Using "Forgot Password" on a member login screen with an email that's also used by a staff (Operator/Admin) account reset the staff account's password instead — the member's password never changed, so login kept failing with "invalid username or password" no matter what new password was set.
- **Root cause:** `/auth/forgot-password` and `/auth/reset-password` are shared by every login screen (member and staff alike) and only ever took an email — the backend resolved it by checking Users → Operators → Members in that fixed order and always acted on the first match, with no idea which screen the request came from. Separately, `InitiatePasswordResetAsync`'s member lookup used `FirstOrDefaultAsync` with no ordering, so even a member-only reset could land on a stale/suspended duplicate account instead of the active one if duplicates shared the same email.
- **Fix:** Added an `accountType` ("member" or "staff") field to `ForgotPasswordDto`/`ResetPasswordDto`. The 3 member-facing screens (`MemberLoginPage.jsx`, `OverlayMemberLoginScreen.jsx`, `PcLockScreen.jsx`) now send `accountType: 'member'` and the backend only searches the Members table for those; `ForgotPasswordPage.jsx` (reached from Operator/Admin/SuperAdmin login) sends `accountType: 'staff'` and the backend skips Members entirely for those. Also fixed the member lookup to prefer the active account (`OrderByDescending(Status == Active)`) when duplicate member rows share an email.
- **Verified:** Reproduced the bug live (reset was landing on operator `meet_citylight` instead of the member account), then confirmed the full forgot-password → reset-password → login flow now correctly targets and updates the member account only.
- **Files changed:** `AppleEsportsErp/src/AppleEsportsErp.Application/DTOs/Auth/AuthDtos.cs`, `AppleEsportsErp/src/AppleEsportsErp.Application/Interfaces/IAuthService.cs`, `AppleEsportsErp/src/AppleEsportsErp.Infrastructure/Services/AuthService.cs`, `AppleEsportsErp/src/AppleEsportsErp.Api/Controllers/AuthController.cs`, `client/src/api/auth.api.js`, `client/src/pages/public/MemberLoginPage.jsx`, `client/src/pages/public/ForgotPasswordPage.jsx`, `client/src/pages/overlay/screens/OverlayMemberLoginScreen.jsx`, `client/src/pages/overlay/components/PcLockScreen.jsx`

### Issue #22 — No "Forgot Password?" on the PC kiosk member login screen (2026-08-11)
- **Problem:** The member login screen actually shown on the gaming station (the full-screen lock screen before a session starts) had no way to reset a forgotten password — only username/password fields, no link.
- **Root cause:** The forgot-password flow was already built and wired up on two other member-login screens (`MemberLoginPage.jsx` at `/user/member-login`, and `OverlayMemberLoginScreen.jsx` inside an active session's overlay nav), but was never added to `PcLockScreen.jsx` — the actual entry screen members use before a session starts, and the one they hit most often.
- **Fix:** Added the same inline "Forgot Password?" toggle/flow to `PcLockScreen.jsx`'s member login form, posting to `/api/auth/forgot-password` and reusing the existing backend endpoint/email flow.
- **Files changed:** `client/src/pages/overlay/components/PcLockScreen.jsx`

### Issue #21 — Beep sound on login/branch switch, only at Adajan (2026-08-11)
- **Problem:** Logging in as an operator, or switching branch to Adajan from Super Admin, played a notification beep every time. Other branches were silent.
- **Root cause:** `GlobalFoodOrderListener.jsx` runs on every authenticated page and, on login/branch-change, calls `checkOrders()` to "baseline" the pending food-order count. That baseline call reused the exact same "did the count increase?" comparison as real new-order detection, starting from `prevPendingCount.current = 0`. So any branch that currently had a pending food order (count > 0) looked like it had "new" orders and beeped — Adajan happened to have one sitting in `Pending` status; other branches didn't.
- **Fix:** `checkOrders()` now takes an `isBaseline` flag. The initial call after login/branch-switch passes `true` and only records the starting count without comparing or playing the sound; only subsequent SignalR-triggered checks (`NewFoodOrder`/`FoodOrderUpdated`) can trigger the beep.
- **Files changed:** `client/src/components/layout/GlobalFoodOrderListener.jsx`

### Issue #20 — Active Shift Not Detected for Multiple Operators (2026-08-06) [CRITICAL]
- **Problem:** Multiple operators in live shifts were seeing "No Active Shift" error on Cash Register page, even though shift was active (SHIFT ACTIVE shown at bottom). This is a critical bug because it blocks all cash register operations.
- **Root cause:** GetShiftIdAsync() in ControllerExtensions.cs was relying solely on JWT "shiftId" claim for regular operators. If the claim was missing or empty (due to token generation issues, claim not embedded during login, or old tokens), it threw an error instead of falling back to database lookup. SuperAdmin/Admin had a fallback (create shift if missing), but regular operators didn't.
- **Fixes implemented:**
  1. Added fallback logic for regular operators: First tries JWT claim, then queries database for operator's active shift, then throws clear error message.
  2. Removed strict requirement on JWT claim presence — shift is now reliably found from database if JWT claim fails
  3. This pattern now matches the SuperAdmin/Admin logic and ensures new operators won't face the same issue
- **Prevention for new operators:** The fallback logic automatically finds the active shift from the database by operator ID, so even if the JWT token doesn't have the claim, the system will still work
- **Files changed:** `AppleEsportsErp/src/AppleEsportsErp.Api/Extensions/ControllerExtensions.cs`
- **Testing:** All operators in a live shift should now see active cash register. No more "No Active Shift" errors.

### Issue #19 — Reservation time offset and form reset (2026-08-06)
- **Problem:** Reservation times displayed with 5-hour offset (entered 04:36, showed 09:36). Form time field didn't reset after creation.
- **Root causes:**
  1. Datetime sent without timezone offset: backend treated local time as UTC and added 5 hours
  2. Form reset logic didn't include date/time fields, so they kept the submitted values
- **Fixes implemented:**
  1. Added IST timezone offset to datetime string: `"2026-08-06T04:36:00+05:30"` instead of bare ISO
  2. Form reset now calls `getDefaultDateTime()` to reset date and time to current values
- **Commits:** 54d9ec2, 8f33c0d
- **Testing:** Create reservation at specific time (e.g., 04:36) → verify it shows as 04:36 in list and form resets to current time

### Issue #18 — Reservation time displaying incorrect time (2026-08-06)
- **Problem:** Reservation time in warning messages showed wrong time — displayed 18:09 when actual reservation was at 23:00 (4.5 hour difference, matching IST UTC+5:30 offset).
- **Root cause:** PcStatusService.cs was calling `.ToLocalTime()` on DateTimeOffset, which converted from the DateTimeOffset's timezone to the server's local timezone. Since the server was likely UTC and the time was stored as UTC, this incorrect conversion shifted the display time backward.
- **Fix:** Removed `.ToLocalTime()` calls on lines 148 and 156 in PcStatusService.cs. DateTimeOffset stores timezone info inherently; formatting it directly (without ToLocalTime()) preserves the correct time.
- **Files changed:** `AppleEsportsErp/src/AppleEsportsErp.Infrastructure/Services/PcStatusService.cs`
- **Commit:** 669a736 "fix: reservation time displaying with incorrect timezone offset (Issue #18)"

### Issue #17 — EOD & Reports Financial Data Accuracy (2026-08-03)
- **Problem:** EOD Dashboard and Reports were not correctly reflecting financial data from all sources (Cash Desk, Online Register, Wallet Desk). Missing "Wallet Top-Ups" line in Overall Collection section, causing financial totals to be incomplete and inaccurate.
- **Root causes:**
  1. "Wallet Top-Ups (Cash Collected)" line was not displayed in Overall Collection & Business section
  2. Overall End Total calculation didn't include wallet top-ups in the sum
  3. Backend EodService correctly calculated wallet top-ups but frontend wasn't displaying them
  4. No real-time update mechanism to refresh EOD dashboard as transactions occurred
- **Fixes implemented:**
  1. Added "Wallet Top-Ups (Cash Collected)" line to EOD Dashboard's Overall Collection section (EodDashboardPage.jsx line 547-549)
  2. Updated Overall End Total calculation to include: `totalCash + totalOnline + totalWalletDeductions + totalWalletTopUps` (line 175)
  3. Verified backend EodService.cs correctly sums wallet recharge transactions (line 115)
  4. Implemented real-time refresh mechanism: 3-second polling interval + SignalR live update subscriptions for Cash, Bill, Session changes (lines 99-128)
  5. Added "Live" indicator showing update status with pulse animation
  6. Added same Wallet Top-Ups line to PDF export report (line 185)
  7. Added safeguard: wallet bonus never bleeds into cash drawer totals (WalletService.cs line 140-158 — CashAmount = Amount only, bonus stays internal)
- **Testing status:** All code changes verified. Comprehensive test plan created with 10 test scenarios covering cash-only, online-only, wallet-only, mixed payments, real-time updates, PDF export, and edge cases. Ready for manual browser testing on local (localhost:5173) and server (140.245.195.222:8081).
- **Files changed:** `EodDashboardPage.jsx`, `EodService.cs`, test plan and results tracking documents

### Issue #16 — Fixed-duration plans never auto-stopping when time ran out (2026-08-03)
- **Problem:** When a member purchased a fixed-duration plan (1 hour, 2 hour, 3 hour), the session kept running past the purchased time. The member overlay showed "overdue" and the session panel said "go to billing" but the session didn't actually auto-stop.
- **Fix:** Implemented auto-stop logic in SessionService that triggers when a session's elapsed time reaches or exceeds the plan's fixed duration. The session now stops automatically and directs to billing for settlement without operator intervention.
- **Files changed:** `SessionService.cs`

### Issue #15 — Member wallet top-up not showing in Finance Center (2026-08-03)
- Where: Citylight branch, Finance Center (Wallet Desk / Online Desk) — affected every branch equally.
- Root cause: when an Admin/Super Admin (not a front-desk Operator) tops up a member's wallet, the system stamps the transaction with a synthetic "System Operator" ID, but records it against whatever real Operator's shift happens to be open on that branch — while Wallet Desk/Online Desk only showed transactions matching `OperatorId == the shift's operator` exactly. Since those never matched, the top-up silently disappeared from both panels.
- Also found while verifying the fix on production: when more than one operator is concurrently on shift at the same branch, the shift an Admin/Super Admin action attaches to was picked with no deterministic ordering — could attach to the wrong operator's shift entirely.
- Fixed: wallet transactions now record which shift they belong to directly (same pattern bills already used), Wallet Desk/Online Desk match on that shift link first, and Admin/Super Admin actions now deterministically attach to the most recently opened active shift on the branch.
- Verified live on the Citylight branch: created a test member, topped up via both a real Operator login and the Super Admin login, and confirmed both top-ups appeared correctly in Wallet Desk and Online Desk. Test member and test shifts cleaned up afterward.

### Issue #9 — Member payment approval "axios is not defined" error and login context (2026-08-03)
- **Problem 1 (axios error):** Member PC was showing "axios is not defined" when trying to approve payment requests from Billing Counter, blocking wallet payments completely.
- **Fix:** The issue was a missing import or incorrect reference in the client payment approval flow. Added proper axios/API import and verified the approval request handler is correctly calling the wallet payment API endpoint.
- **Problem 2 (Login context leakage):** Even when not logged in as a member, the payment approval screen was showing past payment requests from other sessions.
- **Fix:** Added session validation to ensure payment approval screens only display requests for the currently authenticated member on that device. Non-authenticated screens no longer show past member data.

### Issue #10 — Duplicate member registration and split top-up option (2026-08-03)
- **Problem 1 (Duplicate block):** When a member was deleted, the system still blocked re-registering with the same email/phone, preventing reactivation.
- **Fix:** Modified member creation to allow re-registering with same email/phone as long as name is different (member reactivation use case). Email + phone uniqueness check now considers only active members, not deleted ones.
- **Problem 2 (Split payment missing):** Top-up flow didn't have a split payment option (Cash + Online).
- **Fix:** Added Split payment method to member wallet top-up modal with separate Cash and Online amount fields, same as Billing Counter.

### Issue #11 — Member top-up bonus system, minimum amounts, and extra bonus permission gating (2026-08-03)
- **Problem 1 (Minimum top-up):** No enforced minimum for new member top-ups.
- **Fix:** Set minimum gaming top-up to ₹500 (configurable in Settings → Wallet Rules). Food wallet minimum is ₹10.
- **Problem 2 (Bonus calculation & display):** Top-up bonus wasn't clearly shown separately from total credited.
- **Fix:** Updated TopUpModal display to show: "₹500 top-up + 10% bonus ₹50 = ₹550 credited" so members see bonus amounts separately and understand the total.
- **Problem 3 (Bonus gameplay):** Member with ₹10 balance should be able to use only that ₹10 for gaming, then auto-end session and show top-up prompt.
- **Fix:** Session auto-stop logic now triggers when live charge reaches wallet balance. Added pre-session balance check to prevent play beyond current balance.
- **Problem 4 (Extra bonus permission):** Super Admin can give members extra bonuses (fixed amount or percentage), but this was visible to all operators.
- **Fix:** Restricted "Extra Bonus" button in Members panel to only users with 'member_extra_bonus' permission (Super Admin always has it; Admin only if Super Admin grants it via Settings).

### Issue #13 & #14 (Combined) — Shift handoff, wallet bonus in cash, cash lifecycle & revenue calculations (2026-08-03)
- **Problem 1 (Wallet bonus bleeding into cash drawer):** Wallet top-up bonus (₹100 for a ₹1000 top-up) was being added to the "Cash Sales + Wallet TopUps" line, inflating Expected Drawer Total. The bonus is member wallet credit only, not actual cash the operator has.
- **Fix:** Added explicit safeguard in WalletService.cs: CashTransaction.CashAmount = dto.Amount only (never includes bonus). Bonus stays internal to wallet_transactions table, never flows to cash_transactions.
- **Problem 2 (Expected Drawer Total wrong):** Was calculating as 400 (opening) + 1010 (wrong amount) instead of 400 + 1000. Fixed by the above.
- **Problem 3 (Shift handoff - Issue #13):** Next operator logging in after shift end wasn't seeing starting cash/stock counts from previous shift.
- **Fix for #13:** Shift closing now captures final counts and displays them to next operator on login as "Opening Balance" and "Opening Stock".
- **Problem 4 (Revenue fields all ₹0):** "Total Net Revenue", "Gaming Revenue", "Food Revenue", "Discounts Applied" all showing ₹0 in end-of-day dashboard despite active sessions.
- **Fix:** Dashboard now queries completed bills from current shift, sums their GamingAmount, FoodAmount, DiscountAmount to populate revenue fields correctly.
- **Files changed:** `WalletService.cs`, shift opening/closing logic, EOD dashboard calculations

### Issue #12 — Email links and wallet receipt print button (2026-08-03)
- **Problem 1 (Password Reset Redirect):** After a member reset their password successfully, the page redirected back to the local app (`/`), not the customer-facing website.
- **Fix:** Changed `navigate('/')` to `window.location.href = 'https://appleesports.in/'` so users are sent to the external website after password reset.
- **Problem 2 (Wallet Receipt Email):** The wallet top-up receipt email had an "OPEN PORTAL" button that tried to open the app, but members should be able to print the receipt instead.
- **Fix:** Replaced the OPEN PORTAL link button with a PRINT RECEIPT button that triggers `window.print()`, so members can print a clean copy of their receipt.
- **Files changed:** `ResetPasswordPage.jsx`, `WalletService.cs`

### Issue #1 — Maintenance mode throwing errors (2026-07-18)
- Root cause 1: role name mismatch — token stored role as lowercase (`operator`) but 3 endpoints (incl. the Maintenance endpoint) checked for capitalized `Operator`, so the server silently rejected valid operators. Fixed to use the shared role constants everywhere.
- Root cause 2: "Restore PC" set the PC to an `Offline` state that the UI had no screen for (blank dead card). Fixed to restore straight to Idle/Free, and added a Restore button to the Offline card too as a safety net.
- Confirmed working by user across branches.

### Issue #2 — Stop Session / PC card / billing counter bugs, routing/token issues (2026-07-18/19)
- Same token/role bug as Issue #1 affected several endpoints, not just Maintenance — fixed there covered part of this too.
- Pay-As-You-Go / Postpaid sessions couldn't start at all: frontend sent `durationMinutes: null` for 0-duration plans, which crashed the server's JSON parser (400 error) on every branch. Fixed to send `0`.
- Billing Counter showed a fake hardcoded "Rate Mode: STD ₹40" tile — removed.
- Billing Counter's Active Sessions list could show a stale, non-ticking elapsed time after repeated testing — added a 15s→10s auto-refresh safety net so it can't go stale.
- `sessions.GamingType` (package name) was capped at 50 characters in the database; appending the buffer-cancellation message could push realistic package names over that limit, silently failing the entire Stop transaction (safely rolled back, but the PC stayed stuck "Occupied"). Widened the column and added a permanent length guard.
- Removed the redundant "Bill" button from the operator PC card (was just a shortcut to the same Billing Counter page reachable another way) and hid "Extend" on Pay-As-You-Go sessions (nothing to extend — they already bill continuously for real time).
- Confirmed working across Adajan, Katargam, and Citylight.

### Issue #4 — Pricing not linked across session / PC card / billing counter / member overlay (2026-07-18/19)
- Built one shared calculation (`SessionPricingCalculator`) used identically everywhere: final billing on Stop, the live PC-card feed, the Billing Counter's bill panel, and the member overlay — so the same session always shows the same number on every screen.
- Found and fixed the actual root cause of "billing counter shows a different amount than the PC card": the Billing Counter's bill panel was reading the stale bill row from the database (only updated at Stop), while the PC card computed a fresh live number — now the bill panel also computes live while a session is active.
- Found and fixed a real money bug: food ordered mid-session was silently erased from the final bill when the session was stopped (a leftover `session.FoodAmount`, always 0, was overwriting the real `bill.FoodAmount`). Also fixed the same overwrite wiping out any discount already applied to a bill.
- Removed every hardcoded ₹100/hr fallback rate. A PC without a Pricing Profile now hard-blocks session start and PC creation with a clear error, instead of silently guessing a price.
- Removed the dead/duplicate "Default Base Rate", "Tax Percentage", and "Hardware Tier Pricing (Hz)" fields from Settings → System Configuration — pricing lives only in Branch-Wise Pricing Profiles now.
- Pricing Profile edits (rate or buffer) now push live to every open screen (operator dashboard, Billing Counter) instantly via a real-time signal, with a 10s polling safety net everywhere (PC dashboard, Billing Counter, member overlay) in case a signal is ever missed.
- Proven live on 3 different branches (Adajan, Katargam, Citylight) — not branch-specific.

### Issue #6 — No 10-minute free buffer, fixed sessions charged full price regardless of actual time (2026-07-18/19)
- The 10-minute free buffer is now a real, per-branch configurable field (`BufferMinutes`) on each Branch-Wise Pricing Profile, editable by Super Admin, applied live everywhere immediately (proven: changed a live profile's rate/buffer mid-test and a brand-new session immediately used the new numbers).
- ALL sessions — fixed packages and Pay-As-You-Go alike — are now billed for exact real elapsed time (after the free buffer), never a flat package price. Proven: a "1 Hour ₹60" package stopped at 11 real minutes charged ₹11, not ₹60; a 25-minute session on a "10 Min Trial" package charged ₹25 for the overrun, not capped at ₹10.
- A session that ends within the free buffer now charges ₹0 AND auto-releases the PC straight to Idle — operators no longer have to manually process a ₹0 "payment" to free the PC.
- Confirmed the buffer boundary is exact (10 min → free, 11 min → charged) via direct testing.

### Issue #3 — "Time remaining" voice alert repeating (2026-07-19)
- Root cause: the alert fired on an exact match (`remainingTime === 600` etc). My earlier 10s background refresh (added for Issue #4) periodically re-syncs `remainingTime` from the server — if that resync nudges the value back up even slightly, the local countdown crosses the same threshold a second time and re-fires the alert.
- Fixed: each of the 10-min / 5-min / 1-min alerts now tracks whether it has already fired for the current session (reset only when a new session starts) and can never fire twice, regardless of how many times the value gets resynced.

### Issue #5 — Long decimal values in money displays (2026-07-19)
- Money is still calculated and stored precisely (down to the paisa) for accounting — only the on-screen display was long/messy (e.g. ₹16.666666, or an inconsistent ₹215.1).
- Added one shared rounding helper and applied it everywhere real money gets shown: the operator PC card's live charge, the Admin PC Status page, the member/user panel's live charge and wallet balances, and the Billing Counter (active bills list, bill details panel, payment screen — gaming/food/discount/total/change).
- Every one of those now shows a clean whole-rupee figure (₹17, not ₹16.67 or ₹16.666666667).

### Issue #7 — Food order approval doesn't redirect to Food Orders page (2026-07-19)
- The "New Food Order" popup that appears on the operator panel (wherever they currently are) only dismissed itself on "Acknowledge" — it now also takes the operator straight to the Food Orders page so they can act on it immediately.
- Also fixed: for Super Admin, clicking Acknowledge could land on an empty "Select a Branch" screen instead of the order if no branch was actively selected — it now switches to the order's branch automatically.

### Issue #8 — Member session not auto-ending when wallet balance runs out (2026-07-19)
- Root cause: the code that watches a member's live charge and auto-stops the session once it reaches their wallet balance only ran while the member was looking at the overlay's Home screen. Navigate to Food/Extend/Call/Bill (or minimize the overlay) and it stopped watching entirely — the session then kept running unpaid past their balance.
- Proven live: a real member with a ₹10 wallet balance had a test session sit at ₹20 in live charges (2x over) with nothing stopping it.
- Fixed: moved this check to the part of the app that's always running for the whole overlay session, regardless of which screen is open — it now reliably auto-ends and settles the session the instant the live charge reaches the member's balance, no matter what the customer is looking at.

### Billing Counter — members restricted to Wallet-only payment (2026-07-19)
- When a bill belongs to a member, the payment method grid now shows only "Wallet" (Cash/UPI/Split/Credit are hidden) and defaults straight to it — walk-in bills are unaffected and keep all payment options.