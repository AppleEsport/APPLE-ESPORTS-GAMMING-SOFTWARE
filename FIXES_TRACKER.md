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

### Issue #24 — Nothing starts itself after a boot or power cut (gaming PCs and counter PC)
- Where: Every branch. Gaming PCs (user role) and the counter PC (operator role).
- What I did: Switched a PC on / recovered from a power cut.
- What happened: Nothing starts. On a gaming PC that means the Windows desktop with no lock screen, no member login, no session gate and no billing — the customer can just use the machine. On the counter PC the operator has to open the app by hand every morning.
- What should happen instead: Work like PanCafe Pro. Gaming PCs start the app automatically after every boot or power cut, and relaunch it automatically if it crashes, so a customer cannot get past the lock screen. An Operator, Admin or Super Admin who deliberately picks "Exit Kiosk Mode / Switch to Windows" closes it normally and it stays closed, so the PC can be used without our app. A "Return to Kiosk Mode" option puts the protection back, and any reboot returns to kiosk mode by itself.
- Priority: Urgent
- Notes from investigation: The shipped installer (`installer/AppleEsportsBranch.iss`) creates only a Start Menu and Desktop shortcut — no startup folder entry, no registry Run entry. `AppleEsportsAgent.exe` is not a Windows service either; only `AppleEsportsApi` is, which is why the API is the one thing that does come back. The auto-start checkbox people remember is in `installer/AppleEsports.iss`, the old installer we no longer ship. After an *update* the app does restart itself, which is why this only shows up on a normal boot or power cut. The deliberate-exit flag has to be cleared on boot, otherwise "Exit Kiosk Mode" would survive a restart and leave the PC unprotected for good.

### Issue #25 — Operator cannot shut down a gaming PC, or all of them
- Where: Counter PC (operator dashboard), and Admin via Quick-Switch at the branch.
- What I did: Looked for a way to shut a gaming PC down at closing time.
- What happened: There is no button anywhere, so it cannot be done at all.
- What should happen instead: The Operator can shut down one gaming PC, or all of them at once. An Admin who has come to the branch and used Quick-Switch can do the same. Head Office cannot shut PCs down — it does not need to.
- Priority: Urgent (for the security hole below; the feature itself is Normal)
- Notes from investigation: The shutdown itself already works end to end — `Hubs.cs:183 SendShutdownCommand` tells the PC, the agent locks the screen and runs `shutdown /s /t 10`. Two things are missing and one is a hole. `SendShutdownCommand` has NO role restriction; it inherits only the class-level `[Authorize]` on `BranchAwareHub`, so any authenticated account can call it — including a gaming PC's own `user_panel` account, which means a customer seat's token could shut machines down today. Other sensitive PC actions in `PcManagementController` are properly restricted and this one was missed. There is also no bulk "all PCs" version, and no UI calls it at all. Branch scoping already exists to build on: the hub reads a `branchId` claim and Operators/Admins join `branch:{branchId}`.

### Issue #26 — A member cannot pay a wallet shortfall in cash or UPI
- Where: Billing Counter / food ordering, any branch.
- What I did: Member has ₹20 in their food wallet and orders a ₹120 Red Bull.
- What happened: The order is refused outright with "Insufficient Food wallet balance". The sale simply cannot be made.
- What should happen instead: Take the ₹20 that is there, let the sale go through, and show the remaining ₹100 as still due so the counter can collect it in cash, UPI, or a split of both.
- Priority: Normal
- Notes from investigation: `WalletService.DeductWalletAsync` (WalletService.cs:228) hard-blocks any deduction larger than the balance, so nothing ever goes negative — it just refuses. `BillingService.ProcessPaymentAsync` already supports splitting a bill across cash / online / wallet / credit, but the parts must add up to the total exactly and the wallet part still cannot exceed the real balance, so the shortfall has to be worked out and typed by hand and the payment fails if it is entered wrong. There is no shortfall or settle-up flow anywhere. This is a new feature rather than a regression, and it is money code, so it gets proved on a real machine rather than by compiling.

### Issue #27 — Activity Log will not stay scrolled up
- Where: Sessions page, the Activity Log strip along the bottom.
- What I did: Scrolled up in the Activity Log to read an earlier line.
- What happened: As soon as the next activity arrives it jumps back to the bottom, so older entries cannot be read. It feels like scrolling is broken.
- What should happen instead: If I have scrolled up to read something, leave me there. Only follow along automatically when I am already at the bottom.
- Priority: Normal
- Notes from investigation: `SessionActivityLog.jsx:58` sets `scrollTop = scrollHeight` in an effect that runs on every new entry, unconditionally, so it fights the user's own scrolling. Fix is the usual stick-to-bottom pattern: only auto-scroll when the view was already at (or near) the bottom before the new entry arrived. Follows smoothly rather than snapping, so it is possible to see what just arrived; instant on first open, because animating down through a hundred loaded entries every time the page opens is not worth watching, and instant for anyone whose system asks for reduced motion. A gesture during one of those animations cancels it, so scrolling up mid-slide is respected rather than fought.

