using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskMux.Core;

namespace DeskMux.Windows;

/// <summary>Event-driven HWND tracking; no desktop enumeration or persistence runs inside native callbacks.</summary>
public sealed class WinEventTracker : IDisposable
{
    private readonly Action<Action> _dispatch;
    private readonly ILog _log;
    private readonly NativeMethods.WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private readonly ConcurrentDictionary<long, byte> _pendingWindows = new();
    private readonly ConcurrentQueue<(long Handle, bool Foreground, bool MoveSizeStarted, bool MoveSizeEnded, bool Coalesced, LaunchWindowEvent? Observation)> _events = new();
    private BackgroundMessageLoop? _loop;
    private int _draining;
    private volatile bool _disposed;

    public WinEventTracker(Action<Action> dispatch, ILog log)
    {
        _dispatch = dispatch;
        _log = log;
        _callback = OnEvent;
    }

    /// <summary>Fires for foreground, show/hide, destroyed, minimized, title, or location changes.</summary>
    public event Action<long, bool>? WindowChanged;
    /// <summary>Fires when Windows begins an interactive move or resize of a top-level window.</summary>
    public event Action<long>? MoveSizeStarted;
    public event Action<long>? MoveSizeEnded;
    /// <summary>Precise show/foreground events delivered on a worker thread, with original monotonic event time.</summary>
    public event Action<LaunchWindowEvent>? WindowObserved;
    public event Action? DisplayChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loop is not null) return;
        var loop = new BackgroundMessageLoop("DeskMux window events", () =>
        {
            AddHook(0x0003, 0x0003); // EVENT_SYSTEM_FOREGROUND
            AddHook(0x000A, 0x000B); // EVENT_SYSTEM_MOVESIZESTART / END
            AddHook(0x0016, 0x0017); // EVENT_SYSTEM_MINIMIZESTART / END
            AddHook(0x8001, 0x8003); // EVENT_OBJECT_DESTROY / SHOW / HIDE
            AddHook(0x800B, 0x800C); // EVENT_OBJECT_LOCATIONCHANGE / NAMECHANGE
        }, () =>
        {
            foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
            _hooks.Clear();
        });
        try { loop.Start(); _loop = loop; }
        catch (Exception exception)
        {
            loop.Dispose();
            RecoveryWatcher.SafeLog(_log, "window.hooks.failed", exception.Message);
            throw;
        }
    }

    private void AddHook(uint minimum, uint maximum)
    {
        var hook = NativeMethods.SetWinEventHook(minimum, maximum, 0, _callback, 0, 0, 0x0002);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not register a window event listener.");
        _hooks.Add(hook);
    }

    private void OnEvent(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed || hwnd == 0 || objectId != 0 || childId != 0) return;
        var handle = hwnd.ToInt64();
        var foreground = eventId == 0x0003;
        var shown = eventId == 0x8002;
        var moveSizeStarted = eventId == 0x000A;
        var moveSizeEnded = eventId == 0x000B;
        // Foreground and show events retain their kind and original occurrence time even
        // when geometry updates are already queued. Delayed old events must not claim a launch.
        var coalesced = !foreground && !shown && !moveSizeStarted && !moveSizeEnded;
        if (coalesced && !_pendingWindows.TryAdd(handle, 0)) return;
        LaunchWindowEvent? observation = null;
        if (foreground || shown)
        {
            var now = Environment.TickCount64;
            var age = unchecked((uint)now - time); // DWORD uptime wraps every 49.7 days.
            observation = new(handle, foreground ? LaunchWindowEventKind.Foreground : LaunchWindowEventKind.Shown, now - age);
        }
        _events.Enqueue((handle, foreground, moveSizeStarted, moveSizeEnded, coalesced, observation));
        if (Interlocked.CompareExchange(ref _draining, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => Drain());
    }

    private void Drain()
    {
        do
        {
            while (_events.TryDequeue(out var item))
            {
                if (_disposed) continue;
                if (item.Observation is { } observation)
                {
                    try { WindowObserved?.Invoke(observation); }
                    catch (Exception exception) { RecoveryWatcher.SafeLog(_log, "window.observation.failed", exception.Message); }
                }
                try
                {
                    _dispatch(() =>
                    {
                        if (item.Coalesced) _pendingWindows.TryRemove(item.Handle, out _);
                        if (_disposed) return;
                        if (item.MoveSizeStarted) MoveSizeStarted?.Invoke(item.Handle);
                        if (item.MoveSizeEnded) MoveSizeEnded?.Invoke(item.Handle);
                        WindowChanged?.Invoke(item.Handle, item.Foreground);
                    });
                }
                catch (Exception exception)
                {
                    if (item.Coalesced) _pendingWindows.TryRemove(item.Handle, out _);
                    RecoveryWatcher.SafeLog(_log, "window.event.dispatch.failed", exception.Message);
                }
            }
            Interlocked.Exchange(ref _draining, 0);
        } while (!_events.IsEmpty && Interlocked.CompareExchange(ref _draining, 1, 0) == 0);
    }

    /// <summary>The application's hidden HwndSource calls this for WM_DISPLAYCHANGE / WM_DPICHANGED.</summary>
    public void NotifyDisplayChanged()
    {
        if (!_disposed) _dispatch(() => { if (!_disposed) DisplayChanged?.Invoke(); });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loop?.Dispose();
        _loop = null;
        GC.KeepAlive(_callback);
    }
}
