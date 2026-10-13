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

        // Global exception safety
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
        {
            MainForm.ShowTopMostMessage(
                $"NetSpeed Monitor encountered an unexpected error:\n\n{e.Exception.Message}",
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MainForm.ShowTopMostMessage(
                    $"NetSpeed Monitor critical error:\n\n{ex.Message}",
                    MessageBoxIcon.Error);
            }
        };

        Application.Run(new MainForm());
    }
}
