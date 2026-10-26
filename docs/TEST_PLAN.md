# NOVA manual test plan

Automated tests (`dotnet test`) cover Core logic only. Everything below must be run by a person on Windows. Record result, Windows build, scaling and date per run.

## A. Smoke
1. Launch → Island pill visible top-centre, dock bottom-centre, tray icon present.
2. Launch a second copy → it exits immediately (single instance).
3. Tray → Exit NOVA → process gone, tray icon gone, taskbar/Explorer unaffected.

## B. Focus (no focus stealing)
1. Open Notepad, type continuously. Wait through a minute rollover of the clock → typing is never interrupted.
2. Click the dock icon of another app → that app opens; Notepad focus rules behave normally.
3. Press the shortcut while typing in Notepad → Island expands and takes focus (explicit action). Press `Esc` → focus returns to Notepad.
4. Trigger a notice (launch a dock entry whose file was deleted) while typing → text appears in the pill, typing is not interrupted.

## C. Keyboard only
1. Shortcut → Tab through Open dock / Settings / Reset layout / Exit; focus rectangle visible on each.
2. Open dock → arrows/Tab move between icons and **Add app**; Enter launches; `Alt+←/→` reorders; `Delete` removes; focus lands on a neighbour.
3. Settings: reachable and operable using Tab, arrows, Space, Enter; `Esc` closes.
4. No keyboard trap anywhere (Tab always leaves the dock and Island).

## D. Narrator (Win+Ctrl+Enter)
Every button, slider, combo box, checkbox and text box announces a meaningful name and role. Notices are announced when they appear. Dock buttons announce "Launch <app>". Record anything announced as just "button".

## E. Display scaling
Repeat B/C smoke at 100%, 125%, 150%, 200%, and with two monitors of different scale. Text is legible, nothing clipped, Island/dock centred on the primary work area. After changing scale or unplugging a monitor, use tray → Reset layout if misplaced and record it.

## F. Contrast and motion
1. Windows high contrast on (Aquatic, Desert, Night sky, Dusk): Island, dock, Settings use system colors and are readable; focus indicator visible. Toggle while running → NOVA updates.
2. Settings → Increase contrast on → same without Windows HC.
3. Reduce motion on, or Windows "Animation effects" off → expanded panel appears with no fade.

## G. Settings and recovery
1. Change theme, sizes, spacing, offsets, clock format, preset → Apply → visible immediately; restart → persisted.
2. Invalid shortcut text → error shown, nothing applied. Shortcut already used by another app → "unavailable" message, app still works via click/tray.
3. Corrupt `settings.json` (type garbage) → app starts with defaults, notice shown, `settings.json.bad` exists.
4. Reset layout / Reset all → known-good layout restored.
5. `--safe-mode` → defaults, nothing written to the real settings file.
6. Island behavior "Hidden until invoked" → pill hidden until shortcut/tray. Dock "Reveal on edge hover" → thin handle; hover reveals; mouse away hides after ~1 s; keyboard focus keeps it open.

## H. Dock safety
Add `.exe` and `.lnk`; try adding other types (blocked); delete the target of an entry then launch (notice, no crash); 25th entry refused.

## I. Install / uninstall
1. Install via installer (per-user) with and without "Start with Windows".
2. Toggle Start with Windows in Settings → Run key `HKCU\...\Run\NOVA` appears/disappears.
3. Uninstall → files removed, Run key removed, prompt offers to delete `%LocalAppData%\NOVA`; taskbar/Explorer unaffected.
4. Portable zip: unzip anywhere, run `NOVA.exe`, delete folder → nothing left except `%LocalAppData%\NOVA`.

## J. Performance baseline (needed to set numeric budgets)
Record cold start to Island visible, idle CPU % over 5 min, working set, with and without dock icons. Idle CPU should be ~0 (clock wakes once a minute).

---

## Phase 2 / 3 additions

## K. Media controls
1. Play music/video in a browser or media app → expand the Island → track and artist appear, with **Previous / Pause / Next**; buttons enable/disable to match what the app supports.
2. Pause from the app itself → Island shows "(paused)" and the button reads **Play**. Close the app → media panel disappears.
3. Settings → turn "Media controls" off → panel gone, no media subscription (verify in the log that no media errors appear).
4. Island behavior **Adaptive** + "Show temporary notices" on → changing track shows "Now playing: …" briefly without taking focus. With notices off → nothing appears.
5. Narrator: buttons announce "Previous track", "Pause playback"/"Resume playback", "Next track".

