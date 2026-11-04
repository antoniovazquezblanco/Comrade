using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace COMrade.Windowing;

/// <summary>Runs work from other threads on the UI thread and waits for it.</summary>
public static class DispatcherQueueExtensions
{
    /// <summary>Runs <paramref name="work"/> on the queue's thread; fails when COMrade is closing.</summary>
    public static Task<T> InvokeAsync<T>(this DispatcherQueue queue, Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = queue.TryEnqueue(async () =>
        {
            try
            {
                done.SetResult(await work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });

        if (!queued)
            done.SetException(new InvalidOperationException("COMrade is closing."));
        return done.Task;
    }

    public static Task InvokeAsync(this DispatcherQueue queue, Func<Task> work) =>
        queue.InvokeAsync(async () =>
        {
            await work();
            return true;
        });

    public static Task<T> InvokeAsync<T>(this DispatcherQueue queue, Func<T> work) =>
        queue.InvokeAsync(() => Task.FromResult(work()));

    public static Task InvokeAsync(this DispatcherQueue queue, Action work) =>
        queue.InvokeAsync(() =>
        {
            work();
            return true;
        });
}
