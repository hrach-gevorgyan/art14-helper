# Changelog

All notable changes to Art14 Helper. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- Keyboard light Auto follows the room again while you work. It used to change only at
  start, after wake or when the mode was picked: with the change sensitivity the app asked
  for, this laptop's light sensor driver sends no reports at all. Auto now checks the sensor
  every 2 seconds, only while it's on and the screen is on.

## [1.3.0] - 2026-11-27

### Added
- Russian and Simplified Chinese. The app follows Windows' display language, or the language
  chosen in Settings > System > Language; the choice applies at once. The log and Copy
  diagnostics stay in English for bug reports.

### Changed
- Chinese text uses Windows' Chinese interface font (Microsoft YaHei UI), so a line never
  mixes fonts.
- Popups are wider when needed, and the title keeps its room before the detail is cut.

### Fixed
- The switch's On/Off label is as wide as the word needs.
- The tray tooltip leaves out whole lines when it's full, instead of cutting one mid-number.
- Error messages stay in English in the log and Copy diagnostics, and show translated in
  dialogs, BIOS errors included.
- Restoring a settings backup shows its language at once; the Drivers page status follows a
  language change; a copy started through UAC speaks the same language as the app.
- The home page's refresh-rate tip shows only at the panel's highest rate.
- Many Russian and Chinese wordings polished after a native-speaker style review.

## [1.2.0] - 2026-11-27

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
- Silent install and uninstall for scripts and package managers: `--install --silent` and
  `--uninstall --silent`, with no window and an exit code that says how it went. Windows'
  app list gets a matching quiet uninstall command.

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

[1.3.0]: ../../releases/tag/v1.3.0
[1.2.0]: ../../releases/tag/v1.2.0
[1.1.0]: ../../releases/tag/v1.1.0
[1.0.1]: ../../releases/tag/v1.0.1
[1.0.0]: ../../releases/tag/v1.0.0
