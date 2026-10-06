using System.Text.Json;

namespace BackITUp;

internal sealed class AppSettings
{
    public List<string> SourceFolders { get; set; } = [];
    public string DestinationFolder { get; set; } = string.Empty;
    public int IntervalMinutes { get; set; } = 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BackITUp");

            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var normalized = new AppSettings
        {
            SourceFolders = SourceFolders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DestinationFolder = DestinationFolder.Trim(),
            IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 1440)
        };

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
