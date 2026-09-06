namespace DeskMux.Core;

/// <summary>Maps physical pixel layouts between display arrangements, keeping restored windows reachable.</summary>
public static class MonitorMapper
{
    public static WindowLayout Map(WindowLayout layout, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var usable = monitors.Where(m => m.WorkArea.Width > 0 && m.WorkArea.Height > 0).ToArray();
        if (usable.Length == 0) return Clone(layout);
        var target = (!string.IsNullOrEmpty(layout.MonitorId) ? usable.FirstOrDefault(m => string.Equals(m.StableId, layout.MonitorId, StringComparison.OrdinalIgnoreCase)) : null)
            ?? usable.FirstOrDefault(m => string.Equals(m.DeviceName, layout.MonitorDevice, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(layout.MonitorId) || string.IsNullOrEmpty(m.StableId)))
            ?? usable.FirstOrDefault(m => m.IsPrimary) ?? usable[0];
        var oldWork = layout.MonitorWorkArea;
        var work = target.WorkArea;
        var oldDpi = layout.Dpi > 0 ? layout.Dpi : 96;
        var newDpi = target.Dpi > 0 ? target.Dpi : 96;
        var scale = (double)newDpi / oldDpi;
        var width = ClampDimension(layout.Bounds.Width * scale, work.Width);
        var height = ClampDimension(layout.Bounds.Height * scale, work.Height);

        // Preserve an offset measured in logical pixels; shift with the monitor origin after reordering.
        var offsetX = oldWork.Width > 0 ? (double)layout.Bounds.X - oldWork.X : 0;
        var offsetY = oldWork.Height > 0 ? (double)layout.Bounds.Y - oldWork.Y : 0;
        var x = ClampCoordinate(work.X + offsetX * scale, work.X, (long)work.X + work.Width - width);
        var y = ClampCoordinate(work.Y + offsetY * scale, work.Y, (long)work.Y + work.Height - height);
        return new WindowLayout
        {
            Bounds = new PixelRect(x, y, width, height), ShowState = layout.ShowState, RestoreToMaximized = layout.RestoreToMaximized,
            MonitorDevice = target.DeviceName, MonitorId = target.StableId, MonitorWorkArea = work, Dpi = newDpi
        };
    }

    public static WindowLayout Clone(WindowLayout layout) => new()
    {
        Bounds = layout.Bounds with { }, ShowState = layout.ShowState, RestoreToMaximized = layout.RestoreToMaximized, MonitorDevice = layout.MonitorDevice,
        MonitorId = layout.MonitorId, MonitorWorkArea = layout.MonitorWorkArea with { }, Dpi = layout.Dpi
    };

    private static int ClampDimension(double value, int maximum) =>
        (int)Math.Clamp(double.IsFinite(value) ? Math.Round(value) : maximum, Math.Min(100, maximum), maximum);
    private static int ClampCoordinate(double value, long minimum, long maximum) =>
        (int)Math.Clamp(Math.Clamp(double.IsFinite(value) ? Math.Round(value) : minimum, minimum, maximum), int.MinValue, int.MaxValue);
}
