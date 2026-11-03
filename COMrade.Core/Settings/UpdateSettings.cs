namespace COMrade.Core.Settings;

/// <summary>How COMrade looks for new releases.</summary>
/// <param name="CheckAutomatically">Ask GitHub for the latest release once a day.</param>
/// <param name="LastChecked">When the last check succeeded; null before the first.</param>
/// <param name="DismissedVersion">The release whose notice was closed; it is not shown again.</param>
public sealed record UpdateSettings(
    bool CheckAutomatically = true,
    DateTimeOffset? LastChecked = null,
    string? DismissedVersion = null)
{
    public static UpdateSettings Default { get; } = new();
}
