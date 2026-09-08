using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DeskMux.App.UI;
internal sealed class PaneDragController : IDisposable
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(50)};
    private PaneGuideOverlay? _preview;
    private long _handle;
    private PixelRect? _original;
    private Guid? _session;
    public PaneDragController(AppController controller){_controller=controller;_timer.Tick+=(_,_)=>Preview();}
    public void Begin(long handle)
    {
        Clear();
        if((GetAsyncKeyState(0x12)&0x8000)!=0) { _controller.Sessions.ReleasePaneForManualMove(handle); return; }
        _handle=handle;_session=_controller.Sessions.State.ActiveSessionId;
        var snapshot=_controller.Windows.Inspect(handle);_original=snapshot?.VisibleBounds??snapshot?.Layout.Bounds;
        _controller.Sessions.BeginPaneMoveSize(handle);_timer.Start();
    }
    private Guid? Target()
    {
        if(_session!=_controller.Sessions.State.ActiveSessionId || _original==null)return null;
        var snapshot=_controller.Windows.Inspect(_handle);var current=snapshot?.VisibleBounds??snapshot?.Layout.Bounds;
        if(current==null || current.Width!=_original.Width || current.Height!=_original.Height ||
            Math.Abs(current.X-_original.X)+Math.Abs(current.Y-_original.Y)<8)return null;
        GetCursorPos(out var point);return _controller.Sessions.PaneDropTarget(_handle,point.X,point.Y);
    }
    private void Preview()
    {
        try {
            if(Target() is {} id && _controller.Sessions.ActiveSession is {} session) {
                var canvas=session.PaneCanvases.First(c=>PaneTree.FindLeaf(c,id)!=null);
                var rect=session.Windows.First(w=>w.Id==id).Layout.Bounds;
                _preview??=new();var style=_controller.Sessions.State.Settings.PaneGuides.Resolve(ThemeManager.Resolve(_controller.Sessions.State.Settings.Theme));
                _preview.Update(canvas.MonitorWorkArea,new([],rect),style with{Thickness=3},.9,new HashSet<Guid>());
            } else _preview?.Hide();
        } catch(PaneLayoutException){_preview?.Hide();}
    }
    public void End(long handle)
    {
        var target=_handle==handle?Target():null;
        if(target is {} id)_controller.Sessions.DropPane(handle,id);
        _controller.Sessions.EndPaneMoveSize(handle);Clear();
    }
    private void Clear(){_timer.Stop();_preview?.Close();_preview=null;_handle=0;_original=null;}
    public void Dispose()=>Clear();
    [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
}
