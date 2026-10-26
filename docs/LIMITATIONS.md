# Decisions and known limitations (v2.0)

## Decisions made (VISION §13) — change in code/settings if you disagree
| Decision | Chosen |
|---|---|
| Supported Windows | Windows 10 version 2004 (build 19041) or later, including 22H2, and Windows 11; x64. The floor comes from the media-session API. Declared, unverified until TEST_PLAN is run |
| Island behavior | Selectable; default **Always compact** |
| Dock behavior | Selectable; default **Always visible**, or **Reveal on edge hover** |
| Default positions | Island top-centre, dock bottom-centre, primary monitor work area (each can be moved to another monitor) |
| Offline | Works fully offline. The only network feature is weather, and it is **off by default** and asks for consent |
| Installer | Inno Setup per-user installer (no admin) |
| Portable build | Yes: zip from `build\publish.ps1` |

## Not implemented, and why
- **System notification mirroring.** Windows only lets apps read other apps' notifications through `UserNotificationListener`, which requires package identity (an MSIX-packaged app) and a user permission prompt. NOVA ships as a normal unpackaged desktop app, so this cannot work honestly. The Island's notices show NOVA's own messages and "now playing" only; nothing is simulated. Revisit only if NOVA is repackaged as MSIX.
- **Media controls** work with apps that publish a Windows media session (most browsers, Spotify, VLC, etc.); apps that don't will not appear.
- **Automatic opaque fallback.** Transparency problems cannot be detected reliably, so opaque mode is a manual setting (restart required) and is on in safe mode.
- **Island auto-expand** was dropped: expanding by itself conflicts with the "never steal focus" principle.

## Other limitations
- Weather depends on the free open-meteo.com service (no key). Coordinates are city-level; if the service changes or is unreachable, the line shows "Weather unavailable".
- Clipboard history keeps text, pictures (as compressed PNG, at most 10, none over 8 MB / 36 megapixels) and the paths of copied files (not their contents), in memory only by design; it ends when NOVA exits. It honours the Windows/password-manager "do not record" formats, but cannot know about sensitive text copied from apps that don't set them.
- Layout profiles cover look and placement only (not hotkey, dock shortcuts, or accessibility choices, deliberately).
- The compact Island is non-activating by design, so keyboard users reach it with the shortcut or tray icon, not Tab.
- Tray menu uses standard Windows styling (not themed).
- Installer and binaries are unsigned; SmartScreen may warn.
- Accessibility conformance is **not claimed**; it must be established with `docs/TEST_PLAN.md` (Narrator, high contrast, scaling).

## Mac-style notch (v2.1)
- The top bar uses the supported AppBar API, so it reserves space exactly like the taskbar. Another AppBar on the same edge (e.g. a taskbar moved to the top) is stacked next to it by Windows.
- NOVA cannot re-skin other windows' title bars, the Start menu or Explorer, and does not hook into the shell. "Windows theme" here means: the top bar + notch, optional taskbar auto-hide, and optional Windows dark mode (the standard per-user setting).
- The expanded notch temporarily covers the top of the window below it (like a Mac Dynamic Island). Only the compact notch matches the bar height.
- The bar is opaque by design (no blur/acrylic); the notch is always black (except in high contrast).
- Auto-hide taskbar and dark mode are restored on exit. If NOVA is killed, the originals are kept in `%LocalAppData%\NOVA\system-restore.json` and restored the next time NOVA starts and exits (or when the option is unticked).
- On Windows 11 with several monitors, the taskbar auto-hide setting may apply to the main taskbar only.
- Full-screen apps: the bar drops behind them and the notch hides (relies on Windows' full-screen notification; some borderless-windowed games don't send it).
- Taskbar replacement hides the taskbar window with `ShowWindow` plus auto-hide. If Explorer restarts, the taskbar can reappear until NOVA is restarted. The Windows Start menu may open at the bottom-centre, partly behind the dock.
- Open apps are found by polling visible windows every 2 s (only while the dock is on). Pinned shortcuts are matched to open apps by exe path or file name, so an oddly named shortcut may not show its dot. Store apps share a frame-host process and are listed per window, without a pinned dot.
- No tray-icon area, no notification centre, and no quick settings from the hidden taskbar: reach them with the Win key (Start), `Win+A` (quick settings), `Win+N` (notifications), or turn off taskbar replacement.

