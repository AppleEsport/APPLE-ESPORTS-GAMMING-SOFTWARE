# Apple Esports ERP — Onboarding

You're joining a real, live production system for a gaming café chain in Surat, India — not
a practice project. Three of its branches are trading with real customers and real money right
now, on a server you can reach over SSH. Read this whole file before you touch anything,
especially before you run a deploy command or push to `main`.

This file is the entry point, not the whole story. It tells you what's true *right now* and
where to go for depth. The existing docs in this repo are good and you should use them:

| Doc | What it's for |
|---|---|
| `README.md` | Local setup, architecture overview, tech stack, feature list |
| `RELEASING.md` | The exact, step-by-step process for shipping a new version to the branches. Read this in full before you ship anything — it exists because that process was gotten wrong before, expensively. |
| `docs/MASTER_SOP.md` | The full technical spec (8000+ lines). Reference, not bedtime reading — search it for the thing you're touching. |
| `REBUILD_PLAN.md` | The phase 2–4 roadmap and what's deliberately still open. |
| `docs/CLIENT_HANDOVER_PROTOCOL.md`, `docs/DISTRIBUTED_SYNC_MVP.md` | Sync architecture and client handover specifics. |

---

## 1. Read this before you do anything else

**This system is being actively developed by two people (you and Harshal) at the same time,
each with your own Claude Code session, on the same shared codebase.** That combination has
already caused a real problem once tonight: one session had uncommitted changes sitting in the
working tree, and the other nearly stepped on them by switching branches over the top. Nobody
lost work, but only because it was caught before acting. Follow these rules so it doesn't
happen for real:

1. **Before you start a session of work, run `git status`.** If there are changes you didn't
   make, someone else is mid-edit. Don't commit over them, don't discard them, don't switch
   branches through them. Ask first — a quick message costs nothing; losing someone's
   in-progress work costs a redo.
2. **Say out loud (Slack/WhatsApp/whatever you two use) what you're about to work on before you
   start**, especially if it touches a file the other person might also be touching. This
   project has already had two different features land in the same file (`BillingController.cs`,
   `BranchHeartbeatService.cs`) from two different sessions on the same night.
3. **Never `git push --force`, never `git reset --hard`, never `git checkout -- .` without
   checking `git status` first and understanding what you'd be discarding.**
4. **Never push to `origin` or `meetmoliya`.** The only remote that matters is `new-origin` →
   `harshal4172005/APPLE-ESPORTS-GAMMING-SOFTWARE-new`. `origin` (the old repo, no `-new`
   suffix) is deleted from GitHub — pushing there just fails. `meetmoliya` is someone else's
   fork.
5. **Two branches, two purposes — don't mix them up:**
   - `phase2-exe` — where you actually work. Has everything: the API, the React dashboard, the
     WPF desktop client, the installer scripts.
   - `main` — what the Head Office server runs. Deliberately does **not** contain the desktop
     client or installer — only the API and the dashboard. You get things onto `main` by
     `git cherry-pick`-ing the relevant commit from `phase2-exe`, never by working on `main`
     directly.
   - Always run `git branch --show-current` before you push. `git push new-origin main` pushes
     whatever your local `main` ref currently points at — if you're actually standing on
     `phase2-exe`, that command silently does nothing useful ("Everything up-to-date") while
     you think you just shipped.
6. **Before anything that touches the live Oracle server (SSH, `docker compose build/up`,
   writing to the production database) — say what you're about to do and why, in plain words,
   before you do it.** Not because you need permission for every command, but because a
   sentence of context costs five seconds and a wrong guess against a live server costs a lot
   more. If you're not sure whether something is safe, it probably means asking first is the
   right call.
7. **Money code is not verified by reading it.** Every real bug in the billing/wallet/cash code
   on this project so far was caught by actually running it against a live database and
   checking what ended up in the rows — never by reading the code and reasoning it looked
   correct. If you touch billing, wallets, cash, or EOD reconciliation, write a throwaway
   harness that drives the real service methods against a real Postgres and assert on what's
   actually in the database afterward.

---

## 2. What this actually is

A four-branch (three currently live) gaming café ERP: PC session billing, food orders, member
wallets, cash reconciliation, end-of-day close, all synced between each branch's own local
server and a central "Head Office" server. Full feature list and architecture diagram are in
`README.md` — read that next, it's accurate and well-written.

