namespace DeskMux.Core;

public record PixelRect(int X, int Y, int Width, int Height);
public record PaneMinimumSize(int Width, int Height);
public record MonitorDescriptor(string DeviceName, PixelRect Bounds, PixelRect WorkArea, uint Dpi, bool IsPrimary, string StableId = "");
public enum WindowShowState { Normal, Minimized, Maximized }
public sealed class WindowLayout
{
    public bool UseVisibleFrameBounds { get; set; }
    public PixelRect Bounds { get; set; } = new(100, 100, 900, 650);
    public WindowShowState ShowState { get; set; }
    public bool RestoreToMaximized { get; set; }
    public string MonitorDevice { get; set; } = "";
    public string MonitorId { get; set; } = "";
    public PixelRect MonitorWorkArea { get; set; } = new(0, 0, 1920, 1080);
    public uint Dpi { get; set; } = 96;
}
public sealed class WindowFingerprint
{
    public int ProcessId { get; set; }
    public long ProcessStartTimeUtcTicks { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string WindowClass { get; set; } = "";
    public string Title { get; set; } = "";
}
public sealed class ManagedWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Handle { get; set; }
    public WindowFingerprint Fingerprint { get; set; } = new();
    public WindowLayout Layout { get; set; } = new();
    public bool IsMissing { get; set; }
    public bool HiddenByDeskMux { get; set; }
    public DateTime LastFocusedUtc { get; set; }
    public Guid? LaunchProfileId { get; set; }
    public string Status { get; set; } = "";
    public string DisplayTitle => string.IsNullOrWhiteSpace(Fingerprint.Title) ? Fingerprint.ProcessName : Fingerprint.Title;
}
public sealed class WorkspaceSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New session";
    public List<ManagedWindow> Windows { get; set; } = [];
    public List<PaneCanvas> PaneCanvases { get; set; } = [];
    public List<PaneCanvas> SuspendedPaneCanvases { get; set; } = [];
    public Guid? RestorePresetId { get; set; }
}
[Flags]
public enum PrefixModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
public readonly record struct CommandGesture(int VirtualKey, PrefixModifiers Modifiers = PrefixModifiers.None);
public sealed class AppLaunchProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ExpectedProcessName { get; set; } = "";
}
public sealed class ThemePalette
{
    public string Background { get; set; } = "#F5F7F9";
    public string Surface { get; set; } = "#FFFFFF";
    public string Sidebar { get; set; } = "#162A35";
    public string Text { get; set; } = "#172B3A";
    public string Muted { get; set; } = "#647583";
    public string Accent { get; set; } = "#087E75";
    public string Border { get; set; } = "#CFD8DF";
    public string Danger { get; set; } = "#A65349";
}
public sealed class ThemeSettings
{
    public string Preset { get; set; } = "System";
    public ThemePalette Custom { get; set; } = new();
}
public sealed class AppSettings
{
    public Dictionary<string, CommandGesture> CommandBindings { get; set; } = [];
    public PaneGuideSettings PaneGuides { get; set; } = new();
    public PrefixModifiers PrefixModifiers { get; set; } = PrefixModifiers.Control;
    public int PrefixVirtualKey { get; set; } = 0x42;
    public int CommandTimeoutMs { get; set; } = 1800;
    public bool KeyboardPaused { get; set; }
    public bool ShowManagerOnStartup { get; set; } = true;
    public bool RestoreMinimizedOnSessionSwitch { get; set; }
    public bool RestoreMonitorLayouts { get; set; } = true;
    public bool RestoreActiveSessionOnStartup { get; set; }
    public ThemeSettings Theme { get; set; } = new();
    public List<AppLaunchProfile> LaunchProfiles { get; set; } = [];
    public List<string> ExcludedProcesses { get; set; } = ["DeskMux", "DeskMux.Watchdog", "dwm", "csrss", "winlogon", "LogonUI", "LockApp", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "sihost", "lsass"];
}
public sealed class WorkspaceState
{
    public List<LayoutPreset> LayoutPresets { get; set; } = [];
    public int Version { get; set; } = 3;
    public List<WorkspaceSession> Sessions { get; set; } = [];
    public Guid? ActiveSessionId { get; set; }
    public Guid? PreviousSessionId { get; set; }
    public AppSettings Settings { get; set; } = new();
}
public sealed record WindowSnapshot(long Handle, WindowFingerprint Fingerprint, WindowLayout Layout, bool IsVisible)
{
    public PixelRect? VisibleBounds { get; init; }
}
public sealed record WindowOperationResult(bool Success, string? Error = null)
{
    public static WindowOperationResult Ok { get; } = new(true);
    public static WindowOperationResult Fail(string error) => new(false, error);
}
public interface IWindowSystem
{
    long ForegroundWindow { get; }
    IReadOnlyList<WindowSnapshot> EnumerateWindows(bool includeHidden = false);
    WindowSnapshot? Inspect(long handle);
    bool IsSameWindow(ManagedWindow window);
    WindowOperationResult Hide(ManagedWindow window);
    WindowOperationResult Show(ManagedWindow window);
    WindowOperationResult Focus(ManagedWindow window);
    PaneMinimumSize GetMinimumPaneSize(ManagedWindow window) => new(PaneTree.MinimumWidth, PaneTree.MinimumHeight);
    WindowOperationResult ApplyLayout(ManagedWindow window, WindowLayout layout, bool activate = false)
        => WindowOperationResult.Fail("This window system does not support pane positioning.");
    IReadOnlyList<WindowOperationResult> ApplyLayouts(IReadOnlyList<(ManagedWindow Window, WindowLayout Layout)> placements)
        => placements.Select(p => ApplyLayout(p.Window, p.Layout)).ToArray();
    IReadOnlyList<WindowOperationResult> BringToFront(IReadOnlyList<ManagedWindow> windows)
        => windows.Select(_ => WindowOperationResult.Fail("This window system does not support session z-ordering.")).ToArray();
    IReadOnlyList<MonitorDescriptor> GetMonitors();
}
public interface IStateStore
{
    WorkspaceState Load();
    void Save(WorkspaceState state);
}
public interface ILog
{
    void Write(string eventName, string message, object? data = null);
}
