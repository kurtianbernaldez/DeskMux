using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeskMux.Core;

namespace DeskMux.Windows;

/// <summary>Individual top-level HWND management. Never launches, reparents, or terminates applications.</summary>
public sealed class WindowSystem : IWindowSystem
{
    private readonly AppSettings _settings;
    private readonly ILog _log;
    private readonly RecoveryJournal _journal;

    public WindowSystem(AppSettings settings, string dataDirectory, ILog log)
    {
        _settings = settings;
        _log = log;
        _journal = new RecoveryJournal(dataDirectory);
    }

    public long ForegroundWindow => NativeMethods.GetForegroundWindow().ToInt64();

    public IReadOnlyList<WindowSnapshot> EnumerateWindows(bool includeHidden = false)
    {
        var windows = new List<WindowSnapshot>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if ((includeHidden || NativeMethods.IsWindowVisible(hwnd)) && Inspect(hwnd.ToInt64()) is { } snapshot)
                    windows.Add(snapshot);
            }
            catch (Exception exception) { Log("window.enumeration.failed", exception.Message); }
            return true;
        }, 0);
        return windows;
    }

    public WindowSnapshot? Inspect(long handle)
    {
        try { return NativeDesktop.Inspect((nint)handle, _settings); }
        catch (Exception exception)
        {
            Log("window.inspect.failed", exception.Message, new { handle });
            return null;
        }
    }

    public bool IsSameWindow(ManagedWindow window) => NativeDesktop.IsSameWindow(window);

    public WindowOperationResult Hide(ManagedWindow window)
    {
        try
        {
            if (!IsSameWindow(window)) return Fail("hide", window, "This window closed or its identity changed.");
            if (!NativeDesktop.IsEligible((nint)window.Handle, _settings, out _))
                return Fail("hide", window, "This window is excluded or Windows does not allow access to it.");
            if (!NativeMethods.IsWindowVisible((nint)window.Handle))
                return _journal.Contains(window) ? WindowOperationResult.Ok
                    : Fail("hide", window, "The application already hid this window; DeskMux has left it unchanged.");

            // The disk transaction MUST finish before any operation can make a window invisible.
            // Keep the entry even if an asynchronous request times out: it may complete later.
            _journal.Add(window);
            var result = NativeDesktop.Hide(window);
            if (!result.Success) return Fail("hide", window, result.Error!);
            Log("window.hidden", window.DisplayTitle, new { window.Handle });
            return result;
        }
        catch (Exception exception) { return Fail("hide", window, exception.Message); }
    }

    public WindowOperationResult Show(ManagedWindow window)
    {
        try
        {
            if (!IsSameWindow(window))
            {
                // A dead handle cannot conceal the original window. An identity mismatch is never restored.
                _journal.Remove(window);
                return Fail("show", window, "This window closed or its identity changed.");
            }
            var result = NativeDesktop.Restore(window);
            if (!result.Success) return Fail("show", window, result.Error!);
            _journal.Remove(window);
            Log("window.shown", window.DisplayTitle, new { window.Handle });
            return result;
        }
        catch (Exception exception) { return Fail("show", window, exception.Message); }
    }

    public WindowOperationResult Focus(ManagedWindow window)
    {
        if (!IsSameWindow(window)) return Fail("focus", window, "This window is no longer available.");
        var hwnd = (nint)window.Handle;
        if (!NativeMethods.IsWindowVisible(hwnd)) return Fail("focus", window, "The window is hidden.");
        // Session restore preserves minimized windows; an explicit focus action restores its target.
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SwRestore);
            NativeDesktop.WaitUntil(() => !NativeMethods.IsIconic(hwnd), 150);
        }
        if (NativeMethods.GetForegroundWindow() == hwnd || NativeMethods.SetForegroundWindow(hwnd))
            return WindowOperationResult.Ok;

        var flash = new NativeMethods.FlashInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.FlashInfo>(), Window = hwnd, Flags = 3, Count = 2
        };
        NativeMethods.FlashWindowEx(ref flash);
        return Fail("focus", window, "Windows kept focus in another application. Select this window on the taskbar.");
    }

    public IReadOnlyList<MonitorDescriptor> GetMonitors() => NativeDesktop.GetMonitors();

    public WindowOperationResult ApplyLayout(ManagedWindow window, WindowLayout layout, bool activate = false)
    {
        try
        {
            if (!IsSameWindow(window)) return Fail("layout", window, "This window closed or its identity changed.");
            if (!NativeDesktop.IsEligible((nint)window.Handle, _settings, out _))
                return Fail("layout", window, "This window is excluded or Windows does not allow access to it.");
            if (!NativeDesktop.CanControl((nint)window.Handle))
                return Fail("layout", window, "Windows denied access. Run DeskMux and the application at the same administrator permission level.");
            var result = NativeDesktop.ApplyLayout(window, layout, out var appliedLayout);
            if (!result.Success) return Fail("layout", window, result.Error!);
            _journal.Remove(window); // Placement verified that the application is visible and reachable.
            if (activate)
            {
                var focused = Focus(window);
                if (!focused.Success) return focused;
            }
            window.Layout = appliedLayout!;
            Log("window.layout.applied", window.DisplayTitle, new { window.Handle, layout.Bounds });
            return WindowOperationResult.Ok;
        }
        catch (Exception exception) { return Fail("layout", window, exception.Message); }
    }

    public IReadOnlyList<WindowOperationResult> ApplyLayouts(IReadOnlyList<(ManagedWindow Window, WindowLayout Layout)> placements)
    {
        // A batch is useful for separate document windows belonging to one application.
        // Mixed applications, minimized/maximized windows, and uncertain eligibility use
        // individual verified operations so they retain a safe restoration boundary.
        if (placements.Count < 2)
            return placements.Select(item => ApplyLayout(item.Window, item.Layout)).ToArray();
        if (!NativeDesktop.CanDeferLayouts(placements, _settings, out var preflightReason))
        {
            Log("window.layout.batch.fallback", preflightReason);
            return placements.Select(item => ApplyLayout(item.Window, item.Layout)).ToArray();
        }
        try
        {
            if (!NativeDesktop.TryDeferLayouts(placements, out var nativeReason))
            {
                Log("window.layout.batch.fallback", nativeReason);
                return placements.Select(item => ApplyLayout(item.Window, item.Layout)).ToArray();
            }
            var results = new List<WindowOperationResult>(placements.Count);
            foreach (var (window, layout) in placements)
            {
                try
                {
                    if (!NativeDesktop.VerifyLayout(window, layout.Bounds))
                    {
                        results.Add(Fail("layout", window, "The application did not accept the deferred pane size or position."));
                        continue;
                    }
                    _journal.Remove(window);
                    window.Layout = MonitorMapper.Clone(layout);
                    window.Layout.ShowState = WindowShowState.Normal;
                    window.Layout.RestoreToMaximized = false;
                    results.Add(WindowOperationResult.Ok);
                }
                catch (Exception exception) { results.Add(Fail("layout", window, exception.Message)); }
            }
            Log("window.layout.batch.applied", "Deferred pane placement completed.", new { Count = placements.Count, Success = results.All(result => result.Success) });
            return results;
        }
        catch (Exception exception)
        {
            Log("window.layout.batch.failed", exception.Message);
            return placements.Select(item => ApplyLayout(item.Window, item.Layout)).ToArray();
        }
    }

    public IReadOnlyList<WindowOperationResult> BringToFront(IReadOnlyList<ManagedWindow> windows)
    {
        var results = new List<WindowOperationResult>(windows.Count);
        foreach (var window in windows)
        {
            try
            {
                var hwnd = (nint)window.Handle;
                if (!IsSameWindow(window) || !NativeMethods.IsWindowVisible(hwnd))
                { results.Add(Fail("zorder", window, "This window is no longer visible or its identity changed.")); continue; }
                if (!NativeDesktop.IsEligible(hwnd, _settings, out _) || !NativeDesktop.CanControl(hwnd))
                { results.Add(Fail("zorder", window, "Windows denied access. Run DeskMux and the application at the same permission level.")); continue; }
                // Windows can keep a non-activating HWND_TOP request behind the current
                // foreground application. Briefly crossing the topmost band and immediately
                // restoring the original band reliably raises the whole selected session.
                var flags = NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate;
                var wasTopMost = (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0;
                var promoted = NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopMost, 0, 0, 0, 0, flags);
                var restored = wasTopMost || NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndNoTopMost, 0, 0, 0, 0, flags);
                if (!promoted || !restored)
                { results.Add(Fail("zorder", window, "Windows rejected the request to bring this session forward.")); continue; }
                results.Add(WindowOperationResult.Ok);
            }
            catch (Exception exception) { results.Add(Fail("zorder", window, exception.Message)); }
        }
        return results;
    }

    private WindowOperationResult Fail(string operation, ManagedWindow window, string error)
    {
        Log($"window.{operation}.failed", error, new { window.Handle, window.Fingerprint.ProcessName });
        return WindowOperationResult.Fail(error);
    }

    private void Log(string eventName, string message, object? data = null)
    {
        try { _log.Write(eventName, message, data); } catch { /* Logging must never compromise restoration. */ }
    }
}

