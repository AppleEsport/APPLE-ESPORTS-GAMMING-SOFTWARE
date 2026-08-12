# Cash handover between shifts

Agreed with the owner, 11 August 2026. Not yet built — this is the specification.

---

## The bug that prompted it

An operator logged in, set the opening float to ₹100, reloaded the page, logged in again, and
was asked for the opening float **again**. They typed ₹0.

Two cash registers now existed for one physical drawer on the same day:

| | |
|---|---|
| End of day screen read | ₹100 — the first register |
| Lock Register screen read | ₹0 — the second register |

Nothing compared the two. Nothing questioned ₹0. The second figure was simply believed.

This is the same fault already fixed for **shifts** — login resumes an open shift rather than
stacking another — which was never applied to the **register**. So the shift is reused and the
drawer is not.

## The rule it breaks

**A branch has one drawer, and it is continuous through the trading day.** Only the first
shift of the day puts money in it. After that, every shift **inherits** the drawer and
**verifies** it. Nobody is ever asked "how much is in the drawer?" again.

---

## The flow

```
A opens the day    puts Rs 100 in, sets the float   <- the ONLY time anyone is asked
A trades           billing, top-ups, food
A hands over       counts drawer + stock, closes their shift
B starts           counts BLIND, then sees A's figure and any difference
B trades
B hands back       counts out
A verifies in      counts blind again
A ends             counts out
B takes last shift counts out, ticks "last shift of the day", day closes
```

Each shift's opening figure is **a count**, not a typed guess, and it is checked against what
the previous shift said they left.

---

## Decisions

**Blind count first.** The incoming operator enters what they actually find. Only then does
the system show what the outgoing operator said they left, and the difference. Neither can
simply agree with the other's number — which is the tick-box problem, and it is how a
shortfall silently becomes the wrong person's fault.

**A shortfall does not stop the shop.** The incoming operator types a reason, and the owner is
emailed with both figures, the amount missing, and that reason. The difference is recorded
against the **outgoing** shift, and the incoming operator starts from the money actually in
the drawer. They are never made responsible for cash missing before they arrived.

**If the outgoing operator never closed their shift** — walked off, PC died — the incoming
operator closes it for them: they count everything, that count becomes the closing figure for
the abandoned shift, it is marked as not counted by its own operator, the owner is emailed,
and then the incoming shift starts.

---

## What has to change

**Opening a register must be idempotent per branch per trading day.** If one is already open,
it is reused, never duplicated. This alone stops the reported bug.

**The opening prompt becomes two different questions.**

| Situation | Asked |
|---|---|
| First shift of the trading day | "How much are you putting in the drawer?" — a float |
| Every later shift | "Count the drawer" — blind, then shown the difference |

**Stock is handed over the same way** as cash: counted at close, counted blind at open,
difference shown. The stock list is already on the close screen.

**A handover record** is needed, holding: outgoing shift, incoming shift, what was said,
what was found, the difference, the reason, and when. Without it a discrepancy has nowhere to
live and cannot be reported against the right shift.

**Emails** — the owner is told on any handover difference, and on any shift closed by
somebody other than its own operator.

---

---

# A shift nobody closed

Agreed with the owner, 12 August 2026. **Not yet built — this is the next piece of work.**

Two faults, one cause: the system depends on an operator doing something, and sometimes nobody
does. Building them together because the answer is the same.

## 1. The day only closes if somebody ticks a box

"This is the last shift of the day" sets a flag on the shift and sends the end-of-day email.
Nothing else depends on it, and nothing notices when it does not happen.

**Ticked by mistake:** the owner gets an early email with partial figures. The drawer is
untouched, the next operator logs in normally, and the real last shift sends a correct email
afterwards. One confusing email, recoverable.

**Forgotten:** no end-of-day email at all; the register is never closed and stays open past 06:00;
the next morning is a new trading day, so a fresh register opens and asks for a float while
yesterday's stays open indefinitely. This is where the **30 stale registers** already cleared off
the live system came from.

Forgetting is much the more likely of the two, because it requires doing nothing.

**Decided: stop depending on the tick.** Shortly after 06:00, any branch whose previous trading
day is still open gets closed by the system — the register closed with its figures preserved, the
end-of-day email sent, and the email saying plainly that nobody marked the last shift. The tick
stays as a convenience for closing early; it stops being the only thing holding the day together.

A tired operator at 3am should not be able to cost the owner a day's report.

## 2. An operator who never closed their shift

Power cut, or they simply walked out. Login only ever looks for an active shift belonging to *the
same operator*, so another operator's abandoned shift is invisible: the next person logs in, a
second shift opens alongside the first, and the abandoned one dangles.

**Decided, in the owner's words:** *"B will close A's shift and count all the things and then B
will log in."*

So when an operator logs in and finds another operator's shift still active at that branch, they
are asked to close it first: they count the drawer and the stock, that count becomes the closing
figure for the abandoned shift, it is recorded as closed by somebody other than its own operator,
the owner is emailed, and only then does the new shift start.

