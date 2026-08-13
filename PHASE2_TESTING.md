# Phase 2 — testing the branch EXE

Everything you need, in one file. Work top to bottom on the test laptop.

**Install `AppleEsports-Branch-Setup-2.2.0.exe`.** If you see **2.1.0** anywhere, that is the old
9 August build — it has none of this year's work in it. Ignore or delete it.

---

# Part 1 — Before you start

## Is the laptop suitable?

| Needs | Why |
|---|---|
| Windows 10 or 11, 64-bit | |
| ~2 GB free disk | the 164 MB file expands to about 1.5 GB |
| You can log in as Administrator | it creates two Windows services |
| Ports **5016** and **5433** free | the branch uses both |
| Internet you can switch off properly | unplug the cable, or wifi off |

**Check the ports.** On the laptop, in PowerShell:

```powershell
Get-NetTCPConnection -LocalPort 5016,5433 -ErrorAction SilentlyContinue |
    Format-Table LocalPort, State, OwningProcess
```

Nothing listed = good. Anything listed = something else is using that port; tell me what.

> This is why it cannot be tested on the development PC — Docker holds both ports there.

## If it is a virtual machine

**Take a snapshot now.** Then Test 9 (power cut) is just "reset the VM", and any mess rolls back
in seconds instead of reinstalling Windows.

## Copy these across

The whole `TEST-2026-08-13` folder. A USB stick is fine.

## Clean the laptop first

Run **`UNINSTALL-EVERYTHING.ps1`** — right-click → *Run with PowerShell*. It will ask for
Administrator. Do this **even on a laptop that has never had it**, so you know you are starting
from nothing.

> ### Only ever before a FIRST install
>
> That script **deletes the branch database**. Its own header says so — it is a clean-slate
> script and is meant to.
>
> **Never run it before an upgrade.** An earlier version of this document said to run it every
> time, which is wrong and cost a real test its trading data: the shop had traded offline, the
> uninstaller wiped the database, and the fresh install adopted an empty branch. Nothing was
> actually lost, because the takings had already reached Head Office — but nothing on screen
> would have told you that.
>
> To upgrade, install straight over the top, or let the branch update itself. Both keep the data.

---

# Part 2 — Things you will need during the tests

| | |
|---|---|
| Installs to | `C:\Program Files\Apple Esports` |
| Database and logs | `C:\ProgramData\Apple Esports` |
| Windows services | `AppleEsportsDb` and `AppleEsportsApi` |
| Dashboard address | `http://127.0.0.1:5016` |
| Desktop icon | **Apple Esports** |
| Database | name `gamecafe_erp`, user `appleesports`, port `5433` |

## How to look inside the branch database

Open PowerShell **as Administrator** (the password file is deliberately locked down), then:

```powershell
$env:PGPASSWORD = (Get-Content "C:\ProgramData\Apple Esports\db.secret" -Raw).Trim()
$psql = "C:\Program Files\Apple Esports\pgsql\bin\psql.exe"
& $psql -U appleesports -p 5433 -d gamecafe_erp -c 'SELECT "Name" FROM branches;'
```

Keep that window open — several tests use it. Just change the bit in quotes.

## The three log files

If anything fails, send me these:

```
C:\ProgramData\Apple Esports\logs\setup-database.log
C:\ProgramData\Apple Esports\logs\setup-api.log
C:\ProgramData\Apple Esports\logs\postgres.log
```

---

# Part 3 — The plan

Four sittings. You can stop after any of them.

| Sitting | Tests | Roughly | Needs me? |
|---|---|---|---|
| **1 — Does it install?** | 1–6 | 30 min | yes, at Test 5 |
| **2 — Does it work offline?** | 7–8 | 30 min | yes, at Test 8 |
| **3 — Does it survive abuse?** | 9–10 | 15 min | no |
| **4 — The newest feature** | 11 | 15 min | yes, before you start |

**Tests marked STOP:** if one fails, stop there. The tests after it will not mean anything.

> **Please expect Sitting 1 to fail.** The installer failed six times when it was first written,
> and that is where six of the bug fixes came from. Since then all of Phase 1 has gone in and none
> of it has ever been through an installer. A failure is the seventh bug being found, not a sign
> that something is wrong. Send me the logs and I will fix and send a new build.

