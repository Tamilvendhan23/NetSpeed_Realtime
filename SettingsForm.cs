using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetSpeedMonitor;

/// <summary>
/// Preferences dialog for NetSpeed Monitor status bar.
///
/// Layout notes (why this no longer breaks on high-DPI screens):
///   * No hardcoded pixel positions. Every row lives in a TableLayoutPanel whose rows/columns are AutoSize,
///     so labels, dropdowns and buttons are always placed relative to the real text size.
///   * The form itself is AutoSize (GrowAndShrink), so the window always fits its content at 100%–300% scaling.
///   * AutoScaleMode.Font with a 9pt Segoe UI base (7x15) is the standard WinForms setup for PerMonitorV2 DPI.
/// </summary>
public sealed class SettingsForm : Form
{
    // ---- Palette -------------------------------------------------------------------------------------------
    private static readonly Color BackgroundColor = Color.FromArgb(24, 24, 28);
    private static readonly Color HeaderColor = Color.FromArgb(32, 32, 38);
    private static readonly Color InputColor = Color.FromArgb(38, 38, 44);
    private static readonly Color DividerColor = Color.FromArgb(55, 55, 66);
    private static readonly Color MutedColor = Color.FromArgb(160, 160, 175);
    private static readonly Color AccentColor = Color.FromArgb(34, 197, 94);

    // Makes the Windows 10/11 title bar dark so it matches the dialog.
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    // ---- State ---------------------------------------------------------------------------------------------
    private readonly AppSettings _settings;
    private readonly Action _onSettingsUpdated;
    private readonly Action? _onResetPosition;

    private readonly CheckBox _chkAlwaysOnTop = new();
    private readonly CheckBox _chkLockPosition = new();
    private readonly CheckBox _chkShowDownload = new();
    private readonly CheckBox _chkShowUpload = new();
    private readonly CheckBox _chkStartWithWindows = new();
    private readonly ComboBox _cboDisplayUnit = new();
    private readonly ComboBox _cboTextColor = new();
    private readonly ComboBox _cboRefreshInterval = new();
    private readonly ComboBox _cboAdapters = new();
    private readonly Label _lblStartupStatus = new();
    private readonly Button _btnResetPosition = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnClose = new();

    private readonly Font _fontTitle = new("Segoe UI Semibold", 11f, FontStyle.Bold);
    private readonly Font _fontSection = new("Segoe UI Semibold", 8.5f, FontStyle.Bold);
    private readonly Font _fontSmall = new("Segoe UI", 8.5f, FontStyle.Regular);
    private readonly Font _fontButtonBold = new("Segoe UI Semibold", 9f, FontStyle.Bold);

    private TableLayoutPanel _grid = null!;

    public SettingsForm(AppSettings settings, Action onSettingsUpdated, Action? onResetPosition = null)
    {
        _settings = settings;
        _onSettingsUpdated = onSettingsUpdated;
        _onResetPosition = onResetPosition;

        InitializeComponent();
        LoadValues();

        // Wire change handlers AFTER the initial values are loaded, so populating the form
        // does not trigger them (previously this made the startup status label say
        // "Will register upon saving" the moment the dialog opened).
        _chkStartWithWindows.CheckedChanged += (_, _) => UpdateStartupStatus();
        _chkShowDownload.CheckedChanged += ShowDirection_CheckedChanged;
        _chkShowUpload.CheckedChanged += ShowDirection_CheckedChanged;
        UpdateStartupStatus();
    }

    // =====================================================================================================
    // Layout
    // =====================================================================================================

    private void InitializeComponent()
    {
        SuspendLayout();

        // DPI / sizing: scale by font metrics and let the form size itself to its content.
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        Text = "NetSpeed Monitor Preferences";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackgroundColor;
        ForeColor = Color.WhiteSmoke;

        // ---- Root: header / content / footer ---------------------------------------------------------
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = BackgroundColor,
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Header
        var lblTitle = new Label
        {
            Text = "Status Bar Preferences",
            Font = _fontTitle,
            ForeColor = Color.FromArgb(240, 240, 245),
            BackColor = HeaderColor,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 12),
            Margin = Padding.Empty,
            UseMnemonic = false
        };
        root.Controls.Add(lblTitle, 0, 0);

