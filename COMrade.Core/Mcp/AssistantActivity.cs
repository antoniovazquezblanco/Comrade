namespace COMrade.Core.Mcp;

/// <summary>Which assistants use which tabs, and what they did last, for the tabs to show.</summary>
/// <remarks>Thread-safe: each assistant's calls run on their own threads.</remarks>
internal sealed class AssistantActivity(TimeProvider time)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<IAssistantTab, TabActivity> _tabs = [];

    /// <summary>An assistant did something with a tab.</summary>
    public void Report(IAssistantTab tab, object client, string action)
    {
        lock (_lock)
        {
            if (!_tabs.TryGetValue(tab, out var activity))
                _tabs[tab] = activity = new TabActivity();
            activity.Clients.Add(client);
            activity.LastAction = action;
            activity.LastActionAt = time.GetUtcNow();

            // In the lock, so tabs see the updates in order; tabs only queue them.
            tab.ShowAssistantStatus(activity.Status);
        }
    }

    /// <summary>The assistant disconnected: the tabs it used stop counting it.</summary>
    public void Release(object client)
    {
        lock (_lock)
        {
            foreach (var (tab, activity) in _tabs.ToList())
            {
                if (!activity.Clients.Remove(client))
                    continue;
                tab.ShowAssistantStatus(activity.Status);
                if (activity.Clients.Count == 0)
                    _tabs.Remove(tab);
            }
        }
    }

    private sealed class TabActivity
    {
        public HashSet<object> Clients { get; } = [];

        public string? LastAction { get; set; }

        public DateTimeOffset? LastActionAt { get; set; }

        public AssistantStatus Status => new(Clients.Count, LastAction, LastActionAt);
    }
}
