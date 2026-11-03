using System.Net;
using COMrade.Core.Tests.Fakes;
using COMrade.Core.Updates;

namespace COMrade.Core.Tests.Updates;

public sealed class UpdateCheckerTests
{
    private readonly FakeHttpHandler _handler = new();

    private UpdateChecker Checker(string current = "0.3.0") => new(new HttpClient(_handler), ReleaseVersion.TryParse(current)!);

    [Fact]
    public async Task Finds_a_newer_release()
    {
        _handler.Respond = _ => FakeHttpHandler.Json(FakeHttpHandler.Release("v0.4.0"));

        var update = await Checker().FindUpdateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ReleaseVersion(0, 4, 0), update?.Version);
        Assert.Equal(new Uri("https://github.com/antoniovazquezblanco/Comrade/releases/tag/v0.4.0"), update?.Page);
    }

    [Theory]
    [InlineData("v0.3.0")]
    [InlineData("v0.2.1")]
    public async Task Finds_nothing_when_the_latest_release_is_not_newer(string tag)
    {
        _handler.Respond = _ => FakeHttpHandler.Json(FakeHttpHandler.Release(tag));

        Assert.Null(await Checker().FindUpdateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Finds_nothing_before_the_first_release()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Null(await Checker().FindUpdateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Asks_GitHub_for_the_latest_release_with_only_the_version()
    {
        _handler.Respond = _ => FakeHttpHandler.Json(FakeHttpHandler.Release("v0.3.0"));

        await Checker("0.3.1-alpha.0.3").GetLatestAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UpdateChecker.LatestReleaseUri, request.RequestUri);
        Assert.Equal("COMrade/0.3.1-alpha.0.3", request.Headers.UserAgent.ToString());
        Assert.Null(request.Content);
    }

    [Theory]
    [InlineData("""{ "tag_name": "nightly", "html_url": "https://github.com/antoniovazquezblanco/Comrade/releases/tag/nightly" }""")]
    [InlineData("""{ "tag_name": "v0.4.0", "html_url": "https://example.com/COMrade.exe" }""")]
    [InlineData("""{ "tag_name": "v0.4.0", "html_url": "http://github.com/antoniovazquezblanco/Comrade/releases/tag/v0.4.0" }""")]
    [InlineData("""{ "tag_name": "v0.4.0" }""")]
    [InlineData("""{ "tag_name": 4 }""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    public async Task Rejects_an_answer_it_cannot_trust(string json)
    {
        _handler.Respond = _ => FakeHttpHandler.Json(json);

        await Assert.ThrowsAsync<UpdateCheckException>(() => Checker().GetLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reports_an_error_answer()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "rate limit exceeded" };

        var error = await Assert.ThrowsAsync<UpdateCheckException>(() => Checker().GetLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("GitHub answered 403 rate limit exceeded.", error.Message);
    }

    [Fact]
    public async Task Reports_a_network_failure()
    {
        _handler.Respond = _ => throw new HttpRequestException("No such host is known.");

        var error = await Assert.ThrowsAsync<UpdateCheckException>(() => Checker().GetLatestAsync(TestContext.Current.CancellationToken));

        Assert.Equal("GitHub could not be reached.", error.Message);
    }
}
