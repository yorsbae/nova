# NOVA — Vision

> **Status:** Draft v1.0  
> **Product type:** Windows desktop companion  
> **Working name:** NOVA  
> **Primary goal:** Improve desktop accessibility and everyday workflow through a minimalist interface, an adaptive Dynamic Island-style control, and a configurable floating app dock.

---

## 1. Product Vision

NOVA is a lightweight Windows desktop companion that makes common actions easier to reach without turning the desktop into a cluttered dashboard.

It combines three main elements:

1. **Adaptive Island** — a compact, expandable pill for contextual information and quick controls.
2. **Desktop Clock** — a restrained clock and date display.
3. **Floating Dock** — a configurable shortcut dock for frequently used applications.

The experience should feel integrated with Windows, remain unobtrusive during focused work, and support keyboard, mouse, screen-reader, display-scaling, and reduced-motion use cases.

NOVA is a desktop customization application, **not a replacement for Windows Explorer or the Windows shell**.

## 2. Problem Statement

Windows provides many useful features, but frequent actions can be spread across the taskbar, system tray, Settings, individual applications, and notification surfaces. Heavy customization tools can also introduce unnecessary complexity, inconsistent styling, resource use, or breakage after Windows updates.

NOVA addresses this by providing a coherent and configurable access layer for a small set of high-value actions, while preserving standard Windows functionality and user control.

## 3. Product Principles

### 3.1 Accessibility before decoration
A control is not successful merely because it looks minimal. It must remain discoverable, readable, keyboard-accessible, and understandable to assistive technology.

### 3.2 Minimal by default, configurable when needed
Show only the information relevant to the current state. Keep advanced options available without exposing them constantly.

### 3.3 Contextual, not noisy
The Island may expand for a relevant event or explicit interaction. It must not repeatedly animate, steal focus, or cover important content.

### 3.4 Windows remains in control
NOVA must not disable core Windows functions or require replacing the Windows shell. If a Windows feature has no stable supported integration, provide a safe fallback or defer it.

### 3.5 Privacy by default
Prefer local processing and local configuration. Network access should only be used for an explicitly enabled feature, such as weather. Do not collect analytics or telemetry by default.

### 3.6 Recoverable and reversible
Every customization should be reversible. NOVA must provide safe defaults, a reset option, and a clean uninstall path.

### 3.7 Resource-conscious
NOVA should be suitable for everyday use on a typical Windows PC. Avoid unnecessary polling, large background services, and excessive animation.

## 4. Target Users and Use Cases

Primary users are Windows users who want a clean desktop and faster access to frequently used tools, including users who benefit from larger controls, clear focus indicators, keyboard navigation, reduced motion, or screen-reader support.

Representative use cases:

- Check the time and date without opening an application.
- Open a frequently used app from a dock.
- Expand the Island to control supported media playback.
- Open a selected Windows Settings page using a shortcut.
- Inspect basic system status without leaving the current task.
- Reduce animation or increase contrast when needed.
- Restore the default layout after experimentation or troubleshooting.

## 5. Scope

### 5.1 MVP — Required

- Windows desktop application that runs in the background.
- Adaptive Island with compact and expanded states.
- Clock and date display with 12-hour/24-hour format options.
- Floating dock with configurable app shortcuts.
- Dark and light themes, plus a system-theme option where practical.
- Settings for position, size, spacing, and startup behavior.
- Global keyboard shortcut to show/hide or expand the Island, configurable by the user.
- Keyboard navigation, visible focus indicators, accessible control names, and screen-reader-friendly semantics.
- Reduced-motion setting.
- High-contrast/readability options.
- JSON-based local configuration with safe defaults.
- Reset-to-default option and clean application exit.
- Installer and uninstaller for the MVP.

### 5.2 Later phases

- Media controls using supported Windows media-session APIs.
- Notifications, subject to Windows notification permissions and API limitations.
- Optional system metrics such as CPU and memory usage.
- Battery status on devices that expose battery information.
- Optional weather integration, requiring network access and clear configuration.
- Optional clipboard history only after explicit opt-in, with privacy safeguards.
- Per-monitor positioning and additional layout profiles.
- Backup and restore of user settings.

### 5.3 Explicitly out of scope for the MVP

