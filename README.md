# NOVA

A lightweight Windows desktop companion with a **Mac-style notch**: a bar docked to the top edge of the screen (Windows keeps your windows *below* it, it does not float over them) with the Island attached in the middle like a notch, plus an adaptive **Island** (clock, media controls, system status, optional weather), a configurable **floating dock** of app shortcuts, optional private clipboard history (text, pictures, files), layout profiles, multi-monitor placement, and accessibility-first settings. It does **not** replace Explorer or the Windows shell. See [`docs/VISION.md`](docs/VISION.md). Project status: [`docs/PROGRESS.md`](docs/PROGRESS.md). Limitations: [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md).

## Requirements
- Windows 10 version 2004 (build 19041) or later, or Windows 11, x64 (declared target; verify with `docs/TEST_PLAN.md`)
- .NET 8 SDK to build; .NET 8 Desktop Runtime to run the framework-dependent build

## Build, test, run
```powershell
dotnet build Nova.sln
dotnet test  Nova.sln
dotnet run --project src/Nova.App
dotnet run --project src/Nova.App -- --safe-mode   # default layout, nothing saved
```
Package (portable zip + Inno Setup installer): `.\build\publish.ps1` (add `-SelfContained` to bundle the runtime).

## Run and stop

**Run**
- From source: `dotnet run --project src/Nova.App`
- From a build: double-click `NOVA.exe` (portable zip) or start it from the Start menu (installer).
- Optional: Settings → *Start NOVA when I sign in to Windows*.
- If something looks wrong: `dotnet run --project src/Nova.App -- --safe-mode` (floating Island, no top bar, nothing in Windows is changed or saved).

**Stop** (any one of these)
- Click the notch (or press `Ctrl+Alt+N`) → **Exit NOVA**.
- Tray icon (bottom-right, maybe under the `^`) → right-click → **Exit NOVA**.
- If it is frozen: `Ctrl+Shift+Esc` → Task Manager → find **NOVA** → *End task*.
- Stopping `dotnet run` with `Ctrl+C` in the terminal also works.

On exit NOVA gives the screen space back, and puts the taskbar (and the Windows theme, if you used it) back exactly as it found them. If NOVA was force-killed and the taskbar stays hidden: start NOVA again and exit normally. Or press `Ctrl+Shift+Esc` → Task Manager → *Windows Explorer* → *Restart*, which brings the taskbar back immediately.

## New in v2.9
- **Plan and Alarm are inside the Calendar:** one month view, one agenda per day that lists events, tasks and alarms together (time order, check to finish a task or switch an alarm on/off, × to delete), and one add row. The kind button cycles *Acara → Tugas → Alarm*; the weekday chips show only for alarms (no day = rings once, listed under today).
- **Dock, Mac style again:** a glass shelf that keeps its small resting height; icons sit on it, and grow upward out of it (the shelf widens with them) when the pointer comes near. The v2.7 shrink-and-dim effect is gone.

## New in v2.8
- **Calendar, Plan and Alarm are one page with no tabs:** month and day agenda on top, today's plan and the alarms side by side below.
- **More face animations:** on top of blinking and looking around, the face now randomly hops, tilts, pops, shakes, nods, squishes, sways or spins, and changes its emoticon.
- **Dynamic Island opacity:** the Island background is see-through by default (65%). Hold **Ctrl and scroll** over the Island to change it (saved). While the Island is open it stays at least 60% solid so text is readable (Settings → Island → *Dynamic Island opacity*).

## New in v2.7
- **Dock like before:** auto-hide is on by default (touch the bottom edge, a 6 px strip, to bring it up). The dock rests slightly smaller and dimmer and swells to full size, with the icon zoom (now 2.0x), when the pointer is on it.
- **Minimal Island, all modules:** Settings → Island → *all modules* takes turns between clock, face, water progress and the next prayer (water only when reminders are on).
- **Revitalised look:** deep-navy glass, soft blue borders, accent-outlined tabs. Calendar, Plan and Alarm stay together as tabs on one page.

## New in v2.6
- **A face for the Island.** When nothing else is using the compact Island, the clock and a small animated face take turns (Settings → Island: *clock only*, *clock and face*, or *face only*). Six styles: Pet, Robot, Anime, Cat, Sleepy, Emoticon; they blink and look around (still when reduced motion is on). You choose how many seconds each stays.
- **New Island pages (8 in all):** **Minum Air** (glass counter, goal, a notice every 15–240 minutes between two hours), **Waktu Sholat** (the five prayers and sunrise worked out on the PC without internet, Kemenag method, 25 Indonesian cities or your own coordinates, optional adzan notice and sound) . **Plan and Alarm now live inside the Kalender page** as tabs (*Kalender · Rencana · Alarm*), so calendar, today's plan and alarms are one page. Alarms: one-shot or repeating on chosen weekdays, own sound file optional; click the notice to silence it.
- **Tasks on the calendar.** On the calendar page the add button switches between *Acara* and *Tugas*; tasks for a day show in that day's agenda (with check and delete) and mark the day. Today's plan list shows undated tasks, today's, and unfinished older ones.
- **CPU, RAM and more.** The *Sistem & Alat* page starts with bars for CPU, RAM, disk and battery (red when high / low) and the network speed. Optional: a small "CPU 12% · RAM 48%" line under the clock in the compact Island.
- **Lock screen button** in the Island's button row (same as Win+L).

