# Apple Esports — rebuild plan

Agreed 10 August 2026. Supersedes the earlier EXE plan.

---

## The shape of it

```
                    SERVER  (Oracle — Head Office)
                    DB + API + Backend · the master copy
                    · super admin sees all four branches
                    · owns the schema; a branch never edits it
                    · frozen after Phase 1
                                  │
                      internet, when there is some
                      (records up, commands down)
                                  │
      ┌───────────────┬───────────┴────────┬───────────────┐
   Adajan         Citylight            Katargam        Varachha
   OPERATOR PC — the whole shop, running locally
   · PostgreSQL, identical schema to the server
   · the same API and the same dashboard
   · holds ONLY this branch's rows
   · trades with zero internet, indefinitely
                                  │
                    shop LAN — always up, no internet needed
                                  │
        ┌──────────┬──────────────┼──────────────┬──────────┐
      USER       USER           USER           USER       USER
      gaming PCs — thin client, one small SQLite file, no PostgreSQL
```

Three rules that everything else follows from:

1. **The server owns the schema.** Branches use the same migrations. Nothing about the
   server's database changes to suit a branch.
2. **The branch is the shop.** Every click an operator makes is answered by the PC in front
   of them. The internet is only ever used to report upward and receive instructions.
3. **Identity comes from above.** A branch takes the server's identifiers. It never invents
   its own — that is precisely what broke sync last time.

---

## Where we actually are

The remote was rewound to `feaa555`, which removed **51 commits**. Only 24 were
installer work; **27 changed server code**, including the dashboard password gate,
HTTP-only cookie auth, money by the 06:00–06:00 trading day, the power-cut pause, the EOD
outage sections, and the bugs found during testing.

`feaa555` is an ancestor of the local `23569b4`, so **nothing is lost** — every commit is
still in local history, and restoring is a fast-forward, not a merge.

The Oracle server has already been redeployed from the rolled-back code:

```
/api/provisioning/ping        404
http://140.245.195.222:8081/  200   ← no password prompt
```

The dashboard is open on the public internet, and the two security problems originally
reported are both back.

---

## Phase 1 — fix the server, then freeze it

### 1a. Close the exposure (first, on its own)

Restore, verify, deploy as one small change:

| Commit | What it does |
|---|---|
| `af700f5` | the nginx dashboard password gate, in version control |
| `282ac28` | HTTP-only cookie auth + CSP headers — keeps tokens out of the inspector |
| `8ad1866` | fixes the login bouncing back to the portal after that migration |
| `0806b56` | fixes the Basic Auth header defeating the cookie fallback |

The last two are not optional extras: they are the fixes for bugs the first two introduced.
Deploying `282ac28` without them reproduces a login loop.

**Verified by:** the dashboard prompts for a password; a logged-in session shows no token in
the browser inspector; operator, admin and super admin can all still log in.

### 1b. Bring back the rest of the server work

The remaining 23 server-side commits, applied in order and checked in groups rather than all
at once. Roughly:

- **Money** — trading day 06:00–06:00, credited to the shift that took it
- **Time** — IST fixed at +05:30 everywhere
- **Sessions** — power-cut pause, resume-or-stop prompt, one session per member
- **Reporting** — EOD power cuts and internet outages, wallet top-up times, UPI in Online Desk
- **Access** — portal session leak, Updates page
- **Sync** — outbox actually written; inbox no longer discarding what it accepts

Installer and desktop-client commits are **not** brought back. The EXE is rebuilt in Phase 2.

### 1c. Verify and freeze

A written pass over each restored behaviour against the live server, then tag the release.
**After this the server is not edited again** except through a deliberate, tested release.

---

## Phase 1 — where it actually stands, 12 August 2026

Restoring the rolled-back work is done. So is everything the owner raised while testing it.

### Live and verified on the server

