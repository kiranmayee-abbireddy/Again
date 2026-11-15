namespace Encore.Core.Domain;

public class AppSettings
{
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public string Theme { get; set; } = "Dark"; // "Dark", "Light", "System"
    
    // Restoration defaults
    public bool DefaultSkipAlreadyRunning { get; set; } = false;
    public bool DefaultRepositionExistingWindows { get; set; } = true;
    public bool DefaultLaunchMissing { get; set; } = true;
    public int DefaultLaunchTimeoutSeconds { get; set; } = 15;
    public int DefaultLaunchDelayMs { get; set; } = 300;

    // Shortcuts
    public string CaptureHotkey { get; set; } = "Ctrl+Alt+C";
    public string RestoreLastHotkey { get; set; } = "Ctrl+Alt+R";
    public string ShowHideHotkey { get; set; } = "Ctrl+Alt+E";
    public string QuickSelectorHotkey { get; set; } = "Ctrl+Alt+W";

    // Categories
    public List<string> Categories { get; set; } = new()
    {
        "Development",
        "Meetings",
        "Learning",
        "Design",
        "Personal",
        "Custom"
    };
}