- Replacing Explorer, Start, or the Windows shell.
- Recreating every Windows taskbar or system-tray feature.
- Reading or controlling arbitrary third-party applications without supported interfaces.
- Collecting usage analytics or uploading user activity.
- Bundling opaque third-party customization tools without disclosure.
- Guaranteeing compatibility with every Windows build before testing.
- Requiring administrator privileges for normal operation unless a clearly documented feature genuinely needs them.

## 6. Core Experience

### 6.1 Adaptive Island states

**Compact**
- Small pill with minimal information.
- Does not obscure application content unnecessarily.
- Remains in a consistent, configurable position.

**Expanded**
- Opens from a click or keyboard shortcut.
- Shows the currently available actions or information.
- Closes with Escape, an outside click where appropriate, or a second shortcut.
- Must not unexpectedly take keyboard focus when the user is typing in another app.

**Temporary notification**
- Briefly shows a supported event.
- Does not steal focus.
- Respects notification and reduced-motion preferences.
- Has a configurable or sensible display duration.

The MVP may implement the visual states before integrating system media or notification sources. Unsupported integrations must not be simulated as if they were real.

### 6.2 Floating Dock

- Holds user-selected application shortcuts.
- Supports adding, removing, and reordering shortcuts.
- Displays a tooltip or accessible name for each icon.
- Supports mouse and keyboard operation.
- Has adjustable icon size, spacing, position, and visibility behavior.
- Launches applications using validated user-selected paths or shortcuts.
- Does not replace the Windows taskbar; users may keep both.

### 6.3 Clock

- Displays local system time and date.
- Supports 12-hour and 24-hour formats.
- Provides adjustable font size and position.
- Uses readable contrast and does not rely on color alone.
- Updates efficiently rather than running unnecessary high-frequency work.

### 6.4 Themes and layout

- Dark, light, and system-following themes where feasible.
- Optional accent color.
- Adjustable opacity only within readable limits.
- Presets such as **Minimal**, **Balanced**, and **Custom**.
- A one-click reset restores a known-good layout.

## 7. Accessibility Requirements

Accessibility is a product requirement, not a finishing task.

- All primary actions must be operable without a mouse.
- Keyboard focus must be visible and follow a predictable order.
- Controls must expose meaningful names, roles, and states to UI Automation and screen readers.
- Text and icons must maintain adequate contrast; do not communicate state using color alone.
- UI must remain usable at common Windows scaling levels, including 125%, 150%, and 200%.
- Reduced-motion mode must remove or simplify nonessential transitions.
- The Island and dock must not trap focus.
- Tooltips must not be the only way to understand an icon.
- Essential actions must have text labels or accessible names.
- Settings must allow the user to disable auto-expansion, temporary notices, and other potentially distracting behavior.

Accessibility conformance must be tested with Windows Narrator, keyboard-only navigation, Windows high-contrast settings, and common display scaling values. Do not claim full accessibility compliance until tested.

## 8. Technical Direction

### Proposed stack

- **Language:** C#
- **UI framework:** WPF
- **Target runtime:** .NET 8 LTS for the initial prototype, with the target framework reviewed before release.
- **Windows integration:** supported Windows APIs and documented interfaces where available.
- **Configuration:** JSON stored in the user's local application-data directory.
- **Packaging:** self-contained or framework-dependent Windows installer, selected after evaluating package size and deployment needs.
- **Tests:** unit tests for configuration and logic, plus manual UI/accessibility and Windows integration tests.

WPF is proposed because it supports custom windows, styling, and animations with a mature .NET ecosystem. The team must verify transparent-window behavior, focus management, DPI scaling, multi-monitor positioning, and startup behavior on the actual target Windows versions.

Do not use undocumented or fragile taskbar hacks as a core dependency. Keep OS-specific integrations behind interfaces so that they can be replaced or disabled.

### Suggested solution boundaries

- `Core`: configuration, settings validation, events, logging.
- `Features/DynamicIsland`: compact/expanded/temporary states.
- `Features/DesktopClock`: clock and date.
- `Features/FloatingDock`: shortcut management and launching.
- `Features/Media`: later Windows media-session integration.
- `Features/Notifications`: later notification integration.
- `Features/SystemStatus`: later system metrics.
- `Integrations/Windows`: Windows-specific API wrappers.
- `Settings`: settings UI and profile management.
- `Themes`: shared colors, typography, spacing, and control styles.
- `Tests`: logic, configuration, and integration tests.

