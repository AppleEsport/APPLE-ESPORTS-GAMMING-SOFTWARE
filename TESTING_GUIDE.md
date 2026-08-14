# Apple Esports ERP — what is fixed, what is not, and how to test it

**Version this describes:** 2.4.0
**Last updated:** 14 August 2026

This is written for whoever is testing the system, not for a developer. It says what
each fix was supposed to achieve, how to check it actually did, and — just as
importantly — what is still broken so nobody wastes an afternoon reporting it again.

---

## How to read this

| Mark | Means |
|---|---|
| **PROVEN** | Verified against real data, on real hardware or the live server. Trust it. |
| **BUILT** | The code is written, deployed and compiles. **Nobody has run it yet.** Test it first. |
| **KNOWN BROKEN** | Confirmed still wrong. Do not report it; it is on the list below. |

That distinction matters more than anything else in this document. Almost every bug
found in this project was found by *running* it, never by reading the code — including
two found in the last hour that would have shipped silently.

---

## Before you start

**Two machines are involved and they are not the same thing.**

- **Head Office (the server)** — `http://140.245.195.222:8081`. The owner's view of all
  four shops. It watches; it does not trade.
- **The branch (counter PC)** — the shop's own machine, running its own database. This
  is where all trading happens, and it keeps working with the internet unplugged.

**The single most common testing mistake:** doing something on the server and expecting
it at the counter, or the reverse. Which direction each thing travels is listed below.
If a direction is not listed, it does not travel yet.

**Updates install themselves.** Every branch checks every 30 seconds, and again 10
seconds after the PC is switched on. You should never need to install an EXE by hand.

---

# PART 1 — What has been fixed

## 1. Updates now actually arrive

**What was wrong:** three separate faults, any one of which made updates look broken.

- The app looked for a new version once every **four hours**. A fix published in the
  evening was not picked up until the middle of the night.
- The Updates page asked *the branch's own database* what the newest version was — so it
  said "There is nothing to do" while a new version sat waiting on the server.
- Even after a correct install, the screen kept showing the old version until somebody
  pressed Ctrl+Shift+R. No operator would ever do that.

**Status: PROVEN.** Versions 2.2.5 through 2.3.0 all installed themselves with no USB stick.

**How to test**
1. On a branch, open **Updates**. Note the version.
2. Wait. Within about 30 seconds of a release being published, it downloads and the app
   restarts itself.
3. After the restart, the new version must show **without** a hard refresh.

---

## 2. A failed update no longer leaves the shop switched off

**What was wrong:** the installer stops the database and the API to replace them. Two
different pieces of code decided whether to stop and whether to start again, and they
disagreed during a silent update — so an update could stop the branch and never start it.
On a real machine this left both services dead for 16 minutes until they were started by
hand. Worse, if the install failed and rolled back, the restart code never ran at all.

**Status: PROVEN** — this is exactly what happened on the test laptop, and the fix has
been through several updates since.

**How to test**
- Let an update install. The screen should say *"Installing update — nothing is wrong,
  do not switch this PC off"*, not a red error.
- After it finishes, the branch comes back on its own.
- If an update ever does fail, it now writes a log to
  `C:\ProgramData\Apple Esports\logs\update-install.log`.

---

## 3. Head Office can see what each shop is doing, live

**What was wrong:** the branch sent its status every 30 seconds and **every single one
was rejected by the server with an error**. The table had never had a row in it. Nothing
on either screen said so — the shop simply looked quiet.

Cause: the branch sent its clock as Indian time (`+05:30`); the database only accepts UTC.

**Status: PROVEN.** Verified with a real operator showing active on the server.

**How to test**
1. Log in at a branch as an operator.
2. On the server, that operator should show **active** within a few seconds.
3. Log out. They go back to logged out.

Head Office now also warns if **two PCs claim to be the same branch** — which happened,
and silently mixed two shops' figures together.

---

## 4. The server and the counter stop disagreeing about PCs

**What was wrong:** two different things wrote each PC's state at Head Office — session
events (up to 30 seconds stale, arriving in batches) and the live status report (every 3
seconds, always current). They overwrote each other, so a PC could flick back to a state
it had already left. This is the *"sometimes it shows correctly, sometimes it does not"*
report.

