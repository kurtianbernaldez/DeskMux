using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskMux.Core;

public sealed class JsonStateStore(string dataDirectory, ILog log) : IStateStore
{
    public string FilePath { get; } = Path.Combine(dataDirectory, "sessions.json");
    public string? LastLoadError { get; private set; }
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorkspaceState Load()
    {
        LastLoadError = null;
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var state = Read(path);
                if (path != FilePath)
                {
                    LastLoadError = "Session data was recovered from its backup. The damaged file has been preserved.";
                    log.Write("persistence.recovered", LastLoadError);
                }
                return state;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                LastLoadError = "Session data could not be read. Existing files were preserved; see the log for details.";
                log.Write("persistence.load_failed", ex.Message, new { path });
                PreserveDamaged(path);
            }
        }
        return new WorkspaceState();
    }

    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(dataDirectory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(output, state, Options);
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath))
            {
                var valid = false;
                try { Read(FilePath); valid = true; }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
                { PreserveDamaged(FilePath); }
                if (valid) File.Replace(temporary, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
                else File.Move(temporary, FilePath, overwrite: true);
            }
            else File.Move(temporary, FilePath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static WorkspaceState Read(string path)
    {
        using var input = File.OpenRead(path);
        var state = JsonSerializer.Deserialize<WorkspaceState>(input, Options) ?? throw new InvalidDataException("Empty session data.");
        if (state.Version is < 1 or > 3) throw new InvalidDataException($"Unsupported session format version {state.Version}.");
        StateSanitizer.Normalize(state);
        return state;
    }

    private void PreserveDamaged(string path)
    {
        try
        {
            File.Copy(path, path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}", overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { log.Write("persistence.preserve_failed", ex.Message, new { path }); }
    }
}

internal static class StateSanitizer
{
    public static void Normalize(WorkspaceState state)
    {
        // Version 1 had no pane geometry. Absent collections deserialize empty, leaving every old entry floating.
        if (state.Version == 1)
        {
            foreach (var session in state.Sessions ?? [])
                if (session is not null) session.PaneCanvases = [];
        }
        state.Version = 3;
        state.Sessions ??= [];
        state.LayoutPresets ??= [];
        state.LayoutPresets.RemoveAll(p=>p==null);
        foreach(var preset in state.LayoutPresets) {
            preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? "Layout" : preset.Name;
            preset.Windows ??= []; preset.Windows.RemoveAll(w=>w==null);
            preset.Windows=preset.Windows.DistinctBy(w=>w.WindowId).ToList();
            foreach(var window in preset.Windows) { window.Fingerprint ??= new(); window.Layout ??= new(); window.Fingerprint.ExecutablePath ??= ""; }
            preset.Canvases ??= []; preset.Canvases.RemoveAll(c=>c==null);
            var used=new HashSet<Guid>(); foreach(var canvas in preset.Canvases) PaneTree.Validate(canvas,preset.Windows.Select(w=>w.WindowId).ToHashSet(),used);
        }
        state.Settings ??= new AppSettings();
        state.Settings.ExcludedProcesses ??= [];
        state.Settings.ExcludedProcesses = state.Settings.ExcludedProcesses.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        state.Settings.LaunchProfiles ??= [];
        state.Settings.LaunchProfiles.RemoveAll(p => p is null);
        var profileIds = new HashSet<Guid>();
        foreach (var profile in state.Settings.LaunchProfiles)
        {
            if (profile.Id == Guid.Empty || !profileIds.Add(profile.Id)) { profile.Id = Guid.NewGuid(); profileIds.Add(profile.Id); }
            profile.Name ??= ""; profile.Target ??= ""; profile.Arguments ??= "";
            profile.WorkingDirectory ??= ""; profile.ExpectedProcessName ??= "";
        }
        state.Settings.Theme ??= new ThemeSettings();
        state.Settings.Theme.Preset = string.IsNullOrWhiteSpace(state.Settings.Theme.Preset) ? "System" : state.Settings.Theme.Preset.Trim();
        state.Settings.CommandBindings ??= [];
        if (Hotkeys.Validate(state.Settings) != null) state.Settings.CommandBindings.Clear();
        state.Settings.PaneGuides ??= new PaneGuideSettings();
        state.Settings.PaneGuides.Normalize();
        state.Settings.Theme.Custom ??= new ThemePalette();
        var theme = state.Settings.Theme.Custom;
        theme.Background = NormalizeColor(theme.Background, "#F5F7F9"); theme.Surface = NormalizeColor(theme.Surface, "#FFFFFF");
        theme.Sidebar = NormalizeColor(theme.Sidebar, "#162A35"); theme.Text = NormalizeColor(theme.Text, "#172B3A");
        theme.Muted = NormalizeColor(theme.Muted, "#647583"); theme.Accent = NormalizeColor(theme.Accent, "#087E75");
        theme.Border = NormalizeColor(theme.Border, "#CFD8DF"); theme.Danger = NormalizeColor(theme.Danger, "#A65349");
        state.Settings.CommandTimeoutMs = Math.Clamp(state.Settings.CommandTimeoutMs, 500, 10000);
        if (state.Settings.PrefixVirtualKey is < 1 or > 254) state.Settings.PrefixVirtualKey = 0x42;
        var sessionIds = new HashSet<Guid>();
        var entryIds = new HashSet<Guid>();
        state.Sessions.RemoveAll(s => s is null);
        foreach (var session in state.Sessions)
        {
            if (session.Id == Guid.Empty || !sessionIds.Add(session.Id)) { session.Id = Guid.NewGuid(); sessionIds.Add(session.Id); }
            session.Name = string.IsNullOrWhiteSpace(session.Name) ? "New session" : session.Name.Trim();
            session.SuspendedPaneCanvases ??= [];
            session.SuspendedPaneCanvases.RemoveAll(c=>c==null);
            session.Windows ??= [];
            session.Windows.RemoveAll(w => w is null);
            foreach (var window in session.Windows)
            {
                if (window.Id == Guid.Empty || !entryIds.Add(window.Id)) { window.Id = Guid.NewGuid(); entryIds.Add(window.Id); }
                window.Fingerprint ??= new WindowFingerprint();
                window.Fingerprint.ExecutablePath ??= "";
                window.Fingerprint.ProcessName ??= "";
                window.Fingerprint.WindowClass ??= "";
                window.Fingerprint.Title ??= "";
                window.Layout ??= new WindowLayout();
                window.Layout.Bounds ??= new PixelRect(100, 100, 900, 650);
                window.Layout.MonitorWorkArea ??= new PixelRect(0, 0, 1920, 1080);
                window.Layout.MonitorDevice ??= "";
                window.Layout.MonitorId ??= "";
                if (window.Layout.Dpi == 0) window.Layout.Dpi = 96;
                window.Status ??= "";
                if (window.LaunchProfileId is { } profileId && !state.Settings.LaunchProfiles.Any(p => p.Id == profileId))
                    window.LaunchProfileId = null;
            }
            session.PaneCanvases ??= [];
            session.PaneCanvases.RemoveAll(c => c is null);
            var canvasIds = new HashSet<Guid>();
            var monitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paneWindows = new HashSet<Guid>();
            // Missing leaves remain in the persisted tree so a restored application can
            // return to its exact pane. Runtime layout uses a live projection of this tree.
            var validWindows = session.Windows.Select(w => w.Id).ToHashSet();
            foreach (var canvas in session.PaneCanvases)
            {
                if (canvas.Id == Guid.Empty || !canvasIds.Add(canvas.Id)) { canvas.Id = Guid.NewGuid(); canvasIds.Add(canvas.Id); }
                var identity = !string.IsNullOrWhiteSpace(canvas.MonitorId) ? "id:" + canvas.MonitorId
                    : !string.IsNullOrWhiteSpace(canvas.MonitorDevice) ? "device:" + canvas.MonitorDevice
                    : "area:" + canvas.MonitorWorkArea;
                if (!monitorIds.Add(identity)) { canvas.Nodes = []; canvas.RootNodeId = null; canvas.ZoomedLeafId = null; continue; }
                PaneTree.Validate(canvas, validWindows, paneWindows);
            }
            session.PaneCanvases.RemoveAll(c => c.RootNodeId is null);
            foreach(var suspended in session.SuspendedPaneCanvases) PaneTree.Validate(suspended,validWindows);
            session.SuspendedPaneCanvases.RemoveAll(c=>c.RootNodeId==null);
        }
        if (state.ActiveSessionId is { } active && !sessionIds.Contains(active)) state.ActiveSessionId = null;
        if (state.PreviousSessionId is { } previous && !sessionIds.Contains(previous)) state.PreviousSessionId = null;
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        var text = value?.Trim();
        return text is { Length: 7 } && text[0] == '#' && text.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0
            ? text.ToUpperInvariant() : fallback;
    }
}