This is a conceptual structure, not a requirement to create every folder before implementing the MVP.

## 9. Performance, Stability, and Safety

- Start with the minimum features required for the MVP.
- Avoid tight polling loops; use events or reasonable update intervals.
- Do not elevate privileges by default.
- Do not inject code into other processes.
- Do not interfere with security software or Windows updates.
- Validate shortcut targets before launching them.
- Handle missing files, invalid configuration, and API failures without crashing the entire application.
- If a feature fails, disable or degrade that feature gracefully while keeping the clock and dock usable.
- Keep logs local and avoid storing sensitive content.
- Provide a safe mode or a way to start with custom overlays disabled if the normal layout becomes unusable.

## 10. Success Metrics

The MVP is successful when:

1. The user can launch a selected application from the dock with one click or keyboard interaction.
2. The clock and Island remain legible at 100%, 125%, 150%, and 200% display scaling.
3. Core interactions work without a mouse.
4. Reduced-motion mode disables nonessential animation.
5. Settings persist across restarts and invalid settings recover safely.
6. The application does not repeatedly steal focus from other apps.
7. Closing or uninstalling NOVA does not damage the Windows taskbar or shell.
8. The app remains usable if an optional integration is unavailable.
9. Startup, idle resource use, and responsiveness are measured on the target PC before release; numeric performance budgets are set after a baseline prototype rather than invented in advance.

## 11. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Windows updates change taskbar behavior | Do not depend on undocumented taskbar modifications for core features |
| Overlay steals focus | Use explicit window styles and test focus behavior across applications |
| Transparent windows behave differently across GPUs or builds | Test target configurations and provide an opaque fallback |
| Dynamic Island becomes distracting | Provide compact mode, reduced motion, and notification controls |
| Screen-reader support is incomplete | Test early with Narrator and UI Automation |
| Multi-monitor or DPI positioning errors | Store logical positions carefully and test monitor changes |
| Optional integrations fail | Isolate integrations and degrade gracefully |
| Too many features delay delivery | Keep the MVP limited to Island, clock, dock, settings, and accessibility |

## 12. Delivery Plan

### Phase 0 — Technical spike
- Confirm target Windows versions.
- Prototype a transparent top-level WPF window.
- Test always-on-top behavior, focus handling, DPI scaling, and multi-monitor placement.
- Validate global shortcut behavior and conflict handling.

### Phase 1 — MVP
- Implement compact/expanded Island.
- Implement clock/date.
- Implement floating dock.
- Implement settings, themes, keyboard operation, and reduced motion.
- Add configuration persistence and reset.
- Package and test the installer.

### Phase 2 — Supported integrations
- Add media controls.
- Add notification integration if reliable and permitted.
- Add basic system metrics.
- Add optional weather only with explicit network configuration.

### Phase 3 — Refinement
- Add layout profiles and backup/restore.
- Improve multi-monitor behavior.
- Run accessibility, reliability, and performance testing.
- Document known limitations.

## 13. Open Decisions

The following decisions must be finalized before implementation:

- Supported Windows versions: Windows 11 only, or Windows 10 and 11.
- Island behavior: always compact, hidden until invoked, or adaptive.
- Dock behavior: always visible, reveal on edge hover, or user-selectable.
- Default Island and dock positions.
- Whether the MVP must work fully offline.
- Installer packaging choice.
- Whether the user wants a portable build in addition to an installer.

Until these are decided, implement configurable defaults and avoid hard-coding assumptions into the architecture.

## 14. Definition of Done for the MVP

The MVP is done only when:

- The app launches and exits cleanly.
- The Island supports compact and expanded states.
- Clock/date and dock function correctly.
- Core controls work with keyboard and mouse.
- Focus is not unexpectedly stolen from other apps.
- Themes, positions, and settings persist.
- Reduced-motion and readable-contrast options work.
- Invalid configuration does not crash startup.
- Installer and uninstaller are tested.
- Limitations and supported Windows versions are documented.

---

**Guiding principle:** NOVA should make Windows easier to use, not create another layer of complexity. If a feature adds visual polish but harms focus, accessibility, stability, or performance, it should not ship in the MVP.
