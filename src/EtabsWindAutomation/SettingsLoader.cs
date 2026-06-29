using System.Text.Json;

namespace EtabsWindAutomation;

public static class SettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static WindSettings LoadFromPluginFolder()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wind-settings.json");
        if (!File.Exists(path))
        {
            return new WindSettings();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WindSettings>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse settings file: {path}");
    }
}

