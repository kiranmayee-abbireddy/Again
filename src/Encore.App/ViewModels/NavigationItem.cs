using CommunityToolkit.Mvvm.ComponentModel;

namespace Encore.App.ViewModels;

public enum NavigationPage
{
    Dashboard,
    Workspaces,
    Applications,
    History,
    Settings,
    WorkspaceDetails,
    CaptureWorkspace
}

public partial class NavigationItem : ObservableObject
{
    [ObservableProperty]
    private NavigationPage _page;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _glyph = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _badgeCount;
}
