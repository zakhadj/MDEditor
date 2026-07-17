using System.Collections.Generic;

namespace MdEditor.Models;

public class SessionTabEntry
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? OriginalFilePath { get; set; }

    public string AutosavePath { get; set; } = string.Empty;
}

public class SessionManifest
{
    public string? ActiveTabId { get; set; }

    public List<SessionTabEntry> Tabs { get; set; } = new();
}
