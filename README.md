<p align="center">
  <img src="assets/deskmux-mark.svg" alt="DeskMux logo" width="120">
</p>

<h1 align="center">DeskMux</h1>

<p align="center">
  <strong>tmux-inspired sessions and panes for native Windows GUI applications.</strong>
</p>

<p align="center">
  <a href="https://github.com/kurtianbernaldez/DeskMux/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/kurtianbernaldez/DeskMux?label=release"></a>
  <a href="https://github.com/kurtianbernaldez/DeskMux/actions/workflows/ci.yml"><img alt="CI status" src="https://img.shields.io/github/actions/workflow/status/kurtianbernaldez/DeskMux/ci.yml?branch=main&label=CI"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/github/license/kurtianbernaldez/DeskMux"></a>
  <img alt="Windows 11 x64" src="https://img.shields.io/badge/platform-Windows%2011%20x64-0078D4">
</p>

<p align="center">
  <a href="https://deskmux.kurtian.dev">Website</a> ·
  <a href="https://github.com/kurtianbernaldez/DeskMux/releases/latest">Download</a> ·
  <a href="https://github.com/kurtianbernaldez/DeskMux/issues">Issues</a>
</p>

DeskMux turns ordinary Windows application **windows** into keyboard-driven workspaces. Group windows into named sessions, arrange them in nested panes, switch between workspaces, resize and swap panes, save layouts, and restore applications later.

DeskMux manages ordinary top-level windows with documented Windows APIs. It does **not** embed applications, re-parent their HWNDs, or depend on undocumented Virtual Desktop APIs. Applications keep running normally, and separate windows from the same process can belong to different sessions.

## Highlights

- **tmux-style sessions for GUI apps** — switch between named workspaces using a configurable keyboard prefix.
- **Nested panes** — split left/right or top/bottom, navigate, resize, swap, zoom, release panes to floating mode, and undo layout changes.
- **Saved layouts and app restore** — reconnect running windows or relaunch missing applications into remembered pane positions.
- **Multi-monitor aware** — maintain separate pane canvases per display and recover safely from monitor, work-area, resolution, and DPI changes.
- **Keyboard-first, but not keyboard-only** — use the prefix commands for fast navigation and the manager UI for sessions, launchers, hotkeys, behavior, appearance, and recovery.
- **Built for recoverability** — emergency show-all, crash recovery journals, transactional layout changes, and conservative window matching help avoid stranded hidden windows.
- **Local-first** — sessions, settings, recovery data, and logs stay on the machine; DeskMux does not require a cloud service.

## Install