## v2.2 (Island pages, glass dock)
- **Never compiled or run.** Like v2.0/v2.1 this was written without a .NET SDK or a Windows PC. Expect compile errors on the first `dotnet build`; send them and they get fixed. Everything marked below as a limitation is by design, not a bug.
- **No real blur.** "Glass" is a see-through tinted strip with a soft edge. True acrylic/blur needs undocumented shell APIs or a non-transparent window; NOVA does neither.
- **Toggles open Windows Settings.** Wi-Fi, Bluetooth, airplane mode and Night light are not switched by NOVA (Night light has no supported API for unpackaged apps; radios need a permission prompt). The buttons open the right Settings page. Mute and volume *are* controlled directly.
- **Brightness** is available only on laptops/tablets with a built-in display (read and set through WMI by starting PowerShell briefly). On desktops the slider is hidden.
- **No playlist or queue.** Windows' media-session API exposes the current song only, never the app's playlist or queue. Page 6 therefore shows the current song and its lyrics, not Spotify's list.
- **Lyrics** come from lrclib.net, matched by artist and title (browser tab titles can be messy); songs without synced lyrics show none. Opt-in and sends only artist, title and length.
- **Calendar** holds NOVA's own events only. It does not read Outlook, Google or the Windows Calendar app (no supported API for unpackaged apps).
- **Lock screen**: Windows does not let third-party apps draw on the lock screen, so the Island is not shown there.
- **Swiping**: mouse drag, arrow keys, buttons and wheel work. Two-finger horizontal touchpad scrolling does not (WPF does not deliver it without a message hook).
- **Language**: the new Island pages are in Indonesian; Settings and older messages are still in English.
- Notes, plan and events are stored unencrypted in `data.json`. Clipboard history stays memory-only.
- Dock reserve strip: if another app also reserves the bottom edge, Windows stacks the strips and the dock sits in its own strip.

## v2.2.1 (concept layout, live activities, eco mode)
- **Still not compiled or run.** Same warning as above; expect a round of compile fixes.
- **Not implemented from NotchNook / Alcove:** camera *Mirror* (needs camera capture and a privacy prompt), Windows notification mirroring (no supported API for desktop apps), lock-screen widgets, AirDrop-style sharing, haptics, and call/focus-mode activities.
- **Implemented:** hover open, file shelf, app shortcuts on the notch, music with art + lyrics + real output-level visualizer, volume HUD, charger / low-battery and Bluetooth / Wi-Fi activities, two-finger sideways swipe between pages, Pomodoro and calendar live activities.
- **Volume HUD** follows the default speakers at the time NOVA starts; if you switch output device (e.g. plug in headphones) restart NOVA or toggle *Live activities* off and on.
- **Visualizer** shows the peak level of everything that plays on the default speakers (not an FFT of the song), so it also reacts to other sounds.
- **Mode Gelap tile** switches Windows light/dark right away. If *Windows dark mode* is also enabled in Settings, NOVA restores the original theme when it exits.