**Fixed by one rule:** one owner per fact.
- **What is happening now** (PC busy/free, who is on shift, what the drawer holds) — owned
  by the live status report. Newest wins.
- **What happened** (a session ran, a bill was paid, ₹180 was taken) — owned by the sync
  queue. Never lost, retried forever.

Sync also went from **30 seconds to 3 seconds**.

**Status: BUILT.** The reasoning is verified but the two screens have not been watched
side by side under real trading yet.

**How to test**
1. Put the server's PC grid and the counter's side by side.
2. Start a session. Both should go busy within ~3 seconds.
3. Stop it. Both should free up.
4. Repeat several times, quickly. They must never disagree.

---

## 5. Head Office can now stop or start a session at any branch — 2.4.0

**This replaces the old behaviour.** Until 2.3.1 the server *refused* to start or stop a
session, and that refusal was correct at the time but not what anybody wanted.

**What was wrong originally:** a session started at Head Office was created **only** at
Head Office. The counter showed the PC as free — so it could not be stopped and **could
not be billed**. Real money, uncollectable. On the test system this produced a ₹60 session
that existed nowhere the shop could see. It also explains the "Stop button does not work"
report: start on one screen, stop on the other, and there is nothing on that side to stop.

**What 2.4.0 does instead:** the server *asks the shop*. Press Stop at Head Office and an
instruction travels down to that branch, the branch stops the session through exactly the
same code an operator's own click uses, it is billed into the correct shift and till, and
the branch reports back. Both screens agree afterwards because only the shop ever acted.

**Status: BUILT.** The Head Office half is deployed and proven not to break the heartbeat.
The branch half needs both PCs on 2.4.0 before it can be tested end to end.

**How to test**
- Start a session at the counter.
- On the server, press Stop on that session. The message should say it has been sent to
  the branch, not that it is already done.
- Within about 5 seconds the counter must show the session stopped and the bill raised.
- The server must then show the same thing, **and it must stay stopped.** Watch it for
  30 seconds. If it flips back to running, that is the bug below and it is worth reporting.

> **The few seconds are deliberate and honest.** The old version updated the server screen
> instantly and changed nothing at the shop. Instant and wrong is worse than five seconds
> and real.

**Also new: taking a PC out of service from Head Office.** Same mechanism. It used to
appear to work and then silently undo itself within three seconds, because the branch
reports every PC's state twenty times a minute and Head Office believed it over its own
instruction. Head Office now stops overwriting a PC while it is still waiting for the
branch's answer about it.

---

## 6. An hour of play no longer shows as unlimited

**What was wrong:** the session length reached the server correctly, but the server never
worked out *when it ends* — and the PC grid decides "pay-as-you-go" purely on "has no end
time". So every synced session drew the infinity symbol.

**Status: BUILT.** Fixed and deployed; needs one real 1-hour session to confirm.

**How to test**
- Start a **1 hour** session at the counter. The server should show a countdown, not ∞.
- Start a **Quick Start / pay-as-you-go** session. That one *should* show ∞ — it genuinely
  has no end time.

---

## 7. What the owner sets on the server now reaches the shop

**What was wrong:** everything travelled one way — upward. A super admin could tick
"End of Day" for an operator, the server saved it, and the counter never heard. **Every
permission screen on the server was decoration.** The same gap meant an operator added at
Head Office could not log in at the shop they had just been hired for.

**Status: PROVEN** for operators and permissions (tested against the live server).

**How to test**
1. On the server, change an operator's dashboard permissions.
2. Within a few seconds, the branch's menu changes for that operator.
3. Add a brand new operator at Head Office. They can log in at the branch.

> Being **suspended** travels down from the server. Being **on shift** does not — only the
> branch knows that, and the server must never sign someone out mid-shift.

---

## 8. Food menu reaches the branch

**What was wrong:** the Menu Editor stores a separate list per branch. Adding an item
"for Adajan" *while working on the server* saved it into the server's copy of Adajan's
list — the actual Adajan counter has its own database and was never told.

