using CommunityToolkit.Mvvm.ComponentModel;
using Encore.Core.Domain;

namespace Encore.App.ViewModels;

public class AppPreviewItem
{
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "🖥️";
}

public partial class WorkspaceItemViewModel : ObservableObject
{
    public Workspace Model { get; }

    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public string? Description => Model.Description;
    public string Category => Model.Category;
    public int AppCount => Model.Applications.Count;
    public DateTime CreatedAt => Model.CreatedAt;
    public DateTime? LastRestoredAt => Model.LastRestoredAt;

    [ObservableProperty]
    private bool _isPinned;

    public string LastRestoredText => LastRestoredAt.HasValue
        ? $"Restored {GetRelativeTime(LastRestoredAt.Value)}"
        : "Never restored";

    public string AppCountText => $"{AppCount} {(AppCount == 1 ? "app" : "apps")}";
    public string CompactAppCountText => $"{AppCount} {(AppCount == 1 ? "app" : "apps")}";

    public IReadOnlyList<AppPreviewItem> AppPreviews { get; }
    public int AdditionalAppsCount { get; }
    public bool HasAdditionalApps => AdditionalAppsCount > 0;

    public WorkspaceItemViewModel(Workspace model)
    {
        Model = model;
        _isPinned = model.IsPinned;

        var distinctApps = model.Applications
            .GroupBy(a => a.ProcessName.ToLowerInvariant())
            .Select(g => g.First())
            .Take(4)
            .Select(a => new AppPreviewItem
            {
                ProcessName = a.ProcessName,
                DisplayName = GetAppDisplayName(a.ProcessName, a.WindowTitle),
                IconGlyph = GetAppGlyph(a.ProcessName, a.WindowTitle)
            })
            .ToList();

        AppPreviews = distinctApps;
        AdditionalAppsCount = Math.Max(0, model.Applications.Count - distinctApps.Count);
    }

    public static string GetAppGlyph(string processName, string? windowTitle = null)
    {
        var lower = processName.ToLowerInvariant();
        if (lower.Contains("chrome")) return "🌐";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "🧭";
        if (lower.Contains("firefox")) return "🦊";
        if (lower.Contains("brave")) return "🦁";
        if (lower.Contains("code")) return "💻";
        if (lower.Contains("devenv") || lower.Contains("visualstudio")) return "🔷";
        if (lower.Contains("explorer")) return "📁";
        if (lower.Contains("terminal") || lower.Contains("powershell") || lower.Contains("cmd")) return "⚡";
        if (lower.Contains("slack")) return "💬";
        if (lower.Contains("teams")) return "👥";
        if (lower.Contains("discord")) return "🎮";
        if (lower.Contains("notepad")) return "📝";
        if (lower.Contains("spotify")) return "🎵";
        if (lower.Contains("figma")) return "🎨";
        if (lower.Contains("postman")) return "🚀";
        if (lower.Contains("word") || lower.Contains("winword")) return "📄";
        if (lower.Contains("excel")) return "📊";
        if (lower.Contains("powerpnt")) return "📑";
        if (lower.Contains("rider") || lower.Contains("idea") || lower.Contains("pycharm")) return "⚡";
        return "🖥️";
    }

    private static string GetAppDisplayName(string processName, string? windowTitle)
    {
        var lower = processName.ToLowerInvariant();
        if (lower.Contains("chrome")) return "Google Chrome";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "Microsoft Edge";
        if (lower.Contains("code")) return "Visual Studio Code";
        if (lower.Contains("explorer")) return "File Explorer";
        if (lower.Contains("terminal")) return "Windows Terminal";
        if (lower.Contains("powershell")) return "PowerShell";
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            var shortTitle = windowTitle.Split(new[] { " - ", " — " }, StringSplitOptions.None)[0].Trim();
            if (shortTitle.Length > 0 && shortTitle.Length <= 24) return shortTitle;
        }
        return processName;
    }

    private static string GetRelativeTime(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return dt.ToLocalTime().ToString("MMM dd, yyyy");
    }
}
