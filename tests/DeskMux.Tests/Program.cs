using DeskMux.Core;

var tests = new (string Name, Action Run)[]
{
    ("Release versions follow semantic precedence", () => {
        Check.True(ReleaseVersion.TryParse("v1.2.3-alpha.2", out var alpha));
        Check.True(ReleaseVersion.TryParse("1.2.3-alpha.10+build", out var laterAlpha));
        Check.True(ReleaseVersion.TryParse("1.2.3", out var stable));
        Check.True(laterAlpha.CompareTo(alpha) > 0); Check.True(stable.CompareTo(laterAlpha) > 0);
        Check.False(ReleaseVersion.TryParse("version one", out _));
    }),
    ("Monitor interface identity survives display number reassignment", () => {
        var layout = new WindowLayout { Bounds = new(100, 80, 700, 500), MonitorDevice = "DISPLAY1", MonitorId = "physical-A", MonitorWorkArea = new(0, 0, 1920, 1080) };
        var monitors = new[] { Fixture.Monitor("DISPLAY1", new(0,0,1920,1080), true) with { StableId = "physical-B" }, Fixture.Monitor("DISPLAY2", new(1920,0,1920,1080), false) with { StableId = "physical-A" } };
        var mapped = MonitorMapper.Map(layout, monitors); Check.Equal("DISPLAY2", mapped.MonitorDevice); Check.Equal(2020, mapped.Bounds.X);
    }),
    ("Removal restores a window even when an earlier hide timed out", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
        f.Manager.AddWindow(1, a.Id); var entry = a.Windows.Single(); f.Windows.FailHide = true; f.Manager.SwitchTo(b.Id);
        Check.False(entry.HiddenByDeskMux);
        f.Windows.Items[1] = f.Windows.Items[1] with { IsVisible = false };
        f.Manager.RemoveWindow(entry.Id); Check.True(f.Windows.Items[1].IsVisible); Check.Equal(0, a.Windows.Count);
    }),
    ("Deletion retains a session when an uncertain hide cannot be restored", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
        f.Manager.AddWindow(1, a.Id); f.Windows.FailHide = true; f.Manager.SwitchTo(b.Id); f.Windows.FailShow = true;
        f.Manager.DeleteSession(a.Id, null); Check.True(f.Manager.State.Sessions.Contains(a)); Check.Equal(1, a.Windows.Count);
    }),
    ("First session becomes active and names are trimmed", () => {
        var f = new Fixture(); var session = f.Manager.CreateSession("  DEV  ");
        Check.Equal("DEV", session.Name); Check.Equal(session.Id, f.Manager.State.ActiveSessionId); Check.Equal(1, f.Store.Saves);
    }),
    ("Creating a second session preserves current workspace", () => {
        var f = new Fixture(); var first = f.Manager.CreateSession("DEV"); f.Manager.CreateSession("IMAGES");
        Check.Equal(first.Id, f.Manager.ActiveSession!.Id);
    }),
    ("Session rename and safe empty name", () => {
        var f = new Fixture(); var s = f.Manager.CreateSession(""); f.Manager.RenameSession(s.Id, "  Research ");
        Check.Equal("Research", s.Name); f.Manager.RenameSession(s.Id, " "); Check.Equal("New session", s.Name);
    }),
    ("Ordering clamps at edges and keeps active identity", () => {
        var f = new Fixture(); var a = f.Manager.CreateSession("A"); var b = f.Manager.CreateSession("B"); var c = f.Manager.CreateSession("C");
        f.Manager.ReorderSession(c.Id, -1); Check.Sequence(new[] { a.Id, c.Id, b.Id }, f.Manager.State.Sessions.Select(s => s.Id));
        f.Manager.ReorderSession(a.Id, int.MaxValue); Check.Equal(a.Id, f.Manager.State.Sessions.Last().Id); Check.Equal(a.Id, f.Manager.ActiveSession!.Id);
    }),
    ("Next and previous wrap in current order", () => {
        var f = new Fixture(); var a = f.Manager.CreateSession("A"); var b = f.Manager.CreateSession("B"); var c = f.Manager.CreateSession("C");
        f.Manager.SwitchRelative(-1); Check.Equal(c.Id, f.Manager.State.ActiveSessionId);
        f.Manager.SwitchRelative(1); Check.Equal(a.Id, f.Manager.State.ActiveSessionId);
        f.Manager.SwitchRelative(1); Check.Equal(b.Id, f.Manager.State.ActiveSessionId);
    }),
    ("Last-session switch toggles", () => {
        var f = new Fixture(); var a = f.Manager.CreateSession("A"); var b = f.Manager.CreateSession("B");
        f.Manager.SwitchTo(b.Id); f.Manager.SwitchPrevious(); Check.Equal(a.Id, f.Manager.State.ActiveSessionId);
        f.Manager.SwitchPrevious(); Check.Equal(b.Id, f.Manager.State.ActiveSessionId);
    }),
    ("Detach without previous hides workspace and retains return target", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!;
        f.Manager.AddWindow(1, a.Id); f.Manager.Detach(); Check.Equal<Guid?>(null, f.Manager.State.ActiveSessionId);
        Check.False(f.Windows.Items[1].IsVisible); f.Manager.SwitchPrevious(); Check.Equal(a.Id, f.Manager.State.ActiveSessionId); Check.True(f.Windows.Items[1].IsVisible);
    }),
    ("Detach returns to previous session", () => {
        var f = new Fixture(); var a = f.Manager.CreateSession("A"); var b = f.Manager.CreateSession("B");
        f.Manager.SwitchTo(b.Id); f.Manager.Detach(); Check.Equal(a.Id, f.Manager.State.ActiveSessionId);
    }),
    ("Switching hides outgoing and shows incoming individual windows", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("Images");
        Check.True(f.Manager.AddWindow(1, a.Id)); Check.True(f.Manager.AddWindow(2, b.Id));
        Check.True(f.Windows.Items[1].IsVisible); Check.False(f.Windows.Items[2].IsVisible);
        f.Manager.SwitchTo(b.Id); Check.False(f.Windows.Items[1].IsVisible); Check.True(f.Windows.Items[2].IsVisible);
        Check.Equal(2, f.Windows.Items.Count);
    }),
    ("Two windows from one process keep separate memberships", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
        f.Manager.AddWindow(1, a.Id); f.Manager.AddWindow(2, b.Id);
        Check.Equal(1L, a.Windows.Single().Handle); Check.Equal(2L, b.Windows.Single().Handle);
    }),
    ("Duplicate add cannot silently transfer ownership", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
        f.Manager.AddWindow(1, a.Id); Check.False(f.Manager.AddWindow(1, b.Id)); Check.Equal(1, a.Windows.Count); Check.Equal(0, b.Windows.Count);
    }),
    ("Move to inactive session hides and transfer back shows", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
        f.Manager.AddWindow(1, a.Id); var entry = a.Windows.Single(); f.Manager.MoveWindow(entry.Id, b.Id);
        Check.Equal(0, a.Windows.Count); Check.Equal(entry.Id, b.Windows.Single().Id); Check.False(f.Windows.Items[1].IsVisible);
        f.Manager.MoveWindow(entry.Id, a.Id); Check.True(f.Windows.Items[1].IsVisible);
    }),
    ("Manual capture respects process exclusions with case and exe suffix", () => {
        var f = Fixture.WithTwoWindows(); f.Manager.State.Settings.ExcludedProcesses.Add("BROWSER.EXE");
        Check.False(f.Manager.AddWindow(1, f.Manager.ActiveSession!.Id)); Check.Contains("excluded", f.Manager.LastError!);
    }),
    ("Manual capture refuses invisible windows", () => {
        var f = Fixture.WithTwoWindows(); f.Windows.Items[1] = f.Windows.Items[1] with { IsVisible = false };
        Check.False(f.Manager.AddWindow(1, f.Manager.ActiveSession!.Id)); Check.Equal(0, f.Manager.ActiveSession.Windows.Count);
    }),
    ("Layout tracking is debounced and updates current layout", () => {
        var f = Fixture.WithTwoWindows(); f.Manager.AddWindow(1, f.Manager.ActiveSession!.Id); var saves = f.Store.Saves;
        var moved = new PixelRect(-1500, 80, 1100, 800); f.Windows.SetBounds(1, moved); f.Manager.TrackWindow(1, false);
        Check.Equal(moved, f.Manager.ActiveSession.Windows.Single().Layout.Bounds); Check.Equal(saves, f.Store.Saves); Check.True(f.Manager.HasUnsavedChanges);
        f.Manager.Save(); Check.Equal(saves + 1, f.Store.Saves); Check.False(f.Manager.HasUnsavedChanges);
    }),
    ("Switch records visible layout without mutating hidden layout", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id);
        var saved = new PixelRect(1000, 40, 700, 900); f.Windows.SetBounds(1, saved); f.Manager.SwitchTo(b.Id);
        f.Windows.SetBounds(1, new PixelRect(-32000, -32000, 160, 24)); f.Manager.SwitchTo(a.Id);
        Check.Equal(saved, f.Windows.Items[1].Layout.Bounds);
    }),
    ("Focus returns to per-session last focused non-minimized window", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
        f.Manager.AddWindow(1, a.Id); f.Manager.AddWindow(2, a.Id); f.Windows.ForegroundWindow = 2; f.Manager.TrackWindow(2, true);
        f.Manager.SwitchTo(b.Id); f.Manager.SwitchTo(a.Id); Check.Equal(2L, f.Windows.LastFocused);
    }),
    ("Minimized windows are not selected for focus", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!;
        f.Windows.Items[2].Layout.ShowState = WindowShowState.Minimized;
        f.Manager.AddWindow(1, a.Id); f.Manager.AddWindow(2, a.Id); f.Manager.TrackWindow(2, true); f.Manager.SwitchTo(a.Id);
        Check.Equal(1L, f.Windows.LastFocused);
    }),
    ("Show All preserves hidden placement and pauses future hiding", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
        f.Manager.AddWindow(1, a.Id); var saved = a.Windows.Single().Layout.Bounds; f.Manager.SwitchTo(b.Id);
        f.Windows.SetBounds(1, new PixelRect(-32000, -32000, 1, 1)); f.Manager.ShowAll();
        Check.True(f.Manager.HidingPaused); Check.True(f.Windows.Items[1].IsVisible); Check.Equal(saved, f.Windows.Items[1].Layout.Bounds);
        f.Manager.SwitchTo(b.Id); Check.True(f.Windows.Items[1].IsVisible);
        f.Manager.ResumeHiding(); Check.False(f.Manager.HidingPaused); Check.False(f.Windows.Items[1].IsVisible);
    }),
    ("Inactive managed windows that show themselves are hidden again", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id); f.Manager.SwitchTo(b.Id);
        f.Windows.Items[1] = f.Windows.Items[1] with { IsVisible = true }; f.Manager.TrackWindow(1, true); Check.False(f.Windows.Items[1].IsVisible);
    }),
    ("Missing window remains as saved entry with stale handle cleared", () => {
        var f = Fixture.WithTwoWindows(); f.Manager.AddWindow(1, f.Manager.ActiveSession!.Id); f.Windows.Items.Remove(1); f.Manager.Reconcile();
        var entry = f.Manager.ActiveSession.Windows.Single(); Check.True(entry.IsMissing); Check.Equal(0L, entry.Handle); Check.Equal("Document A", entry.Fingerprint.Title);
    }),
    ("Disappearing HWND during switching does not crash", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id);
        f.Windows.Items.Remove(1); f.Manager.SwitchTo(b.Id); Check.True(a.Windows.Single().IsMissing); Check.Equal(b.Id, f.Manager.State.ActiveSessionId);
    }),
    ("Handle reuse cannot hide an unrelated process", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id);
        f.Windows.Items[1] = Fixture.Snapshot(1, "Other", processId: 888, started: 999); f.Manager.SwitchTo(b.Id);
        Check.True(f.Windows.Items[1].IsVisible); Check.True(a.Windows.Single().IsMissing);
    }),
    ("Exact instance matching tolerates changing document title", () => {
        var snapshot = Fixture.Snapshot(1, "Old title"); var entry = Fixture.Entry(snapshot);
        var candidate = Fixture.Snapshot(1, "New title"); Check.True(WindowMatcher.IsExactIdentity(entry, candidate));
        Check.Equal(candidate, WindowMatcher.FindUniqueMatch(entry, new[] { candidate }));
    }),
    ("Restart association requires matching app class and exact nonempty title", () => {
        var entry = Fixture.Entry(Fixture.Snapshot(1, "Document A")); var candidate = Fixture.Snapshot(30, "Document A", 777, 900);
        Check.Equal(candidate, WindowMatcher.FindUniqueMatch(entry, new[] { candidate }));
        Check.Equal<WindowSnapshot?>(null, WindowMatcher.FindUniqueMatch(entry, new[] { Fixture.Snapshot(31, "Different", 777, 900) }));
    }),
    ("Fingerprint ambiguity is rejected", () => {
        var entry = Fixture.Entry(Fixture.Snapshot(1, "Same"));
        Check.Equal<WindowSnapshot?>(null, WindowMatcher.FindUniqueMatch(entry, new[] { Fixture.Snapshot(2, "Same"), Fixture.Snapshot(3, "Same") }));
    }),
    ("Different executable path cannot match a title", () => {
        var entry = Fixture.Entry(Fixture.Snapshot(1, "Same")); var candidate = Fixture.Snapshot(2, "Same"); candidate.Fingerprint.ExecutablePath = "C:\\Other\\browser.exe";
        Check.Equal<WindowSnapshot?>(null, WindowMatcher.FindUniqueMatch(entry, new[] { candidate }));
    }),
    ("One candidate cannot satisfy two saved entries", () => {
        var f = new Fixture(); var a = f.Manager.CreateSession("A"); var b = f.Manager.CreateSession("B");
        a.Windows.Add(Fixture.Entry(Fixture.Snapshot(10, "Same", 999, 300))); b.Windows.Add(Fixture.Entry(Fixture.Snapshot(11, "Same", 999, 300)));
        f.Windows.Items[1] = Fixture.Snapshot(1, "Same"); f.Manager.Reconcile();
        Check.True(a.Windows.Single().IsMissing); Check.True(b.Windows.Single().IsMissing);
    }),
    ("Reassociation keeps saved layout and recovers missing entry", () => {
        var f = new Fixture(); var a = f.Manager.CreateSession("A"); var entry = Fixture.Entry(Fixture.Snapshot(99, "Document A", 999, 300));
        entry.Layout.Bounds = new PixelRect(444, 333, 700, 500); entry.IsMissing = true; a.Windows.Add(entry);
        f.Windows.Items[1] = Fixture.Snapshot(1, "Document A"); f.Manager.Reconcile();
        Check.False(entry.IsMissing); Check.Equal(1L, entry.Handle); Check.Equal(444, entry.Layout.Bounds.X);
    }),
    ("Restore associates a launched window while preserving floating placement", () => {
        var f=new Fixture(); var a=f.Manager.CreateSession("A"); var entry=Fixture.Entry(Fixture.Snapshot(99,"Document A",999,300));
        entry.IsMissing=true; entry.Handle=0; entry.Layout.Bounds=new(420,240,780,560); a.Windows.Add(entry);
        f.Windows.Items[3]=Fixture.Snapshot(3,"Fresh title",777,900);
        Check.True(f.Manager.RestoreMissingWindow(entry.Id,f.Windows.Items[3])); Check.False(entry.IsMissing); Check.Equal(3L,entry.Handle);
        Check.Equal(new PixelRect(420,240,780,560),f.Windows.Items[3].Layout.Bounds);
    }),
    ("Restore returns a missing member to its saved pane tree", () => {
        var f=Fixture.WithTwoWindows(); var a=f.Manager.ActiveSession!; f.Manager.AddWindow(1,a.Id); f.Manager.OpenPane(2,1,PaneOrientation.Vertical);
        var entry=a.Windows.Single(w=>w.Handle==2); f.Windows.Items.Remove(2); f.Manager.Reconcile(); f.Manager.ReapplyPaneLayouts();
        Check.True(entry.IsMissing); Check.Equal(2,PaneTree.Leaves(a.PaneCanvases.Single()).Count); Check.Equal(1920,f.Windows.Items[1].Layout.Bounds.Width);
        f.Windows.Items[3]=Fixture.Snapshot(3,"Document B",777,900);
        Check.True(f.Manager.RestoreMissingWindow(entry.Id,f.Windows.Items[3])); Check.Equal(2,PaneTree.Leaves(a.PaneCanvases.Single()).Count);
        Check.Equal(new PixelRect(960,0,960,1040),f.Windows.Items[3].Layout.Bounds);
    }),
    ("Removing a missing member also removes its saved pane leaf", () => {
        var f=Fixture.WithTwoWindows(); var a=f.Manager.ActiveSession!; f.Manager.AddWindow(1,a.Id); f.Manager.OpenPane(2,1,PaneOrientation.Vertical);
        var entry=a.Windows.Single(w=>w.Handle==2); f.Windows.Items.Remove(2); f.Manager.Reconcile(); f.Manager.RemoveWindow(entry.Id);
        Check.Equal(1,a.Windows.Count); Check.Equal(0,a.PaneCanvases.Count);
    }),
    ("Hide failure is visible in status and preserves membership", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id);
        f.Windows.FailHide = true; f.Manager.SwitchTo(b.Id); Check.False(a.Windows.Single().HiddenByDeskMux); Check.Contains("permission", f.Manager.LastError!); Check.True(f.Windows.Items[1].IsVisible);
    }),
    ("Failed restoration blocks removal of hidden recovery entry", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id); f.Manager.SwitchTo(b.Id);
        f.Windows.FailShow = true; f.Manager.RemoveWindow(a.Windows.Single().Id); Check.Equal(1, a.Windows.Count); Check.True(a.Windows.Single().HiddenByDeskMux);
    }),
    ("Failed restoration blocks deleting session with hidden windows", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id); f.Manager.SwitchTo(b.Id);
        f.Windows.FailShow = true; f.Manager.DeleteSession(a.Id, null); Check.True(f.Manager.State.Sessions.Contains(a)); Check.Equal(1, a.Windows.Count);
    }),
    ("Delete leaves windows unmanaged and visible without closing them", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id); f.Manager.SwitchTo(b.Id);
        f.Manager.DeleteSession(a.Id, null); Check.False(f.Manager.State.Sessions.Contains(a)); Check.True(f.Windows.Items[1].IsVisible); Check.Equal(2, f.Windows.Items.Count);
    }),
    ("Delete transfers membership to selected session", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id);
        f.Manager.DeleteSession(a.Id, b.Id); Check.Equal(1, b.Windows.Count); Check.Equal(b.Id, f.Manager.State.ActiveSessionId); Check.True(f.Windows.Items[1].IsVisible);
    }),
    ("Deleting final session safely leaves empty state", () => {
        var f = new Fixture(); var a = f.Manager.CreateSession("A"); f.Manager.DeleteSession(a.Id, null);
        Check.Equal(0, f.Manager.State.Sessions.Count); Check.Equal<Guid?>(null, f.Manager.State.ActiveSessionId);
    }),
    ("Shutdown restores hidden windows and persists once more", () => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B"); f.Manager.AddWindow(1, a.Id); f.Manager.SwitchTo(b.Id);
        f.Manager.Shutdown(); Check.True(f.Windows.Items[1].IsVisible); Check.True(f.Manager.HidingPaused); Check.False(a.Windows.Single().HiddenByDeskMux);
        var saves = f.Store.Saves; f.Manager.Shutdown(); Check.Equal(saves, f.Store.Saves);
    }),
    ("Save failures preserve dirty state and report useful error", () => {
        var f = new Fixture(); f.Store.Fail = true; f.Manager.CreateSession("A"); Check.True(f.Manager.HasUnsavedChanges); Check.Contains("save", f.Manager.LastError!);
        f.Store.Fail = false; f.Manager.Save(); Check.False(f.Manager.HasUnsavedChanges);
    }),
    ("Save success does not emit recursive Changed notifications", () => {
        var f = new Fixture(); var changed = 0; f.Manager.Changed += () => changed++; f.Manager.CreateSession("A");
        Check.Equal(1, changed); f.Manager.Save(); Check.Equal(1, changed);
    }),
    ("JSON roundtrip keeps sessions settings fingerprints placement and ordering", () => TempDirectory(path => {
        var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("图像"); f.Manager.AddWindow(1, a.Id);
        f.Manager.State.Settings.ExcludedProcesses.Add("SensitiveApp"); a.Windows[0].Layout.ShowState = WindowShowState.Minimized; a.Windows[0].Layout.RestoreToMaximized = true;
        f.Manager.State.PreviousSessionId = b.Id; var store = new JsonStateStore(path, new NullLog()); store.Save(f.Manager.State); var read = store.Load();
        Check.Equal("图像", read.Sessions[1].Name); Check.Equal(a.Id, read.ActiveSessionId); Check.Equal(b.Id, read.PreviousSessionId);
        Check.Equal(a.Windows[0].Fingerprint.ProcessStartTimeUtcTicks, read.Sessions[0].Windows[0].Fingerprint.ProcessStartTimeUtcTicks);
        Check.True(read.Sessions[0].Windows[0].Layout.RestoreToMaximized); Check.True(read.Settings.ExcludedProcesses.Contains("SensitiveApp"));
    })),
    ("Atomic store keeps previous valid backup and no leftover temp files", () => TempDirectory(path => {
        var store = new JsonStateStore(path, new NullLog()); var state = new WorkspaceState(); state.Sessions.Add(new() { Name = "First" }); store.Save(state);
        state.Sessions[0].Name = "Second"; store.Save(state); Check.True(File.Exists(store.FilePath + ".bak")); Check.Equal("Second", store.Load().Sessions[0].Name);
        Check.Equal(0, Directory.GetFiles(path, "*.tmp").Length);
    })),
    ("Corrupt primary recovers backup and preserves evidence", () => TempDirectory(path => {
        var store = new JsonStateStore(path, new NullLog()); var state = new WorkspaceState(); state.Sessions.Add(new() { Name = "Good" }); store.Save(state); store.Save(state);
        File.WriteAllText(store.FilePath, "{invalid"); var recovered = store.Load(); Check.Equal("Good", recovered.Sessions[0].Name); Check.True(store.LastLoadError is not null);
        Check.True(Directory.GetFiles(path, "*.corrupt-*").Length > 0); store.Save(recovered); Check.Equal("Good", store.Load().Sessions[0].Name);
    })),
    ("Malformed nullable state is normalized without crashing", () => TempDirectory(path => {
        var store = new JsonStateStore(path, new NullLog()); File.WriteAllText(store.FilePath, "{\"Version\":1,\"Sessions\":[null,{\"Name\":null,\"Windows\":[null,{\"Fingerprint\":null,\"Layout\":null}]}],\"Settings\":null}");
        var state = store.Load(); Check.Equal(1, state.Sessions.Count); Check.Equal(1, state.Sessions[0].Windows.Count); Check.True(state.Settings is not null);
    })),
    ("Absent persistence creates empty state", () => TempDirectory(path => {
        var store = new JsonStateStore(path, new NullLog()); Check.Equal(0, store.Load().Sessions.Count); Check.Equal<string?>(null, store.LastLoadError);
    })),
    ("Structured logger writes parseable local JSON records", () => TempDirectory(path => {
        using var log = new StructuredLog(path); log.Write("session.switched", "DEV", new { number = 1 });
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllLines(Directory.GetFiles(path, "*.jsonl").Single()).Single());
        Check.Equal("session.switched", json.RootElement.GetProperty("eventName").GetString());
    })),
    ("Missing monitor maps safely to primary work area", () => {
        var layout = new WindowLayout { Bounds = new(-1800, 500, 1600, 1100), MonitorDevice = "gone", MonitorWorkArea = new(-1920, 0, 1920, 1200) };
        var mapped = MonitorMapper.Map(layout, new[] { Fixture.Monitor("primary", new(0, 0, 1280, 720), true) });
        Check.Equal("primary", mapped.MonitorDevice); Check.Reachable(mapped); Check.Equal(1280, mapped.Bounds.Width); Check.Equal(720, mapped.Bounds.Height);
        Check.Equal(-1800, layout.Bounds.X);
    }),
    ("Reordered monitor retains relative saved placement", () => {
        var layout = new WindowLayout { Bounds = new(2020, 100, 800, 600), MonitorDevice = "B", MonitorWorkArea = new(1920, 0, 1920, 1080) };
        var mapped = MonitorMapper.Map(layout, new[] { Fixture.Monitor("B", new(-1920, 0, 1920, 1080), false) });
        Check.Equal(new PixelRect(-1820, 100, 800, 600), mapped.Bounds); Check.Reachable(mapped);
    }),
    ("DPI change preserves logical size and offset", () => {
        var layout = new WindowLayout { Bounds = new(100, 50, 800, 600), MonitorDevice = "A", MonitorWorkArea = new(0, 0, 1920, 1080), Dpi = 96 };
        var monitor = Fixture.Monitor("A", new(0, 0, 2560, 1440), true) with { Dpi = 144 };
        var mapped = MonitorMapper.Map(layout, new[] { monitor }); Check.Equal(new PixelRect(150, 75, 1200, 900), mapped.Bounds); Check.Equal(144U, mapped.Dpi);
    }),
    ("Monitor mapping handles extreme coordinates and invalid dimensions", () => {
        var layout = new WindowLayout { Bounds = new(int.MinValue, int.MaxValue, -10, int.MaxValue), MonitorDevice = "A", MonitorWorkArea = new(int.MaxValue, int.MinValue, 100, 100), Dpi = 1 };
        var mapped = MonitorMapper.Map(layout, new[] { Fixture.Monitor("A", new(-1920, -1080, 1920, 1080), true) with { Dpi = uint.MaxValue } });
        Check.Reachable(mapped); Check.True(mapped.Bounds.Width > 0); Check.True(mapped.Bounds.Height > 0);
    }),
    ("No monitors returns an independent layout copy", () => {
        var layout = new WindowLayout { ShowState = WindowShowState.Minimized, RestoreToMaximized = true }; var mapped = MonitorMapper.Map(layout, Array.Empty<MonitorDescriptor>());
        Check.False(ReferenceEquals(layout, mapped)); Check.Equal(layout.Bounds, mapped.Bounds); Check.True(mapped.RestoreToMaximized);
    })
};