---

# SITTING 1 — Does it install?

## Test 1 — It installs · STOP

1. Double-click **`AppleEsports-Branch-Setup-2.2.0.exe`**
2. Choose **"Operator counter PC — runs the branch (database + dashboard)"**
3. Wait. The database step takes a minute or two — PostgreSQL setting itself up, first time only.

**PASS:** it finishes and says it succeeded.

**FAIL:** it shows an error, *or* it says it succeeded but Test 2 finds nothing. Send the logs.

---

## Test 2 — The six old bugs stayed fixed

```powershell
Get-Service AppleEsportsDb, AppleEsportsApi | Format-Table Name, Status, StartType
Test-Path "C:\ProgramData\Apple Esports\data"
Test-Path "C:\Program Files\Apple Esports\api\AppleEsportsErp.Api.exe"
```

**PASS:** all of these —

- Both services listed, **Running**, StartType **Automatic**
  *(bug: the API service was never created — and this is also what makes it start with Windows)*
- `ProgramData\...\data` is **True**
  *(bug: PostgreSQL cannot initialise inside Program Files)*
- The API .exe is **True**
- `setup-database.log` has no "access denied" on its own config file
  *(bug: setup locked itself out of the file it had written)*

**FAIL:** any service missing or Stopped, or the data folder not there.

---

## Test 3 — The dashboard opens · STOP

Open the **Apple Esports** icon on the desktop.

**PASS:** the login screen appears.

**FAIL:** blank, or "cannot reach". Then also try `http://127.0.0.1:5016` in a normal browser:

- Browser works, app does not → the app's address problem (bug: IPv6 `localhost`)
- Neither works → the API is not running; check Test 2 again

---

## Test 4 — It did NOT invent its own shops · STOP

**The most important test here.** This is the fault that broke syncing last time.

```powershell
& $psql -U appleesports -p 5433 -d gamecafe_erp -c 'SELECT "Name" FROM branches;'
& $psql -U appleesports -p 5433 -d gamecafe_erp -c 'SELECT count(*) FROM operators;'
```

**PASS:** **zero branches and zero operators.** Empty is correct — the shop has not been told
which shop it is yet.

**FAIL:** you see Adajan, Citylight, Katargam, Varachha and 8 operators.

That means the counter created its own copy of the whole business, with **its own ID numbers**.
Nothing it ever records would match Head Office. **Stop and tell me.**

---

## Test 5 — It takes its identity from Head Office · STOP

Internet **on**. In the app, the first-run setup asks three things:

1. **Server address** — leave it as this PC. This PC *is* the counter.
2. **Which shop this is** — the list must come from Head Office
3. **An admin PIN** — at least 4 characters. Write down what you chose.

Pick a shop and finish.

**PASS:** the list shows your four real shops, and setup completes.

Then:

```powershell
& $psql -U appleesports -p 5433 -d gamecafe_erp -c 'SELECT "Id", "Name" FROM branches;'
```

**Send me that Id.** I check the same shop on the server has the **same Id**.

**FAIL:** the list is empty or wrong, setup errors, or the Ids do not match. Different Ids mean
everything this shop records is invisible to Head Office.

---

## Test 6 — It holds only its own shop

```powershell
& $psql -U appleesports -p 5433 -d gamecafe_erp -c 'SELECT "Name" FROM branches;'
& $psql -U appleesports -p 5433 -d gamecafe_erp -c 'SELECT count(*) FROM operators;'
& $psql -U appleesports -p 5433 -d gamecafe_erp -c 'SELECT count(*) FROM pcs;'
```

**PASS:** exactly **one** shop — the one you chose — with its own staff and PCs.

**FAIL:** all four shops are there. A counter PC should hold its own shop only: smaller, faster,
and a compromised counter exposes one shop rather than the business.

**→ Message me now with the Id from Test 5, before Sitting 2.**

---

# SITTING 2 — Does it work without internet?

## Test 7 — It trades with the internet OFF · STOP

**This is the whole point of Phase 2.**

Switch the internet off **properly** — unplug the cable or turn wifi off. Not just "Head Office is
slow".

Now do a normal evening's work. Log in as an operator (password `12345`):

