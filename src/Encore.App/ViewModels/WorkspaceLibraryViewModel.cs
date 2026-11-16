using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Encore.Core.Storage;

namespace Encore.App.ViewModels;

public partial class WorkspaceLibraryViewModel : ObservableObject
{
    private readonly IWorkspaceRepository _repository;
    private readonly IWorkspaceRestoreService _restoreService;
    private readonly IConfigurationRepository _configRepo;
    private readonly Action<NavigationPage, object?> _navigateAction;
    private List<Workspace> _allWorkspaces = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private bool _isRestoring;

    [ObservableProperty]
    private string _restoringStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasWorkspaces;

    [ObservableProperty]
    private int _totalWorkspacesCount;

    public ObservableCollection<string> Categories { get; } = new() { "All" };
    public ObservableCollection<WorkspaceItemViewModel> FilteredWorkspaces { get; } = new();

    public WorkspaceLibraryViewModel(
        IWorkspaceRepository repository,
        IWorkspaceRestoreService restoreService,
        IConfigurationRepository configRepo,
        Action<NavigationPage, object?> navigateAction)
    {
        _repository = repository;
        _restoreService = restoreService;
        _configRepo = configRepo;
        _navigateAction = navigateAction;
    }

    public async Task LoadAsync()
    {
        var settings = await _configRepo.LoadSettingsAsync();
        Categories.Clear();
        Categories.Add("All");
        foreach (var cat in settings.Categories)
        {
            Categories.Add(cat);
        }

        _allWorkspaces = await _repository.GetAllWorkspacesAsync();
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = _allWorkspaces.AsEnumerable();

        if (!string.Equals(SelectedCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(w => string.Equals(w.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered.Where(w =>
                w.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                (w.Description != null && w.Description.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) ||
                w.Applications.Any(a => a.ProcessName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                        (a.WindowTitle != null && a.WindowTitle.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))));
        }

        FilteredWorkspaces.Clear();
        foreach (var ws in filtered.OrderByDescending(w => w.IsPinned).ThenByDescending(w => w.UpdatedAt))
        {
            FilteredWorkspaces.Add(new WorkspaceItemViewModel(ws));
        }

        TotalWorkspacesCount = _allWorkspaces.Count;
        HasWorkspaces = FilteredWorkspaces.Count > 0;
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchQuery = string.Empty;
        SelectedCategory = "All";
    }

    [RelayCommand]
    private async Task TogglePinAsync(WorkspaceItemViewModel? item)
    {
        if (item == null) return;
        bool newPinState = !item.IsPinned;
        await _repository.UpdatePinStatusAsync(item.Id, newPinState);
        item.IsPinned = newPinState;
        item.Model.IsPinned = newPinState;
    }

    [RelayCommand]
    private async Task RestoreAsync(WorkspaceItemViewModel? item)
    {
        if (item == null) return;

        IsRestoring = true;
        RestoringStatusText = $"Restoring {item.Name}...";

        try
        {
            var progress = new Progress<RestorationProgress>(p =>
            {
                RestoringStatusText = p.Message;
            });

            await _restoreService.RestoreWorkspaceAsync(item.Model, progress: progress);
            await LoadAsync();
        }
        finally
        {
            IsRestoring = false;
            RestoringStatusText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task DuplicateAsync(WorkspaceItemViewModel? item)
    {
        if (item == null) return;
        await _repository.DuplicateWorkspaceAsync(item.Id, $"{item.Name} (Copy)");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(WorkspaceItemViewModel? item)
    {
        if (item == null) return;
        await _repository.DeleteWorkspaceAsync(item.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenDetails(WorkspaceItemViewModel? item)
    {
        if (item != null)
        {
            _navigateAction(NavigationPage.WorkspaceDetails, item.Model);
        }
    }

    [RelayCommand]
    private void StartCapture()
    {
        _navigateAction(NavigationPage.CaptureWorkspace, null);
    }
}
