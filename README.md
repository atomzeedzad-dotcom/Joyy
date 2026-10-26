# Joyy

Makes a PS5 DualSense work in old XInput-only games: the game sees exactly one
Xbox 360 controller, with buttons mapped by position (Cross = A, Circle = B,
Square = X, Triangle = Y, R1 = RB, R2 = RT), and the real DualSense is hidden.

Full specification: [PROMPT.md](PROMPT.md).

## Status

| Milestone | State |
|---|---|
| M1 Core mapping + unit tests | Code written, waiting for first test run |
| M2 Read DualSense (SDL2) | Not started |
| M3 Virtual Xbox 360 pad (ViGEm) | Not started |
| M4 Rumble | Not started |
| M5 Hide the DualSense (HidHide) | Not started |
| M6 Tray app, settings, profiles | Not started |
| M7 Packaging | Not started |

## Building (developers)

Requires the .NET 8 SDK.

```powershell
.\tools\test.ps1
```

## Layout

- `src/Joyy.Core` — pure mapping logic (no hardware, no UI).
- `tests/Joyy.Core.Tests` — xUnit tests for the mapping, deadzones, triggers and D-pad.
