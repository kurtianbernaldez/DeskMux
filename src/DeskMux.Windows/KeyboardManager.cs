using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskMux.Core;

namespace DeskMux.Windows;

/// <summary>A dedicated message-loop thread keeps low-level input independent of UI and persistence work.</summary>
public sealed class KeyboardManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action<Action> _dispatch;
    private readonly ILog _log;
    private readonly NativeMethods.KeyboardProc _callback;
    private readonly HashSet<int> _pressed = [];
    private readonly HashSet<int> _suppressed = [];
    private readonly ConcurrentQueue<Action> _notifications = new();
    private KeyboardOptions _options;
    private BackgroundMessageLoop? _loop;
    private nint _hook;
    private int _notificationWorker;
    private volatile bool _commandMode;
    private volatile bool _pickerMode;
    private volatile bool _disposed;
    private volatile bool _recording;
    private long _modeStarted;
    private CommandGesture? _heldResize;
    private long _resizeRepeated;

    public KeyboardManager(AppSettings settings, Action<Action> dispatch, ILog log)
    {
        _settings = settings;
        _dispatch = dispatch;
        _log = log;
        _options = SnapshotSettings();
        _callback = HandleKeyboard;
    }

    public event Action? PrefixEntered;
    public event Action? PrefixCancelled;
    /// <summary>Receives the physical virtual key and the modifiers held for the second key.</summary>
    public event Action<CommandGesture>? Command;
    public event Action<int>? PickerKeyPressed;
    public event Action? Emergency;
    public bool IsCommandMode => _commandMode;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loop is not null) return;
        var loop = new BackgroundMessageLoop("DeskMux keyboard", () =>
        {
            _hook = NativeMethods.SetWindowsHookEx(13, _callback, NativeMethods.GetModuleHandle(null), 0);
            if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not register the global keyboard listener.");
        }, () =>
        {
            if (_hook != 0) NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        });
        try { loop.Start(); _loop = loop; }
        catch (Exception exception)
        {
            loop.Dispose();
            RecoveryWatcher.SafeLog(_log, "keyboard.hook.failed", exception.Message);
            throw;
        }
    }

    public void SetRecording(bool recording) { _recording=recording; CancelCommandMode(); }
    public void CancelCommandMode() => _commandMode = false;
    public void BeginPickerMode() { _commandMode = false; _pickerMode = true; }
    public void EndPickerMode() => _pickerMode = false;

    public void RefreshSettings()
    {
        Volatile.Write(ref _options, SnapshotSettings());
        CancelCommandMode();
    }

    private KeyboardOptions SnapshotSettings() => new(
        _settings.PrefixVirtualKey, _settings.PrefixModifiers,
        Math.Clamp(_settings.CommandTimeoutMs, 500, 10000), _settings.KeyboardPaused, Hotkeys.All.ToDictionary(d => Hotkeys.Get(_settings,d), d => d));

    private nint HandleKeyboard(int code, nuint message, nint dataPointer)
    {
        if (code < 0 || _disposed) return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);
        try
        {
            var messageId = (uint)message;
            if (messageId is not (0x0100 or 0x0101 or 0x0104 or 0x0105))
                return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardData>(dataPointer);
            var key = (int)data.VirtualKey;
            var isUp = messageId is 0x0101 or 0x0105;
            if (isUp)
            {
                if (_heldResize?.VirtualKey == key) _heldResize = null;
                _pressed.Remove(key);
                if (_suppressed.Remove(key)) return 1;
                return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);
            }

            var firstDown = _pressed.Add(key);
            if (_suppressed.Contains(key))
            {
                if (_heldResize is { } repeat && repeat.VirtualKey == key && !Volatile.Read(ref _options).Paused &&
                    CurrentModifiers() == repeat.Modifiers && Stopwatch.GetElapsedTime(_resizeRepeated).TotalMilliseconds >= 90)
                {
                    _resizeRepeated = Stopwatch.GetTimestamp();
                    Notify(() => Command?.Invoke(repeat));
                }
                return 1;
            }
            if (!firstDown) return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);

            if (_recording) return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);
            var modifiers = CurrentModifiers();
            if (Volatile.Read(ref _options).Bindings.TryGetValue(new(key,modifiers), out var emergency) && emergency.Global)
            {
                _suppressed.Add(key);
                _commandMode = false;
                _pickerMode = false;
                Notify(() => { PrefixCancelled?.Invoke(); Emergency?.Invoke(); });
                return 1;
            }

            // Picker navigation must not depend on foreground activation permission.
            // Swallow ordinary typing while it is open so it cannot reach the source app.
            if (_pickerMode && !IsModifier(key))
            {
                if ((modifiers & (PrefixModifiers.Alt | PrefixModifiers.Windows)) != 0)
                {
                    _pickerMode = false;
                    Notify(() => PickerKeyPressed?.Invoke(0));
                    return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);
                }
                _suppressed.Add(key);
                var pickerKey = key is >= 0x61 and <= 0x69 ? key - 0x30 : key;
                if (pickerKey is 0x09 or 0x26 or 0x28 or 0x4A or 0x4B or 0x0D or 0x1B or >= 0x31 and <= 0x39)
                    Notify(() => PickerKeyPressed?.Invoke(pickerKey));
                return 1;
            }

            var options = Volatile.Read(ref _options);
            if (options.Paused) return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);
            if (key == options.Key && modifiers == options.Modifiers)
            {
                _suppressed.Add(key);
                _modeStarted = Stopwatch.GetTimestamp();
                _commandMode = true;
                Notify(() => PrefixEntered?.Invoke());
                return 1;
            }

            if (_commandMode && Stopwatch.GetElapsedTime(_modeStarted).TotalMilliseconds > options.Timeout)
            {
                _commandMode = false;
                Notify(() => PrefixCancelled?.Invoke());
            }
            if (_commandMode && !IsModifier(key))
            {
                _commandMode = false;
                if (options.Bindings.TryGetValue(new(key,modifiers), out var cancel) && cancel.Id == "Cancel")
                {
                    _suppressed.Add(key);
                    Notify(() => PrefixCancelled?.Invoke());
                    return 1;
                }
                var commandKey = !options.Bindings.ContainsKey(new(key,modifiers)) && key is >= 0x61 and <= 0x69 ? key - 0x30 : key;
                var gesture = new CommandGesture(commandKey, modifiers);
                if (options.Bindings.TryGetValue(gesture, out var binding) && !binding.Global)
                {
                    if (binding.Repeat)
                    { _heldResize = gesture; _resizeRepeated = Stopwatch.GetTimestamp(); }
                    _suppressed.Add(key);
                    // Arm immediately so a fast navigation key cannot slip through while
                    // the dispatcher is still constructing the W/M picker.
                    if (binding.Picker) _pickerMode = true;
                    Notify(() => Command?.Invoke(gesture));
                    return 1;
                }
                // Unsupported second keys cancel the overlay and reach the foreground app normally.
                Notify(() => PrefixCancelled?.Invoke());
            }
        }
        catch (Exception exception)
        {
            _commandMode = false;
            Notify(() => RecoveryWatcher.SafeLog(_log, "keyboard.callback.failed", exception.Message));
        }
        return NativeMethods.CallNextHookEx(_hook, code, message, dataPointer);
    }

    private static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
    private static PrefixModifiers CurrentModifiers()
    {
        static bool Down(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        var modifiers = PrefixModifiers.None;
        if (Down(0x11)) modifiers |= PrefixModifiers.Control;
        if (Down(0x12)) modifiers |= PrefixModifiers.Alt;
        if (Down(0x10)) modifiers |= PrefixModifiers.Shift;
        if (Down(0x5B) || Down(0x5C)) modifiers |= PrefixModifiers.Windows;
        return modifiers;
    }

    private void Notify(Action notification)
    {
        _notifications.Enqueue(notification);
        if (Interlocked.CompareExchange(ref _notificationWorker, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => DrainNotifications());
    }

    private void DrainNotifications()
    {
        do
        {
            while (_notifications.TryDequeue(out var notification))
            {
                if (_disposed) continue;
                try { _dispatch(() => { if (!_disposed) notification(); }); }
                catch (Exception exception) { RecoveryWatcher.SafeLog(_log, "keyboard.dispatch.failed", exception.Message); }
            }
            Interlocked.Exchange(ref _notificationWorker, 0);
        } while (!_notifications.IsEmpty && Interlocked.CompareExchange(ref _notificationWorker, 1, 0) == 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commandMode = false;
        _pickerMode = false;
        _loop?.Dispose();
        _loop = null;
        GC.KeepAlive(_callback);
    }

    private sealed record KeyboardOptions(int Key, PrefixModifiers Modifiers, int Timeout, bool Paused, Dictionary<CommandGesture, HotkeyDefinition> Bindings);
}

internal sealed class BackgroundMessageLoop(string name, Action initialize, Action cleanup) : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private uint _threadId;
    private Exception? _initializationError;
    private volatile bool _disposed;

    internal void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException($"{name} did not start in time.");
        if (_initializationError is not null) throw new InvalidOperationException($"{name} could not start.", _initializationError);
    }

    private void Run()
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            // Force a message queue into existence before another thread can post WM_QUIT.
            NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
            initialize();
            _ready.Set();
            while (!_disposed && NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessage(in message);
            }
        }
        catch (Exception exception) { _initializationError = exception; _ready.Set(); }
        finally { cleanup(); }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, 0x0012, 0, 0);
        if (_thread is not null && Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
    }
}
