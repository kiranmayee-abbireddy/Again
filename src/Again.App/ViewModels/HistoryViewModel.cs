using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Again.Core.Domain;
using Again.Core.Services;

namespace Again.App.ViewModels;

public partial class SessionItemViewModel : ObservableObject
{
    public RestorationSession Session { get; }

    public Guid Id => Session.Id;
    public string WorkspaceName => Session.WorkspaceName;
    public DateTime StartedAt => Session.StartedAt;
    public string Status => Session.Status;
    public long? DurationMs => Session.DurationMs;
    public int TotalItems => Session.Items.Count;
    public int SuccessItems => Session.Items.Count(i => i.Status is "LaunchedAndPlaced" or "RepositionedOnly");
    public int FailedItems => Session.Items.Count(i => i.Status == "Failed");
    public bool HasFailures => FailedItems > 0;

    public string TimeText => StartedAt.ToLocalTime().ToString("MMM dd, yyyy HH:mm:ss");
    public string DurationText => DurationMs.HasValue ? $"{DurationMs.Value} ms" : "N/A";
    public string SummaryText => $"{SuccessItems} of {TotalItems} restored";

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<RestorationItem> Items { get; } = new();

    public SessionItemViewModel(RestorationSession session)
    {
        Session = session;
        foreach (var item in session.Items)
        {
            Items.Add(item);
        }
    }
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly IRestorationHistoryService _historyService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRetrying;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSessions;

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = new();

    public HistoryViewModel(IRestorationHistoryService historyService)
    {
        _historyService = historyService;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            var sessions = await _historyService.GetRecentHistoryAsync(50);
            Sessions.Clear();
            foreach (var s in sessions)
            {
                Sessions.Add(new SessionItemViewModel(s));
            }
            HasSessions = Sessions.Count > 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RetryFailedAsync(SessionItemViewModel? item)
    {
        if (item == null || !item.HasFailures) return;

        IsRetrying = true;
        StatusMessage = $"Retrying failed applications for {item.WorkspaceName}...";

        try
        {
            var result = await _historyService.RetryFailedItemsAsync(item.Id);
            if (result != null)
            {
                StatusMessage = $"Retry complete with status: {result.Status}";
                await LoadAsync();
            }
            else
            {
                StatusMessage = "No failed applications could be retried.";
            }
        }
        finally
        {
            IsRetrying = false;
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await _historyService.ClearHistoryAsync();
        Sessions.Clear();
        HasSessions = false;
        StatusMessage = "Restoration history cleared.";
    }
}