var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}\n{ex}"); }
}
Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} logic tests passed.");
var paneResults = PaneTests.Run();
failed += paneResults.Failed;
failed += SessionPaneTests.Run();
failed += LaunchTests.Run();
return failed == 0 ? 0 : 1;

static void TempDirectory(Action<string> test)
{
    var root = Path.Combine(Path.GetTempPath(), "DeskMux.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try { test(root); }
    finally { Directory.Delete(root, recursive: true); }
}

static class Check
{
    public static void True(bool value) { if (!value) throw new Exception("Expected true."); }
    public static void False(bool value) => True(!value);
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    public static void Contains(string expected, string actual) { if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception($"Expected '{actual}' to contain '{expected}'."); }
    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) => True(expected.SequenceEqual(actual));
    public static void Reachable(WindowLayout layout)
    {
        var b = layout.Bounds; var w = layout.MonitorWorkArea;
        True(b.X >= w.X && b.Y >= w.Y && (long)b.X + b.Width <= (long)w.X + w.Width && (long)b.Y + b.Height <= (long)w.Y + w.Height);
    }
}

sealed class Fixture
{
    public FakeWindows Windows { get; } = new();
    public FakeStore Store { get; } = new();
    public SessionManager Manager { get; }
    public Fixture() => Manager = new SessionManager(new(), Windows, Store, new NullLog());
    public static Fixture WithTwoWindows()
    {
        var f = new Fixture(); f.Manager.CreateSession("DEV"); f.Windows.Items.Add(1, Snapshot(1, "Document A")); f.Windows.Items.Add(2, Snapshot(2, "Document B")); return f;
    }
    public static WindowSnapshot Snapshot(long handle, string title, int processId = 123, long started = 123456) => new(handle,
        new() { ProcessId = processId, ProcessStartTimeUtcTicks = started, ProcessName = "browser", ExecutablePath = "C:\\Apps\\browser.exe", WindowClass = "BrowserWindow", Title = title },
        new() { Bounds = new(100, 100, 900, 650), MonitorDevice = "A", MonitorWorkArea = new(0, 0, 1920, 1040) }, true);
    public static ManagedWindow Entry(WindowSnapshot snapshot) => new() { Handle = snapshot.Handle, Fingerprint = snapshot.Fingerprint, Layout = snapshot.Layout };
    public static MonitorDescriptor Monitor(string name, PixelRect workArea, bool primary) => new(name, workArea, workArea, 96, primary);
}

