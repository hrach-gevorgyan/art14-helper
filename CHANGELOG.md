# Changelog

All notable changes to Art14 Helper. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- ACCESSIBILITY.md: what works with the keyboard, screen readers and other assistive
  technology, the known gaps with workarounds, and how to report a barrier.
- The log says why the app closed (tray menu, installer, Windows shutting down, a copy with
  admin rights taking over), and at the next start when a run ended without closing (Task
  Manager, a crash, power loss).
- Limited mode is no longer silent: without admin rights, a popup and the tooltip say so,
  and **Restart with admin rights** at the top of the tray menu fixes it.
- Leftovers in the working folder (for example a driver download cut short) are removed
  after a day.
- **Tell me about new versions** (Settings > System, off by default): once a week the app
  asks GitHub for its latest release, sending only its version, and when a newer one is out
  says so once and adds an entry to the tray menu that opens the release page. It never
  downloads anything.

### Fixed
- Popups follow Windows' Animation effects switch: with it off, they appear and go without
  sliding or fading.
- A copy started with admin rights now takes over from one running without them, instead of
  opening its window and closing, which left the BIOS features off until the next sign-in.
- If the BIOS interface isn't ready (right after boot or wake), the app looks again a minute
  later instead of giving up until it restarts.
- The log can no longer flood the disk: at most 120 lines a minute and 4000 characters a line,
  on top of the existing 2 MB cap.

## [1.1.0] - 2026-11-27

### Added
- Popup when the magnetic camera is taken off or put back; put back while the F8 privacy
  switch is on, it says so, instead of apps silently getting a black picture.

### Changed
- Popups rise gently into place as they fade in, like Windows' own flyouts.

## [1.0.1] - 2026-11-26

### Fixed
- Started without admin rights (e.g. reopened from the Start menu after the app closed), the
  app now hands over to its elevated sign-in task again instead of running without fans,
  temperatures, the brightness flyout and other BIOS features. Administrators filtered by
  UAC were taken for standard users.

## [1.0.0] - 2026-11-24

The first release.

### Battery
- Charge limit: Home 40–70 %, Office 70–90 %, Travel 95–100 %, or off; put back after
  wake when the firmware drops it.
- Charge to 100 % once, until the next unplug.
- Battery health against the design capacity with cycle count, and Windows' battery report.

### Keyboard and keys
- Keyboard light Off, Dim, Bright and Auto (from the light sensor, with hysteresis, dwell
  time and a fade), idle timeout.
- Fn lock: standard F-keys or media keys.
- F7 microphone mute for every app with the key's LED, F8 camera privacy switch, F12
  screen snip, F10 and F11 open a chosen app.

### Touchpad
- Vibration and press sensitivity; left-edge brightness and right-edge volume gestures.
- Knuckle double-tap screenshot, saved and copied to the clipboard.

### Display
- Refresh rate 120 Hz, 60 Hz or Auto by power source.
- Calibrated sRGB, Display P3 and Adobe RGB color modes from the panel's factory
  measurement, loaded into the Intel graphics driver, with matching color profiles.
- Touchscreen on or off.

### Power
- Windows power mode in one click.
- Keep awake for a set time or until turned off, optionally keeping the screen on and
  apps from showing you as away; it carries on after an app update or restart.
- Remote mode: awake, screen off and running with the lid closed; separate lid switches.

### App
- Windows 11-style settings window, light and dark, with keyboard navigation; the same
  choices in the tray menu.
- Windows 11-style popups, only for keys, automatic changes, failures and unseen results.
- BIOS and driver updates from HONOR's catalog with checked downloads; driver backup and
  restore.
- Settings backup and restore; Copy diagnostics for bug reports.
- Installer, updater and uninstaller; starts at sign-in without prompts.
- One native exe of about 10 MB, about 11 MB of memory, no work while idle.

[1.1.0]: ../../releases/tag/v1.1.0
[1.0.1]: ../../releases/tag/v1.0.1
[1.0.0]: ../../releases/tag/v1.0.0
