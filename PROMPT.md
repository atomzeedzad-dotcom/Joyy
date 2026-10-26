# Engineering Prompt: DualSense → Virtual Xbox 360 Bridge ("Joyy")

You are a senior Windows systems/gamepad-input engineer. Build the software specified below end to end: design, implementation, tests, build scripts and a README. Work incrementally, verify each milestone, and report anything you could not verify on real hardware.

---

## 1. Problem statement

The user plays old PC games that only understand XInput (Xbox 360 controller). They own a PS5 DualSense controller. Existing tools fail them in two ways:

1. **Duplicate controllers.** Tools like DS4Windows/Steam Input create a virtual Xbox pad, but the physical DualSense stays visible to the game, so the game sees two controllers.
2. **Swapped buttons.** When a game reads the DualSense directly (DirectInput / raw HID), the button indices do not match the Xbox layout: A/B, X/Y and R1/R2 end up swapped.

**Goal:** the game must see exactly ONE controller, a virtual Xbox 360 pad, whose buttons are mapped by physical position (Cross = A, Circle = B, Square = X, Triangle = Y, L1 = LB, R1 = RB, L2 = LT, R2 = RT), and the physical DualSense must be hidden from every other application.

## 2. Target environment

- Windows 11 x64 (Windows 10 22H2 should also work).
- DualSense (VID 054C, PID 0CE6) and DualSense Edge (PID 0DF2), over USB and Bluetooth.
- The user is not a developer. The tool must be easy to run: one click to start, a tray icon, clear status.

## 3. Recommended stack (deviate only with a written justification)

| Concern | Choice | Reason |
|---|---|---|
| Language/runtime | C# / .NET 8, `win-x64`, self-contained single-file publish | Good Windows interop, easy tray/GUI, no runtime install for the user |
| UI | WinForms or WPF tray app (`NotifyIcon`) + a small settings window | Lightweight |
| Reading the DualSense | SDL2 GameController API (SDL2-CS or equivalent binding) with `SDL_HINT_JOYSTICK_HIDAPI_PS5=1` | Handles USB and BT reports, hot-plug, and normalizes the layout so the swap problem cannot occur by construction |
| Virtual pad | ViGEmBus via `Nefarius.ViGEm.Client` (Xbox 360 target) | De facto standard; games see a genuine XInput device |
| Hiding the physical pad | HidHide (Nefarius) driven through `HidHideCLI.exe` | Removes the duplicate-controller problem |
| Config | JSON in `%APPDATA%\Joyy\config.json` | Human-editable, versionable |
| Tests | xUnit for the mapping/state logic, hardware-free | CI-friendly |

Note: ViGEmBus is no longer actively maintained upstream but still works on Windows 10/11. Isolate it behind an `IVirtualPad` interface so the backend can be swapped later.

## 4. Architecture

```
Joyy/
  src/
    Joyy.Core/            # no UI, no P/Invoke, fully unit-testable
      PadState.cs         # immutable struct: buttons bitmask, 2 sticks (short), 2 triggers (byte), dpad
      ButtonMap.cs        # physical-button -> X360 button table (data-driven)
      Profile.cs          # deadzones, trigger curve, remap overrides, rumble scale
      Mapper.cs           # PadState(DualSense) -> X360Report
    Joyy.Platform/
      IPhysicalPad.cs     # abstraction over SDL2
      SdlPad.cs
      IVirtualPad.cs      # abstraction over ViGEm
      ViGEmPad.cs
      HidHideService.cs   # wraps HidHideCLI.exe
      DependencyChecker.cs
    Joyy.App/             # tray app, settings window, logging, startup wiring
  tests/Joyy.Core.Tests/
  tools/                  # build + publish scripts
  README.md
```

Rules: `Joyy.Core` must not reference SDL, ViGEm or WinForms. All hardware access goes through interfaces so the mapper and the state machine can be tested with fakes.

## 5. Functional requirements

### 5.1 Input
- Enumerate and open DualSense/Edge controllers. Support hot-plug (connect, disconnect, USB↔BT change) without restarting the app.
- Poll at ≥ 250 Hz, or event-driven with a ≤ 4 ms worst-case latency target. The loop must not allocate per frame (no GC spikes).
- Set `SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1` so input continues while a game has focus.
- Read: face buttons, shoulders, analog triggers (0–255), sticks (−32768..32767), stick clicks, D-pad, Options, Create/Share, PS button, touchpad click, Mute.

### 5.2 Default mapping (position-based, the core fix)

| DualSense | Virtual X360 |
|---|---|
| Cross | A |
| Circle | B |
| Square | X |
| Triangle | Y |
| L1 / R1 | LB / RB |
| L2 / R2 (analog) | LT / RT (analog, 0–255) |
| L3 / R3 | LS / RS |
| Options | Start |
| Create (Share) | Back |
| PS button | Guide |
| Touchpad click | Back (configurable) |
| Mute | unmapped (configurable) |
| D-pad | D-pad |

Also support a user-selectable "Nintendo layout" toggle (swap A/B and X/Y), since some users want it.

### 5.3 Output
- Create one virtual Xbox 360 pad per active physical pad (the first release may support exactly one and must say so).
- Submit the full report each frame, only when it changed or every N ms as a keep-alive.
- **Rumble passthrough:** subscribe to the ViGEm feedback event (large/small motor) and forward it to the DualSense through SDL rumble. Apply a configurable scale.

### 5.4 Device hiding (HidHide)
On start:
1. Verify HidHide and ViGEmBus are installed; if not, show a clear message with download links and stop. Do not install drivers silently.
2. Resolve the DualSense device instance path(s) for HidHide (the HID device, including the BT/USB variants).
3. Register our own executable in the HidHide **application whitelist**, otherwise we will not be able to read the hidden device either.
4. Hide the device and enable the cloak.