| Built | Proven by |
|---|---|
| Dashboard password gate, cookie auth | 401 without credentials, 200 with, from outside |
| Money on the 06:00–06:00 trading day | all four branches reconcile |
| Emails actually sending | real mail delivered |
| Five EOD money bugs | figures agree with the End of Day screen |
| End Shift flow — cash count, real stock list, last-shift tick | owner confirmed the EOD mail matched |
| Direct wallet deduction, no member approval | owner confirmed |
| Updates dashboard, history, operator email, `updates` permission | endpoints answered, 12 operators backfilled |
| Wallet runs out — stops on time, warns, tells the member | 6688 billing cases, none leaving a debt |
| Cash difference emails the owner | — |
| The trading day closes itself when nobody ticks "last shift" | closed 11 Aug on live data, mail arrived |
| B takes over A's abandoned shift — blind count, drawer and stock, closed by somebody else | 95 checks against a real database, real login, real drawer |

### Phase 1 is closed

**Frozen 12 August 2026** at `f4dc789`, tagged **`phase1-server-frozen`**. That tag is the commit
the branch EXE is built against.

From here the server changes only through a deliberate, tested, versioned release. Not because it
is finished, but because Phase 2 builds a branch database from these same migrations, and a schema
that moves while the EXE is built against it is close to what went wrong last time.

The freeze is a discipline, not a lock. Everything on the open list below is still fixable — it
just gets a version number.

**Verified before freezing:** the real services driven against a real database with the real
migrations, 123 checks; plus a walkthrough on the live server — a shift left open four hours, taken
over, counted ₹240 short, the shortfall recorded against the operator whose shift it was, and the
incoming operator's drawer opened on the money actually in it.

**One check still outstanding:** nobody has looked at the End of Day screen since the cash panel
was fixed. The logic is proven and the field names line up, but the rendered screen has not been
seen, and the new handover line only appears on a day that has had a handover.

### B takes over A's abandoned shift — built 12 August 2026

Specified in `CASH_HANDOVER_DESIGN.md`, which now records what was built.

The order is enforced rather than drawn: **login issues no shift at all** while somebody else's
uncounted drawer is open at that branch. The handover issues it. A blocking screen alone can be
refreshed past; withholding the shift cannot.

The count is blind — the expected figures are never sent to the counting screen — and it is
written down before the comparison is revealed, so it cannot be revised to agree. The trap held:
**who closed it is stored separately from whose shift it was** (`shifts.ClosedByOperatorId`,
`cash_register.CountedByOperatorId`, and a `shift_handovers` row holding both sides). The
incoming operator opens on **what they counted**, so a shortfall found on arrival never follows
them into their own shift.

A shift counts as abandoned after **two hours** with nothing recorded — no session, no bill, no
cash movement, no audited action. Deliberately generous: branches run up to three counters and
one shift per counter is not enforced until Phase 2, so too short a threshold closes a live
colleague out or blocks the second counter from opening, while too long simply falls back to the
automatic day close that already exists. One constant if the owner wants it moved.

Two things came with it, because the flow is incoherent without them: the opening prompt is now
the two questions the design called for (a float for the first shift of the day, the inherited
figure for every shift after), and `SHIFT_CLOSED` no longer signs an operator out when a handover
is the reason they have no shift — that would have looped them between the login screen and the
count with an uncounted drawer at the end of it.

### Decisions waiting on the owner

- **₹1 buys ten free minutes, repeatedly** — the minimum to start is ₹1 and the first ten minutes
  are free, so a session can be restarted indefinitely without paying. Predates this work.
  Closing it changes what customers are charged.
- **An operator created at Head Office cannot log in at a branch** — see the known gap at the end
  of this document.
- **Before handover:** the seeder has the owner's personal Gmail compiled into it.

