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

## Deliberately not part of this

**One shift per counter PC.** Katargam and Varachha have three counter machines each, so the
rule has to be per machine, and the server has no reliable machine identity — a browser is
not a PC. It belongs in Phase 2 with the EXE, where each counter has a real identity. Building
it now on a guessable signal would look enforced and not be.

---

# When a member's wallet runs out mid-game

Agreed with the owner, 11 August 2026. Not yet built.

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

## Care needed

This touches session stopping and wallet deduction at once: the two places where a mistake
either gives away free play or overcharges a customer. Meet's commit 6109844 also changed
wallet deduction on session stop, in the same file as the fix for online top-ups being counted
as cash. That commit should be read against those changes before this is built.
