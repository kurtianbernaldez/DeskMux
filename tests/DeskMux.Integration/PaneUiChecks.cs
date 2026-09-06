using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskMux.App;
using DeskMux.App.UI;
using DeskMux.Core;

namespace DeskMux.Integration;

/// <summary>Exercises only DeskMux-owned WPF windows and synthetic window data.</summary>
internal static class PaneUiChecks
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Pane UI: " + name);
            checks++; Console.WriteLine("  PASS " + name);
        }
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["Ink"] = new SolidColorBrush(Color.FromRgb(23, 43, 58));
        app.Resources["Accent"] = new SolidColorBrush(Color.FromRgb(8, 126, 117));
        var owned = new List<Window>();
        var directory = Path.Combine(Path.GetTempPath(), "DeskMux.PaneUi", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var controller = new AppController(app, directory);
        void Show(Window window)
        {
            owned.Add(window); window.ShowActivated = false; window.Show(); window.UpdateLayout(); Pump();
        }
        try
        {
            var monitorA = new MonitorDescriptor("A", new(-1600, 0, 1600, 900), new(-1600, 0, 1600, 860), 96, false);
            var monitorB = new MonitorDescriptor("B", new(0, 0, 2560, 1440), new(0, 0, 2560, 1400), 144, true);
            Check(UIHelpers.SelectMonitor([monitorA, monitorB], null, -400, 300) == monitorA,
                "Overlay fallback follows the cursor across negative-coordinate monitors");
            Check(UIHelpers.SelectMonitor([monitorA, monitorB], "B", -400, 300) == monitorB,
                "Eligible source-window monitor takes precedence over cursor position");
            var identityPath = Path.Combine(directory, "identity");
            Check(DeskMux.App.App.NormalizeDataDirectory(identityPath) == DeskMux.App.App.NormalizeDataDirectory(identityPath + Path.DirectorySeparatorChar),
                "Equivalent data-directory spellings share one application identity");
            var startupState = new WorkspaceState { ActiveSessionId = Guid.NewGuid(), Settings = new() { RestoreActiveSessionOnStartup = true } };
            Check(AppController.ShouldRestoreAppsOnStartup(false, true, startupState) &&
                !AppController.ShouldRestoreAppsOnStartup(true, true, startupState) &&
                !AppController.ShouldRestoreAppsOnStartup(false, false, startupState),
                "Automatic app restore is disabled for recovery-only or unprotected startup");

            var pickerSession = new WorkspaceSession { Name = "WORK" };
            var editor = new ManagedWindow { Fingerprint = new() { ProcessName = "Code", Title = "DeskMux — Visual Studio Code" } };
            var browser = new ManagedWindow { Fingerprint = new() { ProcessName = "msedge", Title = "Documentation" } };
            var missing = new ManagedWindow { IsMissing = true, Fingerprint = new() { ProcessName = "Terminal", Title = "Build" } };
            pickerSession.Windows.AddRange([editor, browser, missing]);
            var pickerCanvas = new PaneCanvas(); PaneTree.Split(pickerCanvas, null, editor.Id, PaneOrientation.Vertical);
            pickerSession.PaneCanvases.Add(pickerCanvas);
            var pickerState = new WorkspaceState { Sessions = [pickerSession], ActiveSessionId = pickerSession.Id };
            var sessionPicker = new SessionPicker(pickerState, "Switch session"); Show(sessionPicker);
            var sessionText = Text(sessionPicker);
            Check(sessionText.Contains("Code") && sessionText.Contains("msedge") && sessionText.Contains("Terminal"),
                "Session switcher previews every saved application");
            Check(sessionText.Contains("Pane") && sessionText.Contains("Floating") && sessionText.Contains("Missing"),
                "Session switcher identifies pane, floating, and missing windows");
            sessionPicker.Close();
            Check(PrefixOverlay.CompactApps(new() { Windows = [editor, new ManagedWindow { Fingerprint = new() { ProcessName = "Code" } }, browser] })
                .Contains("Code ×2"), "Prefix overlay groups repeated application windows compactly");

            var empty = new PanePicker([], []); Show(empty);
            var emptyText = Text(empty);
            Check(emptyText.Contains("RUNNING WINDOWS") && emptyText.Contains("LAUNCH NEW"), "Empty Open pane picker retains both section headings");
            empty.HandleKey(0x28); empty.HandleKey(0x0D);
            Check(empty.IsVisible && empty.SelectedChoice == null, "Empty picker does not select section headers");
            empty.HandleKey(0x1B);
            Check(!empty.IsVisible && empty.CancelledByKeyboard && empty.SelectedChoice == null, "Escape closes an empty picker without selecting or changing data");

            WindowSnapshot Snapshot(long handle, string title) => new(handle,
                new WindowFingerprint { ProcessName = "SharedApp", Title = title, ProcessId = 777, ProcessStartTimeUtcTicks = 1 },
                new WindowLayout(), true);
            var first = new PaneChoice(Snapshot(111, "First document"), null, "Unmanaged");
            var second = new PaneChoice(Snapshot(222, "Second document"), null, "Current session");
            var third = new PaneChoice(Snapshot(333, "Third document"), null, "Session: OTHER");
            var profile = new AppLaunchProfile { Name = "Test launcher", Target = Environment.ProcessPath! };
            var launch = new PaneChoice(null, profile, "Launch application");
            var picker = new PanePicker([first, second, third], [launch]); Show(picker);
            var pickerText = Text(picker);
            Check(pickerText.Contains("First document") && pickerText.Contains("Second document") && pickerText.Contains("Third document"), "Windows sharing a process retain separate recognizable titles");
            Check(pickerText.Contains("Unmanaged") && pickerText.Contains("Current session") && pickerText.Contains("Session: OTHER"), "Running window rows show all three membership statuses");
            picker.HandleKey(0x4B); picker.HandleKey(0x0D);
            Check(picker.SelectedChoice == launch, "K wraps from first running window to launcher while skipping section headers");

            picker = new PanePicker([first, second, third], [launch]); Show(picker);
            picker.HandleKey(0x4A); picker.HandleKey(0x0D);
            Check(picker.SelectedChoice == second, "J chooses the next distinct native window");
            picker = new PanePicker([first, second, third], [launch]); Show(picker);
            picker.HandleKey(0x34);
            Check(picker.SelectedChoice == launch, "Quick selection numbers count choices across both sections");
            picker = new PanePicker([first, second], [], "Choose application window", true); Show(picker);
            Check(!Text(picker).Contains("LAUNCH NEW"), "Ambiguous launch chooser only offers explicit candidate windows");
            picker.HandleKey(0);
            Check(picker.SelectedChoice == null && !picker.CancelledByKeyboard, "External picker cancellation does not masquerade as Escape or select a candidate");

            var confirmation = new ConfirmMoveDialog("Second document", "OTHER", "CURRENT"); Show(confirmation);
            Check(Text(confirmation).Contains("Second document") && Text(confirmation).Contains("OTHER") && Text(confirmation).Contains("CURRENT"), "Cross-session confirmation identifies the exact window and both sessions");
            Check(Descendants<Button>(confirmation).Any(b => Equals(b.Content, "Cancel") && b.IsDefault && b.IsCancel), "Cross-session move confirmation defaults to cancellation");
            confirmation.Close();

            var draft = new AppLaunchProfile { Name = "Original", Target = Environment.ProcessPath!, Arguments = "--original" };
            var edit = new LaunchProfileDialog(draft); Show(edit);
            var fields = Descendants<TextBox>(edit).ToList(); fields[0].Text = "Unsaved"; fields[2].Text = "--unsaved";
            edit.Close();
            Check(draft.Name == "Original" && draft.Arguments == "--original", "Cancelling launcher editing leaves the original profile unchanged");
            var progress = new LaunchProgressWindow("Test launcher"); Show(progress); progress.HandleKey(0x1B);
            Check(!progress.IsVisible, "Launch progress supports cancellation without launching or closing any application");
            var restoreProgress = new LaunchProgressWindow("Test launcher", restoring: true); Show(restoreProgress);
            Check(Text(restoreProgress).Contains("Restoring Test launcher") && Descendants<Button>(restoreProgress).Any(b => Equals(b.Content,"Cancel session restore")),
                "Session restore progress explains cancellation without hiding launched apps");
            restoreProgress.Close();

            controller.Sessions.CreateSession("FIRST"); controller.Sessions.CreateSession("SECOND");
            var originalSession = controller.Sessions.State.ActiveSessionId;
            var execute = typeof(AppController).GetMethod("ExecuteCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var key in new[] { 0x25, 0x26, 0x27, 0x28 })
            {
                execute.Invoke(controller, [new CommandGesture(key)]);
                Check(controller.Sessions.State.ActiveSessionId == originalSession && !string.IsNullOrWhiteSpace(controller.Sessions.LastError), "Arrow " + key + " reports absent panes without switching sessions");
                execute.Invoke(controller, [new CommandGesture(key, PrefixModifiers.Control)]);
                Check(controller.Sessions.State.ActiveSessionId == originalSession, "Modified arrow " + key + " stays in the active session");
            }
            controller.Sessions.State.Settings.LaunchProfiles.Add(profile);
            var manager = new ManagerWindow(controller); Show(manager);
            manager.Navigate("Launchers"); manager.UpdateLayout();
            Check(Text(manager).Contains("Test launcher") && Descendants<Button>(manager).Any(b => Equals(b.Content, "+  Add executable…")), "Manager exposes persisted launchers and executable selection");
            manager.Navigate("Hotkeys"); manager.UpdateLayout();
            Check(Text(manager).Contains("Ctrl+Arrows") && Text(manager).Contains("Zoom / restore pane"), "Hotkeys page documents pane resize and zoom mappings");
            manager.Navigate("Sessions"); manager.UpdateLayout();
            Check(Descendants<Button>(manager).Any(b => Equals(b.Content, "Release to floating")) && Descendants<Button>(manager).Any(b => Equals(b.Content, "Reapply layout")), "Manager exposes pane release and layout reapplication");
            Check(Descendants<Button>(manager).Any(b => Equals(b.Content, "Restore apps")), "Manager exposes session application restore");
            manager.Navigate("Behavior"); manager.UpdateLayout();
            Check(Descendants<CheckBox>(manager).Any(b => Equals(b.Content, "Restore missing applications in the active session when DeskMux starts")),
                "Behavior page exposes optional startup restore");
            manager.Navigate("Appearance"); manager.UpdateLayout();
            var appearanceText=Text(manager);
            Check(ThemeManager.PresetNames.Contains("Dracula") && ThemeManager.PresetNames.Contains("Nord") && ThemeManager.PresetNames.Contains("Solarized Dark") &&
                Descendants<Button>(manager).Any(b=>Equals(b.Content,"Apply selected theme")), "Appearance page exposes terminal-inspired theme presets");
            Check(Descendants<TextBox>(manager).Count() >= 8 && Descendants<Button>(manager).Any(b=>Equals(b.Content,"Save and apply custom")) && appearanceText.Contains("CUSTOM COLORS"),
                "Appearance page exposes an editable custom color palette");
            Check(ThemeManager.TryNormalize(new ThemePalette { Accent="#123ABC" },out var normalizedTheme,out _) && normalizedTheme.Accent=="#123ABC" &&
                !ThemeManager.TryNormalize(new ThemePalette { Accent="not-a-color" },out _,out _) &&
                !ThemeManager.TryNormalize(new ThemePalette { Accent="Red" },out _,out _), "Custom theme colors require #RRGGBB before saving");
            var accentBrush=ThemeManager.Brush("Accent"); controller.ApplyTheme("Dracula");
            Check(ReferenceEquals(accentBrush,ThemeManager.Brush("Accent")) && ((SolidColorBrush)accentBrush).Color==Color.FromRgb(0xBD,0x93,0xF9),
                "Applying a theme updates existing shared UI brushes live");
            controller.ApplyTheme("System");
            manager.Close();
            Check(controller.Sessions.State.Sessions.All(s => s.Windows.Count == 0 && s.PaneCanvases.Count == 0), "UI verification leaves every unrelated application unmanaged");
        }
        finally
        {
            foreach (var window in owned.Where(w => w.IsVisible).ToList()) window.Close();
        }
        return checks;
    }

    private static string Text(DependencyObject root) => string.Join("\n", Descendants<TextBlock>(root).Select(t => t.Text));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T result) yield return result;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