**Status: BUILT.** The server was verified sending the menu down; not yet seen arriving
at a counter.

**How to test**
1. On the server, add a food item for a branch. Set a price.
2. At that branch, open **Food Orders / Menu**. It should appear within a few seconds.
3. Change the price on the server. It changes at the branch.

> **Stock levels do not travel.** If the shop marks something out of stock, the server
> must not silently restock it. That is deliberate.

---

## 9. Wallet balances sync both ways

**What was wrong:** only **half** of it synced. A top-up taken at the counter reached the
server; a member **spending** their wallet did not. So the server's balance could only ever
climb and never fall — a confident, wrong, ever-inflating number. That is more dangerous
than an obviously stale one, because it is what lets the same balance be spent twice at
two different shops.

**Fixed with:** both directions now sync, and whichever side has the **newer** figure wins
(judged by when the money moved, not by which message arrived last).

**Status: BUILT.** Needs a real top-up and a real spend to confirm.

**How to test**
1. Top up a member at the counter. The server's balance matches.
2. Have that member **spend** from their wallet. The server's balance goes *down* to match.
3. Members now also reach every branch — someone who joined at one shop can be found and
   served at another.

---

## 10. End of Day, Credits and shift figures

**What was wrong:** shifts, cash registers, cash transactions, bills and customer credits
**were never sent to the server at all**. Head Office was being asked to arrive at the same
totals from less than half the data. No amount of fixing the screens could have made the
numbers agree.

Worse: an unpaid bill only reached the server as a side effect of somebody *paying* it —
so bills that were left unpaid, which are exactly the ones that create a customer credit,
structurally could never arrive. **Unpaid money was the half of the ledger that could not
be reported.**

**Status: BUILT, and the sync path itself is PROVEN.** A real shift, till, bill and credit
were pushed through the live server and every figure landed exactly (₹2,000 opening,
₹1,450.50 taken, ₹3,450.50 expected, a ₹200 credit). What has **not** been tested is a real
branch generating them through normal trading.

Two bugs were caught during that test and fixed before shipping:
- Customer credits could never be filed, because their bill never arrived.
- **Every till would have synced with zero money in it** — the code skipped any column with
  a database default, which is exactly the three money columns End of Day is built from.

**How to test**
1. At a branch: open a shift, open the cash register with a float, take some cash payments.
2. On the server, the shift and the till should appear with the **same figures**.
3. Stop a session and defer payment (customer leaves owing money). The credit appears on
   the server with the right amount owed.
4. Close the shift, count the drawer. End of Day figures should match on both sides.

---

## 11. Other fixes

- **Takings were being permanently lost** after 2.5 minutes offline — the retry budget was
  burned by connection failures. **PROVEN fixed.**
- **A new branch install invented all four shops** with made-up IDs and the owner's personal
  email. **PROVEN fixed.**
- **Emails queued by a branch were silently thrown away** — which is why no welcome or
  password reset ever arrived. **PROVEN fixed.**
- **The End of Day panel read two separate drawers as one.** **PROVEN fixed.**

---

## 12. Four background jobs were running against the wrong shop — 2.4.0

**This is the most serious thing found in this round, and nobody had reported it**, because
it produced wrong numbers rather than an error message.

Head Office holds a synced copy of every branch's sessions, shifts and tills. To the
software those copies are indistinguishable from its own. Four jobs that are supposed to
watch live trading were therefore, at Head Office, pointed at four other people's shops:

- The **fixed-duration monitor** stops any session whose paid time has run out. At Head
  Office that meant every branch's sessions — stopped and billed a second time, in a
  database no operator can see and no customer is standing in.
- The **trading day closer** closes shifts and cash registers and emails the owner about
  any difference. At Head Office it would close its own copies of all four branches' tills
  and email about a shortfall that never happened.
- The **wallet monitor** deducts a member's balance as they play. At Head Office it would
  debit the same member a second time for the same hour.
- **Downtime recovery** credits back time lost to a power cut. Head Office restarting is
  not the branches' power cut, so it would hand time back to customers at four shops who
  had played through it perfectly happily.

