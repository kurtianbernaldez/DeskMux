using System.Diagnostics;
using DeskMux.Core;

namespace DeskMux.Windows;

/// <summary>Starts configured executables and observes their windows without changing application state.</summary>
public sealed class LaunchCoordinator : IDisposable
{
    private readonly LaunchDetectionCoordinator _coordinator;
    private readonly NativeLaunchEnvironment _environment;

    public LaunchCoordinator(IWindowSystem windows, WinEventTracker tracker, ILog log, LaunchDetectionOptions? options = null)
    {
        _environment = new(windows, tracker, log);
        _coordinator = new(_environment, options);
    }

    public Task<LaunchResult> LaunchAsync(AppLaunchProfile profile, CancellationToken cancellationToken = default) =>
        _coordinator.LaunchAsync(profile, cancellationToken);

    public void Dispose() => _coordinator.Dispose();

    private sealed class NativeLaunchEnvironment(IWindowSystem windows, WinEventTracker tracker, ILog log) : ILaunchEnvironment
    {
        public event Action<LaunchWindowEvent>? WindowObserved
        {
            add => tracker.WindowObserved += value;
            remove => tracker.WindowObserved -= value;
        }

        public long MonotonicMilliseconds => Environment.TickCount64;
        public long ForegroundWindow => windows.ForegroundWindow;
        public IReadOnlyList<WindowSnapshot> EnumerateWindows(bool includeHidden = false) => windows.EnumerateWindows(includeHidden)
            .Where(window => NativeDesktop.CanControl((nint)window.Handle)).ToArray();
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);

        public LaunchedProcess? Start(AppLaunchProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Target)) throw new ArgumentException("Choose an executable for this launcher.");
            var target = profile.Target.Trim();
            // Profiles describe executable files. No cmd.exe, PowerShell, shell verbs, URI
            // associations, or shell-constructed command string is involved in starting them.
            if (!string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The launcher target must be an executable (.exe) file.");
            var start = new ProcessStartInfo
            {
                FileName = target,
                Arguments = profile.Arguments ?? "",
                WorkingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory) ? "" : profile.WorkingDirectory.Trim(),
                UseShellExecute = false
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not return a launched process.");
            try
            {
                var identity = new LaunchedProcess(process.Id, process.StartTime.ToUniversalTime().Ticks);
                RecoveryWatcher.SafeLog(log, "application.launched", profile.Name);
                return identity;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Single-instance launchers may exit before their start time can be read.
                // Detection then requires the configured process identity and fresh activity.
                RecoveryWatcher.SafeLog(log, "application.launch.identity.unavailable", exception.Message);
                return null;
            }
        }
    }
}
