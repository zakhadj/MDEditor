using System;
using System.IO;
using System.Text.Json;
using MdEditor.Models;

namespace MdEditor.Services;

public class SessionService
{
    private readonly string _rootDir;
    private readonly string _autosaveDir;
    private readonly string _manifestPath;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public SessionService()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "MdEditor");
        _autosaveDir = Path.Combine(_rootDir, "autosave");
        _manifestPath = Path.Combine(_rootDir, "session.json");
    }

    public SessionManifest? LoadManifest()
    {
        try
        {
            if (File.Exists(_manifestPath))
            {
                var json = File.ReadAllText(_manifestPath);
                return JsonSerializer.Deserialize<SessionManifest>(json);
            }
        }
        catch
        {
            // Corrupt or unreadable manifest: treat as no prior session.
        }

        return null;
    }

    public void SaveManifest(SessionManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(_rootDir);
            var json = JsonSerializer.Serialize(manifest, SerializerOptions);
            File.WriteAllText(_manifestPath, json);
        }
        catch
        {
            // Best-effort persistence; ignore failures.
        }
    }

    public string CreateAutosavePath(string tabId) => Path.Combine(_autosaveDir, tabId + ".md");

    public void WriteAutosaveContent(string autosavePath, string content)
    {
        try
        {
            Directory.CreateDirectory(_autosaveDir);
            File.WriteAllText(autosavePath, content);
        }
        catch
        {
            // Best-effort autosave; ignore transient I/O failures.
        }
    }

    public string ReadAutosaveContent(string autosavePath)
    {
        try
        {
            if (File.Exists(autosavePath))
            {
                return File.ReadAllText(autosavePath);
            }
        }
        catch
        {
            // Fall through to empty content.
        }

        return string.Empty;
    }

    public void DeleteAutosave(string autosavePath)
    {
        try
        {
            if (File.Exists(autosavePath))
            {
                File.Delete(autosavePath);
            }
        }
        catch
        {
            // Best-effort cleanup; ignore failures.
        }
    }
}
