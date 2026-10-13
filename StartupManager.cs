using Microsoft.Win32;

namespace NetSpeedMonitor;

/// <summary>
/// Manages per-user Windows startup registration via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Requires NO administrator privileges.
/// </summary>
public static class StartupManager
{
    private const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NetSpeedMonitor";

    /// <summary>
    /// Checks if NetSpeedMonitor is currently registered in HKCU Run key.
    /// </summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: false);
            if (key == null) return false;

            object? val = key.GetValue(ValueName);
            return val is string str && !string.IsNullOrWhiteSpace(str);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers the specified executable path in the HKCU Run key.
    /// Quotes paths with spaces properly.
    /// </summary>
    /// <param name="executablePath">Full path to the NetSpeedMonitor.exe</param>
    /// <param name="warningMessage">Output warning if registered from a temporary directory</param>
    /// <returns>True if registration succeeded, false otherwise</returns>
    public static bool Register(string executablePath, out string? warningMessage)
    {
        warningMessage = null;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            warningMessage = "Executable file does not exist on disk.";
            return false;
        }

        // Check for temporary build/download folders
        string tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (executablePath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase))
        {
            warningMessage = "Notice: The executable is located in a temporary directory. " +
                             "If Windows cleans temporary files or you move the folder, startup will fail. " +
                             "Move the app to a permanent folder (e.g., C:\\Tools\\NetSpeedMonitor) for best results.";
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
            if (key == null)
            {
                warningMessage = "Unable to open HKCU Run registry key.";
                return false;
            }

            // Always wrap in double quotes for safe path parsing
            string quotedPath = $"\"{executablePath}\"";
            key.SetValue(ValueName, quotedPath, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            warningMessage = $"Registry access error: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Unregisters the app from HKCU Run key.
    /// </summary>
    public static bool Unregister(out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistrySubKey, writable: true);
            if (key == null) return true;

            if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Registry error while unregistering: {ex.Message}";
            return false;
        }
    }
}