- **Every operator's email address is invented, and the domain is not ours.** `DataSeeder.cs`
  builds them as `name_branch@appleesports.com`; all eight live operators have one. That domain
  resolves to Amazon parking addresses — it is registered, it is not this business's, and it is
  not accepting the mail. Seen as a Gmail bounce on 12 August 2026.

  Two consequences. Every operator-facing email — update approvals, low stock, lockouts — goes
  nowhere, so the "reaches all twelve operators" fix reaches none of them. And the account
  lockout after five wrong passwords **automatically emails a working password-reset link** to
  that address, so anyone who ever runs mail on `appleesports.com` can lock out an operator and
  collect the link.

  **Left as-is deliberately while testing**, on the owner's decision — no real operators are
  using the system and nothing has been delivered (Gmail is deferring, not accepting).

  **The addresses themselves are data, and the freeze does not trap them.** They are editable in
  Settings (`PUT /api/operators/{id}` writes `Email`), and the seeder cannot undo the edit: it
  returns early if Adajan or Citylight already exist, so it has run once on this database and
  will never run again. Fixing them is five minutes in the UI, whenever.

  What *is* code, and therefore does belong on a list:

  - **The seeder still hardcodes the fake domain**, and the owner's personal Gmail as the super
    admin. That only bites a **fresh** install — a new server, or a branch EXE in Phase 2 — where
    the seeder does run. Fix it before anything is installed anywhere new.
  - **Optional guard:** refuse to send a password-reset link to an address nobody has confirmed.
    Worth having whatever the addresses say, because the lockout path emails one automatically.

### How money bugs were actually found

Worth recording, because it held every time: **each one was caught by running the code against the
real billing path, never by reading it.** The wallet stop-time maths looked right and left members
in debt in 3339 of 5016 cases. The automatic day close correctly closed the day and silently
emailed nobody. An update email would have reached one operator out of twelve.

All three read as correct. For anything touching money, drive the real functions with realistic
inputs and check the outcome — not that the step ran.

It held once more on the takeover. Reading the shift-takeover code gives no hint that the client
would sign the operator out the moment it ran: any page behind the blocking screen calls a
shift-scoped endpoint, which correctly answers `SHIFT_CLOSED` because there is deliberately no
shift, and the response interceptor treats that as a dead session and returns to the login
screen — where logging in produces the same handover again. A loop with an uncounted drawer at
the end of it, from a file that was not being changed.

---

## How Phase 2 is kept away from the server

Agreed 12 August 2026, and this is the part that actually matters.

**What happened last time:** the installer work and the server work shared one history. Undoing the
installer meant rewinding the branch, and the rewind took **27 server commits** with it — the
password gate, the cookie auth, the trading day, the power-cut pause. The dashboard went back onto
the open internet as a side effect of abandoning an EXE. Nobody decided that; the shape of the
repository decided it.

**So it is not allowed to be possible again.** Three things, in order of how much they matter:

**1. The EXE never commits to `main`.** Phase 2 lives on its own branch, `phase2-exe`. Abandoning
Phase 2 then means deleting a branch, and the server cannot notice. This is the whole fix — the
other two are only there for when somebody forgets it.

**2. The frozen server is held by three independent refs.** Any one of them can restore it:

| Ref | |
|---|---|
| `phase1-server-frozen` | annotated tag — a force-push to a branch does not touch it |
| `phase1-frozen` | a branch, so it is easy to check out and deploy from |
| `main` | where development continues |

A rewind of `main` no longer loses anything, because the other two do not move. Deleting all three
takes three deliberate, separate commands.

**3. The server is deployed from a named ref, not from wherever `main` happens to point.** While
Phase 2 is in progress `main` is a moving target. Updating the live server means checking out the
tag or `phase1-frozen`, never pulling whatever landed on `main` this afternoon.

**Server code changed during Phase 2 gets its own commit on `main` and its own release.** If the
EXE turns out to need something from the server — and it will, for provisioning and sync — that
change is a server change. It is made deliberately, tested, versioned, and it does **not** ride
along inside installer work where rolling one back rolls back the other.

---

## Phase 2 — the operator EXE

One installer, run on the counter PC, producing a complete shop:

- **PostgreSQL**, bundled — no separate install, runs as a Windows service
- **The same API binary** as the server, serving the same dashboard
- **The same migrations**, so the schema is identical by construction rather than by care
- Starts with Windows; survives power cuts; needs no internet to trade