## L. System status
1. Expanded Island shows `CPU x%  ·  Memory y%` (and `Battery z%` on a laptop; absent on a desktop). Run a CPU-heavy task → CPU rises within ~4 s.
2. Collapse → no further sampling (idle CPU for NOVA ≈ 0; check in Task Manager/Process Explorer).
3. Unplug/plug in a laptop → text shows "(plugged in)" / "(charging)" / nothing.

## M. Weather (needs internet)
1. Settings → enable weather with a city → consent dialog appears first; choosing **No** leaves weather off.
2. After **Yes** → within a few seconds the expanded Island shows `Place: 29°C, Partly cloudy`. Fahrenheit toggle changes the unit.
3. Misspelled city → notice "Couldn't find …"; nothing crashes.
4. Disconnect network → "Weather unavailable"; reconnect → recovers on the next refresh (≤ 30 min, or toggle off/on).
5. With weather **off** (default), confirm with a network monitor (e.g. Resource Monitor → Network) that NOVA makes no connections.

## N. Clipboard history
1. Settings → enable → consent dialog; **No** keeps it off.
2. Copy several text snippets → Island **Clipboard** button (or tray → Clipboard history) lists newest first; choosing one copies it again; duplicates move to top.
3. Copy from a password manager (e.g. Bitwarden/KeePass copy-password) → entry is **not** recorded.
4. Copy an image/file → not recorded. Copy a text over 5,000 characters → not recorded.
5. **Clear history** empties the list. Disable the feature → list wiped; re-enable → starts empty. Exit/restart → empty (nothing persisted: search `%LocalAppData%\NOVA` for a copied string; it must not be there, including in `nova.log`).

## O. Multiple monitors and DPI
1. With 2+ monitors: set Island monitor and Dock monitor independently in Settings → each moves and stays centred on that monitor's work area.
2. Mixed scaling (e.g. 100% and 150%): placement correct on both, text not clipped, no repeated jumping.
3. Unplug the monitor that holds an overlay → it falls back to the primary monitor (setting is kept); re-plug → returns after the next Apply or restart. Change resolution/scale while running → overlays re-centre.

## P. Profiles and backup
1. Arrange a layout → save as profile "Work" → change things → load "Work" → Apply → layout restored; hotkey, dock shortcuts and accessibility settings unchanged.
2. Save 10 profiles → the 11th is refused with a message. Save same name again → replaces. Delete works.
3. Export settings → file is readable JSON. Import it after changing settings → form shows the backup (including dock shortcuts); Apply makes it live.
4. Import a text/HTML file, a missing file, and a hand-edited file with absurd values (`"dockIconSize": 5000`) → clean error or values clamped; no crash.
5. After two saves, `settings.json.bak` exists and holds the previous version.

## Q. Opaque fallback
Settings → "Use opaque windows" → restart NOVA → Island and dock are solid rectangles, everything still works. `--safe-mode` also uses opaque windows.

## R. Mac-style notch (v2.1)
1. Launch → a bar spans the top of the primary monitor, the notch is attached to its centre, flush with the screen edge.
2. Open Notepad and maximize it → its title bar starts **below** the bar (nothing hidden underneath). Snap windows with `Win+←/→` → same.
3. Click the notch → it expands (clock, buttons). `Esc` or clicking elsewhere collapses it. The bar stays put.
4. Exit NOVA → a maximized window grows back to the top edge (space released).
5. Settings → turn *Mac-style notch* off → Apply: bar disappears, windows can use the top edge, the floating pill returns. On again → bar and notch come back.
6. Settings → *Auto-hide taskbar* on → taskbar hides; Exit NOVA → taskbar is back as before. Repeat: kill NOVA in Task Manager while hidden, start NOVA, exit → restored.
7. Settings → *Windows dark mode* on → apps/Explorer turn dark; exit → previous theme restored (test starting from light and from dark).
8. Play a full-screen video/game → bar and notch don't cover it; leaving full screen → they return.
9. Two monitors (different scale): set *Island monitor* to the second one → bar and notch move there, first monitor's work area is released.
10. Windows high contrast on → notch uses system colors and stays readable. `--safe-mode` → no bar, floating pill, taskbar/theme untouched.
11. Change display scale (125%/150%) while running → bar height and notch stay aligned (no gap or overlap).
12. Taskbar is hidden; dock shows Start first. Click Start → Start menu opens. Open Notepad → it appears in the dock with a dot; click it while another window is in front → it comes forward; minimize it, click → restored. Close it → icon disappears within ~2 s.
13. Right-click an open (unpinned) app → *Keep in dock* → it stays after closing. Pinned app already open: clicking switches instead of starting a second copy.
14. Exit NOVA → taskbar visible again, work area back to normal. Kill NOVA with the taskbar hidden → restart Explorer from Task Manager recovers it.

