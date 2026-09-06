using DeskMux.Core;

internal static class LaunchTests
{
    private static readonly AppLaunchProfile Profile = new() { Name = "Editor", Target = @"C:\Apps\editor.exe", ExpectedProcessName = "editor" };
    private static readonly LaunchedProcess Process = new(100, 1000);
    private static WindowSnapshot Window(long handle = 1, int pid = 100, long started = 1000, string title = "Document", string name = "editor", string windowClass = "EditorWindow") => new(handle,
        new() { ProcessId = pid, ProcessStartTimeUtcTicks = started, ProcessName = name, ExecutablePath = @"C:\Apps\" + name + ".exe", WindowClass = windowClass, Title = title },
        new() { Bounds = new(0, 0, 900, 600) }, true);
    private static IReadOnlyList<WindowSnapshot> Select(IReadOnlyList<WindowSnapshot> current,
        IReadOnlyList<WindowSnapshot>? before = null, IReadOnlyList<LaunchWindowEvent>? events = null, LaunchedProcess? process = null) =>
        LaunchCandidateClassifier.Select(Profile, process ?? Process, before ?? [], current, events ?? [], 100);

    public static int Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Launch: returned process window has priority", () => {
                var a = Window(); var b = Window(2, 200, 2000);
                Check.Sequence(new[] { 1L }, Select([b, a]).Select(item => item.Handle));
            }),
            ("Launch: multiprocess application matches configured identity", () => {
                Check.Equal(2L, Select([Window(2, 200, 2000)]).Single().Handle);
            }),
            ("Launch: single-instance window needs fresh foreground activity", () => {
                var existing = Window();
                Check.Equal(0, Select([existing], [existing]).Count);
                Check.Equal(1, Select([existing], [existing], [new(1, LaunchWindowEventKind.Foreground, 110)]).Count);
            }),
            ("Launch: shown hidden matching window is reused", () => {
                var existing = Window();
                Check.Equal(1, Select([existing], [existing with { IsVisible = false }], [new(1, LaunchWindowEventKind.Shown, 110)]).Count);
            }),
            ("Launch: delayed events from before launch cannot claim an existing window", () => {
                var existing = Window();
                Check.Equal(0, Select([existing], [existing], [new(1, LaunchWindowEventKind.Foreground, 99), new(1, LaunchWindowEventKind.Shown, 99)]).Count);
            }),
            ("Launch: unrelated foreground application is rejected", () => {
                var unrelated = Window(8, 800, 8000, name: "unrelated");
                Check.Equal(0, Select([unrelated], [unrelated], [new(8, LaunchWindowEventKind.Foreground, 110)]).Count);
            }),
            ("Launch: splash is excluded when stable main window appears", () => {
                Check.Sequence(new[] { 2L }, Select([Window(windowClass: "SplashScreen"), Window(2)]).Select(item => item.Handle));
                Check.Equal(0, Select([Window(title: "Editor splash")]).Count);
            }),
            ("Launch: equally suitable windows remain separate and ambiguous", () => {
                Check.Sequence(new[] { 1L, 2L }, Select([Window(2, title: "Second document"), Window(title: "First document")]).Select(item => item.Handle));
            }),
            ("Launch: returned PID reuse alone cannot establish identity", () => {
                Check.Equal(0, Select([Window(started: 9999, name: "unrelated")]).Count);
            }),
            ("Launch: hidden, empty, and uninspectable protected windows are rejected", () => {
                Check.Equal(0, Select([Window() with { IsVisible = false }, Window(2) with { Layout = new() { Bounds = new(0, 0, 0, 0) } }, Window(3, started: 0)]).Count);
            }),
            ("Launch: expected identity is an exact process name", () => {
                Check.False(LaunchCandidateClassifier.MatchesProfile(Profile, Window(name: "editor-helper").Fingerprint));
                Check.True(LaunchCandidateClassifier.MatchesProfile(new() { ExpectedProcessName = "EDITOR.EXE" }, Window().Fingerprint));
            }),
            ("Launch: executable path distinguishes same-named applications", () => {
                var profile = new AppLaunchProfile { Target = @"C:\Other\editor.exe" };
                Check.False(LaunchCandidateClassifier.MatchesProfile(profile, Window().Fingerprint));
            }),
            ("Launch: asynchronous detection settles a new process window", () => {
                var environment = new FakeLaunchEnvironment();
                environment.OnDelay = step => { if (step == 2) environment.Windows = [Window()]; };
                using var coordinator = Coordinator(environment);
                var result = coordinator.LaunchAsync(Profile).GetAwaiter().GetResult();
                Check.Equal(LaunchStatus.Success, result.Status); Check.Equal(1L, result.Window!.Handle);
                Check.Equal(0, environment.Subscribers); Check.Equal(1, environment.Starts);
            }),
            ("Launch: splash followed by main waits for the main window", () => {
                var environment = new FakeLaunchEnvironment();
                environment.OnDelay = step => environment.Windows = step < 4 ? [Window(windowClass: "SplashScreen")] : [Window(2)];
                using var coordinator = Coordinator(environment);
                var result = coordinator.LaunchAsync(Profile).GetAwaiter().GetResult();
                Check.Equal(LaunchStatus.Success, result.Status); Check.Equal(2L, result.Window!.Handle);
            }),
            ("Launch: multiple stable windows return a chooser result", () => {
                var environment = new FakeLaunchEnvironment { OnStart = env => env.Windows = [Window(), Window(2)] };
                using var coordinator = Coordinator(environment);
                var result = coordinator.LaunchAsync(Profile).GetAwaiter().GetResult();
                Check.Equal(LaunchStatus.Ambiguous, result.Status); Check.Equal(2, result.Candidates.Count); Check.Equal<WindowSnapshot?>(null, result.Window);
            }),
            ("Launch: unchanged existing foreground window times out", () => {
                var environment = new FakeLaunchEnvironment { Windows = [Window()], ForegroundWindow = 1 };
                using var coordinator = Coordinator(environment);
                Check.Equal(LaunchStatus.TimedOut, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status);
                Check.Equal(1, environment.Windows.Count); Check.Equal(0, environment.Subscribers);
            }),
            ("Launch: event observation finds single-instance reuse", () => {
                var environment = new FakeLaunchEnvironment { Windows = [Window()] };
                environment.OnDelay = step => { if (step == 2) environment.Raise(new(1, LaunchWindowEventKind.Foreground, environment.MonotonicMilliseconds)); };
                using var coordinator = Coordinator(environment);
                Check.Equal(LaunchStatus.Success, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status);
            }),
            ("Launch: fallback enumeration finds a foreground transition", () => {
                var environment = new FakeLaunchEnvironment { Windows = [Window()], ForegroundWindow = 9 };
                environment.OnDelay = step => { if (step == 2) environment.ForegroundWindow = 1; };
                using var coordinator = Coordinator(environment);
                Check.Equal(LaunchStatus.Success, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status);
            }),
            ("Launch: process-start failure detaches observation", () => {
                var environment = new FakeLaunchEnvironment { FailStart = true };
                using var coordinator = Coordinator(environment);
                Check.Equal(LaunchStatus.Failed, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status); Check.Equal(0, environment.Subscribers);
            }),
            ("Launch: cancellation before start never launches a process", () => {
                var environment = new FakeLaunchEnvironment();
                using var coordinator = Coordinator(environment); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                Check.Equal(LaunchStatus.Cancelled, coordinator.LaunchAsync(Profile, cancellation.Token).GetAwaiter().GetResult().Status); Check.Equal(0, environment.Starts);
            }),
            ("Launch: cancellation stops observation and leaves the application alone", () => {
                var environment = new FakeLaunchEnvironment(); using var cancellation = new CancellationTokenSource();
                environment.OnDelay = _ => cancellation.Cancel(); using var coordinator = Coordinator(environment);
                Check.Equal(LaunchStatus.Cancelled, coordinator.LaunchAsync(Profile, cancellation.Token).GetAwaiter().GetResult().Status);
                Check.Equal(1, environment.Starts); Check.Equal(0, environment.Subscribers);
            }),
            ("Launch: disposal cancels pending observation and rejects later launches", () => {
                var environment = new FakeLaunchEnvironment(); var coordinator = Coordinator(environment);
                environment.OnDelay = _ => coordinator.Dispose();
                Check.Equal(LaunchStatus.Cancelled, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status);
                Check.Equal(LaunchStatus.Cancelled, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status); Check.Equal(0, environment.Subscribers);
            }),
            ("Launch: overlapping launches are rejected without starting another app", () => {
                var environment = new FakeLaunchEnvironment(); using var coordinator = Coordinator(environment);
                environment.OnStart = _ => Check.Equal(LaunchStatus.Failed, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status);
                coordinator.LaunchAsync(Profile).GetAwaiter().GetResult(); Check.Equal(1, environment.Starts);
            }),
            ("Launch: an unstable late window is left unchanged at timeout", () => {
                var environment = new FakeLaunchEnvironment();
                environment.OnDelay = step => { if (step >= 9) environment.Windows = [Window(title: "Document " + step)]; };
                using var coordinator = Coordinator(environment);
                Check.Equal(LaunchStatus.TimedOut, coordinator.LaunchAsync(Profile).GetAwaiter().GetResult().Status);
                Check.Equal(1, environment.Windows.Count);
            })
        };
        var failures = 0;
        foreach (var (name, test) in tests)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}\n{exception}"); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} launch tests passed.");
        return failures;
    }

    private static LaunchDetectionCoordinator Coordinator(FakeLaunchEnvironment environment) => new(environment,
        new() { Timeout = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(10),
            SettleTime = TimeSpan.FromMilliseconds(20), MinimumObservationTime = TimeSpan.FromMilliseconds(30) });

    private sealed class FakeLaunchEnvironment : ILaunchEnvironment
    {
        private Action<LaunchWindowEvent>? _observed;
        private int _step;
        public int Subscribers { get; private set; }
        public event Action<LaunchWindowEvent>? WindowObserved
        {
            add { _observed += value; Subscribers++; }
            remove { _observed -= value; Subscribers--; }
        }
        public long MonotonicMilliseconds { get; private set; } = 100;
        public long ForegroundWindow { get; set; }
        public IReadOnlyList<WindowSnapshot> Windows { get; set; } = [];
        public Action<int>? OnDelay { get; set; }
        public Action<FakeLaunchEnvironment>? OnStart { get; set; }
        public bool FailStart { get; set; }
        public int Starts { get; private set; }
        public IReadOnlyList<WindowSnapshot> EnumerateWindows(bool includeHidden = false) => Windows.Where(window => includeHidden || window.IsVisible).ToArray();
        public LaunchedProcess? Start(AppLaunchProfile profile)
        {
            Starts++;
            if (FailStart) throw new InvalidOperationException("Windows denied starting this executable.");
            OnStart?.Invoke(this);
            return Process;
        }
        public void Raise(LaunchWindowEvent item) => _observed?.Invoke(item);
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MonotonicMilliseconds += (long)delay.TotalMilliseconds;
            OnDelay?.Invoke(++_step);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
