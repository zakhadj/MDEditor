using System;
using System.IO;
using System.Text.Json;
using MdEditor.Models;

namespace MdEditor.Services;

public class SettingsService
{
    private readonly string _settingsDir;
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        _settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdEditor");
        _settingsPath = Path.Combine(_settingsDir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file: fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Best-effort persistence; ignore failures (e.g. locked file, no permissions).
        }
    }
}
