namespace COMrade.Core.Tests.Fakes;

/// <summary>Records what is reported, in order, on the reporting thread (unlike <see cref="Progress{T}"/>).</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly List<T> _values = [];

    public T[] Values
    {
        get
        {
            lock (_values)
                return [.. _values];
        }
    }

    public void Report(T value)
    {
        lock (_values)
            _values.Add(value);
    }
}
