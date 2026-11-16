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
    public bool IsPrimary { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string RoleBadge => IsPrimary ? "Primary Display" : "Secondary Display";
}

public class LiveWindowPreviewItem
{
    public string Title { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "🖥️";
    public string BoundsText { get; set; } = string.Empty;
    public string StateText { get; set; } = "Normal";
    public string MonitorName { get; set; } = "Display";
}

public class WorkspacePresetItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "💻";
    public string Category { get; set; } = "Development";
    public string SuggestedApps { get; set; } = string.Empty;
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
    private bool _hasPinnedWorkspaces;

    [ObservableProperty]
    private bool _hasRecentWorkspaces;

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

    public ObservableCollection<WorkspaceItemViewModel> PinnedWorkspaces { get; } = new();
    public ObservableCollection<WorkspaceItemViewModel> RecentWorkspaces { get; } = new();
    public ObservableCollection<LiveMonitorPreviewItem> CurrentMonitors { get; } = new();
    public ObservableCollection<LiveWindowPreviewItem> LiveActiveWindows { get; } = new();
    public ObservableCollection<WorkspacePresetItem> RecommendedPresets { get; } = new();
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

        InitializePresets();
    }

    private void InitializePresets()
    {
        RecommendedPresets.Clear();
        RecommendedPresets.Add(new WorkspacePresetItem
        {
            Title = "Full-Stack Dev",
            Category = "Development",
            Description = "Code editor, terminal, browser, and project folder.",
            IconGlyph = "💻",
            SuggestedApps = "VS Code • Chrome • Terminal • Explorer"
        });
        RecommendedPresets.Add(new WorkspacePresetItem
        {
            Title = "Meeting & Calls",
            Category = "Meetings",
            Description = "Video conferencing, calendar, and meeting notes.",
            IconGlyph = "🤝",
            SuggestedApps = "Teams / Zoom • Chrome • Notepad"
        });
        RecommendedPresets.Add(new WorkspacePresetItem
        {
            Title = "Deep Focus",
            Category = "Learning",
            Description = "Distraction-free editor, documentation, and music.",
            IconGlyph = "🎯",
            SuggestedApps = "Editor • Docs • Spotify"
        });
        RecommendedPresets.Add(new WorkspacePresetItem
        {
            Title = "Design & Creative",
            Category = "Design",
            Description = "Design workspace, browser inspection, and assets.",
            IconGlyph = "🎨",
            SuggestedApps = "Figma / Tools • Chrome • Assets"
        });
    }

    public async Task LoadAsync()
    {
        var all = await _repository.GetAllWorkspacesAsync();
        TotalWorkspacesCount = all.Count;

        var pinned = all.Where(w => w.IsPinned).Take(6).Select(w => new WorkspaceItemViewModel(w)).ToList();
        PinnedWorkspaces.Clear();
        foreach (var p in pinned) PinnedWorkspaces.Add(p);
        PinnedCount = PinnedWorkspaces.Count;
        HasPinnedWorkspaces = PinnedCount > 0;

        var recent = all.OrderByDescending(w => w.LastRestoredAt ?? w.UpdatedAt).Take(4).Select(w => new WorkspaceItemViewModel(w)).ToList();
        RecentWorkspaces.Clear();
        foreach (var r in recent) RecentWorkspaces.Add(r);
        HasRecentWorkspaces = RecentWorkspaces.Count > 0;

        // Load Displays
        var monitors = _monitorService.GetMonitors();
        ActiveMonitorsCount = monitors.Count;
        CurrentMonitors.Clear();
        foreach (var m in monitors)
        {
            CurrentMonitors.Add(new LiveMonitorPreviewItem
            {
                DeviceName = m.DeviceName,
                ResolutionText = $"{m.Width} × {m.Height}",
                IsPrimary = m.IsPrimary,
                Width = m.Width,
                Height = m.Height
            });
        }
        var primaryMon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        PrimaryMonitorSummary = primaryMon != null 
            ? $"{primaryMon.Width}×{primaryMon.Height} (Primary)" 
            : "Primary Monitor";

        // Load Live Active Windows
        try
        {
            var currentPid = Process.GetCurrentProcess().Id;
            var detected = _windowService.EnumerateVisibleWindows(excludeProcessId: currentPid);
            DetectedWindowsCount = detected.Count;
            LiveActiveWindows.Clear();
            foreach (var w in detected.Take(6))
            {
                LiveActiveWindows.Add(new LiveWindowPreviewItem
                {
                    Title = string.IsNullOrWhiteSpace(w.Title) ? w.ProcessName : w.Title,
                    ProcessName = w.ProcessName,
                    IconGlyph = WorkspaceItemViewModel.GetAppGlyph(w.ProcessName, w.Title),
                    BoundsText = $"{w.Bounds.Width}×{w.Bounds.Height}",
                    StateText = w.WindowState,
                    MonitorName = w.Monitor?.DeviceName ?? "Display 1"
                });
            }
            HasLiveWindows = LiveActiveWindows.Count > 0;
            LiveDesktopSummary = $"{ActiveMonitorsCount} {(ActiveMonitorsCount == 1 ? "Monitor" : "Monitors")} Connected • {DetectedWindowsCount} Active Windows";
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
                var history = await _historyService.GetRecentHistoryAsync(3);
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
    private void UsePreset(WorkspacePresetItem? preset)
    {
        _navigateAction(NavigationPage.CaptureWorkspace, preset?.Category);
    }

    [RelayCommand]
    private async Task QuickRestoreAsync(WorkspaceItemViewModel? item)
    {
        var target = item?.Model ?? RecentWorkspaces.FirstOrDefault()?.Model ?? PinnedWorkspaces.FirstOrDefault()?.Model;
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
        if (item != null)
        {
            _navigateAction(NavigationPage.WorkspaceDetails, item.Model);
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
