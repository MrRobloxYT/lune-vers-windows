using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LuneVers;

/// Writes any unexpected error to %APPDATA%\<app>\crash.log and keeps the app
/// running when it can, so a bug shows up as a message instead of the app
/// silently not opening.
public static class Crash
{
    public static string Log(string app) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app, "crash.log");

    public static void Install(string app, Application application)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(app, e.ExceptionObject as Exception, true);
        TaskScheduler.UnobservedTaskException += (_, e) => { Write(app, e.Exception, false); e.SetObserved(); };
        application.DispatcherUnhandledException += (_, e) => { Write(app, e.Exception, false); e.Handled = true; };
    }

    static int shown;
    public static void Write(string app, Exception? error, bool fatal)
    {
        try
        {
            var path = Log(app);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {app} {Updater.Current} {(fatal ? "FATAL" : "error")}\n{error}\n\n");
        }
        catch { }
        if (fatal && Interlocked.Exchange(ref shown, 1) == 0)
        {
            try { MessageBox.Show($"{app} hit a problem and has to close.\n\n{error?.Message}\n\nDetails are in {Log(app)}", app, MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
        }
    }
}
