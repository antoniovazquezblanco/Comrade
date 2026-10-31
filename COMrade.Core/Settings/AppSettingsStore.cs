namespace COMrade.Core.Settings;

/// <summary>Keeps <see cref="AppSettings"/> in a JSON file (see <see cref="JsonFile"/>).</summary>
public sealed class AppSettingsStore
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private AppSettings _current;

    public AppSettingsStore(string path)
    {
        _path = path;
        _current = JsonFile.Read<AppSettings>(path) ?? AppSettings.Default;
    }

    /// <summary>The settings changed. Raised on the thread that saved them.</summary>
    public event EventHandler? Changed;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "COMrade", "settings.json");

    public AppSettings Current
    {
        get
        {
            lock (_lock)
                return _current;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            if (settings == _current)
                return;
            _current = settings;
            JsonFile.Write(_path, settings);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
