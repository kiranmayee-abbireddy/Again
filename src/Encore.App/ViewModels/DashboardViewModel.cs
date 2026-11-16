using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;

namespace Encore.App.ViewModels;

public class LiveMonitorPreviewItem
{
    public string DeviceName { get; set; } = string.Empty;
    public string ResolutionText { get; set; } = string.Empty;
    public string ScaleText { get; set; } = "100% DPI";
    public string BoundsText { get; set; } = "(0, 0)";
    public string WorkAreaText { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string RoleBadge => IsPrimary ? "Primary Display" : "Secondary Display";
}

public class LiveWindowPreviewItem
{
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE737";
    public string BoundsText { get; set; } = string.Empty;
    public string StateText { get; set; } = "Normal";
    public string MonitorName { get; set; } = "Display";
}

public class RecentHistorySnippetItem
{
    public string WorkspaceName { get; set; } = string.Empty;
    public string RelativeTime { get; set; } = "just now";
    public string Status { get; set; } = "Success";
    public long DurationMs { get; set; }
    public int ItemCount { get; set; }
    public string SummaryText => $"{ItemCount} {(ItemCount == 1 ? "app" : "apps")} restored in {DurationMs}ms";
}

public partial class DashboardViewModel : ObservableObject
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceRestoreService _restoreService;
    private readonly IMonitorManagementService _monitorService;
    private readonly IWindowsWindowService _windowService;
    private readonly IRestorationHistoryService? _historyService;
    private readonly Action<NavigationPage, object?> _navigateAction;

    [ObservableProperty]
    private int _pinnedCount;

    [ObservableProperty]
    private int _totalWorkspacesCount;

    [ObservableProperty]
    private int _activeMonitorsCount;

    [ObservableProperty]
    private int _detectedWindowsCount;

    [ObservableProperty]
    private string _activeMonitorsLabel = "0 Displays";

    [ObservableProperty]
    private string _detectedWindowsLabel = "0 Live Windows";

    [ObservableProperty]
    private string _totalWorkspacesLabel = "0 Stages";

    [ObservableProperty]
    private bool _hasPinnedWorkspaces;

    [ObservableProperty]
    private bool _hasRecentWorkspaces;

    [ObservableProperty]
    private bool _hasAnyWorkspaces;

    [ObservableProperty]
    private bool _hasLiveWindows;

    [ObservableProperty]
    private bool _hasRestorationHistory;

    [ObservableProperty]
    private string _liveDesktopSummary = "Analyzing desktop...";

    [ObservableProperty]
    private string _primaryMonitorSummary = "Display";

    [ObservableProperty]
    private bool _isRestoring;

    [ObservableProperty]
    private string _restoringMessage = string.Empty;

    // Active Stage Spotlight Properties
    [ObservableProperty]
    private WorkspaceItemViewModel? _activeStageWorkspace;

    [ObservableProperty]
    private bool _hasActiveStage;

    [ObservableProperty]
    private string _activeStageName = "No Stage Active";

    [ObservableProperty]
    private string _activeStageCategory = "None";

    [ObservableProperty]
    private string _activeStageRestoredText = "Ready to align";

    [ObservableProperty]
    private string _activeStageAppCountText = "0 apps";

    [ObservableProperty]
    private string _activeStageDisplaysText = "1 display";

    // Engine & System Diagnostics
    [ObservableProperty]
    private string _engineStatusText = "Active (Win32 DWM)";

    [ObservableProperty]
    private string _engineDpiModeText = "PerMonitorV2 Calibrated";

    [ObservableProperty]
    private string _engineDaemonText = "System Tray & Hotkeys Active";

    [ObservableProperty]
    private string _engineLatencyText = "< 350ms avg restore";

    public ObservableCollection<WorkspaceItemViewModel> PinnedWorkspaces { get; } = new();
    public ObservableCollection<WorkspaceItemViewModel> RecentWorkspaces { get; } = new();
    public ObservableCollection<WorkspaceItemViewModel> AllWorkspaces { get; } = new();
    public ObservableCollection<LiveMonitorPreviewItem> CurrentMonitors { get; } = new();
    public ObservableCollection<LiveWindowPreviewItem> LiveActiveWindows { get; } = new();
    public ObservableCollection<RecentHistorySnippetItem> RecentRestorations { get; } = new();

