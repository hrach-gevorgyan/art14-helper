# Hardware reference

Everything Art14 Helper talks to on the HONOR MagicBook Art 14 (2024, Intel Core Ultra,
MRA-XXX), and how. Measured and verified on that laptop; other HONOR models may differ.

## BIOS (ACPI-WMI)

HONOR's BIOS interface is the WMI method `OemWMIMethod.OemWMIfun` in `root\wmi`,
instance `HWMI_0`. Only administrators may call it.

- **Input:** 64 bytes. Byte 0 is the group, byte 1 the command, bytes 2–7 the arguments.
  In this document a command is written as one little-endian number, so group `0x03`,
  command `0x10` with arguments `40, 70` is `0x46281003`.
- **Reply:** byte 0 is a status (`0` = done), the rest is the answer.

| What | Group | Command | Arguments | Reply |
|---|---|---|---|---|
| Charge window, write | `0x03` | `0x10` | start %, end % | — |
| Charge window, read | `0x03` | `0x11` | — | byte 1 start %, byte 2 end % |
| Temperature | `0x02` | `0x02` | zone: `0x00` CPU, `0x0E` battery | byte 2: °C |
| Fan speed | `0x02` | `0x08` | fan: `0` or `1` | bytes 1–2: rpm, bytes 3–4: the firmware's target (16-bit LE) |
| Fn lock | `0x04` | `0x07` | `1` standard F-keys, `2` media keys | — |
| Microphone LED | `0x04` | `0x0B` | `0` off, `1` on | — |
| Keyboard idle timeout | `0x06` | `0x11` | seconds, 16-bit little-endian (`0` = stay on) | — |
| Keyboard mode, read | `0x06` | `0x13` | — | byte 1: mode code |
| Keyboard mode, write | `0x06` | `0x14` | mode code | — |
| Keyboard Auto level | `0x06` | `0x15` | 0–100 % | — |

Keyboard mode codes: `0x02` off, `0x03` dim, `0x04` bright, `0x10` Auto. In Auto the
firmware picks no brightness itself: the app reads the light sensor and sends the level
with `0x1506`. Measured on BIOS 3.05: level `0` turns the light off and any other level
gives the same full brightness, so Auto is on or off (there is no dim step in Auto).

The ambient light sensor (Windows Sensor API) reads correctly on request, but with a change
sensitivity set it sends no change reports at all (any `VT_R8` value; a `VT_R4` one is
ignored), and without one it reports five times a second whatever the report interval. The
app therefore reads it every 2 s while keyboard Auto is on and the screen is on. Some firmware versions refuse fixed levels; the app then emulates Dim and
Bright through Auto at 50 % and 100 %.

Charge windows: Home 40–70 %, Office 70–90 %, Travel 95–100 %, off 0–100 %. Charging
starts below the first number and stops at the second. Turning the limit off writes an
empty window (`0, 0`) first, then `0, 100` a second later, which is how the firmware
accepts a full window. Some firmware drops the window back to 0–100 a few seconds after
wake; the app checks three times, 3 s apart, and puts it back.

## BIOS events

The same firmware raises `OemWMIEvent` in `root\wmi`. The low 16 bits of its `Force`
property are the event code.

