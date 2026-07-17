using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdEditor.Models;
using MdEditor.Services;

namespace MdEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private int _untitledCounter;

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new();

    public AppSettings Settings { get; }

    public SessionService SessionService { get; }

    public SettingsService SettingsService { get; }

    public MarkdownRenderService RenderService { get; }

    public SearchService SearchService { get; }

    [ObservableProperty]
    private DocumentTabViewModel? _activeTab;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private bool _isRawEditorFocused;

    public MainViewModel(SettingsService settingsService, SessionService sessionService, MarkdownRenderService renderService, SearchService searchService)
    {
        SettingsService = settingsService;
        SessionService = sessionService;
        RenderService = renderService;
        SearchService = searchService;

        Settings = SettingsService.Load();
        _isDarkTheme = Settings.Theme == AppTheme.Dark;
    }

    partial void OnActiveTabChanged(DocumentTabViewModel? value)
    {
        SaveSessionManifest();
    }

    public DocumentTabViewModel CreateTab(string displayName, string? filePath, string initialContent, bool isDirty,
        string? existingId = null, string? existingAutosavePath = null)
    {
        var id = existingId ?? Guid.NewGuid().ToString("N");
        var autosavePath = existingAutosavePath ?? SessionService.CreateAutosavePath(id);
        var tab = new DocumentTabViewModel(id, displayName, filePath, autosavePath, initialContent, isDirty, SessionService, RenderService);
        Tabs.Add(tab);
        ActiveTab = tab;
        SaveSessionManifest();
        return tab;
    }

    public DocumentTabViewModel NewUntitledTab()
    {
        _untitledCounter++;
        var name = _untitledCounter == 1 ? "sans titre.md" : $"sans titre {_untitledCounter}.md";
        return CreateTab(name, null, string.Empty, false);
    }

    [RelayCommand]
    private void NewTab() => NewUntitledTab();

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        Settings.Theme = IsDarkTheme ? AppTheme.Dark : AppTheme.Light;
        ThemeService.Apply(Settings.Theme);
        PersistSettings();
    }

    public void RemoveTab(DocumentTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasActive = ActiveTab == tab;
        Tabs.RemoveAt(index);
        tab.Dispose();
        SessionService.DeleteAutosave(tab.AutosavePath);

        if (wasActive)
        {
            if (Tabs.Count > 0)
            {
                ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
                return; // setter triggers SaveSessionManifest via OnActiveTabChanged
            }

            ActiveTab = null;
            SaveSessionManifest();
            return;
        }

        SaveSessionManifest();
    }

    public DocumentTabViewModel? FindTabByPath(string path) =>
        Tabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));

    public void SaveSessionManifest()
    {
        var manifest = new SessionManifest { ActiveTabId = ActiveTab?.Id };
        foreach (var tab in Tabs)
        {
            manifest.Tabs.Add(new SessionTabEntry
            {
                Id = tab.Id,
                DisplayName = tab.DisplayName,
                OriginalFilePath = tab.FilePath,
                AutosavePath = tab.AutosavePath
            });
        }

        SessionService.SaveManifest(manifest);
    }

    public void PersistSettings() => SettingsService.Save(Settings);

    public void AddRecentFile(string path)
    {
        Settings.AddRecentFile(path);
        PersistSettings();
    }
}
