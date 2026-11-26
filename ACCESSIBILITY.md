# Accessibility

Art14 Helper should be usable by everyone who owns the laptop. This page says honestly what
works today, what doesn't yet, and how to get around it. If something here is wrong, or
something gets in your way, please [tell us](#reporting-a-barrier).

The goal is [WCAG 2.2](https://www.w3.org/TR/WCAG22/) level AA, as it applies to a desktop
app. **The app doesn't meet it yet**, mainly because screen readers can't read the settings
window. The gaps are listed below.

## What works

**Keyboard**
- The settings and setup windows work without a mouse: **Tab** and **Shift+Tab** move
  between rows, tiles and buttons, **Enter** or **Space** activates the one in focus, and a
  focus ring shows where you are. **Esc** closes the window. **Up**, **Down**, **Page Up**,
  **Page Down**, **Home** and **End** scroll.
- Holding Space or Enter doesn't repeat, so a held key can't flip a hardware switch over
  and over.
- The tray menu has every choice the settings window has. Open it from the keyboard with
  **Win+B**, the arrow keys to the icon, and **Enter**. It's a standard Windows menu: arrow
  keys move, **Enter** chooses, **Esc** closes.
- Everything the app does with a key (F7 microphone, F8 camera, Fn+Space keyboard light)
  is a hardware key, and also available in the menu and the settings window.

**Vision**
- Follows Windows' light or dark mode and accent color, and switches live when you change
  them.
- Sharp at every display scale (100 % to 300 % and per monitor): text and icons are drawn at
  the monitor's resolution, not stretched.
- Colors come from Windows 11's own theme values, which are designed for readable contrast.
- Popups and menu entries say their state in words ("Camera off", "Microphone on", a check
  mark on the chosen entry), not by color alone.

**Motion**
- Popups follow Windows' **Animation effects** switch (Settings > Accessibility > Visual
  effects). With it off, they appear and go without sliding or fading.
- Nothing flashes or blinks.

**Popups**
- Popups never take the focus or block clicks, so they can't interrupt typing or a screen
  reader.
- They only repeat something you can also see elsewhere: the state is always in the tray
  menu and the settings window too.

## Known gaps

| Gap | Who it affects | Workaround until it's fixed |
|---|---|---|
| **Screen readers (Narrator, NVDA, JAWS) can't read the settings and setup windows.** The app draws them itself and doesn't yet describe them to Windows' accessibility interface (UI Automation). | Blind and low-vision users | The tray menu is readable by screen readers and has every choice the settings window has. The setup window only appears when installing or updating: see the README for what its two options do. |
| **Popups aren't announced** by screen readers. | Screen-reader users | The new state is in the tray menu (a check mark on the chosen entry). For F7, the key's LED also shows when the microphone is muted. |
| The **tray menu has no access keys** (underlined letters), so each entry is reached with the arrow keys. | Keyboard users | Arrow keys; the menu is short at each level. |
| **Popups disappear after about 2 seconds**, with no way to keep them longer. | People who read slowly or use magnification | The same state is in the tray menu and the settings window, without a time limit. |
| **High-contrast themes** (contrast themes) aren't followed: the windows keep their light or dark colors. | People who rely on contrast themes | Windows Magnifier's color inversion, or dark mode with a higher display scale. |
| **Windows' Text size setting** (Settings > Accessibility > Text size) doesn't enlarge the app's text; only the display scale does. | People who need larger text | Raise the display scale (Settings > System > Display > Scale), or use Magnifier. |
| The **touchpad edge gestures** and the **knuckle screenshot** need fine finger control. | People with limited dexterity | Brightness and volume keys, Win+Shift+S or F12 for a screenshot. The gestures can be switched off. |

## Planned

In order of impact:

1. Describe the settings and setup windows to UI Automation, so screen readers can read and
   use every row, switch and button.
2. Announce popups to screen readers (a UI Automation notification), and add access keys to
   the tray menu.
3. Follow contrast themes with Windows' system colors.
4. Follow Windows' Text size setting.
5. Follow Windows' "Dismiss notifications after" setting for how long popups stay.

## Reporting a barrier

Please [open an issue](../../issues/new/choose) and choose **Bug report**. Say what you were
trying to do, what got in the way, and which assistive technology you use (for example
Narrator, NVDA, Magnifier, a contrast theme, or keyboard only). Accessibility problems are
treated as bugs, not feature requests.

Art14 Helper is a free project maintained in spare time, so a fix may take a while, but
barriers that stop someone using the app come first.
