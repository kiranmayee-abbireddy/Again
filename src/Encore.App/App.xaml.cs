using System.Windows;
using Encore.App.Services;
using Encore.App.ViewModels;
using Encore.Core.Native;
using Encore.Core.Services;
using Encore.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Encore.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services => ((App)Current)._serviceProvider!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure PerMonitorV2 DPI awareness
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch
        {
            // Fallback gracefully on older OS versions
        }

        var services = new ServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
        });

        // App Data Paths
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string encoreDir = Path.Combine(localAppData, "Encore");
        string dbPath = Path.Combine(encoreDir, "encore.db");
        string settingsPath = Path.Combine(encoreDir, "settings.json");

        // Storage
        services.AddSingleton<IWorkspaceRepository>(sp =>
            new SqliteWorkspaceRepository(dbPath, sp.GetService<ILogger<SqliteWorkspaceRepository>>()));
        services.AddSingleton<IConfigurationRepository>(sp =>
            new ConfigurationRepository(settingsPath, sp.GetService<ILogger<ConfigurationRepository>>()));

        // Infrastructure & Native Services
        services.AddSingleton<IMonitorManagementService, WindowsMonitorService>();
        services.AddSingleton<IWindowsProcessService, WindowsProcessService>();
        services.AddSingleton<IWindowsWindowService, WindowsWindowService>();
        services.AddSingleton<IShortcutManagementService, WindowsShortcutService>();
        services.AddSingleton<ISystemTrayManager, SystemTrayManager>();

        // Application Services
        services.AddSingleton<IWorkspaceCaptureService, WorkspaceCaptureService>();
        services.AddSingleton<IWorkspaceRestoreService, WorkspaceRestoreService>();
        services.AddSingleton<IRestorationHistoryService, RestorationHistoryService>();

        // ViewModels
        services.AddTransient<DashboardViewModel>(sp =>
            new DashboardViewModel(
                sp.GetRequiredService<IWorkspaceRepository>(),
                sp.GetRequiredService<IWorkspaceRestoreService>(),
                sp.GetRequiredService<IMonitorManagementService>(),
                sp.GetRequiredService<IWindowsWindowService>(),
                (page, param) => _ = sp.GetRequiredService<MainViewModel>().NavigateToPageAsync(page, param)));

        services.AddTransient<WorkspaceLibraryViewModel>(sp =>
            new WorkspaceLibraryViewModel(
                sp.GetRequiredService<IWorkspaceRepository>(),
                sp.GetRequiredService<IWorkspaceRestoreService>(),
                sp.GetRequiredService<IConfigurationRepository>(),
                (page, param) => _ = sp.GetRequiredService<MainViewModel>().NavigateToPageAsync(page, param)));

        services.AddTransient<WorkspaceDetailsViewModel>(sp =>
            new WorkspaceDetailsViewModel(
                sp.GetRequiredService<IWorkspaceRepository>(),
                sp.GetRequiredService<IWorkspaceRestoreService>(),
                sp.GetRequiredService<IWindowsProcessService>(),
                sp.GetRequiredService<IConfigurationRepository>(),
                (page, param) => _ = sp.GetRequiredService<MainViewModel>().NavigateToPageAsync(page, param)));

        services.AddTransient<WorkspaceCaptureViewModel>(sp =>
            new WorkspaceCaptureViewModel(
                sp.GetRequiredService<IWorkspaceCaptureService>(),
                sp.GetRequiredService<IConfigurationRepository>(),
                (page, param) => _ = sp.GetRequiredService<MainViewModel>().NavigateToPageAsync(page, param)));

        services.AddTransient<ApplicationManagerViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<MainViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
