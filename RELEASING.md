# Shipping a version

Read this before publishing anything. It exists because the release on 13 August was done
from memory: the push went to a remote that no longer exists, the version was bumped in two
of the three places that matter, and two steps turned out to need permission that had not
been asked for. Every one of those is written down below.

**Rule: follow the steps in order and do not skip the checks.** A release that half-arrives
is worse than one that does not arrive at all, because the branch reports the new version
number while running some of the old code.

---

## What a release actually is

Two separate things, and confusing them is what made updates look broken for a whole day.

| | Who does it | How often |
|---|---|---|
| **Publishing** — building a version and putting it on Head Office | Me, following this file | When there is something to ship |
| **Delivering** — a branch noticing and installing it | The branch, by itself | Every 30 seconds, and 10 seconds after the PC is switched on |

You never have to trigger delivery. If publishing is done correctly, every branch that is
online takes the update within about half a minute, and a branch that was switched off takes
it shortly after it is switched on. There is also a **Check for updates now** button on the
Updates page for when somebody does not want to wait even that long.

---

## Before you start

Two things need permission from the user, and both are near the end. **Ask up front**, so a
release does not stop half-finished:

1. `git push new-origin main` — pushing to the default branch
2. Writing the release row into the live Head Office database

Neither can be worked around. Ask first.

---

## 1. Bump the version — all three files

Miss one and the release is subtly broken.

| File | What it sets |
|---|---|
| `AppleEsportsErp/src/AppleEsportsErp.Api/AppleEsportsErp.Api.csproj` | what the branch reports it is running |
| `desktop-client/AppleEsports.Desktop.csproj` | what the updater compares against to decide "newer" |
| `installer/AppleEsportsBranch.iss` | the installer's own name and version |

```bash
for f in AppleEsportsErp/src/AppleEsportsErp.Api/AppleEsportsErp.Api.csproj \
         desktop-client/AppleEsports.Desktop.csproj \
         installer/AppleEsportsBranch.iss; do
  sed -i 's/2\.2\.6/2.2.7/g' "$f"
done
git diff --stat    # expect exactly 3 files
```

> **Why the desktop csproj is the important one.** `UpdateService` compares the offered
> version against `Assembly.GetExecutingAssembly().GetName().Version` — the *desktop client's*
> version, not the API's. Leave it behind and the branch re-downloads and re-installs the same
> build in a loop, for ever.

> Note: on `main` the API csproj has no `<Version>` at all. That is deliberate — Head Office
> does not report a version to anybody. Do not add one during a cherry-pick.

---

## 2. Build

```powershell
& "installer\build-branch-installer.ps1"
```

Builds the dashboard, publishes the API and the desktop client, stages PostgreSQL, publishes
the gaming-PC agent, and compiles the installer to
`dist\AppleEsports-Branch-Setup-<version>.exe` (~164 MB).

**Check:** the last line says `Successful compile` and names the version you just set. If it
names the previous version, the `.iss` bump was missed.

---

## 3. Commit — and get the branch right

Work happens on **`phase2-exe`**. `main` is the Phase 1 server line and deliberately does not
contain the desktop client or the installer at all, so that Phases 2–4 stay removable.

```bash
git branch --show-current      # expect phase2-exe
git add -A
git status --short             # expect ONLY the files you changed
```

> **Check the file count.** `git add -A` once staged 21,428 PostgreSQL files because the
> staging folder was not ignored. `installer/branch/staging/` and `.cache/` are in
> `.gitignore` now — if the count looks wrong, stop and look before committing.

```bash
git commit
git push new-origin phase2-exe
```

> **The remote is `new-origin`.** There are three:
> - `new-origin` → `harshal4172005/APPLE-ESPORTS-GAMMING-SOFTWARE-new` — **the live one**
> - `origin` → the old repo, **deleted from GitHub**; pushing to it fails with "Repository not found"
> - `meetmoliya` → somebody else's fork; never push here

---

## 4. Cherry-pick the server half onto `main`

The Oracle server pulls from `main`. Only server-relevant files go across.

```bash
git checkout main
git cherry-pick <sha>
```

Expect conflicts on `desktop-client/*` and `installer/*` — `main` does not have those files.
That is correct, not a problem:

```bash
git rm --cached desktop-client/AppleEsports.Desktop.csproj desktop-client/MainForm.cs \
                installer/AppleEsportsBranch.iss
git checkout --ours AppleEsportsErp/src/AppleEsportsErp.Api/AppleEsportsErp.Api.csproj
git add AppleEsportsErp/src/AppleEsportsErp.Api/AppleEsportsErp.Api.csproj
GIT_EDITOR=true git cherry-pick --continue
```

**Then build `main` before it goes anywhere near the server:**

```bash
cd AppleEsportsErp && dotnet build src/AppleEsportsErp.Api/AppleEsportsErp.Api.csproj -c Release
```

```bash
git push new-origin main      # NEEDS PERMISSION — ask first
```

