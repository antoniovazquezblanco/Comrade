using COMrade.Core.Settings;

namespace COMrade.Core.Updates;

/// <summary>What the last check for updates found.</summary>
public enum UpdateState
{
    /// <summary>Not checked since COMrade started.</summary>
    NotChecked,
    Checking,
    UpToDate,
    Available,
    Failed,
}

/// <summary>
/// Checks for updates when asked and, while <see cref="UpdateSettings.CheckAutomatically"/> is on,
/// once a day: shortly after COMrade starts when the last check is a day old, and again each day
/// it keeps running. Created on the UI thread, it does its work and raises
/// <see cref="Changed"/> there.
/// </summary>
public sealed class UpdateService : IDisposable
{
    /// <summary>How often automatic checks run.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromDays(1);

    /// <summary>The wait before the first automatic check, so that it does not slow COMrade's start.</summary>
    public static TimeSpan StartDelay { get; } = TimeSpan.FromSeconds(10);

    // How often the timer looks whether a check is due: the computer may have been asleep.
    private static readonly TimeSpan Tick = TimeSpan.FromHours(1);

    private readonly UpdateChecker _checker;
    private readonly AppSettingsStore _settings;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private ITimer? _timer;
    private Task? _check;

    public UpdateService(UpdateChecker checker, AppSettingsStore settings, TimeProvider? time = null)
    {
        _checker = checker;
        _settings = settings;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The state, the update or the error changed.</summary>
    public event EventHandler? Changed;

    public ReleaseVersion Current => _checker.Current;

    public UpdateState State { get; private set; }

    /// <summary>The newer release found by the last check.</summary>
    public ReleaseInfo? Update { get; private set; }

    /// <summary>Why the last check failed.</summary>
    public string? Error { get; private set; }

    /// <summary>An update was found and its notice was not closed.</summary>
    public bool ShowNotice =>
        Update is { } update && update.Version.ToString() != _settings.Current.UpdatesOrDefault.DismissedVersion;

    /// <summary>Starts the automatic checks.</summary>
    public void Start() => _timer ??= _time.CreateTimer(_ => Post(CheckIfDue), null, StartDelay, Tick);

    /// <summary>Checks now; a check already running is not started twice.</summary>
    public Task CheckAsync() => _check is { IsCompleted: false } running ? running : _check = RunCheckAsync();

    /// <summary>Hides the notice of the update found, also after a restart.</summary>
    public void Dismiss()
    {
        if (Update is not { } update)
            return;
        _settings.Update(settings => settings with
        {
            Updates = settings.UpdatesOrDefault with { DismissedVersion = update.Version.ToString() },
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void CheckIfDue()
    {
        var updates = _settings.Current.UpdatesOrDefault;
        var due = updates.LastChecked is not { } last || _time.GetUtcNow() - last >= Interval;
        if (updates.CheckAutomatically && due)
            _ = CheckAsync();
    }

    private async Task RunCheckAsync()
    {
        SetState(UpdateState.Checking);
        try
        {
            Update = await _checker.FindUpdateAsync();
            Error = null;
            _settings.Update(settings => settings with
            {
                Updates = settings.UpdatesOrDefault with { LastChecked = _time.GetUtcNow() },
            });
            SetState(Update is null ? UpdateState.UpToDate : UpdateState.Available);
        }
        catch (UpdateCheckException ex)
        {
            Error = ex.Message;
            SetState(UpdateState.Failed);
        }
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Post(Action action)
    {
        if (_context is null)
            action();
        else
            _context.Post(_ => action(), null);
    }
}
