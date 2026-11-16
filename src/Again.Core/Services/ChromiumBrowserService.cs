using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Again.Core.Native;

namespace Again.Core.Services;

public class ChromiumProfileInfo
{
    public string DirectoryName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public double ActiveTime { get; set; }
}

public class BrowserTabInfo
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class BrowserWindowDetails
{
    public string BrowserName { get; set; } = string.Empty;
    public string ProfileDirectory { get; set; } = string.Empty;
    public string ProfileDisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public List<BrowserTabInfo> Tabs { get; set; } = new();
    public string LaunchArguments { get; set; } = string.Empty;
}

public interface IChromiumBrowserService
{
    bool IsSupportedBrowser(string executablePath);
    IReadOnlyList<ChromiumProfileInfo> GetProfiles(string executablePath);
    BrowserWindowDetails? InspectWindow(IntPtr hWnd, string executablePath);
}

public class ChromiumBrowserService : IChromiumBrowserService
{
    private readonly ILogger<ChromiumBrowserService>? _logger;

    public ChromiumBrowserService(ILogger<ChromiumBrowserService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsSupportedBrowser(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        string exe = Path.GetFileName(executablePath).ToLowerInvariant();
        return exe is "chrome.exe" or "msedge.exe" or "brave.exe" or "chromium.exe" or "vivaldi.exe";
    }

    private string? GetUserDataDirectory(string executablePath)
    {
        string exe = Path.GetFileName(executablePath).ToLowerInvariant();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return exe switch
        {
            "chrome.exe" => Path.Combine(localAppData, "Google", "Chrome", "User Data"),
            "msedge.exe" => Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            "brave.exe" => Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"),
            "chromium.exe" => Path.Combine(localAppData, "Chromium", "User Data"),
            "vivaldi.exe" => Path.Combine(localAppData, "Vivaldi", "User Data"),
            _ => null
        };
    }

    public IReadOnlyList<ChromiumProfileInfo> GetProfiles(string executablePath)
    {
        var result = new List<ChromiumProfileInfo>();
        var userDataDir = GetUserDataDirectory(executablePath);
        if (string.IsNullOrEmpty(userDataDir) || !Directory.Exists(userDataDir))
            return result;

        string localStatePath = Path.Combine(userDataDir, "Local State");
        if (!File.Exists(localStatePath))
            return result;

        try
        {
            string json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("profile", out var profProp) &&
                profProp.TryGetProperty("info_cache", out var infoCache))
            {
                foreach (var prop in infoCache.EnumerateObject())
                {
                    var p = new ChromiumProfileInfo { DirectoryName = prop.Name };
                    if (prop.Value.TryGetProperty("name", out var n)) p.DisplayName = n.GetString() ?? prop.Name;
                    if (prop.Value.TryGetProperty("user_name", out var u)) p.UserEmail = u.GetString() ?? "";
                    if (prop.Value.TryGetProperty("active_time", out var a)) p.ActiveTime = a.GetDouble();

                    if (string.IsNullOrWhiteSpace(p.DisplayName))
                    {
                        p.DisplayName = prop.Name;
                    }

                    result.Add(p);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse Chromium Local State for {Path}", executablePath);
        }

        return result;
    }

    public BrowserWindowDetails? InspectWindow(IntPtr hWnd, string executablePath)
    {
        if (!IsSupportedBrowser(executablePath) || hWnd == IntPtr.Zero)
            return null;

        var userDataDir = GetUserDataDirectory(executablePath);
        if (string.IsNullOrEmpty(userDataDir) || !Directory.Exists(userDataDir))
            return null;

        var profiles = GetProfiles(executablePath);
        BrowserWindowDetails? details = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var desktop = NativeMethods.EnsureInputDesktop();

                // Wake up accessibility in Chromium window
                Guid iidIAccessible = NativeMethods.IID_IAccessible;
                NativeMethods.AccessibleObjectFromWindow(hWnd, NativeMethods.OBJID_CLIENT, ref iidIAccessible, out object _);
                Thread.Sleep(250);

                var root = AutomationElement.FromHandle(hWnd);
                if (root == null) return;

                string detectedProfileDir = string.Empty;
                string detectedProfileName = string.Empty;
                string detectedUserEmail = string.Empty;

                // Match Profile Button
                var btnCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var buttons = root.FindAll(TreeScope.Descendants, btnCond);
                foreach (AutomationElement b in buttons)
                {
                    string bName = b.Current.Name;
                    foreach (var p in profiles)
                    {
                        if (!string.IsNullOrEmpty(p.DisplayName) && bName.Contains(p.DisplayName, StringComparison.OrdinalIgnoreCase))
                        {
                            detectedProfileDir = p.DirectoryName;
                            detectedProfileName = p.DisplayName;
                            detectedUserEmail = p.UserEmail;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(detectedProfileDir)) break;
                }

                if (string.IsNullOrEmpty(detectedProfileDir))
                {
                    // Fallback to most recently active profile
                    var mostRecent = profiles.OrderByDescending(p => p.ActiveTime).FirstOrDefault();
                    if (mostRecent != null)
                    {
                        detectedProfileDir = mostRecent.DirectoryName;
                        detectedProfileName = mostRecent.DisplayName;
                        detectedUserEmail = mostRecent.UserEmail;
                    }
                    else
                    {
                        detectedProfileDir = "Default";
                        detectedProfileName = "Default";
                    }
                }

                // Active tab URL from Omnibox
                string activeUrl = string.Empty;
                var editCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                var edits = root.FindAll(TreeScope.Descendants, editCond);
                foreach (AutomationElement e in edits)
                {
                    string name = e.Current.Name;
                    if (name.Contains("Address", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Search", StringComparison.OrdinalIgnoreCase))
                    {
                        if (e.TryGetCurrentPattern(ValuePattern.Pattern, out object? vpObj) && vpObj is ValuePattern vp)
                        {
                            string val = vp.Current.Value;
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                activeUrl = val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                                            val.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                    ? val
                                    : "https://" + val;
                                break;
                            }
                        }
                    }
                }

                // Tab Items
                var tabTitles = new List<string>();
                var tabCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                var tabs = root.FindAll(TreeScope.Descendants, tabCond);
                for (int i = 0; i < tabs.Count; i++)
                {
                    string raw = tabs[i].Current.Name;
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    string clean = Regex.Replace(raw, @"\s*-\s*Memory usage\s*-\s*.*$", "", RegexOptions.IgnoreCase);
                    clean = Regex.Replace(clean, @"\s*-\s*Google Chrome.*$", "", RegexOptions.IgnoreCase);
                    clean = Regex.Replace(clean, @"\s*-\s*Microsoft\s*Edge.*$", "", RegexOptions.IgnoreCase);
                    clean = Regex.Replace(clean, @"\s*-\s*Brave.*$", "", RegexOptions.IgnoreCase);
                    clean = clean.Trim();
                    if (!string.IsNullOrEmpty(clean))
                    {
                        tabTitles.Add(clean);
                    }
                }

                // Match titles with History database
                var resolvedTabs = new List<BrowserTabInfo>();
                string historyPath = Path.Combine(userDataDir, detectedProfileDir, "History");
                if (File.Exists(historyPath))
                {
                    string tempDb = Path.Combine(Path.GetTempPath(), $"again_hist_{Guid.NewGuid():N}.db");
                    try
                    {
                        using (var src = new FileStream(historyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var dst = new FileStream(tempDb, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            src.CopyTo(dst);
                        }

                        using (var conn = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly;"))
                        {
                            conn.Open();
                            foreach (var tabTitle in tabTitles)
                            {
                                using var cmd = conn.CreateCommand();
                                cmd.CommandText = "SELECT url FROM urls WHERE title LIKE @t ORDER BY last_visit_time DESC LIMIT 1";
                                cmd.Parameters.AddWithValue("@t", $"%{tabTitle}%");
                                var urlObj = cmd.ExecuteScalar();
                                if (urlObj != null && !string.IsNullOrWhiteSpace(urlObj.ToString()))
                                {
                                    resolvedTabs.Add(new BrowserTabInfo { Title = tabTitle, Url = urlObj.ToString()! });
                                }
                                else if (!string.IsNullOrEmpty(activeUrl) && resolvedTabs.Count == 0)
                                {
                                    resolvedTabs.Add(new BrowserTabInfo { Title = tabTitle, Url = activeUrl });
                                }
                                else
                                {
                                    resolvedTabs.Add(new BrowserTabInfo { Title = tabTitle, Url = $"https://www.google.com/search?q={Uri.EscapeDataString(tabTitle)}" });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to read history from {HistoryPath}", historyPath);
                    }
                    finally
                    {
                        SqliteConnection.ClearAllPools();
                        try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch {}
                    }
                }

                if (resolvedTabs.Count == 0 && !string.IsNullOrEmpty(activeUrl))
                {
                    resolvedTabs.Add(new BrowserTabInfo { Title = root.Current.Name, Url = activeUrl });
                }

                // Format launch arguments
                var argSb = new StringBuilder();
                if (!string.IsNullOrEmpty(detectedProfileDir))
                {
                    argSb.Append($"--profile-directory=\"{detectedProfileDir}\" ");
                }
                argSb.Append("--new-window ");
                foreach (var t in resolvedTabs)
                {
                    argSb.Append($"\"{t.Url}\" ");
                }

                string exeName = Path.GetFileNameWithoutExtension(executablePath);
                string browserName = exeName.ToLowerInvariant() switch
                {
                    "chrome" => "Google Chrome",
                    "msedge" => "Microsoft Edge",
                    "brave" => "Brave Browser",
                    _ => exeName
                };

                details = new BrowserWindowDetails
                {
                    BrowserName = browserName,
                    ProfileDirectory = detectedProfileDir,
                    ProfileDisplayName = detectedProfileName,
                    UserEmail = detectedUserEmail,
                    Tabs = resolvedTabs,
                    LaunchArguments = argSb.ToString().Trim()
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error inspecting Chromium window {Hwnd}", hWnd);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(3500))
        {
            _logger?.LogWarning("Chromium window inspection timed out for {Hwnd}", hWnd);
        }

        return details;
    }
}
