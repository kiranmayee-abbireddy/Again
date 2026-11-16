using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Encore.Core.Domain;
using Encore.Core.Storage;
using Microsoft.Win32;

namespace Encore.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "AgainWorkspaceManager";

    private readonly IConfigurationRepository _configRepo;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private bool _skipAlreadyRunning;

    [ObservableProperty]
    private bool _repositionExistingWindows = true;

    [ObservableProperty]
    private bool _launchMissing = true;

    [ObservableProperty]
    private int _launchTimeoutSeconds = 15;

    [ObservableProperty]
    private int _launchDelayMs = 300;

    [ObservableProperty]
    private string _captureHotkey = "Ctrl+Alt+C";

    [ObservableProperty]
    private string _restoreLastHotkey = "Ctrl+Alt+R";

    [ObservableProperty]
    private string _showHideHotkey = "Ctrl+Alt+E";

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> Themes { get; } = new()
    {
        "Emerald (Dark)",
        "Emerald (Light)",
        "Nordic Teal (Dark)",
        "Nordic Teal (Light)",
        "Solar Amber (Dark)",
        "Solar Amber (Light)",
        "Rosewood (Dark)",
        "Rosewood (Light)"
    };
    public ObservableCollection<string> Categories { get; } = new();

    public Action<string>? ThemeChanged { get; set; }

    public SettingsViewModel(IConfigurationRepository configRepo)
    {
        _configRepo = configRepo;
    }

    public async Task LoadAsync()
    {
        var settings = await _configRepo.LoadSettingsAsync();

        MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        StartWithWindows = IsStartWithWindowsEnabled();
        SelectedTheme = settings.Theme switch
        {
            "Dark" => "Emerald (Dark)",
            "Light" => "Emerald (Light)",
            _ => string.IsNullOrEmpty(settings.Theme) ? "Emerald (Dark)" : settings.Theme
        };
        SkipAlreadyRunning = settings.DefaultSkipAlreadyRunning;
        RepositionExistingWindows = settings.DefaultRepositionExistingWindows;
        LaunchMissing = settings.DefaultLaunchMissing;
        LaunchTimeoutSeconds = settings.DefaultLaunchTimeoutSeconds;
        LaunchDelayMs = settings.DefaultLaunchDelayMs;
        CaptureHotkey = settings.CaptureHotkey;
        RestoreLastHotkey = settings.RestoreLastHotkey;
        ShowHideHotkey = settings.ShowHideHotkey;

        Categories.Clear();
        foreach (var c in settings.Categories)
        {
            Categories.Add(c);
        }

        StatusMessage = string.Empty;
    }

    partial void OnSelectedThemeChanged(string value)
    {
        ThemeChanged?.Invoke(value);
    }

    [RelayCommand]
    private void AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;

        var trimmed = NewCategoryName.Trim();
        if (!Categories.Contains(trimmed))
        {
            Categories.Add(trimmed);
            NewCategoryName = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        if (Categories.Count > 1)
        {
            Categories.Remove(category);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            StartWithWindows = StartWithWindows,
            Theme = SelectedTheme,
            DefaultSkipAlreadyRunning = SkipAlreadyRunning,
            DefaultRepositionExistingWindows = RepositionExistingWindows,
            DefaultLaunchMissing = LaunchMissing,
            DefaultLaunchTimeoutSeconds = Math.Max(3, LaunchTimeoutSeconds),
            DefaultLaunchDelayMs = Math.Max(0, LaunchDelayMs),
            CaptureHotkey = CaptureHotkey,
            RestoreLastHotkey = RestoreLastHotkey,
            ShowHideHotkey = ShowHideHotkey,
            Categories = Categories.ToList()
        };

        SetStartWithWindows(StartWithWindows);
        await _configRepo.SaveSettingsAsync(settings);
        StatusMessage = "Settings saved successfully.";
    }

    private static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppRegistryName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppRegistryName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppRegistryName, false);
            }
        }
        catch
        {
            // Ignore registry permissions if running unprivileged
        }
    }
}
