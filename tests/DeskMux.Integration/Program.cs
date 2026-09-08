using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeskMux.Core;
using DeskMux.Windows;

namespace DeskMux.Integration;

internal static class Program
{
    private static int _checks;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [STAThread]
    private static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        if (args.Length > 0 && args[0] == "--fixture") return RunFixture(args[1], args[2]);
        if (args.Length > 0 && args[0] == "--launch-fixture") return NativeLaunchChecks.RunFixture(args[1], args[2]);
        if (args.Length > 0 && args[0] == "--mixed-panes-only")
        {
            var path = Path.Combine(Path.GetTempPath(), "DeskMux.MixedPanes", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path); RunMixedAppPaneChecks(path); return 0;
        }
        if (args.Length > 0 && args[0] == "--pane-diagnostics")
        {
            var windows = new WindowSystem(new(), Path.Combine(Path.GetTempPath(), "DeskMux.PaneDiagnostics"), new TestLog());
            foreach (var snapshot in windows.EnumerateWindows().Where(w => args.Skip(1).Contains(w.Fingerprint.ProcessName, StringComparer.OrdinalIgnoreCase)))
            {
                var entry = new ManagedWindow { Handle = snapshot.Handle, Fingerprint = snapshot.Fingerprint, Layout = snapshot.Layout };
                Console.WriteLine(JsonSerializer.Serialize(new { App = snapshot.Fingerprint.ProcessName, Minimum = windows.GetMinimumPaneSize(entry), Outer = snapshot.Layout.Bounds, Visible = snapshot.VisibleBounds }));
            }
            return 0;
        }
        if (args.Length > 0 && args[0] == "--watchdog")
        {
            RecoveryWatcher.Run(int.Parse(args[1]), long.Parse(args[2]), args[3]);
            return 0;
        }
        if (args.Length > 0 && args[0] == "--crash-worker") return RunCrashWorker(args[1], long.Parse(args[2]), long.Parse(args[3]));
        if (!Environment.UserInteractive)
        {
            Console.Error.WriteLine("These integration checks need an unlocked interactive Windows desktop.");
            return 2;
        }

