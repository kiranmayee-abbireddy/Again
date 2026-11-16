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

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logDir = Path.Combine(localAppData, "Again");
        Directory.CreateDirectory(logDir);
        string crashLog = Path.Combine(logDir, "startup.log");

        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try { File.AppendAllText(crashLog, $"[UnhandledException] {ev.ExceptionObject}\n"); } catch { }
        };

        DispatcherUnhandledException += (s, ev) =>
        {
            try { File.AppendAllText(crashLog, $"[DispatcherUnhandledException] {ev.Exception}\n"); } catch { }
        };

        try
        {
            File.AppendAllText(crashLog, $"[Startup] Starting Again at {DateTime.UtcNow}\n");

            // Ensure PerMonitorV2 DPI awareness
            try
            {
                NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));
                File.AppendAllText(crashLog, "Step 1: DpiAwareness set\n");
            }
            catch (Exception dpiEx)
            {
                File.AppendAllText(crashLog, $"Step 1: DpiAwareness skipped: {dpiEx.Message}\n");
            }

            var services = new ServiceCollection();
            File.AppendAllText(crashLog, "Step 2a: ServiceCollection created\n");
            ConfigureServices(services);
            File.AppendAllText(crashLog, "Step 2b: ConfigureServices done\n");

            _serviceProvider = services.BuildServiceProvider();
            File.AppendAllText(crashLog, "Step 3: BuildServiceProvider done\n");

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            File.AppendAllText(crashLog, "Step 4: GetRequiredService<MainWindow> done\n");
            mainWindow.Show();
            File.AppendAllText(crashLog, $"Step 5: MainWindow shown successfully\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(crashLog, $"[StartupError] {ex}\n");
            throw;
        }
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
        string againDir = Path.Combine(localAppData, "Again");
        string legacyDir = Path.Combine(localAppData, "Encore");

        if (!Directory.Exists(againDir) && Directory.Exists(legacyDir))
        {
            try
            {
                Directory.Move(legacyDir, againDir);
            }
            catch
            {
                againDir = legacyDir;
            }
        }

        string dbPath = Path.Combine(againDir, "again.db");
        if (!File.Exists(dbPath) && File.Exists(Path.Combine(againDir, "encore.db")))
        {
            try { File.Move(Path.Combine(againDir, "encore.db"), dbPath); } catch { dbPath = Path.Combine(againDir, "encore.db"); }
        }
        string settingsPath = Path.Combine(againDir, "settings.json");

        // Storage
        services.AddSingleton<IWorkspaceRepository>(sp =>
            new SqliteWorkspaceRepository(dbPath, sp.GetService<ILogger<SqliteWorkspaceRepository>>()));
        services.AddSingleton<IConfigurationRepository>(sp =>
            new ConfigurationRepository(settingsPath, sp.GetService<ILogger<ConfigurationRepository>>()));

        // Infrastructure & Native Services
        services.AddSingleton<IMonitorManagementService, WindowsMonitorService>();
        services.AddSingleton<IWindowsProcessService, WindowsProcessService>();
        services.AddSingleton<IWindowsWindowService, WindowsWindowService>();
        services.AddSingleton<IChromiumBrowserService, ChromiumBrowserService>();
        services.AddSingleton<IWindowsExplorerService, WindowsExplorerService>();
        services.AddSingleton<IShortcutManagementService, WindowsShortcutService>();
        services.AddSingleton<ISystemTrayManager, SystemTrayManager>();

        // Application Services
        services.AddSingleton<IApplicationCatalogService, ApplicationCatalogService>();
        services.AddSingleton<IWorkspaceCaptureService, WorkspaceCaptureService>();
        services.AddSingleton<IWorkspaceRestoreService>(sp =>
            new WorkspaceRestoreService(
                sp.GetRequiredService<IWindowsProcessService>(),
                sp.GetRequiredService<IWindowsWindowService>(),
                sp.GetRequiredService<IMonitorManagementService>(),
                sp.GetRequiredService<IWorkspaceRepository>(),
                sp.GetService<ILogger<WorkspaceRestoreService>>(),
                sp.GetRequiredService<IApplicationCatalogService>(),
                sp.GetService<IChromiumBrowserService>(),
                sp.GetService<IWindowsExplorerService>()));
        services.AddSingleton<IRestorationHistoryService, RestorationHistoryService>();

        // ViewModels
        services.AddTransient<DashboardViewModel>(sp =>
            new DashboardViewModel(
                sp.GetRequiredService<IWorkspaceRepository>(),
                sp.GetRequiredService<IWorkspaceRestoreService>(),
                sp.GetRequiredService<IMonitorManagementService>(),
                sp.GetRequiredService<IWindowsWindowService>(),
                (page, param) => _ = sp.GetRequiredService<MainViewModel>().NavigateToPageAsync(page, param),
                sp.GetService<IRestorationHistoryService>()));

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
                sp.GetRequiredService<IApplicationCatalogService>(),
                sp.GetRequiredService<IMonitorManagementService>(),
                (page, param) => _ = sp.GetRequiredService<MainViewModel>().NavigateToPageAsync(page, param)));

        services.AddTransient<WorkspaceCaptureViewModel>(sp =>
            new WorkspaceCaptureViewModel(
                sp.GetRequiredService<IWorkspaceCaptureService>(),
                sp.GetRequiredService<IConfigurationRepository>(),
                sp.GetRequiredService<IApplicationCatalogService>(),
                sp.GetRequiredService<IMonitorManagementService>(),
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
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string crashLog = Path.Combine(localAppData, "Again", "startup.log");
        try { File.AppendAllText(crashLog, $"[OnExit] Application exiting with exit code {e.ApplicationExitCode}\n"); } catch { }
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
