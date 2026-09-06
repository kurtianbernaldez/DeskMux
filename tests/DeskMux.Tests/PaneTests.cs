using DeskMux.Core;

static class PaneTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Root pane occupies the complete work area", () => {
                var canvas = Canvas(); var id = Guid.NewGuid(); PaneTree.Split(canvas, null, id, PaneOrientation.Vertical);
                Check.Equal(1, canvas.Nodes.Count); Check.Equal(canvas.MonitorWorkArea, PaneTree.Calculate(canvas)[id].Bounds);
            }),
            ("Vertical split promotes floating source and preserves leaf IDs", () => {
                var c = Canvas(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); PaneTree.Split(c, null, a, PaneOrientation.Vertical);
                var oldLeaf = c.RootNodeId; PaneTree.Split(c, a, b, PaneOrientation.Vertical); var layout = PaneTree.Calculate(c);
                Check.Equal(oldLeaf, PaneTree.FindLeaf(c, a)!.Id); Check.Equal(new PixelRect(0, 0, 960, 1040), layout[a].Bounds);
                Check.Equal(new PixelRect(960, 0, 960, 1040), layout[b].Bounds);
                var d = Canvas(); PaneTree.Split(d, a, b, PaneOrientation.Vertical); Check.Equal(2, PaneTree.Leaves(d).Count);
            }),
            ("Horizontal split places new application below source", () => {
                var (c, a, b) = Pair(PaneOrientation.Horizontal); var layout = PaneTree.Calculate(c);
                Check.Equal(new PixelRect(0, 0, 1920, 520), layout[a].Bounds); Check.Equal(new PixelRect(0, 520, 1920, 520), layout[b].Bounds);
            }),
            ("Nested split keeps unaffected rectangles and depth-first order", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); var old = PaneTree.Calculate(c)[a].Bounds;
                PaneTree.Split(c, b, d, PaneOrientation.Horizontal); Check.Equal(old, PaneTree.Calculate(c)[a].Bounds);
                Check.Sequence(new[] { a, b, d }, PaneTree.Leaves(c).Select(n => n.WindowId!.Value)); Check.Equal(5, c.Nodes.Count);
            }),
            ("Removing leaf collapses parent and promotes surviving sibling", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Horizontal);
                Check.True(PaneTree.Remove(c, b)); Check.Equal(3, c.Nodes.Count); Check.Equal(1040, PaneTree.Calculate(c)[d].Bounds.Height);
                Check.True(PaneTree.Remove(c, a)); Check.Equal(1, c.Nodes.Count); Check.Equal(c.MonitorWorkArea, PaneTree.Calculate(c)[d].Bounds);
                Check.True(PaneTree.Remove(c, d)); Check.Equal<Guid?>(null, c.RootNodeId); Check.Equal(0, c.Nodes.Count);
                Check.False(PaneTree.Remove(c, d));
            }),
            ("Moving leaf out of tree leaves entry identity available", () => {
                var (c, a, b) = Pair(); var entry = new ManagedWindow { Id = a }; PaneTree.Remove(c, entry.Id);
                Check.Equal(a, entry.Id); Check.Equal(b, PaneTree.Leaves(c).Single().WindowId); Check.Equal(c.MonitorWorkArea, PaneTree.Calculate(c)[b].Bounds);
            }),
            ("Swap changes only window references and wraps depth-first order", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Horizontal);
                var ids = PaneTree.Leaves(c).Select(n => n.Id).ToArray(); Check.True(PaneTree.Swap(c, a, -1));
                Check.Sequence(new[] { d, b, a }, PaneTree.Leaves(c).Select(n => n.WindowId!.Value));
                Check.Sequence(ids, PaneTree.Leaves(c).Select(n => n.Id)); Check.True(PaneTree.Swap(c, a, 1));
                Check.Sequence(new[] { a, b, d }, PaneTree.Leaves(c).Select(n => n.WindowId!.Value));
            }),
            ("Duplicate pane insertion is rejected without altering tree", () => {
                var (c, a, b) = Pair(); var root = c.RootNodeId; var ids = c.Nodes.Select(n => n.Id).ToArray();
                Throws("DuplicateWindow", () => PaneTree.Split(c, a, b, PaneOrientation.Horizontal));
                Check.Equal(root, c.RootNodeId); Check.Sequence(ids, c.Nodes.Select(n => n.Id));
            }),
            ("Missing split source does not replace an existing tree", () => {
                var (c, a, b) = Pair(); Throws("SourceMissing", () => PaneTree.Split(c, Guid.NewGuid(), Guid.NewGuid(), PaneOrientation.Vertical));
                Check.Sequence(new[] { a, b }, PaneTree.Leaves(c).Select(n => n.WindowId!.Value));
            }),
            ("Invalid trees repair cycles dangling children and orphan nodes", () => {
                var a = Guid.NewGuid(); var leaf = new PaneNode { WindowId = a }; var root = new PaneNode { FirstChildId = leaf.Id, Ratio = double.NaN };
                root.SecondChildId = root.Id; var c = Canvas(); c.RootNodeId = root.Id; c.Nodes = [root, leaf, new() { WindowId = Guid.NewGuid() }];
                Check.True(PaneTree.Validate(c, new HashSet<Guid> { a })); Check.Equal(leaf.Id, c.RootNodeId); Check.Equal(1, c.Nodes.Count);
                Check.False(PaneTree.Validate(c, new HashSet<Guid> { a })); Check.Equal(c.MonitorWorkArea, PaneTree.Calculate(c)[a].Bounds);
            }),
            ("Invalid duplicate windows and repeated child references retain first occurrence", () => {
                var (c, a, b) = Pair(); PaneTree.FindLeaf(c, b)!.WindowId = a;
                Check.True(PaneTree.Validate(c, new HashSet<Guid> { a, b })); Check.Equal(1, PaneTree.Leaves(c).Count);
                var root = new PaneNode { FirstChildId = c.RootNodeId, SecondChildId = c.RootNodeId }; c.Nodes.Add(root); c.RootNodeId = root.Id;
                Check.True(PaneTree.Validate(c, new HashSet<Guid> { a })); Check.Equal(1, c.Nodes.Count);
            }),
            ("Validation removes missing entries and clears orphan zoom", () => {
                var (c, a, b) = Pair(); c.ZoomedLeafId = PaneTree.FindLeaf(c, b)!.Id;
                PaneTree.Validate(c, new HashSet<Guid> { a }); Check.Equal(a, PaneTree.Leaves(c).Single().WindowId); Check.Equal<Guid?>(null, c.ZoomedLeafId);
            }),
            ("Validation shares duplicate detection between canvases", () => {
                var (c, a, b) = Pair(); var d = Canvas(); PaneTree.Split(d, a, Guid.NewGuid(), PaneOrientation.Horizontal);
                var valid = PaneTree.Leaves(d).Select(n => n.WindowId!.Value).Append(b).ToHashSet(); var used = new HashSet<Guid>();
                PaneTree.Validate(c, valid, used); PaneTree.Validate(d, valid, used); Check.Equal(1, PaneTree.Leaves(d).Count);
                Check.False(PaneTree.Leaves(d).Any(n => n.WindowId == a));
            }),
            ("Validation handles malformed nullable and duplicate node collections", () => {
                var id = Guid.NewGuid(); var leaf = new PaneNode { WindowId = id, FirstChildId = Guid.NewGuid(), Ratio = double.PositiveInfinity };
                var c = Canvas(); c.RootNodeId = leaf.Id; c.Nodes = [null!, leaf, new() { Id = leaf.Id, WindowId = Guid.NewGuid() }];
                PaneTree.Validate(c, new HashSet<Guid> { id }); Check.Equal(1, c.Nodes.Count); Check.Equal<Guid?>(null, leaf.FirstChildId);
                c.Nodes = null!; PaneTree.Validate(c, new HashSet<Guid> { id }); Check.Equal<Guid?>(null, c.RootNodeId);
            }),
            ("Excessive tree depth repairs safely without stack overflow", () => {
                var c = Canvas(); var valid = new HashSet<Guid>(); var root = new PaneNode(); c.RootNodeId = root.Id; c.Nodes.Add(root);
                for (var i = 0; i < 1000; i++) { var leaf = new PaneNode { WindowId = Guid.NewGuid() }; var split = new PaneNode(); valid.Add(leaf.WindowId!.Value); root.FirstChildId = leaf.Id; root.SecondChildId = split.Id; c.Nodes.Add(leaf); c.Nodes.Add(split); root = split; }
                PaneTree.Validate(c, valid); Check.True(PaneTree.Leaves(c).Count <= 257);
            }),
            ("Odd pixel dimensions divide exactly with remainder in second child", () => {
                var (c, a, b) = Pair(PaneOrientation.Vertical, new(0, 0, 1001, 701)); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Horizontal);
                var layouts = PaneTree.Calculate(c); Check.Equal(500, layouts[a].Bounds.Width); Check.Equal(501, layouts[b].Bounds.Width);
                Check.Equal(350, layouts[b].Bounds.Height); Check.Equal(351, layouts[d].Bounds.Height); ExactCoverage(c);
            }),
            ("Negative work-area origins and taskbar offsets remain exact", () => {
                var (c, a, b) = Pair(PaneOrientation.Horizontal, new(-1920, -1100, 1880, 1060)); var layouts = PaneTree.Calculate(c);
                Check.Equal(-1920, layouts[a].Bounds.X); Check.Equal(-1100, layouts[a].Bounds.Y); Check.Equal(-570, layouts[b].Bounds.Y); ExactCoverage(c);
            }),
            ("Nested ratios calculate deterministic physical rectangles", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Horizontal);
                c.Nodes.Single(n => n.Id == c.RootNodeId).Ratio = .25;
                PaneTree.Ancestors(c, PaneTree.FindLeaf(c, d)!.Id).Last().Ratio = .75;
                var layouts = PaneTree.Calculate(c); Check.Equal(480, layouts[a].Bounds.Width); Check.Equal(780, layouts[b].Bounds.Height); Check.Equal(260, layouts[d].Bounds.Height); ExactCoverage(c);
            }),
            ("Invalid ratios are clamped without mutating calculation inputs", () => {
                var (c, a, b) = Pair(); var split = c.Nodes.Single(n => !n.IsLeaf); split.Ratio = -100;
                var layout = PaneTree.Calculate(c); Check.Equal(PaneTree.MinimumWidth, layout[a].Bounds.Width); Check.Equal(-100d, split.Ratio);
                split.Ratio = double.NaN; Check.Equal(960, PaneTree.Calculate(c)[a].Bounds.Width);
                PaneTree.Validate(c, new HashSet<Guid> { a, b }); Check.Equal(.5, split.Ratio);
            }),
            ("Subtree minimum sizes include every nested split", () => {
                var (c, a, b) = Pair(PaneOrientation.Vertical, new(0, 0, 640, 400)); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Vertical);
                c.Nodes.Single(n => n.Id == c.RootNodeId).Ratio = .99;
                var layout = PaneTree.Calculate(c); Check.Equal(320, layout[a].Bounds.Width); Check.Equal(160, layout[b].Bounds.Width); Check.Equal(160, layout[d].Bounds.Width); ExactCoverage(c);
            }),
            ("Legal ratios below one percent retain their physical minimum geometry", () => {
                var (c, a, b) = Pair(PaneOrientation.Vertical, new(0, 0, 40000, 1000)); var root = c.Nodes.Single(n => !n.IsLeaf); root.Ratio = .004;
                PaneTree.Validate(c, new HashSet<Guid> { a, b }); Near(.004, root.Ratio); Check.Equal(160, PaneTree.Calculate(c)[a].Bounds.Width);
                root.Ratio = 0; Check.Equal(160, PaneTree.Calculate(c)[a].Bounds.Width); root.Ratio = 1; Check.Equal(160, PaneTree.Calculate(c)[b].Bounds.Width);
            }),
            ("Impossible split is rejected atomically", () => {
                var c = Canvas(new(0, 0, 319, 500)); var a = Guid.NewGuid(); PaneTree.Split(c, null, a, PaneOrientation.Vertical); var root = c.RootNodeId;
                Throws("MinimumSize", () => PaneTree.Split(c, a, Guid.NewGuid(), PaneOrientation.Vertical)); Check.Equal(root, c.RootNodeId); Check.Equal(1, c.Nodes.Count);
                c.MonitorWorkArea = new(0, 0, 159, 99); Throws("MinimumSize", () => PaneTree.Calculate(c));
            }),
            ("Mixed DPI metadata is preserved without scaling physical canvas", () => {
                var (c, a, _) = Pair(PaneOrientation.Vertical, new(-2560, 0, 2560, 1400)); c.Dpi = 192; c.MonitorId = "physical-secondary";
                var layout = PaneTree.Calculate(c)[a]; Check.Equal(192U, layout.Dpi); Check.Equal("physical-secondary", layout.MonitorId); Check.Equal(1280, layout.Bounds.Width); Check.Equal(c.MonitorWorkArea, layout.MonitorWorkArea);
            }),
            ("Invalid geometry fails explicitly before calculating outside monitor", () => {
                var (c, _, _) = Pair(); c.MonitorWorkArea = new(int.MaxValue - 10, 0, 1000, 1000); Throws("InvalidCanvas", () => PaneTree.Calculate(c));
                c.MonitorWorkArea = new(0, 0, -1, 1000); Throws("InvalidCanvas", () => PaneTree.Calculate(c));
            }),
            ("Calculation rejects a cyclic raw tree without modifying it", () => {
                var (c, _, _) = Pair(); var root = c.Nodes.Single(n => !n.IsLeaf); root.FirstChildId = root.Id;
                Throws("InvalidTree", () => PaneTree.Calculate(c)); Check.Equal(root.Id, root.FirstChildId); Check.Equal(3, c.Nodes.Count);
            }),
            ("Many nested layouts have no gaps overlaps or out-of-bounds pixels", () => {
                var random = new Random(73);
                for (var trial = 0; trial < 20; trial++) {
                    var c = Canvas(new(-2001, -1003, 4003, 2401)); var first = Guid.NewGuid(); PaneTree.Split(c, null, first, PaneOrientation.Vertical);
                    for (var i = 0; i < 24; i++) {
                        var source = PaneTree.Leaves(c)[random.Next(PaneTree.Leaves(c).Count)];
                        try { PaneTree.Split(c, source.WindowId, Guid.NewGuid(), (PaneOrientation)random.Next(2)); } catch (PaneLayoutException e) when (e.Code == "MinimumSize") { }
                    }
                    foreach (var split in c.Nodes.Where(n => !n.IsLeaf)) split.Ratio = random.NextDouble(); ExactCoverage(c);
                }
            }),
            ("Navigation chooses geometric left right up and down neighbors", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Horizontal);
                Check.Equal(b, PaneTree.Neighbor(c, a, PaneDirection.Right)); Check.Equal(a, PaneTree.Neighbor(c, b, PaneDirection.Left));
                Check.Equal(d, PaneTree.Neighbor(c, b, PaneDirection.Down)); Check.Equal(b, PaneTree.Neighbor(c, d, PaneDirection.Up));
                Check.Equal<Guid?>(null, PaneTree.Neighbor(c, a, PaneDirection.Left)); Check.Equal<Guid?>(null, PaneTree.Neighbor(c, d, PaneDirection.Down));
            }),
            ("Navigation prefers partial perpendicular overlap over diagonal pane", () => {
                var (c, a, b) = Pair(); var leftBottom = Guid.NewGuid(); var rightBottom = Guid.NewGuid();
                PaneTree.Split(c, a, leftBottom, PaneOrientation.Horizontal); PaneTree.Split(c, b, rightBottom, PaneOrientation.Horizontal);
                PaneTree.Ancestors(c, PaneTree.FindLeaf(c, leftBottom)!.Id).Last().Ratio = .25;
                PaneTree.Ancestors(c, PaneTree.FindLeaf(c, rightBottom)!.Id).Last().Ratio = .75;
                Check.Equal(b, PaneTree.Neighbor(c, a, PaneDirection.Right)); Check.Equal(leftBottom, PaneTree.Neighbor(c, rightBottom, PaneDirection.Left));
            }),
            ("Navigation ties use focus history then stable depth-first order", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Horizontal);
                for (var i = 0; i < 10; i++) Check.Equal(b, PaneTree.Neighbor(c, a, PaneDirection.Right));
                Check.Equal(d, PaneTree.Neighbor(c, a, PaneDirection.Right, new Dictionary<Guid, DateTime> { [d] = DateTime.UtcNow }));
            }),
            ("Resize finds nearest relevant ancestor and changes five percent", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Horizontal);
                var root = c.Nodes.Single(n => n.Id == c.RootNodeId); var inner = PaneTree.Ancestors(c, PaneTree.FindLeaf(c, d)!.Id).Last();
                Check.True(PaneTree.Resize(c, d, PaneDirection.Left)); Near(.45, root.Ratio); Near(.5, inner.Ratio);
                Check.True(PaneTree.Resize(c, d, PaneDirection.Down)); Near(.55, inner.Ratio); Near(.45, root.Ratio); ExactCoverage(c);
                Check.False(PaneTree.Resize(c, a, PaneDirection.Down));
            }),
            ("Resize selects inner vertical ancestor before outer split", () => {
                var (c, a, b) = Pair(); var d = Guid.NewGuid(); PaneTree.Split(c, b, d, PaneOrientation.Vertical);
                var ancestors = PaneTree.Ancestors(c, PaneTree.FindLeaf(c, d)!.Id);
                Check.True(PaneTree.Resize(c, d, PaneDirection.Right)); Near(.5, ancestors[0].Ratio); Near(.55, ancestors[1].Ratio);
            }),
            ("Repeated resize clamps to practical pane sizes", () => {
                var (c, a, b) = Pair(PaneOrientation.Vertical, new(0, 0, 1000, 600));
                for (var i = 0; i < 100; i++) PaneTree.Resize(c, a, PaneDirection.Left);
                Check.Equal(160, PaneTree.Calculate(c)[a].Bounds.Width); Check.False(PaneTree.Resize(c, a, PaneDirection.Left));
                for (var i = 0; i < 100; i++) PaneTree.Resize(c, b, PaneDirection.Right);
                Check.Equal(160, PaneTree.Calculate(c)[b].Bounds.Width); Check.False(PaneTree.Resize(c, b, PaneDirection.Right)); ExactCoverage(c);
            }),
            ("Tree clones isolate edits and preserve zoom and monitor identity", () => {
                var (c, a, b) = Pair(); c.ZoomedLeafId = PaneTree.FindLeaf(c, a)!.Id; var draft = PaneTree.Clone(c);
                Check.Equal(c.ZoomedLeafId, draft.ZoomedLeafId); Check.Equal(c.Id, draft.Id); PaneTree.Swap(draft, a, 1);
                Check.Equal(a, PaneTree.Leaves(c)[0].WindowId); Check.Equal(b, PaneTree.Leaves(draft)[0].WindowId); Check.Equal<Guid?>(null, draft.ZoomedLeafId);
                Check.Equal(960, PaneTree.Calculate(c)[a].Bounds.Width);
            }),
            ("Version 1 state migrates with existing entries floating", () => InTemporaryDirectory(path => {
                var store = new JsonStateStore(path, new NullLog()); var entry = Guid.NewGuid();
                File.WriteAllText(store.FilePath, "{\"Version\":1,\"Sessions\":[{\"Name\":\"Legacy\",\"Windows\":[{\"Id\":\"" + entry + "\",\"Handle\":123}]}]}");
                var state = store.Load(); Check.Equal(3, state.Version); Check.Equal(entry, state.Sessions[0].Windows.Single().Id); Check.Equal(0, state.Sessions[0].PaneCanvases.Count); Check.Equal(0, state.Settings.LaunchProfiles.Count);
                store.Save(state); Check.Equal(3, store.Load().Version);
            })),
            ("Pane and launcher JSON roundtrip preserves IDs ratios metadata and zoom", () => InTemporaryDirectory(path => {
                var (c, a, b) = Pair(); c.ZoomedLeafId = PaneTree.FindLeaf(c, a)!.Id; c.Dpi = 144; c.MonitorId = "monitor-serial"; c.Nodes.Single(n => !n.IsLeaf).Ratio = .37;
                var launcher = new AppLaunchProfile { Name = "Editor", Target = @"C:\Apps\Editor.exe", Arguments = "--new-window", WorkingDirectory = @"C:\Work", ExpectedProcessName = "Editor" };
                var state = new WorkspaceState { Sessions = [new() { Windows = [new() { Id = a, LaunchProfileId = launcher.Id }, new() { Id = b }], PaneCanvases = [c] }], Settings = new() { LaunchProfiles = [launcher], RestoreActiveSessionOnStartup = true } };
                var store = new JsonStateStore(path, new NullLog()); store.Save(state); var loaded = store.Load(); var pane = loaded.Sessions[0].PaneCanvases.Single();
                Check.Equal(c.Id, pane.Id); Check.Equal(c.ZoomedLeafId, pane.ZoomedLeafId); Check.Equal(144U, pane.Dpi); Check.Equal("monitor-serial", pane.MonitorId); Near(.37, pane.Nodes.Single(n => !n.IsLeaf).Ratio);
                Check.Equal(launcher.Id, loaded.Settings.LaunchProfiles.Single().Id); Check.Equal(launcher.Arguments, loaded.Settings.LaunchProfiles.Single().Arguments);
                Check.Equal<Guid?>(launcher.Id, loaded.Sessions[0].Windows[0].LaunchProfileId); Check.True(loaded.Settings.RestoreActiveSessionOnStartup); ExactCoverage(pane);
            })),
            ("Loaded missing application records retain their saved pane leaves", () => InTemporaryDirectory(path => {
                var (c, a, b) = Pair(); var state = new WorkspaceState { Sessions = [new() { Windows = [new() { Id = a }, new() { Id = b, IsMissing = true }], PaneCanvases = [c] }] };
                var store = new JsonStateStore(path, new NullLog()); store.Save(state); var loaded = store.Load(); var session = loaded.Sessions.Single();
                Check.Equal(2, session.Windows.Count); Check.Equal(2,PaneTree.Leaves(session.PaneCanvases.Single()).Count); Check.True(session.Windows.Single(w => w.Id == b).IsMissing);
            })),
            ("Malformed saved trees are repaired while membership survives", () => InTemporaryDirectory(path => {
                var (c, a, b) = Pair(); var root = c.Nodes.Single(n => !n.IsLeaf); root.SecondChildId = root.Id; c.ZoomedLeafId = Guid.NewGuid();
                var store = new JsonStateStore(path, new NullLog()); store.Save(new() { Sessions = [new() { Windows = [new() { Id = a }, new() { Id = b }], PaneCanvases = [c] }] });
                var session = store.Load().Sessions.Single(); Check.Equal(2, session.Windows.Count); Check.Equal(1, PaneTree.Leaves(session.PaneCanvases.Single()).Count); Check.Equal<Guid?>(null, session.PaneCanvases.Single().ZoomedLeafId);
            })),
            ("Malformed launch profiles normalize null fields and duplicate IDs", () => InTemporaryDirectory(path => {
                var store = new JsonStateStore(path, new NullLog()); var id = Guid.NewGuid();
                File.WriteAllText(store.FilePath, "{\"Version\":2,\"Settings\":{\"LaunchProfiles\":[null,{\"Id\":\"" + id + "\",\"Name\":null,\"Target\":null,\"Arguments\":null,\"WorkingDirectory\":null,\"ExpectedProcessName\":null},{\"Id\":\"" + id + "\"}]}}");
                var launchers = store.Load().Settings.LaunchProfiles; Check.Equal(2, launchers.Count); Check.Equal("", launchers[0].Target); Check.Equal("", launchers[0].Arguments);
                Check.False(launchers[0].Id == launchers[1].Id); Check.Equal("", launchers[0].ExpectedProcessName);
            })),
            ("Custom appearance settings persist with session state", () => InTemporaryDirectory(path => {
                var state=new WorkspaceState(); state.Settings.Theme.Preset="Custom"; state.Settings.Theme.Custom.Accent="#FF00AA"; state.Settings.Theme.Custom.Background="#101010";
                var store=new JsonStateStore(path,new NullLog()); store.Save(state); var loaded=store.Load();
                Check.Equal("Custom",loaded.Settings.Theme.Preset); Check.Equal("#FF00AA",loaded.Settings.Theme.Custom.Accent); Check.Equal("#101010",loaded.Settings.Theme.Custom.Background);
            })),
            ("Invalid persisted custom colors fall back without invalidating session data", () => InTemporaryDirectory(path => {
                var store=new JsonStateStore(path,new NullLog());
                File.WriteAllText(store.FilePath,"{\"Version\":3,\"Settings\":{\"Theme\":{\"Preset\":\"Custom\",\"Custom\":{\"Background\":\"broken\",\"Accent\":\" #abcdef \"}}}}");
                var loaded=store.Load(); Check.Equal("Custom",loaded.Settings.Theme.Preset); Check.Equal("#F5F7F9",loaded.Settings.Theme.Custom.Background); Check.Equal("#ABCDEF",loaded.Settings.Theme.Custom.Accent);
                Check.Equal<string?>(null,store.LastLoadError);
            })),
            ("Duplicate saved monitor canvases release later panes to floating", () => InTemporaryDirectory(path => {
                var (c, a, b) = Pair(); var (d, e, f) = Pair();
                var store = new JsonStateStore(path, new NullLog()); store.Save(new() { Sessions = [new() { Windows = new[] { a, b, e, f }.Select(id => new ManagedWindow { Id = id }).ToList(), PaneCanvases = [c, d] }] });
                var session = store.Load().Sessions.Single(); Check.Equal(4, session.Windows.Count); Check.Equal(1, session.PaneCanvases.Count);
            })),
            ("Unknown future state version retains existing corruption safeguards", () => InTemporaryDirectory(path => {
                var store = new JsonStateStore(path, new NullLog()); File.WriteAllText(store.FilePath, "{\"Version\":99}");
                Check.Equal(0, store.Load().Sessions.Count); Check.True(store.LastLoadError is not null); Check.True(Directory.GetFiles(path, "*.corrupt-*").Length > 0);
            })),
            ("Gesture identity distinguishes modified arrows and shifted symbols", () => {
                Check.False(new CommandGesture(0x25) == new CommandGesture(0x25, PrefixModifiers.Control));
                Check.False(new CommandGesture(0xDC) == new CommandGesture(0xDC, PrefixModifiers.Shift));
                Check.Equal(PrefixModifiers.None, new CommandGesture(0xDC).Modifiers);
            })
        };
        var failed = 0;
        foreach (var (name, run) in tests)
        {
            try { run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}\n{ex}"); }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} pure pane tests passed.");
        return (tests.Length - failed, failed);
    }

    private static PaneCanvas Canvas(PixelRect? work = null) => new() { MonitorDevice = "A", MonitorId = "physical-A", MonitorWorkArea = work ?? new(0, 0, 1920, 1040) };
    private static (PaneCanvas Canvas, Guid First, Guid Second) Pair(PaneOrientation orientation = PaneOrientation.Vertical, PixelRect? work = null)
    {
        var c = Canvas(work); var a = Guid.NewGuid(); var b = Guid.NewGuid(); PaneTree.Split(c, a, b, orientation); return (c, a, b);
    }
    private static void Throws(string code, Action action)
    {
        try { action(); }
        catch (PaneLayoutException ex) { Check.Equal(code, ex.Code); return; }
        throw new Exception($"Expected pane failure {code}.");
    }
    private static void Near(double expected, double actual) => Check.True(Math.Abs(expected - actual) < .00000001);
    private static void ExactCoverage(PaneCanvas canvas)
    {
        var layouts = PaneTree.Calculate(canvas).Values.ToArray();
        Check.Equal((long)canvas.MonitorWorkArea.Width * canvas.MonitorWorkArea.Height, layouts.Sum(l => (long)l.Bounds.Width * l.Bounds.Height));
        foreach (var layout in layouts) { Check.Reachable(layout); Check.True(layout.Bounds.Width >= PaneTree.MinimumWidth); Check.True(layout.Bounds.Height >= PaneTree.MinimumHeight); }
        for (var i = 0; i < layouts.Length; i++) for (var j = i + 1; j < layouts.Length; j++)
        {
            var a = layouts[i].Bounds; var b = layouts[j].Bounds;
            Check.True((long)a.X + a.Width <= b.X || (long)b.X + b.Width <= a.X || (long)a.Y + a.Height <= b.Y || (long)b.Y + b.Height <= a.Y);
        }
    }
    private static void InTemporaryDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "DeskMux.PaneTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try { action(path); } finally { Directory.Delete(path, recursive: true); }
    }
}
