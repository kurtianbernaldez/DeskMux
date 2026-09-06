using System.Collections.Concurrent;

namespace DeskMux.Core;

public enum LaunchStatus { Success, Ambiguous, Failed, TimedOut, Cancelled }

public sealed record LaunchResult(LaunchStatus Status, IReadOnlyList<WindowSnapshot> Candidates, string? Error = null)
{
    public bool Success => Status == LaunchStatus.Success;
    public WindowSnapshot? Window => Success && Candidates.Count == 1 ? Candidates[0] : null;
}

public sealed record LaunchedProcess(int ProcessId, long ProcessStartTimeUtcTicks);
public enum LaunchWindowEventKind { Shown, Foreground }
public sealed record LaunchWindowEvent(long Handle, LaunchWindowEventKind Kind, long OccurredAtMilliseconds);

public sealed class LaunchDetectionOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(900);
    public TimeSpan MinimumObservationTime { get; init; } = TimeSpan.FromMilliseconds(1600);
}

/// <summary>All native activity is supplied by the host; Core does not start processes or call Win32.</summary>
public interface ILaunchEnvironment
{
    event Action<LaunchWindowEvent>? WindowObserved;
    long MonotonicMilliseconds { get; }
    long ForegroundWindow { get; }
    IReadOnlyList<WindowSnapshot> EnumerateWindows(bool includeHidden = false);
    LaunchedProcess? Start(AppLaunchProfile profile);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Deterministic identity and event classifier. Only eligible snapshots supplied by the host are considered.</summary>
public static class LaunchCandidateClassifier
{
    public static IReadOnlyList<WindowSnapshot> Select(
        AppLaunchProfile profile, LaunchedProcess? process,
        IReadOnlyList<WindowSnapshot> before, IReadOnlyList<WindowSnapshot> current,
        IReadOnlyList<LaunchWindowEvent> events, long launchedAtMilliseconds)
    {
        var candidates = new List<(WindowSnapshot Window, int Rank)>();
        foreach (var window in current)
        {
            if (!window.IsVisible || window.Handle == 0 || window.Layout.Bounds.Width <= 0 || window.Layout.Bounds.Height <= 0 ||
                window.Fingerprint.ProcessStartTimeUtcTicks <= 0 || IsSplash(window)) continue;
            var existing = before.FirstOrDefault(old => old.Handle == window.Handle && SameProcess(old.Fingerprint, window.Fingerprint));
            var owned = process is not null && process.ProcessId > 0 && process.ProcessStartTimeUtcTicks > 0 &&
                window.Fingerprint.ProcessId == process.ProcessId && window.Fingerprint.ProcessStartTimeUtcTicks == process.ProcessStartTimeUtcTicks;
            var matches = MatchesProfile(profile, window.Fingerprint);
            if (!owned && !matches) continue;
            if (existing is null)
            {
                candidates.Add((window, owned ? 0 : 1));
                continue;
            }
            // A current foreground HWND alone is insufficient: require an actual event or
            // observed foreground transition after launch, not a delayed pre-launch callback.
            var activity = events.Where(item => item.Handle == window.Handle && item.OccurredAtMilliseconds >= launchedAtMilliseconds).ToArray();
            if (matches && activity.Any(item => item.Kind == LaunchWindowEventKind.Shown)) candidates.Add((window, 2));
            else if (matches && activity.Any(item => item.Kind == LaunchWindowEventKind.Foreground)) candidates.Add((window, 3));
        }
        if (candidates.Count == 0) return [];
        var bestRank = candidates.Min(item => item.Rank);
        return candidates.Where(item => item.Rank == bestRank).Select(item => item.Window)
            .DistinctBy(item => item.Handle).OrderBy(item => item.Handle).ToArray();
    }

