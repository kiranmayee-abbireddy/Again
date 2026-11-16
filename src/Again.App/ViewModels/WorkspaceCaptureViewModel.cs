using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Again.Core.Domain;
using Again.Core.Services;
using Again.Core.Storage;
using Microsoft.Win32;

namespace Again.App.ViewModels;

public class CatalogAppItemViewModel
{
    public CatalogApplicationInfo Info { get; }

    public string Name => Info.Name;
    public string ExecutablePath => Info.ExecutablePath;
    public string ProcessName => Info.ProcessName;
    public string? Arguments => Info.Arguments;
    public string IconGlyph => Info.IconGlyph;
    public string Source => Info.Source;

    public CatalogAppItemViewModel(CatalogApplicationInfo info)
    {
        Info = info;
    }
}

public partial class CapturableWindowItemViewModel : ObservableObject
{
    public WorkspaceApplication Application { get; }

    public string ProcessName => Application.ProcessName;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Application.WindowTitle) ? Application.ProcessName : Application.WindowTitle;
    public string ExecutablePath => Application.ExecutablePath;

    public bool IsRunning { get; }
    public bool IsNotRunning => !IsRunning;
    public string StatusBadgeText => IsRunning ? "Active Window" : "Not Running";

    public string IconGlyph => ApplicationCatalogService.GetAppGlyph(ProcessName, DisplayTitle);

    public string? BrowserProfile => Application.BrowserProfile;
    public bool HasBrowserProfile => !string.IsNullOrEmpty(Application.BrowserProfile);

    public string? FolderViewPath => Application.FolderViewPath;
    public bool HasFolderViewPath => !string.IsNullOrEmpty(Application.FolderViewPath);

    public int TabCount
    {
        get
        {
            if (string.IsNullOrEmpty(Application.BrowserTabsJson)) return 0;
            try
            {
                var tabs = System.Text.Json.JsonSerializer.Deserialize<List<BrowserTabInfo>>(Application.BrowserTabsJson);
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
            if (string.IsNullOrEmpty(Application.BrowserTabsJson)) return string.Empty;
            try
            {
                var tabs = System.Text.Json.JsonSerializer.Deserialize<List<BrowserTabInfo>>(Application.BrowserTabsJson);
                if (tabs == null || tabs.Count == 0) return string.Empty;
                return $"{tabs.Count} tab{(tabs.Count > 1 ? "s" : "")}: " + string.Join(", ", tabs.Take(3).Select(t => t.Title));
            }
            catch { return string.Empty; }
        }
    }

    [ObservableProperty]
    private bool _isSelected = true;

    public string PlacementText => Application.Placement != null
        ? $"{Application.Placement.Width}×{Application.Placement.Height} at ({Application.Placement.X}, {Application.Placement.Y}) [{Application.Placement.WindowState}]"
        : "Default bounds (Normal)";

    public string MonitorText => MonitorInfoModel.FormatFriendlyName(Application.Placement?.MonitorDeviceId, true);

    public CapturableWindowItemViewModel(WorkspaceApplication app, bool isRunning = true)
    {
        Application = app;
        IsRunning = isRunning;
    }
}

public partial class WorkspaceCaptureViewModel : ObservableObject
{
    private readonly IWorkspaceCaptureService _captureService;
    private readonly IConfigurationRepository _configRepo;
    private readonly IApplicationCatalogService _catalogService;
    private readonly IMonitorManagementService _monitorService;
    private readonly Action<NavigationPage, object?> _navigateAction;

    [ObservableProperty]
    private string _workspaceName = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _selectedCategory = "Development";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isAppPickerOpen;

    [ObservableProperty]
    private string _catalogSearchQuery = string.Empty;

    private readonly List<CatalogAppItemViewModel> _allCatalogApps = new();

    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<CapturableWindowItemViewModel> DetectedWindows { get; } = new();
    public ObservableCollection<CatalogAppItemViewModel> FilteredCatalogApps { get; } = new();

    public string SummaryCountText => $"{DetectedWindows.Count(w => w.IsSelected)} selected of {DetectedWindows.Count} total";

    public WorkspaceCaptureViewModel(
        IWorkspaceCaptureService captureService,
        IConfigurationRepository configRepo,
        IApplicationCatalogService catalogService,
        IMonitorManagementService monitorService,
        Action<NavigationPage, object?> navigateAction)
    {
        _captureService = captureService;
        _configRepo = configRepo;
        _catalogService = catalogService;
        _monitorService = monitorService;
        _navigateAction = navigateAction;
    }