/// <summary>Reusable enumeration entry point independent of UI code.</summary>
public sealed class WindowEnumerator(IWindowSystem windowSystem)
{
    public IReadOnlyList<WindowSnapshot> Enumerate(bool includeHidden = false) => windowSystem.EnumerateWindows(includeHidden);
}

internal static class NativeDesktop
{
    private static readonly Lazy<int?> OwnElevation = new(() => ReadElevation((uint)Environment.ProcessId));
    private static readonly HashSet<string> CriticalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DeskMux", "DeskMux.Watchdog", "dwm", "csrss", "winlogon", "LogonUI", "LockApp", "lsass",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "sihost",
        "CredentialUIBroker", "consent", "smss", "services", "fontdrvhost", "System", "Registry"
    };
    private static readonly HashSet<string> CriticalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "DV2ControlHost", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "Windows.UI.Composition.DesktopWindowContentBridge"
    };

    internal static WindowSnapshot? Inspect(nint hwnd, AppSettings settings)
    {
        if (!IsEligible(hwnd, settings, out var fingerprint)) return null;
        using var dpi = new DpiScope();
        var placement = NativeMethods.WindowPlacement.Create();
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement)) return null;
        var monitor = GetMonitor(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest));
        var minimized = NativeMethods.IsIconic(hwnd);
        var maximized = NativeMethods.IsZoomed(hwnd);
        PixelRect bounds;
        if (!minimized && !maximized && NativeMethods.GetWindowRect(hwnd, out var actual))
            bounds = actual.ToPixelRect();
        else
        {
            var normal = placement.NormalPosition.ToPixelRect();
            bounds = normal with
            {
                X = normal.X + monitor.WorkArea.X - monitor.Bounds.X,
                Y = normal.Y + monitor.WorkArea.Y - monitor.Bounds.Y
            };
        }
        if (bounds.Width < 40 || bounds.Height < 30) return null;
        return new WindowSnapshot(hwnd.ToInt64(), fingerprint!, new WindowLayout
        {
            Bounds = bounds,
            ShowState = minimized ? WindowShowState.Minimized : maximized ? WindowShowState.Maximized : WindowShowState.Normal,
            MonitorDevice = monitor.DeviceName,
            MonitorId = monitor.StableId,
            MonitorWorkArea = monitor.WorkArea,
            Dpi = monitor.Dpi,
            RestoreToMaximized = (placement.Flags & 2) != 0
        }, NativeMethods.IsWindowVisible(hwnd));
    }

    internal static bool IsEligible(nint hwnd, AppSettings settings, out WindowFingerprint? fingerprint)
    {
        fingerprint = null;
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd) || hwnd == NativeMethods.GetShellWindow() ||
            hwnd == NativeMethods.GetDesktopWindow() || NativeMethods.GetAncestor(hwnd, 2) != hwnd)
            return false;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        var extended = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        if ((style & NativeMethods.WsChild) != 0 || (extended & (NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate)) != 0)
            return false;
        // Owned dialogs follow their owner and must not become independently assigned session windows.
        if (NativeMethods.GetWindow(hwnd, 4) != 0) return false;
        if (NativeMethods.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return false;
        var className = ReadClass(hwnd);
        if (string.IsNullOrEmpty(className) || CriticalClasses.Contains(className)) return false;
        try
        {
            using var process = Process.GetProcessById(checked((int)pid));
            var processName = process.ProcessName;
            if (CriticalProcesses.Contains(processName) || settings.ExcludedProcesses.Any(value =>
                string.Equals(Path.GetFileNameWithoutExtension(value.Trim()), processName, StringComparison.OrdinalIgnoreCase)))
                return false;
            var startTime = process.StartTime.ToUniversalTime().Ticks;
            string executable = "";
            try { executable = process.MainModule?.FileName ?? ""; } catch (Win32Exception) { } catch (InvalidOperationException) { }
            var title = new StringBuilder(2048);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            fingerprint = new WindowFingerprint
            {
                ProcessId = (int)pid, ProcessStartTimeUtcTicks = startTime, ProcessName = processName,
                ExecutablePath = executable, WindowClass = className, Title = title.ToString()
            };
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    internal static bool IsSameWindow(ManagedWindow window)
    {
        var hwnd = (nint)window.Handle;
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd) || window.Fingerprint.ProcessStartTimeUtcTicks <= 0) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != window.Fingerprint.ProcessId || pid == Environment.ProcessId) return false;
        if (!string.Equals(ReadClass(hwnd), window.Fingerprint.WindowClass, StringComparison.Ordinal)) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.StartTime.ToUniversalTime().Ticks == window.Fingerprint.ProcessStartTimeUtcTicks;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    private static string ReadClass(nint hwnd)
    {
        var text = new StringBuilder(512);
        NativeMethods.GetClassName(hwnd, text, text.Capacity);
        return text.ToString();
    }

    internal static bool CanControl(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;
        var target = ReadElevation(pid);
        var own = OwnElevation.Value;
        return target.HasValue && own.HasValue && (target.Value == 0 || own.Value != 0);
    }

    private static int? ReadElevation(uint processId)
    {
        var process = NativeMethods.OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) return null;
        try
        {
            if (!NativeMethods.OpenProcessToken(process, 0x0008, out var token)) return null; // TOKEN_QUERY
            try
            {
                return NativeMethods.GetTokenInformation(token, 20, out var elevation, sizeof(int), out _) ? elevation : null;
            }
            finally { NativeMethods.CloseHandle(token); }
        }
        finally { NativeMethods.CloseHandle(process); }
    }

    internal static WindowOperationResult Hide(ManagedWindow window)
    {
        var hwnd = (nint)window.Handle;
        if (!Responds(hwnd)) return WindowOperationResult.Fail("The application is busy or protected by Windows. Try again, or run both apps at the same permission level.");
        if (!IsSameWindow(window)) return WindowOperationResult.Fail("The window closed before it could be hidden.");
        if (!NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate |
            NativeMethods.SwpHideWindow | NativeMethods.SwpAsyncWindowPos))
            return NativeFailure("hide");
        return WaitUntil(() => !NativeMethods.IsWindowVisible(hwnd), 180)
            ? WindowOperationResult.Ok : WindowOperationResult.Fail("The application has not accepted the hide request. Recovery protection remains active.");
    }

    internal static WindowOperationResult Restore(ManagedWindow window)
    {
        if (!IsSameWindow(window)) return WindowOperationResult.Fail("This window is no longer available.");
        using var dpi = new DpiScope();
        var hwnd = (nint)window.Handle;
        var monitors = GetMonitors();
        if (monitors.Count == 0) return WindowOperationResult.Fail("No connected monitor is available.");
        var layout = MonitorMapper.Map(window.Layout, monitors);
        var monitor = monitors.FirstOrDefault(item => string.Equals(item.DeviceName, layout.MonitorDevice, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(item => item.IsPrimary) ?? monitors[0];
        var placement = NativeMethods.WindowPlacement.Create();
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement)) return NativeFailure("read window placement");
        placement.Flags = NativeMethods.WpfAsyncWindowPlacement |
            (layout.ShowState == WindowShowState.Minimized && layout.RestoreToMaximized ? 2u : 0u);
        placement.ShowCmd = (uint)(layout.ShowState switch
        {
            WindowShowState.Maximized => NativeMethods.SwMaximized,
            WindowShowState.Minimized => NativeMethods.SwShowMinNoActive,
            _ => NativeMethods.SwShowNoActivate
        });
        placement.NormalPosition = NativeMethods.Rect.From(layout.Bounds with
        {
            X = layout.Bounds.X - (monitor.WorkArea.X - monitor.Bounds.X),
            Y = layout.Bounds.Y - (monitor.WorkArea.Y - monitor.Bounds.Y)
        });
        // Ignore maximized/minimized positions from a removed display; Windows derives them.
        placement.MinPosition = new() { X = -1, Y = -1 };
        placement.MaxPosition = new() { X = -1, Y = -1 };
        if (!NativeMethods.SetWindowPlacement(hwnd, in placement)) return NativeFailure("restore window placement");
        // HWND style can already be visible before a queued placement completes. A bounded
        // WM_NULL fence drains the target queue before journal ownership is relinquished.
        Responds(hwnd);
        if (!WaitUntil(() => NativeMethods.IsWindowVisible(hwnd), 180))
        {
            NativeMethods.ShowWindowAsync(hwnd, (int)placement.ShowCmd);
            if (!WaitUntil(() => NativeMethods.IsWindowVisible(hwnd), 180))
                return WindowOperationResult.Fail("The application did not become visible. Recovery protection remains active; check matching administrator permissions.");
        }
        window.Layout = layout;
        return WindowOperationResult.Ok;
    }

    internal static WindowOperationResult ApplyLayout(ManagedWindow window, WindowLayout requested, out WindowLayout? appliedLayout)
    {
        appliedLayout = null;
        if (!IsSameWindow(window)) return WindowOperationResult.Fail("This window is no longer available.");
        if (requested.Bounds.Width <= 0 || requested.Bounds.Height <= 0)
            return WindowOperationResult.Fail("The pane rectangle is empty.");
        using var dpi = new DpiScope();
        var hwnd = (nint)window.Handle;
        if (!Responds(hwnd)) return WindowOperationResult.Fail("The application is busy or protected by Windows. Try again, or run both apps at the same permission level.");
        var monitors = GetMonitors();
        if (monitors.Count == 0) return WindowOperationResult.Fail("No connected monitor is available.");
        var monitor = monitors.FirstOrDefault(item =>
            !string.IsNullOrEmpty(requested.MonitorId) ? string.Equals(item.StableId, requested.MonitorId, StringComparison.OrdinalIgnoreCase) :
            string.Equals(item.DeviceName, requested.MonitorDevice, StringComparison.OrdinalIgnoreCase));
        if (monitor is null) return WindowOperationResult.Fail("The pane monitor disconnected. Recalculate the canvas before arranging its windows.");
        var bounds = requested.Bounds;
        if (bounds.X < monitor.WorkArea.X || bounds.Y < monitor.WorkArea.Y ||
            (long)bounds.X + bounds.Width > (long)monitor.WorkArea.X + monitor.WorkArea.Width ||
            (long)bounds.Y + bounds.Height > (long)monitor.WorkArea.Y + monitor.WorkArea.Height)
            return WindowOperationResult.Fail("The pane rectangle is outside its monitor work area.");

        var placement = NativeMethods.WindowPlacement.Create();
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement)) return NativeFailure("read window placement");
        placement.Flags = NativeMethods.WpfAsyncWindowPlacement;
        // SW_SHOWNOACTIVATE restores the normal rectangle without taking foreground focus.
        // SW_RESTORE would activate each application while a tree is being arranged.
        placement.ShowCmd = NativeMethods.SwShowNoActivate;
        placement.NormalPosition = NativeMethods.Rect.From(bounds with
        {
            X = bounds.X - (monitor.WorkArea.X - monitor.Bounds.X),
            Y = bounds.Y - (monitor.WorkArea.Y - monitor.Bounds.Y)
        });
        placement.MinPosition = new() { X = -1, Y = -1 };
        placement.MaxPosition = new() { X = -1, Y = -1 };
        if (!NativeMethods.SetWindowPlacement(hwnd, in placement)) return NativeFailure("restore the pane before positioning");
        Responds(hwnd);
        if (!WaitUntil(() => NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd) && !NativeMethods.IsZoomed(hwnd), 180))
            return WindowOperationResult.Fail("The application did not accept restoration to a normal visible window.");
        // Different applications have different UI threads. Individual asynchronous placements
        // plus verification provide a reliable rollback boundary; cross-thread deferred batches do not.
        if (!NativeMethods.SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpAsyncWindowPos))
            return NativeFailure("position the pane");
        Responds(hwnd);
        if (!WaitUntil(() => IsSameWindow(window) && NativeMethods.GetWindowRect(hwnd, out var actual) &&
            actual.ToPixelRect() == bounds && NativeMethods.IsWindowVisible(hwnd) &&
            !NativeMethods.IsIconic(hwnd) && !NativeMethods.IsZoomed(hwnd), 180))
            return WindowOperationResult.Fail("The application did not accept the pane size or position. Its minimum size may be larger than the pane.");
        appliedLayout = new WindowLayout
        {
            Bounds = bounds, ShowState = WindowShowState.Normal, RestoreToMaximized = false,
            MonitorDevice = monitor.DeviceName, MonitorId = monitor.StableId,
            MonitorWorkArea = monitor.WorkArea, Dpi = monitor.Dpi
        };
        return WindowOperationResult.Ok;
    }

    internal static bool CanDeferLayouts(IReadOnlyList<(ManagedWindow Window, WindowLayout Layout)> placements, AppSettings settings, out string reason)
    {
        reason = "";
        try
        {
            var monitors = GetMonitors();
            uint owningThread = 0;
            var handles = new HashSet<long>();
            foreach (var (window, layout) in placements)
            {
                var hwnd = (nint)window.Handle;
                if (!handles.Add(window.Handle) || !IsSameWindow(window))
                { reason = "A pane HWND is duplicated or no longer has its recorded identity."; return false; }
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd))
                { reason = "A pane needs individual restoration to normal visible state."; return false; }
                if (!IsEligible(hwnd, settings, out _) || !CanControl(hwnd))
                { reason = "Windows does not allow batch control of a pane."; return false; }
                if (!Responds(hwnd))
                { reason = "A pane did not respond to the batch readiness check."; return false; }
                var thread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
                if (thread == 0 || owningThread != 0 && owningThread != thread)
                { reason = "The panes belong to different native UI threads."; return false; }
                owningThread = thread;
                var monitor = monitors.FirstOrDefault(item =>
                    !string.IsNullOrEmpty(layout.MonitorId) ? string.Equals(item.StableId, layout.MonitorId, StringComparison.OrdinalIgnoreCase) :
                    string.Equals(item.DeviceName, layout.MonitorDevice, StringComparison.OrdinalIgnoreCase));
                if (monitor is null)
                { reason = "A pane monitor is no longer connected."; return false; }
                if (layout.Bounds.Width <= 0 || layout.Bounds.Height <= 0 ||
                    layout.Bounds.X < monitor.WorkArea.X || layout.Bounds.Y < monitor.WorkArea.Y ||
                    (long)layout.Bounds.X + layout.Bounds.Width > (long)monitor.WorkArea.X + monitor.WorkArea.Width ||
                    (long)layout.Bounds.Y + layout.Bounds.Height > (long)monitor.WorkArea.Y + monitor.WorkArea.Height)
                { reason = "A pane rectangle is outside its monitor work area."; return false; }
            }
            return true;
        }
        catch (Exception exception) { reason = "Batch readiness check failed: " + exception.Message; return false; }
    }

    internal static bool TryDeferLayouts(IReadOnlyList<(ManagedWindow Window, WindowLayout Layout)> placements, out string reason)
    {
        reason = "";
        using var dpi = new DpiScope();
        var batch = NativeMethods.BeginDeferWindowPos(placements.Count);
        if (batch == 0) { reason = NativeFailure("begin deferred pane positioning").Error!; return false; }
        foreach (var (window, layout) in placements)
        {
            var bounds = layout.Bounds;
            // DeferWindowPos accepts a smaller documented flag set than SetWindowPos.
            // SWP_ASYNCWINDOWPOS is rejected with ERROR_INVALID_PARAMETER (87), so the
            // same-thread, responsive-window preflight is the batch's readiness boundary.
            batch = NativeMethods.DeferWindowPos(batch, (nint)window.Handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
            // On failure Windows destroys the internal batch; EndDeferWindowPos must not be called.
            if (batch == 0) { reason = NativeFailure("queue deferred pane positioning").Error!; return false; }
        }
        if (NativeMethods.EndDeferWindowPos(batch)) return true;
        reason = NativeFailure("complete deferred pane positioning").Error!;
        return false;
    }

    internal static bool VerifyLayout(ManagedWindow window, PixelRect bounds)
    {
        using var dpi = new DpiScope();
        var hwnd = (nint)window.Handle;
        Responds(hwnd);
        return WaitUntil(() => IsSameWindow(window) && NativeMethods.GetWindowRect(hwnd, out var actual) &&
            actual.ToPixelRect() == bounds && NativeMethods.IsWindowVisible(hwnd) &&
            !NativeMethods.IsIconic(hwnd) && !NativeMethods.IsZoomed(hwnd), 180);
    }

    internal static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (condition()) return true;
            Thread.Sleep(5);
        } while (timer.ElapsedMilliseconds < timeoutMs);
        return condition();
    }

    private static bool Responds(nint hwnd)
    {
        // A window can briefly stop answering while it processes a completed batch resize.
        // Give it a second bounded chance before treating it as protected or hung.
        if (NativeMethods.SendMessageTimeout(hwnd, 0, 0, 0, 0x0002 | 0x0020, 180, out _) != 0) return true;
        return NativeMethods.SendMessageTimeout(hwnd, 0, 0, 0, 0x0002 | 0x0020, 320, out _) != 0;
    }
    private static WindowOperationResult NativeFailure(string action)
    {
        var error = Marshal.GetLastWin32Error();
        return WindowOperationResult.Fail(error == 5
            ? "Windows denied access. Run DeskMux and the application at the same administrator permission level."
            : $"Windows could not {action}: {new Win32Exception(error).Message} (code {error}).");
    }

    internal static IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        using var dpi = new DpiScope();
        var monitors = new List<MonitorDescriptor>();
        NativeMethods.EnumDisplayMonitors(0, 0, (nint handle, nint _, ref NativeMethods.Rect __, nint ___) =>
        {
            try { monitors.Add(GetMonitor(handle)); } catch { /* A display can vanish while enumerating. */ }
            return true;
        }, 0);
        return monitors;
    }

    private static MonitorDescriptor GetMonitor(nint handle)
    {
        var info = NativeMethods.MonitorInfo.Create();
        if (!NativeMethods.GetMonitorInfo(handle, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The monitor is no longer connected.");
        uint dpi = 96;
        try { if (NativeMethods.GetDpiForMonitor(handle, 0, out var x, out _) == 0 && x > 0) dpi = x; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        var display = new NativeMethods.DisplayDevice { Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>(), Name = "", Description = "", Id = "", Key = "" };
        var stableId = NativeMethods.EnumDisplayDevices(info.Device, 0, ref display, 1) ? display.Id : "";
        return new(info.Device, info.Monitor.ToPixelRect(), info.Work.ToPixelRect(), dpi, (info.Flags & 1) != 0, stableId);
    }

    private readonly struct DpiScope : IDisposable
    {
        private readonly nint _previous;
        public DpiScope() { _previous = NativeMethods.SetThreadDpiAwarenessContext(-4); }
        public void Dispose() { if (_previous != 0) NativeMethods.SetThreadDpiAwarenessContext(_previous); }
    }
}
