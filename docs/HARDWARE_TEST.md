# Hardware test

The checks that can only be done on the laptop itself, with the installed app. The
riskiest ones come first. Keep `C:\Program Files\Art14Helper\Logs\Art14Helper.log` open: every
failure lands there with its cause. System > Copy diagnostics gathers the essentials for a
report.

## 1. Install

1. Build: `powershell -ExecutionPolicy Bypass -File .\build.ps1 -SelfTest` ends with
   "Self-test passed" (or take the exe from the CI build).
2. Run `dist\Art14Helper.exe`, keep both options on, **Install**, accept UAC. The progress
   page ends with "installed and running in the tray"; the Start menu has **Art14 Helper**
   with the app icon.

## 2. BIOS

| Check | Expected |
|---|---|
| Hover the tray icon | Tooltip with temperatures, fans and the charge window |
| Charge limit → Office, then Home | The tooltip's window follows within a few seconds |
| Keyboard → Off, Dim, Bright, Auto | The keys follow each time |
| Fn lock → media keys, then standard | The Fn LED follows |

If the tooltip has no temperatures, the log says why (for example "WMI method OemWMIfun failed").

## 3. Keys and touchpad

- Fn+Space, F7, F8, F10 and F11 (media key mode), F12 and the touchpad key each react once,
  with their popup. After 10 idle minutes they still work.
- Slide along the touchpad's left edge (brightness) and right edge (volume); knock twice with
  a knuckle: a screenshot in `Pictures\Screenshots`, also on the clipboard.
- Tapping Fn alone switches the Fn lock without telling Windows (no event), so the Fn lock
  page can't follow a tap.

## 4. Auto keyboard light

Keyboard → Auto. Cover the light sensor next to the camera for 2 s: the keys light up within
about 2 s. Uncover in a bright room: they dim after about 4 s. The log shows
"Keyboard auto: N lux -> M%"; in steady light nothing is logged.

## 5. Charger, sleep and battery

1. Close the lid for a minute and open it. The log shows "Woke up: restoring…" once; keyboard
   mode, charge limit, color mode, touchpad settings and the mic LED are as before. Letting
   the screen dim by itself and moving the mouse logs no wake.
2. Display → Auto, then unplug: one short blank (the panel can only switch 60/120 Hz with a
   mode change), the colors stay right, then "On battery · N%" with "60 Hz to save power".
   No more popups as the percentage drops.
3. Plug in: one short blank, then about 3 s later "Charging · N%" with "… min to 70% · 120 Hz",
   or "Plugged in · N%" with "Limit reached" or "Charges below 40%". With a fixed refresh
   rate there is no blank and no Hz in the popup.
4. Charge limit → Charge to 100% once, while plugged in: the battery charges past the limit,
   also after closing and opening the lid. Unplug: back to the limit, quietly.
5. Charge limit → Battery report opens in the browser, not as administrator.

## 6. Display

Display → sRGB, P3, Native, and Keep native white on/off: each changes the screen. Turn HDR on
and off in Windows: the chosen mode comes back by itself.

## 7. Power and keep awake

1. Power mode → each of the three: Windows Settings > System > Power shows the same.
2. Keep awake → 30 minutes with the Windows screen timeout at 1 minute: the screen stays on
   past it, and `powercfg /requests` (admin) lists Art14Helper. With "Keep the screen on" off
   the screen times out but the laptop doesn't sleep. With "Stop apps showing you as away"
   on, Teams stays green after 10 idle minutes. After 30 minutes: "Keep awake ended".
   Start 4 hours, then update or restart the app: the log says "Keep awake resumed after a
   restart, until …" and the laptop still doesn't sleep.
3. Remote mode on: the screen goes off and the tooltip says "Remote mode". Close the lid for
   5 minutes with a download or a long task running: it keeps going. Remote mode off: closing
   the lid sleeps again. Restarting the app keeps remote mode on. Moving the mouse turns the
   screen back on, and the log shows no "Woke up". Whether Windows then asks for the password
   is its own Sign-in options setting.

## 8. Windows, menus and settings

- Right-click the tray icon: the menu follows the dark or light theme, descriptions show
  next to the pointer, every item works.
- The settings window: every page; Home's values update while it's in front; on a monitor
  with another scale it redraws sharply.
- Restart Explorer (Task Manager > Windows Explorer > Restart): the tray icon comes back.
- Start the app again from the Start menu while it runs: its window opens.
- System → Back up settings, change a few choices, Restore settings: the choices and the
  hardware are back, with "Settings restored".
- System → Copy diagnostics, paste into Notepad: versions, battery, settings and log lines,
  no serial number and no user name.

## 9. Drivers

Open the Drivers page: the list loads, then "Checking for updates…". Download one package:
the save dialog opens, the file lands where chosen, and `C:\Program Files\Art14Helper\Work`
is empty afterwards. Back up drivers to a zip (UAC); the zip opens.

## 10. Uninstall

System → Uninstall, switch on **Turn off the charge limit**, **Uninstall**. The tray icon
goes; `C:\Program Files\Art14Helper` and the Start menu entry are gone; the screen is in
native colors with HONOR's profile as default; the charge limit is off; the lid sleeps as
before.
