using System.Text.Json;
using System.Text.Json.Serialization;

namespace COMrade.Core.Settings;

/// <summary>
/// Reads and writes the JSON files COMrade keeps its settings in. Writes go through a temporary
/// file, so a crash never leaves a file half written; a file that cannot be read is set aside.
/// </summary>
internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The file's contents, or null when it does not exist or cannot be read.</summary>
    public static T? Read<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            File.Move(path, path + ".unreadable", overwrite: true);
            return null;
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
