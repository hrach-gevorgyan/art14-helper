# Architecture

How Art14 Helper is built, for anyone reading or changing the code.

## Goals

- **One small exe, nothing else.** NativeAOT, no .NET runtime to install, no services,
  no drivers, no WinForms or WPF.
- **No work while idle.** No polling timers: the app waits for Windows to tell it
  something changed (power broadcasts, device arrival, display changes, BIOS and sensor
  events) and runs in Windows 11's efficiency mode while no window is open.
- **The hardware, directly.** BIOS through WMI, the touchpad through HID, the display
  through the Intel driver's library, everything else through documented Windows APIs.

## Folders

The app's code is in `src/Art14Helper/` (with `Art14Helper.csproj`, `Program.cs` and
`app.manifest`); the folders below are inside it, except `tests/` at the top.

| Folder | What's in it |
|---|---|
| `App/` | The running app: `TrayApp` (split by concern into `.Menu`, `.Keys`, `.Power`, `.Icon`), settings storage, popups, status texts, diagnostics, backup, first-run defaults, log |
| `Features/` | One file per feature: charge limit, keyboard light and Auto, Fn lock, F10/F11, microphone, camera, sound, touchscreen, keep awake and remote mode, power mode, battery report; `Touchpad/` for the HID touchpad |
| `Display/` | Refresh rate, color modes (color math, the Intel 3D LUT pipeline, panel colorimetry) |
| `Drivers/` | The Drivers page's logic: device inventory, HONOR's update catalogs, download checks |
| `Hardware/` | The BIOS call (`Bios`), sensor reads, and `Privileged` actions that need admin rights |
| `Maintenance/` | Install and uninstall, the autostart task, driver backup, running system tools |
| `Native/` | Thin wrappers over Windows APIs (`LibraryImport` only): WMI COM, HID, CfgMgr32, battery IOCTLs, power settings, clipboard, GDI, user32 |
| `Ui/` | The app's own Win32 UI layer: the UI thread, windows, painted surfaces and parts, theme, tray icon, layered popup window |
| `Settings/` | The settings window, its pages and parts, the setup window |
| `Assets/` | The app icon |
| `tests/` | xUnit v3 tests of the logic that needs no hardware |

## One exe, several jobs

`Program.cs` picks the job from the command line:

| Arguments | Job |
|---|---|
| none | The tray app. If this isn't the installed, elevated copy, it installs, updates or hands over to the running one first |
| `--set-<name> <value>` | One hardware change with admin rights, started through UAC by a tray app running without them |
| `--install`, `--uninstall` | Setup, with its own window |
| `--self-test` | Checks WMI, devices, drawing, a window and memory; used by the build |
| `--backup-drivers`, `--restore-drivers` | The elevated part of the Drivers page's backup |

The installed copy lives in `C:\Program Files\Art14Helper` and starts at sign-in through
a scheduled task with highest privileges, so it runs elevated without a prompt. Only
administrators can write to Program Files, so no other program can swap the exe that
Windows starts elevated.

## Threads

- **The UI thread** (`Ui/UiThread`) owns every window, the message loop, timers and posted
  work. All UI state is touched only there; other threads hand work over with `UiThread.Post`.
- **The WMI event thread** delivers BIOS key events (`OemWMIEvent`). Handlers move anything
  slow to the thread pool.
- **Touchpad reader threads** wait on the HID event collection.
- **The thread pool** does BIOS calls, WMI queries, color pipeline loads and driver work.
  COM objects live in the multithreaded apartment; work started on the UI thread (an STA)
  goes through `Native/Mta`, which runs it on a pool thread without pumping messages.

## Hardware access

- **BIOS:** `Hardware/Bios` builds the 64-byte command, calls `OemWMIfun` and checks the
  status byte. One call at a time (a lock); the WMI connection is shared and reconnects if
  the WMI service restarts. See [HARDWARE.md](HARDWARE.md) for every command.
- **Privileged actions:** `Hardware/Privileged` wraps each change that needs admin rights.
  Elevated, it runs in the process; otherwise it starts the exe with `--set-…` through UAC,
  only when the `AdminPrompt` policy allows (never for background work like wake restores).
- **Stored settings** (`App/AppSettings`, `HKCU\Software\Art14Helper`) record what the user
  chose and what the hardware last reported, so the app can put choices back after a
  restart, sleep or driver reset.

## User interface

- **No controls.** Windows are painted (`Ui/PaintedWindow`, `Surface`, `Part`): shapes with
  GDI+ and text with GDI (ClearType), double-buffered, so nothing flickers. Light and dark
  follow Windows live; keyboard navigation (Tab, Enter, Space, Esc) works everywhere.
- **One menu model, two views.** `TrayApp.BuildMenu` describes every choice once through
  `IMenuBuilder`. The tray menu (`App/TrayMenu`, a Win32 popup menu) and the settings window
  (`Settings/SettingsPages`) are both built from it, so they never disagree.
- **Popups** (`App/Popup`) are a layered window in Windows 11's flyout style. They appear
  only for what the user didn't just do and can't see otherwise: keys and gestures,
  automatic changes, choices that didn't apply, results like "copied". One at a time.

## Startup

1. The tray icon appears at once.
2. The BIOS key listener starts first (the keys are what the user notices).
3. Three independent groups run side by side: the touchpad, then first-run defaults with
   the Fn mode, mic LED and Auto keyboard light, then sound, refresh rate and color mode.
4. After 30 s the app trims its memory and enters efficiency mode.

Each step logs and continues on failure, so one broken part never takes the others down.

## Build and test

- `build.ps1` publishes NativeAOT (it needs the Visual Studio C++ build tools). Without them
  it builds a trimmed single file instead, which works the same. `-SelfTest` runs the
  exe's `--self-test`.
- The GitHub workflow (`.github/workflows/build.yml`) runs the tests, builds with
  `-RequireAot -SelfTest`, and publishes the exe with its SHA-256.
- `tests/` covers the logic that needs no hardware: color math, charge and battery texts,
  driver catalog parsing, backup format, popup layout, BIOS event decoding and more.
  Run them with `dotnet test --project tests/Art14Helper.Tests/Art14Helper.Tests.csproj`.
