# NOVA — Progress (v2.0.1 release candidate)

> **Last updated:** 2026-10-24
> **Source of truth:** [`VISION.md`](VISION.md) · **Related:** [`TEST_PLAN.md`](TEST_PLAN.md) · [`LIMITATIONS.md`](LIMITATIONS.md) · [`../README.md`](../README.md)
> **Overall:** All four delivery phases of VISION §12 are implemented in source, with one honest exception (system-notification mirroring, see below). **The first real build has started (see Build log); nothing has been run or tested yet.**

## Build log (real results from Windows)
| Date | Result |
|---|---|
| 2026-10-24 | First `dotnet build` of `Nova.App` on Windows: NuGet restore, the `net8.0-windows10.0.19041.0` target and the WinRT media projection were accepted, and the compiler ran. **1 error:** `CS0234` in `IslandWindow.xaml.cs` — wrong namespace `System.Windows.Animation` (correct: `System.Windows.Media.Animation`). **Fixed in v2.0.1.** Every other `using` in the solution was audited. Because the compiler can stop at early errors, more errors may still appear on the next build; send them and they get fixed the same way. |

## v2.2 (2026-10-24): UI rework from the user's concept image
Dock made compact (icons only, reserves its screen strip so maximized windows stop above it, shrinks on small screens, glass look, right-click *Add app…*); themed context menus; top bar with Wi-Fi / Bluetooth / sound / battery icons; Island rebuilt (hover expand with animation, 8 pages, adaptive compact text, album art, synced lyrics, file shelf, notes, plan, Pomodoro, calendar with reminders, quick tools). New unit tests: `ShellTests.cs`, `UserDataTests.cs`. ✅ in source · 🧪 none of it has been run.

## ⚠️ Read this first: what "done" means here

This code was written without a .NET SDK or a Windows machine. I checked bracket balance, XAML well-formedness, that every XAML event handler exists, and reviewed API usage by hand (that review already caught two compile errors). That is **not** a build. VISION §14 and §10 make "installer tested", "focus not stolen" and accessibility explicit acceptance criteria; they can only be met by running the test plan on Windows.

**Release gate — 3 steps on a Windows PC with the .NET 8 SDK:**
1. `dotnet build Nova.sln` → fix compile errors (expect some, mostly in the WinRT media and WinForms/WPF interop code), then `dotnet test Nova.sln` (65 test cases).
2. Work through [`TEST_PLAN.md`](TEST_PLAN.md) sections A–Q and record results.
3. `.\build\publish.ps1` → install/uninstall test (section I). `.github/workflows/ci.yml` builds and tests on `windows-latest` if you push to GitHub.

Legend: ✅ implemented in source · 🧪 needs Windows verification · ⛔ deliberately not implemented (reason given)

## Phase 0 — Technical spike (VISION §12)
| Item | Status |
|---|---|
| Supported Windows versions decided | ✅ Windows 10 build 19041+ and Windows 11, x64 (see LIMITATIONS) |
| Transparent top-level WPF window + opaque fallback | ✅🧪 `OpaqueFallback` setting (also used by `--safe-mode`) |
| Always-on-top, focus handling | ✅🧪 non-activating compact state; activation only on explicit user action |
| DPI scaling, multi-monitor placement | ✅🧪 PerMonitorV2 manifest; pixel placement with two-pass DPI correction |
| Global shortcut + conflict handling | ✅🧪 |

## Phase 1 — MVP (VISION §5.1)
| Requirement | Status |
|---|---|
| Background app, single instance, tray icon, clean exit | ✅🧪 |
| Adaptive Island: compact / expanded / temporary notices | ✅🧪 (notices announced to screen readers, never take focus) |
| Island behaviors: always compact / hidden until invoked / adaptive | ✅🧪 |
| Clock + date, 12/24 h, size, position | ✅🧪 |
| Floating dock: launch (validated), add, remove, reorder, edge-reveal | ✅🧪 |
| Themes dark/light/system, accent, opacity (70–100%) | ✅🧪 |
| Settings window; presets Minimal/Balanced/Custom; start with Windows | ✅🧪 |
| Configurable global hotkey | ✅🧪 |
| Keyboard navigation, focus indicators, accessible names | ✅🧪 |
| Reduced motion, high contrast (auto + override) | ✅🧪 |
| JSON config, safe defaults, recovery, reset, safe mode | ✅ logic unit-tested (not yet run) · 🧪 UI |
| Installer + uninstaller + portable zip | ✅🧪 `installer/NOVA.iss`, `build/publish.ps1` |

