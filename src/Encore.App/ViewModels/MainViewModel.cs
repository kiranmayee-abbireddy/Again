using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;

namespace Encore.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceRestoreService _restoreService;
    private readonly IConfigurationRepository _configRepo;
    private readonly IShortcutManagementService _shortcutService;

    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    [ObservableProperty]
    private NavigationItem? _selectedNavigation;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isGlobalRestoring;

    [ObservableProperty]
    private string _currentTheme = "Dark";

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    public DashboardViewModel DashboardVM { get; }
    public WorkspaceLibraryViewModel WorkspacesVM { get; }
    public WorkspaceDetailsViewModel WorkspaceDetailsVM { get; }
    public WorkspaceCaptureViewModel WorkspaceCaptureVM { get; }
    public ApplicationManagerViewModel ApplicationsVM { get; }
    public HistoryViewModel HistoryVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public Action<string>? ThemeChangeRequested { get; set; }
    public Action? ToggleWindowVisibilityRequested { get; set; }

    public MainViewModel(
        IWorkspaceRepository repository,
        IWorkspaceRestoreService restoreService,
        IConfigurationRepository configRepo,
        IShortcutManagementService shortcutService,
        DashboardViewModel dashboardVM,
        WorkspaceLibraryViewModel workspacesVM,
        WorkspaceDetailsViewModel workspaceDetailsVM,
        WorkspaceCaptureViewModel workspaceCaptureVM,
        ApplicationManagerViewModel applicationsVM,
        HistoryViewModel historyVM,
        SettingsViewModel settingsVM)
    {
        _repository = repository;
        _restoreService = restoreService;
        _configRepo = configRepo;
        _shortcutService = shortcutService;

        DashboardVM = dashboardVM;
        WorkspacesVM = workspacesVM;
        WorkspaceDetailsVM = workspaceDetailsVM;
        WorkspaceCaptureVM = workspaceCaptureVM;
        ApplicationsVM = applicationsVM;
        HistoryVM = historyVM;
        SettingsVM = settingsVM;

        SettingsVM.ThemeChanged = theme =>
        {
            CurrentTheme = theme;
            ThemeChangeRequested?.Invoke(theme);
        };

        _shortcutService.ShortcutTriggered += OnShortcutTriggered;

        InitializeNavigation();
    }

    private void InitializeNavigation()
    {
        NavigationItems.Add(new NavigationItem { Page = NavigationPage.Dashboard, Title = "Home", Glyph = "\uE80F" });
        NavigationItems.Add(new NavigationItem { Page = NavigationPage.Workspaces, Title = "Workspaces", Glyph = "\uE8F1" });
        NavigationItems.Add(new NavigationItem { Page = NavigationPage.Applications, Title = "Applications", Glyph = "\uE71D" });
        NavigationItems.Add(new NavigationItem { Page = NavigationPage.History, Title = "History", Glyph = "\uE81C" });
        NavigationItems.Add(new NavigationItem { Page = NavigationPage.Settings, Title = "Settings", Glyph = "\uE713" });

        SelectedNavigation = NavigationItems[0];
    }

    public async Task InitializeAsync()
    {
        var settings = await _configRepo.LoadSettingsAsync();
        CurrentTheme = settings.Theme switch
        {
            "Dark" => "Emerald (Dark)",
            "Light" => "Emerald (Light)",
            _ => string.IsNullOrEmpty(settings.Theme) ? "Emerald (Dark)" : settings.Theme
        };
        ThemeChangeRequested?.Invoke(CurrentTheme);

        await NavigateToPageAsync(NavigationPage.Dashboard);
    }

    private void OnShortcutTriggered(ShortcutAction action)
    {
        App.Current?.Dispatcher?.Invoke(async () =>
        {
            switch (action)
            {
                case ShortcutAction.CaptureWorkspace:
                    await NavigateToPageAsync(NavigationPage.CaptureWorkspace);
                    ToggleWindowVisibilityRequested?.Invoke();
                    break;

                case ShortcutAction.RestoreLastUsed:
                    await QuickRestoreLastUsedAsync();
                    break;

                case ShortcutAction.ToggleShowHide:
                    ToggleWindowVisibilityRequested?.Invoke();
                    break;

                case ShortcutAction.QuickSelector:
                    await NavigateToPageAsync(NavigationPage.Workspaces);
                    ToggleWindowVisibilityRequested?.Invoke();
                    break;
            }
        });
    }

    public async Task NavigateToPageAsync(NavigationPage page, object? parameter = null)
    {
        var navItem = NavigationItems.FirstOrDefault(n => n.Page == page);
        if (navItem != null)
        {
            foreach (var item in NavigationItems) item.IsSelected = false;
            navItem.IsSelected = true;
            SelectedNavigation = navItem;
        }

        switch (page)
        {
            case NavigationPage.Dashboard:
                await DashboardVM.LoadAsync();
                CurrentViewModel = DashboardVM;
                StatusMessage = "Home Dashboard";
                break;

            case NavigationPage.Workspaces:
                await WorkspacesVM.LoadAsync();
                CurrentViewModel = WorkspacesVM;
                StatusMessage = "Workspaces Library";
                break;

            case NavigationPage.WorkspaceDetails:
                if (parameter is Workspace ws)
                {
                    await WorkspaceDetailsVM.InitializeAsync(ws);
                    CurrentViewModel = WorkspaceDetailsVM;
                    StatusMessage = $"Workspace Details: {ws.Name}";
                }
                break;

            case NavigationPage.CaptureWorkspace:
                await WorkspaceCaptureVM.InitializeAsync();
                CurrentViewModel = WorkspaceCaptureVM;
                StatusMessage = "Capture Workspace";
                break;

            case NavigationPage.Applications:
                await ApplicationsVM.LoadAsync();
                CurrentViewModel = ApplicationsVM;
                StatusMessage = "Running Applications & Windows";
                break;

            case NavigationPage.History:
                await HistoryVM.LoadAsync();
                CurrentViewModel = HistoryVM;
                StatusMessage = "Restoration History";
                break;

            case NavigationPage.Settings:
                await SettingsVM.LoadAsync();
                CurrentViewModel = SettingsVM;
                StatusMessage = "Application Settings";
                break;
        }
    }

    [RelayCommand]
    private async Task NavigateAsync(NavigationItem? item)
    {
        if (item != null)
        {
            await NavigateToPageAsync(item.Page);
        }
    }

    [RelayCommand]
    private async Task QuickCaptureAsync()
    {
        await NavigateToPageAsync(NavigationPage.CaptureWorkspace);
    }

    [RelayCommand]
    public async Task QuickRestoreLastUsedAsync()
    {
        var all = await _repository.GetAllWorkspacesAsync();
        var target = all.OrderByDescending(w => w.LastRestoredAt ?? w.UpdatedAt).FirstOrDefault();
        if (target == null)
        {
            StatusMessage = "No workspaces available to restore.";
            return;
        }

        IsGlobalRestoring = true;
        StatusMessage = $"Restoring workspace: {target.Name}...";

        try
        {
            var progress = new Progress<RestorationProgress>(p =>
            {
                StatusMessage = p.Message;
            });

            var session = await _restoreService.RestoreWorkspaceAsync(target, progress: progress);
            StatusMessage = $"Restored '{target.Name}' in {session.DurationMs ?? 0}ms ({session.Status}).";

            if (CurrentViewModel == DashboardVM)
            {
                await DashboardVM.LoadAsync();
            }
        }
        finally
        {
            IsGlobalRestoring = false;
        }
    }
}
