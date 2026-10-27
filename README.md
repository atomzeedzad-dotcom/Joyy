# Joyy

Makes a PS5 DualSense work in old XInput-only games: the game sees exactly one
Xbox 360 controller, with buttons mapped by position (Cross = A, Circle = B,
Square = X, Triangle = Y, R1 = RB, R2 = RT), and the real DualSense is hidden.

Full specification: [PROMPT.md](PROMPT.md).

## Status

| Milestone | State |
|---|---|
| M1 Core mapping + unit tests | Done |
| M2 Read DualSense (SDL2) | Done (USB verified; Bluetooth not yet tried) |
| M3 Virtual Xbox 360 pad (ViGEm) | Done (`joyy-cli selftest` passes end to end) |
| M4 Rumble | Done in software; felt-on-the-controller check pending |
| M5 Hide the DualSense (HidHide) | Done; verified against HidHide 1.5.230 (see below) |
| M6 Tray app, settings, profiles | Done (restore-visibility item waits for M5) |
| M7 Packaging | Not started |

## Building (developers)

Requires the .NET 8 SDK and, at runtime, the ViGEmBus driver.

```powershell
.\tools\test.ps1
```

## Install

`tools\publish.ps1` builds `artifacts\publish\Joyy.exe`: one self-contained file (no .NET
install needed). Double-click it and it offers to install itself:

- copies Joyy to `%LOCALAPPDATA%\Programs\Joyy` (no administrator rights),
- adds **Joyy** to the Start menu, so typing "Joyy" in Windows search finds it,
- adds an entry in Windows Settings → Apps (uninstall from there, or `Joyy.exe --uninstall`),
- asks separately whether to **start with Windows** (recommended: your DualSense only works
  as an Xbox controller while Joyy runs; it is never switched on without asking).

Command line: `Joyy.exe --install [--quiet] [--autostart | --no-autostart] [--no-launch]`,
`--uninstall [--quiet]`, `--quit` (exit a running Joyy cleanly), `--restore` (emergency: make
the DualSense visible again).

### The controller never stays hidden

While Joyy hides the DualSense it also runs a tiny `--guardian` helper. If Joyy is killed
(crash, Task Manager "End task"), the guardian gives the DualSense back within about a second;
a restart of Joyy and `Joyy.exe --restore` also fix it. Only the process that hid the
controller can be restored by its guardian, so a newer Joyy is never undone by an old one.

## Using Joyy

Open **Joyy** from the Start menu (or run `src\Joyy.App\bin\Release\net8.0-windows\win-x64\Joyy.exe` after building).

A black window with a slim top bar: the Joyy logo, the bridge status ("Ready to
play · Xbox 360 slot 1"), the profile in use (click it for Profiles) and a power
button that pauses/resumes the virtual controller.

- **Home**: your DualSense large in the middle, live (pressed buttons glow, L2/R2
  bars above). Underneath, what the last button press sends to the game
  ("Cross → A"), then six tiles that show each feature's state and open its page.
  Every other page opens in the same space with a back button (or press Esc).
- **My Controller**: device information (type, connection, VID/PID, serial,
  polling rate, virtual slot) and health checks for the drivers.
- **Profiles**: "All games" plus one profile per game, used automatically while
  that game's window is in front.
- **Button Mapping**: switch the picture between PHYSICAL (DualSense) and VIRTUAL
  (Xbox); click a button on the picture, or press it on the controller, then pick
  what it sends. Nintendo layout toggle.
- **Sticks & Triggers**: live stick circles with deadzone/full-tilt rings, trigger
  meters, sliders, inversion, rumble strength and a test buzz.
- **Hide DualSense**, **Settings** (startup, controller pictures, log, files, about).
- Edits stay pending until **Save**; **Revert** undoes them.
- Closing the window keeps Joyy running in the tray; use the tray menu's **Exit** to quit.
- Settings live in `%APPDATA%\Joyy\config.json` (hand edits are picked up live);
  logs in `%LOCALAPPDATA%\Joyy\logs`.

## Developer CLI (`joyy-cli`)

| Command | What it does |
|---|---|
| `view` | Live DualSense input next to the Xbox 360 report it maps to |
| `run` | Bridge the DualSense to a virtual Xbox 360 controller until Ctrl+C (`--trace` prints input/output) |
| `selftest` | Scripted input through the real ViGEmBus driver, read back via XInput; checks every button, trigger, stick and rumble |
| `xinput` | List controllers exactly as an XInput game sees them (`--rumble` pulses the first) |

## Hiding the DualSense (HidHide)

With [HidHide](https://github.com/nefarius/HidHide/releases) installed, Joyy hides the
physical DualSense from every program except itself, so games see only the Xbox 360
controller. It turns hiding off when you Pause or Exit.

- **First time after installing HidHide:** unplug the DualSense and plug it back in once
  (or restart Windows). HidHide only attaches to devices that are plugged in after it is
  installed; Joyy checks the driver stack and tells you when this is needed.
- Start Joyy **before** the game. A game that is already open keeps seeing the controller
  it found at startup.
- **Safety:** every change is written to `%LOCALAPPDATA%\Joyy\hidden-devices.json` before it
  is made, and only those changes are undone (your own HidHide settings are left alone).
  If Joyy is killed, the next start restores first. Emergency fix at any time:
  `Joyy.exe --restore` (or `joyy-cli restore`).

HidHideCLI flags used, verified with `HidHideCLI --help` of version 1.5.230:
`--cloak-on`, `--cloak-off`, `--cloak-state`, `--dev-hide`, `--dev-unhide`, `--dev-list`,
`--dev-gaming`, `--app-reg`, `--app-unreg`, `--app-list`. On this PC they worked without
administrator rights.

## Known driver quirk

A freshly plugged ViGEm Xbox 360 pad reports uninitialized stick values to XInput until
it receives a report that differs from all-zero. `ViGEmPad.Connect` sends one
imperceptible priming report to avoid phantom stick drift. `selftest` checks this
("Fresh pad at rest").

## Layout

- `src/Joyy.Core` — pure mapping logic (no hardware, no UI).
- `src/Joyy.Platform` — SDL2 input, ViGEm output, the `Bridge` loop.
- `src/Joyy.App` — the tray app (WinForms).
- `src/Joyy.Cli` — developer/diagnostic console.
- `tests/` — xUnit tests (Core logic, Bridge against fakes).
