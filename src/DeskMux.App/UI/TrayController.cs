using System.Drawing;

using Forms = System.Windows.Forms;

namespace DeskMux.App.UI;

internal sealed class TrayController : IDisposable
{
    private readonly AppController _controller;
    private readonly Forms.NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    public TrayController(AppController controller)
    {
        _controller = controller; _menu = new Forms.ContextMenuStrip(); _menu.Opening += (_, _) => Refresh();
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/DeskMux.ico")).Stream;
        _icon = new Icon(resource);
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "DeskMux", ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => controller.OpenManager(); Refresh();
    }
    public void Refresh()
    {
        while (_menu.Items.Count > 0) { var old = _menu.Items[0]; _menu.Items.RemoveAt(0); old.Dispose(); }
        var current = _controller.Sessions.ActiveSession?.Name ?? "Detached";
        _tray.Text = ("DeskMux — " + current)[..Math.Min(63, ("DeskMux — " + current).Length)];
        _menu.Items.Add(new Forms.ToolStripMenuItem("DeskMux") { Enabled = false });
        _menu.Items.Add(new Forms.ToolStripMenuItem("Current session: " + current) { Enabled = false });
        _menu.Items.Add(new Forms.ToolStripSeparator());
        var switcher = new Forms.ToolStripMenuItem("Switch session");
        foreach (var (session, i) in _controller.Sessions.State.Sessions.Select((s, i) => (s, i)))
        {
            var item = new Forms.ToolStripMenuItem($"{i + 1}  {session.Name}") { Checked = session.Id == _controller.Sessions.State.ActiveSessionId }; item.Click += (_, _) => _controller.Switch(session.Id); switcher.DropDownItems.Add(item);
        }
        _menu.Items.Add(switcher);
        Add("Create session", () => _controller.CreateSession()); Add("Capture current windows", () => _controller.CreateSession(true)); Add("Manage sessions", () => _controller.OpenManager()); Add("Settings", () => _controller.OpenManager("Behavior"));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        Add("Show all managed windows", _controller.Emergency);
        if (_controller.Sessions.HidingPaused) Add("Resume sessions", _controller.Resume);
        var pause = Add("Pause keyboard shortcuts", _controller.ToggleKeyboard); pause.Checked = _controller.Sessions.State.Settings.KeyboardPaused;
        _menu.Items.Add(new Forms.ToolStripSeparator()); Add("Exit DeskMux", _controller.Exit);
        if (_controller.UpdateVersion != null)
            Add(_controller.DownloadingUpdate ? "Downloading update…" : "Restart to update DeskMux", () => _ = _controller.InstallOnlineUpdateAsync()).Enabled = !_controller.DownloadingUpdate;
    }
    private Forms.ToolStripMenuItem Add(string text, Action action) { var item = new Forms.ToolStripMenuItem(text); item.Click += (_, _) => action(); _menu.Items.Add(item); return item; }
    public void Notify(string title, string message) { _tray.BalloonTipTitle = title; _tray.BalloonTipText = message.Length > 240 ? message[..240] : message; _tray.ShowBalloonTip(4500); }
    public void Dispose() { _tray.Visible = false; _tray.Dispose(); _menu.Dispose(); _icon.Dispose(); }

}
