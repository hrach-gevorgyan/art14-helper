# Art14 Helper

**A small, fast replacement for HONOR PC Manager on the HONOR MagicBook Art 14 (2024).**
Charge limit, keyboard light, Fn lock, touchpad, sound, refresh rate, calibrated color
modes, keep awake, function-key popups and driver updates, in one 12 MB exe that does
nothing while idle.

![Popups in dark and light mode](docs/images/popups.png)

> **Unofficial.** Not made, endorsed or supported by HONOR. HONOR and MagicBook are
> trademarks of their owner and are used here only to say which laptop this works with.
> Use at your own risk.

## Why

HONOR PC Manager wraps a handful of useful switches in a heavy bundle of services,
background processes and ads. Art14 Helper keeps the switches and drops the rest: one exe,
no services, no telemetry, no timers while idle. It talks to the laptop's BIOS, touchpad
and graphics driver directly. It was inspired by open-source projects on GitHub that
showed these laptops can be controlled without HONOR's software.

## Features

**Battery**
- Charge limit: Home 40–70 %, Office 70–90 %, Travel 95–100 %, or off.
- Charge to 100 % once, for a trip: the limit comes back by itself when you unplug.
- Battery health against the design capacity, cycle count, and Windows' battery report.

**Keyboard and keys**
- Keyboard light Off, Dim, Bright or **Auto**, which follows the room's light through the
  light sensor (smooth, with hysteresis, at no extra battery cost), plus the idle timeout.
- Fn lock: standard F-keys or media keys.
- F7 mutes the microphone for every app and lights the key; F8 is a camera privacy
  switch that works even during a call; F12 opens the screen snip.
- F10 and F11 open an app of your choice (in media key mode).

**Touchpad**
- Vibration strength, press sensitivity.
- Slide along the left edge for brightness, the right edge for volume.
- Knock twice with a knuckle for a screenshot, saved to `Pictures\Screenshots` and copied
  to the clipboard.

**Display**
- Refresh rate 120 Hz, 60 Hz, or Auto (fastest on the charger, 60 Hz on battery).
- Calibrated color modes: sRGB, Display P3, Adobe RGB or the panel's native gamut, built
  from the panel's own factory measurement and loaded into the Intel graphics driver, with
  matching color profiles for color-managed apps. Native or D65 white.
- Touchscreen on or off.

**Power and keep awake**
- Windows' power mode (best efficiency, balanced, best performance) one click away.
- Keep awake for 30 min to 4 h or until turned off, like Caffeine, with optional "keep the
  screen on" and "stop apps showing you as away". It carries on after an app update or
  restart, and ends on time even if the laptop slept in between.
- Remote mode for work that runs while you're away: stays awake, turns the screen off and
  keeps running with the lid closed. Separate switches for the lid.

**Maintenance**
- BIOS and driver updates from HONOR's own catalog, with every download checked.
- Driver backup and restore, for a clean Windows reinstall.
- Settings backup and restore, and one-click diagnostics for a bug report.

**Sound**
- Spatial or stereo speaker processing.

## The app

**Left-click** the tray icon for the settings window: a Windows 11-style window that is
light or dark like Windows, with a Home page (battery, charge limit, temperatures, fans,
battery health, quick settings) and one page per feature. It works with the mouse or the
keyboard (Tab, Enter, Space, Esc). Options that don't apply right now are grayed out with
the reason. **Right-click** the icon for the same choices as a quick menu. Hover over it
for temperatures, fan speeds and battery power.

**Popups** look and move like Windows 11's own volume flyout, and appear only for what you
didn't just do yourself and can't see otherwise:

