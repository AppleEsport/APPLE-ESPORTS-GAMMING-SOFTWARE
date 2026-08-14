# Apple Esports ERP — what is fixed, what is not, and how to test it

**Version this describes:** 2.3.0
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

## 5. Starting a session from the server is now refused

**What was wrong:** a session started at Head Office was created **only** at Head Office.
The counter showed the PC as free — so it could not be stopped and **could not be billed**.
Real money, uncollectable. On the test system this produced a ₹60 session that existed
nowhere the shop could see.

It also explains the "Stop button does not work" report: start on one screen, stop on the
other, and there is nothing on that side to stop.

**Status: PROVEN.** Three such phantom sessions were found and removed.

**How to test**
- On the server, try to start a session. It must refuse, and explain that sessions belong
  at the counter.
- Start one at the counter instead. It must appear on the server.

> **Sessions are started and stopped at the counter. Always.** That is where the customer
> is, where the cash is, and the only machine that keeps working when the internet drops.

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

# PART 2 — What is still broken or missing

Please do not report these; they are known. Report anything **not** on this list.

## Still not syncing at all

| What | Consequence |
|---|---|
| **Food orders** | Food sold at a branch does not appear in server reports. |
| **Inventory / stock levels** | The server cannot see what a shop has run out of. |
| **Reservations** | Bookings made at a branch are invisible to the server. |
| **Loyalty points** | Points earned at a branch do not reach the server. |
| **Pricing profiles (downward)** | Changing a price on the server does **not** reach the shop. Prices must still be set at each branch. |
| **PC list (downward)** | Adding or renaming a PC on the server does not reach the shop. |

## Known behaviour that looks like a bug but is not

- **Stock levels do not come down from the server.** Deliberate — the shop owns its stock.
- **A pay-as-you-go session shows ∞ on the server.** Correct — it has no fixed end time.
- **The server refuses to start or stop sessions.** Deliberate, see item 5.
- **"0 of 16 gaming PCs up to date"** on the Updates page. The gaming-PC part is not built
  yet (that is Phase 3). It is not a fault.
- **The sidebar says "v2.0"** regardless of the real version. Cosmetic, not yet fixed.

## Not yet built

- **Remote control beyond stopping a session.** Head Office can ask a branch to stop a
  session (BUILT, untested). Anything else — putting a PC into maintenance, forcing a
  logout — is not built yet, though the mechanism is designed to carry it.
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
