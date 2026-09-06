using System.Windows.Media;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using DeskMux.Core;
using Microsoft.Win32;

namespace DeskMux.App.UI;

internal static class ThemeManager
{
    internal static readonly string[] PresetNames =
        ["System", "DeskMux Dark", "Light", "Dracula", "Nord", "Gruvbox", "Solarized Dark", "Solarized Light", "High Contrast", "Custom"];

    private static readonly Dictionary<string, (ThemeColorSource Source, SolidColorBrush Brush)> Brushes = [];
    private static readonly Dictionary<string, ThemePalette> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DeskMux Dark"] = Palette("#10191F", "#17242C", "#0B1217", "#E6EEF2", "#9FB0BA", "#35B6A7", "#344650", "#F07468"),
        ["Light"] = Palette("#F5F7F9", "#FFFFFF", "#162A35", "#172B3A", "#647583", "#087E75", "#CFD8DF", "#A65349"),
        ["Dracula"] = Palette("#282A36", "#343746", "#1E1F29", "#F8F8F2", "#B8B8C7", "#BD93F9", "#505365", "#FF5555"),
        ["Nord"] = Palette("#2E3440", "#3B4252", "#242933", "#ECEFF4", "#AEB8C8", "#88C0D0", "#4C566A", "#BF616A"),
        ["Gruvbox"] = Palette("#282828", "#3C3836", "#1D2021", "#EBDBB2", "#BDAE93", "#B8BB26", "#665C54", "#FB4934"),
        ["Solarized Dark"] = Palette("#002B36", "#073642", "#001F27", "#EEE8D5", "#93A1A1", "#2AA198", "#31505A", "#DC322F"),
        ["Solarized Light"] = Palette("#FDF6E3", "#EEE8D5", "#DDD6C1", "#073642", "#657B83", "#268BD2", "#C9C1AC", "#DC322F"),
        ["High Contrast"] = Palette("#000000", "#0A0A0A", "#000000", "#FFFFFF", "#E5E5E5", "#FFFF00", "#FFFFFF", "#FF4D4D")
    };

    internal static Brush Brush(string key) => Brushes.TryGetValue(key, out var item)
        ? item.Brush : (Application.Current.Resources[key] as Brush ?? System.Windows.Media.Brushes.Transparent);

    internal static ThemePalette Resolve(ThemeSettings settings)
    {
        if (string.Equals(settings.Preset, "Custom", StringComparison.OrdinalIgnoreCase)) return Clone(settings.Custom);
        if (string.Equals(settings.Preset, "System", StringComparison.OrdinalIgnoreCase)) return SystemPalette();
        return Presets.TryGetValue(settings.Preset, out var palette) ? Clone(palette) : SystemPalette();
    }

    internal static void Apply(Application app, ThemeSettings settings)
    {
        var palette = Resolve(settings);
        var background = Parse(palette.Background); var surface = Parse(palette.Surface); var sidebar = Parse(palette.Sidebar);
        var text = Parse(palette.Text); var muted = Parse(palette.Muted); var accent = Parse(palette.Accent);
        Set(app, "Background", background); Set(app, "Surface", surface); Set(app, "Sidebar", sidebar);
        Set(app, "Ink", text); Set(app, "Muted", muted); Set(app, "Accent", accent);
        Set(app, "Border", Parse(palette.Border)); Set(app, "Danger", Parse(palette.Danger));
        Set(app, "Selection", Blend(accent, surface, .22)); Set(app, "Hover", Blend(accent, surface, .12));
        Set(app, "OnAccent", Contrast(accent)); Set(app, "SidebarText", Contrast(sidebar));
        Set(app, "SidebarMuted", Blend(Contrast(sidebar), sidebar, .68));
        Set(app, "SidebarSelection", Blend(accent, sidebar, .32));
        app.Resources["UiFont"] = new FontFamily("Segoe UI");
    }

    internal static bool TryNormalize(ThemePalette source, out ThemePalette normalized, out string error)
    {
        error = "";
        try
        {
            normalized = Palette(Format(Parse(source.Background)),Format(Parse(source.Surface)),Format(Parse(source.Sidebar)),
                Format(Parse(source.Text)),Format(Parse(source.Muted)),Format(Parse(source.Accent)),Format(Parse(source.Border)),Format(Parse(source.Danger)));
            return true;
        }
        catch
        {
            normalized = new ThemePalette(); error = "Every value must be a color such as #1E1F29."; return false;
        }
    }

    internal static ThemePalette Clone(ThemePalette p) => Palette(p.Background,p.Surface,p.Sidebar,p.Text,p.Muted,p.Accent,p.Border,p.Danger);
    private static ThemePalette SystemPalette()
    {
        if (SystemParameters.HighContrast)
            return Palette(Format(SystemColors.WindowColor), Format(SystemColors.ControlColor), Format(SystemColors.WindowColor),
                Format(SystemColors.WindowTextColor), Format(SystemColors.GrayTextColor), Format(SystemColors.HighlightColor),
                Format(SystemColors.WindowTextColor), "#FF4D4D");
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int light && light == 0) return Clone(Presets["DeskMux Dark"]);
        }
        catch { }
        return Clone(Presets["Light"]);
    }
    private static ThemePalette Palette(string background,string surface,string sidebar,string text,string muted,string accent,string border,string danger) =>
        new() { Background=background,Surface=surface,Sidebar=sidebar,Text=text,Muted=muted,Accent=accent,Border=border,Danger=danger };
    private static Color Parse(string value)
    {
        var text = value.Trim();
        if (text.Length != 7 || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException("Theme colors must use #RRGGBB.");
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    private static string Format(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static Color Blend(Color foreground, Color background, double amount) => Color.FromRgb(
        (byte)Math.Round(background.R+(foreground.R-background.R)*amount),
        (byte)Math.Round(background.G+(foreground.G-background.G)*amount),
        (byte)Math.Round(background.B+(foreground.B-background.B)*amount));
    private static Color Contrast(Color c) => (.2126*c.R+.7152*c.G+.0722*c.B) > 145 ? Colors.Black : Colors.White;
    private static void Set(Application app, string key, Color color)
    {
        if (!Brushes.TryGetValue(key, out var item))
        {
            var source = new ThemeColorSource(color); var brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ThemeColorSource.Value)) { Source=source });
            item=(source,brush); Brushes[key]=item;
        }
        item.Source.Value=color;
        app.Resources[key]=item.Brush;
    }

    private sealed class ThemeColorSource(Color value) : INotifyPropertyChanged
    {
        private Color _value=value;
        public Color Value { get=>_value; set { if (_value==value) return; _value=value; PropertyChanged?.Invoke(this,new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
