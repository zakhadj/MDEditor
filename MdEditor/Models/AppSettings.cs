using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MdEditor.Models;

public enum AppTheme
{
    Light,
    Dark
}

public class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Light;

    public bool SyncScroll { get; set; } = true;

    public bool SyncSelection { get; set; } = true;

    public bool WordWrap { get; set; } = true;

    public List<string> RecentFiles { get; set; } = new();

    public bool HasSeenWelcome { get; set; } = false;

    public const int MaxRecentFiles = 10;

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, System.StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }
    }
}
