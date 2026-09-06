using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskMux.Windows;
using DeskMux.App.UI;

namespace DeskMux.App;

public sealed class AppController : IDisposable
{
    private readonly Application _app;
    private readonly KeyboardManager _keyboard;
    private readonly WinEventTracker _tracker;
    private readonly LaunchCoordinator _launcher;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _paneRequest;
    private readonly DispatcherTimer _saveTimer, _prefixTimer, _refreshTimer, _paneTimer;
    private readonly HwndSource _messageWindow;
    private TrayController? _tray;
    private ManagerWindow? _manager;
    private PrefixOverlay? _prefix;
    private Window? _dialog;
    private long _commandWindow, _lastExternalWindow;
    private long _pickerSource;
    private bool _disposed;
    private bool _recoveryReady;
    private Process? _watchdog;
    public SessionManager Sessions { get; }
    public WindowSystem Windows { get; }
    public StructuredLog Log { get; }
    public string DataDirectory { get; }
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string PrefixLabel => UIHelpers.PrefixLabel(Sessions.State.Settings);

    public AppController(Application app, string directory)
    {
        _app = app; DataDirectory = directory;
        Directory.CreateDirectory(directory);
        Log = new StructuredLog(LogDirectory);
        RecoveryWatcher.Recover(directory, Log);
        var store = new JsonStateStore(directory, Log);
        var state = store.Load();
        ThemeManager.Apply(app, state.Settings.Theme);
        Windows = new WindowSystem(state.Settings, directory, Log);
        Sessions = new SessionManager(state, Windows, store, Log);
        Action<Action> dispatch = action => { if (!_disposed && !_app.Dispatcher.HasShutdownStarted) _app.Dispatcher.BeginInvoke(action); };
        _keyboard = new KeyboardManager(state.Settings, dispatch, Log);
        _tracker = new WinEventTracker(dispatch, Log);
        _launcher = new LaunchCoordinator(Windows, _tracker, Log);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Sessions.Save(); };
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); _manager?.Refresh(); _tray?.Refresh(); };
        _paneTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _paneTimer.Tick += (_, _) => { _paneTimer.Stop(); Sessions.FlushPaneLayoutChanges(); };
        _prefixTimer = new DispatcherTimer();
        _prefixTimer.Tick += (_, _) => CancelPrefix();
        Sessions.Changed += () => { _saveTimer.Stop(); _saveTimer.Start(); if (!_refreshTimer.IsEnabled) _refreshTimer.Start(); };
        _keyboard.PrefixEntered += () =>
        {
            if (_dialog != null) { _keyboard.CancelCommandMode(); return; }
            var overlaySource = Windows.ForegroundWindow;
            _commandWindow = overlaySource;
            if (Windows.Inspect(_commandWindow) == null) _commandWindow = _lastExternalWindow;
            Sessions.TrackWindow(_commandWindow, true);
            _prefix?.Close(); _prefix = new PrefixOverlay(this);
            // Keep commands aimed at the last managed application when DeskMux has focus,
            // while placing the overlay on the monitor where the prefix was invoked.
            UIHelpers.ShowNearMonitor(_prefix, Windows, overlaySource, false);
            _prefixTimer.Interval = TimeSpan.FromMilliseconds(state.Settings.CommandTimeoutMs);
            _prefixTimer.Stop(); _prefixTimer.Start();
        };
        _keyboard.PrefixCancelled += CancelPrefix;
        _keyboard.Command += ExecuteCommand;
        _keyboard.PickerKeyPressed += key =>
        {
            if (_dialog is IKeyboardPicker picker) picker.HandleKey(key);
            else _keyboard.EndPickerMode();
        };
        _keyboard.Emergency += Emergency;
        _tracker.WindowChanged += (handle, foreground) =>
        {
            if (foreground && _dialog is SessionPicker or PanePicker && handle != _pickerSource && handle != new WindowInteropHelper(_dialog).Handle.ToInt64())
                _dialog.Close();
            if (foreground && Windows.Inspect(handle) is { } snapshot && snapshot.Fingerprint.ProcessId != Environment.ProcessId) _lastExternalWindow = handle;
            Sessions.TrackWindow(handle, foreground);
            if (!foreground) { _paneTimer.Stop(); _paneTimer.Start(); }
        };
        _tracker.MoveSizeStarted += handle => Sessions.ReleasePaneForManualMove(handle);
        _messageWindow = new HwndSource(new HwndSourceParameters("DeskMux notifications") { Width = 0, Height = 0, WindowStyle = 0 });
        _messageWindow.AddHook((nint hwnd, int message, nint wparam, nint lparam, ref bool handled) =>
        {
            if (message is 0x007E or 0x02E0 or 0x001A) dispatch(Sessions.HandleDisplayChange);
            return nint.Zero;
        });
    }

    public void Start(bool recoveryOnly)
    {
        _tray = new TrayController(this);
        var watchdogReady = StartWatchdog();
        Sessions.Reconcile();
        if (Sessions.State.Sessions.Count == 0) Sessions.CreateSession("DEV");
        if (recoveryOnly || !watchdogReady) Sessions.ShowAll();
        else if (Sessions.State.ActiveSessionId is { } active) Sessions.SwitchTo(active);
        try { _keyboard.Start(); } catch (Exception ex) { Log.Write("keyboard_hook_error", ex.Message); Notify("Keyboard shortcuts unavailable", ex.Message); }
        try { _tracker.Start(); } catch (Exception ex) { Log.Write("window_tracker_error", ex.Message); Notify("Window tracking unavailable", "Layouts are still recorded when switching sessions. " + ex.Message); }
        _tray.Refresh();
        if (Sessions.State.Settings.ShowManagerOnStartup || !watchdogReady) OpenManager();
        if (ShouldRestoreAppsOnStartup(recoveryOnly, watchdogReady, Sessions.State) && Sessions.State.ActiveSessionId is { } restoreSession)
            _app.Dispatcher.BeginInvoke(() => RestoreSession(restoreSession, quietWhenComplete: true));
        Log.Write("application_started", "DeskMux started", new { Environment.ProcessId });
    }

    internal static bool ShouldRestoreAppsOnStartup(bool recoveryOnly, bool watchdogReady, WorkspaceState state) =>
        !recoveryOnly && watchdogReady && state.Settings.RestoreActiveSessionOnStartup && state.ActiveSessionId is not null;

    private bool StartWatchdog()
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(App).Assembly.Location);
            start.ArgumentList.Add("--watchdog"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            var startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            start.ArgumentList.Add(startTicks.ToString()); start.ArgumentList.Add(DataDirectory);
            _watchdog = Process.Start(start) ?? throw new InvalidOperationException("Recovery process did not start.");
            _watchdog.Exited += (_, _) => { if (!_disposed) _app.Dispatcher.BeginInvoke(() => { _recoveryReady = false; Emergency(); Notify("Recovery helper stopped", "All managed windows have been shown. Restart DeskMux to restore crash protection."); }); };
            _watchdog.EnableRaisingEvents = true;
            var readyFile = RecoveryWatcher.ReadyFile(Environment.ProcessId, startTicks, DataDirectory);
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(readyFile) && !_watchdog.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(5)) System.Threading.Thread.Sleep(25);
            if (!File.Exists(readyFile) || _watchdog.HasExited) throw new InvalidOperationException("Recovery helper did not become ready. Session hiding remains paused.");
            _recoveryReady = true;
            return true;
        }
        catch (Exception ex) { Log.Write("watchdog_start_failed", ex.Message); Notify("Recovery helper unavailable", "Session hiding is paused. " + ex.Message); return false; }
    }

    public void OpenManager(string page = "Sessions")
    {
        CancelPrefix();
        if (_manager == null) { _manager = new ManagerWindow(this); _manager.Closed += (_, _) => _manager = null; }
        _manager.Navigate(page); _manager.Show(); if (_manager.WindowState == WindowState.Minimized) _manager.WindowState = WindowState.Normal; _manager.Activate();
    }
    public void CancelPrefix() { _prefixTimer.Stop(); _keyboard.CancelCommandMode(); _prefix?.Close(); _prefix = null; }
    private void ExecuteCommand(CommandGesture gesture)
    {
        CancelPrefix();
        if (_dialog != null) { if (_dialog is not SessionPicker and not PanePicker) _keyboard.EndPickerMode(); return; }
        var key = gesture.VirtualKey;
        if (key is >= 0x25 and <= 0x28)
        {
            var direction = key switch { 0x25 => PaneDirection.Left, 0x26 => PaneDirection.Up, 0x27 => PaneDirection.Right, _ => PaneDirection.Down };
            if (gesture.Modifiers.HasFlag(PrefixModifiers.Control)) Sessions.ResizePane(_commandWindow, direction);
            else Sessions.NavigatePane(_commandWindow, direction);
            ReportError(); return;
        }
        if (key is >= 0x31 and <= 0x39) { var i = key - 0x31; if (i < Sessions.State.Sessions.Count) Switch(Sessions.State.Sessions[i].Id); return; }
        switch (key)
        {
            case 0x4B: Sessions.SwitchRelative(-1); break;
            case 0x4A: Sessions.SwitchRelative(1); break;
            case 0xDC: OpenPane(PaneOrientation.Vertical, _commandWindow); return;
            case 0xBD: OpenPane(PaneOrientation.Horizontal, _commandWindow); return;
            case 0xDE when gesture.Modifiers.HasFlag(PrefixModifiers.Shift): OpenPane(PaneOrientation.Horizontal, _commandWindow); return;
            case 0xDB when gesture.Modifiers.HasFlag(PrefixModifiers.Shift): Sessions.SwapPane(_commandWindow, -1); break;
            case 0xDD when gesture.Modifiers.HasFlag(PrefixModifiers.Shift): Sessions.SwapPane(_commandWindow, 1); break;
            case 0x5A: Sessions.TogglePaneZoom(_commandWindow); break;
            case 0x57: PickSession(false, _commandWindow); break;
            case 0x43: CreateSession(); break;
            case 0x52: if (Sessions.ActiveSession != null) Rename(Sessions.ActiveSession.Id); break;
            case 0x4D: PickSession(true, _commandWindow); break;
            case 0x41: AddForeground(); break;
            case 0x58:
                var window = Sessions.ActiveSession?.Windows.FirstOrDefault(w => w.Handle == _commandWindow && !w.IsMissing);
                if (window != null) Sessions.RemoveWindow(window.Id); else Notify("Window is not in this session", "Focus a managed application and try again.");
                break;
            case 0x44: Sessions.Detach(); break;
            case 0x4C: Sessions.SwitchPrevious(); break;
            case 0x53: OpenManager(); break;
        }
        ReportError();
    }
    public void Switch(Guid id) { CancelPrefix(); Sessions.SwitchTo(id); ReportError(); }
    private void AddForeground()
    {
        if (Sessions.ActiveSession == null) return;
        if (!Sessions.AddWindowToActiveLayout(_commandWindow)) Notify("Could not add window", Sessions.LastError ?? "Focus a supported application window and try again.");
        else
        {
            var entry = Sessions.ActiveSession.Windows.FirstOrDefault(w => !w.IsMissing && w.Handle == _commandWindow);
            var placement = entry != null && Sessions.PaneStatus(entry.Id).StartsWith("Pane", StringComparison.Ordinal) ? " as a pane" : "";
            Notify("Window added", "Added" + placement + " to " + Sessions.ActiveSession.Name + ".");
        }
    }
    public async void OpenPane(PaneOrientation orientation, long sourceHandle = 0)
    {
        if (_disposed) return;
        CancelPrefix();
        if (_dialog != null || _paneRequest != null) { if (_dialog is not SessionPicker and not PanePicker) _keyboard.EndPickerMode(); return; }
        if (Sessions.ActiveSession is not { } session) { _keyboard.EndPickerMode(); Notify("Choose a session", "Create or switch to a session before opening a pane."); return; }
        var sessionId = session.Id;
        // Only an existing member captured at picker opening can become the source.
        var source = session.Windows.FirstOrDefault(w => w.Handle == sourceHandle && !w.IsMissing);
        var sourceIdentity = source == null ? null : CopyIdentity(source);
        var returnSnapshot = Windows.Inspect(sourceHandle);
        var returnWindow = returnSnapshot == null ? null : FromSnapshot(returnSnapshot);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _paneRequest = request;
        try
        {
            var windows = Windows.EnumerateWindows(includeHidden: true)
                .Where(w => w.IsVisible || Sessions.State.Sessions.Any(s => s.Windows.Any(m => !m.IsMissing && m.Handle == w.Handle)))
                .OrderBy(w => w.Fingerprint.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.Fingerprint.Title, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Handle)
                .Select(w => RunningPaneChoice(w, sessionId)).ToList();
            var launchers = Sessions.State.Settings.LaunchProfiles.Select(p => new PaneChoice(null, CopyProfile(p), "Launch application")).ToList();
            var picker = new PanePicker(windows, launchers);
            var choice = await ShowPanePickerAsync(picker, sourceHandle, request.Token);
            if (choice == null || request.IsCancellationRequested)
            {
                if (!_disposed && !request.IsCancellationRequested && picker.CancelledByKeyboard && returnWindow != null && Windows.IsSameWindow(returnWindow)) Windows.Focus(returnWindow);
                return;
            }
            WindowSnapshot? target = choice.Window;
            AppLaunchProfile? launchedProfile = null;
            if (choice.Launcher is { } profile)
            {
                launchedProfile = profile;
                var progress = new LaunchProgressWindow(profile.Name);
                _dialog = progress;
                EventHandler cancelLaunch = (_, _) => { if (ReferenceEquals(_dialog, progress)) _dialog = null; request.Cancel(); };
                progress.Closed += cancelLaunch;
                UIHelpers.ShowNearMonitor(progress, Windows, sourceHandle, false);
                var result = await _launcher.LaunchAsync(profile, request.Token);
                var cancelled = request.IsCancellationRequested;
                progress.Closed -= cancelLaunch;
                _dialog = null;
                if (progress.IsVisible) progress.Close();
                if (_disposed || cancelled || result.Status == LaunchStatus.Cancelled) return;
                if (result.Status == LaunchStatus.Ambiguous)
                {
                    target = (await ShowPanePickerAsync(new PanePicker(result.Candidates.Select(w => RunningPaneChoice(w, sessionId)), [], "Choose application window", true), sourceHandle, request.Token))?.Window;
                    if (target == null) return;
                }
                else if (result.Success) target = result.Window;
                else { Notify("Could not open pane", result.Error ?? "The application opened without a manageable window. Your layout is unchanged."); return; }
            }
            if (_disposed || request.IsCancellationRequested || target == null) return;
            if (Sessions.State.ActiveSessionId != sessionId)
            { Notify("Pane request cancelled", "The active session changed. The selected application has been left unchanged."); return; }
            var targetIdentity = FromSnapshot(target);
            if (!Windows.IsSameWindow(targetIdentity)) { Notify("Window no longer available", "Choose another application window. Your layout is unchanged."); return; }
            var owner = Sessions.State.Sessions.FirstOrDefault(s => s.Windows.Any(w => !w.IsMissing && w.Handle == target.Handle));
            var allowMove = owner != null && owner.Id != sessionId;
            if (allowMove && !ConfirmPaneMove(target, owner!.Name, session.Name)) return;
            if (_disposed || request.IsCancellationRequested || Sessions.State.ActiveSessionId != sessionId || !Windows.IsSameWindow(targetIdentity)) return;
            var actualSource = sourceIdentity != null && session.Windows.Any(w => w.Id == sourceIdentity.Id && !w.IsMissing) && Windows.IsSameWindow(sourceIdentity) ? sourceIdentity.Handle : 0;
            if (Sessions.OpenPane(target.Handle, actualSource, orientation, allowMove) && launchedProfile is not null)
            {
                var restoredEntry = session.Windows.FirstOrDefault(w => !w.IsMissing && w.Handle == target.Handle);
                if (restoredEntry is not null) Sessions.SetLaunchProfile(restoredEntry.Id, launchedProfile.Id);
            }
            ReportError();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Log.Write("pane.open.failed", exception.Message); if (!_disposed) Notify("Could not open pane", exception.Message); }
        finally
        {
            if (_dialog is LaunchProgressWindow progress) progress.Close();
            if (ReferenceEquals(_paneRequest, request)) _paneRequest = null;
            if (!_disposed) _keyboard.EndPickerMode();
        }
    }

    private PaneChoice RunningPaneChoice(WindowSnapshot window, Guid sessionId)
    {
        var owner = Sessions.State.Sessions.FirstOrDefault(s => s.Windows.Any(w => !w.IsMissing && w.Handle == window.Handle));
        return new PaneChoice(window, null, owner == null ? "Unmanaged" : owner.Id == sessionId ? "Current session" : "Session: " + owner.Name);
    }
    private Task<PaneChoice?> ShowPanePickerAsync(PanePicker picker, long sourceHandle, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<PaneChoice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pickerSource = Windows.ForegroundWindow; _dialog = picker;
        var registration = cancellation.Register(() => _app.Dispatcher.BeginInvoke(() => { if (picker.IsVisible) picker.Close(); }));
        picker.Closed += (_, _) =>
        {
            registration.Dispose();
            if (ReferenceEquals(_dialog, picker)) _dialog = null;
            _keyboard.EndPickerMode(); completion.TrySetResult(picker.SelectedChoice);
        };
        _keyboard.BeginPickerMode();
        try { UIHelpers.ShowNearMonitor(picker, Windows, sourceHandle, false); }
        catch { registration.Dispose(); _keyboard.EndPickerMode(); _dialog = null; throw; }
        return completion.Task;
    }
    private bool ConfirmPaneMove(WindowSnapshot window, string source, string target)
    {
        var dialog = new ConfirmMoveDialog(window.Fingerprint.Title, source, target);
        if (_manager?.IsVisible == true) dialog.Owner = _manager;
        _dialog = dialog;
        try { return dialog.ShowDialog() == true; } finally { _dialog = null; }
    }
    private static ManagedWindow FromSnapshot(WindowSnapshot snapshot) => new() { Handle = snapshot.Handle, Fingerprint = snapshot.Fingerprint, Layout = snapshot.Layout };
    private static ManagedWindow CopyIdentity(ManagedWindow window) => new() { Id = window.Id, Handle = window.Handle, Fingerprint = window.Fingerprint, Layout = window.Layout };
    private static AppLaunchProfile CopyProfile(AppLaunchProfile profile) => new() { Id = profile.Id, Name = profile.Name, Target = profile.Target, Arguments = profile.Arguments, WorkingDirectory = profile.WorkingDirectory, ExpectedProcessName = profile.ExpectedProcessName };
    public void PickSession(bool move, long handle = 0, Guid? entryId = null)
    {
        CancelPrefix();
        if (_dialog != null) { if (_dialog is not SessionPicker and not PanePicker) _keyboard.EndPickerMode(); return; }
        var source = Windows.Inspect(handle);
        if (move && entryId == null && source == null) { _keyboard.EndPickerMode(); Notify("Choose an application window", "Focus the window, then press " + PrefixLabel + " followed by M. You can also select a window in the session manager."); return; }
        var returnWindow = source == null ? null : new ManagedWindow { Handle = source.Handle, Fingerprint = source.Fingerprint, Layout = source.Layout };
        var picker = new SessionPicker(Sessions.State, move ? "Move window to…" : "Switch session");
        _pickerSource = Windows.ForegroundWindow;
        _dialog = picker;
        picker.Closed += (_, _) =>
        {
            _dialog = null;
            _keyboard.EndPickerMode();
            // Let WPF finish closing before choosing foreground, otherwise its window
            // teardown can undo the session's requested focus.
            _app.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                if (picker.SelectedSessionId is not { } target)
                {
                    if (picker.CancelledByKeyboard && returnWindow != null) Windows.Focus(returnWindow);
                    return;
                }
                if (!move) Switch(target);
                else
                {
                    var entry = entryId ?? Sessions.State.Sessions.SelectMany(s => s.Windows).FirstOrDefault(w => w.Handle == handle && !w.IsMissing)?.Id;
                    if (entry is { } id) Sessions.MoveWindow(id, target); else Sessions.AddWindow(handle, target);
                    if (returnWindow == null || Windows.Inspect(returnWindow.Handle)?.IsVisible != true || !Windows.Focus(returnWindow).Success) Sessions.RestoreActiveFocus();
                    ReportError();
                }
            });
        };
        _keyboard.BeginPickerMode();
        try { UIHelpers.ShowNearMonitor(picker, Windows, handle, false); }
        catch { _keyboard.EndPickerMode(); _dialog = null; picker.Close(); throw; }
    }
    public void CreateSession(bool capture = false)
    {
        var name = Prompt("Create session", "Give this workspace a name.", "New session");
        if (name == null) return;
        var session = Sessions.CreateSession(name);
        if (capture) CaptureWindows(session.Id);
        Switch(session.Id); _manager?.SelectSession(session.Id);
    }
    public void Rename(Guid id)
    {
        var session = Sessions.State.Sessions.FirstOrDefault(s => s.Id == id); if (session == null) return;
        var name = Prompt("Rename session", "Choose a name you can recognize at a glance.", session.Name);
        if (name != null) { Sessions.RenameSession(id, name); ReportError(); }
    }
    private string? Prompt(string title, string description, string initial)
    {
        CancelPrefix(); if (_dialog != null) return null;
        var dialog = new NameDialog(title, description, initial); if (_manager?.IsVisible == true) dialog.Owner = _manager;
        _dialog = dialog;
        try { return dialog.ShowDialog() == true ? dialog.Value : null; } finally { _dialog = null; }
    }
    public void CaptureWindows(Guid target)
    {
        CancelPrefix(); if (_dialog != null) return;
        var choices = Windows.EnumerateWindows().Select(w => new CaptureChoice(w, Sessions.State.Sessions.FirstOrDefault(s => s.Windows.Any(m => m.Handle == w.Handle && !m.IsMissing))?.Name)).ToList();
        var dialog = new CaptureDialog(choices); if (_manager?.IsVisible == true) dialog.Owner = _manager;
        _dialog = dialog;
        try
        {
            if (dialog.ShowDialog() == true)
                foreach (var choice in choices.Where(c => c.Selected))
                {
                    var existing = Sessions.State.Sessions.SelectMany(s => s.Windows).FirstOrDefault(w => !w.IsMissing && w.Handle == choice.Snapshot.Handle);
                    if (existing != null) Sessions.MoveWindow(existing.Id, target); else Sessions.AddWindow(choice.Snapshot.Handle, target);
                }
        }
        finally { _dialog = null; }
        ReportError();
    }

    public async void RestoreSession(Guid sessionId, bool quietWhenComplete = false)
    {
        if (_disposed || _paneRequest != null || _dialog != null) return;
        CancelPrefix();
        var session = Sessions.State.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;
        Sessions.Reconcile();
        Switch(sessionId);
        var missing = session.Windows.Where(w => w.IsMissing).ToArray();
        if (missing.Length == 0)
        {
            if (!quietWhenComplete) Notify("Session ready", session.Name + " has no missing applications.");
            return;
        }

        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _paneRequest = request;
        var restored = 0;
        var failures = new List<string>();
        try
        {
            foreach (var entry in missing)
            {
                request.Token.ThrowIfCancellationRequested();
                var profile = RestoreProfile(entry);
                if (profile is null) { failures.Add(entry.DisplayTitle + ": no executable or launcher is saved"); continue; }
                var progress = new LaunchProgressWindow(profile.Name, restoring: true);
                _dialog = progress;
                EventHandler cancel = (_, _) => { if (ReferenceEquals(_dialog, progress)) _dialog = null; request.Cancel(); };
                progress.Closed += cancel;
                UIHelpers.ShowNearMonitor(progress, Windows, 0, false);
                var result = await _launcher.LaunchAsync(profile, request.Token);
                progress.Closed -= cancel;
                _dialog = null;
                if (progress.IsVisible) progress.Close();
                if (request.IsCancellationRequested || result.Status == LaunchStatus.Cancelled) break;
                WindowSnapshot? candidate = result.Window;
                if (result.Status == LaunchStatus.Ambiguous)
                    candidate = (await ShowPanePickerAsync(new PanePicker(result.Candidates.Select(w => RunningPaneChoice(w, sessionId)), [], "Choose restored application", true), 0, request.Token))?.Window;
                if (candidate is null) { failures.Add(entry.DisplayTitle + ": " + (result.Error ?? "no manageable window appeared")); continue; }
                if (Sessions.RestoreMissingWindow(entry.Id, candidate)) restored++;
                else failures.Add(entry.DisplayTitle + ": " + (Sessions.LastError ?? "placement could not be restored"));
            }
            if (!_disposed)
            {
                // Restoring an inactive session can continue in the background. Do not pull
                // the user back if they deliberately switched elsewhere during a slow launch.
                if (Sessions.State.ActiveSessionId == sessionId) Switch(sessionId);
                var remaining = session.Windows.Count(w => w.IsMissing);
                if (!quietWhenComplete || failures.Count > 0)
                    Notify(remaining == 0 && failures.Count == 0 ? "Session restored" : "Session partly restored",
                        $"Restored {restored} application{(restored == 1 ? "" : "s")}." +
                        (remaining > 0 ? $" {remaining} still missing." : "") +
                        (failures.Count > 0 ? " " + string.Join("; ", failures.Take(3)) : ""));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Log.Write("session.restore.failed", exception.Message); if (!_disposed) Notify("Session restore stopped", exception.Message); }
        finally
        {
            if (_dialog is LaunchProgressWindow progress) progress.Close();
            if (ReferenceEquals(_paneRequest, request)) _paneRequest = null;
            if (!_disposed) _keyboard.EndPickerMode();
        }
    }

    private AppLaunchProfile? RestoreProfile(ManagedWindow entry)
    {
        if (entry.LaunchProfileId is { } profileId && Sessions.State.Settings.LaunchProfiles.FirstOrDefault(p => p.Id == profileId) is { } saved)
            return CopyProfile(saved);
        var executable = entry.Fingerprint.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return new AppLaunchProfile { Name = string.IsNullOrWhiteSpace(entry.Fingerprint.ProcessName) ? entry.DisplayTitle : entry.Fingerprint.ProcessName,
            Target = executable, ExpectedProcessName = entry.Fingerprint.ProcessName };
    }
    public void Delete(Guid id)
    {
        var session = Sessions.State.Sessions.FirstOrDefault(s => s.Id == id); if (session == null || _dialog != null) return;
        var target = Sessions.ActiveSession?.Id != id ? Sessions.ActiveSession : null;
        var dialog = new DeleteDialog(session.Name, target?.Name); if (_manager?.IsVisible == true) dialog.Owner = _manager;
        _dialog = dialog;
        try { if (dialog.ShowDialog() == true) Sessions.DeleteSession(id, dialog.MoveToCurrent ? target?.Id : null); }
        finally { _dialog = null; }
        ReportError();
    }
    public void SettingsChanged() { _keyboard.RefreshSettings(); Sessions.Save(); _tray?.Refresh(); _manager?.RefreshStatus(); }
    public void ApplyTheme(string preset, ThemePalette? custom = null)
    {
        if (custom is not null) Sessions.State.Settings.Theme.Custom = ThemeManager.Clone(custom);
        Sessions.State.Settings.Theme.Preset = preset;
        ThemeManager.Apply(_app, Sessions.State.Settings.Theme);
        SettingsChanged();
    }
    public void EditLauncher(AppLaunchProfile? profile = null, string? executable = null)
    {
        CancelPrefix(); if (_dialog != null) return;
        var draft = profile == null ? new AppLaunchProfile { Target = executable ?? "", Name = Path.GetFileNameWithoutExtension(executable ?? ""), ExpectedProcessName = Path.GetFileNameWithoutExtension(executable ?? "") } : CopyProfile(profile);
        var dialog = new LaunchProfileDialog(draft);
        if (_manager?.IsVisible == true) dialog.Owner = _manager;
        _dialog = dialog;
        try
        {
            if (dialog.ShowDialog() != true) return;
            var profiles = Sessions.State.Settings.LaunchProfiles;
            var index = profile == null ? -1 : profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) profiles[index] = dialog.Profile; else profiles.Add(dialog.Profile);
            SettingsChanged();
            _manager?.Navigate("Launchers");
        }
        finally { _dialog = null; }
    }
    public void ToggleKeyboard() { Sessions.State.Settings.KeyboardPaused = !Sessions.State.Settings.KeyboardPaused; SettingsChanged(); }
    public void Emergency() { CancelPrefix(); _paneRequest?.Cancel(); _keyboard.EndPickerMode(); if (_dialog is IKeyboardPicker) _dialog.Close(); Sessions.ShowAll(); _manager?.Refresh(); _tray?.Refresh(); Notify("All managed windows shown", "Session hiding is paused. Use Resume sessions when you are ready."); }
    public void Resume()
    {
        if (!_recoveryReady) { Notify("Recovery helper unavailable", "Restart DeskMux before resuming session hiding."); return; }
        Sessions.ResumeHiding(); ReportError();
    }
    public void ReportError() { if (Sessions.LastError is { Length: > 0 } error) Notify("DeskMux needs attention", error); }
    public void Notify(string title, string message) => _tray?.Notify(title, message);
    public void OpenDirectory(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DeskMux", AppInfo.Version));
            using var response = await client.GetAsync(AppInfo.ReleasesApiUrl, _lifetime.Token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(_lifetime.Token));
            if (!ReleaseVersion.TryParse(AppInfo.Version, out var current)) throw new InvalidOperationException("The installed version is invalid.");
            string? latestTag = null, latestUrl = null; var latest = current;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean()) continue;
                var tag = release.GetProperty("tag_name").GetString();
                if (!ReleaseVersion.TryParse(tag, out var candidate) || candidate.CompareTo(latest) <= 0) continue;
                latest = candidate; latestTag = tag; latestUrl = release.GetProperty("html_url").GetString();
            }
            if (latestTag == null)
            {
                MessageBox.Show("You are running the newest available version (" + AppInfo.Version + ").", "DeskMux update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show("DeskMux " + latestTag.TrimStart('v', 'V') + " is available.\n\nOpen the download page?", "DeskMux update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                OpenUrl(latestUrl ?? AppInfo.ReleasesUrl);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception exception)
        {
            Log.Write("update_check_failed", exception.Message);
            MessageBox.Show("DeskMux could not check for updates.\n\n" + exception.Message, "DeskMux update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    public void Exit() { Dispose(); _app.Shutdown(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _lifetime.Cancel(); _paneRequest?.Cancel();
        if (_dialog is IKeyboardPicker) _dialog.Close();
        _saveTimer.Stop(); _refreshTimer.Stop(); _paneTimer.Stop(); CancelPrefix(); _launcher.Dispose(); _keyboard.Dispose(); _tracker.Dispose();
        Sessions.Shutdown(); _tray?.Dispose(); _messageWindow.Dispose(); _watchdog?.Dispose();
        _lifetime.Dispose();
    }
}
