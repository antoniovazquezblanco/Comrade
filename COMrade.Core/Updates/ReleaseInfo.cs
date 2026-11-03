namespace COMrade.Core.Updates;

/// <summary>A published COMrade release.</summary>
/// <param name="Version">Its version, from the release's tag.</param>
/// <param name="Page">The release's page on GitHub, with its notes and downloads.</param>
public sealed record ReleaseInfo(ReleaseVersion Version, Uri Page);
