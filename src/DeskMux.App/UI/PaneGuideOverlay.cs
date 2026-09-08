using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DeskMux.App.UI;

internal sealed class PaneGuideOverlays : IDisposable
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<Guid, View> _views = [];
    private long _resizing;
    public bool CommandMode { get; set; }
    public bool Operation { get; set; }
    private sealed class View
    {
        public PaneGuideOverlay Window { get; } = new();
        public PaneGuideVisibilityState Visibility { get; } = new();
        public PaneGuideGeometry? Previous;
        public string Signature = "";
        public HashSet<Guid> Active = [];
        public TimeSpan ActiveUntil;
    }
    public PaneGuideOverlays(AppController controller)
    {
        _controller = controller;
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }
    public void BeginResize(long handle) => _resizing = handle;
    public void EndResize(long handle) { if (_resizing == handle) _resizing = 0; }
    private void Refresh()
    {
        var session = _controller.Sessions.ActiveSession;
        var keep = new HashSet<Guid>();
        if (session != null && !_controller.Sessions.HidingPaused)
        {
            var snapshots = session.Windows.Where(w => !w.IsMissing && _controller.Windows.IsSameWindow(w))
                .Select(w => (Window: w, Snapshot: _controller.Windows.Inspect(w.Handle)))
                .Where(p => p.Snapshot is { IsVisible: true } && p.Snapshot.Layout.ShowState != WindowShowState.Minimized).ToArray();
            var live = snapshots.Select(p => p.Window.Id).ToHashSet();
            var focused = snapshots.FirstOrDefault(p => p.Window.Handle == _controller.Windows.ForegroundWindow).Window?.Id;
            foreach (var original in session.PaneCanvases)
            {
                var canvas = PaneTree.Clone(original);
                try
                {
                    // Hidden zoom siblings must survive pruning, but never produce guides.
                    var valid = canvas.ZoomedLeafId != null ? session.Windows.Where(w => !w.IsMissing).Select(w => w.Id).ToHashSet() : live;
                    PaneTree.Validate(canvas, valid);
                    var moving = snapshots.FirstOrDefault(p => p.Window.Handle == _resizing);
                    if (moving.Window != null && canvas.ZoomedLeafId == null)
                        PaneTree.ResizeToBounds(canvas, moving.Window.Id, moving.Snapshot!.VisibleBounds ?? moving.Snapshot.Layout.Bounds);
                    var geometry = PaneGuides.Calculate(canvas, valid, focused);
                    if (geometry.Dividers.Count == 0 && geometry.Focus == null) continue;
                    keep.Add(canvas.Id);
                    if (!_views.TryGetValue(canvas.Id, out var view)) _views.Add(canvas.Id, view = new View());
                    var now = _clock.Elapsed;
                    var signature = string.Join(";", canvas.Nodes.Select(n => $"{n.Id}:{n.WindowId}"));
                    if (view.Signature != signature || view.Previous == null || view.Previous.Focus != geometry.Focus || !view.Previous.Dividers.SequenceEqual(geometry.Dividers))
                    {
                        view.Signature = signature;
                        view.Visibility.Changed(now);
                        if (view.Previous != null)
                        {
                            var old = view.Previous.Dividers.ToDictionary(l => l.NodeId);
                            var changed = geometry.Dividers.Where(l => old.TryGetValue(l.NodeId, out var before) && before != l).Select(l => l.NodeId).ToHashSet();
                            if (changed.Count > 0) { view.Active = changed; view.ActiveUntil = now + TimeSpan.FromMilliseconds(700); }
                        }
                        view.Previous = geometry;
                    }
                    if (now > view.ActiveUntil && _resizing == 0) view.Active.Clear();
                    var settings = _controller.Sessions.State.Settings;
                    var alpha = view.Visibility.Alpha(settings.PaneGuides, now, CommandMode, Operation || _resizing != 0);
                    view.Window.Update(canvas.MonitorWorkArea, geometry, settings.PaneGuides.Resolve(ThemeManager.Resolve(settings.Theme)), alpha, view.Active);
                }
                catch (PaneLayoutException) { /* A transient impossible layout has no overlay. */ }
            }
        }
        foreach (var id in _views.Keys.Where(id => !keep.Contains(id)).ToArray()) { _views[id].Window.Close(); _views.Remove(id); }
    }
    public void Dispose() { _timer.Stop(); foreach (var view in _views.Values) view.Window.Close(); _views.Clear(); }
}

internal sealed class PaneGuideOverlay : Window
{
    private readonly GuideDrawing _drawing = new();
    public PaneGuideOverlay()
    {
        Title = "DeskMux pane guides";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
        IsHitTestVisible = false; Content = _drawing;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x08000000 | 0x80 | 0x20);
            HwndSource.FromHwnd(hwnd)?.AddHook((nint h, int m, nint w, nint l, ref bool handled) =>
            {
                if (m == 0x84) { handled = true; return new nint(-1); }
                if (m == 0x21) { handled = true; return new nint(3); }
                return 0;
            });
        };
    }
    public void Update(PixelRect bounds, PaneGuideGeometry geometry, GuideStyle style, double alpha, ISet<Guid> active)
    {
        if (alpha <= 0) { Hide(); return; }
        SetWindowPos(new WindowInteropHelper(this).EnsureHandle(), new nint(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
        Opacity = alpha;
        _drawing.Bounds = bounds; _drawing.Geometry = geometry; _drawing.GuideStyle = style; _drawing.Active = active;
        if (!IsVisible) Show();
        _drawing.InvalidateVisual();
    }
    private sealed class GuideDrawing : FrameworkElement
    {
        public PixelRect Bounds = new(0, 0, 1, 1);
        public PaneGuideGeometry Geometry = new([], null);
        public GuideStyle GuideStyle = new("#FFFFFF", "#FFFFFF", "#FFFFFF", 1, 1);
        public ISet<Guid> Active = new HashSet<Guid>();
        protected override void OnRender(DrawingContext dc)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Point Point(int x, int y) => new((x - Bounds.X) / dpi.DpiScaleX, (y - Bounds.Y) / dpi.DpiScaleY);
            Pen Pen(string color, double width) => new(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), width / dpi.DpiScaleX);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            foreach (var line in Geometry.Dividers)
                dc.DrawLine(Pen(Active.Contains(line.NodeId) ? GuideStyle.Resize : GuideStyle.Divider, GuideStyle.Thickness * (Active.Contains(line.NodeId) ? 2 : 1)), Point(line.X1, line.Y1), Point(line.X2, line.Y2));
            if (Geometry.Focus is { } focus)
            {
                var rect = new Rect(Point(focus.X, focus.Y), Point(focus.X + focus.Width, focus.Y + focus.Height));
                rect.Inflate(-GuideStyle.Thickness / dpi.DpiScaleX / 2, -GuideStyle.Thickness / dpi.DpiScaleY / 2);
                dc.DrawRectangle(null, Pen(GuideStyle.Focus, GuideStyle.Thickness), rect);
            }
            dc.Pop();
        }
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}
