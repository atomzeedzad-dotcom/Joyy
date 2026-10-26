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
| M5 Hide the DualSense (HidHide) | Blocked: HidHide driver not installed |
| M6 Tray app, settings, profiles | Done (restore-visibility item waits for M5) |
| M7 Packaging | Not started |

## Building (developers)

Requires the .NET 8 SDK and, at runtime, the ViGEmBus driver.

```powershell
.\tools\test.ps1
```

## Using Joyy

Run `src\Joyy.App\bin\Release\net8.0-windows\win-x64\Joyy.exe` (after building).

- **Overview**: status, plus live pictures of your DualSense and of the Xbox 360
  controller games see. Press a button and watch both light up.
- **Profiles**: deadzones, trigger curve, rumble strength, stick inversion,
  Nintendo layout and per-button remapping. "Add game…" makes a profile that is
  used automatically while that game's window is in front.
- **Log**: what happened, with buttons to open the log folder and `config.json`.
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
