using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using MdEditor.Services;
using MdEditor.ViewModels;
using MdEditor.Views;

namespace MdEditor;

public partial class MainWindow : Window
{
    private static readonly string[] AcceptedDropExtensions = { ".md", ".markdown", ".txt" };

    // Virtual hostname used to serve local images relative to the active document's folder.
    // Using .invalid TLD guarantees no collision with any real domain.
    private const string VirtualHostName = "mdeditor-local.invalid";

    public MainViewModel Vm { get; }

    private DocumentTabViewModel? _subscribedTab;
    private bool _previewReady;
    private bool _suppressEditorScrollSync;
    private string? _currentVirtualHostFolder;
    private DispatcherTimer? _errorBannerTimer;
    private FindReplaceWindow? _findReplaceWindow;
    private AboutWindow? _aboutWindow;

    public MainWindow(MainViewModel vm)
    {
        Vm = vm;
        DataContext = vm;
        InitializeComponent();

        Loaded += MainWindow_Loaded;
        Vm.PropertyChanged += Vm_PropertyChanged;
        RawEditor.TextArea.SelectionChanged += RawEditor_SelectionChanged;
        RawEditor.TextArea.TextView.ScrollOffsetChanged += RawEditor_ScrollOffsetChanged;

        SubscribeToActiveTab(Vm.ActiveTab);
        UpdateThemeToggleVisual();
        RawEditor.WordWrap = Vm.Settings.WordWrap;
    }

    // ===================== Window corner rounding (Win11 DWM) =====================
    // WindowStyle="None" opts the window out of DWM's automatic corner rounding, so it must be
    // requested explicitly; without this the custom-chrome window would have square corners.

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var preference = DwmwcpRound;
        try
        {
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Pre-Win11 systems: no DWM rounded-corner API, fall back to square corners.
        }

