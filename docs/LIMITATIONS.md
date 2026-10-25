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
- Clipboard history is text-only and in-memory only by design; it ends when NOVA exits. It honours the Windows/password-manager "do not record" formats, but cannot know about sensitive text copied from apps that don't set them.
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
