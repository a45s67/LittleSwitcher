using System.Diagnostics;
using Microsoft.Win32;

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
    private KeyTextBox _txtPinWindow = null!;
    private KeyTextBox _txtToggleTaskbar = null!;
    private KeyTextBox _txtToggleTitleBar = null!;
    private CheckBox _chkStartWithWindows = null!;
    private ComboBox _cboModifier = null!;
    private TextBox _statusTextBox = null!;
    private System.Windows.Forms.Timer _updateTimer = null!;

    private AppLauncherConfig _launcherConfig;
    private Panel _launcherPanel = null!;

    public MainWindow(HotkeyConfig config, FocusHistory focusHistory, Action<HotkeyConfig> onSave)
    {
        _config = config;
        _focusHistory = focusHistory;
        _onSave = onSave;
        _launcherConfig = AppLauncherConfig.Load();
        InitUI();
    }

    private void InitUI()
    {
        Text = "LittleSwitcher";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 450);
        MinimumSize = new Size(500, 450);
        ShowInTaskbar = true;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var settingsTab = new TabPage("Settings");
        BuildSettingsTab(settingsTab);
        tabs.TabPages.Add(settingsTab);

        var launcherTab = new TabPage("App Launcher");
        BuildLauncherTab(launcherTab);
        tabs.TabPages.Add(launcherTab);

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
        _txtPinWindow = AddRow(panel, "Pin Window:", _config.PinWindowKey, labelX, inputX, inputW, ref y, rowH);
        _txtToggleTaskbar = AddRow(panel, "Toggle Taskbar:", _config.ToggleTaskbarKey, labelX, inputX, inputW, ref y, rowH);
        _txtToggleTitleBar = AddRow(panel, "Toggle Title Bar:", _config.ToggleTitleBarKey, labelX, inputX, inputW, ref y, rowH);

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

        // Start with Windows
        _chkStartWithWindows = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(labelX, y + 2),
            AutoSize = true,
            Checked = IsAutoStartEnabled()
        };
        panel.Controls.Add(_chkStartWithWindows);
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
        _txtPinWindow.SetKey(defaults.PinWindowKey);
        _txtToggleTaskbar.SetKey(defaults.ToggleTaskbarKey);
        _txtToggleTitleBar.SetKey(defaults.ToggleTitleBarKey);
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
        _config.PinWindowKey = _txtPinWindow.RecordedKey;
        _config.ToggleTaskbarKey = _txtToggleTaskbar.RecordedKey;
        _config.ToggleTitleBarKey = _txtToggleTitleBar.RecordedKey;

        _config.Save();
        _onSave(_config);

        SetAutoStart(_chkStartWithWindows.Checked);
    }

    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "LittleSwitcher";

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, false);
            return key?.GetValue(RegistryValueName) is not null;
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true);
            if (key is null) return;
            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (exePath is not null)
                    key.SetValue(RegistryValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(RegistryValueName, false);
            }
        }
        catch { }
    }

    private void BuildLauncherTab(TabPage tab)
    {
        _launcherPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        var btnAdd = new Button
        {
            Text = "Add App",
            Size = new Size(80, 28),
            Location = new Point(16, 8),
            Tag = "AddButton"
        };
        btnAdd.Click += (_, _) =>
        {
            _launcherConfig.Entries.Add(new AppLauncherEntry());
            _launcherConfig.Save();
            RebuildLauncherRows();
        };
        _launcherPanel.Controls.Add(btnAdd);

        tab.Controls.Add(_launcherPanel);
        RebuildLauncherRows();
    }

    private void RebuildLauncherRows()
    {
        // Remove all controls except the Add button
        for (int i = _launcherPanel.Controls.Count - 1; i >= 0; i--)
        {
            var ctrl = _launcherPanel.Controls[i];
            if (ctrl.Tag as string != "AddButton")
            {
                _launcherPanel.Controls.RemoveAt(i);
                ctrl.Dispose();
            }
        }

        int y = 44;
        const int rowH = 34;

        for (int idx = 0; idx < _launcherConfig.Entries.Count; idx++)
        {
            var entry = _launcherConfig.Entries[idx];
            var capturedIndex = idx;

            var nudVD = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 9,
                Value = Math.Clamp(entry.VirtualDesktop, 1, 9),
                Width = 42,
                Location = new Point(16, y)
            };
            nudVD.ValueChanged += (_, _) =>
            {
                entry.VirtualDesktop = (int)nudVD.Value;
                _launcherConfig.Save();
            };

            var txtName = new TextBox
            {
                Text = entry.AppName,
                Width = 90,
                Location = new Point(64, y),
                PlaceholderText = "Name"
            };
            txtName.TextChanged += (_, _) =>
            {
                entry.AppName = txtName.Text;
                _launcherConfig.Save();
            };

            var txtCmd = new TextBox
            {
                Text = entry.Command,
                Width = 160,
                Location = new Point(160, y),
                PlaceholderText = "Command"
            };
            txtCmd.TextChanged += (_, _) =>
            {
                entry.Command = txtCmd.Text;
                _launcherConfig.Save();
            };

            var btnRun = new Button
            {
                Text = "Run",
                Size = new Size(50, 24),
                Location = new Point(326, y)
            };
            btnRun.Click += (_, _) => LaunchApp(entry);

            var btnRemove = new Button
            {
                Text = "X",
                Size = new Size(30, 24),
                Location = new Point(382, y)
            };
            btnRemove.Click += (_, _) =>
            {
                _launcherConfig.Entries.RemoveAt(capturedIndex);
                _launcherConfig.Save();
                RebuildLauncherRows();
            };

            _launcherPanel.Controls.Add(nudVD);
            _launcherPanel.Controls.Add(txtName);
            _launcherPanel.Controls.Add(txtCmd);
            _launcherPanel.Controls.Add(btnRun);
            _launcherPanel.Controls.Add(btnRemove);

            y += rowH;
        }
    }

    private void LaunchApp(AppLauncherEntry entry)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = entry.Command,
                Arguments = entry.Arguments,
                UseShellExecute = true
            });

            if (process == null) return;

            var targetDesktop = entry.VirtualDesktop - 1; // convert 1-based to 0-based
            var pollCount = 0;
            var pollTimer = new System.Windows.Forms.Timer { Interval = 200 };
            pollTimer.Tick += (_, _) =>
            {
                pollCount++;
                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        pollTimer.Stop();
                        pollTimer.Dispose();
                        var hwnd = process.MainWindowHandle;
                        VirtualDesktopInterop.MoveWindowToDesktopNumber(hwnd, targetDesktop);
                        _focusHistory.AddOrMoveToFront(hwnd);
                    }
                    else if (pollCount >= 25) // 5 seconds max
                    {
                        pollTimer.Stop();
                        pollTimer.Dispose();
                    }
                }
                catch
                {
                    pollTimer.Stop();
                    pollTimer.Dispose();
                }
            };
            pollTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
