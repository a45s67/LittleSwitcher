namespace LittleSwitcher;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _globalHotkey?.UnregisterAll();
            
            if (_locationHook != IntPtr.Zero)
            {
                UnhookWinEvent(_locationHook);
            }
            
            // Hide and dispose of tray icon
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

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.notifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
        this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
        this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        this.contextMenuStrip.SuspendLayout();
        this.SuspendLayout();
        // 
        // notifyIcon
        // 
        this.notifyIcon.ContextMenuStrip = this.contextMenuStrip;
        this.notifyIcon.Text = "LittleSwitcher";
        this.notifyIcon.Visible = true;
        // 
        // contextMenuStrip
        // 
        this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
        this.exitToolStripMenuItem});
        this.contextMenuStrip.Name = "contextMenuStrip";
        this.contextMenuStrip.Size = new System.Drawing.Size(93, 26);
        // 
        // exitToolStripMenuItem
        // 
        this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
        this.exitToolStripMenuItem.Size = new System.Drawing.Size(92, 22);
        this.exitToolStripMenuItem.Text = "Exit";
        this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
        // 
        // Form1
        // 
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(800, 450);
        this.Name = "Form1";
        this.Text = "LittleSwitcher";
        this.contextMenuStrip.ResumeLayout(false);
        this.ResumeLayout(false);
    }

    private System.Windows.Forms.NotifyIcon notifyIcon;
    private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
    private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;

    #endregion
}