## Care needed

Both close someone else's shift and someone else's drawer, and write a cash count. That is money
being recorded against a person who is not present to confirm it, so who closed it and who
counted it must be stored separately from whose shift it was — otherwise a shortfall lands on the
wrong operator's name.

---

## Deliberately not part of this

**One shift per counter PC.** Katargam and Varachha have three counter machines each, so the
rule has to be per machine, and the server has no reliable machine identity — a browser is
not a PC. It belongs in Phase 2 with the EXE, where each counter has a real identity. Building
it now on a guessable signal would look enforced and not be.

---

# When a member's wallet runs out mid-game

Agreed 11 August 2026. **Built and deployed 12 August 2026 as `c1ac575`.**

What was built: the stop point is calculated up front from the balance and the rate, so the
session ends when the money runs out rather than after; the member is warned five minutes ahead
and told before the PC locks rather than after; and the overlay and the server now share one
stopping rule instead of holding two that disagree.

The rounding was the part that mattered and it is recorded below, because it is not obvious and
it will catch the next person too.

## What already works

`OpenSessionMonitorService` checks every open session once a minute. When the amount owed
passes what is in the wallet, it force-stops the session, and stopping it deducts the wallet
and settles the bill.

So a member with ₹10 at Adajan (₹60/hour) does get stopped after about nine minutes, and does
get charged correctly.

## What is missing

**The member is told nothing.** Nothing is pushed to the gaming PC — the session simply stops.
From the seat it looks as though the machine has failed.

**There is no warning while they can still act.** It only reacts once the money has gone.

**The check asks the wrong question.** It runs once a minute and asks "has he gone over?", so
it only notices *after* he has. At Rs 1 a minute, Rs 10 buys ten minutes - but the check that
catches it fires at minute eleven, by which point Rs 11 is owed against Rs 10. The member ends
up owing Rs 1 that was never there.

That shortfall is created entirely by waiting to catch him instead of stopping him on time.

## To build

**A warning a few minutes before the money runs out**, on the member's own screen:

> About 9 minutes of play left. Top up at the counter to keep going.

**A message the moment it runs out**, with an OK button:

> Your balance is finished. The time you played has been charged. Top up at the counter to
> play more.

**Stop at the moment the money is used up, not after.** The rate and the balance are both
known, so the end time can be worked out up front - Rs 10 at Rs 60/hour is exactly ten minutes -
and the session stopped precisely there, the same way a fixed-duration session already is.
Nothing is owed, nothing goes below zero, and there is no rounding to argue about with a
customer.

The owner pushed back on an earlier version of this that accepted up to a minute of overrun,
and was right to: there is no reason for a member to owe anything when the exact stopping point
is known in advance.

Grace minutes were considered and rejected for the same reason.

**The session stops whether or not OK is pressed.** An unpressed dialog must not mean free
play.

**Billing is automatic** — that part already happens on stop.

## What made this harder than it looked

**The bill is rounded to the nearest ₹10 before the wallet is deducted**, down for a remainder of
0–5 and up for 6–9. So "stop when the balance is spent" still creates debts: a member with ₹16
stopped at exactly ₹16 of play is billed ₹20 and walks away owing ₹4, having been stopped for
running out of money. The first version did precisely that.

Found by a throwaway harness that stops at the calculated minute and then bills it the way
`SessionService` does. First run: **3339 of 5016 cases left the member in debt** — the obvious
answer parks the stop exactly on a rounding boundary, where a few seconds of lateness moves the
bill a whole ₹10. Adding headroom left 228, all small balances against the free buffer.

**The free buffer ends in a cliff.** The instant it expires the *whole* elapsed time becomes
billable, not just the part beyond it — at ₹60/hour with a 10 minute buffer, ten minutes costs
nothing and ten minutes and one second costs ₹10. A member who cannot afford that first
chargeable moment has to be stopped short of the edge, not on it. Final: 6688 cases, none in
debt, nobody losing more than 12% of their balance.

`AffordableMinutes` asks `RoundBillTotal` what each candidate would actually be charged rather
than reimplementing the rounding, so it cannot drift out of step with billing.

**The overlay had the same defect**, and it was nearly missed: it already warned and auto-stopped
client-side on "remaining balance under ₹1", walking into the same trap. Both sides now share one
rule — the overlay stops the session while the PC is running, the server monitor is the backstop
for when it is closed or off the network. If they disagree, whichever fires first decides and one
of them is wrong.

## Still open here

**A member with ₹1 can start a session and play the free buffer for nothing, repeatedly.** The
minimum balance to start is ₹1 and the buffer is 10 minutes, so ₹1 buys ten free minutes, again
and again. This predates the work above and was not introduced by it. Fixing it means either
raising the minimum to cover the first chargeable moment, or charging only for time beyond the
buffer. Not touched, because it changes what customers are charged and that is the owner's call.