    public DashboardViewModel(
        IWorkspaceRepository repository,
        IWorkspaceRestoreService restoreService,
        IMonitorManagementService monitorService,
        IWindowsWindowService windowService,
        Action<NavigationPage, object?> navigateAction,
        IRestorationHistoryService? historyService = null)
    {
        _repository = repository;
        _restoreService = restoreService;
        _monitorService = monitorService;
        _windowService = windowService;
        _historyService = historyService;
        _navigateAction = navigateAction;
    }

    public async Task LoadAsync()
    {
        var all = await _repository.GetAllWorkspacesAsync();
        TotalWorkspacesCount = all.Count;
        HasAnyWorkspaces = all.Count > 0;
        TotalWorkspacesLabel = TotalWorkspacesCount == 1 ? "1 Stage" : $"{TotalWorkspacesCount} Stages";

        // Pinned Workspaces
        var pinned = all.Where(w => w.IsPinned).Take(6).Select(w => new WorkspaceItemViewModel(w)).ToList();
        PinnedWorkspaces.Clear();
        foreach (var p in pinned) PinnedWorkspaces.Add(p);
        PinnedCount = PinnedWorkspaces.Count;
        HasPinnedWorkspaces = PinnedCount > 0;

        // Recent Workspaces
        var recent = all.OrderByDescending(w => w.LastRestoredAt ?? w.UpdatedAt).Take(6).Select(w => new WorkspaceItemViewModel(w)).ToList();
        RecentWorkspaces.Clear();
        foreach (var r in recent) RecentWorkspaces.Add(r);
        HasRecentWorkspaces = RecentWorkspaces.Count > 0;

        // All Workspaces (for stage switcher)
        AllWorkspaces.Clear();
        foreach (var w in all.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.LastRestoredAt ?? x.UpdatedAt).Take(8))
        {
            AllWorkspaces.Add(new WorkspaceItemViewModel(w));
        }

        // Active Stage Spotlight (last restored or most recent)
        var activeModel = all
            .Where(w => w.LastRestoredAt.HasValue)
            .OrderByDescending(w => w.LastRestoredAt)
            .FirstOrDefault() ?? all.OrderByDescending(w => w.UpdatedAt).FirstOrDefault();

        if (activeModel != null)
        {
            ActiveStageWorkspace = new WorkspaceItemViewModel(activeModel);
            HasActiveStage = true;
            ActiveStageName = activeModel.Name;
            ActiveStageCategory = activeModel.Category;
            ActiveStageRestoredText = activeModel.LastRestoredAt.HasValue
                ? $"Restored {GetRelativeTime(activeModel.LastRestoredAt.Value)}"
                : "Ready to restore";
            ActiveStageAppCountText = $"{activeModel.Applications.Count} {(activeModel.Applications.Count == 1 ? "app" : "apps")}";
            
            var distinctMonitors = activeModel.Applications
                .Where(a => a.Placement != null && !string.IsNullOrEmpty(a.Placement.MonitorDeviceId))
                .Select(a => a.Placement!.MonitorDeviceId)
                .Distinct()
                .Count();
            ActiveStageDisplaysText = $"{Math.Max(1, distinctMonitors)} {(Math.Max(1, distinctMonitors) == 1 ? "display" : "displays")}";
        }
        else
        {
            ActiveStageWorkspace = null;
            HasActiveStage = false;
            ActiveStageName = "No Stage Selected";
            ActiveStageCategory = "None";
            ActiveStageRestoredText = "Snapshot your current setup to create your first stage";
            ActiveStageAppCountText = "0 apps";
            ActiveStageDisplaysText = "0 displays";
        }

        // Load Displays
        var monitors = _monitorService.GetMonitors();
        ActiveMonitorsCount = monitors.Count;
        ActiveMonitorsLabel = ActiveMonitorsCount == 1 ? "1 Display" : $"{ActiveMonitorsCount} Displays";
        CurrentMonitors.Clear();
        foreach (var m in monitors)
        {
            CurrentMonitors.Add(new LiveMonitorPreviewItem
            {
                DeviceName = m.FriendlyName,
                ResolutionText = $"{m.Width} × {m.Height}",
                ScaleText = $"{Math.Round(m.ScaleFactor * 100)}% DPI",
                BoundsText = $"Virtual: ({m.Left}, {m.Top})",
                WorkAreaText = $"Work Area: {m.WorkWidth} × {m.WorkHeight}",
                IsPrimary = m.IsPrimary,
                Width = m.Width,
                Height = m.Height
            });
        }
        var primaryMon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        PrimaryMonitorSummary = primaryMon != null 
            ? $"{primaryMon.FriendlyName} ({primaryMon.Width}×{primaryMon.Height})" 
            : "Primary Display";

