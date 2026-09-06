using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;

namespace DeskMux.App.UI;

internal static class UIHelpers
{
    internal static Brush Muted => ThemeManager.Brush("Muted");
    internal static Brush Brush(string key) => ThemeManager.Brush(key);
    internal static TextBlock Text(string text, double size = 14, Brush? color = null, bool bold = false) => new() { Text = text, FontSize = size, Foreground = color ?? ThemeManager.Brush("Ink"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    internal static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label }; button.Click += (_, _) => action();
        if (primary) { button.Background = ThemeManager.Brush("Accent"); button.Foreground = ThemeManager.Brush("OnAccent"); }
        return button;
    }
    internal static string PrefixLabel(AppSettings settings)
    {
        var parts = new List<string>();
        if (settings.PrefixModifiers.HasFlag(PrefixModifiers.Control)) parts.Add("Ctrl");
        if (settings.PrefixModifiers.HasFlag(PrefixModifiers.Alt)) parts.Add("Alt");
        if (settings.PrefixModifiers.HasFlag(PrefixModifiers.Shift)) parts.Add("Shift");
        if (settings.PrefixModifiers.HasFlag(PrefixModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey(settings.PrefixVirtualKey).ToString()); return string.Join("+", parts);
    }
    internal static void ShowNearMonitor(Window window, IWindowSystem system, long source, bool activate)
    {
        var monitors = system.GetMonitors();
        var device = system.Inspect(source)?.Layout.MonitorDevice;
        GetCursorPos(out var cursor);
        var monitor = SelectMonitor(monitors, device, cursor.X, cursor.Y);
        window.ShowActivated = activate; window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (!activate)
                SetWindowLongPtrW(handle, -20, GetWindowLongPtrW(handle, -20) | 0x08000000 | 0x00000080);
            PositionOnMonitor(window, handle, monitor);
        };
        window.ContentRendered += (_, _) =>
        {
            // Correct for SizeToContent after layout. SourceInitialized already placed the
            // HWND before its first paint, so this does not flash on another monitor.
            PositionOnMonitor(window, new WindowInteropHelper(window).Handle, monitor);
        };
        window.Show(); if (activate) window.Activate();
    }

    internal static MonitorDescriptor? SelectMonitor(IReadOnlyList<MonitorDescriptor> monitors, string? sourceDevice, int cursorX, int cursorY) =>
        monitors.FirstOrDefault(m => !string.IsNullOrEmpty(sourceDevice) && string.Equals(m.DeviceName, sourceDevice, StringComparison.OrdinalIgnoreCase))
        ?? monitors.FirstOrDefault(m => cursorX >= m.Bounds.X && cursorY >= m.Bounds.Y &&
            (long)cursorX < (long)m.Bounds.X + m.Bounds.Width && (long)cursorY < (long)m.Bounds.Y + m.Bounds.Height)
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.FirstOrDefault();

    private static void PositionOnMonitor(Window window, nint handle, MonitorDescriptor? monitor)
    {
        if (monitor == null || handle == 0) return;
        var scale = (monitor.Dpi > 0 ? monitor.Dpi : 96) / 96.0;
        var width = window.ActualWidth > 0 ? window.ActualWidth : !double.IsNaN(window.Width) ? window.Width : 640;
        var x = monitor.WorkArea.X + (monitor.WorkArea.Width - (int)Math.Round(width * scale)) / 2;
        var y = monitor.WorkArea.Y + (int)Math.Round(40 * scale);
        SetWindowPos(handle, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}