## S. v2.4 changes
1. Music page: the Island has 7 pages; Musik is page 2; no "Sedang Diputar" page. With lyrics off, the *Aktifkan lirik* button shows on page 2.
2. Play a song with lyrics on: a long lyric wraps onto 2-3 lines in the compact Island, the Island grows smoothly and shrinks on a short line; the corners stay rounded; no flicker or jump of position.
3. Island sits at the top centre, 10 px from the edge, over the windows (a maximized browser goes under it, as in the concept).
4. Rounded windows: open Notepad, File Explorer, a browser; corners are rounded; drag-resize in all 8 directions still works (the invisible border grabs); maximize removes the clip; restore re-applies it; switch the setting off and every window is square again; exit NOVA and the same.
5. Auto-hide dock: Settings → Dock → Auto-hide; dock hides, bottom-edge hover reveals it, an open right-click menu keeps it visible.
6. Auto-hide taskbar (dock replacement off): taskbar hides; switch off or exit and the original state returns. The taskbar is NOT hidden outright.
7. Dock right-click on a running app: Quick access folders open in Explorer; Pin/Unpin; Close window; Exit program (try an app with unsaved work: it asks to save, then after 5 s NOVA asks before ending it). Right-click Start: Win+X menu. Right-click empty dock: Add app…, Task Manager.

## T. v2.5 changes
1. Settings → Island → *Island transparency* to 40%: the Island's background lets a window behind it show through, its text stays sharp; 100% = solid as before.
2. Drag the compact Island (press on the clock, not on a button) to another place: it follows the pointer, stays on its monitor, and does not expand on release. Restart NOVA: it is where you left it. *Put the Island back at the top centre* + Apply: back to default. The full-bar notch (Settings → full top bar) cannot be dragged.
3. Dock: click an open app (also one that has two windows, and one you just closed): it comes to the front every time; a pinned app that is no longer open starts. Try clicking right after switching windows with Alt+Tab.
4. Right-click *File Explorer* in the dock: a *Frequent* heading and your frequent folders appear at the top; clicking one opens it. Folders you opened recently show up the next time.
5. Auto-hide on: the dock slides away; moving the pointer to the bottom edge (left, middle and right part of the screen) brings it back within a moment; holding a mouse button (dragging a window) at the bottom does not; it hides again ~0.7 s after the pointer leaves, but not while its right-click menu is open.
6. Island → *Catatan & Clipboard* → Catatan: add a long note (several lines); click it: the full text opens; edit + *Simpan*, *Salin*, *Hapus*, and the back arrow (keeps what you typed). The list scrolls with many notes.
7. Clipboard (Settings: on, or the *Aktifkan riwayat clipboard* button on the Island): copy text, a screenshot (Win+Shift+S) or "Copy image" in a browser, and files in Explorer: each appears (thumbnail for pictures), clicking one puts it back on the clipboard (paste it to check), ✕ removes one, *Kosongkan riwayat* clears all. Copy from a password manager: not recorded. Turn history off: everything is wiped.

## U. v2.6 changes
1. Settings → Island: *clock and face* (20 s / 8 s): the face replaces the clock for a few seconds, blinks and looks sideways; try each of the six styles; *face only*; *clock only* returns to the old look. The Island does not change size when the face appears. During music/timer/notice the clock text logic is unchanged.
2. Page 7 *Minum Air*: +1 / −1 change the count and drops; turn the reminder on, pick 30 min: a "💧 Waktunya minum" notice appears at the right time (test with a short interval); +1 postpones the next one; nothing outside the hours set.
3. Page 8 *Waktu Sholat*: times for your city look right against a local timetable (±3 min); the next prayer is highlighted with a countdown; *Adzan: Aktif* shows a notice and plays a sound at the time; clicking the notice stops the sound; a custom wav/mp3 in Settings plays.
4. Calendar page → *Alarm* tab: add 06:30 with days Sen–Jum; add a one-shot a minute ahead: it rings (sound + notice), the tick turns off; clicking the notice silences it; the tick toggles an alarm; delete works; after restart alarms are still there.
5. Calendar page → *Kalender* tab: switch *Acara* → *Tugas*, add a task on a future day: the day gets a dot, its agenda shows the task with a check box; today's plan page does not show future tasks.
6. *Sistem & Alat*: bars for CPU, RAM, disk, battery and network speed move every 2 s; a bar turns red above 85% (battery below 20%). Settings → *Show CPU and RAM use under the clock*: the compact Island shows the line.
7. The lock button in the Island's button row locks the PC.

