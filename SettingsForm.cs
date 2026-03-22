using System.Runtime.InteropServices;

namespace LittleSwitcher;

public class HotkeyTextBox : TextBox
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public uint RecordedModifiers { get; private set; }
    public uint RecordedKey { get; private set; }

    public HotkeyTextBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
    }

    public void SetBinding(HotkeyBinding binding)
    {
        RecordedModifiers = binding.Modifiers;
        RecordedKey = binding.Key;
        Text = binding.DisplayText;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var modifiers = keyData & Keys.Modifiers;
        var key = keyData & Keys.KeyCode;

        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return true;

        uint mod = 0;
        if (modifiers.HasFlag(Keys.Control)) mod |= GlobalHotkey.MOD_CONTROL;
        if (modifiers.HasFlag(Keys.Alt)) mod |= GlobalHotkey.MOD_ALT;
        if (modifiers.HasFlag(Keys.Shift)) mod |= GlobalHotkey.MOD_SHIFT;
        if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) mod |= GlobalHotkey.MOD_WIN;

        if (mod == 0) return true;

        RecordedModifiers = mod;
        RecordedKey = (uint)key;

        var binding = new HotkeyBinding(RecordedModifiers, RecordedKey);
        Text = binding.DisplayText;

        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }
}

public class SettingsForm : Form
{
    private readonly HotkeyConfig _config;
    private readonly Action<HotkeyConfig> _onSave;

    private HotkeyTextBox _txtCycleWindows = null!;
    private HotkeyTextBox _txtFocusOtherMonitor = null!;
    private HotkeyTextBox _txtLastDesktop = null!;
    private HotkeyTextBox _txtToggleManagement = null!;
    private ComboBox _cboDesktopModifier = null!;

    public SettingsForm(HotkeyConfig config, Action<HotkeyConfig> onSave)
    {
        _config = config;
        _onSave = onSave;
        InitUI();
    }

    private void InitUI()
    {
        Text = "LittleSwitcher - Hotkey Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 280);

        const int labelX = 16;
        const int inputX = 240;
        const int inputW = 220;
        const int rowH = 34;
        int y = 16;

        _txtCycleWindows = AddRow("Cycle Windows:", _config.CycleWindows, labelX, inputX, inputW, ref y, rowH);
        _txtFocusOtherMonitor = AddRow("Focus Other Monitor:", _config.FocusOtherMonitor, labelX, inputX, inputW, ref y, rowH);
        _txtLastDesktop = AddRow("Last Desktop:", _config.LastDesktop, labelX, inputX, inputW, ref y, rowH);
        _txtToggleManagement = AddRow("Toggle Management:", _config.ToggleManagement, labelX, inputX, inputW, ref y, rowH);

        // Desktop modifier row
        var lblDesktop = new Label
        {
            Text = "Desktop 1-9 Modifier:",
            Location = new Point(labelX, y + 3),
            AutoSize = true
        };
        Controls.Add(lblDesktop);

        _cboDesktopModifier = new ComboBox
        {
            Location = new Point(inputX, y),
            Width = inputW,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboDesktopModifier.Items.AddRange(new object[] { "Alt", "Ctrl", "Shift", "Win" });
        _cboDesktopModifier.SelectedIndex = _config.SwitchDesktopModifier switch
        {
            GlobalHotkey.MOD_ALT => 0,
            GlobalHotkey.MOD_CONTROL => 1,
            GlobalHotkey.MOD_SHIFT => 2,
            GlobalHotkey.MOD_WIN => 3,
            _ => 0
        };
        Controls.Add(_cboDesktopModifier);
        y += rowH;

        // Hint
        var lblHint = new Label
        {
            Text = "Click a field and press a key combination to set it.",
            Location = new Point(labelX, y + 4),
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lblHint);
        y += rowH;

        // Buttons
        var btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(80, 28),
            Location = new Point(ClientSize.Width - 96, y),
            DialogResult = DialogResult.Cancel
        };

        var btnSave = new Button
        {
            Text = "Save",
            Size = new Size(80, 28),
            Location = new Point(btnCancel.Left - 88, y),
            DialogResult = DialogResult.OK
        };
        btnSave.Click += (_, _) => SaveConfig();

        var btnReset = new Button
        {
            Text = "Reset to Defaults",
            Size = new Size(120, 28),
            Location = new Point(labelX, y)
        };
        btnReset.Click += (_, _) => ResetToDefaults();

        Controls.Add(btnCancel);
        Controls.Add(btnSave);
        Controls.Add(btnReset);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private HotkeyTextBox AddRow(string label, HotkeyBinding binding, int labelX, int inputX, int inputW, ref int y, int rowH)
    {
        var lbl = new Label
        {
            Text = label,
            Location = new Point(labelX, y + 3),
            AutoSize = true
        };
        Controls.Add(lbl);

        var txt = new HotkeyTextBox
        {
            Location = new Point(inputX, y),
            Width = inputW
        };
        txt.SetBinding(binding);
        Controls.Add(txt);

        y += rowH;
        return txt;
    }

    private void ResetToDefaults()
    {
        var defaults = new HotkeyConfig();
        _txtCycleWindows.SetBinding(defaults.CycleWindows);
        _txtFocusOtherMonitor.SetBinding(defaults.FocusOtherMonitor);
        _txtLastDesktop.SetBinding(defaults.LastDesktop);
        _txtToggleManagement.SetBinding(defaults.ToggleManagement);
        _cboDesktopModifier.SelectedIndex = 0;
    }

    private void SaveConfig()
    {
        _config.CycleWindows = new HotkeyBinding(_txtCycleWindows.RecordedModifiers, _txtCycleWindows.RecordedKey);
        _config.FocusOtherMonitor = new HotkeyBinding(_txtFocusOtherMonitor.RecordedModifiers, _txtFocusOtherMonitor.RecordedKey);
        _config.LastDesktop = new HotkeyBinding(_txtLastDesktop.RecordedModifiers, _txtLastDesktop.RecordedKey);
        _config.ToggleManagement = new HotkeyBinding(_txtToggleManagement.RecordedModifiers, _txtToggleManagement.RecordedKey);
        _config.SwitchDesktopModifier = _cboDesktopModifier.SelectedIndex switch
        {
            0 => GlobalHotkey.MOD_ALT,
            1 => GlobalHotkey.MOD_CONTROL,
            2 => GlobalHotkey.MOD_SHIFT,
            3 => GlobalHotkey.MOD_WIN,
            _ => GlobalHotkey.MOD_ALT
        };

        _config.Save();
        _onSave(_config);
    }
}