        HwndSource.FromHwnd(hwnd)?.AddHook(WindowProc);
    }

    // ===================== Maximize respects the taskbar (WM_GETMINMAXINFO) =====================
    // A WindowStyle="None" window maximizes to the FULL monitor bounds by default, sliding content
    // under the taskbar. Handling WM_GETMINMAXINFO and clamping to the monitor's work area fixes it.

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point2D { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect2D { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point2D Reserved;
        public Point2D MaxSize;
        public Point2D MaxPosition;
        public Point2D MinTrackSize;
        public Point2D MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect2D Monitor;
        public Rect2D WorkArea;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo monitorInfo);

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ClampMaxSizeToWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ClampMaxSizeToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.Monitor;

        mmi.MaxPosition.X = workArea.Left - monitorArea.Left;
        mmi.MaxPosition.Y = workArea.Top - monitorArea.Top;
        mmi.MaxSize.X = workArea.Right - workArea.Left;
        mmi.MaxSize.Y = workArea.Bottom - workArea.Top;
        mmi.MaxTrackSize.X = mmi.MaxSize.X;
        mmi.MaxTrackSize.Y = mmi.MaxSize.Y;

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    // ===================== Startup / WebView2 setup =====================

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(Path.GetTempPath(), "MdEditor", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await PreviewView.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"WebView2 n'a pas pu être initialisé :\n{ex.Message}", "MD Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        PreviewView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        PreviewView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
        PreviewView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        PreviewView.NavigationCompleted += PreviewView_NavigationCompleted;

        _previewReady = true;
        NavigatePreviewForActiveTab();
        UpdateMaximizeGlyph();
    }

    // ===================== Active tab <-> shared editor/preview =====================

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTab))
        {
            SubscribeToActiveTab(Vm.ActiveTab);
            RawEditor.ScrollToVerticalOffset(Vm.ActiveTab?.SavedRawScrollOffset ?? 0);
            NavigatePreviewForActiveTab();
        }
    }

    private void SubscribeToActiveTab(DocumentTabViewModel? tab)
    {
        if (_subscribedTab != null)
        {
            _subscribedTab.PropertyChanged -= ActiveTab_PropertyChanged;
        }

        _subscribedTab = tab;

        if (_subscribedTab != null)
        {
            _subscribedTab.PropertyChanged += ActiveTab_PropertyChanged;
        }
    }

    private void ActiveTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTabViewModel.RenderedBodyHtml))
        {
            PushIncrementalPreviewUpdate();
        }
        else if (e.PropertyName == nameof(DocumentTabViewModel.FilePath))
        {
            // FilePath changed (e.g. after SaveAs) — the virtual host mapping and base tag must be refreshed.
            NavigatePreviewForActiveTab();
        }
    }

    private void UpdateVirtualHostMapping(string? documentDir)
    {
        if (documentDir == _currentVirtualHostFolder)
        {
            return;
        }

        var webView = PreviewView.CoreWebView2;
        if (webView == null)
        {
            return;
        }

        if (_currentVirtualHostFolder != null)
        {
            try { webView.ClearVirtualHostNameToFolderMapping(VirtualHostName); } catch { /* best-effort */ }
        }

        _currentVirtualHostFolder = documentDir;

        if (documentDir != null && Directory.Exists(documentDir))
        {
            webView.SetVirtualHostNameToFolderMapping(VirtualHostName, documentDir, CoreWebView2HostResourceAccessKind.Allow);
        }
    }

    private void NavigatePreviewForActiveTab()
    {
        if (!_previewReady) return;

        if (Vm.ActiveTab == null)
        {
            UpdateVirtualHostMapping(null);
            PreviewView.NavigateToString(Vm.RenderService.BuildShellHtml(string.Empty, Vm.IsDarkTheme));
            return;
        }

        var documentDir = Vm.ActiveTab.FilePath != null ? Path.GetDirectoryName(Vm.ActiveTab.FilePath) : null;
        UpdateVirtualHostMapping(documentDir);

        var baseHref = documentDir != null ? $"https://{VirtualHostName}/" : null;
        var html = Vm.RenderService.BuildShellHtml(Vm.ActiveTab.RenderedBodyHtml, Vm.IsDarkTheme, baseHref);
        PreviewView.NavigateToString(html);
    }

    private void PushIncrementalPreviewUpdate()
    {
        if (!_previewReady || Vm.ActiveTab == null || PreviewView.CoreWebView2 == null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(Vm.ActiveTab.RenderedBodyHtml);
        _ = PreviewView.CoreWebView2.ExecuteScriptAsync($"window.__md && window.__md.setContent({json})");
    }

    private void PreviewView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var tab = Vm.ActiveTab;
        if (tab == null || PreviewView.CoreWebView2 == null)
        {
            return;
        }

        var ratio = tab.SavedPreviewScrollRatio.ToString(CultureInfo.InvariantCulture);
        _ = PreviewView.CoreWebView2.ExecuteScriptAsync($"window.__md && window.__md.scrollToRatio({ratio})");
    }

    // ===================== Window chrome =====================

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e) => UpdateMaximizeGlyph();

    private void UpdateMaximizeGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        Vm.ToggleThemeCommand.Execute(null);
        UpdateThemeToggleVisual();
        PushThemeToPreview();
    }

    private void UpdateThemeToggleVisual()
    {
        if (Vm.IsDarkTheme)
        {
            SunIcon.Visibility = Visibility.Collapsed;
            MoonIcon.Visibility = Visibility.Visible;
        }
        else
        {
            SunIcon.Visibility = Visibility.Visible;
            MoonIcon.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_Activated(object? sender, EventArgs e) => SetChromeActiveState(true);

    private void Window_Deactivated(object? sender, EventArgs e) => SetChromeActiveState(false);

    private void SetChromeActiveState(bool active)
    {
        var opacity = active ? 1.0 : 0.5;
        TitleText.Opacity = opacity;
        TopMenu.Opacity = opacity;
    }

    private void PushThemeToPreview()
    {
        if (!_previewReady || PreviewView.CoreWebView2 == null)
        {
            return;
        }

        var vars = MarkdownRenderService.GetThemeVars(Vm.IsDarkTheme);
        var json = JsonSerializer.Serialize(vars);
        _ = PreviewView.CoreWebView2.ExecuteScriptAsync($"window.__md && window.__md.applyTheme({json})");
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (!ctrl)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N:
                Vm.NewTabCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.O:
                OpenMenuItem_Click(this, e);
                e.Handled = true;
                break;
            case Key.S:
                if (shift)
                {
                    SaveAsMenuItem_Click(this, e);
                }
                else
                {
                    SaveMenuItem_Click(this, e);
                }

                e.Handled = true;
                break;
            case Key.W:
                CloseTabMenuItem_Click(this, e);
                e.Handled = true;
                break;
            case Key.F:
                FindMenuItem_Click(this, e);
                e.Handled = true;
                break;
            case Key.H:
                ReplaceMenuItem_Click(this, e);
                e.Handled = true;
                break;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        foreach (var tab in Vm.Tabs)
        {
            tab.FlushAutosave();
        }

        Vm.SaveSessionManifest();
    }

    // ===================== File menu =====================

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Markdown et texte (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|Tous les fichiers (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            foreach (var path in dialog.FileNames)
            {
                OpenFilePath(path);
            }
        }
    }

    private void OpenFilePath(string path)
    {
        var existing = Vm.FindTabByPath(path);
        if (existing != null)
        {
            Vm.ActiveTab = existing;
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir le fichier :\n{ex.Message}", "MD Editor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var displayName = Path.GetFileName(path);
        Vm.CreateTab(displayName, path, content, false);
        Vm.AddRecentFile(path);
    }

    private void SaveMenuItem_Click(object sender, RoutedEventArgs e) => SaveTab(Vm.ActiveTab);

    private void SaveTab(DocumentTabViewModel? tab)
    {
        if (tab == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(tab.FilePath))
        {
            SaveTabAs(tab);
            return;
        }

        try
        {
            File.WriteAllText(tab.FilePath, tab.Document.Text);
            tab.MarkSaved();
            Vm.AddRecentFile(tab.FilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'enregistrer le fichier :\n{ex.Message}", "MD Editor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e) => SaveTabAs(Vm.ActiveTab);

    private void SaveTabAs(DocumentTabViewModel? tab)
    {
        if (tab == null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Fichier Markdown (*.md)|*.md|Tous les fichiers (*.*)|*.*",
            FileName = tab.DisplayName
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, tab.Document.Text);
            tab.FilePath = dialog.FileName;
            tab.DisplayName = Path.GetFileName(dialog.FileName);
            tab.MarkSaved();
            Vm.AddRecentFile(dialog.FileName);
            Vm.SaveSessionManifest();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'enregistrer le fichier :\n{ex.Message}", "MD Editor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => Vm.NewUntitledTab();

    private void CloseTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.ActiveTab != null)
        {
            RequestCloseTab(Vm.ActiveTab);
        }
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DocumentTabViewModel tab })
        {
            RequestCloseTab(tab);
        }
    }

    /// <returns>False if the user cancelled the close (used to abort a "close all" loop).</returns>
    private bool RequestCloseTab(DocumentTabViewModel tab)
    {
        if (tab.IsDirty)
        {
            var result = MessageBox.Show(this, $"Enregistrer les modifications de « {tab.DisplayName} » ?", "MD Editor",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (result == MessageBoxResult.Yes)
            {
                SaveTab(tab);
                if (tab.IsDirty)
                {
                    return false; // Save was cancelled or failed (e.g. SaveAs dialog dismissed).
                }
            }
        }

        Vm.RemoveTab(tab);
        return true;
    }

    private void CloseAllTabsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tab in Vm.Tabs.ToList())
        {
            if (!RequestCloseTab(tab))
            {
                break;
            }
        }
    }

    private void PrintMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PreviewView.CoreWebView2?.ShowPrintUI();
    }

    private void FileMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var items = FileMenuItem.Items;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is MenuItem { Tag: "RecentFile" })
            {
                items.RemoveAt(i);
            }
        }

        var insertAt = items.IndexOf(RecentFilesHeader) + 1;
        var recents = Vm.Settings.RecentFiles;

        if (recents.Count == 0)
        {
            items.Insert(insertAt, new MenuItem { Header = "(aucun)", IsEnabled = false, FontSize = 12, Tag = "RecentFile" });
            return;
        }

        for (var i = 0; i < recents.Count; i++)
        {
            var path = recents[i];
            var item = new MenuItem
            {
                Header = $"{i + 1}. {Path.GetFileName(path)}",
                ToolTip = path,
                Tag = "RecentFile"
            };
            item.Click += (_, _) => OpenFilePath(path);
            items.Insert(insertAt + i, item);
        }
    }

    // ===================== Edit menu / raw pane context menu =====================

    private void UndoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (RawEditor.CanUndo)
        {
            RawEditor.Undo();
        }
    }

    private void RedoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (RawEditor.CanRedo)
        {
            RawEditor.Redo();
        }
    }

    private void CutMenuItem_Click(object sender, RoutedEventArgs e) => RawEditor.Cut();
    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) => RawEditor.Copy();
    private void PasteMenuItem_Click(object sender, RoutedEventArgs e) => RawEditor.Paste();
    private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e) => RawEditor.SelectAll();

    private void RawContextCut_Click(object sender, RoutedEventArgs e) => RawEditor.Cut();
    private void RawContextCopy_Click(object sender, RoutedEventArgs e) => RawEditor.Copy();
    private void RawContextPaste_Click(object sender, RoutedEventArgs e) => RawEditor.Paste();
    private void RawContextSelectAll_Click(object sender, RoutedEventArgs e) => RawEditor.SelectAll();

    private void RawEditor_GotFocus(object sender, RoutedEventArgs e) => Vm.IsRawEditorFocused = true;
    private void RawEditor_LostFocus(object sender, RoutedEventArgs e) => Vm.IsRawEditorFocused = false;

    // ===================== Rendered pane context menu (WebView2) =====================

    private void CoreWebView2_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        e.Handled = true;
        double x = e.Location.X;
        double y = e.Location.Y;
        Dispatcher.BeginInvoke(() => ShowPreviewContextMenu(x, y));
    }

    private void ShowPreviewContextMenu(double x, double y)
    {
        var menu = new ContextMenu();

        var copyItem = new MenuItem { Header = "Copier" };
        copyItem.Click += async (_, _) => await CopyFromPreviewAsync();

        var pasteItem = new MenuItem { Header = "Coller" };
        pasteItem.Click += (_, _) => PasteIntoActiveDocument();

        var selectAllItem = new MenuItem { Header = "Tout sélectionner" };
        selectAllItem.Click += (_, _) =>
            _ = PreviewView.CoreWebView2?.ExecuteScriptAsync("window.__md && window.__md.selectAllContent()");

        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(selectAllItem);

        menu.PlacementTarget = PreviewView;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = x;
        menu.VerticalOffset = y;
        menu.IsOpen = true;
    }

    private async System.Threading.Tasks.Task CopyFromPreviewAsync()
    {
        if (PreviewView.CoreWebView2 == null)
        {
            return;
        }

        var resultJson = await PreviewView.CoreWebView2.ExecuteScriptAsync("window.__md ? window.__md.getSelectionText() : ''");
        var text = JsonSerializer.Deserialize<string>(resultJson) ?? string.Empty;
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    private void PasteIntoActiveDocument()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        var tab = Vm.ActiveTab;
        if (tab == null)
        {
            return;
        }

        var text = Clipboard.GetText();
        var current = tab.Document.Text;
        var separator = current.Length > 0 && !current.EndsWith("\n") ? "\n" : string.Empty;
        tab.Document.Text = current + separator + text;
    }

    // ===================== Drag and drop =====================

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var hadInvalid = false;

        foreach (var path in files)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (AcceptedDropExtensions.Contains(ext))
            {
                OpenFilePath(path);
            }
            else
            {
                hadInvalid = true;
            }
        }

        if (hadInvalid)
        {
            ShowDropError();
        }
    }

    private void ShowDropError()
    {
        DropErrorBanner.Visibility = Visibility.Visible;
        _errorBannerTimer?.Stop();
        _errorBannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _errorBannerTimer.Tick += (_, _) =>
        {
            DropErrorBanner.Visibility = Visibility.Collapsed;
            _errorBannerTimer!.Stop();
        };
        _errorBannerTimer.Start();
    }

    // ===================== Formatting toolbar =====================

    private void WrapSelection(string prefix, string suffix, string placeholder)
    {
        var selStart = RawEditor.SelectionStart;
        var selLength = RawEditor.SelectionLength;

        if (selLength > 0)
        {
            var selected = RawEditor.Document.GetText(selStart, selLength);
            RawEditor.Document.Replace(selStart, selLength, prefix + selected + suffix);
            RawEditor.Select(selStart + prefix.Length, selected.Length);
        }
        else
        {
            var text = prefix + placeholder + suffix;
            RawEditor.Document.Insert(RawEditor.CaretOffset, text);
            RawEditor.Select(RawEditor.CaretOffset - text.Length + prefix.Length, placeholder.Length);
        }

        RawEditor.Focus();
    }

    private void InsertLinePrefix(string prefix)
    {
        var line = RawEditor.Document.GetLineByOffset(RawEditor.CaretOffset);
        RawEditor.Document.Insert(line.Offset, prefix);
        RawEditor.Focus();
    }

    private void InsertBlock(string text)
    {
        var offset = RawEditor.CaretOffset;
        RawEditor.Document.Insert(offset, text);
        RawEditor.CaretOffset = offset + text.Length;
        RawEditor.Focus();
    }

    private void InsertH1_Click(object sender, RoutedEventArgs e) => InsertLinePrefix("# ");
    private void InsertH2_Click(object sender, RoutedEventArgs e) => InsertLinePrefix("## ");
    private void InsertH3_Click(object sender, RoutedEventArgs e) => InsertLinePrefix("### ");
    private void InsertBold_Click(object sender, RoutedEventArgs e) => WrapSelection("**", "**", "texte");
    private void InsertItalic_Click(object sender, RoutedEventArgs e) => WrapSelection("*", "*", "texte");
    private void InsertBulletList_Click(object sender, RoutedEventArgs e) => InsertLinePrefix("- ");
    private void InsertNumberedList_Click(object sender, RoutedEventArgs e) => InsertLinePrefix("1. ");
    private void InsertLink_Click(object sender, RoutedEventArgs e) => WrapSelection("[", "](url)", "texte");
    private void InsertImage_Click(object sender, RoutedEventArgs e) => WrapSelection("![", "](url)", "alt");
    private void InsertCodeBlock_Click(object sender, RoutedEventArgs e) => InsertBlock("\n```\ncode\n```\n");
    private void InsertQuote_Click(object sender, RoutedEventArgs e) => InsertLinePrefix("> ");

    private void InsertTable_Click(object sender, RoutedEventArgs e) =>
        InsertBlock("\n| Colonne 1 | Colonne 2 |\n| --- | --- |\n| Valeur 1 | Valeur 2 |\n");

    // ===================== Scroll / selection sync =====================

    private void RawEditor_ScrollOffsetChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorScrollSync)
        {
            _suppressEditorScrollSync = false;
            return;
        }

        if (!Vm.Settings.SyncScroll || !_previewReady || PreviewView.CoreWebView2 == null)
        {
            return;
        }

        var max = RawEditor.ExtentHeight - RawEditor.ViewportHeight;
        var ratio = max > 0 ? RawEditor.VerticalOffset / max : 0;

        if (Vm.ActiveTab != null)
        {
            Vm.ActiveTab.SavedRawScrollOffset = RawEditor.VerticalOffset;
        }

        var ratioStr = ratio.ToString(CultureInfo.InvariantCulture);
        _ = PreviewView.CoreWebView2.ExecuteScriptAsync($"window.__md && window.__md.scrollToRatio({ratioStr})");
    }

    private void RawEditor_SelectionChanged(object? sender, EventArgs e)
    {
        if (!Vm.Settings.SyncSelection || !_previewReady || PreviewView.CoreWebView2 == null)
        {
            return;
        }

        if (RawEditor.SelectionLength <= 0)
        {
            _ = PreviewView.CoreWebView2.ExecuteScriptAsync("window.__md && window.__md.clearHighlights()");
            return;
        }

        var start = RawEditor.SelectionStart;
        var end = start + RawEditor.SelectionLength;
        _ = PreviewView.CoreWebView2.ExecuteScriptAsync($"window.__md && window.__md.highlightRange({start},{end})");
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.WebMessageAsJson;
        }
        catch
        {
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProp))
        {
            return;
        }

        switch (typeProp.GetString())
        {
            case "scroll":
                HandleIncomingScroll(root);
                break;
            case "select":
                HandleIncomingSelection(root);
                break;
        }
    }

    private void HandleIncomingScroll(JsonElement root)
    {
        if (!Vm.Settings.SyncScroll)
        {
            return;
        }

        var ratio = root.GetProperty("ratio").GetDouble();
        _suppressEditorScrollSync = true;
        var max = RawEditor.ExtentHeight - RawEditor.ViewportHeight;
        RawEditor.ScrollToVerticalOffset(ratio * Math.Max(0, max));
    }

    private void HandleIncomingSelection(JsonElement root)
    {
        if (!Vm.Settings.SyncSelection)
        {
            return;
        }

        var docLength = RawEditor.Document.TextLength;
        var start = Math.Clamp(root.GetProperty("start").GetInt32(), 0, docLength);
        var end = Math.Clamp(root.GetProperty("end").GetInt32(), start, docLength);

        RawEditor.Select(start, end - start);
        var location = RawEditor.Document.GetLocation(start);
        RawEditor.ScrollTo(location.Line, location.Column);
    }

    private void SyncOption_Changed(object sender, RoutedEventArgs e) => Vm.PersistSettings();

    private void WordWrap_Changed(object sender, RoutedEventArgs e)
    {
        RawEditor.WordWrap = Vm.Settings.WordWrap;
        Vm.PersistSettings();
    }

    // ===================== Find/Replace & About dialogs =====================

    private void FindMenuItem_Click(object sender, RoutedEventArgs e) => ShowFindReplace(false);

    private void ReplaceMenuItem_Click(object sender, RoutedEventArgs e) => ShowFindReplace(true);

    private void ShowFindReplace(bool replaceMode)
    {
        if (_findReplaceWindow == null || !_findReplaceWindow.IsLoaded)
        {
            _findReplaceWindow = new FindReplaceWindow(Vm, this) { Owner = this };
        }

        _findReplaceWindow.SetMode(replaceMode);
        _findReplaceWindow.Show();
        _findReplaceWindow.Activate();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_aboutWindow == null || !_aboutWindow.IsLoaded)
        {
            _aboutWindow = new AboutWindow { Owner = this };
        }

        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    public void SelectRangeInActiveEditor(DocumentTabViewModel tab, int start, int length)
    {
        if (Vm.ActiveTab != tab)
        {
            Vm.ActiveTab = tab;
        }

        RawEditor.Focus();
        RawEditor.Select(start, length);
        var location = RawEditor.Document.GetLocation(start);
        RawEditor.ScrollTo(location.Line, location.Column);
    }
}