Download **DeskMux-Setup-x64.exe** from the [latest release](https://github.com/kurtianbernaldez/DeskMux/releases/latest).

The installer is per-user, does not require administrator rights, and can create optional desktop and sign-in shortcuts. Upgrades preserve sessions and settings.

A **portable ZIP** is also available. Extract the complete folder and run `DeskMux.exe`. Portable mode stores sessions, settings, recovery data, and logs beside the application in its `Data` folder.

Release pages include `SHA256SUMS.txt` for download verification. Early builds may be unsigned until a Windows code-signing certificate is configured, so Windows SmartScreen can ask for confirmation.

## Quick start

1. Launch DeskMux. The session manager opens and a tray icon remains available.
2. Keep the initial session or rename it.
3. Focus an application window, press **Ctrl+B**, release it, then press **A** to add the window.
4. Press **Ctrl+B**, release it, then press **\\** or **|** to split to the right and choose a window.
5. Use **-** or **\"** for a pane below.
6. Create another session with **Ctrl+B → C** and switch directly with **Ctrl+B → 1**, **2**, and so on.
7. Add executables under **Manager → Launchers** if you want DeskMux to launch missing applications during pane creation or restore.

Only windows you explicitly assign to DeskMux are controlled. Unmanaged windows remain visible and behave normally.

## Keyboard commands

The default prefix is **Ctrl+B**. Press and release the prefix, then press the command key. A small overlay appears near the top center of the foreground monitor.

| After the prefix | Action |
| --- | --- |
| **1–9** | Switch directly to session 1–9 |
| **J / K** | Next / previous session |
| **W** | Open the keyboard session picker |
| **C** | Create a session |
| **R** | Rename the current session |
| **M** | Move the focused window to another session |
| **A** | Add the focused window |
| **\\** or **&#124;** | Split left/right |
| **-** or **\"** | Split top/bottom |
| **Arrow keys** | Focus the neighboring pane |
| **Ctrl+Arrow keys** | Resize the nearest matching split |
| **{ / }** | Swap with the previous / next pane |
| **Z** | Zoom / restore the focused pane |
| **F** | Release the focused pane to floating mode |
| **U** | Undo the previous pane layout change |
| **X** | Remove the focused window from its pane and session |
| **D** | Detach the current session |
| **L** | Switch to the previously active session |
| **S** | Open the session manager |
| **Escape** | Cancel command mode |

Hotkeys, the prefix, modifiers, and timeout can be changed from the **Hotkeys** page. Picker navigation uses arrows or J/K, Enter, number shortcuts, and Escape.

## Panes, sessions, and layouts

A **session** is a workspace. A **floating window** belongs to a session but remains freely movable. A **pane** belongs to a tiled tree managed within one monitor's work area.

Each split divides only the currently focused pane, so layouts can be nested rather than limited to a fixed grid. DeskMux can:

- split panes horizontally or vertically;
- resize shared dividers;
- navigate using pane geometry;
- swap windows;
- zoom one pane;
- drag a pane onto another to swap;
- release a pane back to floating mode;
- save named layouts;
- restore missing applications into saved layouts;
- undo recent layout changes during the current run.

A session can have one pane canvas per physical monitor alongside floating windows. DeskMux tracks monitor identity, work area, coordinates, and DPI, and keeps windows reachable when displays change.

## Application launchers and restore

Under **Manager → Launchers**, add an executable with an optional display name, arguments, working directory, and expected process name.

When a launcher is used, DeskMux waits for an eligible top-level window rather than blindly grabbing the foreground application. Ambiguous matches require an explicit choice.

**Restore apps** reconnects exact running matches first, launches missing applications when a launcher or executable fallback is available, and reapplies saved pane geometry. DeskMux intentionally prefers a missing window over attaching the wrong one when matching is ambiguous.

Application documents, browser tabs, and unsaved work remain the responsibility of the application itself.

## Recovery

Press **Ctrl+Alt+Shift+F12** to immediately show all live managed windows and pause session hiding. The same command is available from the tray as **Show All Managed Windows**.

You can also request recovery from PowerShell:

```powershell
.\artifacts\DeskMux-win-x64\DeskMux.exe --recover
```

Before hiding a window, DeskMux writes a recovery journal. A lightweight companion process watches the manager process and restores journaled windows if the manager exits unexpectedly. DeskMux also checks for stale recovery data on startup.

Normal **Exit DeskMux** shows all managed windows before the process exits.

## Data and privacy

Normal installations store data under:

```text
%LOCALAPPDATA%\DeskMux
```

| File | Purpose |
| --- | --- |
| `sessions.json` | Sessions, launchers, settings, layouts, window fingerprints, and focus history |
| `sessions.json.bak` | Last valid backup |
| `recovery.json` | Recovery information for hidden windows |
| `logs\deskmux-YYYYMMDD.jsonl` | Local structured logs |

Portable builds store the same data under the adjacent `Data` folder.

Window titles and executable paths can appear in session data and diagnostic logs. DeskMux does not send these files to a service.

## Requirements and limitations

DeskMux is a **C#/.NET 10 WPF** application built primarily for **Windows 11 x64**, with Windows 10 compatibility where the underlying Windows APIs and .NET runtime support it.

Current limitations include:

- Windows can prevent a normal process from inspecting, moving, hiding, or focusing elevated applications.
- UAC secure desktop, sign-in/security UI, desktop/taskbar windows, DeskMux itself, and excluded system processes are not managed.
- Some applications create popups, modal dialogs, replacement HWNDs, or custom windows that require special handling.
- Applications can impose their own minimum size, placement, and foreground-activation restrictions.
- Monitor names can change after driver, docking, or hardware changes.
- Complete Start Menu discovery and cloud synchronization are not included.
- Hiding a window does not pause its CPU, networking, audio, notifications, or background work.

For the best behavior, run DeskMux and the applications it manages at the same ordinary privilege level.

## Build and develop

Install the **.NET 10 SDK for Windows x64**, then open PowerShell in the repository:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

To run the native integration checks on an **unlocked interactive Windows desktop**:

```powershell
.\scripts\test.ps1 -Integration
```

To create a self-contained x64 package:

```powershell
.\scripts\package.ps1
```

To create the portable ZIP, checksums, and—when Inno Setup 6 is installed—the installer:

```powershell
.\scripts\build-release.ps1
```

Equivalent SDK commands use the repository's `.slnx` solution:

```powershell
dotnet restore DeskMux.slnx
dotnet build DeskMux.slnx -c Release --no-restore
dotnet run --project tests/DeskMux.Tests -c Release
dotnet run --project src/DeskMux.App -c Release
dotnet publish src/DeskMux.App -c Release -r win-x64 --self-contained true -o artifacts/DeskMux-win-x64
```

The build scripts use `.tools\dotnet\dotnet.exe` when present, otherwise the `dotnet` SDK on `PATH`. No third-party NuGet packages are required.

## Architecture

| Project | Responsibility |
| --- | --- |
| `src/DeskMux.Core` | Sessions, pane trees, geometry, navigation, matching, monitor mapping, persistence, and logging |
| `src/DeskMux.Windows` | Win32 interop, window placement/visibility/focus, launch coordination, keyboard and window hooks, monitor discovery, and recovery |
| `src/DeskMux.App` | WPF lifetime, tray UI, command overlay, pickers, launcher editor, and manager pages |
| `tests/DeskMux.Tests` | Dependency-free executable checks of core behavior |
| `tests/DeskMux.Integration` | Real Windows integration checks using isolated WPF fixture windows |

The core logic is kept separate from WPF and Win32 where possible so pane geometry, matching, persistence, and session behavior can be validated without manipulating real desktop windows.

## Releases

Pushes and pull requests run the application checks. Tags beginning with `v` build the Windows installer, portable ZIP, update ZIP, checksums, and GitHub Release.

Example:

```powershell
git tag v0.1.0-alpha.1
git push origin v0.1.0-alpha.1
```

If `WINDOWS_CERTIFICATE_BASE64` and `WINDOWS_CERTIFICATE_PASSWORD` are configured in GitHub Actions, the release workflow signs the application and installer before publishing.

The landing page is maintained separately in [kurtianbernaldez/deskmux-website](https://github.com/kurtianbernaldez/deskmux-website) and deployed at [deskmux.kurtian.dev](https://deskmux.kurtian.dev).

## Contributing and security

Contributions and bug reports are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for development guidance and [SECURITY.md](SECURITY.md) for private vulnerability reporting.

If you report a bug, include the DeskMux version, Windows version, applications involved, monitor layout/scaling, reproduction steps, and expected versus actual behavior. Remove private window titles and paths from logs before attaching them publicly.

## Roadmap

The current architecture leaves room for a CLI, title/process rules, templates, startup scripts, alternate configuration formats, and plugins. Native windows, predictable pane layouts, and safe recovery remain the foundation.

## License

DeskMux is released under the [MIT License](LICENSE).