    public async Task InitializeAsync()
    {
        WorkspaceName = $"Workspace {DateTime.Now:MMM dd - HH:mm}";
        Description = string.Empty;
        ErrorMessage = string.Empty;
        IsAppPickerOpen = false;
        CatalogSearchQuery = string.Empty;

        var settings = await _configRepo.LoadSettingsAsync();
        Categories.Clear();
        foreach (var c in settings.Categories) Categories.Add(c);
        SelectedCategory = Categories.FirstOrDefault() ?? "Development";

        await ScanWindowsAsync();
    }

    [RelayCommand]
    private async Task ScanWindowsAsync()
    {
        IsScanning = true;
        ErrorMessage = string.Empty;
        try
        {
            var nonRunning = DetectedWindows.Where(w => w.IsNotRunning).ToList();
            var apps = await _captureService.CaptureCurrentWindowsAsync();
            DetectedWindows.Clear();

            foreach (var app in apps)
            {
                var vm = new CapturableWindowItemViewModel(app, isRunning: true);
                vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CapturableWindowItemViewModel.IsSelected)) OnPropertyChanged(nameof(SummaryCountText)); };
                DetectedWindows.Add(vm);
            }

            foreach (var item in nonRunning)
            {
                item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CapturableWindowItemViewModel.IsSelected)) OnPropertyChanged(nameof(SummaryCountText)); };
                DetectedWindows.Add(item);
            }

            OnPropertyChanged(nameof(SummaryCountText));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to scan active windows: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAppPickerAsync()
    {
        IsAppPickerOpen = !IsAppPickerOpen;
        if (IsAppPickerOpen && _allCatalogApps.Count == 0)
        {
            await LoadCatalogAsync();
        }
    }

    [RelayCommand]
    private void CloseAppPicker()
    {
        IsAppPickerOpen = false;
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
            ErrorMessage = $"Failed to load installed applications: {ex.Message}";
        }
    }

    partial void OnCatalogSearchQueryChanged(string value)
    {
        ApplyCatalogFilter();
    }

    private void ApplyCatalogFilter()
    {
        FilteredCatalogApps.Clear();
        var query = CatalogSearchQuery.Trim();
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
        if (item == null) return;

        // Check if already in detected windows
        var existing = DetectedWindows.FirstOrDefault(w => 
            string.Equals(w.ExecutablePath, item.ExecutablePath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.IsSelected = true;
            ErrorMessage = $"{item.Name} is already in the list (marked as selected).";
            OnPropertyChanged(nameof(SummaryCountText));
            return;
        }

        var primary = _monitorService.GetPrimaryMonitor();
        int width = primary != null && primary.WorkWidth > 0 ? (int)(primary.WorkWidth * 0.75) : 1280;
        int height = primary != null && primary.WorkHeight > 0 ? (int)(primary.WorkHeight * 0.75) : 720;
        int left = primary != null ? primary.WorkLeft + 60 : 100;
        int top = primary != null ? primary.WorkTop + 60 : 100;

        var app = new WorkspaceApplication
        {
            Id = Guid.NewGuid(),
            ExecutablePath = item.ExecutablePath,
            ProcessName = item.ProcessName,
            WindowTitle = item.Name,
            LaunchArguments = item.Arguments,
            LaunchOrder = DetectedWindows.Count,
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

        var vm = new CapturableWindowItemViewModel(app, isRunning: false);
        vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CapturableWindowItemViewModel.IsSelected)) OnPropertyChanged(nameof(SummaryCountText)); };
        DetectedWindows.Add(vm);
        OnPropertyChanged(nameof(SummaryCountText));

        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void BrowseCustomApp()
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
    private void RemoveWindow(CapturableWindowItemViewModel? item)
    {
        if (item != null)
        {
            DetectedWindows.Remove(item);
            OnPropertyChanged(nameof(SummaryCountText));
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in DetectedWindows) item.IsSelected = true;
        OnPropertyChanged(nameof(SummaryCountText));
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in DetectedWindows) item.IsSelected = false;
        OnPropertyChanged(nameof(SummaryCountText));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceName))
        {
            ErrorMessage = "Please provide a name for this workspace.";
            return;
        }

        var selected = DetectedWindows
            .Where(w => w.IsSelected)
            .Select(w => w.Application)
            .ToList();

        if (selected.Count == 0)
        {
            ErrorMessage = "Please select at least one window or application to capture.";
            return;
        }

        try
        {
            var created = await _captureService.CreateAndSaveWorkspaceAsync(
                WorkspaceName.Trim(),
                SelectedCategory,
                Description?.Trim(),
                null,
                selected);

            _navigateAction(NavigationPage.WorkspaceDetails, created);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save workspace: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigateAction(NavigationPage.Workspaces, null);
    }
}
