# Apple Esports — Keyboard Shortcuts

Print this and keep it at the counter.

---

## The short version

| Keys | What it does |
|---|---|
| **F5** | Reload the screen |
| **F11** | Full screen on / off |
| **Ctrl + Shift + S** | Change which server this PC talks to 🔒 |
| **Ctrl + Shift + P** | Change which PC this machine is 🔒 |
| **Ctrl + Shift + U** | Remove this machine's setup 🔒 |
| **Ctrl + Alt + Q** | Close the app on a locked gaming PC 🔒 |

🔒 = asks for the **admin PIN** first.

---

## Two kinds of PC

A machine is set up as one or the other. It changes what staff and customers can do.

### Operator PC — the counter

The normal dashboard. Staff can minimise it, close it, use it like any other program.

### Customer gaming PC — locked down

Deliberately sealed so a customer cannot get out to Windows:

- **No X button** in the corner
- **No minimise button**
- **Alt + F4 does nothing**
- Not shown in the taskbar
- No full screen toggle — it is always full screen

The **only** way out is `Ctrl + Alt + Q` with the admin PIN.

> **Why so strict?** If a customer can close the app, they are sitting at your Windows
> desktop with your files and your browser. The lock is the whole point of a gaming PC.

---

## Each shortcut in detail

### F5 — Reload
Reloads the current screen. Use it if something looks stuck or out of date. Safe at any
time. It does **not** log anyone out and does **not** stop a running session.

### F11 — Full screen
Toggles the window border on and off. `Esc` also exits full screen.

Does nothing on a customer gaming PC — that is already permanently full screen.

### Ctrl + Shift + S — Change the server 🔒
Points this machine at a different Apple Esports server. You would use this when moving
a branch from the test server to the owner's real server.

Asks for the admin PIN, then reconnects straight away.

### Ctrl + Shift + P — Change which PC this machine is 🔒
Sets the PC number (for example `PC-1`) and whether it is an operator PC or a customer
gaming PC.

Asks for the admin PIN, then restarts the app.

> A PC number can only be claimed by **one** machine. If you try to set a second machine
> up as `PC-1`, it is refused — two machines both answering as `PC-1` would send the
> unlock command to the wrong screen. To move `PC-1` to a replacement machine, remove the
> old setup from the dashboard first.

### Ctrl + Shift + U — Remove this machine's setup 🔒
Un-configures the machine. It stops being assigned to a PC number and asks to be set up
again next time it starts.

Asks for the admin PIN, then confirms before doing anything.

> **Stop any running session first.** The seat disappears from the dashboard.

### Ctrl + Alt + Q — Close a locked PC 🔒
The escape hatch for a customer gaming PC, where the normal close button is gone.

Asks for the admin PIN. On an operator PC just close the window normally.

---

## The admin PIN

Set in `AppleEsports.config.json` next to the program, as `AdminPin`.

**On a customer gaming PC, if no PIN is set, all four protected shortcuts refuse to
work at all.** That is deliberate — an unprotected escape hatch on a machine the public
can touch is worse than having no shortcut, because any customer who discovers the key
combination could unbind the PC or reach your desktop.

On an operator PC behind the counter, no PIN means the shortcuts simply work.

---

## Troubleshooting

**A shortcut does nothing on a gaming PC**
No admin PIN is set. Add one to `AppleEsports.config.json`, or ask a Super Admin.

**"This machine is already set up as PC-3"**
This machine already holds a different seat. Remove that setup first
(`Ctrl + Shift + U`), then set it up again as the PC you want.

**"PC-1 is already set up on another machine"**
Another machine holds that number. Either pick a different one, or free `PC-1` from the
dashboard first — Super Admin only.

**A PC shows "Awaiting setup" and cannot take a customer**
That is correct and intended. The PC record exists but no machine has claimed it yet.
Run setup on the machine (`Ctrl + Shift + P`). Until then it is not bookable, so nobody
can be seated at a screen that will not unlock.

**Forgotten the admin PIN**
Edit `AppleEsports.config.json` on that machine, or reinstall. There is no back door —
that is the point.

---

## For developers

The shortcuts live in `ProcessCmdKey` in
[`desktop-client/MainForm.cs`](desktop-client/MainForm.cs). **Keep this file in step with
that code** — this page is what branch staff are handed.
