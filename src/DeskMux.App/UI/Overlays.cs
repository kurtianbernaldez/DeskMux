using System.Windows.Input;

namespace DeskMux.App.UI;

internal sealed class PrefixOverlay : ThemedWindow
{
    public PrefixOverlay(AppController controller)
    {
        Title = "DeskMux command mode"; Width = 640; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        var panel = new StackPanel { Margin = new Thickness(22, 17, 22, 17) };
        panel.Children.Add(UIHelpers.Text("DeskMux   /   " + controller.PrefixLabel, 14, bold: true));
        var sessions = new StackPanel();
        foreach (var item in controller.Sessions.State.Sessions.Select((s, i) => (s, i)))
        {
            var apps = CompactApps(item.s);
            var text = UIHelpers.Text($"{item.i + 1}  {item.s.Name}   ·   {apps}", 14,
                item.s.Id == controller.Sessions.State.ActiveSessionId ? null : UIHelpers.Muted,
                bold: item.s.Id == controller.Sessions.State.ActiveSessionId);
            text.Margin = new Thickness(0, 0, 0, 5); sessions.Children.Add(text);
        }
        panel.Children.Add(sessions);
        var settings = controller.Sessions.State.Settings;
        var shortcuts = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        shortcuts.ColumnDefinitions.Add(new ColumnDefinition());
        shortcuts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        shortcuts.ColumnDefinitions.Add(new ColumnDefinition());
        var commands = new[] {
            ("Add", "Add window"),
            ("SplitRight", "Split right"), ("SplitBelow", "Split below"),
            ("Release", "Float pane"), ("Zoom", "Zoom / restore"),
            ("Undo", "Undo layout"), ("Picker", "Switch session"),
            ("Manager", "Open manager"), ("Cancel", "Cancel")
        };
        for (var i = 0; i < commands.Length; i++)
        {
            if (i % 2 == 0) shortcuts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (id, label) = commands[i];
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var key = UIHelpers.Text(HotkeySettingsUi.Shortcut(settings, id), 13, UIHelpers.Brush("Accent"), true);
            key.Margin = new Thickness(8, 5, 8, 5);
            var badge = new Border { Background = UIHelpers.Brush("Surface"), CornerRadius = new CornerRadius(4),
                BorderBrush = UIHelpers.Brush("Border"), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 107, Child = key };
            var description = UIHelpers.Text(label, 14);
            description.Margin = new Thickness(0); description.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(description, 1); row.Children.Add(badge); row.Children.Add(description);
            Grid.SetRow(row, i / 2); Grid.SetColumn(row, i % 2 * 2); shortcuts.Children.Add(row);
        }
        panel.Children.Add(shortcuts);
        Content = new Border { BorderThickness = new Thickness(1), BorderBrush = UIHelpers.Brush("Border"), Child = panel };
    }

