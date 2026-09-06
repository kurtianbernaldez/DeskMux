# DeskMux

**tmux-inspired sessions and panes for native Windows GUI applications.**

[Website](https://deskmux.kurtian.dev) · [Downloads](https://github.com/kurtian/DeskMux/releases) · [Issues](https://github.com/kurtian/DeskMux/issues)

DeskMux groups native application **windows** into named sessions. Arrange them in repeated horizontal and vertical splits, navigate between panes, resize, swap, and zoom with a short keyboard prefix. Fill panes with running windows or configured application launchers. Applications keep running; their windows return with their saved pane layout and focus history. Two Chrome windows can belong to different sessions even when they share a process.

DeskMux is a C#/.NET 10 WPF tray application for Windows 11 x64, with Windows 10 compatibility where the underlying Windows APIs and .NET runtime support it. It positions ordinary top-level windows with documented Windows APIs. It does not embed applications, change their parent HWND, or use undocumented Virtual Desktop APIs.

## Install

Download **DeskMux-Setup-x64.exe** from the [latest GitHub release](https://github.com/kurtian/DeskMux/releases/latest). The per-user installer does not require administrator rights and can create optional desktop and sign-in shortcuts. Upgrades preserve sessions and settings. Uninstalling asks before deleting local DeskMux data.

The **portable ZIP** is for USB drives, test copies, and users who prefer no installation. Extract the complete ZIP and run `DeskMux.exe`. Its `portable.mode` marker makes DeskMux store sessions, settings, recovery data, and logs in the adjacent `Data` folder. Move or back up the whole folder together. Removing the marker returns it to normal `%LOCALAPPDATA%\DeskMux` storage; `--portable` enables the same behavior from any development package.

DeskMux is distributed through GitHub rather than the Microsoft Store. Release pages include `SHA256SUMS.txt` for download verification. Early builds may be unsigned until a Windows code-signing certificate is configured, so Windows SmartScreen can ask for confirmation.

## Build and run

Install the **.NET 10 SDK for Windows x64**, then open PowerShell in the repository:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

The scripts use `.tools\dotnet\dotnet.exe` when present, otherwise the `dotnet` SDK on `PATH`. No third-party NuGet packages are required. A local SDK is a development convenience and is not part of the distribution.

To produce a self-contained x64 package, which includes the runtime:

```powershell
.\scripts\package.ps1
.\artifacts\DeskMux-win-x64\DeskMux.exe
```

To create the portable ZIP, checksums, and—when Inno Setup 6 is installed—the installer:

```powershell
.\scripts\build-release.ps1
```

Release output is written to `artifacts\release`. The installer includes optional sign-in startup registration. GitHub’s tagged-release workflow builds the same assets and publishes them automatically. If repository secrets `WINDOWS_CERTIFICATE_BASE64` and `WINDOWS_CERTIFICATE_PASSWORD` are configured, it signs the application and installer before publishing.

Equivalent SDK commands:

```powershell
dotnet restore DeskMux.sln
dotnet build DeskMux.sln -c Release --no-restore
dotnet run --project tests/DeskMux.Tests -c Release
dotnet run --project src/DeskMux.App -c Release
dotnet publish src/DeskMux.App -c Release -r win-x64 --self-contained true -o artifacts/DeskMux-win-x64
```

## First workspace

1. Launch DeskMux. The session manager opens, and a tray icon stays available.
2. Keep the initial DEV session or rename it. Focus an application window and press **Ctrl+B**, release, then **A** to add that window.
3. Press **Ctrl+B**, release, then **\\** or **|** to open a pane to the right. Choose a running window with arrows and Enter. Use **-** or **\"** for a pane below. Repeat on any focused pane for nested splits.
4. Add application executables on **Manager → Launchers**. They appear in the picker's **Launch new** section. DeskMux waits for an eligible window before changing the layout.
5. Create another session with **Ctrl+B → C**, add its applications, and press **Ctrl+B → 1** or **2** to switch. Unmanaged windows remain visible; only windows you assigned to sessions are controlled.

The session manager provides Sessions, Launchers, Hotkeys, Behavior, Appearance, and About pages. Use it to create, rename, reorder, delete, switch or restore sessions, inspect pane status, focus panes, release panes to floating mode, reapply layouts, capture windows, move entries, or remove membership. **Restore apps** reconnects windows already running, opens missing applications, and reapplies the saved pane tree. Pane details include position, split ancestry, zoom state, monitor, and calculated rectangle. Deleting a session offers moving its windows to the current session when possible or showing them and leaving them unmanaged. It never closes the applications.

Closing the manager leaves the tray process and shortcuts running. To stop DeskMux, choose **Exit DeskMux** in the tray menu; every live managed window is shown before exit.

## Keyboard commands

Press and release the prefix, then press a command key. The default prefix is **Ctrl+B**. A small overlay appears near the top center of the foreground monitor. Command mode times out after **1.8 seconds** by default; Escape cancels it. Configure the prefix, modifiers, and timeout on the Hotkeys page.

| After the prefix | Action |
| --- | --- |
| **1–9** | Switch directly to session 1–9 in list order |
| **J / K** | Next / previous session, wrapping around |
| **W** | Open the keyboard session picker, with the pane and floating applications in each session |
| **C** | Create a session |
| **R** | Rename the current session |
| **M** | Move the focused window to a chosen session as a floating member |
| **A** | Add the focused window; it joins an existing pane canvas on that monitor, otherwise it floats |
| **\\** or **&#124;** | Open pane picker; split left/right with the new pane on the right |
| **-** or **\"** | Open pane picker; split top/bottom with the new pane below |
| **Arrow keys** | Focus the neighboring pane in that direction |
| **Ctrl+Arrow keys** | Resize using the nearest matching split by approximately 5% |
| **{ / }** | Swap the focused application with the previous / next pane |
| **Z** | Zoom / restore the focused pane |
| **X** | Remove the focused window from its pane and session, without closing it |
| **D** | Detach the current session and return to the previous one when possible |
| **L** | Switch to the previously active session |
| **S** | Open the session manager |
| **Escape** | Cancel command mode |

In the session, move, and Open pane pickers, use **K / Up** to move up and **J / Down** to move down, **Enter** to select, **1–9** for quick selection, and **Escape** to cancel. Navigation is captured globally while a picker is open; no click is needed. Open pane has separate **Running windows** and **Launch new** sections. Every window is listed separately with its application, title, and session status. Session ordering determines session number shortcuts. **Prefix → L** switches to the last active session; **H** is unassigned. Arrows always operate on panes after the prefix; without a pane layout they show a concise notification.

The physical backslash/pipe key is recognized as `VK_OEM_5`. Second-key commands retain modifiers to distinguish Ctrl+Arrow and shifted symbols. Other keyboard layouts may place quote and brace symbols differently; the backslash and minus alternatives remain available.

## Panes and floating windows

A **session** is a workspace. A **floating window** is a member whose position can be changed manually. A **pane** is a member in a tiled tree. Each **pane canvas** uses one monitor's work area, excluding taskbars. A **split node** divides its rectangle vertically (left/right) or horizontally (top/bottom); a **leaf node** references exactly one managed window. A window can belong to only one session and one leaf.

The Open pane picker changes nothing until a window is selected and can be controlled. An unmanaged window joins the active session; an existing floating member becomes a pane. Selecting an existing pane focuses it without duplicating it. Selecting another session's window requests confirmation, then removes and collapses its old leaf before moving membership. Cancelling leaves all memberships and layouts unchanged.

If the focused window is a pane, it becomes the first half of the split. A floating member can be promoted and split. If there is no valid source member, the chosen application becomes a root pane on its monitor. DeskMux never silently captures an unrelated focused window as the source.

Adding a focused window with **A** automatically splits an existing pane canvas on that monitor so the new app is visible and the current panes make room. On a monitor without panes, it is added as a floating member.

Starting an interactive mouse move or resize releases that pane to floating mode, keeps it in the session, and lets the chosen placement persist. **Release to floating** in the manager does the same directly. **X / Remove from session** leaves the application visible and unmanaged. Other panes reflow while at least two remain; a lone survivor is released at its current rectangle so it stays movable. These operations never send an application a close command or terminate it.

Navigation uses pane rectangles and overlapping spans, so it works across nested splits. Resizing finds the nearest ancestor split matching the requested axis and respects minimum sizes. Swapping follows stable depth-first pane order and keeps focus on the same application after it moves. Zoom fills only the focused pane's monitor canvas and safely hides the other panes in that canvas. Pane navigation, splitting, resizing, and swapping restore the canvas from zoom before applying their operation. Other monitor canvases remain unaffected.

## Appearance and themes

Open **Manager → Appearance** to choose System, DeskMux Dark, Light, Dracula, Nord, Gruvbox, Solarized Dark, Solarized Light, or High Contrast. System follows the Windows app color preference and Windows contrast mode when DeskMux starts. Theme changes apply immediately to the manager, command overlay, session and pane pickers, progress windows, dialogs, buttons, lists, borders, and status colors.

The Custom theme editor accepts `#RRGGBB` colors for the background, surfaces, sidebar, primary and muted text, accent, borders, and errors. Its palette preview updates as valid values are entered. **Save and apply custom** stores the palette locally in `sessions.json`; **Copy selected preset into custom** provides a starting point for editing.

## Application launchers

On **Manager → Launchers**, choose **Add executable**, set a display name, and optionally provide arguments, a working directory, or an expected process name. Edit, reorder, or remove profiles there. Profiles are persisted locally and appear in **Launch new**. DeskMux starts the executable directly with `ProcessStartInfo`; it does not compose an interactive shell command. Start Menu discovery is not included.

Launch detection runs asynchronously for approximately 15 seconds using window show/foreground events and lightweight fallback enumeration. It prefers a new window from the launched process, then matching application identity, and can recognize a reused single-instance window brought to the foreground. If several windows remain plausible, a chooser requires an explicit selection. DeskMux excludes its own windows, protected applications, child/tool/shell windows, zero-sized windows, and transient splash candidates when a stable main window appears.

Failed launch, timeout, or cancellation leaves the source layout unchanged. A successfully started application stays running even if no pane can be created. Cancel an in-progress pane request with its **Cancel pane request** button. Exiting DeskMux cancels detection and restores safely hidden windows.

Applications opened through a configured launcher remember that launcher, including its arguments and working directory. Other managed windows use their recorded executable path as a restart fallback. Choose **Restore apps** on a session to reconnect exact running matches first and then open its missing applications one at a time. Ambiguous windows require an explicit choice. Enable **Behavior → Restore missing applications in the active session when DeskMux starts** for automatic restore; it is off by default.

DeskMux uses a single low-level keyboard hook. While enabled, the configured prefix is intercepted globally and its command key is consumed. This means an application's normal Ctrl+B action is unavailable with the default prefix. Choose another prefix or **Pause Keyboard Shortcuts** in the tray when needed. The prefix overlay does not take focus from the application whose window you are adding or moving. Auto-repeat and command timeout are handled by the keyboard service.

## Recovery and exit

**Ctrl+Alt+Shift+F12** immediately shows all live managed windows and pauses session hiding. The same **Show All Managed Windows** command is in the tray. Choose **Resume sessions** when you are ready to manage visibility again. Keyboard pausing does not disable emergency recovery.

You can also request recovery from another PowerShell window:

```powershell
.\artifacts\DeskMux-win-x64\DeskMux.exe --recover
```

This signals an existing DeskMux instance for that data directory, or starts DeskMux with all managed windows shown and hiding paused. **Exit DeskMux** also shows managed windows, saves state, and removes the keyboard and window event hooks.

Before hiding a window, DeskMux writes an independent recovery journal. A lightweight companion process waits for the manager process to exit. If the manager crashes or is terminated, the companion restores journaled windows. On startup, DeskMux also checks for a journal left by a dead manager. Recovery checks HWND, process ID, process start time, and window class before touching a window, preventing a recycled handle from being mistaken for the original window.

If both processes are terminated, launch DeskMux with `--recover`. Keep the data directory intact until recovery finishes. Recovery cannot override secure-desktop restrictions or an application that refuses window operations; these failures are logged and the journal is retained for another attempt.

## Sessions and persistence

Data is stored locally in `%LOCALAPPDATA%\DeskMux`:

| File | Purpose |
| --- | --- |
| `sessions.json` | Ordered sessions, launch profiles, appearance settings, pane canvases/trees, zoom state, window fingerprints, layouts and focus history |
| `sessions.json.bak` | Last valid backup for damaged-file recovery |
| `recovery.json` | Durable information about windows hidden by the manager |
| `logs\deskmux-YYYYMMDD.jsonl` | Structured local event and error log |

Session changes are saved locally, and event-driven layout tracking debounces disk writes. Switching away captures the current placement before hiding windows. Saves use a temporary file and atomic replacement; malformed session files are preserved, and the valid backup is tried. Logs are limited in size and old daily logs are removed after 14 days. Settings provides a button to open the log directory. Window titles and executable paths may appear in session data and diagnostic records; none of these files is sent to a service.

State version 3 adds restart links between managed windows and launch profiles, the optional startup restore setting, and persisted appearance themes. Earlier state files migrate automatically. Loaded pane trees are validated to remove cycles, orphan references, and duplicates. Missing application leaves remain in the saved tree so they can return to their exact panes, while the live layout temporarily collapses those leaves and does not reserve empty space.

To use an isolated data directory for development:

```powershell
.\artifacts\DeskMux-win-x64\DeskMux.exe --data-dir C:\DeskMux\artifacts\my-test-profile
.\artifacts\DeskMux-win-x64\DeskMux.exe --data-dir C:\DeskMux\artifacts\my-test-profile --recover
```

A second launch with the same data directory activates the existing instance. Distinct data directories are separate instances; avoid assigning the same application window to multiple instances.

The portable edition stores the same files under its adjacent `Data` folder. DeskMux does not transfer application documents; restoring a session reopens applications from saved launcher or executable details, while each application remains responsible for recovering its own tabs and files.

Each membership entry is one **HWND**, with its process metadata and layout. An unchanged running window is identified by handle, process ID/start time, executable identity, and class. When handles have changed, reassociation requires a unique match using application identity, class, and exact saved title. Ambiguous matches remain **missing** instead of grabbing another window. Restore can launch missing applications from a linked profile or executable fallback and asks you to choose when several windows match. Browser tabs and unsaved documents still depend on the application's own recovery behavior.

## Multi-monitor layout

A session can have one pane canvas per physical monitor, alongside floating windows. Pane calculation uses physical pixels, supports negative coordinates, divides odd pixel counts deterministically, and respects minimum dimensions. It preserves monitor device/stable identity, work area, and DPI. Display, work-area, resolution, and DPI changes remap and recalculate visible canvases. If a missing monitor would cause multiple trees to occupy the same remaining display, DeskMux releases the affected windows to reachable floating layouts and reports it instead of stacking trees.

Floating layouts continue to preserve restored, maximized, or minimized state and use the existing monitor mapper. Multi-window pane changes save the previous tree and rectangles before applying the complete layout. Placement failure rolls back moved windows when possible; recovery failures are reported and applications are kept reachable.

Focus history is tracked per window. Switching back attempts to focus the most recently used available, non-minimized member. Windows can deny foreground activation; in that case switching still restores the windows and records the focus failure.

## Architecture

| Project | Responsibility |
| --- | --- |
| `src/DeskMux.Core` | Session ownership, pane validation/tree operations/geometry/navigation/resize, launch candidate rules, switching, conservative matching, monitor mapping, persistence and logging |
| `src/DeskMux.Windows` | Documented Win32 interop, safe placement/visibility/focus, asynchronous launch coordination, keyboard gestures and window event hooks, monitor discovery and crash recovery |
| `src/DeskMux.App` | WPF lifetime, tray menu, command overlay, session/open-pane pickers, launch progress/ambiguity UI, launcher editor and manager pages |
| `tests/DeskMux.Tests` | Dependency-free executable checks of core behavior with fake window services |
| `tests/DeskMux.Integration` | Real Windows checks using native WPF fixture windows in a separate process |

`SessionManager` depends on `IWindowSystem`, `IStateStore`, and `ILog`; it calculates and transactionally applies whole pane trees. Native placement is exposed through the window abstraction. Pure tree and geometry calculations have no WPF or Win32 dependency. `WindowMatcher` handles identity confidence and `MonitorMapper` handles display changes. `AppController` coordinates pickers and debounced updates. The app uses window events, with limited enumeration only while waiting for a launched application.

## Validation

Run the core checks with `scripts\test.ps1`. Run both core and native integration checks on an **unlocked interactive Windows desktop**:

```powershell
.\scripts\test.ps1 -Integration
```

The native suite creates three identifiable WPF windows in one separate process. It manages only those fixture HWNDs, checks cross-session ownership, real hide/show, process survival, normal/maximized/minimized layouts, transfer, closed-window cleanup, emergency recovery, and recovery after deliberately terminating an isolated test manager. Its fixture windows close when the suite finishes. It does not enumerate and capture the user's unrelated applications into sessions.

The unit suite covers tree validation, splitting/collapse, geometry, navigation, resizing, swapping, zoom, transactional failures, running-window attachment, launch candidate selection, migration, session ownership, recovery, and monitor mapping. These executable suites return a failing exit code on assertion failure; no external test framework is needed.

Manual acceptance requires an unlocked Windows desktop and actual applications. Automated fixture checks cannot prove compatibility with every application's custom behavior. Run this checklist with Notepad, File Explorer, Windows Terminal, a Chromium browser, a slow-starting application, a single-instance application, an elevated application, and multiple windows from one process:

- Create sessions with C; split right/below repeatedly, including nested mixed orientations.
- Select unmanaged and floating windows, select an existing pane, cancel the picker, and cancel/confirm a move from another session.
- Configure launchers, launch a new application, reuse a single-instance window, and exercise slow start, ambiguous candidates, timeout, and cancellation.
- Navigate all four directions, resize both axes, swap in both directions, and zoom/unzoom.
- Remove a pane without closing its application; remove the final pane; move a pane with M and verify the destination member is floating.
- Drag or resize a pane and verify it becomes floating and the manual placement persists; use Reapply layout only for panes still in a canvas.
- Switch sessions while zoomed, restart DeskMux, use Restore apps, and verify relaunched windows return to their pane geometry and focus history.
- Use two monitors with negative coordinates and mixed DPI; move taskbars to different edges; disconnect/reconnect a monitor and verify every window remains reachable.
- Exercise foreground denial and elevated windows; confirm failed operations preserve membership/layout and leave applications running.
- Trigger emergency recovery, terminate an isolated test instance while zoomed, exit normally while zoomed, and exit during launch detection. Verify no application is stranded hidden.

## Permissions and current limitations

- DeskMux runs without administrator rights. Windows may prevent a normal process from inspecting, hiding, moving, or focusing an elevated application. Failures are surfaced as window status, notifications, and logs. Prefer running the applications and DeskMux at the same ordinary privilege level.
- UAC secure desktop, sign-in/security UI, desktop/taskbar windows, DeskMux itself, and excluded system processes are not managed. Configure additional excluded process names on the Behavior page. Exclusions apply to individual candidate windows through their owning process.
- Some applications create independent popups, modal dialogs, replacement HWNDs, or auxiliary windows. Membership remains per explicit top-level window; a new window is not automatically added. Apps may make themselves visible again or impose their own size and focus constraints.
- Floating minimized windows stay minimized by default. Enable **Behavior → Restore minimized windows when switching sessions** to restore them when selecting a session. Pane placement restores minimized/maximized windows so their tree rectangles can be applied. Hiding a window does not pause its CPU, networking, audio, notifications, or background work.
- Monitor names can change after driver or docking changes. The conservative fallback keeps windows reachable but cannot always recover the exact original physical display identity.
- Complete Start Menu discovery, configurable command bindings beyond the prefix, and cloud synchronization are not included. Release signing is active only when the repository owner configures a certificate in GitHub Actions.

## Releases and website

Pushes and pull requests run the application checks and build the static website. Tags beginning with `v` create the Windows installer, portable ZIP, checksums, and a GitHub Release. For example:

```powershell
git tag v0.1.0-alpha.1
git push origin v0.1.0-alpha.1
```

The landing-page source is in `website`. Its production files build into `website/dist/client`; its Dockerfile serves them as a separate Nginx container for Coolify at `deskmux.kurtian.dev`. The website is not included in either Windows download. See [website/DEPLOYMENT.md](website/DEPLOYMENT.md) for the Coolify settings, [CONTRIBUTING.md](CONTRIBUTING.md) for development guidance, and [SECURITY.md](SECURITY.md) for private vulnerability reporting.

## Roadmap

The separation of session state, native window operations, persistence and UI leaves room for a CLI, title/process rules, templates, startup scripts, alternate configuration formats, and plugins. Native windows, predictable pane layouts, and safe recovery remain the foundation.