        var directory = Path.Combine(Path.GetTempPath(), "DeskMux.Integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "fixture.json");
        using var fixture = StartSelf("--fixture", manifestPath, Guid.NewGuid().ToString("N"));
        SessionManager? manager = null;
        try
        {
            WaitFor(() => File.Exists(manifestPath), "fixture startup");
            var handles = JsonSerializer.Deserialize<long[]>(File.ReadAllText(manifestPath))!;
            var log = new TestLog();
            var state = new WorkspaceState();
            var windowSystem = new WindowSystem(state.Settings, Path.Combine(directory, "manager"), log);
            var store = new CountingStore(new JsonStateStore(Path.Combine(directory, "manager"), log));
            manager = new SessionManager(state, windowSystem, store, log);

            var snapshots = handles.Select(h => windowSystem.Inspect(h) ?? throw new Exception("Fixture was not inspectable")).ToArray();
            _checks += NativeLaunchChecks.Run(directory, log);
            _checks += PaneUiChecks.Run();
            Check(snapshots.Select(x => x.Fingerprint.ProcessId).Distinct().Count() == 1, "Fixtures share one process while retaining three distinct HWNDs");
            Check(handles.Distinct().Count() == 3, "Three independent native top-level windows exist");
            Check(windowSystem.EnumerateWindows().Count(s => handles.Contains(s.Handle)) == 3, "Enumeration discovers normal, maximized and minimized fixture windows");
            Check(windowSystem.GetMonitors().Count > 0, "Monitor enumeration returns a reachable desktop");
            var wrongIdentity = new ManagedWindow { Handle = handles[0], Fingerprint = new WindowFingerprint { ProcessId = -1, ProcessStartTimeUtcTicks = 1, WindowClass = snapshots[0].Fingerprint.WindowClass } };
            Check(!windowSystem.Hide(wrongIdentity).Success && Visible(handles[0]), "A mismatched process identity cannot hide a valid HWND");
            var unmanagedCover = new Window { Title = "DeskMux unmanaged z-order fixture", Width = 420, Height = 280,
                Left = 120, Top = 130, ShowActivated = false, ShowInTaskbar = false };
            unmanagedCover.Show();
            var coverHandle = new WindowInteropHelper(unmanagedCover).Handle;
            SetWindowPos(coverHandle, 0, 120, 130, 420, 280, 0x0010);
            var raiseEntries = snapshots.Take(2).Select(snapshot => new ManagedWindow
                { Handle = snapshot.Handle, Fingerprint = snapshot.Fingerprint, Layout = snapshot.Layout }).ToArray();
            var raiseResults = windowSystem.BringToFront(raiseEntries);
            WaitFor(() => raiseResults.All(result => result.Success) &&
                    IsAbove(handles[0], coverHandle.ToInt64()) && IsAbove(handles[1], coverHandle.ToInt64()),
                "A selected session's native windows rise above an unmanaged overlapping window");
            unmanagedCover.Close();

            var dev = manager.CreateSession("DEV");
            var images = manager.CreateSession("IMAGES");
            manager.SwitchTo(dev.Id);
            manager.AddWindow(handles[0], dev.Id);
            manager.AddWindow(handles[1], dev.Id);
            manager.AddWindow(handles[2], images.Id);
            var alpha = dev.Windows.Single(w => w.Handle == handles[0]);
            var bravo = dev.Windows.Single(w => w.Handle == handles[1]);
            var charlie = images.Windows.Single(w => w.Handle == handles[2]);
            Check(alpha.Fingerprint.ProcessId == charlie.Fingerprint.ProcessId, "Two sessions own different windows of the same process");
            Check(bravo.Layout.ShowState == WindowShowState.Maximized, "Maximized placement is captured");
            Check(charlie.Layout.ShowState == WindowShowState.Minimized, "Minimized placement is captured");

            var original = alpha.Layout.Bounds;
            var focusResult = windowSystem.Focus(alpha);
            var canTestForeground = focusResult.Success;
            if (canTestForeground) WaitFor(() => windowSystem.ForegroundWindow == handles[0], "fixture becomes foreground");
            else
            {
                Check(!string.IsNullOrWhiteSpace(focusResult.Error), "Windows foreground restrictions produce a readable status");
                Console.WriteLine("  SKIP foreground restoration assertion: Windows denied foreground activation to this background test runner.");
            }
            manager.TrackWindow(handles[0], true);
            manager.SwitchTo(images.Id);
            WaitFor(() => !Visible(handles[0]) && !Visible(handles[1]) && Visible(handles[2]), "DEV to IMAGES visibility");
            Check(!fixture.HasExited, "Session switching leaves the application process running");
            Check(IsIconic(new IntPtr(handles[2])), "Switching back preserves the previously minimized window state");
            manager.SwitchTo(dev.Id);
            WaitFor(() => Visible(handles[0]) && Visible(handles[1]) && !Visible(handles[2]), "IMAGES to DEV visibility");
            Check(IsZoomed(new IntPtr(handles[1])), "Maximized window state is restored");
            Check(Near(windowSystem.Inspect(handles[0])!.Layout.Bounds, original), "Normal window position and size survive a round trip");
            Check(alpha.LastFocusedUtc > DateTime.MinValue, "Per-window focus history is recorded");
            if (canTestForeground) WaitFor(() => windowSystem.ForegroundWindow == handles[0], "returning to DEV restores its most recently focused window");

            var workArea = windowSystem.GetMonitors().First(m => m.IsPrimary).WorkArea;
            var moved = new PixelRect(workArea.X + 60, workArea.Y + 70, Math.Min(470, workArea.Width - 100), Math.Min(320, workArea.Height - 120));
            Check(SetWindowPos(new IntPtr(handles[0]), IntPtr.Zero, moved.X, moved.Y, moved.Width, moved.Height, 0x0014), "A fixture accepts a user-style position and size change");
            WaitFor(() => Near(windowSystem.Inspect(handles[0])!.Layout.Bounds, moved), "fixture layout update");
            manager.TrackWindow(handles[0], false);
            manager.SwitchTo(images.Id);
            manager.SwitchTo(dev.Id);
            Check(Near(windowSystem.Inspect(handles[0])!.Layout.Bounds, moved), "Updated layout is captured when switching away and restored on return");

            RunPaneChecks(manager, windowSystem, store, dev, images, alpha, bravo, charlie);
            Check(log.Events.Contains("window.layout.batch.applied"), "Compatible native panes use verified deferred positioning");

            manager.MoveWindow(alpha.Id, images.Id);
            WaitFor(() => !Visible(handles[0]), "moving into inactive session hides transferred window");
            Check(dev.Windows.All(w => w.Id != alpha.Id) && images.Windows.Count(w => w.Id == alpha.Id) == 1, "Moving transfers one HWND to exactly one owner");
            manager.SwitchTo(images.Id);
            WaitFor(() => Visible(handles[0]) && Visible(handles[2]), "transferred window appears in destination session");
            Check(!Visible(handles[1]), "The other window in the same application keeps its own session visibility");

            Check(PostMessage(new IntPtr(handles[1]), 0x0010, IntPtr.Zero, IntPtr.Zero), "A managed fixture can be closed independently");
            WaitFor(() => !IsWindow(new IntPtr(handles[1])), "fixture close");
            manager.SwitchTo(dev.Id);
            Check(bravo.IsMissing || bravo.Handle == 0, "A closed HWND is marked unavailable without crashing");
            manager.ShowAll();
            WaitFor(() => Visible(handles[0]) && Visible(handles[2]), "emergency recovery shows every live managed fixture");
            Check(!fixture.HasExited, "Emergency recovery does not terminate managed applications");
            manager.Shutdown();
            Check(store.Saves > 0, "Session mutations are persisted through the store abstraction");
            manager = new SessionManager(store.Load(), windowSystem, store, log);
            manager.Reconcile();
            Check(manager.State.Sessions.SelectMany(s => s.Windows).Count(w => !w.IsMissing) == 2, "Reloading saved JSON reassociates exactly the two surviving fixture windows");
            Check(manager.State.Sessions.Single(s => s.Name == "IMAGES").Windows.Any(w => w.Handle == handles[0]), "Per-window ownership survives manager restart");
            manager.SwitchTo(images.Id);
            WaitFor(() => Visible(handles[0]) && Visible(handles[2]), "restored session presents its reassociated windows");
            manager.Shutdown();
            manager = null;

            alpha.Layout = new WindowLayout { Bounds = new PixelRect(95000, 95000, 900, 600), MonitorDevice = "DISCONNECTED-INTEGRATION-DISPLAY", MonitorWorkArea = new PixelRect(90000, 90000, 1920, 1080), Dpi = 96 };
            Check(windowSystem.Show(alpha).Success, "Native restore accepts layout from a disconnected monitor");
            var reachable = windowSystem.Inspect(handles[0])!.Layout.Bounds;
            Check(windowSystem.GetMonitors().Any(m => reachable.X >= m.WorkArea.X && reachable.Y >= m.WorkArea.Y && (long)reachable.X + reachable.Width <= (long)m.WorkArea.X + m.WorkArea.Width && (long)reachable.Y + reachable.Height <= (long)m.WorkArea.Y + m.WorkArea.Height), "A disconnected monitor layout is clamped inside an available work area");

            RunCrashRecovery(directory, handles[0], handles[2]);
            RunMixedAppPaneChecks(directory);
            Console.WriteLine($"PASS: {_checks} real Windows integration checks. Only isolated fixture windows were managed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL after {_checks} checks: {ex}");
            Console.Error.WriteLine($"Test evidence directory: {directory}");
            return 1;
        }
        finally
        {
            manager?.Shutdown();
            if (!fixture.HasExited) fixture.Kill(entireProcessTree: true);
            fixture.WaitForExit(3000);
        }
    }

