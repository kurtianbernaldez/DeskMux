using System.Windows.Input;

namespace DeskMux.App.UI;

internal sealed record PaneChoice(WindowSnapshot? Window, AppLaunchProfile? Launcher, string Status)
{
    public string Name => Launcher?.Name ?? Window?.Fingerprint.ProcessName ?? "Application";
    public string Description => Launcher?.Target ?? Window?.Fingerprint.Title ?? "";
}

/// <summary>A non-activating picker. Every running HWND has its own explicit choice.</summary>
internal sealed class PanePicker : Window, IKeyboardPicker
{
    private readonly ListBox _list = new() { MaxHeight = 480, BorderThickness = new Thickness(0) };
    private readonly List<ListBoxItem> _choices = [];
    public PaneChoice? SelectedChoice { get; private set; }
    public bool CancelledByKeyboard { get; private set; }

    public PanePicker(IEnumerable<PaneChoice> windows, IEnumerable<PaneChoice> launchers, string title = "Open pane", bool candidatesOnly = false)
    {
        Title = "DeskMux — " + title; Width = 690; MaxHeight = 690; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        var panel = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel(); header.Children.Add(UIHelpers.Text(title, 24, bold: true));
        header.Children.Add(UIHelpers.Text(candidatesOnly ? "Several application windows matched. Choose the window to use." : "Choose an open window or launch an application. Escape leaves the layout unchanged.", 13, UIHelpers.Muted));
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var hint = UIHelpers.Text("↑/K  ↓/J     Enter  Choose     1–9  Quick selection     Esc  Cancel", 12, UIHelpers.Muted);
        hint.Margin = new Thickness(0, 16, 0, 0); DockPanel.SetDock(hint, Dock.Bottom); panel.Children.Add(hint);
        AddSection("RUNNING WINDOWS", windows, "No eligible application windows are available.");
        if (!candidatesOnly) AddSection("LAUNCH NEW", launchers, "Add application launchers on Manager → Launchers.");
        if (_choices.Count > 0) _list.SelectedItem = _choices[0];
        panel.Children.Add(_list);
        Content = new Border { BorderThickness = new Thickness(1), BorderBrush = UIHelpers.Brush("Border"), Child = panel };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_list, source) is ListBoxItem { Tag: PaneChoice } item)
            { _list.SelectedItem = item; Choose(); }
        };
        PreviewKeyDown += (_, e) =>
        {
            var key = KeyInterop.VirtualKeyFromKey(e.Key);
            if (key is >= 0x61 and <= 0x69) key -= 0x30;
            e.Handled = HandleKey(key);
        };
    }

    private void AddSection(string name, IEnumerable<PaneChoice> choices, string empty)
    {
        _list.Items.Add(new ListBoxItem { IsEnabled = false, Focusable = false, Content = UIHelpers.Text(name, 11, UIHelpers.Muted, true) });
        var count = _choices.Count;
        foreach (var choice in choices)
        {
            var row = new StackPanel();
            var number = _choices.Count < 9 ? $"{_choices.Count + 1}   " : "";
            row.Children.Add(UIHelpers.Text(number + choice.Name + "  ·  " + choice.Status, 14, bold: true));
            var title = UIHelpers.Text(choice.Description, 13, UIHelpers.Muted);
            title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.ToolTip = choice.Description; row.Children.Add(title);
            var item = new ListBoxItem { Content = row, Tag = choice, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            _choices.Add(item); _list.Items.Add(item);
        }
        if (_choices.Count == count) _list.Items.Add(new ListBoxItem { IsEnabled = false, Focusable = false, Content = UIHelpers.Text(empty, 13, UIHelpers.Muted) });
    }

    public bool HandleKey(int key)
    {
        if (!IsVisible) return false;
        if (key is 0 or 0x1B) { CancelledByKeyboard = key == 0x1B; Close(); return true; }
        if (key == 0x0D) { Choose(); return true; }
        if (key is 0x26 or 0x28 or 0x4A or 0x4B)
        {
            if (_choices.Count > 0)
            {
                var index = _list.SelectedItem is ListBoxItem item ? _choices.IndexOf(item) : 0;
                index = (index + (key is 0x26 or 0x4B ? -1 : 1) + _choices.Count) % _choices.Count;
                _list.SelectedItem = _choices[index]; _list.ScrollIntoView(_list.SelectedItem);
            }
            return true;
        }
        if (key is >= 0x31 and <= 0x39 && key - 0x31 < _choices.Count)
        { _list.SelectedItem = _choices[key - 0x31]; Choose(); return true; }
        return false;
    }

    private void Choose()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: PaneChoice choice }) { SelectedChoice = choice; Close(); }
    }
}

internal sealed class LaunchProgressWindow : Window, IKeyboardPicker
{
    public LaunchProgressWindow(string name, bool restoring = false)
    {
        Title = restoring ? "DeskMux — Restoring session" : "DeskMux — Opening pane"; Width = 460; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(UIHelpers.Text((restoring ? "Restoring " : "Opening ") + name + "…", 23, bold: true));
        panel.Children.Add(UIHelpers.Text("Waiting for an application window, up to 15 seconds. You can keep using your applications.", 14, UIHelpers.Muted));
        panel.Children.Add(UIHelpers.Button(restoring ? "Cancel session restore" : "Cancel pane request", Close));
        panel.Children.Add(UIHelpers.Text(restoring ? "Apps already opened remain running. You can retry the missing ones later." : "Cancelling leaves the application running and your layout unchanged.", 12, UIHelpers.Muted));
        Content = new Border { BorderThickness = new Thickness(1), BorderBrush = UIHelpers.Brush("Border"), Child = panel };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    public bool HandleKey(int key) { if (key is 0 or 0x1B) { Close(); return true; } return false; }
}
