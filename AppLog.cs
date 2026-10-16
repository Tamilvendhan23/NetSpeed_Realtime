using System.Text;

namespace NetSpeedMonitor;

/// <summary>
/// Tiny diagnostic log: %LOCALAPPDATA%\NetSpeedMonitor\netspeedmonitor.log
/// Records crashes and why the app exited, so "it closed by itself" can be traced.
/// Never throws; trimmed automatically so it cannot grow without bound.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetSpeedMonitor", "netspeedmonitor.log");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ").Append(message);
                if (ex != null)
                {
                    sb.AppendLine().Append(ex);
                }

                sb.AppendLine();
                File.AppendAllText(path, sb.ToString());
            }
        }
        catch
        {
            // Logging must never be the reason the app fails.
        }
    }
}