**The one idea that explains most of the hard design decisions in this codebase:**

Each branch runs its *own* full copy of the system (API + Postgres) on its own counter PC, so a
shop keeps working with zero internet. Head Office holds a synced *copy* of every branch's
data. Writing directly into Head Office's copy is the bug that keeps trying to happen —it
updates the Head Office screen instantly and looks correct, but the actual shop was never told,
so nothing real changed there. The fix, used everywhere in this codebase, is **"Head Office
asks, the branch does"**: Head Office queues an instruction (`BranchCommands` /
`IRemoteBranchControl`), the branch picks it up on its next heartbeat (every 3 seconds) and
carries it out through the exact same code path a person at the counter would use, then reports
back. If you find yourself writing something that changes branch data directly from Head
Office's own database context, stop — that's almost certainly the wrong shape.

The other rule worth knowing before you write a migration: **schema changes are additive-only.**
Never rename or drop a column a branch might still be running an older build against. A branch
that missed an update should still start up and work.

---

## 3. What's live, right now (15 Aug 2026)

- **Version running everywhere: 2.4.8.** Confirmed via `curl http://140.245.195.222:8081/api/releases/latest` and `/api/versions/latest` — both report 2.4.8.
- **Branches actually provisioned at Head Office: Adajan, Citylight, Katargam.** (The README
  and the seeder also mention a fourth, Varachha — it is not live on the current Head Office
  database. Don't assume it exists without checking.)
- **Citylight and Adajan are currently running branch-side version 2.4.9**, even though Head
  Office's approved/offered version is 2.4.8. This is not a bug — it's because 2.4.9 was
  briefly live, both of those branches auto-installed it before it got rolled back, and the
  branch-side updater *refuses to ever downgrade on its own* (by design — see
  `desktop-client/UpdateService.cs`, `if (offered <= InstalledVersion) return null;`). Getting
  them back to 2.4.8 needs an explicit push, not a wait.
  - There is now a UI for this: **Updates page → find the branch's card (Super Admin only) →
    "Send this branch a specific version" → `2.4.8` → Send.** It shows a confirmation dialog
    before it does anything, because it stops and restarts that branch's live service to do
    it. This hasn't been run yet as of writing — it's waiting on a moment when nobody's
    actively on a PC at either branch.
- **Katargam is on 2.4.6** and was never touched by any of tonight's churn.

### Why 2.4.9 existed and got rolled back

Tonight's session built two features end-to-end, shipped them to production, and then rolled
both back at the user's explicit instruction:

1. **Admin/Super-Admin-only stock control** — stock could previously be set by anyone, which
   led to a real bug where Head Office would silently overwrite a branch's real stock count
   with 0. Redesigned so only Admin/Super Admin can change stock, and only by recording a
   delivery (+N units) rather than overwriting a number.
2. **Remote payment processing from Head Office** — let Head Office trigger a payment on a
   specific branch's till remotely, following the same "asks, branch does" pattern.

Both were reverted the same night via `git revert` (not `reset` — history is intact, look for
the "Revert ..." commits) after being live for under an hour, because the second feature had
never actually been tested against a live branch, and the risk profile of remote payment
processing hadn't been fully worked through yet before it shipped. **If you want either of
these back, don't rebuild from scratch — the original commits still exist in history
(`git log --all --grep="stock can only ever be added"` and similar), revert the revert or
cherry-pick them forward, and this time get the payment half tested against a real branch
before it goes near production again.**

### A second, unrelated thing also got fixed tonight, worth knowing about

Two PCs can end up both claiming to be the same branch (e.g., someone runs the branch installer
on a second/backup laptop). This used to only produce a warning in a server log nobody read,
while both PCs kept recording sessions independently and syncing them up under the same branch
— **their takings get merged and cannot be separated afterward.** This is now:
- Actively **refused** at the point a second machine tries to set itself up as an already-live
  branch (`BranchProvisioningController.GetBranchProvisioning`, `BranchAdoptionService`) — the
  second machine has to explicitly force it through, rather than it happening silently.
- **Shown on the Super Admin dashboard** (a red banner) if it's currently happening, instead of
  only in a log.

If you ever see "takings look wrong for branch X" reported, check this before anything else.

### A version-management gap also got closed tonight

Approving a version release never had an "undo." Once approved it's live for the whole fleet
forever, with no way back except editing the database by hand — which is what happened tonight
(twice, in fact: it was manually unapproved once, then got re-approved by mistake from a
different session, which is exactly the kind of collision rule 1 above exists to prevent). The
Updates page (Super Admin) now has:
- **"Un-approve"** — stop offering a version, keep the record for later.
- **"Remove this update entirely"** — delete the record and its installer file outright. Also
  the only way to change what shows as "Newest update," since that display isn't filtered by
  approval status at all — it just shows whichever version was uploaded most recently.

You should never need to touch the `VersionInfos` table by hand again. If you find yourself
about to, that probably means this UI is missing something — fix the UI, not the row.

---

## 4. Getting set up

Follow `README.md`'s **Getting Started** section for the actual commands (Docker Compose is the
easy path). What it won't tell you, because it can't be committed to git:

