using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;
using Microsoft.Win32;

namespace Encore.App.ViewModels;

public partial class WorkspaceAppItemViewModel : ObservableObject
{
    public WorkspaceApplication Model { get; }

    public Guid Id => Model.Id;
    public string ExecutablePath => Model.ExecutablePath;
    public string ProcessName => Model.ProcessName;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Model.WindowTitle) ? Model.ProcessName : Model.WindowTitle;

    public string IconGlyph => ApplicationCatalogService.GetAppGlyph(ProcessName, DisplayTitle);

    public string? BrowserProfile => Model.BrowserProfile;
    public bool HasBrowserProfile => !string.IsNullOrEmpty(Model.BrowserProfile);

    public string? FolderViewPath => Model.FolderViewPath;
    public bool HasFolderViewPath => !string.IsNullOrEmpty(Model.FolderViewPath);

    public int TabCount
    {
        get
        {
            if (string.IsNullOrEmpty(Model.BrowserTabsJson)) return 0;
            try
            {
                var tabs = System.Text.Json.JsonSerializer.Deserialize<List<BrowserTabInfo>>(Model.BrowserTabsJson);
                return tabs?.Count ?? 0;
            }
            catch { return 0; }
        }
    }

    public bool HasTabs => TabCount > 0;

    public string TabsSummary
    {
        get
        {
            if (string.IsNullOrEmpty(Model.BrowserTabsJson)) return string.Empty;
            try
            {
                var tabs = System.Text.Json.JsonSerializer.Deserialize<List<BrowserTabInfo>>(Model.BrowserTabsJson);
                if (tabs == null || tabs.Count == 0) return string.Empty;
                return $"{tabs.Count} tab{(tabs.Count > 1 ? "s" : "")}: " + string.Join(", ", tabs.Take(3).Select(t => t.Title));
            }
            catch { return string.Empty; }
        }
    }

    [ObservableProperty]
    private string? _launchArguments;

    [ObservableProperty]
    private int _launchDelayMs;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _selectedWindowState;

    public ObservableCollection<string> AvailableWindowStates { get; } = new() { "Normal", "Maximized", "Minimized" };

    public string WindowBoundsText => Model.Placement != null
        ? $"{Model.Placement.Width} × {Model.Placement.Height} at ({Model.Placement.X}, {Model.Placement.Y})"
        : "Default bounds (1280×720)";

    public string MonitorText => !string.IsNullOrEmpty(Model.Placement?.MonitorDeviceId)
        ? MonitorInfoModel.FormatFriendlyName(Model.Placement.MonitorDeviceId)
        : "Primary Display";

    public WorkspaceAppItemViewModel(WorkspaceApplication model)
    {
        Model = model;
        _launchArguments = model.LaunchArguments;
        _launchDelayMs = model.LaunchDelayMs;
        _isEnabled = model.IsEnabled;
        _selectedWindowState = model.Placement?.WindowState ?? "Normal";
    }

    public void ApplyChanges()
    {
        Model.LaunchArguments = LaunchArguments;
        Model.LaunchDelayMs = LaunchDelayMs;
        Model.IsEnabled = IsEnabled;
        if (Model.Placement == null)
        {
            Model.Placement = new WindowPlacement();
        }
        Model.Placement.WindowState = SelectedWindowState;
    }
}

