using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Controls.Primitives;

namespace DeskMux.App.UI;

internal sealed class ManagerWindow : ThemedWindow
{
    private readonly AppController _controller;
    private readonly ContentControl _content = new();
    private readonly TextBlock _status;
    private readonly TextBlock _prefixHint;
    private readonly Dictionary<string, Button> _navigation = [];
    private ListBox? _sessionList;
    private ContentControl? _detail;
    private ListBox? _windowList;
    private Guid? _selection;
    private string _page = "Sessions";
    private bool _refreshing;

    public ManagerWindow(AppController controller)
    {
        _controller = controller; Title = "DeskMux — " + AppInfo.BuildLabel; Width = 1080; Height = 720; MinWidth = 880; MinHeight = 580; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid { Background = UIHelpers.Brush("Background") }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel { Background = UIHelpers.Brush("Sidebar"), LastChildFill = true };
        var bottom = new StackPanel { Margin = new Thickness(18) }; bottom.Children.Add(UIHelpers.Text("Always within reach", 12, UIHelpers.Brush("SidebarMuted")));
        var prefix = _prefixHint = UIHelpers.Text(controller.PrefixLabel + "  →  " + HotkeySettingsUi.Shortcut(controller.Sessions.State.Settings,"Picker"), 19, UIHelpers.Brush("SidebarText"), true); bottom.Children.Add(prefix);
        var exit = UIHelpers.Button("Exit DeskMux", controller.Exit); exit.Background = Brushes.Transparent; exit.Foreground = UIHelpers.Brush("SidebarText"); bottom.Children.Add(exit); DockPanel.SetDock(bottom, Dock.Bottom); sidebar.Children.Add(bottom);
        var nav = new StackPanel { Margin = new Thickness(18, 28, 10, 0) }; nav.Children.Add(UIHelpers.Text("▦  DeskMux", 24, UIHelpers.Brush("SidebarText"), true));
        var tag = UIHelpers.Text("YOUR APPS. YOUR CONTEXT.", 9, UIHelpers.Brush("SidebarMuted")); tag.Margin = new Thickness(0, 0, 0, 35); nav.Children.Add(tag);
        foreach (var name in new[] { "Sessions", "Launchers", "Hotkeys", "Behavior", "Appearance", "About" })
        {
            var button = UIHelpers.Button(name, () => Navigate(name)); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent; button.Foreground = UIHelpers.Brush("SidebarText"); _navigation[name] = button; nav.Children.Add(button);
        }
        sidebar.Children.Add(nav); root.Children.Add(sidebar);
        var body = new DockPanel { Margin = new Thickness(30, 28, 30, 20) }; Grid.SetColumn(body, 1);
        var footer = new Border { BorderBrush = UIHelpers.Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Margin = new Thickness(0, 14, 0, 0) };
        _status = UIHelpers.Text("", 12, UIHelpers.Muted); _status.Margin = new Thickness(0); footer.Child = _status; DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer); body.Children.Add(_content); root.Children.Add(body); Content = root;
        Navigate("Sessions");
    }
    public void Navigate(string page)
    {
        _page = page;
        foreach (var (name, button) in _navigation) button.Background = name == page ? UIHelpers.Brush("SidebarSelection") : Brushes.Transparent;
        _content.Content = page switch { "Launchers" => BuildLaunchers(), "Hotkeys" => BuildHotkeys(), "Behavior" => BuildBehavior(), "Appearance" => BuildAppearance(), "About" => BuildAbout(), _ => BuildSessions() };
        RefreshStatus();
    }
    public void Refresh()
    {
        RefreshStatus();
        if (_page != "Sessions" || _sessionList == null) return;
        var selectedWindow = (_windowList?.SelectedItem as ManagedWindow)?.Id;
        _refreshing = true; _sessionList.Items.Clear();
        foreach (var (session, i) in _controller.Sessions.State.Sessions.Select((s, i) => (s, i)))
        {
            var panel = new StackPanel();
            panel.Children.Add(UIHelpers.Text($"{i + 1}   {session.Name}", 16, bold: true));
            var suffix = session.Id == _controller.Sessions.State.ActiveSessionId ? "  ·  Active" : "";
            panel.Children.Add(UIHelpers.Text($"{session.Windows.Count(w => !w.IsMissing)} windows{suffix}", 12, UIHelpers.Muted));
            var item = new ListBoxItem { Content = panel, Tag = session.Id }; _sessionList.Items.Add(item); if (session.Id == _selection) _sessionList.SelectedItem = item;
        }
        if (_sessionList.SelectedItem == null && _sessionList.Items.Count > 0)
        {
            _sessionList.SelectedItem = _sessionList.Items.Cast<ListBoxItem>().FirstOrDefault(i => (Guid)i.Tag == _controller.Sessions.State.ActiveSessionId) ?? _sessionList.Items[0];
            _selection = (Guid)((ListBoxItem)_sessionList.SelectedItem).Tag;
        }
        _refreshing = false; BuildDetail();
        if (_windowList != null && selectedWindow != null) _windowList.SelectedItem = _windowList.Items.Cast<ManagedWindow>().FirstOrDefault(w => w.Id == selectedWindow);
    }
    public void SelectSession(Guid id) { _selection = id; if (_page == "Sessions") Refresh(); }
    public void RefreshStatus()
    {
        _prefixHint.Text = _controller.PrefixLabel + "  →  " + HotkeySettingsUi.Shortcut(_controller.Sessions.State.Settings,"Picker");
        var sessions = _controller.Sessions;
        _status.Text = sessions.LastError ?? (sessions.HidingPaused ? "●  Session hiding paused — all managed windows are accessible." : "●  " + (sessions.ActiveSession?.Name ?? "Detached") + "   ·   " + _controller.PrefixLabel + " for commands   ·   Closing this window keeps DeskMux in the tray.");
    }
    private UIElement BuildSessions()
    {
        var panel = new DockPanel();
        var header = new StackPanel(); header.Children.Add(UIHelpers.Text("Sessions", 30, bold: true)); header.Children.Add(UIHelpers.Text("Keep your applications running. Switch the context around them.", color: UIHelpers.Muted));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 14) }; actions.Children.Add(UIHelpers.Button("+  New session", () => _controller.CreateSession(), true)); actions.Children.Add(UIHelpers.Button("Capture current desktop", () => _controller.CreateSession(true))); actions.Children.Add(UIHelpers.Button("Show all windows", _controller.Emergency)); actions.Children.Add(UIHelpers.Button("Resume sessions", _controller.Resume)); header.Children.Add(actions); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        _sessionList = new ListBox(); _sessionList.SelectionChanged += (_, _) => { if (_refreshing) return; _selection = (_sessionList.SelectedItem as ListBoxItem)?.Tag as Guid?; BuildDetail(); };
        grid.Children.Add(_sessionList); _detail = new ContentControl(); Grid.SetColumn(_detail, 2); grid.Children.Add(_detail); panel.Children.Add(grid); Refresh(); return panel;
    }
    private void BuildDetail()
    {
        if (_detail == null) return;
        var session = _controller.Sessions.State.Sessions.FirstOrDefault(s => s.Id == _selection);
        if (session == null) { _detail.Content = UIHelpers.Text("Create your first session, then add the application windows you want to keep together.", color: UIHelpers.Muted); return; }
        var panel = new DockPanel(); var top = new StackPanel(); top.Children.Add(UIHelpers.Text(session.Name, 25, bold: true));
        var controls = new WrapPanel(); controls.Children.Add(UIHelpers.Button("Switch here", () => _controller.Switch(session.Id), true)); controls.Children.Add(UIHelpers.Button("Restore apps", () => _controller.RestoreSession(session.Id))); controls.Children.Add(UIHelpers.Button("Rename", () => _controller.Rename(session.Id))); controls.Children.Add(UIHelpers.Button("↑", () => _controller.Sessions.ReorderSession(session.Id, -1))); controls.Children.Add(UIHelpers.Button("↓", () => _controller.Sessions.ReorderSession(session.Id, 1))); controls.Children.Add(UIHelpers.Button("Delete", () => _controller.Delete(session.Id))); top.Children.Add(controls);
        top.Children.Add(new Expander{Header="Saved layouts",Content=LayoutPresetsUi.Build(_controller,session.Id)});
        top.Children.Add(UIHelpers.Text("WINDOWS", 11, UIHelpers.Muted, true)); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; var actions = new WrapPanel(); actions.Children.Add(UIHelpers.Button("+  Add windows", () => _controller.CaptureWindows(session.Id)));
        var move = UIHelpers.Button("Move to…", () => { if (_windowList?.SelectedItem is ManagedWindow window) _controller.PickSession(true, window.Handle, window.Id); });
        var remove = UIHelpers.Button("Remove from session", () => { if (_windowList?.SelectedItem is ManagedWindow window) { _controller.Sessions.RemoveWindow(window.Id); _controller.ReportError(); } });
        var inspect = UIHelpers.Button("Details", () => { if (_windowList?.SelectedItem is ManagedWindow window) ShowDetails(window); });
        var focus = UIHelpers.Button("Focus pane", () => { if (_windowList?.SelectedItem is ManagedWindow window) { _controller.Sessions.FocusPane(window.Id); _controller.ReportError(); } });
        var release = UIHelpers.Button("Release to floating", () => { if (_windowList?.SelectedItem is ManagedWindow window) { _controller.Sessions.ReleasePane(window.Id); _controller.ReportError(); } });
        move.IsEnabled = remove.IsEnabled = inspect.IsEnabled = focus.IsEnabled = release.IsEnabled = false;
        actions.Children.Add(focus); actions.Children.Add(release); actions.Children.Add(move); actions.Children.Add(remove); actions.Children.Add(inspect); bottom.Children.Add(actions);
        var paneActions = new WrapPanel();
        var splitRight = UIHelpers.Button("Split right", () => _controller.OpenPane(PaneOrientation.Vertical, (_windowList?.SelectedItem as ManagedWindow)?.Handle ?? 0));
        var splitBelow = UIHelpers.Button("Split below", () => _controller.OpenPane(PaneOrientation.Horizontal, (_windowList?.SelectedItem as ManagedWindow)?.Handle ?? 0));
        var reapply = UIHelpers.Button("Reapply layout", () => { _controller.Sessions.ReapplyPaneLayouts(); _controller.ReportError(); });
        splitRight.IsEnabled = splitBelow.IsEnabled = reapply.IsEnabled = session.Id == _controller.Sessions.State.ActiveSessionId;
        paneActions.Children.Add(splitRight); paneActions.Children.Add(splitBelow); paneActions.Children.Add(reapply); paneActions.Children.Add(UIHelpers.Button("Undo layout",()=>{_controller.Sessions.UndoPaneLayout();_controller.ReportError();})); paneActions.Children.Add(UIHelpers.Button("Edit launchers", () => Navigate("Launchers"))); bottom.Children.Add(paneActions);
        bottom.Children.Add(UIHelpers.Text("Split right or below divides only the focused pane. Resize an app edge to adjust its shared divider on release. Panes stay snapped together. Drag a title bar onto another pane to swap; hold Alt when starting a drag to float. Release to floating also enables free placement. Undo restores the previous layout.", 12, UIHelpers.Muted)); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        _windowList = new ListBox { ItemsSource = session.Windows };
        var row = new FrameworkElementFactory(typeof(StackPanel)); var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(ManagedWindow.DisplayTitle))); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); row.AppendChild(title);
        var subtitle = new FrameworkElementFactory(typeof(TextBlock)); subtitle.SetBinding(TextBlock.TextProperty, new Binding { Converter = new WindowStatusConverter(_controller.Sessions) }); subtitle.SetValue(TextBlock.FontSizeProperty, 12.0); subtitle.SetValue(TextBlock.ForegroundProperty, UIHelpers.Muted); subtitle.SetValue(TextBlock.MarginProperty, new Thickness(0, 5, 0, 0)); subtitle.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(subtitle); _windowList.ItemTemplate = new DataTemplate { VisualTree = row };
        _windowList.SelectionChanged += (_, _) =>
        {
            var window = _windowList.SelectedItem as ManagedWindow;
            move.IsEnabled = remove.IsEnabled = inspect.IsEnabled = window != null;
            focus.IsEnabled = window != null && !window.IsMissing;
            release.IsEnabled = window != null && session.PaneCanvases.Any(c => PaneTree.FindLeaf(c, window.Id) != null);
        };
        if (session.Windows.Count == 0) panel.Children.Add(new Border { Background = UIHelpers.Brush("Surface"), Padding = new Thickness(24), Child = UIHelpers.Text("A place for your next context.\n\nAdd open windows, or focus an application and press " + _controller.PrefixLabel + " followed by " + HotkeySettingsUi.Shortcut(_controller.Sessions.State.Settings,"Add") + ".", color: UIHelpers.Muted) }); else panel.Children.Add(_windowList);
        _detail.Content = panel;
    }
    private void ShowDetails(ManagedWindow window)
    {
        var f = window.Fingerprint; var l = window.Layout;
        var text = $"{window.DisplayTitle}\n\nPane: {_controller.Sessions.PaneStatus(window.Id)}\n\nProcess: {f.ProcessName} ({f.ProcessId})\nPath: {f.ExecutablePath}\nWindow class: {f.WindowClass}\nHWND: 0x{window.Handle:X}\nMonitor: {l.MonitorDevice}\nPosition: {l.Bounds.X}, {l.Bounds.Y}\nSize: {l.Bounds.Width} × {l.Bounds.Height}\nState: {l.ShowState}\nLast focused: {window.LastFocusedUtc.ToLocalTime():g}\nStatus: {(window.IsMissing ? "Missing" : window.HiddenByDeskMux ? "Hidden by DeskMux" : "Available")}\n{window.Status}";
        var details = new Window { Title = "Window details", Width = 620, Height = 450, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = UIHelpers.Brush("Background"), Foreground = UIHelpers.Brush("Ink"),
            Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) } }; details.ShowDialog();
    }
    private UIElement BuildLaunchers()
    {
        var panel = new DockPanel();
        var header = new StackPanel(); header.Children.Add(UIHelpers.Text("Launchers", 30, bold: true));
        header.Children.Add(UIHelpers.Text("Applications available in the Open pane picker. Each launcher can provide arguments and a working directory.", color: UIHelpers.Muted));
        header.Children.Add(UIHelpers.Text("Select an executable to add. DeskMux waits for its window before changing the pane layout.", 13, UIHelpers.Muted));
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var profiles = _controller.Sessions.State.Settings.LaunchProfiles;
        var list = new ListBox();
        foreach (var profile in profiles)
        {
            var row = new StackPanel(); row.Children.Add(UIHelpers.Text(profile.Name, 16, bold: true));
            row.Children.Add(UIHelpers.Text(profile.Target + (string.IsNullOrWhiteSpace(profile.Arguments) ? "" : "  " + profile.Arguments), 12, UIHelpers.Muted));
            if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory)) row.Children.Add(UIHelpers.Text("Working directory: " + profile.WorkingDirectory, 12, UIHelpers.Muted));
            list.Items.Add(new ListBoxItem { Content = row, Tag = profile });
        }
        AppLaunchProfile? Selected() => (list.SelectedItem as ListBoxItem)?.Tag as AppLaunchProfile;
        void Reorder(int delta)
        {
            if (Selected() is not { } profile) return;
            var index = profiles.IndexOf(profile); var next = index + delta;
            if (next < 0 || next >= profiles.Count) return;
            profiles.RemoveAt(index); profiles.Insert(next, profile); _controller.SettingsChanged(); Navigate("Launchers");
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(UIHelpers.Button("+  Add executable…", () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Add application launcher", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) _controller.EditLauncher(executable: picker.FileName);
        }, true));
        var edit = UIHelpers.Button("Edit", () => { if (Selected() is { } profile) _controller.EditLauncher(profile); });
        var up = UIHelpers.Button("↑", () => Reorder(-1)); var down = UIHelpers.Button("↓", () => Reorder(1));
        var remove = UIHelpers.Button("Remove", () => { if (Selected() is { } profile) { profiles.Remove(profile); _controller.SettingsChanged(); Navigate("Launchers"); } });
        edit.IsEnabled = up.IsEnabled = down.IsEnabled = remove.IsEnabled = false;
        list.SelectionChanged += (_, _) =>
        {
            var index = Selected() is { } profile ? profiles.IndexOf(profile) : -1;
            edit.IsEnabled = remove.IsEnabled = index >= 0;
            up.IsEnabled = index > 0; down.IsEnabled = index >= 0 && index < profiles.Count - 1;
        };
        list.MouseDoubleClick += (_, _) => { if (Selected() is { } profile) _controller.EditLauncher(profile); };
        actions.Children.Add(edit); actions.Children.Add(up); actions.Children.Add(down); actions.Children.Add(remove);
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        if (profiles.Count == 0) panel.Children.Add(new Border { Background = UIHelpers.Brush("Surface"), Padding = new Thickness(24), Child = UIHelpers.Text("No launchers yet. Add an executable, then open a pane with " + _controller.PrefixLabel + " followed by " + HotkeySettingsUi.Shortcut(_controller.Sessions.State.Settings,"SplitRight") + " or " + HotkeySettingsUi.Shortcut(_controller.Sessions.State.Settings,"SplitBelow") + ".", color: UIHelpers.Muted) });
        else panel.Children.Add(list);
        return panel;
    }
    private UIElement BuildHotkeys() => HotkeySettingsUi.Build(_controller);
    private UIElement BuildAppearance()
    {
        var panel = new StackPanel();
        panel.Children.Add(UIHelpers.Text("Appearance", 30, bold: true));
        panel.Children.Add(UIHelpers.Text("Choose a terminal-inspired preset or build your own palette. Themes apply to the manager, command overlay, pickers, and dialogs.", color: UIHelpers.Muted));
        panel.Children.Add(UIHelpers.Text("THEME", 11, UIHelpers.Muted, true));
        var settings = _controller.Sessions.State.Settings.Theme;
        var presets = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = ThemeManager.PresetNames };
        presets.SelectedIndex = Math.Max(0, Array.FindIndex(ThemeManager.PresetNames, n => string.Equals(n, settings.Preset, StringComparison.OrdinalIgnoreCase)));
        panel.Children.Add(presets);

        var preview = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 18) };
        panel.Children.Add(preview);
        void ShowPreview(ThemePalette palette)
        {
            preview.Children.Clear();
            foreach (var item in new[] { ("Background",palette.Background), ("Surface",palette.Surface), ("Sidebar",palette.Sidebar), ("Text",palette.Text),
                ("Muted",palette.Muted), ("Accent",palette.Accent), ("Border",palette.Border), ("Error",palette.Danger) })
            {
                Brush color;
                try { color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Item2)!); } catch { color = UIHelpers.Brush("Danger"); }
                var label = UIHelpers.Text(item.Item1 + "\n" + item.Item2, 11, ThemeManager.ContrastBrush(item.Item2), true); label.Margin = new Thickness(8);
                preview.Children.Add(new Border { Background = color, BorderBrush = UIHelpers.Brush("Border"), BorderThickness = new Thickness(1), Margin = new Thickness(0,0,8,8), MinHeight = 62, Child = label });
            }
        }
        ThemePalette SelectedPalette() => ThemeManager.Resolve(new ThemeSettings { Preset = presets.SelectedItem as string ?? "System", Custom = settings.Custom });
        ShowPreview(SelectedPalette());
        presets.SelectionChanged += (_, _) => ShowPreview(SelectedPalette());
        panel.Children.Add(UIHelpers.Button("Apply selected theme", () =>
        {
            var selected = presets.SelectedItem as string ?? "System";
            if (selected == "Custom") _controller.ApplyTheme("Custom", settings.Custom); else _controller.ApplyTheme(selected);
            ShowPreview(ThemeManager.Resolve(_controller.Sessions.State.Settings.Theme));
        }, true));

        panel.Children.Add(UIHelpers.Text("CUSTOM COLORS", 11, UIHelpers.Muted, true));
        panel.Children.Add(UIHelpers.Text("Enter #RRGGBB colors. Save and apply stores this palette locally as Custom.", 13, UIHelpers.Muted));
        var custom = ThemeManager.Clone(settings.Custom);
        var fields = new Dictionary<string, TextBox>();
        var fieldGrid = new UniformGrid { Columns = 2 };
        void Field(string name, string value)
        {
            var group = new StackPanel { Margin = new Thickness(0,0,12,2) };
            group.Children.Add(UIHelpers.Text(name.ToUpperInvariant(), 11, UIHelpers.Muted, true));
            var input = new TextBox { Text = value }; fields[name] = input; group.Children.Add(input); fieldGrid.Children.Add(group);
        }
        Field("Background",custom.Background); Field("Surface",custom.Surface); Field("Sidebar",custom.Sidebar); Field("Text",custom.Text);
        Field("Muted",custom.Muted); Field("Accent",custom.Accent); Field("Border",custom.Border); Field("Error",custom.Danger);
        panel.Children.Add(fieldGrid);
        ThemePalette ReadCustom() => new() { Background=fields["Background"].Text, Surface=fields["Surface"].Text, Sidebar=fields["Sidebar"].Text,
            Text=fields["Text"].Text, Muted=fields["Muted"].Text, Accent=fields["Accent"].Text, Border=fields["Border"].Text, Danger=fields["Error"].Text };
        var error = UIHelpers.Text("",12,UIHelpers.Brush("Danger")); panel.Children.Add(error);
        foreach (var input in fields.Values) input.TextChanged += (_, _) => { if (ThemeManager.TryNormalize(ReadCustom(),out var palette,out _)) ShowPreview(palette); };
        var actions = new WrapPanel();
        actions.Children.Add(UIHelpers.Button("Save and apply custom", () =>
        {
            if (!ThemeManager.TryNormalize(ReadCustom(),out var palette,out var message)) { error.Text=message; return; }
            error.Text=""; presets.SelectedItem="Custom"; _controller.ApplyTheme("Custom",palette); ShowPreview(palette);
        }, true));
        actions.Children.Add(UIHelpers.Button("Copy selected preset into custom", () =>
        {
            var palette=SelectedPalette(); fields["Background"].Text=palette.Background; fields["Surface"].Text=palette.Surface; fields["Sidebar"].Text=palette.Sidebar;
            fields["Text"].Text=palette.Text; fields["Muted"].Text=palette.Muted; fields["Accent"].Text=palette.Accent; fields["Border"].Text=palette.Border; fields["Error"].Text=palette.Danger;
        }));
        panel.Children.Add(actions);
        panel.Children.Add(GuideSettingsUi.Appearance(_controller));
        return new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    }
    private UIElement BuildBehavior()
    {
        var panel = new StackPanel(); panel.Children.Add(UIHelpers.Text("Behavior", 30, bold: true)); panel.Children.Add(UIHelpers.Text("DeskMux manages windows conservatively and keeps your applications running.", color: UIHelpers.Muted));
        panel.Children.Add(GuideSettingsUi.Behavior(_controller));
        var reconnect=new CheckBox{Content="Restore pane layouts when monitors reconnect",IsChecked=_controller.Sessions.State.Settings.RestoreMonitorLayouts};
        reconnect.Click+=(_,_)=>{_controller.Sessions.State.Settings.RestoreMonitorLayouts=reconnect.IsChecked==true;_controller.SettingsChanged();};panel.Children.Add(reconnect);
        var startup = new CheckBox { Content = "Open session manager when DeskMux starts", IsChecked = _controller.Sessions.State.Settings.ShowManagerOnStartup }; startup.Click += (_, _) => { _controller.Sessions.State.Settings.ShowManagerOnStartup = startup.IsChecked == true; _controller.SettingsChanged(); }; panel.Children.Add(startup);
        var restoreMinimized = new CheckBox { Content = "Restore minimized windows when switching sessions", IsChecked = _controller.Sessions.State.Settings.RestoreMinimizedOnSessionSwitch };
        restoreMinimized.Click += (_, _) => { _controller.Sessions.State.Settings.RestoreMinimizedOnSessionSwitch = restoreMinimized.IsChecked == true; _controller.SettingsChanged(); };
        panel.Children.Add(restoreMinimized);
        panel.Children.Add(UIHelpers.Text("When enabled, selecting a session brings its minimized windows back to their normal or maximized state. Applies on your next session selection.", 13, UIHelpers.Muted));
        var restoreApps = new CheckBox { Content = "Restore missing applications in the active session when DeskMux starts", IsChecked = _controller.Sessions.State.Settings.RestoreActiveSessionOnStartup };
        restoreApps.Click += (_, _) => { _controller.Sessions.State.Settings.RestoreActiveSessionOnStartup = restoreApps.IsChecked == true; _controller.SettingsChanged(); };
        panel.Children.Add(restoreApps);
        panel.Children.Add(UIHelpers.Text("DeskMux reconnects matching windows first, then opens missing apps from their saved launcher or executable path. Disabled by default.", 13, UIHelpers.Muted));
        panel.Children.Add(UIHelpers.Text("EXCLUDED PROCESSES", 11, UIHelpers.Muted, true)); panel.Children.Add(UIHelpers.Text("One process name per line, with or without .exe. System and DeskMux windows are always protected. File Explorer windows can be managed; its desktop and taskbar cannot.", 13, UIHelpers.Muted));
        var excluded = new TextBox { Text = string.Join(Environment.NewLine, _controller.Sessions.State.Settings.ExcludedProcesses), AcceptsReturn = true, Height = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(excluded); panel.Children.Add(UIHelpers.Button("Save exclusions", () =>
        {
            _controller.Sessions.State.Settings.ExcludedProcesses = excluded.Text.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => Path.GetFileNameWithoutExtension(s)).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Release windows newly excluded before persisting the new preference.
            foreach (var window in _controller.Sessions.State.Sessions.SelectMany(s => s.Windows).Where(w => _controller.Sessions.State.Settings.ExcludedProcesses.Contains(w.Fingerprint.ProcessName, StringComparer.OrdinalIgnoreCase)).ToList()) _controller.Sessions.RemoveWindow(window.Id);
            _controller.SettingsChanged(); _controller.ReportError();
        }, true));
        panel.Children.Add(UIHelpers.Text("RECOVERY & LOCAL DATA", 11, UIHelpers.Muted, true)); panel.Children.Add(UIHelpers.Text("Show all restores managed windows and pauses session hiding. An independent recovery helper watches DeskMux and restores journaled windows after an unexpected exit.", 13, UIHelpers.Muted));
        var actions = new WrapPanel(); actions.Children.Add(UIHelpers.Button("Show all managed windows", _controller.Emergency)); actions.Children.Add(UIHelpers.Button("Resume sessions", _controller.Resume)); actions.Children.Add(UIHelpers.Button("Open logs", () => _controller.OpenDirectory(_controller.LogDirectory))); actions.Children.Add(UIHelpers.Button("Open data folder", () => _controller.OpenDirectory(_controller.DataDirectory))); panel.Children.Add(actions);
        panel.Children.Add(UIHelpers.Text("Windows that run as administrator may reject management. DeskMux reports those failures and does not require elevation. Missing or ambiguous windows remain unassigned until you select them again.", 13, UIHelpers.Muted)); return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private UIElement BuildAbout()
    {
        var panel = new StackPanel(); panel.Children.Add(UIHelpers.Text("DeskMux", 38, bold: true)); panel.Children.Add(UIHelpers.Text("Sessions and panes for Windows applications.", 20)); panel.Children.Add(UIHelpers.Text("Version " + AppInfo.BuildLabel + "  ·  Windows x64  ·  .NET 10 / WPF", 13, UIHelpers.Muted));
        panel.Children.Add(UIHelpers.Text("Group native application windows into sessions. Arrange them into nested panes, or leave them floating. Split, navigate, resize, swap, and zoom with a keyboard prefix. Applications keep running.", 16, UIHelpers.Muted));
        panel.Children.Add(UIHelpers.Text("GET STARTED", 11, UIHelpers.Muted, true)); panel.Children.Add(UIHelpers.Text("1. Create a session for a context such as DEV or IMAGES.\n2. Use Add windows to choose its open applications.\n3. Press " + _controller.PrefixLabel + ", then a session number.\n4. Press " + _controller.PrefixLabel + ", then " + HotkeySettingsUi.Shortcut(_controller.Sessions.State.Settings,"Picker") + " to browse sessions.", 16));
        panel.Children.Add(UIHelpers.Text("Your sessions and logs are stored locally. DeskMux has no account, cloud service, application embedding, or virtual desktop dependency.", 14, UIHelpers.Muted));
        panel.Children.Add(UIHelpers.Text("Closing this manager leaves DeskMux in your tray. Use Exit DeskMux to restore all managed windows and quit.", 14, bold: true));
        var actions = new WrapPanel();
        actions.Children.Add(UIHelpers.Button("Install downloaded update", () => UpdateInstaller.Choose(_controller)));
        panel.Children.Add(UIHelpers.Text("Running from " + AppContext.BaseDirectory + "\nData: " + _controller.DataDirectory,12,UIHelpers.Muted));
        actions.Children.Add(UIHelpers.Button("Check for updates", () => _ = _controller.CheckForUpdatesAsync(), true));
        actions.Children.Add(UIHelpers.Button("Website", () => _controller.OpenUrl(AppInfo.WebsiteUrl)));
        actions.Children.Add(UIHelpers.Button("GitHub", () => _controller.OpenUrl(AppInfo.RepositoryUrl)));
        actions.Children.Add(UIHelpers.Button("Open local data", () => _controller.OpenDirectory(_controller.DataDirectory)));
        panel.Children.Add(actions); return panel;
    }
    private sealed class WindowStatusConverter(SessionManager sessions) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not ManagedWindow window) return "";
            return (sessions.IsFocused(window) ? "● Focused  ·  " : "") + window.Fingerprint.ProcessName + "  ·  " + (window.IsMissing ? "Missing" : window.HiddenByDeskMux ? "Hidden" : window.Layout.ShowState.ToString()) + "  ·  " + (sessions.PaneStatus(window.Id) == "Floating" ? "Floating · free to move" : sessions.PaneStatus(window.Id)) + (window.Status.Length > 0 ? "  ·  " + window.Status : "");
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