        // Content grid: column 0 = labels, column 1 = inputs
        _grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 4, 20, 14),
            BackColor = BackgroundColor,
            Margin = Padding.Empty
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(_grid, 0, 1);

        // ---- Controls ------------------------------------------------------------------------------
        StyleCombo(_cboDisplayUnit);
        StyleCombo(_cboTextColor);
        StyleCombo(_cboRefreshInterval);
        StyleCombo(_cboAdapters);

        StyleCheck(_chkShowDownload, "Show download speed (↓)");
        StyleCheck(_chkShowUpload, "Show upload speed (↑)");
        StyleCheck(_chkAlwaysOnTop, "Keep status bar always on top");
        StyleCheck(_chkLockPosition, "Lock position (disable drag & drop)");
        StyleCheck(_chkStartWithWindows, "Start NetSpeed Monitor with Windows");

        _lblStartupStatus.AutoSize = true;
        _lblStartupStatus.Anchor = AnchorStyles.Left;
        _lblStartupStatus.ForeColor = MutedColor;
        _lblStartupStatus.Font = _fontSmall;
        _lblStartupStatus.Margin = new Padding(24, 0, 0, 6);
        _lblStartupStatus.UseMnemonic = false;

        _btnResetPosition.Text = "Reset position to bottom-right";
        _btnResetPosition.AutoSize = true;
        _btnResetPosition.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _btnResetPosition.Anchor = AnchorStyles.Left;
        _btnResetPosition.Padding = new Padding(10, 3, 10, 3);
        _btnResetPosition.Margin = new Padding(0, 6, 0, 0);
        StyleButton(_btnResetPosition, Color.FromArgb(34, 34, 42), Color.FromArgb(200, 200, 220), Color.FromArgb(60, 60, 75), Font);
        _btnResetPosition.Click += BtnResetPosition_Click;

        // ---- Sections (ordered to match the feature set) ---------------------------------------------
        AddSection("Display", first: true);
        AddLabeledRow("Speed unit", _cboDisplayUnit);
        AddLabeledRow("Text color", _cboTextColor);
        AddSpanning(_chkShowDownload);
        AddSpanning(_chkShowUpload);

        AddSection("Behavior");
        AddSpanning(_chkAlwaysOnTop);
        AddSpanning(_chkLockPosition);
        AddLabeledRow("Refresh rate", _cboRefreshInterval);

        AddSection("Network");
        AddLabeledRow("Interface", _cboAdapters);

        AddSection("Startup & position");
        AddSpanning(_chkStartWithWindows);
        AddSpanning(_lblStartupStatus);
        AddSpanning(_btnResetPosition);

        // ---- Footer buttons (right-aligned) ----------------------------------------------------------
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = HeaderColor,
            Padding = new Padding(16, 12, 16, 12),
            Margin = Padding.Empty
        };

        _btnClose.Text = "Close";
        _btnClose.DialogResult = DialogResult.Cancel; // closes the dialog without saving
        _btnClose.Click += (_, _) => Close();
        StyleFooterButton(_btnClose, Color.FromArgb(45, 45, 55), Color.WhiteSmoke, Color.FromArgb(70, 70, 85), Font);

        _btnSave.Text = "Save && Apply"; // '&&' renders a literal '&' (single '&' would be a mnemonic)
        // NOTE: no DialogResult here on purpose. A button with DialogResult closes the form *before*
        // the Click handler can reject invalid input. Save closes the dialog itself on success.
        StyleFooterButton(_btnSave, AccentColor, Color.Black, AccentColor, _fontButtonBold);
        _btnSave.Click += BtnSave_Click;

        // RightToLeft flow: first added = right-most.
        footer.Controls.Add(_btnClose);
        footer.Controls.Add(_btnSave);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);

        // No AcceptButton on purpose: pressing Enter on a dropdown/checkbox used to Save and close the dialog
        // unexpectedly. Esc still closes; Save & Apply must be clicked deliberately.
        CancelButton = _btnClose;

        ResumeLayout(performLayout: true);
    }

    private int NextRow()
    {
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _grid.RowCount = _grid.RowStyles.Count;
        return _grid.RowCount - 1;
    }

    private void AddSection(string title, bool first = false)
    {
        var lbl = new Label
        {
            Text = title.ToUpperInvariant(),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = AccentColor,
            Font = _fontSection,
            Margin = new Padding(0, first ? 10 : 18, 0, 4),
            UseMnemonic = false
        };
        AddSpanning(lbl);

        var line = new Panel
        {
            Height = 1,
            Dock = DockStyle.Fill,
            BackColor = DividerColor,
            Margin = new Padding(0, 0, 0, 6)
        };
        AddSpanning(line);
    }

    private void AddSpanning(Control control)
    {
        int row = NextRow();
        _grid.Controls.Add(control, 0, row);
        _grid.SetColumnSpan(control, 2);
    }

    private void AddLabeledRow(string labelText, Control input)
    {
        int row = NextRow();
        var lbl = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left, // Left only => vertically centred against the taller dropdown
            Margin = new Padding(0, 0, 18, 0),
            UseMnemonic = false
        };
        _grid.Controls.Add(lbl, 0, row);
        _grid.Controls.Add(input, 1, row);
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = InputColor;
        combo.ForeColor = Color.White;
        combo.Width = 290; // scaled automatically with the font/DPI
        combo.Anchor = AnchorStyles.Left;
        combo.Margin = new Padding(0, 4, 0, 4);
        combo.MaxDropDownItems = 10;
    }

    private static void StyleCheck(CheckBox check, string text)
    {
        check.Text = text;
        check.AutoSize = true;
        check.Anchor = AnchorStyles.Left;
        check.Margin = new Padding(0, 4, 0, 4);
        check.UseMnemonic = false;
    }

    private static void StyleButton(Button button, Color back, Color fore, Color border, Font font)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = 1;
        button.UseVisualStyleBackColor = false;
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = font;
    }

    private static void StyleFooterButton(Button button, Color back, Color fore, Color border, Font font)
    {
        StyleButton(button, back, fore, border, font);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(96, 32);
        button.Padding = new Padding(10, 3, 10, 3);
        button.Margin = new Padding(8, 0, 0, 0);
    }

    // =====================================================================================================
    // Window lifecycle
    // =====================================================================================================

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int on = 1;
            DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
        }
        catch
        {
            // Older Windows builds: keep the default title bar.
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Run after DPI scaling so the measurements use the final font/width.
        FitDropDownWidth(_cboAdapters);
        FitDropDownWidth(_cboDisplayUnit);
        FitDropDownWidth(_cboTextColor);
        FitDropDownWidth(_cboRefreshInterval);

        // Bring to the front once WITHOUT staying topmost, so message boxes can never end up behind it.
        TopMost = true;
        TopMost = false;
        Activate();
    }

    /// <summary>Widen the popup list so long adapter names are never cut off.</summary>
    private static void FitDropDownWidth(ComboBox combo)
    {
        int widest = combo.Width;
        foreach (object item in combo.Items)
        {
            int w = TextRenderer.MeasureText(item?.ToString() ?? string.Empty, combo.Font).Width;
            if (w > widest) widest = w;
        }

        combo.DropDownWidth = widest + SystemInformation.VerticalScrollBarWidth + 12;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fontTitle.Dispose();
            _fontSection.Dispose();
            _fontSmall.Dispose();
            _fontButtonBold.Dispose();
        }
        base.Dispose(disposing);
    }

    // =====================================================================================================
    // Data
    // =====================================================================================================

    private void LoadValues()
    {
        _chkAlwaysOnTop.Checked = _settings.AlwaysOnTop;
        _chkLockPosition.Checked = _settings.LockPosition;
        _chkShowDownload.Checked = _settings.ShowDownload;
        _chkShowUpload.Checked = _settings.ShowUpload;
        _chkStartWithWindows.Checked = StartupManager.IsRegistered();

        // Display unit
        _cboDisplayUnit.Items.Clear();
        _cboDisplayUnit.Items.AddRange(new object[] { "Automatic", "Mbps", "MB/s", "Kbps", "KB/s", "bps", "B/s" });
        int unitIndex = _cboDisplayUnit.FindStringExact(_settings.DisplayUnit);
        _cboDisplayUnit.SelectedIndex = unitIndex >= 0 ? unitIndex : 0;

        // Text color (three options only)
        _cboTextColor.Items.Clear();
        _cboTextColor.Items.AddRange(new object[] { "Green", "White", "Black" });
        int colorIndex = _cboTextColor.FindStringExact(_settings.TextColor);
        _cboTextColor.SelectedIndex = colorIndex >= 0 ? colorIndex : 0;

        // Refresh interval
        _cboRefreshInterval.Items.Clear();
        _cboRefreshInterval.Items.AddRange(new object[] { "1 second (Default)", "2 seconds", "5 seconds" });
        _cboRefreshInterval.SelectedIndex = _settings.RefreshIntervalSeconds switch
        {
            2 => 1,
            5 => 2,
            _ => 0
        };

        // Network adapters (distinct IDs only)
        var adapters = NetworkSpeedTracker.GetAvailableAdapters();
        _cboAdapters.Items.Clear();
        int selectedAdapterIndex = 0;
        var seenAdapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in adapters)
        {
            if (!seenAdapterIds.Add(item.Id))
            {
                continue;
            }

            int addedIndex = _cboAdapters.Items.Add(item);
            if (!string.IsNullOrEmpty(_settings.SelectedAdapterId) && item.Id == _settings.SelectedAdapterId)
            {
                selectedAdapterIndex = addedIndex;
            }
        }

        if (_cboAdapters.Items.Count > 0)
        {
            _cboAdapters.SelectedIndex = selectedAdapterIndex;
        }
    }

    /// <summary>Compares the real registry state with the checkbox so the status text is always truthful.</summary>
    private void UpdateStartupStatus()
    {
        bool registered = StartupManager.IsRegistered();
        bool wanted = _chkStartWithWindows.Checked;

        _lblStartupStatus.Text = (registered, wanted) switch
        {
            (true, true) => "Currently enabled for your Windows account.",
            (false, false) => "Currently disabled.",
            (false, true) => "Will be enabled when you click Save & Apply.",
            (true, false) => "Will be disabled when you click Save & Apply."
        };
    }

    /// <summary>At least one of download/upload must stay visible (same rule as the tray menu).</summary>
    private void ShowDirection_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is CheckBox changed && !_chkShowDownload.Checked && !_chkShowUpload.Checked)
        {
            changed.Checked = true;
        }
    }

    // =====================================================================================================
    // Actions
    // =====================================================================================================

    private void BtnResetPosition_Click(object? sender, EventArgs e)
    {
        if (_onResetPosition != null)
        {
            // Moves the status bar right now (and saves the new coordinates).
            _onResetPosition();
        }
        else
        {
            _settings.WindowLocationX = -1;
            _settings.WindowLocationY = -1;
            _settings.Save();
            _onSettingsUpdated();
        }

        MessageBox.Show(this, "Status bar moved to the bottom-right corner of the screen.", "Position Reset",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (!_chkShowDownload.Checked && !_chkShowUpload.Checked)
        {
            MessageBox.Show(this, "Please select at least one direction (Download or Upload).", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return; // dialog stays open
        }

        try
        {
            _settings.AlwaysOnTop = _chkAlwaysOnTop.Checked;
            _settings.LockPosition = _chkLockPosition.Checked;
            _settings.ShowDownload = _chkShowDownload.Checked;
            _settings.ShowUpload = _chkShowUpload.Checked;
            _settings.DisplayUnit = _cboDisplayUnit.SelectedItem?.ToString() ?? "Automatic";
            _settings.TextColor = _cboTextColor.SelectedItem?.ToString() ?? "Green";
            _settings.RefreshIntervalSeconds = _cboRefreshInterval.SelectedIndex switch
            {
                1 => 2,
                2 => 5,
                _ => 1
            };

            if (_cboAdapters.SelectedItem is NetworkAdapterInfo selectedAdapter)
            {
                _settings.SelectedAdapterId = selectedAdapter.Id;
            }

            if (_chkStartWithWindows.Checked)
            {
                string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                bool registered = StartupManager.Register(exePath, out string? warning);
                _settings.StartWithWindows = registered;

                if (!string.IsNullOrEmpty(warning))
                {
                    MessageBox.Show(this, warning, "NetSpeed Monitor Startup", MessageBoxButtons.OK,
                        registered ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }
            else
            {
                StartupManager.Unregister(out _);
                _settings.StartWithWindows = false;
            }

            _settings.Save();
        }
        catch (Exception ex)
        {
            // Nothing was persisted: stay open so the user can retry.
            MessageBox.Show(this, $"Error saving settings: {ex.Message}", "Settings", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // Settings are saved. Refreshing the status bar must never keep the dialog from closing.
        try
        {
            _onSettingsUpdated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Settings were saved, but the status bar could not refresh: {ex.Message}",
                "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