---

## 5. Put the installer on Head Office

```powershell
$f = "dist\AppleEsports-Branch-Setup-2.2.7.exe"
(Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
(Get-Item $f).Length
```

```bash
KEY="C:\Users\harsh\Downloads\ORACLE\ssh-key-2026-07-21 (Private).key"
SRV=ubuntu@140.245.195.222

scp -i "$KEY" dist/AppleEsports-Branch-Setup-2.2.7.exe $SRV:/tmp/

ssh -i "$KEY" $SRV 'set -e
V=/var/lib/docker/volumes/appleesports-v2_releases_data/_data
sudo cp /tmp/AppleEsports-Branch-Setup-2.2.7.exe "$V/AppleEsports-Branch-Setup-2.2.7.exe.part"
sudo mv "$V/AppleEsports-Branch-Setup-2.2.7.exe.part" "$V/AppleEsports-Branch-Setup-2.2.7.exe"
sudo chown 1001:1001 "$V/AppleEsports-Branch-Setup-2.2.7.exe"
sudo chmod 755 "$V/AppleEsports-Branch-Setup-2.2.7.exe"
rm -f /tmp/AppleEsports-Branch-Setup-2.2.7.exe
sudo sha256sum "$V/AppleEsports-Branch-Setup-2.2.7.exe"'
```

**Check: the hash printed by the server must equal the one printed locally.** Every branch
verifies this hash before running the installer, so a mismatch here means nothing installs
anywhere — and it fails silently, because a branch that cannot verify a download correctly
decides to do nothing.

The `.part` name then rename is not fussiness: a branch checking at the wrong moment would
otherwise download a half-written file.

---

## 6. Publish the release row

Until this row exists, `/api/releases/latest` answers `available: false` and no branch will
ever see the file, however correctly it was uploaded.

```
Table:  "VersionInfos"   in   gamecafe_erp   as   gamecafe_admin
Container: appleesports-v2-postgres
```

Required: `CurrentVersion`, `ReleaseNotes`, `ApprovedForRollout = true`, `CreatedAt`,
`ApprovedAt`, `ApprovedByUserId = 'system'`, `BranchesApprovedCount = 0`,
`InstallerFileName`, `InstallerSha256`, `InstallerSizeBytes`.

**NEEDS PERMISSION — ask first.**

> Write the SQL to a file and run it with `psql -f`. Passing it inline through PowerShell
> strips the double quotes, and PostgreSQL then cannot see the capitalised table name.

> **Release notes are read by operators**, not developers. Plain sentences, no version jargon,
> no "artefact" or "rollout". Say what changed and what it means at the counter.

---

## 7. Deploy the server code

The server pulls from GitHub; a pull alone does **not** restart anything.

```bash
ssh -i "$KEY" $SRV 'cd ~/APPLE-ESPORTS-GAMMING-SOFTWARE-new && git pull origin main && git log --oneline -1'
```

Then rebuild the containers. Containers: `appleesports-v2-api`, `appleesports-v2-client`,
plus nginx / postgres / redis / certbot / db-backup which are not rebuilt for a release.

**This affects the live shared server — confirm before running it.**

---

## 8. Prove it

Not "it deployed". Proof.

```bash
curl -s "http://140.245.195.222:8081/api/releases/latest"
```

Must report `available: true`, the new version, and the hash from step 5.

Then, on a branch PC, within about half a minute:

- Updates page shows **Update waiting** with the new number
- it downloads and the app restarts itself
- after restarting, the page shows the new version **without anybody pressing Ctrl+Shift+R**

That last one is the real test. `index.html` is served `no-cache` precisely so a correctly
installed update is actually visible; if a hard refresh is needed, the caching rules in
`Program.cs` have regressed and every future release is affected.

---

## Things that have actually gone wrong

Each of these cost real time. None were found by reading code — all of them by running it.

- **Pushed to `origin`.** That repo is deleted. Use `new-origin`.
- **Pushed `main` while on `phase2-exe`.** `git push new-origin main` pushes the *local `main`
  ref*, not your work. It reports "Everything up-to-date" and pushes nothing. Always
  `git branch --show-current` first.
- **Bumped the API version but not the desktop client's.** The updater compares the desktop
  client's version, so the branch reinstalled the same build in a loop.
- **Uploaded the installer but never wrote the release row.** Branches were told
  `available: false` and nothing happened, with no error anywhere.
- **The version number on screen was believed.** An API showing `1.0.0.0` was diagnosed twice
  as a broken installer. It was an unset version property; the files had updated fine, twelve
  seconds apart. Check file timestamps before blaming the installer.
- **The test sheet told the user to run `UNINSTALL-EVERYTHING.ps1` before an upgrade.** That
  deletes the branch database. Never run it as part of an update — an upgrade installs
  straight over the top, same AppId, and keeps the data.
- **A release was "verified" by it compiling.** Compiling, deploying and answering are not
  working. Step 8 exists for this reason.
