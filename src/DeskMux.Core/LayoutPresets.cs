namespace DeskMux.Core;

public sealed class LayoutPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string Name { get; set; } = "Layout";
    public List<PaneCanvas> Canvases { get; set; } = [];
    public List<PresetWindow> Windows { get; set; } = [];
}
public sealed class PresetWindow
{
    public Guid WindowId { get; set; }
    public Guid? LaunchProfileId { get; set; }
    public WindowFingerprint Fingerprint { get; set; } = new();
    public WindowLayout Layout { get; set; } = new();
}

public sealed partial class SessionManager
{
    private sealed record LayoutHistory(Guid SessionId, List<PaneCanvas> Canvases, Dictionary<Guid,WindowLayout> Layouts);
    private readonly List<LayoutHistory> _layoutHistory = [];
    private bool _restoringHistory;
    public bool CanUndoLayout => ActiveSession != null && _layoutHistory.Any(h=>h.SessionId==ActiveSession.Id);
    private void RememberLayout(Dictionary<Guid,List<PaneCanvas>> trees, PaneWindowBackup[] backups)
    {
        if(_restoringHistory || ActiveSession is not {} session || !trees.TryGetValue(session.Id,out var previous)) return;
        if(previous.Count==session.PaneCanvases.Count && session.PaneCanvases.All(c=>SameCanvasState(c,previous.FirstOrDefault(p=>p.Id==c.Id)))) return;
        _layoutHistory.Add(new(session.Id,previous,backups.ToDictionary(b=>b.Entry.Id,b=>MonitorMapper.Clone(b.Layout))));
        if(_layoutHistory.Count>64) _layoutHistory.RemoveAt(0);
    }
    public void UndoPaneLayout() => Change(()=>
    {
        if(ActiveSession is not {} session) return;
        var index=_layoutHistory.FindLastIndex(h=>h.SessionId==session.Id);
        if(index<0) { Error("There are no pane changes to undo in this session."); return; }
        var history=_layoutHistory[index]; _restoringHistory=true;
        try { if(RestoreLayout(session,history.Canvases,history.Layouts)) _layoutHistory.RemoveAt(index); }
        finally { _restoringHistory=false; }
    });
    private bool RestoreLayout(WorkspaceSession session, IEnumerable<PaneCanvas> canvases, IReadOnlyDictionary<Guid,WindowLayout> layouts)
    {
        return PaneTransaction(()=> {
            session.PaneCanvases=canvases.Select(PaneTree.Clone).ToList();
            var valid=session.Windows.Select(w=>w.Id).ToHashSet();
            foreach(var canvas in session.PaneCanvases) PaneTree.Validate(canvas,valid);
            MapPaneMonitors();
            foreach(var entry in session.Windows.Where(w=>CanvasFor(session,w.Id)==null && layouts.ContainsKey(w.Id))) {
                var layout=MonitorMapper.Map(layouts[entry.Id],_windows.GetMonitors());
                if(session==ActiveSession && !entry.IsMissing && SameWindow(entry)) {
                    _paneTouched?.Add(entry.Id);
                    if(entry.HiddenByDeskMux && !PaneShow(entry)) throw new InvalidOperationException("Could not reveal floating window.");
                    var result=_windows.ApplyLayout(entry,layout);
                    if(!result.Success) throw new InvalidOperationException(result.Error);
                }
                entry.Layout=layout;
            }
        },forceReapply:true);
    }
    public void SaveLayoutPreset(Guid sessionId, string name) => Change(()=> {
        var session=FindSession(sessionId); if(session==null)return;
        var preset=new LayoutPreset {SessionId=session.Id,Name=NormalizeName(name),Canvases=session.PaneCanvases.Select(PaneTree.Clone).ToList(),
            Windows=session.Windows.Select(w=>new PresetWindow {WindowId=w.Id,LaunchProfileId=w.LaunchProfileId,Fingerprint=Clone(w.Fingerprint),Layout=MonitorMapper.Clone(w.Layout)}).ToList()};
        State.LayoutPresets.Add(preset);
    });
    public void DeleteLayoutPreset(Guid id) => Change(()=>State.LayoutPresets.RemoveAll(p=>p.Id==id));
    public void ApplyLayoutPreset(Guid id) => Change(()=> {
        var preset=State.LayoutPresets.FirstOrDefault(p=>p.Id==id);
        var session=preset==null?null:FindSession(preset.SessionId); if(session==null||preset==null)return;
        var mapping=new Dictionary<Guid,Guid>(); var used=new HashSet<Guid>();
        foreach(var saved in preset.Windows) {
            var candidate=session.Windows.FirstOrDefault(w=>w.Id==saved.WindowId);
            if(candidate==null) {
                var matches=session.Windows.Where(w=>!used.Contains(w.Id) &&
                    (saved.LaunchProfileId!=null ? w.LaunchProfileId==saved.LaunchProfileId :
                    w.Fingerprint.ExecutablePath.Equals(saved.Fingerprint.ExecutablePath,StringComparison.OrdinalIgnoreCase) && w.Fingerprint.WindowClass==saved.Fingerprint.WindowClass && w.Fingerprint.Title==saved.Fingerprint.Title)).ToArray();
                if(matches.Length==1) candidate=matches[0];
            }
            if(candidate!=null && used.Add(candidate.Id))mapping[saved.WindowId]=candidate.Id;
        }
        var canvases=preset.Canvases.Select(PaneTree.Clone).ToList();
        foreach(var c in canvases) {
            foreach(var n in c.Nodes.Where(n=>n.WindowId!=null)) n.WindowId=mapping.TryGetValue(n.WindowId!.Value,out var current)?current:Guid.Empty;
            PaneTree.Validate(c,mapping.Values.ToHashSet()); c.ZoomedLeafId=null;
        }
        var layouts=preset.Windows.Where(w=>mapping.ContainsKey(w.WindowId)).ToDictionary(w=>mapping[w.WindowId],w=>w.Layout);
        if(RestoreLayout(session,canvases,layouts) && mapping.Count<preset.Windows.Count) Error("Layout applied to matching applications. Restore missing apps, then apply it again for the complete layout.");
    });
    public Guid? PaneDropTarget(long sourceHandle,int x,int y)
    {
        var session=ActiveSession;var source=session?.Windows.FirstOrDefault(w=>w.Handle==sourceHandle && !w.IsMissing);
        if(session==null||source==null||CanvasFor(session,source.Id)==null)return null;
        foreach(var canvas in session.PaneCanvases.Where(c=>c.ZoomedLeafId==null))
            foreach(var (id,layout) in PaneTree.Calculate(LiveCanvas(session,canvas))) {
                var b=layout.Bounds;
                if(id!=source.Id && x>=b.X && y>=b.Y && (long)x<b.X+(long)b.Width && (long)y<b.Y+(long)b.Height)return id;
            }
        return null;
    }
    public void DropPane(long handle,Guid targetId) => Change(()=> {
        var session=ActiveSession;var source=session?.Windows.FirstOrDefault(w=>w.Handle==handle && !w.IsMissing);
        var target=session?.Windows.FirstOrDefault(w=>w.Id==targetId && !w.IsMissing);
        if(source==null||target==null||session==null||source==target)return;
        var a=CanvasFor(session,source.Id);var b=CanvasFor(session,target.Id);
        if(a==null||b==null||!SameWindow(source)||!SameWindow(target))return;
        _interactivePanes.Remove(handle);
        if(PaneTransaction(()=> {
            var first=PaneTree.FindLeaf(a,source.Id)!;var second=PaneTree.FindLeaf(b,target.Id)!;
            (first.WindowId,second.WindowId)=(second.WindowId,first.WindowId);a.ZoomedLeafId=b.ZoomedLeafId=null;
        }))FocusPaneCore(source.Id);
    });
}
