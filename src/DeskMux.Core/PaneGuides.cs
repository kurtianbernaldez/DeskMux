namespace DeskMux.Core;

public enum PaneGuideVisibility { Always, CommandAndOperations, Briefly, Never }

public sealed class PaneGuideSettings
{
    public PaneGuideVisibility Visibility { get; set; } = PaneGuideVisibility.CommandAndOperations;
    // Empty colors follow the current theme.
    public string DividerColor { get; set; } = "";
    public string FocusColor { get; set; } = "";
    public string ResizeColor { get; set; } = "";
    public double Thickness { get; set; } = 1;
    public double Opacity { get; set; } = .65;
    public int FadeDelayMs { get; set; } = 1200;
    public static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#' &&
        uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _);
    public void Normalize()
    {
        if (!Enum.IsDefined(Visibility)) Visibility = PaneGuideVisibility.CommandAndOperations;
        DividerColor = ValidColor(DividerColor) ? DividerColor : "";
        FocusColor = ValidColor(FocusColor) ? FocusColor : "";
        ResizeColor = ValidColor(ResizeColor) ? ResizeColor : "";
        Thickness = double.IsFinite(Thickness) ? Math.Clamp(Thickness, .5, 8) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : .65;
        FadeDelayMs = Math.Clamp(FadeDelayMs, 0, 10000);
    }
    public GuideStyle Resolve(ThemePalette theme) => new(
        ValidColor(DividerColor) ? DividerColor : theme.Border,
        ValidColor(FocusColor) ? FocusColor : theme.Accent,
        ValidColor(ResizeColor) ? ResizeColor : theme.Text, Thickness, Opacity);
}

public sealed record GuideStyle(string Divider, string Focus, string Resize, double Thickness, double Opacity);
public sealed record PaneGuideLine(Guid NodeId, int X1, int Y1, int X2, int Y2);
public sealed record PaneGuideGeometry(IReadOnlyList<PaneGuideLine> Dividers, PixelRect? Focus);

public static class PaneGuides
{
    // Pruning happens on a copy, exactly as in live pane placement. Missing leaves never invent dividers.
    public static PaneGuideGeometry Calculate(PaneCanvas source, ISet<Guid> liveWindows, Guid? focused)
    {
        var canvas = PaneTree.Clone(source);
        PaneTree.Validate(canvas, liveWindows);
        if (canvas.RootNodeId == null) return new([], null);
        var leaf = focused is { } id ? PaneTree.FindLeaf(canvas, id) : null;
        if (canvas.ZoomedLeafId is { } zoom) return new([], leaf?.Id == zoom ? canvas.MonitorWorkArea : null);
        var nodes = PaneTree.CalculateNodes(canvas);
        var lines = canvas.Nodes.Where(n => !n.IsLeaf).Select(n =>
        {
            var parent = nodes[n.Id]; var second = nodes[n.SecondChildId!.Value];
            return n.Orientation == PaneOrientation.Vertical
                ? new PaneGuideLine(n.Id, second.X, parent.Y, second.X, parent.Y + parent.Height)
                : new PaneGuideLine(n.Id, parent.X, second.Y, parent.X + parent.Width, second.Y);
        }).ToArray();
        return new(lines, leaf == null ? null : nodes[leaf.Id]);
    }
}

public sealed class PaneGuideVisibilityState
{
    private TimeSpan? _changed;
    public void Changed(TimeSpan now) => _changed = now;
    public double Alpha(PaneGuideSettings settings, TimeSpan now, bool command, bool operation)
    {
        if (settings.Visibility == PaneGuideVisibility.Never) return 0;
        if (settings.Visibility == PaneGuideVisibility.Always ||
            settings.Visibility == PaneGuideVisibility.CommandAndOperations && (command || operation)) return settings.Opacity;
        if (_changed is not { } changed) return 0;
        var elapsed = (now - changed).TotalMilliseconds - settings.FadeDelayMs;
        return settings.Opacity * Math.Clamp(1 - Math.Max(0, elapsed) / 250, 0, 1);
    }
}
