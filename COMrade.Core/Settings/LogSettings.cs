namespace COMrade.Core.Settings;

/// <summary>How sessions are logged to files.</summary>
/// <param name="Folder">Where log files go; null for <see cref="DefaultFolder"/>.</param>
/// <param name="Timestamps">Start every line with the time it began.</param>
/// <param name="KeepColourCodes">Keep the terminal's colour and cursor codes in the file.</param>
/// <param name="StartWhenPortOpens">Start logging whenever a port opens.</param>
/// <param name="AskWhereToSave">
/// Ask for the file each time logging is turned on, starting in <see cref="Folder"/>; logs that
/// start when a port opens still go to the folder, as nobody may be there to answer.
/// </param>
public sealed record LogSettings(
    string? Folder = null,
    bool Timestamps = false,
    bool KeepColourCodes = false,
    bool StartWhenPortOpens = false,
    bool AskWhereToSave = false)
{
    public static LogSettings Default { get; } = new();

    /// <summary>Documents\COMrade\Logs.</summary>
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "COMrade", "Logs");

    [System.Text.Json.Serialization.JsonIgnore]
    public string FolderOrDefault => string.IsNullOrWhiteSpace(Folder) ? DefaultFolder : Folder;
}