- **Solid-window mode looks right.** If you use *Use opaque windows* the Island and dock are now clipped to their rounded shape (no square corners) and can be faded with the *Island transparency* / new *Dock transparency* sliders. `--safe-mode` no longer forces solid windows. For the see-through Island, the rounded dock and icon zoom, keep that option off and restart NOVA.

## New in v2.5
- **Island: transparency and position.** Settings → Island → *Island transparency* fades only the glass background (text stays sharp), so what is behind the Island shows through. **Drag the Island** with the mouse (press on an empty part of it and move) to put it anywhere on its monitor; it remembers the spot (*Put the Island back at the top centre* in Settings resets it). The full-bar notch stays fixed on the bar.
- **Opening apps from the dock is reliable.** NOVA now brings the window forward the way Windows allows a background program to (before, Windows could refuse and, with the taskbar hidden, nothing visible happened). If the remembered window has just closed, NOVA finds the app's current window, or starts the pinned app again.
- **Frequent folders.** Right-click File Explorer in the dock: the folders you open most are listed directly in the menu (under *Frequent*), no "Quick access" submenu.
- **Auto-hide follows the pointer.** Touch the bottom edge anywhere along the screen to bring the dock up; it goes away about 0.7 s after the pointer leaves, and never while a menu is open.
- **Notes: detail view.** Click a note in *Catatan & Clipboard* to read all of it, edit it, copy it or delete it (notes can now be up to 1000 characters; the list scrolls).
- **Clipboard history works with pictures and files** (text, screenshots / "Copy image", files copied in Explorer), shows a thumbnail, can delete single items, and can be switched on straight from the Island with one tap. Memory only, never saved to disk.

## New in v2.4
- **One music page.** The duplicate "Sedang Diputar" page is gone; the Island now has **7 pages**, and *Musik* (page 2) is the only music page (it also holds the *Aktifkan lirik* button).
- **Lyrics fit the Island.** The compact Island shows the current lyric line and **wraps it onto up to 3 lines** when it is long; the Island grows wider/taller with the text (smoothly) and shrinks again on the next short line. Under the lyric, a small line shows *title · artist*. Without lyrics it shows the title with the artist underneath, plus a play/pause button and the equalizer, as in the concept.
- **New place and look.** The Island floats at the top centre, a little away from the edge (10 px), as a dark-navy glass capsule with a soft blue edge. It **floats over the windows**: no strip is kept free for it any more (*ReserveTopSpace* now defaults to off; it is still a setting).
- **Rounded-square app windows.** The programs you open get large rounded corners (radius 6–40, default 20) through the Windows window-region API. Reversible. See `docs/LIMITATIONS.md` for what it cannot do (no anti-aliasing, maximized windows are left alone).
- **Auto-hide.** Settings → Dock → *Auto-hide the dock*. If you keep the Windows taskbar (dock does not replace it): Settings → Mac style → *Auto-hide the Windows taskbar* (original state restored on exit).
- **Right-click like Windows.** On an app in the dock: for **File Explorer** the folders you open most (listed straight in the menu) · the app (new window) · **Pin to dock / Unpin from dock** · **Close window** · **Exit program** (closes all its windows; if it does not close within 5 s NOVA asks before ending it). On the Start button: Windows' Win+X menu. On an empty part of the dock: *Add app…*, *Task Manager*.

## New in v2.3 (default look)
- **Only the Island at the top**: floating, centred, shows day · date · time; shows lyrics while music plays, and chat alerts (WhatsApp...) / volume / battery / Wi-Fi activity as it changes size. The old full bar is a setting.
- **Wi-Fi and Bluetooth in the Island**: tap the tile (or the bar icon) for an on/off switch and a list to find and connect, no trip to Settings.
- **Dock like the Mac**: small icons that grow near the pointer; click to open, click again to minimize, with a fly-into-the-icon animation; icons hop when an app wants attention.
- **One look for all windows** (Windows 11): title-bar colour, dark mode and round corners are matched across apps; undone on exit.

