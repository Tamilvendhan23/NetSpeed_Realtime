namespace NetSpeedMonitor;

internal static class Program
{
    private const string AppMutexName = "Global\\NetSpeedMonitor_SingleInstance_Mutex_9921_Acer";

    [STAThread]
    private static void Main()
    {
        // Enforce single-instance application using a named mutex
        using var mutex = new Mutex(true, AppMutexName, out bool isOnlyInstance);

        if (!isOnlyInstance)
        {
            // Another instance is already running: ask it to show its status bar.
            MainForm.RequestShowExistingInstance();
            return;
        }

        // Configure WinForms runtime (PerMonitorV2 High DPI)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Global exception safety: log everything, never let one bad tick spam dialogs or kill the app.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        bool errorShownOnce = false;
        Application.ThreadException += (s, e) =>
        {
            AppLog.Write("UI thread exception (app keeps running)", e.Exception);
            if (!errorShownOnce)
            {
                errorShownOnce = true; // only one pop-up per session; the rest go to the log
                MainForm.ShowTopMostMessage(
                    $"NetSpeed Monitor hit an unexpected error and is continuing:\n\n{e.Exception.Message}\n\n" +
                    "Details were written to %LOCALAPPDATA%\\NetSpeedMonitor\\netspeedmonitor.log",
                    MessageBoxIcon.Warning);
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            // Log FIRST: this one terminates the process.
            AppLog.Write($"FATAL unhandled exception (terminating={e.IsTerminating})", e.ExceptionObject as Exception);
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) => AppLog.Write("Process exit");
        AppLog.Write("Started");

        Application.Run(new MainForm());
    }
}