- **Ask Harshal directly (not over an unencrypted channel) for:**
  - The `.env` values — copy `.env.example`, he'll give you the real `DB_PASSWORD`,
    `JWT_SECRET`, `JWT_REFRESH_SECRET`, `REDIS_PASSWORD`, SMTP credentials.
  - The Oracle server SSH private key, if you'll be deploying — currently at
    `ssh-key-2026-07-21 (Private).key` on Harshal's machine. You'll need your own copy, or your
    own key added to the server's `authorized_keys`.
- **Never commit `.env`, any `.key` file, or a raw connection string with a real password in
  it.** `.env` is already gitignored — keep it that way.

### The live Head Office server, for reference

- Oracle Cloud, `140.245.195.222`, repo at `~/APPLE-ESPORTS-GAMMING-SOFTWARE-new` on the
  server, tracking `new-origin`/`main`.
- `ssh -i "<key path>" ubuntu@140.245.195.222`
- Containers: `appleesports-v2-api`, `appleesports-v2-client`, plus `postgres`, `redis`, `nginx`,
  `certbot`, `db-backup` (the last five aren't rebuilt for an ordinary release).
- DB access: `docker compose exec -T postgres psql -U gamecafe_admin -d gamecafe_erp` (user/db
  name are also in the server's `.env`, not the same as your local one).
- A `git pull` on the server does **not** restart anything by itself — you still need
  `docker compose build api client && docker compose up -d api client` after, and you should
  check `docker compose logs api --tail 30` afterward for errors before considering it done.

**This is explicitly the point Harshal's earlier plan called "test-hosted" before migrating to
the business's own Oracle account for final production.** Treat it as production anyway — real
branches are trading through it — but know that a second migration to a different Oracle
account is still a planned future step, not something that's happened yet.

---

## 5. What to actually do right now

1. Read this file fully (you're almost done), then `README.md`'s Architecture and Getting
   Started sections.
2. `git remote -v` — confirm you're pointed at `new-origin` →
   `harshal4172005/APPLE-ESPORTS-GAMMING-SOFTWARE-new`, not a fork.
3. `git checkout phase2-exe && git pull new-origin phase2-exe` — this is current as of tonight
   (commit `587ea834`, "an approved update can be un-approved or removed entirely"). Get your
   local copy to match before you start anything new.
4. `git status` — confirm it's clean. If it isn't and you didn't leave it that way, stop and
   ask before touching anything (see Rule 1).
5. Get your `.env` and set up local Docker Compose per `README.md`. Confirm you can log in
   locally (seeded SuperAdmin: `harshalparekh40@gmail.com` / `12345` — change this password
   before it ever touches anything real).
6. Before writing any code, tell Harshal (or whoever else is around) what you're about to work
   on, so nobody else starts touching the same files.
7. When you're ready to ship something to the live server, **read `RELEASING.md` start to
   finish first** — it is short, specific, and exists precisely because skipping steps in that
   process has broken things before.

---

## 6. If your Claude Code session seems confused

Point it at this file first (`ONBOARDING.md`) and `RELEASING.md`. It has no memory of tonight's
session on Harshal's machine — this file *is* the transfer of that context. If it's about to do
something that touches the live server or `main`, it should be telling you that plainly before
doing it, and asking if it's not sure. If it isn't, slow it down yourself.