    private static void RunPaneChecks(SessionManager manager, WindowSystem windows, CountingStore store,
        WorkspaceSession dev, WorkspaceSession images, ManagedWindow alpha, ManagedWindow bravo, ManagedWindow charlie)
    {
        Check(manager.OpenPane(bravo.Handle, alpha.Handle, PaneOrientation.Vertical), "Existing native applications form left/right panes");
        var canvas = dev.PaneCanvases.Single();
        var calculated = PaneTree.Calculate(canvas);
        WaitFor(() => Near(windows.Inspect(alpha.Handle)!.VisibleBounds!, calculated[alpha.Id].Bounds) &&
            Near(windows.Inspect(bravo.Handle)!.VisibleBounds!, calculated[bravo.Id].Bounds), "Native visible pane edges match exact physical work-area geometry");
        Check(!IsIconic((nint)bravo.Handle) && !IsZoomed((nint)bravo.Handle), "Pane placement restores maximized or minimized native state");
        Check(!manager.OpenPane(charlie.Handle, bravo.Handle, PaneOrientation.Horizontal), "Cross-session native pane selection requires confirmation");
        Check(manager.OpenPane(charlie.Handle, bravo.Handle, PaneOrientation.Horizontal, true), "Confirmed native selection moves membership and creates a nested bottom pane");
        Check(PaneTree.Leaves(canvas).Count == 3 && images.Windows.Count == 0, "Three separate HWNDs occupy exactly three nested leaves");
        var before = windows.Inspect(alpha.Handle)!.Layout.Bounds;
        manager.ResizePane(alpha.Handle, PaneDirection.Right);
        Check(windows.Inspect(alpha.Handle)!.Layout.Bounds.Width > before.Width, "Modified direction resizes the native divider");
        var previousBravo = windows.Inspect(bravo.Handle)!.Layout.Bounds;
        manager.SwapPane(alpha.Handle, 1);
        Check(Near(windows.Inspect(alpha.Handle)!.Layout.Bounds, previousBravo), "Native pane swap moves the same application to the neighboring rectangle");
        var focus = windows.Focus(alpha);
        manager.NavigatePane(alpha.Handle, PaneDirection.Down);
        if (focus.Success && manager.LastError == null)
            Check(windows.ForegroundWindow == charlie.Handle, "Geometric native navigation reaches the lower nested pane");
        else
        {
            Check(!string.IsNullOrWhiteSpace(manager.LastError ?? focus.Error), "Foreground restrictions are reported without disrupting the layout");
            Console.WriteLine("  SKIP geometric foreground assertion: Windows denied foreground activation to this background test runner.");
        }
        manager.TogglePaneZoom(charlie.Handle);
        WaitFor(() => !Visible(alpha.Handle) && !Visible(bravo.Handle) && Visible(charlie.Handle), "Zoom safely hides sibling native panes");
        Check(Near(windows.Inspect(charlie.Handle)!.VisibleBounds!, canvas.MonitorWorkArea), "Zoomed native pane fills the canvas");
        manager.SwitchTo(images.Id);
        WaitFor(() => !Visible(alpha.Handle) && !Visible(bravo.Handle) && !Visible(charlie.Handle), "Switching away hides the zoomed session");
        manager.SwitchTo(dev.Id);
        WaitFor(() => !Visible(alpha.Handle) && !Visible(bravo.Handle) && Visible(charlie.Handle), "Switching back preserves native zoom visibility");
        manager.Save();
        var read = store.Load().Sessions.Single(s => s.Id == dev.Id).PaneCanvases.Single();
        Check(read.RootNodeId == canvas.RootNodeId && read.ZoomedLeafId == canvas.ZoomedLeafId && PaneTree.Leaves(read).Count == 3,
            "Saved JSON roundtrip preserves complete nested tree and zoom identity");
        manager.TogglePaneZoom(charlie.Handle);
        WaitFor(() => Visible(alpha.Handle) && Visible(bravo.Handle) && Visible(charlie.Handle), "Unzoom restores every native sibling");
        var desired = alpha.Layout.Bounds;
        Check(SetWindowPos((nint)alpha.Handle, 0, desired.X + 15, desired.Y + 15, 430, 300, 0x0014), "Fixture accepts a manual pane move");
        manager.TrackWindow(alpha.Handle, false);
        manager.FlushPaneLayoutChanges();
        Check(Near(windows.Inspect(alpha.Handle)!.VisibleBounds!, desired), "Debounced reflow restores authoritative native pane geometry");
        var calls = store.Saves;
        manager.TrackWindow(alpha.Handle, false); manager.FlushPaneLayoutChanges();
        Check(store.Saves == calls, "Visible frame compensation does not create a reflow loop");
        manager.RemoveWindow(charlie.Id);
        Check(Visible(charlie.Handle) && IsWindow((nint)charlie.Handle), "Removing native pane leaves application visible and running");
        Check(PaneTree.Leaves(dev.PaneCanvases.Single()).Count == 2, "Native pane removal collapses the split");
        Check(manager.AddWindow(charlie.Handle, images.Id), "Removed pane can be added as an independent floating member");
        manager.ReleasePane(bravo.Id);
        manager.ReleasePane(alpha.Id);
        Check(dev.PaneCanvases.Count == 0 && dev.Windows.Count == 2, "Releasing native panes preserves session members as floating windows");
    }