- Start a session on a PC
- Stop it and take a **cash** payment
- Top up a member's wallet
- Open the **End of Day** screen

**Write down what you did** — how many sessions, how much cash. You will need it for Test 8.

**PASS:** all of it works exactly as it does online. Nothing says "cannot reach server", nothing
hangs.

**FAIL:** anything blocks, hangs, or complains about the network. The shop must not need the
internet to take money.

---

## Test 8 — It reports to Head Office when the internet returns

Plug the internet back in. Wait about a minute — the courier runs every 30 seconds.

**Tell me, and send what you wrote down.** I check the server for the same sessions and payments.

**PASS:** what you did offline appears at Head Office.

**FAIL:** it does not arrive.

> **"Successfully synced" on screen is not evidence.** That message has only ever meant the server
> answered. The server can accept a record and still fail to keep it, and the branch would tick it
> off and never send it again. I check the actual rows.

---

# SITTING 3 — Does it survive abuse?

## Test 9 — It survives a power cut · STOP

With a session running, **cut the power**. Hold the power button, pull the plug, or reset the VM.
**Do not shut down properly** — that tests the wrong thing.

Switch it back on. Log in to Windows. **Do not open anything.** Wait two minutes.

```powershell
Get-Service AppleEsportsDb, AppleEsportsApi | Format-Table Name, Status
```

**PASS:** both **Running** without anyone starting them. The dashboard opens. The session that was
running is still there.

**FAIL:** either service Stopped. A counter PC must come back by itself after a power cut — the
operator will not know to start a Windows service.

---

## Test 10 — Installing again does not break it

Run the installer again **straight over the top**. Do NOT run the uninstaller first — that
deletes the database, and then this test proves nothing except that a clean-slate script works.

Better still, let the branch update itself: publish a newer version at Head Office and leave the
app open. That is what will actually happen in the shop.

**PASS:** it finishes, both services run, and **your data is still there** — same shop, same
sessions, same members. `setup-database.log` should say *"Database 'gamecafe_erp' already exists -
keeping it."*

**FAIL:** it errors about files being in use *(bug: services hold their own DLLs open)*, or the
database is wiped.

---

# SITTING 4 — The newest feature

## Test 11 — A shift nobody closed

**Message me before starting.** I have to put a shift's clock back two hours in the database, or
the screen will not appear at all.

1. Log in as operator **A**. Put money in the drawer. Start a session.
2. **Walk away.** Do not end the shift. Close the app.
3. Tell me — I set that shift's clock back.
4. Log in as operator **B**, same shop.

**PASS, all of these:**

- B gets a screen saying *"A's shift was never closed"*
- It asks B to count the drawer and the stock, **without showing what it expects**
- Only after B submits does it show the difference, and ask why
- B's own shift starts only after that
- **B's opening drawer is what B counted** — not what the system expected

**FAIL:** if the expected amount is visible before B counts, or B can get to the dashboard without
counting, or B's drawer opens on the system's figure instead of B's count.

---

# Part 4 — Fill this in

| Test | What | Result |
|---|---|---|
| 1 | It installs | |
| 2 | Services running, database in ProgramData | |
| 3 | Dashboard opens | |
| 4 | **Shop is EMPTY before setup** | |
| 5 | Takes its Id from Head Office | |
| 6 | Holds only its own shop | |
| 7 | **Trades with internet OFF** | |
| 8 | Reaches Head Office afterwards | |
| 9 | Survives a power cut | |
| 10 | Reinstall keeps the data | |
| 11 | Shift takeover | |

---

# Part 5 — Will look wrong, but is not

**Test 4 expects a completely empty shop.** No shops, no staff. That is correct — it has not been
told which shop it is yet. Seeing all four shops is the failure, and it is the serious one.

**No email will arrive from the branch.** The staff email addresses are invented and the domain is
not ours. Known, deliberate for now.

**Every operator's password is `12345`**, from the original setup.

**The gaming PC program points at a domain we do not own.** It only affects gaming machines
(Phase 3), not the counter. Being fixed separately.

---

# Part 6 — If you get stuck

Send me:

1. **Which test**, and what you saw
2. The three log files from Part 2
3. A screenshot if it is something on screen

For a failed install, the logs matter more than the screenshot — the installer reports a failure
but the reason is always in the log.
