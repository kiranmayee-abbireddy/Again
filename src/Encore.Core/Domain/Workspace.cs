namespace Encore.Core.Domain;

public class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "Custom";
    public string? Icon { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRestoredAt { get; set; }
    public bool IsPinned { get; set; }
    
    public List<WorkspaceApplication> Applications { get; set; } = new();

    public Workspace Clone(string? newName = null)
    {
        var cloned = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = newName ?? $"{Name} (Copy)",
            Description = Description,
            Category = Category,
            Icon = Icon,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastRestoredAt = null,
            IsPinned = false,
            Applications = new List<WorkspaceApplication>()
        };

        foreach (var app in Applications)
        {
            cloned.Applications.Add(app.Clone(cloned.Id));
        }

        return cloned;
    }
}
