using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Data;

namespace DeskMux.App.UI;

internal sealed class ConfirmMoveDialog : ThemedWindow
{
    public ConfirmMoveDialog(string window, string source, string target)
    {
        Title = "Move window to pane"; Width = 490; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(UIHelpers.Text("Move this window?", 23, bold: true));
        panel.Children.Add(UIHelpers.Text(window, 15, bold: true));
        panel.Children.Add(UIHelpers.Text($"Move from “{source}” to “{target}” and open it as a pane? Its old pane space will be filled by the remaining windows.", color: UIHelpers.Muted));
        var actions = new WrapPanel(); actions.Children.Add(UIHelpers.Button("Move and open pane", () => DialogResult = true, true));
        var cancel = UIHelpers.Button("Cancel", () => DialogResult = false); cancel.IsDefault = true; cancel.IsCancel = true; actions.Children.Add(cancel);
        panel.Children.Add(actions); Content = panel;
    }
}

internal sealed class LaunchProfileDialog : ThemedWindow
{
    public AppLaunchProfile Profile { get; private set; }
    public LaunchProfileDialog(AppLaunchProfile draft)
    {
        Profile = draft; Title = "Application launcher"; Width = 620; SizeToContent = SizeToContent.Height; MaxHeight = 760;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(UIHelpers.Text("Application launcher", 25, bold: true));
        panel.Children.Add(UIHelpers.Text("This application appears in the Open pane picker.", color: UIHelpers.Muted));
        TextBox Field(string label, string value)
        {
            panel.Children.Add(UIHelpers.Text(label, 12, UIHelpers.Muted, true));
            var input = new TextBox { Text = value }; panel.Children.Add(input); return input;
        }
        var name = Field("DISPLAY NAME", draft.Name);
        var target = Field("EXECUTABLE", draft.Target);
        panel.Children.Add(UIHelpers.Button("Browse executable…", () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Choose an application", Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
            if (picker.ShowDialog(this) == true) { target.Text = picker.FileName; if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileNameWithoutExtension(picker.FileName); }
        }));
        var arguments = Field("ARGUMENTS (OPTIONAL)", draft.Arguments);
        var workingDirectory = Field("WORKING DIRECTORY (OPTIONAL)", draft.WorkingDirectory);
        panel.Children.Add(UIHelpers.Button("Browse working directory…", () =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a working directory", Multiselect = false };
            if (picker.ShowDialog(this) == true) workingDirectory.Text = picker.FolderName;
        }));
        var expected = Field("EXPECTED PROCESS NAME (OPTIONAL)", draft.ExpectedProcessName);
        panel.Children.Add(UIHelpers.Text("Use the process name without .exe if the application opens its main window in a different process. Arguments are passed directly to the application.", 12, UIHelpers.Muted));
        var error = UIHelpers.Text("", 12, UIHelpers.Brush("Danger")); panel.Children.Add(error);
        var actions = new WrapPanel(); var save = UIHelpers.Button("Save launcher", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(target.Text)) { error.Text = "Provide a display name and executable."; return; }
            if (!Path.IsPathFullyQualified(target.Text.Trim()) || !File.Exists(target.Text.Trim()) || !string.Equals(Path.GetExtension(target.Text.Trim()), ".exe", StringComparison.OrdinalIgnoreCase))
            { error.Text = "Choose an existing executable file with Browse executable."; return; }
            if (!string.IsNullOrWhiteSpace(workingDirectory.Text) && !Directory.Exists(workingDirectory.Text.Trim())) { error.Text = "Choose an existing working directory, or leave it blank."; return; }
            Profile = new AppLaunchProfile { Id = draft.Id, Name = name.Text.Trim(), Target = target.Text.Trim(), Arguments = arguments.Text, WorkingDirectory = workingDirectory.Text.Trim(), ExpectedProcessName = Path.GetFileNameWithoutExtension(expected.Text.Trim()) };
            DialogResult = true;
        }, true);
        save.IsDefault = true; actions.Children.Add(save); var cancel = UIHelpers.Button("Cancel", () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        panel.Children.Add(actions); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}

