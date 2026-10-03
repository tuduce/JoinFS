using System.Text.Json;

namespace RecordingXRay.Services;

/// <summary>What the app remembers between runs.</summary>
public sealed class AppSettings
{
    /// <summary>Most recently opened first.</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Draw OpenStreetMap tiles under the map. Needs the internet; off keeps the map fully offline.</summary>
    public bool ShowBasemap { get; set; } = true;
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>Settings kept in memory only (tests, and the default when nothing is supplied).</summary>
public sealed class MemorySettingsStore : ISettingsStore
{
    private AppSettings settings = new();

    public AppSettings Load() => new() { RecentFiles = [.. settings.RecentFiles], ShowBasemap = settings.ShowBasemap };

    public void Save(AppSettings value) => settings = new AppSettings { RecentFiles = [.. value.RecentFiles], ShowBasemap = value.ShowBasemap };
}

/// <summary>Settings in a JSON file. A missing or damaged file gives the defaults; a file that cannot be written is ignored.</summary>
public sealed class FileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string path;

    public FileSettingsStore(string path)
    {
        this.path = path;
    }

    /// <summary>%APPDATA%\RecordingXRay\settings.json.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RecordingXRay", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