    private static void RunMixedAppPaneChecks(string directory)
    {
        var firstManifest = Path.Combine(directory, "mixed-first.json");
        var secondManifest = Path.Combine(directory, "mixed-second.json");
        using var first = StartSelf("--fixture", firstManifest, Guid.NewGuid().ToString("N"));
        using var second = StartSelf("--fixture", secondManifest, Guid.NewGuid().ToString("N"));
        SessionManager? manager = null;
        try
        {
            WaitFor(() => File.Exists(firstManifest) && File.Exists(secondManifest), "mixed-app fixtures started");
            var a = JsonSerializer.Deserialize<long[]>(File.ReadAllText(firstManifest))!;
            var b = JsonSerializer.Deserialize<long[]>(File.ReadAllText(secondManifest))!;
            var log = new TestLog(); var state = new WorkspaceState();
            var windows = new WindowSystem(state.Settings, Path.Combine(directory, "mixed"), log);
            manager = new SessionManager(state, windows, new CountingStore(new JsonStateStore(Path.Combine(directory,"mixed"),log)), log);
            var session = manager.CreateSession("MIXED"); manager.AddWindow(a[0],session.Id);
            Check(manager.OpenPane(b[0],a[0],PaneOrientation.Vertical), "Windows from two processes form a pane pair");
            Check(manager.OpenPane(a[2],b[0],PaneOrientation.Horizontal,entireCanvas:true), "Third native app spans the bottom beneath both existing apps");
            var bottom = session.Windows.Single(w=>w.Handle==a[2]);
            var canvas = session.PaneCanvases.Single();
            Check(bottom.Layout.Bounds.Width==canvas.MonitorWorkArea.Width && bottom.Layout.Bounds.Height>=600,
                "Native application minimum height reallocates space from the top row");
            Check(manager.OpenPane(b[1],a[2],PaneOrientation.Vertical,entireCanvas:true), "Fourth native app fits beside the complete existing layout");
            var left = session.Windows.Single(w=>w.Handle==a[0]);
            manager.ResizePane(left.Handle,PaneDirection.Right);
            Check(manager.LastError==null && !manager.HidingPaused && PaneTree.Leaves(canvas).Count==4, "Mixed-process resizing keeps all four panes and session handling active");
            foreach(var entry in session.Windows)
                Check(windows.Inspect(entry.Handle)!.VisibleBounds==entry.Layout.Bounds, "Mixed-process visible edges exactly match the pane plan");
            Check(log.Events.Contains("window.layout.batch.applied"), "Mixed-process apps use a verified simultaneous placement batch");
            manager.BeginPaneMoveSize(left.Handle);
            var outer = windows.Inspect(left.Handle)!.Layout.Bounds;
            SetWindowPos((nint)left.Handle,0,outer.X,outer.Y,outer.Width+40,outer.Height,0x0014);
            manager.EndPaneMoveSize(left.Handle);
            Check(manager.LastError==null && PaneTree.Leaves(canvas).Count==4, "Native mouse resize preserves the four-app pane tree");
            var raw = windows.Inspect(left.Handle)!.Layout;
            Check(windows.ApplyLayout(left,raw).Success, "Rollback accepts native invisible borders extending outside the work area");
        }
        finally
        {
            manager?.Shutdown();
            if(!first.HasExited) first.Kill(entireProcessTree:true);
            if(!second.HasExited) second.Kill(entireProcessTree:true);
            first.WaitForExit(3000); second.WaitForExit(3000);
        }
    }

