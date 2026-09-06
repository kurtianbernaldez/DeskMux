using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskMux.Core;
using DeskMux.Windows;

namespace DeskMux.Integration;

internal static class NativeLaunchChecks
{
    public static int Run(string directory, ILog log)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The integration executable path is unavailable.");
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            executable = Path.ChangeExtension(typeof(NativeLaunchChecks).Assembly.Location, ".exe");
        var checks = 0;
        foreach (var mode in new[] { "main", "splash", "multiple" })
        {
            var manifest = Path.Combine(directory, "launch-" + mode + ".json");
            var windows = new WindowSystem(new AppSettings(), Path.Combine(directory, "launch-" + mode), log);
            using var tracker = new WinEventTracker(action => action(), log);
            tracker.Start();
            using var coordinator = new LaunchCoordinator(windows, tracker, log,
                new() { Timeout = TimeSpan.FromSeconds(12), SettleTime = TimeSpan.FromMilliseconds(700), MinimumObservationTime = TimeSpan.FromMilliseconds(1500) });
            try
            {
                var result = coordinator.LaunchAsync(new()
                {
                    Name = "DeskMux owned launch fixture", Target = executable,
                    Arguments = "--launch-fixture " + mode + " \"" + manifest + "\"",
                    ExpectedProcessName = Path.GetFileNameWithoutExtension(executable)
                }).GetAwaiter().GetResult();
                if (mode == "multiple")
                {
                    Assert(result.Status == LaunchStatus.Ambiguous && result.Candidates.Count == 2,
                        "Native launch returns both fixture windows for explicit selection", result);
                    checks++;
                }
                else
                {
                    Assert(result.Success && result.Window!.Fingerprint.Title == "DeskMux launch fixture main",
                        mode == "splash" ? "Native launch waits for splash replacement by stable main window" : "Native launch finds the unique window of its new process", result);
                    checks++;
                }
                Assert(result.Candidates.All(candidate => candidate.IsVisible && windows.Inspect(candidate.Handle) is { IsVisible: true }),
                    "Native launch leaves every detected application window visible", result);
                checks++;
            }
            finally { StopOwnedFixture(manifest); }
        }
        return checks;
    }

    public static int RunFixture(string mode, string manifestPath)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var fixtureWindows = new List<Window>();
        Window Create(string title)
        {
            var window = new Window
            {
                Title = title, Width = 560, Height = 340, Left = 160, Top = 160,
                ShowActivated = false,
                Content = new TextBlock { Text = title + "\nOwned by the DeskMux integration test.", Margin = new Thickness(25), FontSize = 20 }
            };
            fixtureWindows.Add(window);
            window.Show();
            return window;
        }
        void WriteManifest()
        {
            using var process = Process.GetCurrentProcess();
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new FixtureIdentity(process.Id,
                process.StartTime.ToUniversalTime().Ticks, fixtureWindows.Where(window => window.IsVisible)
                    .Select(window => new WindowInteropHelper(window).Handle.ToInt64()).ToArray())));
        }
        if (mode == "splash")
        {
            var splash = Create("DeskMux launch fixture splash");
            WriteManifest();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                timer.Stop(); Create("DeskMux launch fixture main"); splash.Close(); WriteManifest();
            };
            timer.Start();
        }
        else
        {
            Create("DeskMux launch fixture main");
            if (mode == "multiple") Create("DeskMux launch fixture second document");
            WriteManifest();
        }
        // The parent is solely responsible for the test processes. This timer also ensures
        // an interrupted test cannot leave a fixture window on the user's desktop.
        var lifetime = new DispatcherTimer { Interval = TimeSpan.FromSeconds(35) };
        lifetime.Tick += (_, _) => app.Shutdown();
        lifetime.Start();
        return app.Run();
    }

    private static void StopOwnedFixture(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return;
        var identity = JsonSerializer.Deserialize<FixtureIdentity>(File.ReadAllText(manifestPath));
        if (identity is null) return;
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            if (process.StartTime.ToUniversalTime().Ticks != identity.StartedUtcTicks) return;
            // Only a process created for this test and recorded by its private manifest is stopped.
            process.Kill();
            process.WaitForExit(3000);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    private static void Assert(bool condition, string description, LaunchResult result)
    {
        if (!condition) throw new InvalidOperationException(description + $": {result.Status}; {result.Error}");
        Console.WriteLine("PASS " + description);
    }

    private sealed record FixtureIdentity(int ProcessId, long StartedUtcTicks, long[] Handles);
}
