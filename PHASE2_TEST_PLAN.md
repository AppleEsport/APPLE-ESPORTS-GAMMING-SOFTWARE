# Testing on the other laptop — start here

Copy this whole folder to the laptop (USB stick is fine). Everything you need is in it.

**Use `AppleEsports-Branch-Setup-2.2.0.exe`.** If you see a **2.1.0** anywhere, that is the old
9 August build with none of this year's work in it. Delete it or ignore it.

---

## Before you carry it over — is the laptop suitable?

| Needs | |
|---|---|
| Windows 10 or 11, 64-bit | |
| About 2 GB free disk | the 164 MB file expands to roughly 1.5 GB |
| You can log in as Administrator | it creates two Windows services |
| Ports 5016 and 5433 are free | see below |
| Internet you can switch off | unplugging the cable, or wifi off |

**Check the ports are free.** On the laptop, in PowerShell:

```powershell
Get-NetTCPConnection -LocalPort 5016,5433 -ErrorAction SilentlyContinue | Format-Table LocalPort, State, OwningProcess
```

Nothing listed = good. Anything listed means something else is already using that port and the
install will clash — tell me what it is.

> This is why it cannot be tested on the development PC: Docker is already holding both ports there.

**If it is a virtual machine, take a snapshot now.** Then Test 9 (power cut) is just "reset the
VM", and if anything goes wrong you roll back in seconds instead of reinstalling Windows.

---

## The plan, in four sittings

You do not have to do it all at once. Stop after any sitting.

### Sitting 1 — Does it install at all? (about 30 minutes)

Tests **1 to 6** in `PHASE2_TEST_SHEET.md`.

This is the risky part. The installer failed six times when it was first written, and that is
where six of the bug fixes came from. **Expect a failure. It does not mean anything is broken** —
it means we found the seventh.

If it fails: send me the two log files named in the sheet and stop. I will fix and send a new build.

**Message me after this sitting either way** — Test 5 needs me to check the shop's ID number
against the server, and Test 6 only makes sense once I have.

### Sitting 2 — Does the shop work without internet? (about 30 minutes)

Tests **7 and 8**. This is the whole point of the branch EXE.

Unplug the internet **properly** and do a normal evening: start a session, take cash, top up a
wallet, open End of Day. Then plug it back in and tell me — I check whether it reached the server.

Write down roughly what you did offline (how many sessions, how much cash) so we can compare.

### Sitting 3 — Does it survive being switched off badly? (about 15 minutes)

Tests **9 and 10**. Cut the power mid-session, switch back on, and see whether it comes back by
itself. Then install over the top and check nothing is lost.

### Sitting 4 — The newest feature (about 15 minutes, needs me)

Test **11**, the shift nobody closed. Message me when you get here — I have to reach into the
database and put a shift's clock back two hours before the screen will appear.

---

## Fill this in as you go

| Test | What | Result |
|---|---|---|
| 1 | It installs | |
| 2 | Services running, database in ProgramData | |
| 3 | Dashboard opens | |
| 4 | **Branch is EMPTY before setup** | |
| 5 | Takes its identity from Head Office | |
| 6 | Holds only its own shop | |
| 7 | **Trades with internet OFF** | |
| 8 | Reaches Head Office when internet returns | |
| 9 | Survives a power cut | |
| 10 | Reinstall keeps the data | |
| 11 | Shift takeover | |

For anything that fails, send these three files:

```
C:\ProgramData\Apple Esports\logs\setup-database.log
C:\ProgramData\Apple Esports\logs\setup-api.log
C:\ProgramData\Apple Esports\logs\postgres.log
```

---

## Two things that will look wrong but are not

**Test 4 expects the shop to be EMPTY.** No shops, no staff, nothing. That is correct — the
counter has not been told which shop it is yet. If instead you see all four shops and eight staff,
*that* is the failure, and it is the serious one.

**Emails will not arrive.** The staff email addresses are invented and the domain is not ours.
Known, deliberate for now, not a bug to report.

Also: every operator's password is `12345` from the original setup.