    internal static string CompactApps(WorkspaceSession session)
    {
        if (session.Windows.Count == 0) return "empty";
        var groups = session.Windows.GroupBy(w => string.IsNullOrWhiteSpace(w.Fingerprint.ProcessName) ? "Application" : w.Fingerprint.ProcessName,
            StringComparer.OrdinalIgnoreCase).Select(g => g.Key + (g.Count() > 1 ? $" ×{g.Count()}" : "")).Take(4).ToList();
        if (session.Windows.Select(w => string.IsNullOrWhiteSpace(w.Fingerprint.ProcessName) ? "Application" : w.Fingerprint.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > groups.Count) groups.Add("…");
        return string.Join(", ", groups);
    }
}

internal interface IKeyboardPicker
{
    bool HandleKey(int key);
}

internal sealed class SessionPicker : ThemedWindow, IKeyboardPicker
{
    private readonly ListBox _list;
    public Guid? SelectedSessionId { get; private set; }
    public bool CancelledByKeyboard { get; private set; }
    public SessionPicker(WorkspaceState state, string title)
    {
        Title = "DeskMux — " + title; Width = 640; MaxHeight = 760; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        var panel = new DockPanel { Margin = new Thickness(20) };
        var heading = UIHelpers.Text(title, 23, bold: true); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var hint = UIHelpers.Text("↑/K  ↓/J     Enter  Select     1–9  Jump     Esc  Close", 12, UIHelpers.Muted); hint.Margin = new Thickness(0, 16, 0, 0); DockPanel.SetDock(hint, Dock.Bottom); panel.Children.Add(hint);
        _list = new ListBox { MaxHeight = 570, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var item in state.Sessions.Select((s, i) => (s, i)))
        {
            var row = new StackPanel { Margin = new Thickness(4, 5, 4, 7) };
            var sessionHeading = new DockPanel();
            var count = UIHelpers.Text($"{item.s.Windows.Count} window{(item.s.Windows.Count == 1 ? "" : "s")}", 12, UIHelpers.Muted);
            count.Margin = new Thickness(12, 0, 0, 5); DockPanel.SetDock(count, Dock.Right); sessionHeading.Children.Add(count);
            sessionHeading.Children.Add(UIHelpers.Text($"{item.i + 1}   {item.s.Name}" + (item.s.Id == state.ActiveSessionId ? "   ● active" : ""), 16, bold: true));
            row.Children.Add(sessionHeading);
            if (item.s.Windows.Count == 0)
            {
                var empty = UIHelpers.Text("No applications", 12, UIHelpers.Muted); empty.Margin = new Thickness(27, 0, 0, 0); row.Children.Add(empty);
            }
            else
            {
                foreach (var window in item.s.Windows.Take(5))
                {
                    var isPane = item.s.PaneCanvases.Any(c => c.Nodes.Any(n => n.WindowId == window.Id));
                    var status = window.IsMissing ? "Missing" : isPane ? "Pane" : "Floating";
                    var app = string.IsNullOrWhiteSpace(window.Fingerprint.ProcessName) ? "Application" : window.Fingerprint.ProcessName;
                    var titleText = string.IsNullOrWhiteSpace(window.DisplayTitle) || string.Equals(window.DisplayTitle, app, StringComparison.OrdinalIgnoreCase)
                        ? "" : " — " + window.DisplayTitle;
                    var detail = UIHelpers.Text($"{(isPane ? "▦" : "◇")}  {app}{titleText}   ·   {status}", 12,
                        window.IsMissing ? UIHelpers.Brush("Danger") : UIHelpers.Muted);
                    detail.Margin = new Thickness(27, 0, 8, 3); detail.TextTrimming = TextTrimming.CharacterEllipsis;
                    detail.TextWrapping = TextWrapping.NoWrap; row.Children.Add(detail);
                }
                if (item.s.Windows.Count > 5)
                {
                    var more = UIHelpers.Text($"+ {item.s.Windows.Count - 5} more", 12, UIHelpers.Muted);
                    more.Margin = new Thickness(50, 0, 0, 2); row.Children.Add(more);
                }
            }
            _list.Items.Add(new ListBoxItem { Content = row, Tag = item.s.Id, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        _list.SelectedIndex = Math.Max(0, state.Sessions.FindIndex(s => s.Id == state.ActiveSessionId)); panel.Children.Add(_list);
        Content = new Border { BorderThickness = new Thickness(1), BorderBrush = UIHelpers.Brush("Border"), Child = panel };
        PreviewKeyDown += (_, e) =>
        {
            var key = KeyInterop.VirtualKeyFromKey(e.Key); if (key >= 0x61 && key <= 0x69) key -= 0x30;
            e.Handled = HandleKey(key);
        };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_list, source) is ListBoxItem { Tag: Guid } item)
            { _list.SelectedItem = item; Select(); }
        };
    }
    public bool HandleKey(int key)
    {
        if (!IsVisible) return false;
        if (key == 0) { Close(); return true; }
        if (key == 0x1B) { CancelledByKeyboard = true; Close(); return true; }
        if (key == 0x0D) { Select(); return true; }
        if (key is 0x26 or 0x28 or 0x4A or 0x4B)
        {
            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = (_list.SelectedIndex + (key is 0x26 or 0x4B ? -1 : 1) + _list.Items.Count) % _list.Items.Count;
                _list.ScrollIntoView(_list.SelectedItem);
            }
            return true;
        }
        if (key is >= 0x31 and <= 0x39 && key - 0x31 < _list.Items.Count)
        { _list.SelectedIndex = key - 0x31; Select(); return true; }
        return false;
    }
    private void Select() { if (_list.SelectedItem is ListBoxItem { Tag: Guid id }) { SelectedSessionId = id; Close(); } }
}
