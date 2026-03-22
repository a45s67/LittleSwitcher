namespace LittleSwitcher;

partial class AppHost
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _globalHotkey?.UnregisterAll();

            if (_locationHook != IntPtr.Zero)
            {
                UnhookWinEvent(_locationHook);
            }

            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }

            if (components != null)
            {
                components.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.notifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
        this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
        this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        this.contextMenuStrip.SuspendLayout();
        this.SuspendLayout();
        //
        // notifyIcon
        //
        this.notifyIcon.ContextMenuStrip = this.contextMenuStrip;
        this.notifyIcon.Text = "LittleSwitcher";
        this.notifyIcon.Visible = true;
        this.notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        //
        // contextMenuStrip
        //
        this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.exitToolStripMenuItem});
        this.contextMenuStrip.Name = "contextMenuStrip";
        this.contextMenuStrip.Size = new System.Drawing.Size(110, 48);
        //
        // openToolStripMenuItem
        //
        this.openToolStripMenuItem.Name = "openToolStripMenuItem";
        this.openToolStripMenuItem.Size = new System.Drawing.Size(109, 22);
        this.openToolStripMenuItem.Text = "Open";
        this.openToolStripMenuItem.Font = new System.Drawing.Font(this.openToolStripMenuItem.Font, System.Drawing.FontStyle.Bold);
        this.openToolStripMenuItem.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
        //
        // exitToolStripMenuItem
        //
        this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
        this.exitToolStripMenuItem.Size = new System.Drawing.Size(109, 22);
        this.exitToolStripMenuItem.Text = "Exit";
        this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
        //
        // AppHost
        //
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(800, 450);
        this.Name = "AppHost";
        this.Text = "LittleSwitcher";
        this.contextMenuStrip.ResumeLayout(false);
        this.ResumeLayout(false);
    }

    private System.Windows.Forms.NotifyIcon notifyIcon;
    private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
    private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
}