## Phase 2 — Supported integrations
| Item | Status |
|---|---|
| Media controls (supported Windows media-session API, event-driven) | ✅🧪 `MediaService`; track/artist, previous/play-pause/next, "Now playing" notice in Adaptive mode |
| System metrics: CPU, memory | ✅🧪 `GetSystemTimes` / `GlobalMemoryStatusEx`; sampled only while expanded |
| Battery status (only where a battery exists) | ✅🧪 hidden on desktops |
| Weather (explicit opt-in, consent dialog, open-meteo.com, city + coordinates only) | ✅🧪 needs internet; **off by default**; no timer/network when off |
| Clipboard history (explicit opt-in, consent dialog) | ✅🧪 text, pictures and copied files, memory only, honours password-manager exclusion formats, wipe on disable/exit |
| Notifications: NOVA's own non-focus-stealing notices | ✅🧪 |
| **Mirroring other apps' Windows notifications** | ⛔ Windows requires package identity (MSIX) for `UserNotificationListener`; unsupported for an unpackaged app. VISION allowed this "subject to API limitations". Nothing is simulated. See LIMITATIONS |

## Phase 3 — Refinement
| Item | Status |
|---|---|
| Layout profiles (save/load/delete, max 10; never touch hotkey, dock shortcuts, accessibility) | ✅ unit-tested · 🧪 UI |
| Backup / restore (export, validated import, automatic `.bak` of the previous settings) | ✅ unit-tested · 🧪 UI |
| Per-monitor positioning for Island and dock, re-centre on display changes | ✅🧪 |
| Accessibility, reliability, performance testing | ⬜ **requires a person on Windows**: TEST_PLAN A–J, O |
| Known limitations documented | ✅ `LIMITATIONS.md` |

## Definition of Done (VISION §14)
| Criterion | State |
|---|---|
| Launches and exits cleanly | 🧪 |
| Island compact + expanded; clock/date; dock | ✅ code · 🧪 |
| Core controls work with keyboard and mouse | ✅ code · 🧪 TEST_PLAN C, D |
| Focus not unexpectedly stolen | ✅ by design · 🧪 TEST_PLAN B |
| Themes, positions, settings persist | ✅ unit-tested logic · 🧪 |
| Reduced motion and readable contrast | ✅ code · 🧪 TEST_PLAN F |
| Invalid configuration doesn't crash startup | ✅ unit-tested logic |
| Installer/uninstaller tested | ⬜ **not tested**: TEST_PLAN I |
| Limitations and supported Windows versions documented | ✅ |

## Automated tests (`tests/Nova.Core.Tests`) — 65 cases, **not yet run**
Settings validation/clamping/migration of new fields, hotkey parsing, store round-trip + corrupt recovery + reset + `.bak` + export/import (incl. NaN coordinates, oversized/garbage files), launch validation, dock reorder, presets, layout profiles (limits, replace, case-insensitivity, repair of hand-edited JSON), CPU/memory/battery maths and formatting, media track logic, weather parsing/formatting/URLs, clipboard history (dedupe, cap, size limit, clear, preview), deep clone, privacy defaults (weather and clipboard off).
**Not covered by automation:** every UI/OS behavior (focus, DPI, monitors, Narrator, high contrast, real media sessions, network, clipboard formats).

## Privacy summary (VISION §3.5)
No telemetry or analytics. Logs are local, contain no content, and record only error types. Network: only weather, only if enabled and confirmed. Clipboard: only if enabled and confirmed, never persisted.

## Known risks (verify first)
1. **Compile errors on first build** (never compiled). Highest-risk spots: WinRT `Windows.Media.Control` usage, WPF/WinForms interop (`TrayService`, `MonitorService`), `Dispatcher` calls.
2. Focus behavior of the non-activating overlay (`WS_EX_NOACTIVATE` toggling) across apps.
3. Two-pass DPI placement on mixed-scale multi-monitor setups.
4. Edge-reveal dock hover timing/flicker.
5. Open-meteo response format and rate limits (parsers follow the documented format; not run against the live service).
6. Narrator wording of code-built controls and live-region notices.
7. Unsigned binaries → SmartScreen warnings.

