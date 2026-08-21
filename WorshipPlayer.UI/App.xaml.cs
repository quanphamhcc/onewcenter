using System.Windows;

namespace WorshipPlayer.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure app exits completely when the main window closes
        // (don't keep running because of child windows like OutputWindow)
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // Handle any unhandled exceptions — show them during development
        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.Exception}");
            MessageBox.Show(
                $"Error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "OWC Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Domain unhandled exception: {args.ExceptionObject}");
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        // Backup force-kill in case MainWindow_Closing didn't fire
        // (e.g., if the app was terminated via Task Manager or API)
        Environment.Exit(0);
    }
}