internal sealed class NameDialog : ThemedWindow
{
    private readonly TextBox _input;
    public string Value => _input.Text.Trim();
    public NameDialog(string title, string description, string initial)
    {
        Title = title; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(UIHelpers.Text(title, 23, bold: true)); panel.Children.Add(UIHelpers.Text(description, color: UIHelpers.Muted));
        _input = new TextBox { Text = initial, MaxLength = 80 }; panel.Children.Add(_input);
        var actions = new WrapPanel(); var save = UIHelpers.Button("Save", () => { if (Value.Length > 0) DialogResult = true; }, true); save.IsDefault = true;
        actions.Children.Add(save); var cancel = UIHelpers.Button("Cancel", () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel); panel.Children.Add(actions); Content = panel;
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); }; _input.TextChanged += (_, _) => save.IsEnabled = Value.Length > 0;
    }
}

internal sealed class DeleteDialog : ThemedWindow
{
    private readonly RadioButton _move;
    public bool MoveToCurrent => _move.IsChecked == true;
    public DeleteDialog(string name, string? target)
    {
        Title = "Delete session"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(UIHelpers.Text("Delete “" + name + "”?", 23, bold: true));
        panel.Children.Add(UIHelpers.Text("The applications will keep running. Choose what happens to their windows.", color: UIHelpers.Muted));
        panel.Children.Add(new RadioButton { Content = "Show all windows and leave them unmanaged", IsChecked = true, Margin = new Thickness(0, 12, 0, 12), GroupName = "delete" });
        _move = new RadioButton { Content = target == null ? "Move to current session (no other active session)" : "Move windows to “" + target + "”", IsEnabled = target != null, Margin = new Thickness(0, 0, 0, 22), GroupName = "delete" }; panel.Children.Add(_move);
        var actions = new WrapPanel(); actions.Children.Add(UIHelpers.Button("Delete session", () => DialogResult = true)); var cancel = UIHelpers.Button("Cancel", () => DialogResult = false); cancel.IsCancel = true; cancel.IsDefault = true; actions.Children.Add(cancel); panel.Children.Add(actions); Content = panel;
    }
}

internal sealed class CaptureChoice(WindowSnapshot snapshot, string? session) : INotifyPropertyChanged
{
    private bool _selected;
    public WindowSnapshot Snapshot { get; } = snapshot;
    public string Title => Snapshot.Fingerprint.Title;
    public string Details => Snapshot.Fingerprint.ProcessName + (session == null ? "  ·  Unmanaged" : "  ·  Currently in " + session);
    public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class CaptureDialog : ThemedWindow
{
    public CaptureDialog(List<CaptureChoice> choices)
    {
        Title = "Capture current windows"; Width = 720; Height = 560; MinWidth = 480; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        var panel = new DockPanel { Margin = new Thickness(26) };
        var header = new StackPanel(); header.Children.Add(UIHelpers.Text("Capture current windows", 25, bold: true)); header.Children.Add(UIHelpers.Text("Select the windows to add. Their positions and sizes are preserved. Windows already assigned to another session will move here.", color: UIHelpers.Muted)); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; var add = UIHelpers.Button("Add selected windows", () => DialogResult = true, true); add.IsDefault = true; footer.Children.Add(add);
        var cancel = UIHelpers.Button("Cancel", () => DialogResult = false); cancel.IsCancel = true; footer.Children.Add(cancel); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        var selection = new WrapPanel(); selection.Children.Add(UIHelpers.Button("Select unmanaged", () => choices.ForEach(c => c.Selected = c.Details.EndsWith("Unmanaged")))); selection.Children.Add(UIHelpers.Button("Select all", () => choices.ForEach(c => c.Selected = true))); selection.Children.Add(UIHelpers.Button("Clear", () => choices.ForEach(c => c.Selected = false))); DockPanel.SetDock(selection, Dock.Top); panel.Children.Add(selection);
        var list = new ListBox { ItemsSource = choices };
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(CaptureChoice.Selected)) { Mode = BindingMode.TwoWay });
        var row = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(CaptureChoice.Title))); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); row.AppendChild(title);
        var details = new FrameworkElementFactory(typeof(TextBlock)); details.SetBinding(TextBlock.TextProperty, new Binding(nameof(CaptureChoice.Details))); details.SetValue(TextBlock.ForegroundProperty, UIHelpers.Muted); details.SetValue(TextBlock.FontSizeProperty, 12.0); row.AppendChild(details); check.AppendChild(row); list.ItemTemplate = new DataTemplate { VisualTree = check };
        if (choices.Count == 0) panel.Children.Add(UIHelpers.Text("No eligible visible application windows were found. Open an application and try again.", color: UIHelpers.Muted)); else panel.Children.Add(list);
        Content = panel;
    }
}
