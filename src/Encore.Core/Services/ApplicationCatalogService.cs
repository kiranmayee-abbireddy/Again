using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Encore.Core.Services;

public class CatalogApplicationInfo
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string IconGlyph { get; set; } = "🖥️";
    public string Source { get; set; } = "Installed";
}

public interface IApplicationCatalogService
{
    Task<IReadOnlyList<CatalogApplicationInfo>> GetInstalledApplicationsAsync();
    CatalogApplicationInfo? ResolveApplicationFromPath(string path);
    string? ResolveExecutablePath(string executableOrCommand);
}

public class ApplicationCatalogService : IApplicationCatalogService
{
    private readonly ILogger<ApplicationCatalogService>? _logger;
    private List<CatalogApplicationInfo>? _cachedCatalog;
    private readonly object _lock = new();

    public ApplicationCatalogService(ILogger<ApplicationCatalogService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<CatalogApplicationInfo>> GetInstalledApplicationsAsync()
    {
        lock (_lock)
        {
            if (_cachedCatalog != null && _cachedCatalog.Count > 0)
            {
                return _cachedCatalog;
            }
        }

        return await Task.Run(() =>
        {
            var appsByPath = new Dictionary<string, CatalogApplicationInfo>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan Common Windows System Tools
            AddSystemTools(appsByPath);

            // 2. Scan Well-Known Applications
            AddWellKnownApps(appsByPath);

            // 3. Scan Registry App Paths
            AddRegistryAppPaths(appsByPath, Registry.LocalMachine);
            AddRegistryAppPaths(appsByPath, Registry.CurrentUser);

            // 4. Scan Start Menu Shortcuts
            AddStartMenuShortcuts(appsByPath);

            var sorted = appsByPath.Values
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            lock (_lock)
            {
                _cachedCatalog = sorted;
            }

            _logger?.LogInformation("Discovered {Count} installed and cataloged applications.", sorted.Count);
            return (IReadOnlyList<CatalogApplicationInfo>)sorted;
        });
    }

    public CatalogApplicationInfo? ResolveApplicationFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string cleanPath = path.Trim('\"', ' ');
        string? resolved = ResolveExecutablePath(cleanPath);
        if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
        {
            return null;
        }

        string procName = Path.GetFileNameWithoutExtension(resolved);
        string name = FormatFriendlyAppName(procName);
        string glyph = GetAppGlyph(procName);

        string? arguments = null;
        if (cleanPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (_, shortcutArgs) = ResolveShortcut(cleanPath);
            arguments = shortcutArgs;
        }

        return new CatalogApplicationInfo
        {
            Name = name,
            ExecutablePath = resolved,
            ProcessName = procName,
            Arguments = arguments,
            IconGlyph = glyph,
            Source = "Custom"
        };
    }

    public string? ResolveExecutablePath(string executableOrCommand)
    {
        if (string.IsNullOrWhiteSpace(executableOrCommand)) return null;

        string clean = executableOrCommand.Trim('\"', ' ');

        // 1. Direct file check
        if (File.Exists(clean))
        {
            if (clean.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var (target, _) = ResolveShortcut(clean);
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    return target;
                }
            }
            return Path.GetFullPath(clean);
        }

        // Add .exe extension if missing
        string exeName = clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? clean : clean + ".exe";

        // 2. System32 directory
        string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string sysPath = Path.Combine(system32, exeName);
        if (File.Exists(sysPath)) return sysPath;

        // 3. Windows root directory
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string winPath = Path.Combine(winDir, exeName);
        if (File.Exists(winPath)) return winPath;

        // 4. Registry App Paths
        string? regPath = QueryRegistryAppPath(Registry.LocalMachine, exeName) ?? QueryRegistryAppPath(Registry.CurrentUser, exeName);
        if (!string.IsNullOrEmpty(regPath) && File.Exists(regPath)) return regPath;

