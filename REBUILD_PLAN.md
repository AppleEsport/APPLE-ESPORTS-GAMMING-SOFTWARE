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

## Phase 1 — what to build now (server and dashboard)

- **Email the operators** when the owner approves a version, telling them an update is waiting.
- **Update history**, kept permanently: version, when it was approved, and **what is in it** in
  plain words, so an operator can read what is changing before they apply it.
- **A progress bar** while an update installs: downloading, installing, restarting, done. The
  owner asked for this specifically — during a slow download, silence looks like a failure.
- **Auto-update tick, on by default** for operators and admins.
- **The Updates page shown by default** to operators, admins and the super admin, and added to
  the operator and admin menu permissions.
- **Every word in plain English**, on the page and in the email. No version jargon, no
  "artefact", no "deployment".

## Phase 2 — delivery

- Package a build into a release and host it where a branch can reach it.
- The branch downloads the approved version and installs it.
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
