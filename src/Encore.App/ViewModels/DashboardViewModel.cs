using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;

namespace Encore.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceRestoreService _restoreService;
    private readonly IMonitorManagementService _monitorService;
    private readonly IWindowsWindowService _windowService;
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
    private bool _isRestoring;

    [ObservableProperty]
    private string _restoringMessage = string.Empty;

    public ObservableCollection<WorkspaceItemViewModel> PinnedWorkspaces { get; } = new();
    public ObservableCollection<WorkspaceItemViewModel> RecentWorkspaces { get; } = new();

    public DashboardViewModel(
        IWorkspaceRepository repository,
        IWorkspaceRestoreService restoreService,
        IMonitorManagementService monitorService,
        IWindowsWindowService windowService,
        Action<NavigationPage, object?> navigateAction)
    {
        _repository = repository;
        _restoreService = restoreService;
        _monitorService = monitorService;
        _windowService = windowService;
        _navigateAction = navigateAction;
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

        ActiveMonitorsCount = _monitorService.GetMonitors().Count;
        DetectedWindowsCount = _windowService.EnumerateVisibleWindows().Count;
    }

    [RelayCommand]
    private void StartCapture()
    {
        _navigateAction(NavigationPage.CaptureWorkspace, null);
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
}
