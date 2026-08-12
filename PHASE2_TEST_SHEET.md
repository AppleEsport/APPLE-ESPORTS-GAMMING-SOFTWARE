# Testing the branch EXE

`AppleEsports-Branch-Setup-2.1.0.exe` — the installer that turns a counter PC into a whole shop.

Work top to bottom. Each test says what to do, and what **passing** looks like. If a test marked
**STOP** fails, stop there and send me the log — the tests after it will not mean anything.

Nothing here touches the live server's data except reading it. Head Office is only ever asked
questions, never told anything, until Test 8.

---

## What you need

| | |
|---|---|
| **A spare Windows PC, or a fresh virtual machine** | Windows 10 or 11, 64-bit |
| **Not your development PC** | it already uses ports **5016** and **5433**, and the installer wants both. It will collide. |
| Administrator rights | the installer creates two Windows services |
| About 2 GB free disk | the 172 MB installer expands to roughly 1.5 GB |
| Internet, at the start | only to fetch this branch's identity from Head Office |
| A way to switch the internet off | unplug the cable, or turn off wifi — Tests 7 and 8 need it genuinely off |

**Copy onto the test PC:**
- `dist\AppleEsports-Branch-Setup-2.1.0.exe`
- `UNINSTALL-EVERYTHING.ps1` (from your `exe test` folder)

---

## Useful things to know

| | |
|---|---|
| Installs to | `C:\Program Files\Apple Esports` |
| Database and logs | `C:\ProgramData\Apple Esports` |
| Windows services | `AppleEsportsDb` and `AppleEsportsApi` |
| Dashboard | `http://127.0.0.1:5016` |
| Database | name `gamecafe_erp`, user `appleesports`, port `5433` |
| Setup logs | `C:\ProgramData\Apple Esports\logs\setup-database.log` and `setup-api.log` |

**To look inside the branch database** (run in PowerShell on the test PC):

```powershell
$env:PGPASSWORD = (Get-Content "C:\ProgramData\Apple Esports\db-password.txt" -Raw).Trim()
& "C:\Program Files\Apple Esports\pgsql\bin\psql.exe" -U appleesports -p 5433 -d gamecafe_erp -c "<SQL HERE>"
```

If `db-password.txt` is not there, look in `setup-database.log` for where the password was put.

---

# Test 1 — It installs · **STOP**

1. Run `UNINSTALL-EVERYTHING.ps1` first (right-click → Run with PowerShell). Even on a fresh PC.
2. Run the installer. Choose **"Operator counter PC — runs the branch"**.
3. Wait. The database step takes a minute or two — that is PostgreSQL setting itself up, first time only.

**Passes if:** it finishes and says so.

**Fails if:** it errors, *or* it finishes cheerfully but Test 2 finds nothing. Send me both setup logs.

> This is the one most likely to fail. It failed six times before. That is normal.

---

# Test 2 — The six old bugs stayed fixed

Run this in PowerShell:

```powershell
Get-Service AppleEsportsDb, AppleEsportsApi | Format-Table Name, Status, StartType
Test-Path "C:\ProgramData\Apple Esports\data"
Test-Path "C:\Program Files\Apple Esports\api\AppleEsportsErp.Api.exe"
```

**Passes if:**
- Both services exist, **Running**, StartType **Automatic** ← *bug 3 (service never created), and "starts with Windows"*
- The `ProgramData\...\data` folder exists ← *bug 1 (database in the wrong place)*
- No error about a locked config file in `setup-database.log` ← *bug 2*

---

# Test 3 — The dashboard opens · **STOP**

Open the **Apple Esports** icon on the desktop.

**Passes if:** the login screen appears.

**Fails if:** blank, or "can't reach". That is bug 6 (the IPv6 address problem) coming back. Also
try `http://127.0.0.1:5016` in a browser — if the browser works and the app doesn't, tell me,
they are different faults.

---

# Test 4 — It did NOT invent its own shops · **STOP**

**This is the most important test on the sheet.** This is the fault that broke syncing last time.

```sql
SELECT "Name" FROM branches;
SELECT count(*) FROM operators;
```

**Passes if:** **zero branches, zero operators.** Empty is correct. The shop has not been told who
it is yet.

**Fails if:** you see Adajan, Citylight, Katargam and Varachha, and 8 operators. That means the
branch created its own fake copy of the whole business with its own ID numbers — and nothing it
records will ever match Head Office. **Stop and tell me.**

