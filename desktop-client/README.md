# Apple Esports — Desktop Client

A single Windows `.exe` that opens the Apple Esports dashboard in its own window,
with the Apple Esports logo as the application icon.

This is a **thin client**. It does not run Docker, a database, or the API — it connects
to a server that is already running one. That is the deliberate scope: it is what you
install on an operator PC or a gaming PC so staff get an app icon instead of a browser
bookmark.

> Not to be confused with the branch "Server Mode" install described in
> `EXE_BUILD_AND_PRODUCTION_MIGRATION.md`, which is a different, much larger job
> (running the full stack locally for offline-first operation).

---

## Why this replaced the old NSIS installer

The previous `setup.nsi` produced an `.exe` that could not work:

| Problem | Effect |
|---|---|
| Aborted unless Docker Desktop was installed | Would not run on a normal PC |
| Desktop shortcut pointed at `client/dist/index.html` via `file://` | A Vite build cannot load this way — its assets and `/api` calls resolve to `file:///` |
| No `Icon` directive; referenced an `icon.ico` that never existed | Default installer icon, no branding |
| `RequestExecutionLevel admin` + blocking `ExecWait` on `.bat` files | UAC prompt, then apparent hang |

It was an *installer for a server*, not an *application*. This project is an application.

---

## Requirements

- **To run**: Windows 10 or 11. Nothing else — .NET is bundled inside the `.exe`.
  WebView2 ships with Windows 11 and current Windows 10; if it is somehow missing,
  the app says so and links to the free Microsoft download rather than failing silently.
- **To build**: .NET 8 SDK.

---

## Building

```powershell
cd desktop-client
dotnet publish -c Release -o publish
```

Output: `publish/AppleEsports.exe` — one self-contained file, roughly 63 MB.
Only the `.exe` needs to be distributed; the `.xml` files alongside it are
IntelliSense documentation and are not used at runtime.

### Regenerating the icon

The icon is committed as `appicon.ico` (7 sizes: 16/24/32/48/64/128/256, generated
from `client/public/logo.png`). It only needs regenerating if the logo changes.
The generator script lives outside the repo; the key detail if you rewrite it is that
PowerShell unrolls arrays on `return`, so byte buffers must be returned as `return ,$bytes`
or the `.ico` comes out as a header with no image data.

---

## Configuration

Settings resolve in this order, later winning:

1. Built-in defaults (`AppConfig.cs`)
2. `AppleEsports.config.json` next to the `.exe` — the **deployment default**, so each
   branch can ship a copy already pointed at the right server
3. `%APPDATA%\AppleEsports\config.json` — per-machine override, written when someone
   uses the in-app Settings dialog

```json
{
  "ServerUrl": "http://140.245.195.222:8081",
  "GateUsername": "admin",
  "GatePassword": "Admin@123",
  "Kiosk": false
}
```

| Key | Meaning |
|---|---|
| `ServerUrl` | Server this PC talks to. Scheme optional — `140.245.195.222:8081` works. |
| `GateUsername` / `GatePassword` | Credentials for the **nginx Basic Auth gate** in front of the dashboard. Leave blank to let the browser prompt instead. This is *not* the app login. |
| `Kiosk` | Borderless, always maximised — for gaming PCs. |

### Security note on `GatePassword`

These credentials sit in plain text next to the `.exe`, so anyone with the file has
them. The nginx gate is a coarse "keep strangers off the URL" measure, not the real
access control — that is the application login, whose tokens are HTTP-only cookies.
Two things worth knowing:

- Over plain `http://`, Basic Auth credentials go over the wire base64-encoded on
  every request, which is trivially reversible. Moving the server to HTTPS fixes this.
- If the gate password is ever changed on the server, every deployed config file
  needs updating, or clients will hit the browser's own password prompt.

---

## Keyboard shortcuts

| Key | Action |
|---|---|
| `F5` | Reload |
| `F11` | Toggle full screen (`Esc` also exits) |
| `Ctrl` + `Shift` + `S` | Change server address / gate credentials |

---

## Troubleshooting

**Nothing happens on double-click** — check `%APPDATA%\AppleEsports\crash.log`.
Every unhandled failure is written there and also shown in a dialog.

**"Can't reach …"** — the PC cannot see the server. Verify from the same PC:

```powershell
Invoke-WebRequest http://<server>:8081/ -UseBasicParsing
```

A `401` response is success here: it means the server answered and the password gate
is working. A timeout or connection error means a network or firewall problem.

**A username/password box appears** — the gate credentials in the config are missing
or wrong for this server.

**Reset the app completely** — delete `%APPDATA%\AppleEsports` and
`%LOCALAPPDATA%\AppleEsports`. The latter holds the WebView2 browser profile
(cookies, cache); removing it signs the user out.
