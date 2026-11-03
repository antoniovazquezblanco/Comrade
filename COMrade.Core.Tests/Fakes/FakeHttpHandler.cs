using System.Net;

namespace COMrade.Core.Tests.Fakes;

/// <summary>Answers every request with <see cref="Respond"/> and counts the requests.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json) };

    public static string Release(string tag, string page = "https://github.com/antoniovazquezblanco/Comrade/releases/tag/v0.4.0") =>
        $$"""{ "tag_name": "{{tag}}", "html_url": "{{page}}", "draft": false, "prerelease": false }""";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Respond(request));
    }
}
