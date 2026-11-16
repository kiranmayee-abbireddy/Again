using System.Windows;
using System.Windows.Interop;
using Again.App.Services;
using Again.App.ViewModels;
using Again.Core.Native;
using Again.Core.Services;
using Again.Core.Storage;

namespace Again.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISystemTrayManager _trayManager;
    private readonly IShortcutManagementService _shortcutService;
    private readonly IWorkspaceRepository _repository;
    private readonly IConfigurationRepository _configRepo;
    private HwndSource? _hwndSource;
    private bool _hasShownTrayNotice;

    public MainWindow(
        MainViewModel viewModel,
        ISystemTrayManager trayManager,
        IShortcutManagementService shortcutService,
        IWorkspaceRepository repository,
        IConfigurationRepository configRepo)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _trayManager = trayManager;
        _shortcutService = shortcutService;
        _repository = repository;
        _configRepo = configRepo;

        DataContext = _viewModel;

        _viewModel.ThemeChangeRequested = ApplyTheme;
        _viewModel.ToggleWindowVisibilityRequested = ToggleVisibility;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreButton();
    }

    private void UpdateMaximizeRestoreButton()
    {
        if (WindowState == WindowState.Maximized)
        {
            MaximizeIcon.Visibility = Visibility.Collapsed;
            RestoreIcon.Visibility = Visibility.Visible;
            MaximizeButton.ToolTip = "Restore";
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.Margin = new Thickness(7);
        }
        else
        {
            MaximizeIcon.Visibility = Visibility.Visible;
            RestoreIcon.Visibility = Visibility.Collapsed;
            MaximizeButton.ToolTip = "Maximize";
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.Margin = new Thickness(0);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            var hWnd = helper.Handle;

            _hwndSource = HwndSource.FromHwnd(hWnd);
            _hwndSource?.AddHook(HwndHook);

            _shortcutService.Initialize(hWnd);

            var settings = await _configRepo.LoadSettingsAsync();
            _shortcutService.RegisterShortcut(ShortcutAction.CaptureWorkspace, settings.CaptureHotkey);
            _shortcutService.RegisterShortcut(ShortcutAction.RestoreLastUsed, settings.RestoreLastHotkey);
            _shortcutService.RegisterShortcut(ShortcutAction.ToggleShowHide, settings.ShowHideHotkey);
            _shortcutService.RegisterShortcut(ShortcutAction.QuickSelector, settings.QuickSelectorHotkey);

            _trayManager.Initialize(
                this,
                quickRestoreLastAction: async () => await _viewModel.QuickRestoreLastUsedAsync(),
                openCaptureAction: async () => await _viewModel.NavigateToPageAsync(NavigationPage.CaptureWorkspace),
                openSettingsAction: async () => await _viewModel.NavigateToPageAsync(NavigationPage.Settings));

            var allWorkspaces = await _repository.GetAllWorkspacesAsync();
            _trayManager.RefreshPinnedMenu(allWorkspaces.Where(w => w.IsPinned));

            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string crashLog = System.IO.Path.Combine(localAppData, "Again", "startup.log");
            try { System.IO.File.AppendAllText(crashLog, $"[MainWindow_Loaded_Error] {ex}\n"); } catch { }
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = await _configRepo.LoadSettingsAsync();
        if (settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();

            if (!_hasShownTrayNotice)
            {
                _trayManager.ShowNotification("Again", "Again is running in the system tray. Double-click the tray icon or press Ctrl+Alt+E to reopen.");
                _hasShownTrayNotice = true;
            }
        }
        else
        {
            _shortcutService.UnregisterAll();
            _trayManager.Dispose();
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            _shortcutService.ProcessHotkeyMessage(hotkeyId);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    public void ApplyTheme(string theme)
    {
        string stylePath = theme switch
        {
            "Emerald (Light)" or "Light" => "Styles/LightTheme.xaml",
            "Nordic Teal (Dark)" or "Teal Dark" => "Styles/TealDark.xaml",
            "Nordic Teal (Light)" or "Teal Light" => "Styles/TealLight.xaml",
            "Solar Amber (Dark)" or "Amber Dark" => "Styles/AmberDark.xaml",
            "Solar Amber (Light)" or "Amber Light" => "Styles/AmberLight.xaml",
            "Rosewood (Dark)" or "Rosewood Dark" => "Styles/RosewoodDark.xaml",
            "Rosewood (Light)" or "Rosewood Light" => "Styles/RosewoodLight.xaml",
            _ => "Styles/DarkTheme.xaml" // Emerald (Dark) is default
        };

        var uri = new Uri(stylePath, UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0)
        {
            merged[0] = dict;
        }
        else
        {
            merged.Add(dict);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hwndSource?.RemoveHook(HwndHook);
        _hwndSource?.Dispose();
        _shortcutService.Dispose();
        _trayManager.Dispose();
        base.OnClosed(e);
    }
}
