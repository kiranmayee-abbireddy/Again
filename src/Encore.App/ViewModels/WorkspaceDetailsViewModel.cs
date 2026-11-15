using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;

namespace Encore.App.ViewModels;

public partial class WorkspaceAppItemViewModel : ObservableObject
{
    public WorkspaceApplication Model { get; }

    public Guid Id => Model.Id;
    public string ExecutablePath => Model.ExecutablePath;
    public string ProcessName => Model.ProcessName;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Model.WindowTitle) ? Model.ProcessName : Model.WindowTitle;

    [ObservableProperty]
    private string? _launchArguments;

    [ObservableProperty]
    private int _launchDelayMs;

    [ObservableProperty]
    private bool _isEnabled;

    public string WindowBoundsText => Model.Placement != null
        ? $"{Model.Placement.Width} × {Model.Placement.Height} at ({Model.Placement.X}, {Model.Placement.Y}) [{Model.Placement.WindowState}]"
        : "Default window placement";

    public string MonitorText => !string.IsNullOrEmpty(Model.Placement?.MonitorDeviceId)
        ? Model.Placement.MonitorDeviceId
        : "Primary Monitor";

    public WorkspaceAppItemViewModel(WorkspaceApplication model)
    {
        Model = model;
        _launchArguments = model.LaunchArguments;
        _launchDelayMs = model.LaunchDelayMs;
        _isEnabled = model.IsEnabled;
    }

    public void ApplyChanges()
    {
        Model.LaunchArguments = LaunchArguments;
        Model.LaunchDelayMs = LaunchDelayMs;
        Model.IsEnabled = IsEnabled;
    }
}

public partial class WorkspaceDetailsViewModel : ObservableObject
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceRestoreService _restoreService;
    private readonly IWindowsProcessService _processService;
    private readonly IConfigurationRepository _configRepo;
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

    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<WorkspaceAppItemViewModel> Applications { get; } = new();

    public WorkspaceDetailsViewModel(
        IWorkspaceRepository repository,
        IWorkspaceRestoreService restoreService,
        IWindowsProcessService processService,
        IConfigurationRepository configRepo,
        Action<NavigationPage, object?> navigateAction)
    {
        _repository = repository;
        _restoreService = restoreService;
        _processService = processService;
        _configRepo = configRepo;
        _navigateAction = navigateAction;
    }

    public async Task InitializeAsync(Workspace ws)
    {
        Workspace = ws;
        Name = ws.Name;
        Description = ws.Description;
        SelectedCategory = ws.Category;
        StatusMessage = string.Empty;

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
