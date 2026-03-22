namespace LittleSwitcher;

public class TitleOverlay : Form
{
    private static TitleOverlay? _current;
    private readonly System.Windows.Forms.Timer _dismissTimer;

    private TitleOverlay(string title)
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.White;
        StartPosition = FormStartPosition.Manual;

        var label = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            Padding = new Padding(12, 6, 12, 6)
        };
        label.Location = new Point(0, 0);
        Controls.Add(label);

        // Size the form to fit the label
        using (var g = CreateGraphics())
        {
            var size = g.MeasureString(title, label.Font);
            ClientSize = new Size((int)size.Width + 28, (int)size.Height + 16);
        }
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleCenter;

        _dismissTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            Close();
        };
    }

    public static void ShowOverlay(string title, IntPtr targetHwnd)
    {
        _current?.Close();
        _current = null;

        if (string.IsNullOrWhiteSpace(title))
            return;

        var overlay = new TitleOverlay(title);

        if (WindowHelper.GetWindowRect(targetHwnd, out WindowHelper.RECT rect))
        {
            int windowWidth = rect.Right - rect.Left;
            int x = rect.Left + (windowWidth - overlay.Width) / 2;
            int y = rect.Top + 8;
            overlay.Location = new Point(x, y);
        }

        _current = overlay;
        overlay.Show();
        overlay._dismissTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _dismissTimer.Stop();
        _dismissTimer.Dispose();
        if (_current == this)
            _current = null;
        base.OnFormClosing(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            // WS_EX_TOOLWINDOW prevents the overlay from appearing in Alt+Tab
            // WS_EX_NOACTIVATE prevents it from stealing focus
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
}
