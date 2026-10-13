using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetSpeedMonitor;

/// <summary>
/// Minimal transparent desktop status bar for NetSpeed Monitor.
/// Displays ONLY download and upload rates in a slim, borderless, draggable overlay.
/// All controls and preferences are managed exclusively through the system tray.
/// </summary>
public sealed class MainForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);

    internal const string WindowTitle = "NetSpeed Monitor";
    private const uint MB_TOPMOST = 0x00040000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    // Private message used by a second launch to ask the running instance to show its bar.
    private static readonly uint ShowBarMessage = RegisterWindowMessage("NetSpeedMonitor_ShowBar_9921");

    /// <summary>Called by a second process launch: asks the running instance to show its status bar.</summary>
    internal static void RequestShowExistingInstance()
    {
        nint hwnd = FindWindow(null, WindowTitle);
        if (hwnd != nint.Zero)
        {
            PostMessage(hwnd, ShowBarMessage, nint.Zero, nint.Zero);
        }
    }

    /// <summary>
    /// Message box that is always on top and has no owner, so it can never be hidden behind
    /// the status bar or a dialog (a hidden modal box makes the whole app look frozen).
    /// </summary>
    internal static void ShowTopMostMessage(string text, MessageBoxIcon icon)
    {
        MessageBox.Show(text, WindowTitle, MessageBoxButtons.OK, icon,
            MessageBoxDefaultButton.Button1, (MessageBoxOptions)MB_TOPMOST);
    }

    private readonly AppSettings _settings;
    private readonly NetworkSpeedTracker _tracker;
    private readonly System.Windows.Forms.Timer _timer;

    // Current formatted readings
    private string _currentDownloadText = "↓ 0 Mbps";
    private string _currentUploadText = "↑ 0 Mbps";

    // Styling
    private readonly Font _statusFont;

    // System Tray
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _menuItemToggleVisible = new();
    private readonly ToolStripMenuItem _menuItemUnits = new();
    private readonly ToolStripMenuItem _menuItemColor = new();
    private readonly ToolStripMenuItem _menuItemShowDl = new();
    private readonly ToolStripMenuItem _menuItemShowUl = new();
    private readonly ToolStripMenuItem _menuItemInterval = new();
    private readonly ToolStripMenuItem _menuItemAlwaysOnTop = new();
    private readonly ToolStripMenuItem _menuItemPosition = new();
    private readonly ToolStripMenuItem _menuItemLockPosition = new();
    private readonly ToolStripMenuItem _menuItemStartup = new();
    private readonly ToolStripMenuItem _menuItemPreferences = new();
    private readonly ToolStripMenuItem _menuItemExit = new();

    private bool _isExplicitExitRequested = false;

    public MainForm()
    {
        _settings = AppSettings.Load();
        _tracker = new NetworkSpeedTracker();
        _statusFont = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);

        // WinForms high performance & double buffering
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);

        InitializeStatusBar();
        InitializeTrayIcon();

        // Initial sample
        _tracker.SetSelectedAdapter(_settings.SelectedAdapterId);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(500, _settings.RefreshIntervalSeconds * 1000)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
    }

    private bool HasSavedLocation => !(_settings.WindowLocationX == -1 && _settings.WindowLocationY == -1);

    private void InitializeStatusBar()
    {
        Text = WindowTitle; // never visible (borderless) but lets a second launch find this window
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = _settings.AlwaysOnTop;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Opacity = 1.0;
        ApplyLockState();

        // Auto calculate initial size
        UpdateBarLayout();
        RestorePositionOrPlaceDefault();

        Visible = _settings.IsStatusBarVisible;
    }

    /// <summary>
    /// Application.Run() forces the main form visible; honour the saved "hidden to tray" state instead.
    /// The handle is created even when hidden so a second launch can still find and wake this window.
    /// </summary>
    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated)
        {
            CreateHandle();
        }

        base.SetVisibleCore(value && _settings.IsStatusBarVisible);
    }

    protected override void WndProc(ref Message m)
    {
        if (ShowBarMessage != 0 && m.Msg == (int)ShowBarMessage)
        {
            ShowStatusBar();
            return;
        }

        base.WndProc(ref m);
    }

    private void ShowStatusBar()
    {
        _settings.IsStatusBarVisible = true;
        Visible = true;
        _menuItemToggleVisible.Text = "Hide Status Bar";
        _settings.Save();
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon.Text = "NetSpeed Monitor";
        _notifyIcon.Icon = CreateDefaultTrayIcon();
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (s, e) => ToggleVisibility();

        WireTrayMenuHandlers();
        BuildTrayMenu();
        _notifyIcon.ContextMenuStrip = _trayMenu;
    }

    /// <summary>
    /// Subscribes the click handlers of the persistent tray items exactly once.
    /// BuildTrayMenu() runs after every settings change; subscribing inside it made the handlers
    /// stack up (e.g. "Preferences..." opened N dialogs and "Show Download" toggled N times).
    /// </summary>
    private void WireTrayMenuHandlers()
    {
        _menuItemToggleVisible.Click += (s, e) => ToggleVisibility();
        _menuItemShowDl.Click += (s, e) => ToggleShowDownload();
        _menuItemShowUl.Click += (s, e) => ToggleShowUpload();
        _menuItemAlwaysOnTop.Click += (s, e) => ToggleAlwaysOnTop();
        _menuItemStartup.Click += (s, e) => ToggleStartupRegistration();
        _menuItemLockPosition.Click += (s, e) => ToggleLockPosition();
        _menuItemPreferences.Click += (s, e) => OpenPreferences();
        _menuItemExit.Click += (s, e) => ExitApplication();
    }

    private void BuildTrayMenu()
    {
        _trayMenu.Items.Clear();

        // 1. Show/Hide Status Bar
        _menuItemToggleVisible.Text = _settings.IsStatusBarVisible ? "Hide Status Bar" : "Show Status Bar";
        _trayMenu.Items.Add(_menuItemToggleVisible);

        // 2. Display Units Submenu
        _menuItemUnits.DropDownItems.Clear();
        _menuItemUnits.Text = "Display Units";
        var units = new[] { "Mbps", "MB/s", "Kbps", "KB/s", "bps", "B/s", "Automatic" };
        foreach (var u in units)
        {
            var item = new ToolStripMenuItem(u)
            {
                Checked = string.Equals(_settings.DisplayUnit, u, StringComparison.OrdinalIgnoreCase)
            };
            string capturedUnit = u;
            item.Click += (s, e) => SetDisplayUnit(capturedUnit);
            _menuItemUnits.DropDownItems.Add(item);
        }
        _trayMenu.Items.Add(_menuItemUnits);

        // Speed Color Submenu (three options only: Green, White, Black)
        _menuItemColor.DropDownItems.Clear();
        _menuItemColor.Text = "Text Color";
        var colors = new[] { "Green", "White", "Black" };
        foreach (var c in colors)
        {
            var item = new ToolStripMenuItem(c)
            {
                Checked = string.Equals(_settings.TextColor, c, StringComparison.OrdinalIgnoreCase)
            };
            string capturedColor = c;
            item.Click += (s, e) => SetTextColor(capturedColor);
            _menuItemColor.DropDownItems.Add(item);
        }
        _trayMenu.Items.Add(_menuItemColor);

        _trayMenu.Items.Add(new ToolStripSeparator());

        // 3. Show Download
        _menuItemShowDl.Text = "Show Download";
        _menuItemShowDl.Checked = _settings.ShowDownload;
        _trayMenu.Items.Add(_menuItemShowDl);

        // 4. Show Upload
        _menuItemShowUl.Text = "Show Upload";
        _menuItemShowUl.Checked = _settings.ShowUpload;
        _trayMenu.Items.Add(_menuItemShowUl);

        _trayMenu.Items.Add(new ToolStripSeparator());

        // 5. Refresh Interval Submenu
        _menuItemInterval.DropDownItems.Clear();
        _menuItemInterval.Text = "Refresh Interval";
        var intervals = new[] { (1, "1 second (Default)"), (2, "2 seconds"), (5, "5 seconds") };
        foreach (var (sec, label) in intervals)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = _settings.RefreshIntervalSeconds == sec
            };
            int capturedSec = sec;
            item.Click += (s, e) => SetRefreshInterval(capturedSec);
            _menuItemInterval.DropDownItems.Add(item);
        }
        _trayMenu.Items.Add(_menuItemInterval);

        // 6. Always on Top
        _menuItemAlwaysOnTop.Text = "Always on Top";
        _menuItemAlwaysOnTop.Checked = _settings.AlwaysOnTop;
        _trayMenu.Items.Add(_menuItemAlwaysOnTop);

        // 7. Position Submenu
        _menuItemPosition.DropDownItems.Clear();
        _menuItemPosition.Text = "Position";
        var positions = new[] { "Bottom Right", "Bottom Left", "Top Right", "Top Left" };
        foreach (var pos in positions)
        {
            var item = new ToolStripMenuItem(pos);
            string capturedPos = pos;
            item.Click += (s, e) => SnapToScreenPosition(capturedPos);
            _menuItemPosition.DropDownItems.Add(item);
        }
        _trayMenu.Items.Add(_menuItemPosition);

        // 7b. Lock Position (disables drag & drop of the status bar)
        _menuItemLockPosition.Text = "Lock Position";
        _menuItemLockPosition.Checked = _settings.LockPosition;
        _trayMenu.Items.Add(_menuItemLockPosition);

        // 8. Start with Windows
        _menuItemStartup.Text = "Start with Windows";
        _menuItemStartup.Checked = StartupManager.IsRegistered();
        _trayMenu.Items.Add(_menuItemStartup);

        _trayMenu.Items.Add(new ToolStripSeparator());

        // 9. Preferences...
        _menuItemPreferences.Text = "Preferences...";
        _trayMenu.Items.Add(_menuItemPreferences);

        // 10. Exit
        _menuItemExit.Text = "Exit";
        _trayMenu.Items.Add(_menuItemExit);
    }

    private Icon CreateDefaultTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color arrowColor = _settings.TextColor switch
            {
                "White" => Color.White,
                "Black" => Color.FromArgb(15, 15, 15),
                _ => Color.FromArgb(34, 197, 94) // Green
            };

            Color circleBg = _settings.TextColor switch
            {
                "White" => Color.FromArgb(28, 28, 36),
                "Black" => Color.FromArgb(225, 225, 230),
                _ => Color.FromArgb(20, 26, 20)
            };

            using var brushBg = new SolidBrush(circleBg);
            g.FillEllipse(brushBg, 0, 0, 15, 15);

            using var pen = new Pen(arrowColor, 1.6f);
            // Down arrow (single color)
            g.DrawLine(pen, 4, 3, 4, 11);
            g.DrawLine(pen, 2, 9, 4, 11);
            g.DrawLine(pen, 6, 9, 4, 11);

            // Up arrow (same single color - no multicolor mixing)
            g.DrawLine(pen, 11, 12, 11, 4);
            g.DrawLine(pen, 9, 6, 11, 4);
            g.DrawLine(pen, 13, 6, 11, 4);
        }

        nint hIcon = bmp.GetHicon();
        Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var sample = _tracker.Sample();

        if (sample.IsMeasuring)
        {
            _currentDownloadText = "↓ Measuring…";
            _currentUploadText = "↑ Measuring…";
            UpdateBarLayout();
            Invalidate();
            return;
        }

        if (!sample.HasConnection)
        {
            string zero = NetworkSpeedTracker.FormatSpeedForUnit(0, _settings.DisplayUnit);
            _currentDownloadText = $"↓ {zero}";
            _currentUploadText = $"↑ {zero}";
            _notifyIcon.Text = "NetSpeed Monitor: Offline";
            UpdateBarLayout();
            Invalidate();
            return;
        }

        string dlSpeed = NetworkSpeedTracker.FormatSpeedForUnit(sample.DownloadBytesPerSec, _settings.DisplayUnit);
        string ulSpeed = NetworkSpeedTracker.FormatSpeedForUnit(sample.UploadBytesPerSec, _settings.DisplayUnit);

        _currentDownloadText = $"↓ {dlSpeed}";
        _currentUploadText = $"↑ {ulSpeed}";

        // Update system tray tooltip (Windows max 63 chars)
        string trayText = $"{_currentDownloadText}  {_currentUploadText}";
        if (trayText.Length > 63) trayText = trayText[..63];
        _notifyIcon.Text = trayText;

        UpdateBarLayout();
        Invalidate();
    }

    /// <summary>
    /// Recalculates minimal bar size based on active items and text measurements.
    /// Provides generous padding and dynamically scales with high DPI to prevent any text truncation.
    /// </summary>
    private void UpdateBarLayout()
    {
        int padX = 14;
        int gap = 20;

        using var g = CreateGraphics();
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

        int dlWidth = 0;
        int ulWidth = 0;

        if (_settings.ShowDownload)
        {
            var sz = g.MeasureString(_currentDownloadText, _statusFont);
            dlWidth = (int)Math.Ceiling(sz.Width) + 8;
        }

        if (_settings.ShowUpload)
        {
            var sz = g.MeasureString(_currentUploadText, _statusFont);
            ulWidth = (int)Math.Ceiling(sz.Width) + 8;
        }

        int contentWidth = 0;
        if (_settings.ShowDownload && _settings.ShowUpload)
        {
            contentWidth = dlWidth + gap + ulWidth;
        }
        else if (_settings.ShowDownload)
        {
            contentWidth = dlWidth;
        }
        else if (_settings.ShowUpload)
        {
            contentWidth = ulWidth;
        }
        else
        {
            contentWidth = (int)Math.Ceiling(g.MeasureString("NetSpeed", _statusFont).Width) + 8;
        }

        int targetWidth = contentWidth + (padX * 2);
        // Dynamic height scaling with system font metrics for OLED / 4K high DPI displays
        int targetHeight = Math.Max(36, _statusFont.Height + 14);

        if (Width != targetWidth || Height != targetHeight)
        {
            Rectangle work = Screen.FromControl(this)?.WorkingArea ?? Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            int prevRight = Right;

            ClientSize = new Size(targetWidth, targetHeight);

            if (HasSavedLocation && prevRight >= work.Right - 80)
            {
                Left = prevRight - targetWidth;
            }

            EnsureWithinWorkArea();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Magenta);
        // SingleBitPerPixelGridFit prevents ClearType subpixel color-bleeding against magenta transparency
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

        // Strictly single-color: Green, White, or Black (both download and upload share the same single color)
        Color activeColor = _settings.TextColor switch
        {
            "Black" => Color.FromArgb(15, 15, 15),
            "White" => Color.White,
            _ => Color.FromArgb(34, 197, 94) // Emerald Green
        };

        // Solid shadow color without alpha to avoid TransparencyKey magenta fringing
        Color shadowColor = _settings.TextColor switch
        {
            "Black" => Color.FromArgb(240, 240, 240),
            "White" => Color.FromArgb(10, 10, 12),
            _ => Color.FromArgb(10, 16, 10)
        };

        using var brushShadow = new SolidBrush(shadowColor);
        using var brushForeground = new SolidBrush(activeColor);

        int padX = 14;
        int gap = 20;
        int currentX = padX;
        int y = Math.Max(2, (ClientSize.Height - _statusFont.Height) / 2);

        if (_settings.ShowDownload)
        {
            // Drop shadow
            e.Graphics.DrawString(_currentDownloadText, _statusFont, brushShadow, currentX + 1, y + 1);
            // Crisp foreground in selected single color (NO MULTICOLOR)
            e.Graphics.DrawString(_currentDownloadText, _statusFont, brushForeground, currentX, y);

            var sz = e.Graphics.MeasureString(_currentDownloadText, _statusFont);
            currentX += (int)Math.Ceiling(sz.Width) + 8 + gap;
        }

        if (_settings.ShowUpload)
        {
            // Drop shadow
            e.Graphics.DrawString(_currentUploadText, _statusFont, brushShadow, currentX + 1, y + 1);
            // Crisp foreground in selected single color (NO MULTICOLOR)
            e.Graphics.DrawString(_currentUploadText, _statusFont, brushForeground, currentX, y);
        }

        if (!_settings.ShowDownload && !_settings.ShowUpload)
        {
            e.Graphics.DrawString("NetSpeed", _statusFont, brushShadow, padX + 1, y + 1);
            e.Graphics.DrawString("NetSpeed", _statusFont, brushForeground, padX, y);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            if (e.Clicks >= 2)
            {
                OpenPreferences();
                return;
            }

            // Locked: ignore drag attempts (double-click and right-click menu still work)
            if (_settings.LockPosition)
            {
                return;
            }

            // Smooth native dragging
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);

            _settings.WindowLocationX = Left;
            _settings.WindowLocationY = Top;
            _settings.Save();
        }
        else if (e.Button == MouseButtons.Right)
        {
            // Right clicking the status bar opens tray menu
            _trayMenu.Show(Cursor.Position);
        }
    }

    /// <summary>Move cursor only advertises dragging while the bar is actually draggable.</summary>
    private void ApplyLockState()
    {
        Cursor = _settings.LockPosition ? Cursors.Default : Cursors.SizeAll;
    }

    private void ToggleLockPosition()
    {
        _settings.LockPosition = !_settings.LockPosition;
        _settings.Save();
        ApplyLockState();
        BuildTrayMenu();
    }

    private void ToggleVisibility()
    {
        _settings.IsStatusBarVisible = !_settings.IsStatusBarVisible;
        Visible = _settings.IsStatusBarVisible;
        _menuItemToggleVisible.Text = _settings.IsStatusBarVisible ? "Hide Status Bar" : "Show Status Bar";
        _settings.Save();
    }

    private void SetDisplayUnit(string unit)
    {
        _settings.DisplayUnit = unit;
        _settings.Save();
        BuildTrayMenu();

        // Refresh immediately
        Timer_Tick(null, EventArgs.Empty);
    }

    private void SetTextColor(string color)
    {
        if (color is not ("Green" or "White" or "Black")) return;
        _settings.TextColor = color;
        _settings.Save();
        _notifyIcon.Icon = CreateDefaultTrayIcon();
        BuildTrayMenu();
        UpdateBarLayout();
        Invalidate();
    }

    private void ToggleShowDownload()
    {
        if (_settings.ShowDownload && !_settings.ShowUpload)
        {
            // Avoid hiding both
            return;
        }

        _settings.ShowDownload = !_settings.ShowDownload;
        _settings.Save();
        BuildTrayMenu();
        UpdateBarLayout();
        Invalidate();
    }

    private void ToggleShowUpload()
    {
        if (_settings.ShowUpload && !_settings.ShowDownload)
        {
            // Avoid hiding both
            return;
        }

        _settings.ShowUpload = !_settings.ShowUpload;
        _settings.Save();
        BuildTrayMenu();
        UpdateBarLayout();
        Invalidate();
    }

    private void SetRefreshInterval(int seconds)
    {
        _settings.RefreshIntervalSeconds = seconds;
        _timer.Interval = Math.Max(500, seconds * 1000);
        _settings.Save();
        BuildTrayMenu();
    }

    private void ToggleAlwaysOnTop()
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        TopMost = _settings.AlwaysOnTop;
        _settings.Save();
        BuildTrayMenu();
    }

    private void SnapToScreenPosition(string position)
    {
        Rectangle work = Screen.FromControl(this)?.WorkingArea ?? Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int margin = 16;

        Point pt = position switch
        {
            "Bottom Left" => new Point(work.Left + margin, work.Bottom - Height - margin),
            "Top Right" => new Point(work.Right - Width - margin, work.Top + margin),
            "Top Left" => new Point(work.Left + margin, work.Top + margin),
            _ => new Point(work.Right - Width - margin, work.Bottom - Height - margin) // Bottom Right default
        };

        Location = pt;
        _settings.WindowLocationX = Left;
        _settings.WindowLocationY = Top;
        _settings.Save();
    }

    private void ToggleStartupRegistration()
    {
        string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
        bool isRegistered = StartupManager.IsRegistered();

        if (isRegistered)
        {
            StartupManager.Unregister(out _);
            _settings.StartWithWindows = false;
        }
        else
        {
            bool success = StartupManager.Register(exePath, out string? warning);
            if (!string.IsNullOrEmpty(warning))
            {
                ShowTopMostMessage(warning, MessageBoxIcon.Information);
            }
            _settings.StartWithWindows = success;
        }

        _settings.Save();
        BuildTrayMenu();
    }

    private bool _isPreferencesOpen;

    private void OpenPreferences()
    {
        // The tray menu stays clickable while the modal dialog is open; never stack a second dialog.
        if (_isPreferencesOpen) return;
        _isPreferencesOpen = true;

        try
        {
            using var dlg = new SettingsForm(
                _settings,
                OnPreferencesUpdated,
                onResetPosition: () => SnapToScreenPosition("Bottom Right"));
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowTopMostMessage($"Unable to open preferences: {ex.Message}", MessageBoxIcon.Error);
        }
        finally
        {
            _isPreferencesOpen = false;
        }
    }

    private void OnPreferencesUpdated()
    {
        TopMost = _settings.AlwaysOnTop;
        ApplyLockState();
        _timer.Interval = Math.Max(500, _settings.RefreshIntervalSeconds * 1000);
        _tracker.SetSelectedAdapter(_settings.SelectedAdapterId);
        _tracker.ResetBaseline();

        _notifyIcon.Icon = CreateDefaultTrayIcon();
        BuildTrayMenu();
        UpdateBarLayout();
        Invalidate();
    }

    private void RestorePositionOrPlaceDefault()
    {
        Rectangle workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);

        if (HasSavedLocation)
        {
            Location = new Point(_settings.WindowLocationX, _settings.WindowLocationY);
            EnsureWithinWorkArea();
        }
        else
        {
            // Default placement: Bottom-Right, above taskbar
            int x = workArea.Right - Width - 16;
            int y = workArea.Bottom - Height - 16;
            Location = new Point(Math.Max(workArea.Left, x), Math.Max(workArea.Top, y));
        }
    }

    private void EnsureWithinWorkArea()
    {
        Screen? currentScreen = Screen.FromControl(this) ?? Screen.PrimaryScreen;
        if (currentScreen == null) return;

        Rectangle area = currentScreen.WorkingArea;
        int x = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(x, y);
    }

    private void ExitApplication()
    {
        _isExplicitExitRequested = true;
        _timer.Stop();
        _notifyIcon.Visible = false; // remove the tray icon right away; Dispose() releases everything once
        _settings.Save();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExplicitExitRequested)
        {
            // Close hides to tray
            e.Cancel = true;
            _settings.IsStatusBarVisible = false;
            Visible = false;
            _menuItemToggleVisible.Text = "Show Status Bar";
            _settings.Save();
            return;
        }

        base.OnFormClosing(e);
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            _tracker.ResetBaseline();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Dispose();
            _notifyIcon?.Dispose();
            _trayMenu?.Dispose();
            _statusFont?.Dispose();
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        }
        base.Dispose(disposing);
    }
}