    public static bool MatchesProfile(AppLaunchProfile profile, WindowFingerprint fingerprint)
    {
        var expected = Path.GetFileNameWithoutExtension(profile.ExpectedProcessName.Trim());
        if (!string.IsNullOrEmpty(expected))
            return string.Equals(expected, fingerprint.ProcessName, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(fingerprint.ExecutablePath) && Path.IsPathFullyQualified(profile.Target))
        {
            try { return string.Equals(Path.GetFullPath(profile.Target), Path.GetFullPath(fingerprint.ExecutablePath), StringComparison.OrdinalIgnoreCase); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        }
        // A relative executable may be resolved through PATH. Match the complete process
        // name, never substrings of titles or names, and never an unrecognized URI target.
        return !profile.Target.Contains("://", StringComparison.Ordinal) &&
            string.Equals(Path.GetFileNameWithoutExtension(profile.Target), fingerprint.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSplash(WindowSnapshot window) =>
        window.Fingerprint.WindowClass.Contains("splash", StringComparison.OrdinalIgnoreCase) ||
        window.Fingerprint.Title.Contains("splash", StringComparison.OrdinalIgnoreCase);

    private static bool SameProcess(WindowFingerprint a, WindowFingerprint b) =>
        a.ProcessId == b.ProcessId && a.ProcessStartTimeUtcTicks == b.ProcessStartTimeUtcTicks &&
        string.Equals(a.WindowClass, b.WindowClass, StringComparison.Ordinal);
}

/// <summary>Observes a launch without ever changing memberships, visibility, focus, or window placement.</summary>
public sealed class LaunchDetectionCoordinator : IDisposable
{
    private readonly ILaunchEnvironment _environment;
    private readonly LaunchDetectionOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private bool _disposed;
    private bool _running;

    public LaunchDetectionCoordinator(ILaunchEnvironment environment, LaunchDetectionOptions? options = null)
    {
        _environment = environment;
        _options = options ?? new();
        if (_options.Timeout <= TimeSpan.Zero || _options.PollInterval <= TimeSpan.Zero ||
            _options.SettleTime < TimeSpan.Zero || _options.MinimumObservationTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Launch observation intervals must be positive.");
    }

    public async Task<LaunchResult> LaunchAsync(AppLaunchProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var launchProfile = new AppLaunchProfile
        {
            Id = profile.Id, Name = profile.Name, Target = profile.Target, Arguments = profile.Arguments,
            WorkingDirectory = profile.WorkingDirectory, ExpectedProcessName = profile.ExpectedProcessName
        };
        CancellationTokenSource linked;
        lock (_gate)
        {
            if (_disposed) return new(LaunchStatus.Cancelled, [], "Launch observation stopped.");
            if (_running) return new(LaunchStatus.Failed, [], "Another application launch is still being observed.");
            _running = true;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        using (linked)
        {
            try { return await Task.Run(() => ObserveAsync(launchProfile, linked.Token), linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return new(LaunchStatus.Cancelled, [], "Launch cancelled. Any started application was left unchanged."); }
            catch (Exception exception) { return new(LaunchStatus.Failed, [], $"The application could not be opened: {exception.Message}"); }
            finally { lock (_gate) _running = false; }
        }
    }

    private async Task<LaunchResult> ObserveAsync(AppLaunchProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var events = new ConcurrentQueue<LaunchWindowEvent>();
        void Observed(LaunchWindowEvent item) => events.Enqueue(item);
        _environment.WindowObserved += Observed;
        try
        {
            var before = _environment.EnumerateWindows(includeHidden: true);
            var lastForeground = _environment.ForegroundWindow;
            cancellationToken.ThrowIfCancellationRequested();
            var launchTime = _environment.MonotonicMilliseconds;
            var process = _environment.Start(profile);
            var changedAt = launchTime;
            string signature = "";
            IReadOnlyList<WindowSnapshot> candidates = [];
            var observedEvents = new List<LaunchWindowEvent>();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = _environment.MonotonicMilliseconds;
                var current = _environment.EnumerateWindows();
                while (events.TryDequeue(out var item)) observedEvents.Add(item);
                var foreground = _environment.ForegroundWindow;
                if (foreground != lastForeground && foreground != 0)
                    observedEvents.Add(new(foreground, LaunchWindowEventKind.Foreground, now));
                lastForeground = foreground;
                candidates = LaunchCandidateClassifier.Select(profile, process, before, current, observedEvents, launchTime);
                var nextSignature = string.Join(";", candidates.Select(item =>
                    $"{item.Handle}:{item.Fingerprint.ProcessId}:{item.Fingerprint.ProcessStartTimeUtcTicks}:{item.Fingerprint.Title}:{item.Layout.Bounds}"));
                if (nextSignature != signature) { signature = nextSignature; changedAt = now; }
                var stable = now - changedAt >= _options.SettleTime.TotalMilliseconds;
                var observedEnough = now - launchTime >= _options.MinimumObservationTime.TotalMilliseconds;
                if (stable && observedEnough && candidates.Count > 0)
                    return CandidateResult(candidates);
                if (now - launchTime >= _options.Timeout.TotalMilliseconds)
                {
                    // Unsettled windows remain ambiguous; do not tile an application still
                    // replacing its startup windows when the observation deadline expires.
                    if (stable && candidates.Count > 0) return CandidateResult(candidates);
                    return new(LaunchStatus.TimedOut, [], "The application started, but no stable manageable window appeared. The application was left unchanged.");
                }
                await _environment.DelayAsync(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _environment.WindowObserved -= Observed; }
    }

    private static LaunchResult CandidateResult(IReadOnlyList<WindowSnapshot> candidates) => candidates.Count == 1
        ? new(LaunchStatus.Success, candidates)
        : new(LaunchStatus.Ambiguous, candidates, "Several application windows appeared. Choose the window to open in the pane.");

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