**First run asks two things and no more:** which branch this is, and an admin PIN. It then
takes that branch's real identity from the server — the branch, its PCs, its pricing, its
operators, with the server's identifiers — and holds **only that branch's rows**.

The installer work already done is not thrown away. It is in local history and will be
drawn on rather than rediscovered, particularly:

- PostgreSQL cannot initialise inside `Program Files`; its data belongs in `ProgramData`
- Setup must not grant itself read-only access to a file it needs to rewrite
- Services hold their own DLLs open — stop them before replacing files, or an upgrade fails
  halfway and leaves a half-replaced install
- `localhost` resolves to IPv6 first and can reach an entirely different server on a machine
  running Docker or WSL — bind and address `127.0.0.1`
- A Windows service's working directory is `System32`; every relative path must be made
  absolute, or the log lands there and grows unbounded

---

## Phase 3 — the user EXE (gaming PCs)

A thin client. **No PostgreSQL** — a single SQLite file instead.

A gaming PC owns no business records: sessions, billing and members all belong to the
operator. A database service on it would cost memory, a port and another thing to fail
mid-match, for records it does not own. What it genuinely needs to survive a LAN drop or a
power cut is small:

| It stores | So that |
|---|---|
| Which PC it is, which branch, the operator's address | it can find its way back after a reboot |
| A snapshot of the current session | a power cut does not leave a customer looking at a bare Windows desktop, or a paid session unlocked |
| A short queue of what it did while disconnected | nothing it observed is lost when the LAN returns |

SQLite rather than a JSON file because a power cut mid-write corrupts a text file — which is
the exact case this exists for.

Locked-down as agreed: no close button, no minimise, no Alt+F4, no escape to the desktop.
Everything that could let a customer out sits behind the admin PIN.

---

## Phase 4 — sync

**Upward (branch → server).** A durable outbox at the branch; a courier that ships entries
whenever there is a line; an inbox at the server that stores verbatim before interpreting.
Four faults already found and fixed must stay fixed:

- the branch must not invent identifiers (Phase 2 handles this)
- the Head Office address must actually be written at install time
- an optional reference the server does not have — a branch's shift — must be dropped, not
  cost the whole record
- **delivery and application must be reported separately.** "Successfully synced" only ever
  meant HTTP 2xx; the server can accept a record and still fail to apply it, and the branch
  would mark it delivered and never send it again

**Downward (server → branch).** Does not exist today; sync is one-way. Needed for the super
admin to act on a branch — *stop this session*, *this PC is out of service*, *take this
update*. The branch asks for pending commands on the same cycle it reports on, so it works
through the same one connection and the same outage handling.

**Visible.** "Is my branch reporting?" must be answerable at a glance, not by reading a log
file. Anything queued and undelivered is money the server cannot see.

---

## Phase 5 — rollout

One branch, one full trading day, watched. Then the other three.

---

## Deliberately not doing

- **Changing the server's database schema to suit a branch.** The server is the master.
- **Bringing back the old installer.** Rebuilt in Phase 2, referencing its bug fixes.
- **A full copy of all four branches on every till.** Each operator PC holds its own branch
  only — smaller, faster, and a compromised counter PC exposes one shop rather than the
  business.
- **PostgreSQL on gaming PCs.**

---

## Known gap, not yet solved

An operator created at Head Office **cannot log in at a branch**. Password hashes never
travel, because the endpoint that hands a branch its identity has to be unauthenticated — a
branch has no credentials until that call gives it one. Operators who already exist under the
same username keep working; a newly created one would not.

This needs an authenticated identity sync. It is not built, and it is not in Phase 2.

---

# Updates and versions

Agreed with the owner, 11 August 2026.

Updates carry bug fixes and changes to both the branch EXE and the server. The owner approves
one; every branch then takes it.

## What already exists

The **tracking** half is built and working:

- create a version, approve it for all branches
- see every branch's current version, and how many of its PCs are up to date
- a per-branch auto-update switch
- an Updates page in the dashboard