        // 5. PATH environment variable
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var segment in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(segment, exeName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    private void AddSystemTools(Dictionary<string, CatalogApplicationInfo> apps)
    {
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var tools = new (string Name, string ExePath, string Proc, string Glyph)[]
        {
            ("File Explorer", Path.Combine(win, "explorer.exe"), "explorer", "📁"),
            ("Notepad", Path.Combine(sys32, "notepad.exe"), "notepad", "📝"),
            ("Calculator", Path.Combine(sys32, "calc.exe"), "calc", "🔢"),
            ("Windows Terminal", Path.Combine(localApp, @"Microsoft\WindowsApps\wt.exe"), "wt", "⚡"),
            ("Command Prompt", Path.Combine(sys32, "cmd.exe"), "cmd", "⚡"),
            ("PowerShell", Path.Combine(sys32, @"WindowsPowerShell\v1.0\powershell.exe"), "powershell", "⚡"),
            ("Paint", Path.Combine(sys32, "mspaint.exe"), "mspaint", "🎨"),
            ("Task Manager", Path.Combine(sys32, "taskmgr.exe"), "taskmgr", "📊"),
            ("Snipping Tool", Path.Combine(sys32, "SnippingTool.exe"), "SnippingTool", "✂️")
        };

        foreach (var (name, path, proc, glyph) in tools)
        {
            if (File.Exists(path) && !apps.ContainsKey(path))
            {
                apps[path] = new CatalogApplicationInfo
                {
                    Name = name,
                    ExecutablePath = path,
                    ProcessName = proc,
                    IconGlyph = glyph,
                    Source = "System"
                };
            }
        }
    }

    private void AddWellKnownApps(Dictionary<string, CatalogApplicationInfo> apps)
    {
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var known = new (string Name, string[] Paths, string Proc, string Glyph)[]
        {
            ("Google Chrome", new[] {
                Path.Combine(progFiles, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(progFilesX86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(localApp, @"Google\Chrome\Application\chrome.exe")
            }, "chrome", "🌐"),

            ("Microsoft Edge", new[] {
                Path.Combine(progFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(progFiles, @"Microsoft\Edge\Application\msedge.exe")
            }, "msedge", "🧭"),

            ("Mozilla Firefox", new[] {
                Path.Combine(progFiles, @"Mozilla Firefox\firefox.exe"),
                Path.Combine(progFilesX86, @"Mozilla Firefox\firefox.exe")
            }, "firefox", "🦊"),

            ("Brave Browser", new[] {
                Path.Combine(progFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Path.Combine(localApp, @"BraveSoftware\Brave-Browser\Application\brave.exe")
            }, "brave", "🦁"),

            ("Visual Studio Code", new[] {
                Path.Combine(localApp, @"Programs\Microsoft VS Code\Code.exe"),
                Path.Combine(progFiles, @"Microsoft VS Code\Code.exe")
            }, "Code", "💻"),

            ("Cursor", new[] {
                Path.Combine(localApp, @"Programs\cursor\Cursor.exe"),
                Path.Combine(localApp, @"cursor\Cursor.exe")
            }, "Cursor", "💻"),

            ("Discord", new[] {
                Path.Combine(localApp, @"Discord\Update.exe"),
                Path.Combine(localApp, @"Discord\app-*\Discord.exe")
            }, "Discord", "🎮"),

            ("Spotify", new[] {
                Path.Combine(appData, @"Spotify\Spotify.exe")
            }, "Spotify", "🎵"),

            ("Slack", new[] {
                Path.Combine(localApp, @"slack\slack.exe")
            }, "slack", "💬"),

            ("WhatsApp", new[] {
                Path.Combine(localApp, @"WhatsApp\WhatsApp.exe")
            }, "WhatsApp", "💬"),

            ("Git Bash", new[] {
                Path.Combine(progFiles, @"Git\git-bash.exe"),
                Path.Combine(progFilesX86, @"Git\git-bash.exe")
            }, "git-bash", "⚡")
        };

        foreach (var item in known)
        {
            foreach (var path in item.Paths)
            {
                if (path.Contains('*'))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(path) ?? "";
                        string pattern = Path.GetFileName(dir);
                        string parent = Path.GetDirectoryName(dir) ?? "";
                        if (Directory.Exists(parent))
                        {
                            var matchingDirs = Directory.GetDirectories(parent, pattern);
                            foreach (var md in matchingDirs)
                            {
                                string exe = Path.Combine(md, Path.GetFileName(path));
                                if (File.Exists(exe) && !apps.ContainsKey(exe))
                                {
                                    apps[exe] = new CatalogApplicationInfo
                                    {
                                        Name = item.Name,
                                        ExecutablePath = exe,
                                        ProcessName = item.Proc,
                                        IconGlyph = item.Glyph,
                                        Source = "Installed"
                                    };
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
                else if (File.Exists(path) && !apps.ContainsKey(path))
                {
                    apps[path] = new CatalogApplicationInfo
                    {
                        Name = item.Name,
                        ExecutablePath = path,
                        ProcessName = item.Proc,
                        IconGlyph = item.Glyph,
                        Source = "Installed"
                    };
                    break;
                }
            }
        }
    }

    private void AddRegistryAppPaths(Dictionary<string, CatalogApplicationInfo> apps, RegistryKey root)
    {
        try
        {
            using var appPathsKey = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (appPathsKey == null) return;

            foreach (var subKeyName in appPathsKey.GetSubKeyNames())
            {
                try
                {
                    using var subKey = appPathsKey.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var raw = subKey.GetValue("") as string;
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    string clean = raw.Trim('\"', ' ');
                    if (!File.Exists(clean) || !clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (apps.ContainsKey(clean)) continue;

                    string procName = Path.GetFileNameWithoutExtension(clean);
                    string name = FormatFriendlyAppName(procName);
                    string glyph = GetAppGlyph(procName);

                    apps[clean] = new CatalogApplicationInfo
                    {
                        Name = name,
                        ExecutablePath = clean,
                        ProcessName = procName,
                        IconGlyph = glyph,
                        Source = "Registry"
                    };
                }
                catch { }
            }
        }
        catch { }
    }

    private void AddStartMenuShortcuts(Dictionary<string, CatalogApplicationInfo> apps)
    {
        var dirs = new List<string>();
        string commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        if (Directory.Exists(commonPrograms)) dirs.Add(commonPrograms);

        string userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (Directory.Exists(userPrograms)) dirs.Add(userPrograms);

        foreach (var dir in dirs)
        {
            try
            {
                var lnkFiles = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnk in lnkFiles)
                {
                    string filename = Path.GetFileNameWithoutExtension(lnk);
                    string lower = filename.ToLowerInvariant();

                    if (lower.Contains("uninstall") || lower.Contains("help") || lower.Contains("website") || 
                        lower.Contains("readme") || lower.Contains("remove") || lower.Contains("documentation"))
                    {
                        continue;
                    }

                    var (target, args) = ResolveShortcut(lnk);
                    if (string.IsNullOrWhiteSpace(target) || !File.Exists(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (apps.ContainsKey(target)) continue;

                    string procName = Path.GetFileNameWithoutExtension(target);
                    string glyph = GetAppGlyph(procName, filename);

                    apps[target] = new CatalogApplicationInfo
                    {
                        Name = filename,
                        ExecutablePath = target,
                        ProcessName = procName,
                        Arguments = args,
                        IconGlyph = glyph,
                        Source = "StartMenu"
                    };
                }
            }
            catch { }
        }
    }

    private static string? QueryRegistryAppPath(RegistryKey root, string exeName)
    {
        try
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            var val = key?.GetValue("") as string;
            if (!string.IsNullOrWhiteSpace(val))
            {
                string clean = val.Trim('\"', ' ');
                if (File.Exists(clean)) return clean;
            }
        }
        catch { }
        return null;
    }

    private static (string TargetPath, string? Arguments) ResolveShortcut(string lnkPath)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic? shortcut = shell.CreateShortcut(lnkPath);
                    string? target = shortcut?.TargetPath;
                    string? args = shortcut?.Arguments;
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        return (target, string.IsNullOrWhiteSpace(args) ? null : args);
                    }
                }
            }
        }
        catch { }

        return (lnkPath, null);
    }

    public static string GetAppGlyph(string processName, string? friendlyName = null)
    {
        var lower = (processName + " " + (friendlyName ?? "")).ToLowerInvariant();
        if (lower.Contains("chrome")) return "🌐";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "🧭";
        if (lower.Contains("firefox")) return "🦊";
        if (lower.Contains("brave")) return "🦁";
        if (lower.Contains("cursor")) return "💻";
        if (lower.Contains("code") || lower.Contains("visual studio code")) return "💻";
        if (lower.Contains("devenv") || lower.Contains("visual studio")) return "🔷";
        if (lower.Contains("explorer")) return "📁";
        if (lower.Contains("terminal") || lower.Contains("powershell") || lower.Contains("cmd")) return "⚡";
        if (lower.Contains("slack")) return "💬";
        if (lower.Contains("teams")) return "👥";
        if (lower.Contains("discord")) return "🎮";
        if (lower.Contains("notepad")) return "📝";
        if (lower.Contains("spotify")) return "🎵";
        if (lower.Contains("calc")) return "🔢";
        if (lower.Contains("paint") || lower.Contains("mspaint")) return "🎨";
        if (lower.Contains("taskmgr")) return "📊";
        if (lower.Contains("snip")) return "✂️";
        if (lower.Contains("figma")) return "🎨";
        if (lower.Contains("postman")) return "🚀";
        if (lower.Contains("word") || lower.Contains("winword")) return "📄";
        if (lower.Contains("excel")) return "📊";
        if (lower.Contains("powerpnt")) return "📑";
        if (lower.Contains("whatsapp")) return "💬";
        if (lower.Contains("rider") || lower.Contains("idea") || lower.Contains("pycharm")) return "⚡";
        return "🖥️";
    }

    private static string FormatFriendlyAppName(string processName)
    {
        var lower = processName.ToLowerInvariant();
        if (lower == "chrome") return "Google Chrome";
        if (lower == "msedge") return "Microsoft Edge";
        if (lower == "firefox") return "Mozilla Firefox";
        if (lower == "brave") return "Brave Browser";
        if (lower == "code") return "Visual Studio Code";
        if (lower == "cursor") return "Cursor";
        if (lower == "explorer") return "File Explorer";
        if (lower == "wt") return "Windows Terminal";
        if (lower == "cmd") return "Command Prompt";
        if (lower == "powershell") return "PowerShell";
        if (lower == "notepad") return "Notepad";
        if (lower == "calc") return "Calculator";
        if (lower == "mspaint") return "Paint";
        if (lower == "taskmgr") return "Task Manager";
        if (lower == "devenv") return "Visual Studio";
        if (lower == "discord") return "Discord";
        if (lower == "spotify") return "Spotify";
        if (lower == "slack") return "Slack";
        if (lower == "whatsapp") return "WhatsApp";
        return processName;
    }
}