        // Load Live Active Windows
        try
        {
            var currentPid = Process.GetCurrentProcess().Id;
            var detected = _windowService.EnumerateVisibleWindows(excludeProcessId: currentPid);
            DetectedWindowsCount = detected.Count;
            DetectedWindowsLabel = DetectedWindowsCount == 1 ? "1 Live Window" : $"{DetectedWindowsCount} Live Windows";
            LiveActiveWindows.Clear();
            foreach (var w in detected.Take(12))
            {
                LiveActiveWindows.Add(new LiveWindowPreviewItem
                {
                    Title = string.IsNullOrWhiteSpace(w.Title) ? w.ProcessName : w.Title,
                    ProcessName = w.ProcessName,
                    IconGlyph = WorkspaceItemViewModel.GetAppGlyph(w.ProcessName, w.Title),
                    BoundsText = $"{w.Bounds.Width}×{w.Bounds.Height}",
                    StateText = w.WindowState,
                    MonitorName = MonitorInfoModel.FormatFriendlyName(w.Monitor?.DeviceName)
                });
            }
            HasLiveWindows = LiveActiveWindows.Count > 0;
            LiveDesktopSummary = $"{ActiveMonitorsLabel} Connected • {DetectedWindowsLabel}";
        }
        catch
        {
            HasLiveWindows = false;
        }

        // Load Recent History Snippet
        if (_historyService != null)
        {
            try
            {
                var history = await _historyService.GetRecentHistoryAsync(4);
                RecentRestorations.Clear();
                foreach (var h in history)
                {
                    RecentRestorations.Add(new RecentHistorySnippetItem
                    {
                        WorkspaceName = h.WorkspaceName,
                        RelativeTime = GetRelativeTime(h.StartedAt),
                        Status = h.Status,
                        DurationMs = h.DurationMs ?? 0,
                        ItemCount = h.Items.Count
                    });
                }
                HasRestorationHistory = RecentRestorations.Count > 0;

                if (RecentRestorations.Count > 0)
                {
                    var avg = (int)RecentRestorations.Average(r => r.DurationMs);
                    EngineLatencyText = $"{avg}ms avg restoration";
                }
                else
                {
                    EngineLatencyText = "< 350ms fast restore";
                }
            }
            catch
            {
                HasRestorationHistory = false;
            }
        }
    }

    private static string GetRelativeTime(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return dt.ToLocalTime().ToString("MMM dd");
    }

    [RelayCommand]
    private void StartCapture()
    {
        _navigateAction(NavigationPage.CaptureWorkspace, null);
    }

    [RelayCommand]
    private async Task RescanDesktopAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RealignActiveStageAsync()
    {
        var target = ActiveStageWorkspace?.Model ?? RecentWorkspaces.FirstOrDefault()?.Model;
        if (target == null) return;

        IsRestoring = true;
        RestoringMessage = $"Re-aligning stage windows for {target.Name}...";

        try
        {
            var progress = new Progress<RestorationProgress>(p =>
            {
                RestoringMessage = p.Message;
            });

            await _restoreService.RestoreWorkspaceAsync(target, progress: progress);
            await LoadAsync();
        }
        finally
        {
            IsRestoring = false;
            RestoringMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task QuickRestoreAsync(WorkspaceItemViewModel? item)
    {
        var target = item?.Model ?? ActiveStageWorkspace?.Model ?? RecentWorkspaces.FirstOrDefault()?.Model ?? PinnedWorkspaces.FirstOrDefault()?.Model;
        if (target == null) return;

        IsRestoring = true;
        RestoringMessage = $"Restoring {target.Name}...";

        try
        {
            var progress = new Progress<RestorationProgress>(p =>
            {
                RestoringMessage = p.Message;
            });

            await _restoreService.RestoreWorkspaceAsync(target, progress: progress);
            await LoadAsync();
        }
        finally
        {
            IsRestoring = false;
            RestoringMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void OpenWorkspaceDetails(WorkspaceItemViewModel? item)
    {
        var target = item?.Model ?? ActiveStageWorkspace?.Model;
        if (target != null)
        {
            _navigateAction(NavigationPage.WorkspaceDetails, target);
        }
    }

    [RelayCommand]
    private void ViewAllWorkspaces()
    {
        _navigateAction(NavigationPage.Workspaces, null);
    }

    [RelayCommand]
    private void ViewHistory()
    {
        _navigateAction(NavigationPage.History, null);
    }
}
