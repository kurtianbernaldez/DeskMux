using DeskMux.Core;

static class GuideTests
{
    public static int Run()
    {
        var tests = new (string, Action)[]
        {
            ("Nested guides stay inside both vertical and horizontal parents", () => {
                var (c, a, b, d) = Tree(); var e = Guid.NewGuid();
                PaneTree.Split(c, d, e, PaneOrientation.Vertical);
                var g = PaneGuides.Calculate(c, new HashSet<Guid> { a,b,d,e }, a);
                Check.True(g.Dividers.Any(l => l.X1 == 600 && l.Y1 == 0 && l.Y2 == 800));
                Check.True(g.Dividers.Any(l => l.X1 == 600 && l.X2 == 1200 && l.Y1 == 400));
                Check.True(g.Dividers.Any(l => l.X1 == 900 && l.Y1 == 400 && l.Y2 == 800));
                Check.Equal(new PixelRect(0,0,600,800), g.Focus);
            }),
            ("Missing and floating windows do not create guides", () => {
                var (c,a,b,d) = Tree(); var floating = Guid.NewGuid();
                var g = PaneGuides.Calculate(c,new HashSet<Guid>{a,b,floating},floating);
                Check.Equal(1,g.Dividers.Count); Check.Equal<PixelRect?>(null,g.Focus);
                Check.Equal(2,PaneGuides.Calculate(c,new HashSet<Guid>{a,b,d},a).Dividers.Count);
            }),
            ("Focus changes move only the pane outline", () => {
                var (c,a,b,d)=Tree(); var live=new HashSet<Guid>{a,b,d};
                var first=PaneGuides.Calculate(c,live,a); var second=PaneGuides.Calculate(c,live,d);
                Check.Sequence(first.Dividers,second.Dividers); Check.Equal(new PixelRect(600,400,600,400),second.Focus);
                PaneTree.Swap(c,d,1); Check.False(second.Focus == PaneGuides.Calculate(c,live,d).Focus);
            }),
            ("Keyboard and native resize update scoped divider geometry", () => {
                var (c,a,b,d)=Tree(); var live=new HashSet<Guid>{a,b,d};
                PaneTree.Resize(c,d,PaneDirection.Down);
                Check.True(PaneGuides.Calculate(c,live,d).Dividers.Any(l=>l.Y1==440 && l.X1==600 && l.X2==1200));
                PaneTree.ResizeToBounds(c,d,new(700,500,500,300));
                var g=PaneGuides.Calculate(c,live,d);
                Check.True(g.Dividers.Any(l=>l.X1==700 && l.X2==1200 && l.Y1==500));
            }),
            ("Zoom shows only focused zoomed outline", () => {
                var (c,a,b,d)=Tree(); var live=new HashSet<Guid>{a,b,d}; c.ZoomedLeafId=PaneTree.FindLeaf(c,d)!.Id;
                var g=PaneGuides.Calculate(c,live,d); Check.Equal(0,g.Dividers.Count); Check.Equal(c.MonitorWorkArea,g.Focus);
                Check.Equal<PixelRect?>(null,PaneGuides.Calculate(c,live,a).Focus);
            }),
            ("Monitor geometry and fade clocks remain independent", () => {
                var (c,a,b,d)=Tree(); var other=PaneTree.Clone(c); other.MonitorWorkArea=new(-1200,200,1200,800); other.Dpi=144;
                var live=new HashSet<Guid>{a,b,d};
                Check.Equal(new PixelRect(-1200,200,600,800),PaneGuides.Calculate(other,live,a).Focus);
                Check.Equal(new PixelRect(0,0,600,800),PaneGuides.Calculate(c,live,a).Focus);
                var one=new PaneGuideVisibilityState(); var two=new PaneGuideVisibilityState(); var settings=new PaneGuideSettings();
                one.Changed(TimeSpan.Zero); Check.Equal(.65,one.Alpha(settings,TimeSpan.Zero,false,false)); Check.Equal(0d,two.Alpha(settings,TimeSpan.Zero,false,false));
            }),
            ("Visibility modes honor commands operations opacity and fade", () => {
                var v=new PaneGuideVisibilityState(); var s=new PaneGuideSettings{Opacity=.8,FadeDelayMs=1000};
                Check.Equal(0d,v.Alpha(s,TimeSpan.Zero,false,false)); Check.Equal(.8,v.Alpha(s,TimeSpan.Zero,true,false));
                Check.Equal(.8,v.Alpha(s,TimeSpan.Zero,false,true));
                s.Visibility=PaneGuideVisibility.Briefly; Check.Equal(0d,v.Alpha(s,TimeSpan.Zero,true,true));
                v.Changed(TimeSpan.Zero); Check.Equal(.8,v.Alpha(s,TimeSpan.FromMilliseconds(1000),false,false));
                Check.Equal(.4,v.Alpha(s,TimeSpan.FromMilliseconds(1125),false,false));
                Check.Equal(0d,v.Alpha(s,TimeSpan.FromMilliseconds(1250),false,false));
                v.Changed(TimeSpan.FromMilliseconds(1250)); Check.Equal(.8,v.Alpha(s,TimeSpan.FromMilliseconds(1250),false,false));
                s.Visibility=PaneGuideVisibility.Never; Check.Equal(0d,v.Alpha(s,TimeSpan.Zero,true,true));
                s.Visibility=PaneGuideVisibility.Always; Check.Equal(.8,v.Alpha(s,TimeSpan.FromDays(1),false,false));
            }),
            ("Custom guide colors and line style persist in existing state", () => {
                var dir=Path.Combine(Path.GetTempPath(),"DeskMux-guides-"+Guid.NewGuid());
                try {
                    var s=new WorkspaceState(); s.Settings.PaneGuides=new(){DividerColor="#123456",FocusColor="#ABCDEF",ResizeColor="#FEDCBA",Thickness=3,Opacity=.3,FadeDelayMs=2450,Visibility=PaneGuideVisibility.Briefly};
                    var store=new JsonStateStore(dir,new SilentLog()); store.Save(s); var loaded=store.Load().Settings.PaneGuides;
                    Check.Equal(s.Settings.PaneGuides.Resolve(new()),loaded.Resolve(new())); Check.Equal(2450,loaded.FadeDelayMs); Check.Equal(PaneGuideVisibility.Briefly,loaded.Visibility);
                    var theme=new ThemePalette(); var defaults=new PaneGuideSettings().Resolve(theme); Check.Equal(theme.Border,defaults.Divider); Check.Equal(theme.Accent,defaults.Focus); Check.Equal(theme.Text,defaults.Resize);
                } finally { if(Directory.Exists(dir)) Directory.Delete(dir,true); }
            }),
            ("Invalid settings are normalized", () => {
                var s=new PaneGuideSettings{Opacity=double.NaN,Thickness=-1,FadeDelayMs=-3,DividerColor="red"};s.Normalize();
                Check.Equal(.65,s.Opacity);Check.Equal(.5,s.Thickness);Check.Equal(0,s.FadeDelayMs);Check.Equal("",s.DividerColor);
            })
        };
        var failed=0;
        foreach(var (name,run) in tests) try {run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}
        return failed;
    }
    private static (PaneCanvas,Guid,Guid,Guid) Tree()
    {
        var c=new PaneCanvas{MonitorWorkArea=new(0,0,1200,800)};var a=Guid.NewGuid();var b=Guid.NewGuid();var d=Guid.NewGuid();
        PaneTree.Split(c,a,b,PaneOrientation.Vertical);PaneTree.Split(c,b,d,PaneOrientation.Horizontal);return(c,a,b,d);
    }
    private sealed class SilentLog : ILog { public void Write(string name,string message,object? data=null) {} }
}
