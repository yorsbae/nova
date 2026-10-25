# NOVA

A lightweight Windows desktop companion with a **Mac-style notch**: a bar docked to the top edge of the screen (Windows keeps your windows *below* it, it does not float over them) with the Island attached in the middle like a notch, plus an adaptive **Island** (clock, media controls, system status, optional weather), a configurable **floating dock** of app shortcuts, optional private clipboard history, layout profiles, multi-monitor placement, and accessibility-first settings. It does **not** replace Explorer or the Windows shell. See [`docs/VISION.md`](docs/VISION.md). Project status: [`docs/PROGRESS.md`](docs/PROGRESS.md). Limitations: [`docs/LIMITATIONS.md`](docs/LIMITATIONS.md).

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

Settings live in `%LocalAppData%\NOVA\settings.json`; logs (local only, no content) in `%LocalAppData%\NOVA\nova.log`. No telemetry. The only network feature is weather, which is **off by default**, asks for consent, and sends just your city name (once) and its coordinates to open-meteo.com. Clipboard history is **off by default**, text-only, memory-only.

## Layout
`src/Nova.Core` (settings, validation, hotkey parsing, launch validation, logging, no UI) · `src/Nova.App` (WPF UI + Windows integrations) · `tests/Nova.Core.Tests` · `installer/` · `build/`

## v2.2: Island with 8 pages, glass dock
Hover the notch (or click it, or press `Ctrl+Alt+N`) and it grows into a panel with **8 pages**. Move between them with the ◀ ▶ buttons, the `←` / `→` keys, the mouse wheel over the title, or by dragging sideways on a page. It opens on the page that matters: Pomodoro while the timer runs, Music while a song plays, otherwise page 1. When the pointer leaves it folds back to the clock.

| # | Page | What it does |
|---|---|---|
| 1 | Wi-Fi, Suara & Pengaturan | Network / Bluetooth / battery summary, volume slider, shortcuts to Wi-Fi, mute, Bluetooth, airplane mode, Night light, Windows Settings |
| 2 | Musik | Album art, previous / play-pause / next, progress, synced lyrics |
| 3 | Catatan & Clipboard | Quick notes (saved locally), clipboard history (opt-in), file shelf (drop files on the notch, drag them out later) |
| 4 | Rencana | Today's tasks with time, priority (end a title with `!`) and progress |
| 5 | Pomodoro | 25 / 5 / 15 min timer, pause, skip, session count |
| 6 | Sedang Diputar | Detail view of the current song with surrounding lyric lines |
| 7 | Kalender & Acara | Month view, agenda per day, events with a reminder 10 minutes before |
| 8 | Sistem & Alat | CPU / memory / battery, brightness (laptops), screenshot, Night light, Task Manager, Calculator, Settings |

**Adaptive compact notch** (most urgent first): reminder notice → running Pomodoro timer → current lyric line while music plays → time and date. The equalizer and album art appear while music plays.

**Dock**: icons only (names in tooltips), no "+" button. Right-click the dock → *Add app…*, or drag an `.exe` / `.lnk` onto it. It reserves its strip of the screen like the taskbar (so a maximized browser stops above it), shrinks its icons on narrow screens, and steps aside for full-screen apps. Right-click menus and the tray menu are themed instead of classic Windows.

**Top bar** shows Wi-Fi (with signal), Bluetooth, sound (click = mute, scroll = volume) and battery. Each icon opens the matching Windows settings page.

Notes, tasks and events live in `%LocalAppData%\NOVA\data.json` (local only, not encrypted). Lyrics are **off by default** and send the artist and title to lrclib.net only after you accept the prompt in Settings.

### Autostart and low resource use
See `docs/AUTOSTART-RINGAN.md`: how to start NOVA at sign-in (with `--startup`, which waits for Explorer), the lean build settings, which options cost what, and how to measure RAM/CPU.
