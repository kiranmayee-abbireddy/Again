using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Services;
using Microsoft.Win32;

namespace Encore.App.ViewModels;

public partial class RunningWindowItemViewModel : ObservableObject
{
    public DetectedWindowInfo Info { get; }

    public int ProcessId => Info.ProcessId;
    public string ProcessName => Info.ProcessName;
    public string Title => Info.Title;
    public string ExecutablePath => Info.ExecutablePath;
    public string WindowState => Info.WindowState;
    public string BoundsText => $"{Info.Bounds.Width} × {Info.Bounds.Height} at ({Info.Bounds.Left}, {Info.Bounds.Top})";
    public string MonitorText => Info.Monitor != null 
        ? MonitorInfoModel.FormatFriendlyName(!string.IsNullOrWhiteSpace(Info.Monitor.DeviceName) ? Info.Monitor.DeviceName : Info.Monitor.DeviceId, Info.Monitor.IsPrimary) 
        : "Primary Display";

    public RunningWindowItemViewModel(DetectedWindowInfo info)
    {
        Info = info;
    }
}

public partial class ApplicationManagerViewModel : ObservableObject
{
    private readonly IWindowsWindowService _windowService;
    private readonly IWindowsProcessService _processService;
    private List<DetectedWindowInfo> _allWindows = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalWindowsCount;

    [ObservableProperty]
    private string _manualLaunchPath = string.Empty;

    [ObservableProperty]
    private string _manualLaunchArgs = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<RunningWindowItemViewModel> Windows { get; } = new();

    public ApplicationManagerViewModel(
        IWindowsWindowService windowService,
        IWindowsProcessService processService)
    {
        _windowService = windowService;
        _processService = processService;
    }

    public Task LoadAsync()
    {
        RefreshWindows();
        return Task.CompletedTask;
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        IsLoading = true;
        try
        {
            _allWindows = _windowService.EnumerateVisibleWindows().ToList();
            TotalWindowsCount = _allWindows.Count;
            ApplyFilter();
            StatusMessage = $"Discovered {_allWindows.Count} running top-level windows.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allWindows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered.Where(w =>
                w.ProcessName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                w.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                w.ExecutablePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        Windows.Clear();
        foreach (var w in filtered)
        {
            Windows.Add(new RunningWindowItemViewModel(w));
        }
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select Application Executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            ManualLaunchPath = openFileDialog.FileName;
        }
    }

    [RelayCommand]
    private void LaunchManual()
    {
        if (string.IsNullOrWhiteSpace(ManualLaunchPath))
        {
            StatusMessage = "Please specify an executable path.";
            return;
        }

        var proc = _processService.LaunchApplication(ManualLaunchPath, ManualLaunchArgs);
        if (proc != null)
        {
            StatusMessage = $"Launched application (PID: {proc.Id}).";
            RefreshWindows();
        }
        else
        {
            StatusMessage = $"Failed to launch '{ManualLaunchPath}'.";
        }
    }
}