They were only failing loudly instead of succeeding wrongly because the server refused to
stop a session at all — the exact refusal that item 5 lifts. Fixing item 5 without this
would have turned a noisy failure into a silent, expensive success.

**Status: PROVEN.** All four confirmed standing down on the live Head Office server
immediately after deploy.

**How to test:** nothing to do at a branch. On the server log you should see three lines
saying each service "does not run at Head Office". If End of Day figures have been drifting
without explanation, this was a likely cause.

---

## 13. "Rate limit exceeded" while simply starting a session — 2.4.0

**What was wrong:** the limit is per IP address, which is right, and it was being given the
wrong address. Head Office sits behind a front-end server, so every request appeared to
arrive from that one machine. Four branches, every dashboard, every PC agent and 80
heartbeats a minute all shared **one single allowance**. Whoever happened to press a button
when the shared allowance ran dry got the error — which is exactly why it looked random and
why it landed on ordinary work like starting a session.

**Status: BUILT.** Each signed-in account now has its own allowance, the real caller address
is used, and branch-to-Head-Office reporting is not counted against anybody.

**How to test:** work normally and quickly at the counter for a few minutes — start, stop,
bill, repeat. The error must not appear. If it does, note the exact action and the time.

---

## 14. Payments accepted negative amounts — 2.4.0

**What was wrong:** the only check was that the parts of a payment add up to the bill total.
₹500 cash and **minus** ₹400 online adds up to ₹100 and settled a ₹100 bill perfectly
happily. The drawer then expected ₹500 that was never taken, so End of Day over-counted by
₹400, and the session reported a **negative total** in the reports.

There is no validation on payments anywhere in the system — only login and sessions have
any — so the check now lives in the billing service itself, where every route to taking
money has to pass through it.

**Status: BUILT.** Needs one deliberate attempt to confirm it is refused.

**How to test:** take a normal split payment (part cash, part online). It must work exactly
as before. Negative amounts are not reachable from the screens, so this is mainly a guard
against a future screen or a bad request — but if any payment is ever refused with "cannot
contain a negative amount", that is this working.

---

## 15. Password reset and welcome emails linked to nothing — 2.4.0

**What was wrong:** every link pointed at `http://localhost:5173`. On the server that is a
valid address; in somebody's inbox it means "this computer", so it opened nothing on the
phone or laptop that received it. The mail sent, the log looked clean, and the recipient
simply could not use it.

**Status: BUILT.** Links are now built from the address the person actually reached the
server on, and the localhost default that ships in the config file is no longer treated as
a real answer.

**How to test:** trigger a password reset for an address you can read. Open the link **on a
phone**, not on the server. It must load the reset page.

---

## 16. Smaller things — 2.4.0

- **The food menu only travelled downward.** An item added or repriced at a counter never
  reached Head Office, so every sales report was priced against a menu that branch had
  stopped using. Now travels both ways. **BUILT** — add an item at the counter, it should
  appear on the server within a few seconds.
- **Any operator could delete a member.** That takes a wallet balance, a credit history and
  a share of every End of Day that member appears in with it — and there was no branch check
  either, so one shop could remove another shop's member. **Super Admin only now.**
  Suspending is still available to operators and is reversible.
- **The member list never refreshed.** Loaded once and then never again, so a member
  registered at another branch was simply absent until somebody pressed F5 — which reads
  exactly like sync being broken, when the data had arrived perfectly well. Now refreshes
  itself every 15 seconds while the tab is open.
- **Every member edit printed the member's name and login details into the branch log** in
  plain text, from a leftover debug line. Removed.

---

# PART 2 — What is still broken or missing

Please do not report these; they are known. Report anything **not** on this list.

## Still not syncing at all

| What | Consequence |
|---|---|
| **Food orders** | Food sold at a branch does not appear in server reports. |
| **Reservations** | Bookings made at a branch are invisible to the server. |
| **Loyalty points** | Points earned at a branch do not reach the server. |
| **Pricing profiles (downward)** | Changing a price on the server does **not** reach the shop. Prices must still be set at each branch. |
| **PC list (downward)** | Adding or renaming a PC on the server does not reach the shop. |