On exit (normal, crash, Ctrl+C, logoff, shutdown): **un-hide the device and restore the previous HidHide state.** Never leave the user's controller invisible. Implement all of these:
- A `finally`/`ProcessExit`/`SessionEnding` handler.
- A persisted "hidden devices" journal file so that, if the app was killed, the next launch (or a `--restore` CLI command) can undo it.
- A prominent "Restore controller visibility" button.

HidHideCLI needs administrator rights. Request elevation through the application manifest (`requireAdministrator`) or a clean relaunch with UAC. Do not assume the exact CLI flags: run `HidHideCLI.exe --help` of the installed version, record the verified flags in the README, and fail clearly if they differ.

### 5.5 Profiles
- Global profile plus optional per-game profiles, selected by foreground process name.
- Per-profile: stick deadzone (radial, default 8%), outer saturation, trigger threshold/curve, remap overrides, invert axes, rumble scale, Nintendo-layout toggle.
- Hot reload of the config file without restarting.

### 5.6 UI
- Tray icon states: no controller / connected / error. Right-click menu: status, profile, restore visibility, open logs, exit.
- Settings window: live input viewer (shows physical input next to the virtual output so mapping can be verified visually), profile editor, dependency status.
- Start with Windows (optional, off by default).

## 6. Non-functional requirements

- **Safety first:** the controller must always be recoverable (see 5.4). Treat this as the highest-priority invariant and test it.
- **No telemetry**, no network access except an optional, user-triggered link to dependency download pages.
- **Performance:** idle CPU < 1%, memory < 80 MB, added input latency < 5 ms over the SDL poll.
- **Robustness:** survive driver restarts, sleep/resume, controller removal during use, and ViGEm client failure with a bounded retry (exponential backoff), without crashing.
- **Logging:** rolling file log (Serilog or equivalent) with levels; log every HidHide action with the exact command and exit code.
- **Security:** the elevated process must not execute anything from user-writable paths; locate HidHideCLI from the registry or the standard install directory and verify it exists.
- **Compatibility with other tools:** detect if DS4Windows, Steam (Steam Input) or DualSenseX is running and warn that it may cause duplicate controllers.

## 7. Edge cases that must be handled and tested

1. Two DualSense controllers connected: either support them or clearly pick the first one and warn.
2. The controller connects over BT and USB at the same time: de-duplicate by serial.
3. The app crashes while the controller is hidden: the next launch restores it.
4. HidHide installed but the cloak is off or the whitelist is missing: self-repair.
5. A game that enumerates devices before our virtual pad exists: document the recommended start order and provide an option "Start with Windows".
6. Trigger idle drift and stick drift: the deadzone must be applied before conversion to 16-bit.
7. D-pad hat value to bitmask conversion, including diagonals.
8. The PS button is intercepted by Windows/Steam overlays: document it.
9. Sleep/resume: reopen the device and recreate the virtual pad if needed.
10. Running without admin rights: show a clear error instead of a silent failure.

## 8. Testing and acceptance

**Automated (hardware-free):**
- Mapper table tests: every row in 5.2 is verified.
- Deadzone/curve tests with edge values (0, ±1, max, min, diagonals).
- Hat to bitmask tests for all 9 states.
- Journal/restore logic tested against a fake `HidHideService`.

**Manual acceptance checklist (document each result):**
- [ ] `joy.cpl` (Game Controllers) lists exactly one device, an Xbox 360 controller, while the app runs.
- [ ] The Cross button lights A, Circle lights B, Square lights X, Triangle lights Y in the Windows test panel.
- [ ] R1 → RB, R2 → RT (analog sweep is smooth), L1 → LB, L2 → LT.
- [ ] Both sticks reach full range and return to a stable center.
- [ ] Rumble works from a game or XInput test tool.
- [ ] Closing the app makes the DualSense visible again; killing the process and relaunching restores it.
- [ ] Unplug and replug the controller, and BT↔USB switching, without restarting.
- [ ] At least two old XInput games play correctly with the original button layout.

## 9. Milestones (stop and report after each)

1. **M1: Skeleton + Core.** Solution layout, `PadState`, `ButtonMap`, `Mapper`, unit tests green.
2. **M2: Read.** SDL2 reader plus a console viewer printing live physical input.
3. **M3: Write.** ViGEm X360 target; verify in the Windows test panel with the mapping above.
4. **M4: Rumble.** Feedback passthrough.
5. **M5: HidHide.** Dependency check, whitelist, hide/unhide, journal, crash recovery.
6. **M6: Tray app + settings + profiles.**
7. **M7: Packaging.** Self-contained publish, README (install ViGEmBus + HidHide, usage, troubleshooting, restore procedure).

## 10. Deliverables

- Source code with the structure in section 4.
- `README.md` with prerequisites, verified HidHideCLI flags, usage, troubleshooting and an uninstall/restore section.
- A published single-file `Joyy.exe` plus the build script.
- A short `ARCHITECTURE.md` with a data-flow diagram: DualSense → SDL → Mapper → ViGEm X360 → Game.

## 11. Working rules

- Before writing code, restate your plan and list the assumptions you cannot verify (e.g. exact HidHideCLI flags, SDL binding version).
- Prefer small, reviewable commits and keep every milestone runnable.
- Do not claim hardware behavior works unless it was actually run; mark unverified items clearly.
- Do not add features outside this spec (no gyro/touchpad mouse emulation, no DualSense adaptive-trigger or lightbar control) without asking.
- If a requirement conflicts with reality (driver behavior, API limits), stop and ask instead of silently changing the design.
