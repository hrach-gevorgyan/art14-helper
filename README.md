<div align="center">

<img src="docs/images/icon.png" width="96" alt="Art14 Helper icon">

# Art14 Helper

**A small, fast replacement for HONOR PC Manager on the HONOR MagicBook Art 14.**<br>
One 12 MB exe. No services, no telemetry, no ads, no CPU while idle.

[![License: GPL v3](https://img.shields.io/badge/license-GPL%20v3-0b7a6b?style=flat-square)](LICENSE)
![Windows 11](https://img.shields.io/badge/Windows-11-0078d4?style=flat-square)
![.NET 10 NativeAOT](https://img.shields.io/badge/.NET%2010-NativeAOT-512bd4?style=flat-square)

**[Download the latest release](../../releases/latest)**

<img src="docs/images/app.png" width="820" alt="The Art14 Helper settings window">

</div>

> [!NOTE]
> Unofficial. Not made, endorsed or supported by HONOR. HONOR and MagicBook are trademarks
> of their owner, used here only to say which laptop this works with. Use at your own risk.

## Features

- **Battery:** charge limit (Home 40–70 %, Office 70–90 %, Travel 95–100 %), a one-time
  charge to 100 %, battery health and Windows' battery report.
- **Keyboard:** backlight Off, Dim, Bright or Auto from the light sensor; Fn lock; F7 mic
  mute with its LED, F8 camera privacy, F10/F11 open an app of your choice.
- **Touchpad:** vibration and press sensitivity, brightness and volume along the edges, a
  knuckle double-knock for a screenshot.
- **Display:** 120 Hz, 60 Hz or Auto by power source; calibrated sRGB, Display P3 and
  Adobe RGB from the panel's factory measurement.
- **Power:** Windows' power mode, keep awake for a set time or until turned off, and remote
  mode that keeps working with the lid closed.
- **Maintenance:** BIOS and driver updates from HONOR's catalog with every download checked,
  driver and settings backup, one-click diagnostics.

Popups appear only for keys, gestures and things that change by themselves, never for a
choice you just made. The [user guide](docs/GUIDE.md) has the details.

## Install

Requires the HONOR MagicBook Art 14 (2024) with Intel Core Ultra and Windows 11.

1. [Download](../../releases/latest) `Art14Helper.exe` and run it.
2. Keep **Start with Windows** and **Apply recommended settings** on, click **Install** and
   accept the UAC prompt.

It installs to `C:\Program Files\Art14Helper` and starts at sign-in without prompts. To
update, run a newer exe and click **Update**. To uninstall, use Settings > Apps.

**The exe isn't code-signed, on purpose.** Windows will call it an "unknown publisher".
Certificates cost money every year, and this free, open-source project will never pay for
one; a signature only says who built a file, not what it does. If you'd rather not trust a
download, read the code and [build it yourself](#build).

## Privacy

No telemetry, no accounts, no ads, no self-updater. The only network use is the Drivers
page asking HONOR's update server for new versions: laptop model, BIOS and driver versions,
Windows version and language, never a serial number.

## Build

Needs Windows and the .NET 10 SDK; the Visual Studio C++ build tools give the native exe
(without them you get a trimmed single file that works the same).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SelfTest
dotnet test --project tests\Art14Helper.Tests\Art14Helper.Tests.csproj
```

The exe lands in `dist\`.

## Documentation

[User guide](docs/GUIDE.md) · [Architecture](docs/ARCHITECTURE.md) ·
[Hardware reference](docs/HARDWARE.md) · [Hardware test](docs/HARDWARE_TEST.md) ·
[Changelog](CHANGELOG.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) ·
[Support](SUPPORT.md)

## License

Copyright © 2026 The Art14 Helper authors. Free software under the
[GNU General Public License v3](LICENSE): use, study, change and share it; any version you
share stays free, with its source. No warranty.
