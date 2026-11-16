using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Again.Core.Domain;
using Again.Core.Storage;
using Microsoft.Win32;

namespace Again.App.ViewModels;

public partial class ThemeItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string ColorFamily { get; }
    public string ModeText { get; }
    public bool IsDark { get; }
    public SolidColorBrush AccentBrush { get; }
    public SolidColorBrush BgBrush { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ThemeItemViewModel(
        string id, 
        string name, 
        string colorFamily, 
        string modeText, 
        bool isDark, 
        string accentHex, 
        string bgHex, 
        string description)
    {
        Id = id;
        Name = name;
        ColorFamily = colorFamily;
        ModeText = modeText;
        IsDark = isDark;
        
        var accentColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(accentHex);
        AccentBrush = new SolidColorBrush(accentColor);
        AccentBrush.Freeze();

        var bgColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(bgHex);
        BgBrush = new SolidColorBrush(bgColor);
        BgBrush.Freeze();

        Description = description;
    }
}

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
    private string _selectedTheme = "Emerald (Dark)";

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

    public ObservableCollection<ThemeItemViewModel> ThemeOptions { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public Action<string>? ThemeChanged { get; set; }

    public SettingsViewModel(IConfigurationRepository configRepo)
    {
        _configRepo = configRepo;
        InitializeThemeOptions();
    }

    private void InitializeThemeOptions()
    {
        ThemeOptions.Clear();
        // Emerald Dark (Default)
        ThemeOptions.Add(new ThemeItemViewModel(
            "Emerald (Dark)", "Emerald Dark", "Emerald", "Dark", true,
            "#10B981", "#0D1117", "Obsidian dark with vivid emerald mint (Default)"));

        // Emerald Light
        ThemeOptions.Add(new ThemeItemViewModel(
            "Emerald (Light)", "Emerald Light", "Emerald", "Light", false,
            "#059669", "#F8FAFC", "Clean crisp white with vibrant emerald"));

        // Nordic Teal Dark
        ThemeOptions.Add(new ThemeItemViewModel(
            "Nordic Teal (Dark)", "Nordic Teal Dark", "Teal", "Dark", true,
            "#06B6D4", "#0A0E17", "Arctic deep night with electric cyan-teal"));

        // Nordic Teal Light
        ThemeOptions.Add(new ThemeItemViewModel(
            "Nordic Teal (Light)", "Nordic Teal Light", "Teal", "Light", false,
            "#06B6D4", "#F8FAFC", "Clean frost light with cyan-teal accent"));

        // Solar Amber Dark
        ThemeOptions.Add(new ThemeItemViewModel(
            "Solar Amber (Dark)", "Solar Amber Dark", "Amber", "Dark", true,
            "#F59E0B", "#121214", "Charcoal zinc with golden solar amber"));

        // Solar Amber Light
        ThemeOptions.Add(new ThemeItemViewModel(
            "Solar Amber (Light)", "Solar Amber Light", "Amber", "Light", false,
            "#D97706", "#F8FAFC", "Warm daylight surface with rich amber"));

        // Rosewood Dark
        ThemeOptions.Add(new ThemeItemViewModel(
            "Rosewood (Dark)", "Rosewood Dark", "Rose", "Dark", true,
            "#F43F5E", "#140E14", "Midnight plum with crimson rose"));

        // Rosewood Light
        ThemeOptions.Add(new ThemeItemViewModel(
            "Rosewood (Light)", "Rosewood Light", "Rose", "Light", false,
            "#E11D48", "#F8FAFC", "Soft porcelain light with rich rosewood"));
    }

    [RelayCommand]
    public void SelectTheme(ThemeItemViewModel? theme)
    {
        if (theme == null) return;
        SelectedTheme = theme.Id;
    }

    private void UpdateThemeSelection()
    {
        foreach (var t in ThemeOptions)
        {
            t.IsSelected = string.Equals(t.Id, SelectedTheme, StringComparison.OrdinalIgnoreCase);
        }
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
        UpdateThemeSelection();

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
        UpdateThemeSelection();
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
