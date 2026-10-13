using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetSpeedMonitor;

/// <summary>
/// Manages application preferences saved locally at %LOCALAPPDATA%\NetSpeedMonitor\settings.json.
/// Safely falls back to defaults if settings are missing or corrupted.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Keep the minimal status bar on top of other windows.
    /// </summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Active display unit mode: "Automatic", "Mbps", "MB/s", "Kbps", "KB/s", "bps", "B/s".
    /// </summary>
    public string DisplayUnit { get; set; } = "Automatic";

    /// <summary>
    /// Whether to display download speed.
    /// </summary>
    public bool ShowDownload { get; set; } = true;

    /// <summary>
    /// Whether to display upload speed.
    /// </summary>
    public bool ShowUpload { get; set; } = true;

    /// <summary>
    /// Refresh interval in seconds (default: 1, supports 1, 2, 5).
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 1;

    /// <summary>
    /// Color theme for speed readings: "Green", "Black", or "White" (three options only).
    /// </summary>
    public string TextColor { get; set; } = "Green";

    /// <summary>
    /// Identifier of the selected network adapter ("" or "AUTO" for Automatic active network).
    /// </summary>
    public string SelectedAdapterId { get; set; } = string.Empty;

    /// <summary>
    /// Saved desktop X position (-1 means default bottom-right).
    /// </summary>
    public int WindowLocationX { get; set; } = -1;

    /// <summary>
    /// Saved desktop Y position (-1 means default bottom-right).
    /// </summary>
    public int WindowLocationY { get; set; } = -1;

    /// <summary>
    /// When true, the status bar cannot be dragged (position stays fixed until unlocked).
    /// </summary>
    public bool LockPosition { get; set; } = false;

    /// <summary>
    /// Whether Start with Windows is configured via HKCU Run key.
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Whether the status bar is currently visible.
    /// </summary>
    public bool IsStatusBarVisible { get; set; } = true;

    /// <summary>
    /// Gets the absolute path to settings.json in %LOCALAPPDATA%\NetSpeedMonitor\.
    /// </summary>
    public static string GetSettingsFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(localAppData, "NetSpeedMonitor");
        return Path.Combine(folder, "settings.json");
    }

    /// <summary>
    /// Loads settings from disk. Returns safe defaults on missing or corrupted file.
    /// </summary>
    public static AppSettings Load()
    {
        string path = GetSettingsFilePath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    // Validate refresh interval bounds
                    if (loaded.RefreshIntervalSeconds is not (1 or 2 or 5))
                    {
                        loaded.RefreshIntervalSeconds = 1;
                    }

                    // Validate unit mode
                    if (string.IsNullOrWhiteSpace(loaded.DisplayUnit))
                    {
                        loaded.DisplayUnit = "Automatic";
                    }

                    // Validate text color option (strictly "Green", "Black", or "White")
                    if (loaded.TextColor is not ("Green" or "Black" or "White"))
                    {
                        loaded.TextColor = "Green";
                    }

                    // Ensure at least one metric is enabled
                    if (!loaded.ShowDownload && !loaded.ShowUpload)
                    {
                        loaded.ShowDownload = true;
                        loaded.ShowUpload = true;
                    }

                    return loaded;
                }
            }
        }
        catch
        {
            // Fail gracefully on any I/O or JSON deserialization error
        }

        return new AppSettings();
    }

    /// <summary>
    /// Saves current settings to disk atomically.
    /// </summary>
    public void Save()
    {
        string path = GetSettingsFilePath();
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(this, JsonOptions);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Do not crash if unable to write to disk
        }
    }
}
