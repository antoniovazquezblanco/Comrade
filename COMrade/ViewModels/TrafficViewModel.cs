using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using COMrade.Core.Sessions;
using COMrade.Core.TrafficView;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMrade.ViewModels;

/// <summary>
/// The analysis view of a device tab: what went over the port both ways, in rows, laid out as it
/// arrives while the view shows.
/// </summary>
public sealed partial class TrafficViewModel : ObservableObject, IDisposable
{
    // Data that arrives sooner after an update joins the next one.
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(16);

    private readonly Traffic _traffic;
    private CancellationTokenSource? _follow;

    public TrafficViewModel(Traffic traffic)
    {
        _traffic = traffic;
        Rows = new TrafficRows(traffic);
    }

    /// <summary>The rows changed: new traffic, or laid out again.</summary>
    public event EventHandler? Updated;

    public Traffic Traffic => _traffic;

    public TrafficRows Rows { get; }

    /// <summary>The view shows: the rows follow the traffic. Must be set on the UI thread.</summary>
    public bool IsActive
    {
        get;
        set
        {
            if (value == field)
                return;
            field = value;
            _follow?.Cancel();
            _follow?.Dispose();
            _follow = null;
            if (value)
            {
                _follow = new CancellationTokenSource();
                _ = FollowAsync(_follow.Token);
            }
        }
    }

    /// <summary>Shows only the traffic from now on, as Clear does in the terminal.</summary>
    public void Clear()
    {
        Rows.Clear();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => IsActive = false;

    // On the UI thread, which the waits come back to.
    private async Task FollowAsync(CancellationToken cancellationToken)
    {
        var since = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var end = _traffic.End;
                if (Rows.Update())
                    Updated?.Invoke(this, EventArgs.Empty);
                since.Restart();

                await _traffic.WaitAsync(end, cancellationToken);
                if (since.Elapsed < MinimumInterval)
                    await Task.Delay(MinimumInterval - since.Elapsed, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The view hid or the tab closed.
        }
    }
}
