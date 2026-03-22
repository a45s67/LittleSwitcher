namespace LittleSwitcher;

public class KeyTextBox : TextBox
{
    public uint RecordedKey { get; private set; }

    public KeyTextBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
    }

    public void SetKey(uint key)
    {
        RecordedKey = key;
        Text = HotkeyBinding.KeyToString(key);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;

        // Ignore modifier-only presses
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return true;

        RecordedKey = (uint)key;
        Text = HotkeyBinding.KeyToString(RecordedKey);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }
}

public class MainWindow : Form
{
    private readonly HotkeyConfig _config;
    private readonly FocusHistory _focusHistory;
    private readonly Action<HotkeyConfig> _onSave;

    private KeyTextBox _txtCycleWindows = null!;
    private KeyTextBox _txtFocusOtherMonitor = null!;
    private KeyTextBox _txtLastDesktop = null!;
    private KeyTextBox _txtToggleManagement = null!;
    private ComboBox _cboModifier = null!;
    private TextBox _statusTextBox = null!;
    private System.Windows.Forms.Timer _updateTimer = null!;

    public MainWindow(HotkeyConfig config, FocusHistory focusHistory, Action<HotkeyConfig> onSave)
    {
        _config = config;
        _focusHistory = focusHistory;
        _onSave = onSave;
        InitUI();
    }

    private void InitUI()
    {
        Text = "LittleSwitcher";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 380);
        MinimumSize = new Size(500, 380);
        ShowInTaskbar = true;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var settingsTab = new TabPage("Settings");
        BuildSettingsTab(settingsTab);
        tabs.TabPages.Add(settingsTab);

        var statusTab = new TabPage("Status");
        BuildStatusTab(statusTab);
        tabs.TabPages.Add(statusTab);

        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab == statusTab)
            {
                _updateTimer.Start();
                UpdateStatus();
            }
            else
            {
                _updateTimer.Stop();
            }
        };

        Controls.Add(tabs);
    }

    private void BuildSettingsTab(TabPage tab)
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        const int labelX = 16;
        const int inputX = 240;
        const int inputW = 220;
        const int rowH = 34;
        int y = 16;

        // Modifier dropdown first
        var lblMod = new Label
        {
            Text = "Modifier Key:",
            Location = new Point(labelX, y + 3),
            AutoSize = true
        };
        panel.Controls.Add(lblMod);

        _cboModifier = new ComboBox
        {
            Location = new Point(inputX, y),
            Width = inputW,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboModifier.Items.AddRange(new object[] { "Alt", "Ctrl", "Shift", "Win" });
        _cboModifier.SelectedIndex = _config.Modifier switch
        {
            GlobalHotkey.MOD_ALT => 0,
            GlobalHotkey.MOD_CONTROL => 1,
            GlobalHotkey.MOD_SHIFT => 2,
            GlobalHotkey.MOD_WIN => 3,
            _ => 0
        };
        panel.Controls.Add(_cboModifier);
        y += rowH;

        // Key rows
        _txtCycleWindows = AddRow(panel, "Cycle Windows:", _config.CycleWindowsKey, labelX, inputX, inputW, ref y, rowH);
        _txtFocusOtherMonitor = AddRow(panel, "Focus Other Monitor:", _config.FocusOtherMonitorKey, labelX, inputX, inputW, ref y, rowH);
        _txtLastDesktop = AddRow(panel, "Last Desktop:", _config.LastDesktopKey, labelX, inputX, inputW, ref y, rowH);
        _txtToggleManagement = AddRow(panel, "Toggle Management:", _config.ToggleManagementKey, labelX, inputX, inputW, ref y, rowH);

        // Desktop 1-9 note
        var lblDesktopNote = new Label
        {
            Text = "Desktop 1-9 uses the same modifier + number keys.",
            Location = new Point(labelX, y + 4),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        panel.Controls.Add(lblDesktopNote);
        y += rowH;

        var lblHint = new Label
        {
            Text = "Press any key in a field to set it (modifier is shared).",
            Location = new Point(labelX, y + 4),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        panel.Controls.Add(lblHint);
        y += rowH;

        // Buttons
        var btnExit = new Button
        {
            Text = "Exit App",
            Size = new Size(80, 28),
            Location = new Point(inputX + inputW - 80, y)
        };
        btnExit.Click += (_, _) =>
        {
            DialogResult = DialogResult.Abort;
            Close();
        };

        var btnMinimize = new Button
        {
            Text = "Save && Minimize",
            Size = new Size(120, 28),
            Location = new Point(btnExit.Left - 128, y)
        };
        btnMinimize.Click += (_, _) =>
        {
            SaveConfig();
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnReset = new Button
        {
            Text = "Reset to Defaults",
            Size = new Size(120, 28),
            Location = new Point(labelX, y)
        };
        btnReset.Click += (_, _) => ResetToDefaults();

        panel.Controls.Add(btnExit);
        panel.Controls.Add(btnMinimize);
        panel.Controls.Add(btnReset);

        AcceptButton = btnMinimize;

        tab.Controls.Add(panel);
    }

    private void BuildStatusTab(TabPage tab)
    {
        _statusTextBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F, FontStyle.Regular),
            BackColor = Color.White,
            ForeColor = Color.Black
        };
        tab.Controls.Add(_statusTextBox);

        _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _updateTimer.Tick += (_, _) => UpdateStatus();
    }

    private void UpdateStatus()
    {
        try
        {
            var statusReport = _focusHistory.GetStatusReport();
            var timestampedReport = $"Last Updated: {DateTime.Now:HH:mm:ss}\r\n\r\n{statusReport}";
            if (_statusTextBox.Text != timestampedReport)
                _statusTextBox.Text = timestampedReport;
        }
        catch (Exception ex)
        {
            _statusTextBox.Text = $"Error updating status: {ex.Message}";
        }
    }

    private KeyTextBox AddRow(Panel parent, string label, uint key, int labelX, int inputX, int inputW, ref int y, int rowH)
    {
        var lbl = new Label
        {
            Text = label,
            Location = new Point(labelX, y + 3),
            AutoSize = true
        };
        parent.Controls.Add(lbl);

        var txt = new KeyTextBox
        {
            Location = new Point(inputX, y),
            Width = inputW
        };
        txt.SetKey(key);
        parent.Controls.Add(txt);

        y += rowH;
        return txt;
    }

    private void ResetToDefaults()
    {
        var defaults = new HotkeyConfig();
        _cboModifier.SelectedIndex = 0;
        _txtCycleWindows.SetKey(defaults.CycleWindowsKey);
        _txtFocusOtherMonitor.SetKey(defaults.FocusOtherMonitorKey);
        _txtLastDesktop.SetKey(defaults.LastDesktopKey);
        _txtToggleManagement.SetKey(defaults.ToggleManagementKey);
    }

    private void SaveConfig()
    {
        _config.Modifier = _cboModifier.SelectedIndex switch
        {
            0 => GlobalHotkey.MOD_ALT,
            1 => GlobalHotkey.MOD_CONTROL,
            2 => GlobalHotkey.MOD_SHIFT,
            3 => GlobalHotkey.MOD_WIN,
            _ => GlobalHotkey.MOD_ALT
        };
        _config.CycleWindowsKey = _txtCycleWindows.RecordedKey;
        _config.FocusOtherMonitorKey = _txtFocusOtherMonitor.RecordedKey;
        _config.LastDesktopKey = _txtLastDesktop.RecordedKey;
        _config.ToggleManagementKey = _txtToggleManagement.RecordedKey;

        _config.Save();
        _onSave(_config);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _updateTimer?.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
