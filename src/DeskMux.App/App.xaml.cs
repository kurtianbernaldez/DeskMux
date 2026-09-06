using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeskMux.Windows;

namespace DeskMux.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private EventWaitHandle? _recover;
    private EventWaitHandle? _exit;
    private RegisteredWaitHandle? _activateWait, _recoverWait, _exitWait;
    internal AppController? Controller { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 4 && e.Args[0] == "--watchdog")
        {
            RecoveryWatcher.Run(int.Parse(e.Args[1]), long.Parse(e.Args[2]), e.Args[3]);
            Shutdown(); return;
        }
        var dataDirectory = ResolveDataDirectory(e.Args);
        dataDirectory = NormalizeDataDirectory(dataDirectory);
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..16];
        _mutex = new Mutex(true, "Local\\DeskMux." + suffix, out var created);
        if (!created)
        {
            var command = e.Args.Contains("--shutdown") ? ".exit" : e.Args.Contains("--recover") ? ".recover" : ".activate";
            try { using var signal = EventWaitHandle.OpenExisting("Local\\DeskMux." + suffix + command); signal.Set(); } catch { }
            _mutex.Dispose(); _mutex = null; Shutdown(); return;
        }
        if (e.Args.Contains("--shutdown")) { ReleaseInstanceMutex(); Shutdown(); return; }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DeskMux." + suffix + ".activate");
        _recover = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DeskMux." + suffix + ".recover");
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DeskMux." + suffix + ".exit");
        try
        {
            Controller = new AppController(this, dataDirectory);
            DispatcherUnhandledException += (_, args) =>
            {
                Controller.Log.Write("unhandled_ui_error", args.Exception.ToString());
                Controller.Emergency();
                MessageBox.Show("DeskMux encountered an error and restored managed windows.\n\n" + args.Exception.Message, "DeskMux", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => { try { Controller.Log.Write("fatal_error", args.ExceptionObject.ToString() ?? "Unknown error"); } catch { } };
            SessionEnding += (_, _) => Controller.Exit();
            _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(() => Controller.OpenManager()), null, -1, false);
            _recoverWait = ThreadPool.RegisterWaitForSingleObject(_recover, (_, _) => Dispatcher.BeginInvoke(() => Controller.Emergency()), null, -1, false);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exit, (_, _) => Dispatcher.BeginInvoke(() => Controller.Exit()), null, -1, true);
            Controller.Start(e.Args.Contains("--recover"));
        }
        catch (Exception ex)
        {
            try { Controller?.Dispose(); } catch { }
            MessageBox.Show("DeskMux could not start.\n\n" + ex.Message + "\n\nAny recovery journal is retained for the next launch.", "DeskMux", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal static string NormalizeDataDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static string ResolveDataDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--data-dir");
        if (index >= 0 && args.Length > index + 1) return args[index + 1];
        if (args.Contains("--portable") || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode")))
            return Path.Combine(AppContext.BaseDirectory, "Data");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskMux");
    }

    private void ReleaseInstanceMutex()
    {
        if (_mutex == null) return;
        _mutex.ReleaseMutex(); _mutex.Dispose(); _mutex = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Controller?.Dispose(); _activateWait?.Unregister(null); _recoverWait?.Unregister(null); _exitWait?.Unregister(null);
        _activate?.Dispose(); _recover?.Dispose(); _exit?.Dispose();
        ReleaseInstanceMutex();
        base.OnExit(e);
    }
}
