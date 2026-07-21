using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FlexTool;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlexTool", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch WPF dispatcher exceptions (e.g. style system internal errors)
        // so they don't tear down the process.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Catch exceptions from background threads and unobserved tasks so a
        // failed background scan (mods, logs, saves) never crashes the app.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception, "AppDomain");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception, "Task");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Swallow known WPF-internal style/trigger errors that cannot be avoided
        // when dynamic-theming replaces resources after the visual tree is built.
        if (e.Exception is System.NullReferenceException &&
            e.Exception.StackTrace is not null &&
            e.Exception.StackTrace.Contains("StyleHelper"))
        {
            e.Handled = true;
            return;
        }

        LogCrash(e.Exception, "Dispatcher");

        // For any other unhandled exception, log it and let the user decide.
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails were written to:\n{CrashLogPath}",
            "FlexTool Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogCrash(Exception? ex, string source)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) {ex}\n\n");
        }
        catch { /* Never throw from the crash logger itself */ }
    }
}