## Open decisions — resolved with defaults (all changeable)
Windows 10 2004+ and 11 x64 · Island default always-compact · Dock default always-visible · top-centre / bottom-centre on primary monitor · weather is the only online feature, opt-in · Inno Setup per-user installer + portable zip.

## Changelog
- **2026-10-25 (v2.5)** — Requested changes: Island transparency (`IslandOpacity`, background only) and drag-to-move (`IslandOffsetX` + `IslandTopOffset`, saved on release); reliable bring-to-front (`NativeMethods.Activate` with input-queue sharing, stale-window re-lookup, launch when the pinned app is gone); File Explorer's dock menu lists frequent folders inline; auto-hide driven by the pointer's real position (`PollEdge`); note detail/edit view (1000 characters); clipboard history for pictures and files, per-item delete, enable button on the Island, and a fix for apps whose `CanIncludeInClipboardHistory` value was read as "exclude". New tests: `Rev25Tests`. **Written without a compiler; needs a `dotnet build` and a pass through TEST_PLAN §T on Windows.**
- **2026-10-26 (v2.6)** — Requested: Island face (6 styles, clock/face rhythm), drink reminder page, prayer-time/adzan page (offline astronomical calculation, `PrayerCalculator`), alarms, dated tasks on the calendar, CPU/RAM/disk/battery/network indicators, lock-screen button. New core code: `Reminders/`, `SystemMetrics`, `RateFormatter`, `AlarmItem`, `UserData` water/alarm/task helpers; new UI in `IslandWindow.Extras.cs`. Tests: `Rev26Tests`. **Written without a compiler; needs a `dotnet build` and a pass through TEST_PLAN §U on Windows.**
- **2026-10-25 (v2.4)** — Requested changes: single music page (page 6 removed, 8 → 7 pages); compact Island wraps long lyrics (up to 3 lines) and animates its size, shows title/artist, play/pause and equalizer; floating navy-glass Island placement (10 px from the top, no reserved strip; settings migrate once, schema 6); rounded-square app windows (`WindowShapeService`, SetWindowRgn, follows resizing through a WinEvent hook, fully reverted on exit); dock auto-hide checkbox (`DockAutoHide` ↔ `DockVisibility`) and optional Windows-taskbar auto-hide (`TaskbarAutoHide`); Windows-style dock right-click menu (`AppControl`: Quick access, Pin/Unpin, Close window, Exit program, Win+X on Start, Task Manager on the dock). New tests: `Rev24Tests`. **Written without a compiler; needs a `dotnet build` and a pass through TEST_PLAN §S on Windows.**
- **2026-10-24 (v2.1)** — Mac-style notch. The Island no longer floats over windows: a top bar is docked to the screen edge as a real AppBar (Windows reserves its space, windows maximize below it) and the Island is attached to it as a flush, black, bottom-rounded notch. Optional, reversible: auto-hide taskbar, Windows dark mode (originals persisted to `system-restore.json`, restored on exit). Full-screen apps hide the notch. Safe mode touches nothing in Windows. New: `AppBarService`, `TopBarWindow`, `TaskbarService`, `WindowsThemeService`, `SystemChangesService`, `NotchLayout` (+ tests), TEST_PLAN §R. **Written without a compiler; needs a first `dotnet build` on Windows.**
- **2026-10-24 (v2.0.1)** — Fixed `CS0234` (wrong `using` for `DoubleAnimation`) reported from the first Windows build; audited all `using` directives.
- **2026-10-24 (v2.0 RC)** — Phase 2/3: media controls, system status, battery, opt-in weather, opt-in clipboard history, layout profiles, backup/restore (+ automatic `.bak`), per-monitor placement, opaque fallback, consent dialogs. Removed the unused `AutoExpand` setting. Moved this file to `docs/`. Extended settings, tests (26 → 65 cases), test plan (K–Q), limitations, README. Fixed two compile errors found in review.
- **2026-10-24 (v1.0 RC)** — Settings window, presets, dock reorder + edge-reveal, hidden-until-invoked Island, notices, tray icon, start-with-Windows, single instance, installer, publish script, CI, docs.
- **2026-10-23** — Initial scaffold.
