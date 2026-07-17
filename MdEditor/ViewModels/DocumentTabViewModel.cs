using System;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ICSharpCode.AvalonEdit.Document;
using MdEditor.Services;

namespace MdEditor.ViewModels;

/// <summary>
/// Owns one open document: its AvalonEdit text buffer, dirty/autosave state, and cached preview HTML.
/// A single shared TextEditor/WebView2 pair in the main window is repointed at whichever tab is active.
/// </summary>
public partial class DocumentTabViewModel : ObservableObject, IDisposable
{
    private readonly SessionService _sessionService;
    private readonly MarkdownRenderService _renderService;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _previewTimer;

    public string Id { get; }

    public TextDocument Document { get; }

    public string AutosavePath { get; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _renderedBodyHtml = string.Empty;

    /// <summary>Last raw-editor vertical scroll offset, restored when this tab becomes active again.</summary>
    public double SavedRawScrollOffset { get; set; }

    /// <summary>Last preview scroll ratio (0..1), restored after the preview re-navigates for this tab.</summary>
    public double SavedPreviewScrollRatio { get; set; }

    public DocumentTabViewModel(
        string id,
        string displayName,
        string? filePath,
        string autosavePath,
        string initialContent,
        bool isDirty,
        SessionService sessionService,
        MarkdownRenderService renderService)
    {
        Id = id;
        _displayName = displayName;
        _filePath = filePath;
        AutosavePath = autosavePath;
        _sessionService = sessionService;
        _renderService = renderService;

        Document = new TextDocument(initialContent ?? string.Empty);
        Document.TextChanged += OnDocumentTextChanged;

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _autosaveTimer.Tick += (_, _) =>
        {
            _autosaveTimer.Stop();
            _sessionService.WriteAutosaveContent(AutosavePath, Document.Text);
        };

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RenderedBodyHtml = _renderService.RenderBody(Document.Text);
        };

        // Initial autosave copy + initial preview so a freshly created tab is immediately consistent.
        _sessionService.WriteAutosaveContent(AutosavePath, Document.Text);
        RenderedBodyHtml = _renderService.RenderBody(Document.Text);

        _isDirty = isDirty;
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        IsDirty = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    public void MarkSaved()
    {
        IsDirty = false;
    }

    /// <summary>Writes the autosave copy immediately, bypassing the debounce timer (used on app shutdown).</summary>
    public void FlushAutosave()
    {
        _sessionService.WriteAutosaveContent(AutosavePath, Document.Text);
    }

    public void Dispose()
    {
        _autosaveTimer.Stop();
        _previewTimer.Stop();
        Document.TextChanged -= OnDocumentTextChanged;
    }
}