## v2.3 (island-only top, Mac dock, radios, chat alerts, window harmony)
- **Never compiled or run**, like v2.0-v2.2 (no .NET SDK or Windows PC where it was written). Expect a few compile errors on the first `dotnet build`; send them and they get fixed. New logic with no Windows dependency (dock zoom maths, title/counter parsing, song-title cleaning, settings migration) has unit tests in `Rev23Tests.cs`.
- **Top of the screen**: the default is now *no bar*: only the Island floats at the top centre showing day, date and time (lyrics, alerts and activities take its place for a moment). An invisible strip keeps maximized windows below it (Settings: *Island-only mode*). The old full bar is still available (*Show the full top bar*); its Wi-Fi/Bluetooth icons now open NOVA's panel too.
- **Wi-Fi / Bluetooth** are controlled with Windows' radio API (on/off), Wi-Fi API (scan, connect, password) and device API (list, find, pair, connect). Windows rules: scanning Wi-Fi needs *Location* switched on; switching radios needs *Privacy > Radios* allowed; Bluetooth devices that need a PIN must be paired in Windows Settings; "Connect" to a paired Bluetooth device is best effort (Windows decides). Airplane mode and Night light still open Windows Settings (no supported API).
- **Chat alerts (WhatsApp etc.)**: Windows only lets a packaged (MSIX) app read other apps' notification text, so NOVA cannot show *who wrote what*. It shows "WhatsApp · N belum dibaca / ada pesan baru" from two local signals: the taskbar-flash message chat apps send, and the "(N)" counter in the window title (works for WhatsApp Web only while that browser tab is the window's active tab). The dock icon hops too. Clicking the alert brings the app forward.
- **Dock**: icons start small (26) and zoom (up to 1.7x) near the pointer; the transparent dock window now spans the screen width (clicks pass through its empty parts). Clicking an open app's icon brings it forward, clicking again minimizes it with a picture-based "fly into the icon" animation; clicking a minimized one flies it back out. It is a scale-and-fade, not the Mac's genie curve. Windows that Windows can't draw (protected / some GPU-only windows) minimize without the animation. Reduce-motion turns the zoom and animations off.
- **Window harmony** uses per-window DWM settings: title-bar colour, text colour, light/dark mode, round corners, no border. Colours and corners need Windows 11; dark mode needs Windows 10 2004+. Programs that draw their own title bar (Brave/Chrome/Edge tab strip, some editors) keep their own look: set that program's theme to match (e.g. Brave: Settings > Appearance > Dark). Elevated (administrator) windows can't be changed by a normal app. Everything is restored when switched off or when NOVA exits; if NOVA is force-killed, windows keep the colours until they are reopened.
- **Lyrics** stay opt-in (they send artist + title to lrclib.net). The Now Playing page now has an *Aktifkan lirik* button for a one-tap opt-in, and browser-style titles such as "Artist - Song (Lyric Video)" are cleaned before the lookup.
- **Rounded-square windows (v2.4)** use the window region (SetWindowRgn). The clipped edge is not anti-aliased (it can look slightly jagged) and the window's own shadow is clipped. Maximized, minimized and full-screen windows, windows that already have their own shape, and elevated (administrator) windows are left alone. NOVA does not shrink maximized windows to float with a margin like the concept picture; Windows has no supported way to do that. If a program draws oddly, switch *Rounded-square app windows* off in Settings. If NOVA is force-killed, windows keep their shape until reopened.
- **Dock right-click (v2.5)**: File Explorer's menu lists its frequent folders (Explorer's Quick access, then Windows' Frequent places, read through the Windows shell) and opens them in File Explorer; other programs get no list, because it is not a per-program Jump List (Windows has no simple supported API to read those). *Exit program* closes the program's windows and, only after your confirmation, ends a program that did not close; Store apps are closed window by window and never ended.
- **Auto-hide taskbar** uses the supported AppBar state message; Windows may animate or briefly show the taskbar after some actions.
- **Island drag and transparency (v2.5)**: dragging works on the floating Island, not on the full-bar notch; it starts only from a part of the Island that is not a button, slider, list or text box. With *Use opaque windows* (the transparency fallback) the Island cannot be made see-through.
- **Bringing apps forward (v2.5)**: NOVA shares the input queue of the current foreground window for an instant, which Windows allows. A few programs (some games, elevated windows when NOVA is not elevated) can still refuse; clicking the tile again, or Alt+Tab, still works.
- **Prayer times (v2.6)** are calculated, not downloaded: expect them to be within a few minutes of the official timetable of your city (Kemenag method, +2 minutes precaution); the PC's time zone and clock must be right. Far from the equator some twilight times do not exist and are left out. The adzan is a notice plus a sound (your file or a system sound); there is no full recorded adzan built in.
- **Alarms and reminders (v2.6)** are checked every 15 seconds while NOVA runs; they do not wake a sleeping PC, and an alarm whose time passed while the PC slept (more than 2 minutes) does not ring.
- **The face (v2.6)** shows only when the compact Island would show the plain clock (not during music, a timer or a notice). Eyes are drawn with simple shapes, not images.
- **Usage line (v2.6)**: CPU / RAM under the clock reads the system every 3 seconds while shown; switch it off in Settings if you want no background work while compact.

