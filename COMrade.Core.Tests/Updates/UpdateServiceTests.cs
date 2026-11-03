using System.Net;
using COMrade.Core.Settings;
using COMrade.Core.Tests.Fakes;
using COMrade.Core.Updates;
using Microsoft.Extensions.Time.Testing;

namespace COMrade.Core.Tests.Updates;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private readonly FakeHttpHandler _handler = new() { Respond = _ => FakeHttpHandler.Json(FakeHttpHandler.Release("v0.4.0")) };
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly AppSettingsStore _settings;
    private readonly UpdateService _service;

    public UpdateServiceTests()
    {
        _settings = new AppSettingsStore(Path.Combine(_folder, "settings.json"));
        _service = new UpdateService(new UpdateChecker(new HttpClient(_handler), new ReleaseVersion(0, 3, 0)), _settings, _time);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task A_check_finds_the_update_and_shows_its_notice()
    {
        await _service.CheckAsync();

        Assert.Equal(UpdateState.Available, _service.State);
        Assert.Equal(new ReleaseVersion(0, 4, 0), _service.Update?.Version);
        Assert.True(_service.ShowNotice);
        Assert.Equal(_time.GetUtcNow(), _settings.Current.UpdatesOrDefault.LastChecked);
    }

    [Fact]
    public async Task A_check_can_find_COMrade_up_to_date()
    {
        _handler.Respond = _ => FakeHttpHandler.Json(FakeHttpHandler.Release("v0.3.0"));

        await _service.CheckAsync();

        Assert.Equal(UpdateState.UpToDate, _service.State);
        Assert.Null(_service.Update);
        Assert.False(_service.ShowNotice);
    }

    [Fact]
    public async Task A_failed_check_says_why_and_is_not_recorded_as_done()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { ReasonPhrase = "Service Unavailable" };

        await _service.CheckAsync();

        Assert.Equal(UpdateState.Failed, _service.State);
        Assert.Equal("GitHub answered 503 Service Unavailable.", _service.Error);
        Assert.Null(_settings.Current.UpdatesOrDefault.LastChecked);
    }

    [Fact]
    public async Task Each_step_of_a_check_is_announced()
    {
        var states = new List<UpdateState>();
        _service.Changed += (_, _) => states.Add(_service.State);

        await _service.CheckAsync();

        Assert.Equal([UpdateState.Checking, UpdateState.Available], states);
    }

    [Fact]
    public async Task A_closed_notice_stays_closed_for_that_version_only()
    {
        await _service.CheckAsync();
        _service.Dismiss();

        Assert.False(_service.ShowNotice);
        Assert.Equal("0.4.0", _settings.Current.UpdatesOrDefault.DismissedVersion);

        _handler.Respond = _ => FakeHttpHandler.Json(FakeHttpHandler.Release("v0.5.0"));
        await _service.CheckAsync();

        Assert.True(_service.ShowNotice);
    }

    [Fact]
    public void Checks_automatically_shortly_after_starting()
    {
        _service.Start();

        _time.Advance(UpdateService.StartDelay - TimeSpan.FromSeconds(1));
        Assert.Empty(_handler.Requests);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(_handler.Requests);
        Assert.Equal(UpdateState.Available, _service.State);
    }

    [Fact]
    public void Checks_again_once_a_day_while_running()
    {
        _service.Start();
        _time.Advance(UpdateService.StartDelay);

        _time.Advance(TimeSpan.FromHours(23));
        Assert.Single(_handler.Requests);

        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public void Does_not_check_again_on_a_start_within_a_day_of_the_last_check()
    {
        _settings.Save(AppSettings.Default with { Updates = new UpdateSettings(LastChecked: _time.GetUtcNow().AddHours(-20)) });

        _service.Start();
        _time.Advance(UpdateService.StartDelay);
        Assert.Empty(_handler.Requests);

        _time.Advance(TimeSpan.FromHours(4));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public void Does_not_check_automatically_when_turned_off()
    {
        _settings.Save(AppSettings.Default with { Updates = new UpdateSettings(CheckAutomatically: false) });

        _service.Start();
        _time.Advance(UpdateService.StartDelay + TimeSpan.FromDays(3));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Checks_when_asked_also_when_automatic_checks_are_off()
    {
        _settings.Save(AppSettings.Default with { Updates = new UpdateSettings(CheckAutomatically: false) });

        await _service.CheckAsync();

        Assert.Single(_handler.Requests);
    }
}