- a key or gesture: F7, F8, Fn+Space (with a level bar), the touchpad key, the knuckle screenshot;
- something that changed by itself: the charger plugged in (charge level, time to the limit
  or why it isn't charging), Auto switching the refresh rate, keep awake running out;
- a choice that didn't apply, with the reason (for example "Turn off HDR first");
- a result you can't see: diagnostics copied, settings restored.

Choices you make in the menu or the settings window show no popup: you can see them there.

## Requirements

- HONOR MagicBook Art 14 (2024) with Intel Core Ultra, Windows 11. Other HONOR laptops may
  work in parts; the app only offers what it finds.
- Administrator rights once, to install. After that it starts at sign-in without prompts.

## Install, update, uninstall

**Install:** run `Art14Helper.exe`. The setup window offers **Start with Windows** and
**Apply recommended settings**, then installs to `C:\Program Files\Art14Helper` after one
UAC prompt and adds a Start menu entry and an entry in Settings > Apps.

**Update:** run the new exe and click **Update**. The running copy closes cleanly, is
replaced and starts again; your settings stay.

**Uninstall:** Settings > Apps > Installed apps > Art14 Helper, or System > Uninstall in the
app. It removes the program, its autostart task, Start menu entry and log, puts the
screen back to native colors with HONOR's own profile, and undoes the lid settings the
app changed. You can keep your settings for a reinstall, and choose to turn the charge
limit off; other BIOS settings stay as they are.

The app needs admin rights for the BIOS, so it lives in Program Files, where only
administrators can write: no other program can swap the exe that Windows starts elevated.

### Why the exe isn't signed

Windows will call it an "unknown publisher", in SmartScreen and in the UAC prompt. That's
because the exe has no code-signing certificate, and it never will. Certificates cost money
every year, paid to a certificate company, and this free, open-source project will never
spend money on one. A signature only says who built a file; it says nothing about what the
file does.

The source is the guarantee here. If you don't want to trust a downloaded exe, don't: read
the code and [build it yourself](#build). It takes one command, and that's what open
source is for.

## Defaults

With **Apply recommended settings** on (the default), the first start sets: charge limit
Home, keyboard light Auto with a 30 s timeout, standard F-keys, F10/F11 nothing, touchpad
soft vibration and less sensitive press, both edge gestures and the knuckle screenshot on,
spatial sound, refresh rate Auto, Display P3 with native white, and start with Windows. Keep
awake and its options are off. After that, your choices are kept.

With it off, the app changes nothing until you choose something.

## Privacy and safety

- No telemetry, no accounts, no ads, no self-updater.
- The only network use is the Drivers page asking HONOR's update server for new versions:
  laptop model, BIOS and driver versions, Windows version and display language. No serial
  number, no personal data.
- Downloads are checked by HTTPS, SHA-256 and the digital signature of every program file.
- Settings stay in `HKCU\Software\Art14Helper`, the log in `C:\Program Files\Art14Helper\Logs`.
- The app ships no HONOR files; its icons come from the Windows icon font.

## Size and speed

One native exe (NativeAOT, about 12 MB), no .NET runtime to install, no services, about
11 MB of private memory. The app waits for Windows' own notifications (keys, power,
display, devices, light sensor) instead of polling, and runs in Windows 11's efficiency
mode while no window is open.

## FAQ

**The screen blinks when I plug in or unplug.** With the refresh rate on Auto, the app
switches between 120 and 60 Hz. This panel can only do that with a full display mode
change, which blanks the screen for a moment (its graphics driver has no seamless low
refresh rate). The popup tells you when it happened. Pick a fixed rate to avoid it.

**Tapping Fn changes the keys, but the app shows the old setting.** The firmware switches
the Fn lock on a tap without telling Windows, so no app can follow it. Choosing in the
app is always exact.

**I see two popups for one key.** HONOR's own popup service is still running. Disable it:
`Set-Service OSDServer -StartupType Disabled`.

**Color modes do nothing.** They need HDR off. The chosen mode comes back by itself when
HDR is turned off.

**Battery health shows 100%+.** A new battery usually holds a little more than its rating.
The exact numbers are in the tooltip and on the Charge limit page.

**Windows asks for my password when the screen comes back in Remote mode.** That's Windows'
sign-in setting (Settings > Accounts > Sign-in options); everything kept running meanwhile.

**Something doesn't work.** System > Copy diagnostics, and paste it into your report. It
contains versions, what the app found and the last log lines, never the serial number.

## Build

Needs Windows and the .NET 10 SDK. For the native exe also the Visual Studio C++ build tools
("Desktop development with C++"); without them the script builds a trimmed single file,
which works the same.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SelfTest
```

The result is `dist\Art14Helper.exe`. `-SelfTest` runs the exe's `--self-test` (WMI,
devices, drawing, a window opened and closed; no setting is touched).

Tests cover the logic that needs no hardware:

```powershell
dotnet test --project tests\Art14Helper.Tests\Art14Helper.Tests.csproj
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md): how the code is organized, threads, UI, startup.
- [Hardware reference](docs/HARDWARE.md): every BIOS command, event, touchpad report and
  registry key the app uses.
- [Hardware test](docs/HARDWARE_TEST.md): the checklist for testing on the laptop.
- [Changelog](CHANGELOG.md): what changed in each version.
- [Contributing](CONTRIBUTING.md), [Code of conduct](CODE_OF_CONDUCT.md),
  [Security policy](SECURITY.md), [Getting help](SUPPORT.md).
- [Third-party notices](THIRD-PARTY-NOTICES.md).

## License

Copyright © 2026 The Art14 Helper authors.

Art14 Helper is free software: you can redistribute it and/or modify it under the terms of
the [GNU General Public License version 3](LICENSE), as published by the Free Software
Foundation. In short: you may use, study, change and share it, and any version you share
must stay free under the same license, with its source. It comes with no warranty.
