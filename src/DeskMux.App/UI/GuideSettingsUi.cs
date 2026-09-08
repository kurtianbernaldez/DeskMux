namespace DeskMux.App.UI;

internal static class GuideSettingsUi
{
    internal static UIElement Behavior(AppController controller)
    {
        var panel = new StackPanel();
        panel.Children.Add(UIHelpers.Text("Pane guide visibility", 18, bold: true));
        var choice = new ComboBox { ItemsSource = new[] { "Always visible", "Show during command mode and pane operations", "Show briefly after changes", "Never visible" },
            SelectedIndex = (int)controller.Sessions.State.Settings.PaneGuides.Visibility };
        choice.SelectionChanged += (_, _) => { controller.Sessions.State.Settings.PaneGuides.Visibility = (PaneGuideVisibility)choice.SelectedIndex; controller.SettingsChanged(); };
        panel.Children.Add(choice); return panel;
    }
    internal static UIElement Appearance(AppController controller)
    {
        var settings = controller.Sessions.State.Settings.PaneGuides;
        var panel = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
        panel.Children.Add(UIHelpers.Text("Pane guides", 20, bold: true));
        panel.Children.Add(UIHelpers.Text("Use #RRGGBB colors, or leave blank to follow the theme. Thickness is in screen pixels; fading takes 250 ms after the delay.", color: UIHelpers.Muted));
        var preview = new Canvas { Width = 300, Height = 130, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 12) };
        var divider = Field("Divider color", settings.DividerColor);
        var focus = Field("Focused-pane outline color", settings.FocusColor);
        var resize = Field("Active-resize divider color", settings.ResizeColor);
        var thickness = Slider("Line thickness", .5, 8, settings.Thickness);
        var opacity = Slider("Overlay opacity", 0, 1, settings.Opacity);
        var delay = Slider("Fade delay (milliseconds)", 0, 10000, settings.FadeDelayMs);
        var error = UIHelpers.Text("", color: UIHelpers.Brush("Danger"));
        PaneGuideSettings Draft() => new() { DividerColor = divider.Text.Trim(), FocusColor = focus.Text.Trim(), ResizeColor = resize.Text.Trim(), Thickness = thickness.Value, Opacity = opacity.Value, FadeDelayMs = (int)delay.Value, Visibility = settings.Visibility };
        void Preview()
        {
            var theme = ThemeManager.Resolve(controller.Sessions.State.Settings.Theme);
            var style = Draft().Resolve(theme);
            preview.Children.Clear(); preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Surface));
            void Line(double x1, double y1, double x2, double y2, string color, double width)
                => preview.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), StrokeThickness = width, Opacity = style.Opacity });
            Line(150, 8, 150, 122, style.Divider, style.Thickness);
            Line(150, 65, 292, 65, style.Resize, style.Thickness * 2);
            Line(8, 8, 148, 8, style.Focus, style.Thickness); Line(8, 8, 8, 122, style.Focus, style.Thickness);
            Line(8, 122, 148, 122, style.Focus, style.Thickness); Line(148, 8, 148, 122, style.Focus, style.Thickness);
        }
        foreach (var field in new[] { divider, focus, resize }) field.TextChanged += (_, _) => Preview();
        foreach (var slider in new[] { thickness, opacity, delay }) slider.ValueChanged += (_, _) => Preview();
        panel.Children.Add(preview); panel.Children.Add(error);
        panel.Children.Add(UIHelpers.Button("Save pane guides", () =>
        {
            var draft = Draft();
            if (new[] { draft.DividerColor, draft.FocusColor, draft.ResizeColor }.Any(c => c.Length > 0 && !PaneGuideSettings.ValidColor(c)))
            { error.Text = "Use #RRGGBB colors or leave blank for theme defaults."; return; }
            draft.Normalize(); controller.Sessions.State.Settings.PaneGuides = draft; settings = draft;
            controller.SettingsChanged(); error.Text = "Saved.";
        }, true));
        Preview(); return panel;
        TextBox Field(string label, string value)
        { panel.Children.Add(UIHelpers.Text(label)); var input = new TextBox { Text = value, Width = 200, HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(input); return input; }
        Slider Slider(string label, double minimum, double maximum, double value)
        {
            var caption = UIHelpers.Text(label + ": " + value.ToString("0.##")); panel.Children.Add(caption);
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
            slider.ValueChanged += (_, _) => caption.Text = label + ": " + slider.Value.ToString("0.##");
            panel.Children.Add(slider); return slider;
        }
    }
}