sealed class FakeStore : IStateStore
{
    public int Saves { get; private set; }
    public bool Fail { get; set; }
    public WorkspaceState Load() => new();
    public void Save(WorkspaceState state) { if (Fail) throw new IOException("Disk unavailable"); Saves++; }
}
sealed class NullLog : ILog { public void Write(string eventName, string message, object? data = null) { } }
sealed class FakeWindows : IWindowSystem
{
    public Dictionary<long, WindowSnapshot> Items { get; } = [];
    public long ForegroundWindow { get; set; }
    public long LastFocused { get; private set; }
    public bool FailHide { get; set; }
    public bool FailShow { get; set; }
    public bool FailFocus { get; set; }
    public int LayoutCalls { get; private set; }
    public HashSet<int> FailLayoutCalls { get; } = [];
    public HashSet<long> ProtectedHandles { get; } = [];
    public Action<ManagedWindow>? BeforeLayout { get; set; }
    public List<long> Raised { get; } = [];
    public List<MonitorDescriptor> Monitors { get; } = [Fixture.Monitor("A", new(0, 0, 1920, 1040), true)];
    public IReadOnlyList<WindowSnapshot> EnumerateWindows(bool includeHidden = false) => Items.Values.Where(s => includeHidden || s.IsVisible).ToArray();
    public WindowSnapshot? Inspect(long handle) => Items.GetValueOrDefault(handle);
    public bool IsSameWindow(ManagedWindow window) => Inspect(window.Handle) is { } snapshot && WindowMatcher.IsExactIdentity(window, snapshot);
    public WindowOperationResult Hide(ManagedWindow window)
    {
        if (FailHide || ProtectedHandles.Contains(window.Handle)) return WindowOperationResult.Fail("Windows permission denied");
        if (!IsSameWindow(window)) return WindowOperationResult.Fail("Window disappeared");
        Items[window.Handle] = Items[window.Handle] with { IsVisible = false }; return WindowOperationResult.Ok;
    }
    public WindowOperationResult Show(ManagedWindow window)
    {
        if (FailShow || ProtectedHandles.Contains(window.Handle)) return WindowOperationResult.Fail("Windows permission denied");
        if (!IsSameWindow(window)) return WindowOperationResult.Fail("Window disappeared");
        Items[window.Handle] = Items[window.Handle] with { IsVisible = true, Layout = MonitorMapper.Clone(window.Layout) }; return WindowOperationResult.Ok;
    }
    public WindowOperationResult Focus(ManagedWindow window) { if (FailFocus) return WindowOperationResult.Fail("Foreground denied"); LastFocused = window.Handle; ForegroundWindow = window.Handle; return WindowOperationResult.Ok; }
    public WindowOperationResult ApplyLayout(ManagedWindow window, WindowLayout layout, bool activate = false)
    {
        LayoutCalls++;
        BeforeLayout?.Invoke(window);
        if (FailLayoutCalls.Contains(LayoutCalls) || ProtectedHandles.Contains(window.Handle)) return WindowOperationResult.Fail("Placement permission denied");
        if (!IsSameWindow(window)) return WindowOperationResult.Fail("Window disappeared");
        Items[window.Handle] = Items[window.Handle] with { Layout = MonitorMapper.Clone(layout), IsVisible = true };
        return activate ? Focus(window) : WindowOperationResult.Ok;
    }
    public IReadOnlyList<WindowOperationResult> BringToFront(IReadOnlyList<ManagedWindow> windows)
    {
        Raised.Clear();
        return windows.Select(window =>
        {
            if (!IsSameWindow(window) || !Items[window.Handle].IsVisible) return WindowOperationResult.Fail("Unavailable");
            Raised.Add(window.Handle); return WindowOperationResult.Ok;
        }).ToArray();
    }
    public IReadOnlyList<MonitorDescriptor> GetMonitors() => Monitors;
    public void SetBounds(long handle, PixelRect bounds)
    {
        var layout = MonitorMapper.Clone(Items[handle].Layout); layout.Bounds = bounds; Items[handle] = Items[handle] with { Layout = layout };
    }
}