## What "Mac style" does (and does not do)
- **Top bar**: a real Windows AppBar, like the taskbar. Maximized and snapped windows stop below it. It shows the date on the left and status/weather on the right.
- **Notch**: the Island is flush with the top edge, black, with rounded bottom corners. Click it (or `Ctrl+Alt+N`) to expand clock, media, dock, settings.
- **The dock replaces the taskbar and Start button**: while NOVA runs, the Windows taskbar is hidden. The dock at the bottom starts with a **Start** button (opens the normal Windows Start menu; the Win key works too), then your pinned apps, then the other open apps after a thin separator. A dot under an icon means the app is open; clicking it switches to it. Right-click an open app → *Keep in dock*. Turn this off in Settings → *Mac style* to get the taskbar back.
- **Optional, off by default, reversible**: switch Windows to dark mode while NOVA runs (Settings → *Mac style*).
- It does **not** replace Explorer or re-skin Windows' own windows (title bars, Start menu). Windows has no supported way to do that. The real Start menu stays Windows' own; NOVA only gives you a button for it.
- Turn the whole look off in Settings → *Mac style* to get the old floating pill back.

## Using NOVA
| Action | How |
|---|---|
| Show / expand / collapse the notch (Island) | `Ctrl+Alt+N` (configurable), click the pill, or tray icon |
| Close the expanded Island | `Esc`, click elsewhere, or press the shortcut again |
| Open the dock with the keyboard | Island → **Open dock**, or tray → **Focus dock** |
| Move a dock shortcut | `Alt+←` / `Alt+→`, or right-click → Move |
| Remove a dock shortcut | `Delete`, or right-click → Remove |
| Media controls | Expanded Island, when an app is playing |
| Clipboard history (opt-in) | Island → **Clipboard**, or tray → **Clipboard history** |
| Settings, profiles, backup/restore | Island → **Settings**, or tray → **Settings…** |
| Reset layout | Island → **Reset layout**, or tray → **Reset layout** |
| Exit | Island → **Exit NOVA**, or tray → **Exit NOVA** |

Settings live in `%LocalAppData%\NOVA\settings.json`; logs (local only, no content) in `%LocalAppData%\NOVA\nova.log`. No telemetry. The only network feature is weather, which is **off by default**, asks for consent, and sends just your city name (once) and its coordinates to open-meteo.com. Clipboard history is **off by default** (text, pictures and copied files), memory-only.

## Layout
`src/Nova.Core` (settings, validation, hotkey parsing, launch validation, logging, no UI) · `src/Nova.App` (WPF UI + Windows integrations) · `tests/Nova.Core.Tests` · `installer/` · `build/`

## v2.2: Island with pages (now 7), glass dock
Hover the notch (or click it, or press `Ctrl+Alt+N`) and it grows into a panel with **7 pages** (8 before v2.4). Move between them with the ◀ ▶ buttons, the `←` / `→` keys, the mouse wheel over the title, or by dragging sideways on a page. It opens on the page that matters: Pomodoro while the timer runs, Music while a song plays, otherwise page 1. When the pointer leaves it folds back to the clock.

| # | Page | What it does |
|---|---|---|
| 1 | Wi-Fi, Suara & Pengaturan | Network / Bluetooth / battery summary, volume slider, shortcuts to Wi-Fi, mute, Bluetooth, airplane mode, Night light, Windows Settings |
| 2 | Musik | Album art, previous / play-pause / next, progress, synced lyrics, one-tap *Aktifkan lirik* |
| 3 | Catatan & Clipboard | Quick notes (saved locally), clipboard history (opt-in; text, pictures, files), file shelf (drop files on the notch, drag them out later) |
| 4 | Rencana | Today's tasks with time, priority (end a title with `!`) and progress |
| 5 | Pomodoro | 25 / 5 / 15 min timer, pause, skip, session count |
| 6 | Kalender & Acara | Month view, agenda per day, events with a reminder 10 minutes before |
| 7 | Sistem & Alat | CPU / memory / battery, brightness (laptops), screenshot, Night light, Task Manager, Calculator, Settings |

**Adaptive compact notch** (most urgent first): reminder notice → running Pomodoro timer → current lyric line while music plays → time and date. The equalizer and album art appear while music plays.

**Dock**: icons only (names in tooltips), no "+" button. Right-click the dock → *Add app…*, or drag an `.exe` / `.lnk` onto it. It reserves its strip of the screen like the taskbar (so a maximized browser stops above it), shrinks its icons on narrow screens, and steps aside for full-screen apps. Right-click menus and the tray menu are themed instead of classic Windows.

**Top bar** shows Wi-Fi (with signal), Bluetooth, sound (click = mute, scroll = volume) and battery. Each icon opens the matching Windows settings page.

Notes, tasks and events live in `%LocalAppData%\NOVA\data.json` (local only, not encrypted). Lyrics are **off by default** and send the artist and title to lrclib.net only after you accept the prompt in Settings.

### Autostart and low resource use
See `docs/AUTOSTART-RINGAN.md`: how to start NOVA at sign-in (with `--startup`, which waits for Explorer), the lean build settings, which options cost what, and how to measure RAM/CPU.
