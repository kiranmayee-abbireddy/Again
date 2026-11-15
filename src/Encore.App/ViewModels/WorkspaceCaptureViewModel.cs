using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;

namespace Encore.App.ViewModels;

public partial class CapturableWindowItemViewModel : ObservableObject
{
    public WorkspaceApplication Application { get; }

    public string ProcessName => Application.ProcessName;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Application.WindowTitle) ? Application.ProcessName : Application.WindowTitle;
    public string ExecutablePath => Application.ExecutablePath;

    [ObservableProperty]
    private bool _isSelected = true;

    public string PlacementText => Application.Placement != null
        ? $"{Application.Placement.Width}×{Application.Placement.Height} at ({Application.Placement.X}, {Application.Placement.Y}) [{Application.Placement.WindowState}]"
        : "Default bounds";

    public string MonitorText => Application.Placement?.MonitorDeviceId ?? "Primary Monitor";

    public CapturableWindowItemViewModel(WorkspaceApplication app)
    {
        Application = app;
    }
}

public partial class WorkspaceCaptureViewModel : ObservableObject
{
    private readonly IWorkspaceCaptureService _captureService;
    private readonly IConfigurationRepository _configRepo;
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

    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<CapturableWindowItemViewModel> DetectedWindows { get; } = new();

    public WorkspaceCaptureViewModel(
        IWorkspaceCaptureService captureService,
        IConfigurationRepository configRepo,
        Action<NavigationPage, object?> navigateAction)
    {
        _captureService = captureService;
        _configRepo = configRepo;
        _navigateAction = navigateAction;
    }

    public async Task InitializeAsync()
    {
        WorkspaceName = $"Workspace {DateTime.Now:MMM dd - HH:mm}";
        Description = string.Empty;
        ErrorMessage = string.Empty;

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
            var apps = await _captureService.CaptureCurrentWindowsAsync();
            DetectedWindows.Clear();
            foreach (var app in apps)
            {
                DetectedWindows.Add(new CapturableWindowItemViewModel(app));
            }
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
    private void SelectAll()
    {
        foreach (var item in DetectedWindows) item.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in DetectedWindows) item.IsSelected = false;
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
            ErrorMessage = "Please select at least one window to capture.";
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