### Issue #28 — Logout does not work on the member's own screen
- Where: Gaming PC, member session overlay (the member's own screen during a session).
- What I did: Tapped Logout on my own session screen.
- What happened: Nothing completes. The button changes to "Paying…" and stays there, disabled, so it cannot even be tried again. The member cannot log out of their own session.
- What should happen instead: Tapping Logout ends the session immediately and returns to the login screen, every time.
- Priority: Urgent
- Notes from investigation: The confusing part is that the session really did end. `memberCheckout` posts to `/public/sessions/{id}/member-checkout` and the server bills and closes the session properly. The client then did nothing further, on the assumption that the PC flipping to Idle over SignalR would unmount the screen. When that message did not arrive — socket dropped, wifi blip, PC state update lost on the way — nothing else ever finished the job, and `setCheckoutLoading(false)` was never called on the success path, so the button was stuck for good. Also, the member's token was left in localStorage: on a shared machine that hands their wallet to whoever sits down next. `handleWalletEmptyLogout` already cleared storage and navigated correctly; the normal path, used far more often, did not.

### Issue #29 — The floating widget sits on top of games
- Where: Gaming PC (user role), during a session.
- What I did: Started a game full screen while a session was running.
- What happened: The small overlay widget with the minimise button stays above the game, in the corner, over the top of whatever is being played.
- What should happen instead: It should sit behind, in the normal window order, out of the way at the bottom. A game covers it like any other window.
- Priority: Normal
- Notes from investigation: The widget inherited `TopMost` from the full-screen gate. That pin is right for the gate — the walk-in/member screen and the locked "session ended" screen must beat anything on the desktop, or a game drawn over the top of them is a customer playing with no session — but it was being kept once the overlay shrank to the widget. Now only the full-screen modes are pinned; bubble and panel are ordinary windows. It already defaults to the bottom-right corner.

### Issue #30 — The operator cannot see a gaming PC's update happening
- Where: Counter PC, Updates page.
- What I did: Looked for what the gaming PCs attached to this counter are doing during an update.
- What happened: Nothing about them is shown. The counter reports its own version and progress, and the gaming PCs report nothing at all.
- What should happen instead: If a gaming PC is connected to this counter, the operator should see that its update is happening and how far along it is, the same way the counter's own is now shown.
- Priority: Normal
- Notes from investigation: `BranchVersionStatuses` already has `GamingPcsUpToDateCount` and `GamingPcsTotalCount`, and the total is filled in honestly — but `BranchVersionReporterService` passes `upToDateCount = 0` hardcoded, so "0 of 35" is what it always says regardless of the truth. Nothing anywhere records a gaming PC's own version. The 3.1.0 stage/progress reporting added in this session is per-branch, sent by whichever app is running; a gaming PC reports under the same branch id as the counter, so the two would overwrite each other rather than appearing separately. Needs per-PC rows, not just per-branch, and each gaming PC reporting its own version and stage.

### Issue #31 — Updates leave no trace in the Audit Trail
- Where: Audit Trail, whole system.
- What I did: Looked for a record of updates being approved, sent, started, finished or failed.
- What happened: None of it is in the Audit Trail. Approving a version, a branch downloading and installing one, an install failing, and a PC being pushed a specific version are all invisible there.
- What should happen instead: Every step of an update should appear in the Audit Trail, like the rest of the system — who approved it, which branch and which PC took it, when it started, whether it finished, and why it failed if it did.
- Priority: Normal
- Notes from investigation: The 3.1.0 work writes update stage and progress to `BranchVersionStatuses`, which is live status and is overwritten on every report — nothing is kept. `RemoteBranchControl.SendAsync` does audit `remote_command_issued`, which is why "send this branch a specific version" shows up, but the ordinary automatic update path writes no audit entry at any stage. Worth doing together with #30, since both need the update path to record per-PC facts rather than one row per branch that everything overwrites.

### Issue #32 — "Install updates by themselves" cannot actually make an update unattended
- Where: Any branch. Updates page, the "Install updates by themselves" tick.
- What I did: Left the tick on and published a new version, expecting the branch to update on its own.
- What happened: On some machines it does; on others nothing happens and there is no error. Windows is quietly waiting for somebody to approve the installer, and nobody is standing at a counter PC at 3am to do it.
- What should happen instead: With that tick on, an update installs with nothing to click, on every machine.
- Priority: Urgent
- Notes from investigation: The tick and the prompt are two different gates and ours cannot open the other one. `AutoUpdateEnabled` is our setting and means only "do not wait to be told to install". The prompt is Windows' UAC, asking whether a program may change the computer, and no application setting can switch that off — that is the point of it. `UpdateService.Install` launches the installer with `Verb = "runas"`, which asks for elevation, so the app gets a prompt unless it already happens to be elevated or UAC is turned off on that machine. That is exactly why this looks intermittent: the branches where updates have been arriving are the ones where the app runs elevated or UAC is off, and the rest sit there looking stuck. `Process.Start` only reports a failure if the prompt is actively refused, so an unanswered prompt is indistinguishable from nothing happening.
  The fix already exists in the codebase, on a different path. `install_version` from Head Office is reliable precisely because the branch's Windows **service** runs the installer: a service is already LocalSystem and has no desktop to show a prompt on, so there is nothing to click and nothing to refuse — `BranchHeartbeatService.RunInstallVersionAsync` even notes it deliberately omits `runas` for that reason. The automatic update path should hand the verified installer to that same service instead of launching it from the desktop app. Doing so also removes the "Windows refused the installer" failure state added in 3.0.9, because it would no longer be reachable.

### Issue #33 — A PC cannot be removed from a branch from Head Office
- Where: Head Office, and any branch a wrong PC has attached itself to.
- What I did: A tester PC (LAPTOP-C215S9B3) was reporting itself as the Citylight branch alongside the real counter PC (APPLE-11).
- What happened: Head Office can only warn about it. The banner names both machines and explains the damage, and then there is nothing on any screen that can do anything about it. The only way to stop it is to physically get to that PC and disable a Windows service — and if only `sc.exe stop` is run without `sc.exe config start= disabled`, it comes back on the next reboot, which is exactly what happened here.
- What should happen instead: Super Admin can remove a PC from a branch at Head Office, and it stops affecting that branch immediately, with no need to touch the machine.
- Priority: Urgent
- Notes from investigation: This is the root cause behind most of a day's confusion, not a cosmetic gap. While two machines report as one branch they each keep their own database and sync under the same branch id, so `BranchVersionStatuses` and `branch_heartbeats` hold one row that both overwrite several times a minute — the version read 3.0.9 then 2.4.9 then 3.0.9 depending on who spoke last, which made the Updates page untrustworthy and made a working update look stuck. Their takings merge and cannot be separated afterwards, because sessions and bills record no machine.
  Design, and the order matters. **Reject at the door first**: keep a list of machines Head Office accepts per branch, and have the heartbeat, version-report and sync-inbox endpoints answer a revoked machine with a refusal instead of writing its data. That half is enforced entirely at Head Office, so it works against *any* branch build, including 2.4.x — which is the point, since a rogue machine is exactly the one likely to be running something old. It stops the merging and the overwriting the moment it is pressed.
  **Then tell the machine to stand down**: a `stand_down` branch command riding the heartbeat response, so a modern branch stops its own sync and says so on screen rather than running on quietly rejected. This half only works for builds that know the command — an older one answers "this branch does not know the command yet" and keeps running locally — so it is the courtesy, not the enforcement. Building it the other way round would be a feature that fails against precisely the machines it is needed for.
  Should record who revoked what and when, per #31.

### Issue #34 — The Dashboard never warns about low stock
- Where: Head Office and branch Dashboard, the low-stock figure.
- What I did: Looked at the Dashboard while stock was running down.
- What happened: It always reads 0. It has never once warned about anything.
- What should happen instead: It should count the items actually at or below their reorder level, so running out is something the screen tells you before a customer does.
- Priority: Urgent
- Notes from investigation: `DashboardService.cs:83` reads `LowStockAlerts = 0, // Mocked for now`. Every other number in that same DTO — revenue, cash, online, wallet, bills, active PCs, operators — is computed from real data; this one field is a literal zero. It has to be counted from the inventory table against each item's reorder level.
  Same shape as `upToDateCount = 0` behind "0 of 35 gaming PCs up to date" (#30), and worth treating as one class of bug rather than two coincidences: a placeholder put in so a screen would render, which then survives because a hardcoded 0 looks exactly like a real answer. Nothing throws, nothing is blank, so nobody questions it. Anything that cannot be answered yet should say so on screen rather than quietly return zero — that is the difference between "no alerts" and "not being counted", and only one of them is safe to trust.

### Issue #35 — Shutdown does nothing, because it is sent to a program that is not running
- Where: Counter PC, Sessions page. Both "Shut Down PC" and "Shut Down All PCs".
- What I did: Started a session on a gaming PC, stopped it, then tried per-PC shutdown and shut-down-all.
- What happened: Nothing at all, and no error either. The gaming PC stayed on.
- What should happen instead: The PC shuts down.
- Priority: Urgent
- Notes from investigation: The command is delivered to the SignalR group `agent:{pcId}`, and only `AppleEsportsAgent.exe` ever joins that group — by calling `AgentConnected` in `DualConnectionService.cs:123`. At Citylight, all 35 PCs report 0 agents online, 0 that have ever sent an agent heartbeat, and 0 provisioned, so nothing has ever joined those groups. `Clients.Group(...).SendAsync(...)` does not fail when a group is empty, so the message is dropped in silence and the operator gets a success toast. The shutdown work in #25 is correct as far as it goes — role check, branch check, skipping busy PCs — it just hands off to a listener that is not there.
  Two ways out, and the second is better. The agent could be made to run and stay running on gaming PCs (kiosk-guard.ps1 already starts `AppleEsportsAgent.exe` when the file is present, so 3.1.1 may partly do this by accident). But the more solid route is to stop depending on that separate program: `AppleEsports.exe` is definitely running on a gaming PC — it is drawing the lock screen — and it already has a native bridge for `session-ended`. Delivering shutdown to the overlay page and letting the native shell run the shutdown removes a whole component from the path, and with it the failure mode where the visible app is fine and the invisible one is missing.
  Whichever is chosen, the hub should stop reporting success for a command nobody received. Sending to an empty group is knowable at the moment of sending, and saying "no agent is connected to that PC" is the difference between a bug that took one test to find and one that hid behind a green toast.

### Issue #36 — A gaming PC takes too long to be usable after switching on
- Where: Gaming PCs, from power on to the Choose User Type screen.
- What I did: Switched a gaming PC on.
- What happened: Startup works correctly now, but it takes longer than it should before the screen is ready for a customer.
- What should happen instead: Ready as quickly as possible after Windows arrives at the desktop.
- Priority: Normal
- Notes from investigation: Raised after #24 was confirmed working on a real machine. Not yet measured, and that is the first job — where the time actually goes decides the fix. Candidates worth timing separately: the app's own launch and WebView2 startup; the wait before it can reach the branch API, which on a gaming PC means the counter PC's API and database being up first; and the dashboard bundle being fetched over the LAN. The gaming PC also currently has to wait for the counter PC to be ready, which on a shop-wide power cut means every gaming PC is waiting on one machine. Measure before changing anything.

### Issue #37 — The gaming PC agent connects to nothing, and never says so
- Where: Every gaming PC. Root cause behind #35, the missing red state, and #38.
- What happened: `AppleEsportsAgent.exe` is running (Windows named it as preventing shutdown), yet Head Office reports 0 agents online, 0 that ever beat, and 0 provisioned across all 35 Citylight PCs.
- What should happen instead: the agent registers with its branch, stays registered, and complains loudly if it cannot.
- Priority: Urgent
- Notes: `DualConnectionService.cs:81` builds the hub URL with `?access_token={_config.MachineToken}`, and the hub inherits `[Authorize]` from `BranchAwareHub`. `ProvisionedAt` is null on every one of those PCs, so there is no machine token, so the connection is rejected as unauthorised - and `TryConnect`'s `catch { return false; }` at line 127 throws the reason away. Nothing is logged, nothing is shown, and the agent sits there apparently fine.
  This one fault produces three separate reported bugs. Shutdown is sent to the SignalR group `agent:{pcId}` which nothing ever joined, so it vanishes silently (#35). A shut-down PC never turns red, because the agent's heartbeat is the only thing that would report power state - the legend now has a Shut Down colour with nothing able to set it. And PCs cannot appear by themselves (#38), because that needs the same registration.
  Fixing the swallowed catch is not optional dressing on this: an agent that cannot reach its branch must be visible as broken, or the next person spends a day proving it is running before discovering it was never connected.

### Issue #38 — A gaming PC should claim its own seat when the EXE is installed
- Where: Setting up a new branch, or adding a PC to an existing one.
- What happens now: all 35 PCs are created by hand up front, and each machine is then pointed at one of them. Nothing verifies the machine ever attached itself, so a PC can look configured while its agent is connected to nothing (#37).
- What should happen instead: set up the operator counter PC for the branch first, then install the EXE on each gaming PC one at a time. Each one claims a seat over the LAN, is issued its machine token, registers, and appears on the Sessions dashboard immediately - no pre-created list.
- Priority: Urgent
- Notes: The owner's own proposal, and it is the right shape rather than a convenience. Provisioning is what issues the machine token the agent needs to register at all, so making installation do it removes the state where a PC exists in the database, looks set up, and has nothing behind it. `BranchProvisioningController` and `/api/agent/provision` already exist and `HeadOfficeClient.ProvisionAsync` already calls something like this - what is missing is the gaming PC doing it against its own branch on the LAN as part of setup, and the counter's Sessions grid being built from PCs that have actually reported rather than from rows somebody typed.

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