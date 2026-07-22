using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubeDrop.Core.Settings;

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    void Update(Func<AppSettings, AppSettings> mutate);
}

/// <summary>JSON-backed settings at %LOCALAPPDATA%\TubeDrop\settings.json. Never stores secrets in the repo.</summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private AppSettings _current;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubeDrop", "settings.json");
        _current = Load();
    }

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        _current = mutate(_current);
        Save();
        Changed?.Invoke(this, _current);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Corrupt settings must never block startup — fall back to defaults.
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(_current, Options));
        }
        catch (Exception)
        {
            // Best-effort persistence.
        }
    }
}
