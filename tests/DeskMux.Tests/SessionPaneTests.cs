using DeskMux.Core;

static class SessionPaneTests
{
    public static int Run()
    {
        var tests = new (string Name, Action Test)[]
        {
            ("Open unmanaged root without capturing unrelated foreground", () => {
                var f = Fixture.WithTwoWindows(); Check.True(f.Manager.OpenPane(2, 1, PaneOrientation.Vertical));
                Check.Equal(1, f.Manager.ActiveSession!.Windows.Count); Check.Equal(2L, f.Manager.ActiveSession.Windows[0].Handle);
                Check.Equal(new PixelRect(0, 0, 1920, 1040), f.Windows.Items[2].Layout.Bounds);
                Check.Equal(new PixelRect(100, 100, 900, 650), f.Windows.Items[1].Layout.Bounds);
            }),
            ("Floating source promoted and selected app occupies right pane", () => {
                var f = Split(); var a = f.Manager.ActiveSession!;
                Check.Equal(2, a.Windows.Count); Check.Equal(2, PaneTree.Leaves(a.PaneCanvases.Single()).Count);
                Check.Equal(new PixelRect(0,0,960,1040), f.Windows.Items[1].Layout.Bounds);
                Check.Equal(new PixelRect(960,0,960,1040), f.Windows.Items[2].Layout.Bounds); Check.Equal(2L, f.Windows.LastFocused);
            }),
            ("Attach floating target without duplicate membership", () => {
                var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!;
                f.Manager.AddWindow(1,a.Id); f.Manager.AddWindow(2,a.Id);
                Check.True(f.Manager.OpenPane(2,1,PaneOrientation.Horizontal)); Check.Equal(2,a.Windows.Count);
                Check.Equal(new PixelRect(0,520,1920,520), f.Windows.Items[2].Layout.Bounds);
            }),
            ("Add joins an existing full-monitor pane layout", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; f.Windows.Items[3]=Fixture.Snapshot(3,"Third");
                Check.True(f.Manager.AddWindowToActiveLayout(3)); Check.Equal(3,a.Windows.Count);
                Check.Equal(3,PaneTree.Leaves(a.PaneCanvases.Single()).Count);
                Check.True(f.Manager.PaneStatus(a.Windows.Single(w=>w.Handle==3).Id).StartsWith("Pane"));
            }),
            ("Add remains floating when its monitor has no pane canvas", () => {
                var f=Fixture.WithTwoWindows(); var a=f.Manager.ActiveSession!;
                Check.True(f.Manager.AddWindowToActiveLayout(1)); Check.Equal(1,a.Windows.Count);
                Check.Equal(0,a.PaneCanvases.Count); Check.Equal("Floating",f.Manager.PaneStatus(a.Windows.Single().Id));
            }),
            ("Duplicate selection only focuses existing pane", () => {
                var f = Split(); var before = f.Windows.LayoutCalls;
                Check.True(f.Manager.OpenPane(1,2,PaneOrientation.Horizontal));
                Check.Equal(2, f.Manager.ActiveSession!.Windows.Count); Check.Equal(before, f.Windows.LayoutCalls); Check.Equal(1L,f.Windows.LastFocused);
            }),
            ("Cross-session selection requires explicit confirmation", () => {
                var f = Fixture.WithTwoWindows(); var a = f.Manager.ActiveSession!; var b = f.Manager.CreateSession("B");
                f.Manager.AddWindow(1,a.Id); f.Manager.AddWindow(2,b.Id);
                Check.False(f.Manager.OpenPane(2,1,PaneOrientation.Vertical)); Check.Equal(1,b.Windows.Count); Check.Equal(0,a.PaneCanvases.Count);
                Check.True(f.Manager.OpenPane(2,1,PaneOrientation.Vertical,true)); Check.Equal(0,b.Windows.Count); Check.Equal(2,a.Windows.Count);
                Check.True(f.Windows.Items[2].IsVisible);
            }),
            ("Selection failure preserves source rectangle and membership", () => {
                var f = Fixture.WithTwoWindows(); f.Manager.AddWindow(1,f.Manager.ActiveSession!.Id); var before=f.Windows.Items[1].Layout.Bounds;
                Check.False(f.Manager.OpenPane(999,1,PaneOrientation.Vertical)); Check.Equal(before,f.Windows.Items[1].Layout.Bounds);
                Check.Equal(0,f.Manager.ActiveSession.PaneCanvases.Count); Check.Equal(1,f.Manager.ActiveSession.Windows.Count);
            }),
            ("Disappeared source yields selected root", () => {
                var f = Fixture.WithTwoWindows(); f.Manager.AddWindow(1,f.Manager.ActiveSession!.Id); f.Windows.Items.Remove(1);
                Check.True(f.Manager.OpenPane(2,1,PaneOrientation.Vertical)); Check.Equal(1,PaneTree.Leaves(f.Manager.ActiveSession.PaneCanvases.Single()).Count);
            }),
            ("Nested split, navigation, swap and same-application focus", () => {
                var f = Split(); f.Windows.Items[3]=Fixture.Snapshot(3,"Third");
                Check.True(f.Manager.OpenPane(3,2,PaneOrientation.Horizontal));
                f.Manager.NavigatePane(3,PaneDirection.Up); Check.Equal(2L,f.Windows.LastFocused);
                f.Manager.NavigatePane(2,PaneDirection.Left); Check.Equal(1L,f.Windows.LastFocused);
                var old=f.Windows.Items[2].Layout.Bounds; f.Manager.SwapPane(1,1);
                Check.Equal(old,f.Windows.Items[1].Layout.Bounds); Check.Equal(1L,f.Windows.LastFocused);
            }),
            ("Foreground failure retains previous focus history", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var entry=a.Windows.Single(w=>w.Handle==1);
                var time=entry.LastFocusedUtc; f.Windows.FailFocus=true; f.Manager.NavigatePane(2,PaneDirection.Left);
                Check.Equal(time,entry.LastFocusedUtc); Check.Equal(2L,f.Windows.LastFocused); Check.Contains("foreground",f.Manager.LastError!);
            }),
            ("Directional command without pane never switches sessions", () => {
                var f=Fixture.WithTwoWindows(); var active=f.Manager.ActiveSession!.Id; f.Manager.CreateSession("B");
                f.Manager.NavigatePane(1,PaneDirection.Down); Check.Equal(active,f.Manager.ActiveSession!.Id); Check.Contains("pane",f.Manager.LastError!);
            }),
            ("Resize nearest divider by five percent", () => {
                var f=Split(); f.Manager.ResizePane(1,PaneDirection.Right); Check.Equal(1056,f.Windows.Items[1].Layout.Bounds.Width);
                var root=f.Manager.ActiveSession!.PaneCanvases.Single(); Check.Equal(.55,root.Nodes.Single(n=>!n.IsLeaf).Ratio);
            }),
            ("Resize failure rolls back ratios and already moved windows", () => {
                var f=Split(); var previous=f.Windows.Items[1].Layout.Bounds; f.Windows.FailLayoutCalls.Add(f.Windows.LayoutCalls+2);
                f.Manager.ResizePane(1,PaneDirection.Right); Check.Equal(previous,f.Windows.Items[1].Layout.Bounds);
                Check.Equal(.5,f.Manager.ActiveSession!.PaneCanvases.Single().Nodes.Single(n=>!n.IsLeaf).Ratio);
                Check.Contains("restored",f.Manager.LastError!);
            }),
            ("Initial layout failure undoes new membership", () => {
                var f=Fixture.WithTwoWindows(); var a=f.Manager.ActiveSession!; f.Manager.AddWindow(1,a.Id); var previous=f.Windows.Items[1].Layout.Bounds;
                f.Windows.FailLayoutCalls.Add(2); Check.False(f.Manager.OpenPane(2,1,PaneOrientation.Vertical));
                Check.Equal(1,a.Windows.Count); Check.Equal(0,a.PaneCanvases.Count); Check.Equal(previous,f.Windows.Items[1].Layout.Bounds); Check.True(f.Windows.Items[2].IsVisible);
            }),
            ("Partial rollback reports failure and pauses hiding", () => {
                var f=Split(); var next=f.Windows.LayoutCalls; f.Windows.FailLayoutCalls.UnionWith([next+2,next+3]);
                f.Manager.ResizePane(1,PaneDirection.Right); Check.True(f.Manager.HidingPaused); Check.Contains("incomplete",f.Manager.LastError!);
                Check.True(f.Windows.Items.Values.All(w=>w.IsVisible));
            }),
            ("Protected target stays visible and unadded", () => {
                var f=Fixture.WithTwoWindows(); f.Manager.AddWindow(1,f.Manager.ActiveSession!.Id); f.Windows.ProtectedHandles.Add(2);
                Check.False(f.Manager.OpenPane(2,1,PaneOrientation.Vertical)); Check.True(f.Windows.Items[2].IsVisible); Check.Equal(1,f.Manager.ActiveSession.Windows.Count);
            }),
            ("Zoom hides siblings and unzoom restores exact tree", () => {
                var f=Split(); var before=f.Windows.Items[2].Layout.Bounds;
                f.Manager.TogglePaneZoom(2); Check.False(f.Windows.Items[1].IsVisible); Check.True(f.Manager.ActiveSession!.Windows[0].HiddenByDeskMux);
                Check.Equal(new PixelRect(0,0,1920,1040),f.Windows.Items[2].Layout.Bounds);
                f.Manager.TogglePaneZoom(2); Check.True(f.Windows.Items[1].IsVisible); Check.Equal(before,f.Windows.Items[2].Layout.Bounds);
            }),
            ("Zoom hide failure restores tree and leaves apps visible", () => {
                var f=Split(); f.Windows.FailHide=true; f.Manager.TogglePaneZoom(2);
                Check.Equal<Guid?>(null,f.Manager.ActiveSession!.PaneCanvases.Single().ZoomedLeafId); Check.True(f.Windows.Items.Values.All(w=>w.IsVisible));
            }),
            ("Unzoom restore failure leaves journaled sibling recoverable", () => {
                var f=Split(); f.Manager.TogglePaneZoom(2); f.Windows.FailShow=true; f.Manager.TogglePaneZoom(2);
                Check.True(f.Manager.ActiveSession!.Windows.Any(w=>w.HiddenByDeskMux)); Check.True(f.Manager.LastError != null);
                f.Windows.FailShow=false; f.Manager.ShowAll(); Check.True(f.Windows.Items.Values.All(w=>w.IsVisible));
            }),
            ("Zoom survives session switching", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var b=f.Manager.CreateSession("B"); f.Manager.TogglePaneZoom(2);
                f.Manager.SwitchTo(b.Id); Check.True(f.Windows.Items.Values.All(w=>!w.IsVisible)); f.Manager.SwitchTo(a.Id);
                Check.False(f.Windows.Items[1].IsVisible); Check.True(f.Windows.Items[2].IsVisible); Check.True(a.PaneCanvases.Single().ZoomedLeafId != null);
            }),
            ("Shutdown clears zoom and recovers every sibling", () => {
                var f=Split(); f.Manager.TogglePaneZoom(2); f.Manager.Shutdown();
                Check.True(f.Windows.Items.Values.All(w=>w.IsVisible)); Check.Equal<Guid?>(null,f.Manager.ActiveSession!.PaneCanvases.Single().ZoomedLeafId);
                Check.Equal(new PixelRect(960,0,960,1040),f.Windows.Items[2].Layout.Bounds);
            }),
            ("Pane commands unzoom predictably", () => {
                var f=Split(); f.Manager.TogglePaneZoom(2); f.Manager.NavigatePane(2,PaneDirection.Left);
                Check.True(f.Windows.Items[1].IsVisible); Check.Equal(1L,f.Windows.LastFocused); Check.Equal<Guid?>(null,f.Manager.ActiveSession!.PaneCanvases.Single().ZoomedLeafId);
            }),
            ("Remove pane without closing app and release lone survivor", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; f.Manager.RemoveWindow(a.Windows.Single(w=>w.Handle==2).Id);
                Check.Equal(2,f.Windows.Items.Count); Check.True(f.Windows.Items[2].IsVisible); Check.Equal(1,a.Windows.Count);
                Check.Equal(0,a.PaneCanvases.Count); Check.Equal(new PixelRect(0,0,960,1040),f.Windows.Items[1].Layout.Bounds);
                Check.Equal("Floating",f.Manager.PaneStatus(a.Windows.Single().Id)); Check.Equal(1L,f.Windows.LastFocused);
                f.Manager.RemoveWindow(a.Windows.Single().Id); Check.Equal(0,a.PaneCanvases.Count); Check.True(f.Windows.Items[1].IsVisible);
            }),
            ("Release from a two-pane canvas makes both survivors floating", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var entry=a.Windows.Single(w=>w.Handle==2);
                f.Manager.TogglePaneZoom(1); f.Manager.ReleasePane(entry.Id); Check.True(f.Windows.Items[2].IsVisible);
                Check.Equal(2,a.Windows.Count); Check.Equal(0,a.PaneCanvases.Count);
                Check.True(a.Windows.All(w=>f.Manager.PaneStatus(w.Id)=="Floating"));
            }),
            ("Interactive move releases a pane so its new position persists", () => {
                var f=Split(); var a=f.Manager.ActiveSession!;
                Check.True(f.Manager.ReleasePaneForManualMove(2)); Check.Equal(0,a.PaneCanvases.Count);
                f.Windows.SetBounds(2,new(180,140,700,500)); f.Manager.TrackWindow(2,false);
                Check.Equal(new PixelRect(180,140,700,500),a.Windows.Single(w=>w.Handle==2).Layout.Bounds);
            }),
            ("Releasing a zoomed pane also reveals the floating survivor", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; f.Manager.TogglePaneZoom(1);
                Check.True(f.Manager.ReleasePaneForManualMove(1)); Check.Equal(0,a.PaneCanvases.Count);
                Check.True(f.Windows.Items[1].IsVisible); Check.True(f.Windows.Items[2].IsVisible);
                Check.True(a.Windows.All(w=>!w.HiddenByDeskMux));
            }),
            ("Move pane collapses source and arrives floating", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var b=f.Manager.CreateSession("B"); var entry=a.Windows.Single(w=>w.Handle==2);
                f.Manager.MoveWindow(entry.Id,b.Id); Check.Equal(1,a.Windows.Count); Check.Equal(entry.Id,b.Windows.Single().Id);
                Check.Equal(0,a.PaneCanvases.Count); Check.Equal(0,b.PaneCanvases.Count); Check.False(f.Windows.Items[2].IsVisible);
                Check.Equal(960,f.Windows.Items[1].Layout.Bounds.Width);
            }),
            ("Release inactive pane keeps floating membership hidden", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var entry=a.Windows[0]; var b=f.Manager.CreateSession("B");
                f.Manager.SwitchTo(b.Id); f.Manager.ReleasePane(entry.Id);
                Check.Equal("Floating",f.Manager.PaneStatus(entry.Id)); Check.False(f.Windows.Items[entry.Handle].IsVisible);
                f.Manager.SwitchTo(a.Id); Check.True(f.Windows.Items[entry.Handle].IsVisible);
            }),
            ("Manager focus switches to pane owner before activation", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var b=f.Manager.CreateSession("B"); f.Manager.SwitchTo(b.Id);
                Check.True(f.Manager.FocusPane(a.Windows[0].Id)); Check.Equal(a.Id,f.Manager.ActiveSession!.Id); Check.Equal(1L,f.Windows.LastFocused);
            }),
            ("Switch raises every visible member above unmanaged windows", () => {
                var f=Fixture.WithTwoWindows(); var a=f.Manager.ActiveSession!; var b=f.Manager.CreateSession("B");
                f.Manager.AddWindow(1,a.Id); f.Manager.AddWindow(2,a.Id); f.Manager.SwitchTo(b.Id); f.Manager.SwitchTo(a.Id);
                Check.Sequence(new long[]{1,2},f.Windows.Raised.Order()); Check.True(f.Windows.LastFocused is 1 or 2);
            }),
            ("Zoomed session raises only its visible pane", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var b=f.Manager.CreateSession("B"); f.Manager.TogglePaneZoom(2);
                f.Manager.SwitchTo(b.Id); f.Manager.SwitchTo(a.Id); Check.Sequence(new long[]{2},f.Windows.Raised);
            }),
            ("Move failure restores original membership and pane tree", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; var b=f.Manager.CreateSession("B"); f.Windows.FailHide=true;
                f.Manager.MoveWindow(a.Windows[1].Id,b.Id); Check.Equal(2,a.Windows.Count); Check.Equal(0,b.Windows.Count);
                Check.Equal(2,PaneTree.Leaves(a.PaneCanvases.Single()).Count); Check.True(f.Windows.Items[2].IsVisible);
            }),
            ("External close retains missing record and reflows live leaf", () => {
                var f=Split(); var a=f.Manager.ActiveSession!; f.Windows.Items.Remove(2); f.Manager.TrackWindow(2,false); f.Manager.FlushPaneLayoutChanges();
                Check.Equal(2,a.Windows.Count); Check.True(a.Windows.Single(w=>w.Fingerprint.Title=="Document B").IsMissing);
                Check.Equal(2,PaneTree.Leaves(a.PaneCanvases.Single()).Count); Check.Equal(1920,f.Windows.Items[1].Layout.Bounds.Width);
            }),
            ("User edits are debounced and authoritative pane geometry restored", () => {
                var f=Split(); var previous=f.Windows.Items[1].Layout.Bounds; var calls=f.Windows.LayoutCalls;
                f.Windows.SetBounds(1,new(150,150,400,300)); f.Manager.TrackWindow(1,false);
                Check.Equal(calls,f.Windows.LayoutCalls); f.Manager.FlushPaneLayoutChanges(); Check.Equal(previous,f.Windows.Items[1].Layout.Bounds);
                calls=f.Windows.LayoutCalls; f.Manager.TrackWindow(1,false); f.Manager.FlushPaneLayoutChanges(); Check.Equal(calls,f.Windows.LayoutCalls);
            }),
            ("Multiple monitors stay independent during zoom", () => {
                var f=TwoCanvases(); var previous=f.Windows.Items[3].Layout.Bounds; f.Manager.TogglePaneZoom(2);
                Check.True(f.Windows.Items[3].IsVisible); Check.Equal(previous,f.Windows.Items[3].Layout.Bounds);
            }),
            ("Busy pane on another monitor cannot block local zoom", () => {
                var f=TwoCanvases(); f.Windows.ProtectedHandles.Add(3); f.Manager.TogglePaneZoom(2);
                Check.False(f.Windows.Items[1].IsVisible); Check.True(f.Windows.Items[2].IsVisible); Check.True(f.Windows.Items[3].IsVisible);
                Check.False(f.Manager.HidingPaused);
            }),
            ("Zoom sibling showing itself is safely hidden again", () => {
                var f=Split(); f.Manager.TogglePaneZoom(2); f.Windows.Items[1]=f.Windows.Items[1] with { IsVisible=true };
                f.Manager.TrackWindow(1,true); f.Manager.FlushPaneLayoutChanges(); Check.False(f.Windows.Items[1].IsVisible);
                Check.True(f.Manager.ActiveSession!.Windows[0].HiddenByDeskMux);
            }),
            ("Display removal releases displaced tree instead of stacking canvases", () => {
                var f=TwoCanvases(); f.Windows.Monitors.RemoveAt(1); f.Manager.HandleDisplayChange();
                Check.Equal(1,f.Manager.ActiveSession!.PaneCanvases.Count); var entry=f.Manager.ActiveSession.Windows.Single(w=>w.Handle==3);
                Check.Equal("Floating",f.Manager.PaneStatus(entry.Id)); Check.Reachable(entry.Layout); Check.True(f.Windows.Items[3].IsVisible);
            }),
            ("Lost target during placement removes invalid restored leaf", () => {
                var f=Split(); f.Windows.BeforeLayout = w => { if(w.Handle==2) f.Windows.Items.Remove(2); };
                f.Manager.ResizePane(1,PaneDirection.Right); f.Windows.BeforeLayout=null; f.Manager.FlushPaneLayoutChanges();
                Check.Equal(2,PaneTree.Leaves(f.Manager.ActiveSession!.PaneCanvases.Single()).Count);
                Check.True(f.Manager.ActiveSession.Windows.Single(w=>w.Handle==0).IsMissing); Check.True(f.Windows.Items[1].IsVisible);
            }),
        };
        var failures=0;
        foreach(var (name,test) in tests)
            try { test(); Console.WriteLine("PASS " + name); }
            catch(Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
        Console.WriteLine($"{tests.Length-failures}/{tests.Length} pane session tests passed.");
        return failures;
    }

    private static Fixture Split()
    {
        var f=Fixture.WithTwoWindows(); f.Manager.AddWindow(1,f.Manager.ActiveSession!.Id);
        Check.True(f.Manager.OpenPane(2,1,PaneOrientation.Vertical)); return f;
    }
    private static Fixture TwoCanvases()
    {
        var f=Split(); f.Windows.Monitors.Add(Fixture.Monitor("B",new(-1600,0,1600,900),false) with { Dpi=144 });
        var snapshot=Fixture.Snapshot(3,"Other monitor"); snapshot.Layout.MonitorDevice="B"; snapshot.Layout.MonitorWorkArea=new(-1600,0,1600,900); snapshot.Layout.Bounds=new(-1500,100,900,650); snapshot.Layout.Dpi=144;
        f.Windows.Items[3]=snapshot; Check.True(f.Manager.OpenPane(3,0,PaneOrientation.Vertical)); return f;
    }
}