public partial class WorkspaceDetailsViewModel : ObservableObject
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceRestoreService _restoreService;
    private readonly IWindowsProcessService _processService;
    private readonly IConfigurationRepository _configRepo;
    private readonly IApplicationCatalogService _catalogService;
    private readonly IMonitorManagementService _monitorService;
    private readonly Action<NavigationPage, object?> _navigateAction;

    [ObservableProperty]
    private Workspace? _workspace;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _selectedCategory = "Custom";

    [ObservableProperty]
    private bool _isRestoring;

    [ObservableProperty]
    private string _restoringMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isAddAppOpen;

    [ObservableProperty]
    private string _appSearchQuery = string.Empty;

    private readonly List<CatalogAppItemViewModel> _allCatalogApps = new();

    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<WorkspaceAppItemViewModel> Applications { get; } = new();
    public ObservableCollection<CatalogAppItemViewModel> FilteredCatalogApps { get; } = new();

    public WorkspaceDetailsViewModel(
        IWorkspaceRepository repository,
        IWorkspaceRestoreService restoreService,
        IWindowsProcessService processService,
        IConfigurationRepository configRepo,
        IApplicationCatalogService catalogService,
        IMonitorManagementService monitorService,
        Action<NavigationPage, object?> navigateAction)
    {
        _repository = repository;
        _restoreService = restoreService;
        _processService = processService;
        _configRepo = configRepo;
        _catalogService = catalogService;
        _monitorService = monitorService;
        _navigateAction = navigateAction;
    }

    public async Task InitializeAsync(Workspace ws)
    {
        Workspace = ws;
        Name = ws.Name;
        Description = ws.Description;
        SelectedCategory = ws.Category;
        StatusMessage = string.Empty;
        IsAddAppOpen = false;
        AppSearchQuery = string.Empty;

        var settings = await _configRepo.LoadSettingsAsync();
        Categories.Clear();
        foreach (var c in settings.Categories) Categories.Add(c);
        if (!Categories.Contains(SelectedCategory)) Categories.Add(SelectedCategory);

        Applications.Clear();
        foreach (var app in ws.Applications.OrderBy(a => a.LaunchOrder))
        {
            Applications.Add(new WorkspaceAppItemViewModel(app));
        }
    }

    [RelayCommand]
    private async Task ToggleAddAppAsync()
    {
        IsAddAppOpen = !IsAddAppOpen;
        if (IsAddAppOpen && _allCatalogApps.Count == 0)
        {
            await LoadCatalogAsync();
        }
    }

    [RelayCommand]
    private void CloseAddApp()
    {
        IsAddAppOpen = false;
    }

    private async Task LoadCatalogAsync()
    {
        try
        {
            var installed = await _catalogService.GetInstalledApplicationsAsync();
            _allCatalogApps.Clear();
            foreach (var app in installed)
            {
                _allCatalogApps.Add(new CatalogAppItemViewModel(app));
            }
            ApplyCatalogFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load installed apps: {ex.Message}";
        }
    }

    partial void OnAppSearchQueryChanged(string value)
    {
        ApplyCatalogFilter();
    }

    private void ApplyCatalogFilter()
    {
        FilteredCatalogApps.Clear();
        var query = AppSearchQuery.Trim();
        var items = string.IsNullOrEmpty(query)
            ? _allCatalogApps
            : _allCatalogApps.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredCatalogApps.Add(item);
        }
    }

    [RelayCommand]
    private void AddCatalogApp(CatalogAppItemViewModel? item)
    {
        if (item == null || Workspace == null) return;

        var primary = _monitorService.GetPrimaryMonitor();
        int width = primary != null && primary.WorkWidth > 0 ? (int)(primary.WorkWidth * 0.75) : 1280;
        int height = primary != null && primary.WorkHeight > 0 ? (int)(primary.WorkHeight * 0.75) : 720;
        int left = primary != null ? primary.WorkLeft + 60 : 100;
        int top = primary != null ? primary.WorkTop + 60 : 100;

        var app = new WorkspaceApplication
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Workspace.Id,
            ExecutablePath = item.ExecutablePath,
            ProcessName = item.ProcessName,
            WindowTitle = item.Name,
            LaunchArguments = item.Arguments,
            LaunchOrder = Applications.Count,
            LaunchDelayMs = 250,
            IsEnabled = true,
            Placement = new WindowPlacement
            {
                Id = Guid.NewGuid(),
                X = left,
                Y = top,
                Width = width,
                Height = height,
                WindowState = "Normal",
                MonitorDeviceId = primary?.DeviceId,
                MonitorBounds = primary?.BoundsString
            }
        };

        Applications.Add(new WorkspaceAppItemViewModel(app));
        StatusMessage = $"Added {item.Name} to workspace.";
    }

    [RelayCommand]
    private void BrowseApp()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select Application Executable or Shortcut",
            Filter = "Executables and Shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            var resolved = _catalogService.ResolveApplicationFromPath(openFileDialog.FileName);
            if (resolved != null)
            {
                AddCatalogApp(new CatalogAppItemViewModel(resolved));
            }
            else
            {
                string path = openFileDialog.FileName;
                string procName = Path.GetFileNameWithoutExtension(path);
                var customInfo = new CatalogApplicationInfo
                {
                    Name = procName,
                    ExecutablePath = path,
                    ProcessName = procName,
                    IconGlyph = ApplicationCatalogService.GetAppGlyph(procName),
                    Source = "Custom"
                };
                AddCatalogApp(new CatalogAppItemViewModel(customInfo));
            }
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Workspace == null) return;

        Workspace.Name = Name;
        Workspace.Description = Description;
        Workspace.Category = SelectedCategory;
        Workspace.UpdatedAt = DateTime.UtcNow;

        for (int i = 0; i < Applications.Count; i++)
        {
            var item = Applications[i];
            item.ApplyChanges();
            item.Model.LaunchOrder = i;
        }

        Workspace.Applications = Applications.Select(a => a.Model).ToList();
        await _repository.SaveWorkspaceAsync(Workspace);
        StatusMessage = "Workspace saved successfully.";
    }

    [RelayCommand]
    private async Task RestoreNowAsync()
    {
        if (Workspace == null) return;
        await SaveAsync();

        IsRestoring = true;
        RestoringMessage = $"Restoring {Workspace.Name}...";

        try
        {
            var progress = new Progress<RestorationProgress>(p =>
            {
                RestoringMessage = p.Message;
            });

            var session = await _restoreService.RestoreWorkspaceAsync(Workspace, progress: progress);
            StatusMessage = $"Restoration completed with status: {session.Status}";
        }
        finally
        {
            IsRestoring = false;
            RestoringMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void MoveAppUp(WorkspaceAppItemViewModel? item)
    {
        if (item == null) return;
        int index = Applications.IndexOf(item);
        if (index > 0)
        {
            Applications.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveAppDown(WorkspaceAppItemViewModel? item)
    {
        if (item == null) return;
        int index = Applications.IndexOf(item);
        if (index < Applications.Count - 1)
        {
            Applications.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void RemoveApp(WorkspaceAppItemViewModel? item)
    {
        if (item == null) return;
        Applications.Remove(item);
    }

    [RelayCommand]
    private void TestLaunch(WorkspaceAppItemViewModel? item)
    {
        if (item == null) return;
        _processService.LaunchApplication(item.ExecutablePath, item.LaunchArguments);
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigateAction(NavigationPage.Workspaces, null);
    }
}