---

# Test 5 — It takes its identity from Head Office · **STOP**

Internet **on**. In the app, the first-run setup should ask:

1. The server address (leave as this PC — it *is* the counter)
2. **Which shop this is** — the list must come from Head Office
3. An **admin PIN**

Pick a branch. Finish.

**Passes if:** the branch list is the real four shops, and setup completes.

Then check it took Head Office's numbers, not its own:

```sql
SELECT "Id", "Name" FROM branches;
```

On the **server** (I can run this for you), the same shop must have the **same `Id`**.

**Fails if:** the IDs differ. Everything this shop records would then be invisible to Head Office.

---

# Test 6 — It holds only its own shop

```sql
SELECT "Name" FROM branches;
SELECT b."Name", count(o.*) FROM branches b LEFT JOIN operators o ON o."BranchId" = b."Id" GROUP BY 1;
SELECT count(*) FROM pcs;
```

**Passes if:** exactly **one** branch — the one you chose — with its own operators and PCs.

**Fails if:** all four shops are there. A counter PC should hold its own shop only.

---

# Test 7 — It trades with the internet OFF · **STOP**

**Switch the internet off properly.** Unplug it. Not just "Head Office is slow".

Then do a normal evening's work:

- Log in as an operator
- Start a session on a PC
- Stop it and take a cash payment
- Top up a member's wallet
- Open the End of Day screen

**Passes if:** all of it works exactly as with the internet on. Nothing says "cannot reach server".

**Fails if:** anything blocks, hangs, or complains about the network. The shop must not depend on
the internet to take money. **This is the whole point of Phase 2.**

---

# Test 8 — It reports to Head Office when the internet comes back

Still offline, note down what you did: how many sessions, how much cash.

Now **plug the internet back in** and wait about a minute (the courier runs every 30 seconds).

Tell me, and I will check the server for the same sessions and payments.

**Passes if:** what you did offline appears at Head Office.

**Fails if:** it does not. Note that **"Successfully synced" is not proof** — that message has
only ever meant the server answered, not that it kept anything. I check the actual rows.

---

# Test 9 — It survives a power cut · **STOP**

With a session running, **cut the power**. Hold the button, or pull the plug on a VM. Do not shut
down properly — that tests the wrong thing.

Switch it back on. Do not open anything. Wait two minutes.

```powershell
Get-Service AppleEsportsDb, AppleEsportsApi | Format-Table Name, Status
```

**Passes if:** both are **Running** without anybody starting them, the dashboard opens, and the
session that was running is still there.

**Fails if:** a service is stopped. A counter PC after a power cut must come back by itself — the
operator will not know to start a Windows service.

---

# Test 10 — Installing again does not break it

Run the **same installer again**, over the top. This is what an update will do.

**Passes if:** it finishes, both services run again, and **your data is still there** — same
branch, same sessions, same members.

**Fails if:** it errors on files in use (bug 5), or wipes the database. Check `setup-database.log`
says *"Database 'gamecafe_erp' already exists - keeping it."*

---

# Test 11 — A shift nobody closed

Only after Tests 1–10 pass. This is the newest feature and has never run on a branch.

1. Log in as operator A. Put money in the drawer. Start a session.
2. **Leave it.** Do not end the shift. Close the app.
3. Tell me — I set that shift's clock back so it looks two hours old.
4. Log in as operator **B**, same shop.

**Passes if:** B gets a screen saying *"A's shift was never closed"*, asks B to count the drawer
and the stock **without showing what it expects**, then shows the difference and asks why. B's own
shift only starts after that.

**Passes also if:** B's opening drawer is **what B counted**, not what the system expected.

---

## When you are done

Tell me for each test: passed, or what you saw. For any failure send:

- `C:\ProgramData\Apple Esports\logs\setup-database.log`
- `C:\ProgramData\Apple Esports\logs\setup-api.log`
- `C:\ProgramData\Apple Esports\logs\postgres.log`

---

## Known already, not worth reporting

- **The gaming PC program points at `api.appleesports.com`**, a domain the business does not own.
  It only affects Phase 3 (gaming machines), not the counter. Being fixed separately.
- **Operator emails are invented**, so no email a branch sends will arrive. Deliberate, for now.
- Operator passwords are all `12345` from the original seed.