## What does not exist

**Delivery.** Nothing packages a build into a downloadable release, nothing hosts it, and
nothing on a branch fetches or installs it. That part lived in the desktop client, which is out
of the repository until Phase 2. So today the dashboard can say "2.2.0 is ready" and pressing
Update Now would have nothing to fetch.

Updates therefore cannot be finished before Phase 2 — only alongside it.

## Phase 1 — built and live (server and dashboard)

Deployed 11 August 2026 as `00400e9` and `a4fa10f`.

- **The Updates page rewritten in plain English**, for the owner, admins and operators, each
  seeing their own scope. The owner can add an update and approve it; a branch sees what it is
  running, what is waiting, and what changed.
- **Release notes are required** when adding an update. They are the only thing an operator can
  read before deciding whether to apply one during a busy evening, and "various fixes" tells
  them nothing.
- **Update history**, kept permanently and readable by any operator — every version, newest
  first, with what was in it.
- **Approving emails every operator**, in plain words, saying an update is waiting and that
  nobody playing will be interrupted.
- **The auto-update tick works and is on by default.**
- **Updates is now a real permission**, on by default for operators and admins, listed with the
  others an admin can manage, and checked on the route. It had been in the sidebar with no
  permission key at all.
- **A progress bar driven by what the branch actually reports** — stage, percent and message are
  columns a branch writes to, with a stuck stage called out after twenty minutes.

### Three bugs found while building it

**Automatic updates were switched off for every branch at the moment it first reported in.** The
entity defaults the flag on; the line creating the row on first contact set it to `false`, so the
default could never take effect. The owner would have had to turn it on manually for all four
branches without ever being told why it was off.

**The update email would have reached one operator out of twelve.** `OperatorStatus.Active` means
"logged in right now" — `LoggedOut` is where an operator sits after going home. Filtering on
Active would have mailed only whoever happened to be at a counter that second, skipping seven
real addresses. Backwards, too: the people who most need telling are the ones not logged in. The
same mistake was already in the admin recipient list, latent because no operator carries the
global-admin flag; fixed alongside.

**Updates was ungoverned.** In the menu, absent from the permission list, unchecked on the route.

### What was deliberately not faked

The progress bar could have been animated on a timer when somebody presses Update Now. It would
have looked identical while working and lied outright when a download stalled — a full bar and a
finished update are not the same thing, and the first time an operator trusts one that was
pretending, the page is finished as a source of truth. So it renders only reported state, and
where installing is not possible yet the page says so instead of offering a button that does
nothing.

## Phase 2 — delivery

- Package a build into a release and host it where a branch can reach it.
- The branch downloads the approved version and installs it, **reporting progress to
  `POST /api/versions/branch/{branchId}/progress`** as it goes — stage, percent and a plain
  message. That endpoint and its columns are already live; nothing writes to them yet, which is
  the only reason the bar has nothing to show.
- Record the installer's name and its SHA-256 against the version. `HasInstaller` on the version
  is what makes "Update Now" appear at all, and a recorded installer with no hash is refused by
  the branch rather than trusted — branches talk to Head Office over plain HTTP, so that hash is
  currently the only thing standing between a branch and running a program somebody else chose.
- The branch then passes the same update to its own gaming PCs over the shop network.

**Installed instantly, in the background, without spoiling anyone's game.** The owner was
explicit: the update should apply straight away rather than waiting for a quiet moment, but a
customer must not notice. That is achievable because a session lives in the database rather
than in the running program, and the gaming-PC agent already reconnects by itself — so the new
version can be brought up before traffic moves to it, and a brief reconnect passes unseen.

Waiting for no active sessions, and waiting for the day to close, were both considered and
rejected: an urgent bug fix should not sit unapplied for hours.

## Care needed

An update mechanism is the one piece of software that can break every branch at once. It needs
a way to fail safely — a branch that cannot install an update must carry on running the version
it has, and say so, rather than ending up with neither.
