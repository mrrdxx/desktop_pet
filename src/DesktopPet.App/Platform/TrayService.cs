using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;

namespace DesktopPet.App.Platform;

/// <summary>
/// System tray icon with context menu for Pause/Resume, Recover, and Exit.
/// Uses WinForms NotifyIcon per ARCHITECTURE §1 (UseWindowsForms=true for NotifyIcon).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;
    private bool _disposed;

    /// <summary>Raised when the user clicks Pause or Resume.</summary>
    public event EventHandler<bool>? PauseToggled;

    /// <summary>Raised when the user clicks Recover.</summary>
    public event EventHandler? RecoverRequested;

    /// <summary>Raised when the user clicks Exit.</summary>
    public event EventHandler? ExitRequested;

    public TrayService()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "DesktopPet",
            Visible = true,
            Icon = CreateDefaultIcon()
        };

        var menu = new ContextMenuStrip();

        _pauseItem = new ToolStripMenuItem("Pause", null, OnPauseClicked)
        {
            CheckOnClick = true
        };
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Recover", null, OnRecoverClicked);
        menu.Items.Add("Exit", null, OnExitClicked);

        _notifyIcon.ContextMenuStrip = menu;
    }

    /// <summary>Update the pause menu check state.</summary>
    public void SetPaused(bool paused)
    {
        if (_pauseItem.Checked != paused)
            _pauseItem.Checked = paused;
        _pauseItem.Text = paused ? "Resume" : "Pause";
    }

    public void ShowReadOnlyArchiveNotice()
    {
        _notifyIcon.BalloonTipTitle = "桌宠存档只读";
        _notifyIcon.BalloonTipText = "检测到较新版本的存档，原文件已保留。本次对应的状态或设置更改不会保存。";
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void OnPauseClicked(object? sender, EventArgs e)
    {
        PauseToggled?.Invoke(this, _pauseItem.Checked);
    }

    private void OnRecoverClicked(object? sender, EventArgs e)
    {
        RecoverRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Create a simple default icon (blue circle) for the tray.</summary>
    private static Icon CreateDefaultIcon()
    {
        // Use a built-in system icon as placeholder; real icon added with assets later.
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }
}
