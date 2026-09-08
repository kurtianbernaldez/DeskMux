using DeskMux.Core;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
static class WorkflowTests
{
    public static int Run()
    {
        var tests=new (string,Action)[]{
            ("Shortcut export import keeps every binding and prefix",()=>{
                var s=new AppSettings();s.PrefixVirtualKey=0x20;s.CommandBindings["Release"]=new(0x54,PrefixModifiers.Shift);
                var imported=ShortcutProfile.Import(ShortcutProfile.Export(s));Check.Equal(0x20,imported.PrefixVirtualKey);
                foreach(var d in Hotkeys.All)Check.Equal(Hotkeys.Get(s,d),Hotkeys.Get(imported,d));
            }),
            ("Shortcut import rejects conflicts and unsupported versions",()=>{
                var s=new ShortcutProfile{Commands=new(){["Release"]=new(0x58)}};Reject(()=>ShortcutProfile.Import(JsonSerializer.Serialize(s)));
                s.Version=2;Reject(()=>ShortcutProfile.Import(JsonSerializer.Serialize(s)));
            }),
            ("New Undo command preserves existing personalized U shortcut",()=>{
                var s=new AppSettings{CommandBindings=new(){["Release"]=new(0x55)}};
                Check.Equal<string?>(null,Hotkeys.Validate(s));Check.Equal("Release",Hotkeys.Resolve(s,new(0x55))!.Id);
            }),
            ("Preset and reconnect history persist with workspace state",()=>{
                InDirectory(dir=>{
                    var state=new WorkspaceState();var session=new WorkspaceSession();state.Sessions.Add(session);
                    var window=new ManagedWindow();session.Windows.Add(window);var canvas=new PaneCanvas();PaneTree.Split(canvas,null,window.Id,PaneOrientation.Vertical);
                    session.SuspendedPaneCanvases.Add(canvas);state.LayoutPresets.Add(new(){SessionId=session.Id,Canvases=[canvas],Windows=[new(){WindowId=window.Id}]});
                    var store=new JsonStateStore(dir,new Quiet());store.Save(state);var loaded=store.Load();
                    Check.Equal(canvas.Id,loaded.Sessions[0].SuspendedPaneCanvases[0].Id);Check.Equal(window.Id,loaded.LayoutPresets[0].Windows[0].WindowId);
                });
            }),
            ("Update verifies every file and refuses modified executables",()=>{
                InDirectory(dir=>{
                    File.WriteAllText(Path.Combine(dir,"DeskMux.exe"),"fixture");var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir,"DeskMux.exe"))));
                    var m=new PackageManifest{Version="test",Files=new(){["DeskMux.exe"]=hash}};File.WriteAllText(Path.Combine(dir,"build.json"),JsonSerializer.Serialize(m));
                    Check.Equal("test",UpdatePackage.Verify(dir).Version);File.AppendAllText(Path.Combine(dir,"DeskMux.exe"),"changed");Reject(()=>UpdatePackage.Verify(dir));
                    m.Files["../escape"]="bad";File.WriteAllText(Path.Combine(dir,"build.json"),JsonSerializer.Serialize(m));Reject(()=>UpdatePackage.Verify(dir));
                });
            }),
            ("Update packages cannot overwrite portable user data",()=>{
                InDirectory(dir=>{var m=new PackageManifest{Files=new(){["Data/sessions.json"]="bad",["DeskMux.exe"]="bad"}};
                    File.WriteAllText(Path.Combine(dir,"build.json"),JsonSerializer.Serialize(m));Reject(()=>UpdatePackage.Verify(dir));});
            }),
            ("Update extraction rejects archive traversal",()=>{
                InDirectory(dir=>{var zip=Path.Combine(dir,"bad.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){using var writer=new StreamWriter(z.CreateEntry("../outside.txt").Open());writer.Write("bad");}
                    Reject(()=>UpdatePackage.Extract(zip,Path.Combine(dir,"extracted")));Check.False(File.Exists(Path.Combine(dir,"outside.txt")));});
            })
        };
        var failures=0;foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL "+name+" "+e);}return failures;
    }
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is IOException or InvalidDataException){return;}throw new Exception("Expected rejection");}
    private static void InDirectory(Action<string> action){var dir=Path.Combine(Path.GetTempPath(),"DeskMux-workflow-"+Guid.NewGuid());Directory.CreateDirectory(dir);try{action(dir);}finally{Directory.Delete(dir,true);}}
    private sealed class Quiet:ILog{public void Write(string a,string b,object? c=null){}}
}
