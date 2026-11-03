using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace COMrade.Core.Updates;

/// <summary>
/// Asks GitHub for the latest COMrade release. The request carries nothing but COMrade's version,
/// in the User-Agent header GitHub requires.
/// </summary>
public sealed class UpdateChecker(HttpClient http, ReleaseVersion current)
{
    public static Uri LatestReleaseUri { get; } = new("https://api.github.com/repos/antoniovazquezblanco/Comrade/releases/latest");

    public ReleaseVersion Current { get; } = current;

    /// <summary>The latest release, or null when nothing has been released yet.</summary>
    /// <exception cref="UpdateCheckException">GitHub could not be reached or gave an unexpected answer.</exception>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("COMrade", Current.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new UpdateCheckException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Read(json.RootElement);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateCheckException("GitHub could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateCheckException("GitHub took too long to answer.", ex);
        }
        catch (JsonException ex)
        {
            throw new UpdateCheckException("GitHub's answer could not be read.", ex);
        }
    }

    /// <summary>The latest release when it is newer than this COMrade, otherwise null.</summary>
    /// <exception cref="UpdateCheckException">GitHub could not be reached or gave an unexpected answer.</exception>
    public async Task<ReleaseInfo?> FindUpdateAsync(CancellationToken cancellationToken = default) =>
        await GetLatestAsync(cancellationToken).ConfigureAwait(false) is { } latest && latest.Version > Current ? latest : null;

    private static ReleaseInfo Read(JsonElement release)
    {
        if (release.ValueKind is not JsonValueKind.Object)
            throw new UpdateCheckException("GitHub's answer could not be read.");

        var tag = release.TryGetProperty("tag_name", out var tagName) && tagName.ValueKind is JsonValueKind.String ? tagName.GetString() : null;
        var page = release.TryGetProperty("html_url", out var htmlUrl) && htmlUrl.ValueKind is JsonValueKind.String ? htmlUrl.GetString() : null;

        // Only a page on GitHub: the answer decides what the Download button opens.
        if (ReleaseVersion.TryParse(tag) is not { } version
            || !Uri.TryCreate(page, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != "github.com")
            throw new UpdateCheckException("GitHub's answer could not be read.");

        return new ReleaseInfo(version, uri);
    }
}