| Code | Meaning | What the app does |
|---|---|---|
| `0x287` | F7 | Mutes or unmutes the default microphone, sets the LED |
| `0x288` | F8 | Camera privacy switch (see below) |
| `0x28A` | F10 | Runs the action chosen for F10 (media key mode only) |
| `0x28B` | F11 | Runs the action chosen for F11 (media key mode only) |
| `0x28E` | F12 | Opens the screen snip (`ms-screenclip:`) |
| `0x283` / `0x2A3` | Touchpad turned on / off by the firmware | Popup |
| `0x2B1`–`0x2B4` | Fn+Space set the keyboard light: off, dim, bright, Auto | Stores the mode, Auto starts or stops, popup |
| `0x2E0` / `0x2E1` | Magnetic camera taken off / put back on the screen | Popup; when put back with the F8 privacy switch on, says so |
| `0x109` | Charger plugged in or out | Not used (Windows' own power broadcast is used) |
| `0x281` / `0x282` | Screen brightness went down / up (touchpad left edge or keys) | Not used (Windows shows its own brightness flyout) |

The camera turns up as a USB device ("FHD Camera", `USB\VID_3277&PID_00A8`) only while an
app uses it; the rest of the time Windows lists it as not present, attached or not. The
camera events are states, and the app reacts only to a change, so a repeated or
re-announced event shows nothing.

Tapping Fn alone switches the Fn lock in the firmware (LED on = standard keys) without any
event, so the app can't follow a tap. Some firmware repeats a press; events within 400 ms
of the previous one count once.

## Touchpad (HID)

The touchpad's vendor collection (usage page `0xFF00`, usage `0x01`) on Goodix `27C6:0F9A`
or `35CC:0104` takes 3-byte output reports `0E <command> <value>` and sends events as
reports with id `0x0E`. The firmware forgets settings in sleep; the app writes them again
after wake and when the device reappears.

| Setting | Command | Values |
|---|---|---|
| Press sensitivity | `0x01` | `0` less, `1` more sensitive |
| Vibration | `0x02` | `0` soft, `1` medium, `2` strong |
| Left-edge swipe reports (brightness) | `0x07` | `0` off, `1` on |
| Right-edge swipe reports (volume) | `0x08` | `0` off, `1` on |
| Knuckle double-tap reports | `0x0B` | `0` off, `1` on |

| Event | Subcommand | Values |
|---|---|---|
| Left-edge swipe | `0x03` | `1` up, `2` down |
| Right-edge swipe | `0x04` | `1` up, `2` down |
| Knuckle double-tap | `0x06` | — |

## Registry

| Key | Value | Meaning |
|---|---|---|
| `HKLM\SOFTWARE\HONOR\ASvidDMFT\Settings` | `CameraStatus` | `1` picture, `0` black frames. HONOR's camera driver reads it on every frame, so F8 works even during a call |
| `HKLM\SOFTWARE\AwinicAPO\mode` | `mode` | Speaker processing: `11` spatial, `1` stereo |
| `HKLM\SOFTWARE\HonorAPO` | `HonorAlgoMode` | The same for HONOR's audio driver |
| `HKCU\Software\Art14Helper` | — | The app's own settings |

## Display

- **Panel:** EverDisplay OLED `EDO4179`, 3120 × 2080. Its EDID keeps the real timings in a
  DisplayID 2.0 extension: 120 Hz and 60 Hz at the same 900.864 MHz pixel clock, 60 Hz
  with twice the vertical blanking.
- **Refresh rate:** switched with `ChangeDisplaySettingsEx`. Windows' virtual refresh rate
  path (`SetDisplayConfig`) also works, but it is a full mode change too (about 1.2 s), and
  the Intel driver reports no low refresh rate feature (`ctlGetPowerOptimizationCaps`:
  frame buffer compression, panel self refresh and DPST/OPST only). So every switch blanks
  the screen once and drops the color calibration, which the app restores at once.
- **Color modes:** the Intel Graphics Control Library (`ControlLib.dll`, part of the
  graphics driver) loads a 17 × 17 × 17 3D LUT into the display pipe. The color points come
  from HONOR's factory measurement when present, otherwise from the EDID
  (`WmiMonitorDescriptorMethods`). The app also writes matching ICC profiles
  (`Art14 Helper … .icc`) and makes one the default, for color-managed apps.

## Power and battery

- **Battery details:** `IOCTL_BATTERY_QUERY_TAG` (`0x294040`), `IOCTL_BATTERY_QUERY_INFORMATION`
  (`0x294044`) for design and full-charge capacity and cycles, `IOCTL_BATTERY_QUERY_STATUS`
  (`0x29404C`) for capacity and charge rate. No admin rights needed.
- **Power mode:** `PowerGetEffectiveOverlayScheme` / `PowerSetActiveOverlayScheme`
  (`961cc777-…` best efficiency, `00000000-…` balanced, `ded574b5-…` best performance).
- **Lid close action:** setting `5ca83367-6e45-459f-a27b-476b1d01c936` in subgroup
  `4f971e89-eebd-4455-a8de-9e59040e7347` of the active power plan, for AC and DC.
- **Keep awake:** `SetThreadExecutionState` (`ES_CONTINUOUS | ES_SYSTEM_REQUIRED`, plus
  `ES_DISPLAY_REQUIRED` to keep the screen on). The laptop uses Modern Standby (S0 low power
  idle), where a dark screen is the way into standby; Remote mode therefore
  holds `ES_SYSTEM_REQUIRED` until the screen is on again.

## HONOR's update servers

The Drivers page asks HONOR's update platform
(`update.platform.hihonorcloud.com`) and support site for the laptop's model, BIOS and
driver versions, Windows version and display language. It sends no serial number.
Downloads are checked by HTTPS, SHA-256 when HONOR publishes one, and the Authenticode
signature of every executable file.
