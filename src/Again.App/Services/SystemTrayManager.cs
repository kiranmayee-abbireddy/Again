using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Again.Core.Domain;
using Again.Core.Services;
using Again.Core.Storage;

namespace Again.App.Services;

public interface ISystemTrayManager : IDisposable
{
    void Initialize(System.Windows.Window mainWindow, Func<Task> quickRestoreLastAction, Func<Task> openCaptureAction, Func<Task> openSettingsAction);
    void RefreshPinnedMenu(IEnumerable<Workspace> pinnedWorkspaces);
    void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info);
}

public class SystemTrayManager : ISystemTrayManager
{
    private NotifyIcon? _notifyIcon;
    private System.Windows.Window? _mainWindow;
    private ToolStripMenuItem? _pinnedSubMenu;
    private readonly IWorkspaceRestoreService _restoreService;

    public SystemTrayManager(IWorkspaceRestoreService restoreService)
    {
        _restoreService = restoreService;
    }

    public void Initialize(
        System.Windows.Window mainWindow, 
        Func<Task> quickRestoreLastAction, 
        Func<Task> openCaptureAction, 
        Func<Task> openSettingsAction)
    {
        _mainWindow = mainWindow;

        Icon? icon = null;
        try
        {
            var resUri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(resUri);
            if (streamInfo != null)
            {
                icon = new Icon(streamInfo.Stream);
            }
        }
        catch { }

        if (icon == null)
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    icon = Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch { }
        }

        icon ??= SystemIcons.Application;

        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open Again", null, (s, e) => ShowMainWindow());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        contextMenu.Items.Add(openItem);

        _pinnedSubMenu = new ToolStripMenuItem("Pinned Workspaces");
        _pinnedSubMenu.DropDownItems.Add(new ToolStripMenuItem("(None)") { Enabled = false });
        contextMenu.Items.Add(_pinnedSubMenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        contextMenu.Items.Add(new ToolStripMenuItem("📸 Capture Workspace", null, async (s, e) =>
        {
            ShowMainWindow();
            await openCaptureAction();
        }));

        contextMenu.Items.Add(new ToolStripMenuItem("⚡ Restore Last Workspace", null, async (s, e) =>
        {
            await quickRestoreLastAction();
        }));

        contextMenu.Items.Add(new ToolStripSeparator());

        contextMenu.Items.Add(new ToolStripMenuItem("⚙️ Settings", null, async (s, e) =>
        {
            ShowMainWindow();
            await openSettingsAction();
        }));

        contextMenu.Items.Add(new ToolStripMenuItem("Exit Again", null, (s, e) =>
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            System.Windows.Application.Current.Shutdown();
        }));

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Again — Workspace Manager",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
    }

    public void RefreshPinnedMenu(IEnumerable<Workspace> pinnedWorkspaces)
    {
        if (_pinnedSubMenu == null) return;

        _pinnedSubMenu.DropDownItems.Clear();
        var pinnedList = pinnedWorkspaces.ToList();

        if (pinnedList.Count == 0)
        {
            _pinnedSubMenu.DropDownItems.Add(new ToolStripMenuItem("(No pinned workspaces)") { Enabled = false });
            return;
        }

        foreach (var ws in pinnedList)
        {
            var item = new ToolStripMenuItem(ws.Name, null, async (s, e) =>
            {
                ShowNotification("Again", $"Restoring workspace: {ws.Name}...", ToolTipIcon.Info);
                var session = await _restoreService.RestoreWorkspaceAsync(ws);
                ShowNotification("Again", $"Restored '{ws.Name}' in {session.DurationMs ?? 0}ms ({session.Status}).", ToolTipIcon.Info);
            });
            _pinnedSubMenu.DropDownItems.Add(item);
        }
    }

    public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, text, icon);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            _mainWindow.WindowState = System.Windows.WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }
}
