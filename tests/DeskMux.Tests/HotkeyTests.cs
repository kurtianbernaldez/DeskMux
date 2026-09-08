using DeskMux.Core;
static class HotkeyTests
{
    public static int Run()
    {
        var tests = new (string,Action)[] {
            ("Default shortcuts are unique and include release",()=>{
                var s=new AppSettings(); Check.Equal<string?>(null,Hotkeys.Validate(s));
                Check.Equal("Release",Hotkeys.Resolve(s,new(0x46))!.Id);
            }),
            ("Every command supports custom bindings without old-key fallback",()=>{
                foreach(var d in Hotkeys.All) {
                    var s=new AppSettings(); var custom=new CommandGesture(0x70,PrefixModifiers.Alt);
                    s.CommandBindings[d.Id]=custom;
                    Check.Equal<string?>(null,Hotkeys.Validate(s));
                    Check.Equal(d,Hotkeys.Resolve(s,custom,d.Global));
                    Check.Equal<HotkeyDefinition?>(null,Hotkeys.Resolve(s,d.Default,d.Global));
                }
            }),
            ("Conflicts reject command prefix and emergency collisions",()=>{
                var s=new AppSettings(); s.CommandBindings["Release"]=new(0x58);Check.True(Hotkeys.Validate(s)!=null);
                s.CommandBindings["Release"]=new(s.PrefixVirtualKey,s.PrefixModifiers);Check.True(Hotkeys.Validate(s)!=null);
                s.CommandBindings["Release"]=Hotkeys.All.Single(d=>d.Global).Default;Check.True(Hotkeys.Validate(s)!=null);
            }),
            ("Remapped resize and picker keep input handling metadata",()=>{
                var s=new AppSettings();s.CommandBindings["ResizeLeft"]=new(0x48,PrefixModifiers.Shift);
                s.CommandBindings["SplitRight"]=new(0x50);
                Check.True(Hotkeys.Resolve(s,new(0x48,PrefixModifiers.Shift))!.Repeat);
                Check.True(Hotkeys.Resolve(s,new(0x50))!.Picker);
            }),
            ("Custom shortcuts survive state roundtrip",()=>{
                var dir=Path.Combine(Path.GetTempPath(),"DeskMux-hotkeys-"+Guid.NewGuid());
                try { var state=new WorkspaceState(); state.Settings.CommandBindings["Release"]=new(0x55,PrefixModifiers.Shift);
                    var store=new JsonStateStore(dir,new Quiet());store.Save(state);
                    Check.Equal("Release",Hotkeys.Resolve(store.Load().Settings,new(0x55,PrefixModifiers.Shift))!.Id);
                } finally { if(Directory.Exists(dir))Directory.Delete(dir,true); }
            })
        };
        var failures=0;foreach(var (name,run) in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}return failures;
    }
    private sealed class Quiet:ILog{public void Write(string name,string message,object? data=null){}}
}