> **Inventory now syncs upward** as of 2.4.0 — see item 16. Stock levels still never travel
> *down*, deliberately: the shop owns its own stock.

## Known behaviour that looks like a bug but is not

- **Stock levels do not come down from the server.** Deliberate — the shop owns its stock.
- **A pay-as-you-go session shows ∞ on the server.** Correct — it has no fixed end time.
- **Stopping a session from the server takes a few seconds.** Correct as of 2.4.0 — see
  item 5. The instruction has to reach the shop, and the shop is what actually stops it.
- **"0 of 16 gaming PCs up to date"** on the Updates page. The gaming-PC part is not built
  yet (that is Phase 3). It is not a fault.
- **The sidebar says "v2.0"** regardless of the real version. Cosmetic, not yet fixed.

## Not yet built

- **Remote control beyond stop / start / PC maintenance.** Those three are BUILT in 2.4.0.
  Anything else — forcing an operator logout, closing a shift remotely — is not built yet,
  though the mechanism is designed to carry it.
- **Truly instant sync.** Currently the shop and server check in every 3 seconds. A
  permanently open connection (instant) is designed but not built.
- **Phase 3 — the gaming PCs themselves.** Screen locking, per-PC sessions on the customer
  machines. Not started.

## Must be done before real operators use this

- **Real email addresses.** The system still contains a made-up domain
  (`@appleesports.com`) and the owner's personal Gmail as seeded data. Emails to those
  addresses bounce.
- **A leftover test cash register** exists on Adajan from sync verification (14 Aug). It is
  closed and holds one real ₹100 top-up. Clear it when test data is next wiped.

---

# PART 3 — Testing checklist

Run these in order. Each assumes the one before it passed.

### A. Updates
- [ ] Branch picks up a new version within ~30 seconds
- [ ] New version shows **without** Ctrl+Shift+R
- [ ] "Installing update" message appears, not a red error
- [ ] Branch comes back by itself afterwards

### B. Live status
- [ ] Operator logs in at branch → shows **active** on server within seconds
- [ ] Operator logs out → shows logged out
- [ ] Branch switched off → server shows it as not reporting within ~1 minute

### C. Sessions
- [ ] Start at counter → appears on server within ~3 seconds
- [ ] Stop at counter → frees on both within ~3 seconds
- [ ] **1 hour** session shows a countdown on the server, not ∞
- [ ] Pay-as-you-go shows ∞ (correct)
- [ ] Server **refuses** to start a session
- [ ] Start/stop 5 times quickly — screens never disagree

### D. Money
- [ ] Cash payment at counter → appears in server reports
- [ ] Wallet top-up → server balance matches
- [ ] Wallet **spend** → server balance goes down to match
- [ ] Defer a payment → credit appears on server with correct amount
- [ ] Cash register opening float and expected cash match on both sides
- [ ] End of Day totals match on both sides

### E. Settings travelling down
- [ ] Change an operator's permissions on server → branch menu changes
- [ ] Add a new operator on server → they can log in at branch
- [ ] Add a food item on server → appears at branch
- [ ] Change a food price on server → changes at branch

### F. Offline
- [ ] Unplug the branch's internet. Trading continues normally.
- [ ] Take payments while offline.
- [ ] Reconnect. Everything taken offline appears on the server.
- [ ] Power cut mid-session → session is held for a decision, not lost.

---

## When something fails

Include all of this, or it cannot be diagnosed:

1. **Which machine** — the server, or which branch's counter PC.
2. **What you did**, step by step.
3. **What you expected**, and what happened instead.
4. **Both screens** — a screenshot of the server and the counter at the same moment.
5. **The branch log**, if the branch is involved:
   ```powershell
   Get-ChildItem "C:\ProgramData\Apple Esports\logs" |
     Sort-Object LastWriteTime -Desc | Select-Object -First 1 |
     Get-Content -Tail 120
   ```

The log is what matters most. The heartbeat failure in section 3 was invisible on both
screens for hours and was only ever going to be found in a log.