    private static void RunCrashRecovery(string parentDirectory, long handle, long zoomTarget)
    {
        var recoveryDirectory = Path.Combine(parentDirectory, "crash-recovery");
        Directory.CreateDirectory(recoveryDirectory);
        using var worker = StartSelf("--crash-worker", recoveryDirectory, handle.ToString(), zoomTarget.ToString());
        Process? watchdog = null;
        try
        {
            WaitFor(() => File.Exists(Path.Combine(recoveryDirectory, "ready.json")), "crash worker setup");
            var watchdogPid = JsonSerializer.Deserialize<int>(File.ReadAllText(Path.Combine(recoveryDirectory, "ready.json")));
            watchdog = Process.GetProcessById(watchdogPid);
            Check(!Visible(handle), "Crash fixture is hidden after the recovery journal is written");
            Check(File.Exists(Path.Combine(recoveryDirectory, "recovery.json")), "Crash safety journal exists before process termination");
            worker.Kill();
            worker.WaitForExit(3000);
            WaitFor(() => Visible(handle), "watchdog restores the window after manager termination", 12000);
            Check(IsWindow(new IntPtr(handle)), "The recovered native window remains alive");
            WaitFor(() => watchdog.HasExited, "watchdog exits after recovery");
        }
        finally
        {
            if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(3000); }
            RecoveryWatcher.Recover(recoveryDirectory, new TestLog());
            if (watchdog is not null)
            {
                if (!watchdog.HasExited) watchdog.Kill();
                watchdog.Dispose();
            }
        }
    }

    private static int RunCrashWorker(string directory, long handle, long zoomTarget)
    {
        var log = new TestLog();
        var windows = new WindowSystem(new AppSettings(), directory, log);
        var snapshot = windows.Inspect(handle) ?? throw new Exception("Crash fixture disappeared");
        var managed = new ManagedWindow { Handle = handle, Fingerprint = snapshot.Fingerprint, Layout = snapshot.Layout };
        var process = Process.GetCurrentProcess();
        using var watchdog = StartSelf("--watchdog", process.Id.ToString(), process.StartTime.ToUniversalTime().Ticks.ToString(), directory);
        var manager = new SessionManager(new(), windows, new JsonStateStore(directory, log), log);
        var session = manager.CreateSession("Zoom crash fixture");
        if (!manager.AddWindow(handle, session.Id) || !manager.OpenPane(zoomTarget, handle, PaneOrientation.Vertical))
            throw new Exception($"Crash pane setup failed: {manager.LastError}");
        manager.TogglePaneZoom(zoomTarget);
        if (Visible(handle)) throw new Exception($"Crash fixture zoom failed: {manager.LastError}");
        File.WriteAllText(Path.Combine(directory, "ready.json"), JsonSerializer.Serialize(watchdog.Id));
        Thread.Sleep(60000);
        return 3;
    }

    private static int RunFixture(string manifestPath, string token)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var windows = Enumerable.Range(0, 3).Select(index => new Window
        {
            Title = $"DeskMux integration {token} {new[] { "Alpha", "Bravo", "Charlie" }[index]}",
            Left = 90 + index * 45,
            Top = 100 + index * 40,
            Width = 420,
            Height = 290,
            MinHeight = index == 2 ? 620 : 100,
            Background = new SolidColorBrush(Color.FromRgb(25, 32, 45)),
            Content = new TextBlock { Text = $"DeskMux test fixture {index + 1}\nSeparate native window · same process", Foreground = Brushes.White, FontSize = 19, Margin = new Thickness(22) }
        }).ToArray();
        app.Startup += (_, _) =>
        {
            foreach (var window in windows) window.Show();
            windows[1].WindowState = WindowState.Maximized;
            windows[2].WindowState = WindowState.Minimized;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var handles = windows.Select(w => new WindowInteropHelper(w).Handle.ToInt64()).ToArray();
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(handles, JsonOptions));
            };
            timer.Start();
        };
        return app.Run();
    }

    private static Process StartSelf(params string[] args)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new Exception("Could not start the fixture process");
    }

    private static bool Visible(long handle) => IsWindowVisible(new IntPtr(handle));
    private static bool IsAbove(long candidate, long reference)
    {
        for (var window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero; window = GetWindow(window, 2))
        {
            if (window.ToInt64() == candidate) return true;
            if (window.ToInt64() == reference) return false;
        }
        return false;
    }
    private static bool Near(PixelRect a, PixelRect b) => Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2 && Math.Abs(a.Width - b.Width) <= 2 && Math.Abs(a.Height - b.Height) <= 2;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine($"  PASS {description}");
    }

    private static void WaitFor(Func<bool> condition, string description, int timeoutMs = 6000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > timeoutMs) throw new TimeoutException(description);
            Thread.Sleep(35);
        }
        Check(true, description);
    }

    private sealed class CountingStore(IStateStore inner) : IStateStore
    {
        public int Saves { get; private set; }
        public WorkspaceState Load() => inner.Load();
        public void Save(WorkspaceState state) { inner.Save(state); Saves++; }
    }

    private sealed class TestLog : ILog
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, byte> SeenEvents { get; } = new();
        public IEnumerable<string> Events => SeenEvents.Keys;
        public void Write(string eventName, string message, object? data = null)
        {
            SeenEvents.TryAdd(eventName, 0);
            Console.WriteLine($"    [{eventName}] {message}");
        }
    }

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
