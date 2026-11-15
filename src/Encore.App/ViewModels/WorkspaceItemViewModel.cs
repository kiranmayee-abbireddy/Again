using CommunityToolkit.Mvvm.ComponentModel;
using Encore.Core.Domain;

namespace Encore.App.ViewModels;

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

    public WorkspaceItemViewModel(Workspace model)
    {
        Model = model;
        _isPinned = model.IsPinned;
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
