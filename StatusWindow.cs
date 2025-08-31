using System;
using System.Drawing;
using System.Windows.Forms;

namespace LittleSwitcher;

public partial class StatusWindow : Form
{
    private readonly FocusHistory _focusHistory;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private TextBox _statusTextBox;

    public StatusWindow(FocusHistory focusHistory)
    {
        _focusHistory = focusHistory;
        InitializeComponent();
        
        // Create and configure the timer for real-time updates
        _updateTimer = new System.Windows.Forms.Timer();
        _updateTimer.Interval = 1000; // Update every second
        _updateTimer.Tick += UpdateTimer_Tick;
        
        // Initial update
        UpdateStatus();
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        
        // Form properties
        this.Text = "LittleSwitcher - Real-time Status";
        this.Size = new Size(600, 400);
        this.MinimumSize = new Size(400, 300);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.ShowInTaskbar = true;
        this.Icon = SystemIcons.Information;
        
        // TextBox for status display
        _statusTextBox = new TextBox();
        _statusTextBox.Multiline = true;
        _statusTextBox.ScrollBars = ScrollBars.Vertical;
        _statusTextBox.ReadOnly = true;
        _statusTextBox.Dock = DockStyle.Fill;
        _statusTextBox.Font = new Font("Consolas", 9F, FontStyle.Regular);
        _statusTextBox.BackColor = Color.White;
        _statusTextBox.ForeColor = Color.Black;
        
        this.Controls.Add(_statusTextBox);
        this.ResumeLayout(false);
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        try
        {
            var statusReport = _focusHistory.GetStatusReport();
            var timestampedReport = $"Last Updated: {DateTime.Now:HH:mm:ss}\n\n{statusReport}";
            
            if (_statusTextBox.Text != timestampedReport)
            {
                _statusTextBox.Text = timestampedReport;
            }
        }
        catch (Exception ex)
        {
            _statusTextBox.Text = $"Error updating status: {ex.Message}";
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value);
        
        if (value)
        {
            _updateTimer.Start();
            UpdateStatus(); // Immediate update when shown
        }
        else
        {
            _updateTimer.Stop();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hide instead of close when user clicks X
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
        }
        else
        {
            _updateTimer.Stop();
            base.OnFormClosing(e);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _statusTextBox?.Dispose();
        }
        base.Dispose(disposing);
    }
}