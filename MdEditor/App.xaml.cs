using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MdEditor.Models;
using MdEditor.Services;
using MdEditor.ViewModels;

namespace MdEditor;

public partial class App : Application
{
    private const string MutexName = "MdEditor-SingleInstance-Mutex-9F3B9C2E";
    private const string PipeName = "MdEditor-SingleInstance-Pipe-9F3B9C2E";

    private Mutex? _instanceMutex;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, MutexName, out var isNewInstance);
        if (!isNewInstance)
        {
            // MD Editor is already running: hand the file(s) off to that instance and exit.
            // Without this, "Open with" while the app is already open would launch a second
            // process that reads/writes the same session.json and autosave files concurrently.
            ForwardArgsToRunningInstance(e.Args);
            Shutdown();
            return;
        }

        StartPipeServer();

        var settingsService = new SettingsService();
        var sessionService = new SessionService();
        var renderService = new MarkdownRenderService();
        var searchService = new SearchService();

        _mainViewModel = new MainViewModel(settingsService, sessionService, renderService, searchService);
        ThemeService.Apply(_mainViewModel.Settings.Theme);

        RestoreSession(_mainViewModel, sessionService);

        var filesToOpen = e.Args.Where(File.Exists).ToList();
        if (filesToOpen.Count > 0)
        {
            OpenFiles(_mainViewModel, filesToOpen);
        }

        _mainWindow = new MainWindow(_mainViewModel);
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Mutex wasn't owned by this instance (e.g. the redirect-and-exit path); nothing to release.
        }

        base.OnExit(e);
    }

    // ===================== Single-instance file forwarding =====================

    private static void ForwardArgsToRunningInstance(string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            foreach (var arg in args)
            {
                writer.WriteLine(arg);
            }
        }
        catch
        {
            // Best-effort: if the running instance can't be reached, this one-shot process has
            // nothing else useful to do.
        }
    }

    private void StartPipeServer()
    {
        _ = Task.Run(PipeServerLoopAsync);
    }

    private async Task PipeServerLoopAsync()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();

                using var reader = new StreamReader(server);
                var paths = new List<string>();
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        paths.Add(line);
                    }
                }

                if (paths.Count > 0)
                {
                    Dispatcher.Invoke(() => HandleIncomingFiles(paths));
                }
            }
            catch
            {
                // Keep listening even if a single connection misbehaves.
            }
        }
    }

    private void HandleIncomingFiles(List<string> paths)
    {
        if (_mainViewModel == null || _mainWindow == null)
        {
            return;
        }

        OpenFiles(_mainViewModel, paths.Where(File.Exists).ToList());

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private static void OpenFiles(MainViewModel vm, List<string> paths)
    {
        foreach (var path in paths)
        {
            var existing = vm.FindTabByPath(path);
            if (existing != null)
            {
                vm.ActiveTab = existing;
                continue;
            }

            try
            {
                var content = File.ReadAllText(path);
                vm.CreateTab(Path.GetFileName(path), path, content, false);
                vm.AddRecentFile(path);
            }
            catch
            {
                // Skip files that can't be read.
            }
        }
    }

    // ===================== Session restore =====================

    private static void RestoreSession(MainViewModel vm, SessionService sessionService)
    {
        var manifest = sessionService.LoadManifest();
        if (manifest != null && manifest.Tabs.Count > 0)
        {
            DocumentTabViewModel? activeTab = null;

            foreach (var entry in manifest.Tabs)
            {
                var autosaveContent = sessionService.ReadAutosaveContent(entry.AutosavePath);
                var isDirty = ComputeIsDirty(entry, autosaveContent);
                var tab = vm.CreateTab(entry.DisplayName, entry.OriginalFilePath, autosaveContent, isDirty, entry.Id, entry.AutosavePath);
                if (entry.Id == manifest.ActiveTabId)
                {
                    activeTab = tab;
                }
            }

            vm.ActiveTab = activeTab ?? vm.Tabs.FirstOrDefault();
            return;
        }

        if (!vm.Settings.HasSeenWelcome)
        {
            vm.Settings.HasSeenWelcome = true;
            vm.PersistSettings();
            if (TryOpenReadme(vm))
            {
                return;
            }
        }

        vm.NewUntitledTab();
    }

    private static bool TryOpenReadme(MainViewModel vm)
    {
        try
        {
            var readmePath = Path.Combine(AppContext.BaseDirectory, "README.md");
            if (File.Exists(readmePath))
            {
                var content = File.ReadAllText(readmePath);
                vm.CreateTab("README.md", null, content, false);
                return true;
            }
        }
        catch
        {
            // Fall back to the default empty tab if the bundled README can't be read.
        }

        return false;
    }

    private static bool ComputeIsDirty(SessionTabEntry entry, string autosaveContent)
    {
        if (string.IsNullOrEmpty(entry.OriginalFilePath))
        {
            return autosaveContent.Length > 0;
        }

        try
        {
            if (File.Exists(entry.OriginalFilePath))
            {
                var onDisk = File.ReadAllText(entry.OriginalFilePath);
                return onDisk != autosaveContent;
            }
        }
        catch
        {
            // If the original file can't be read, err on the side of "dirty" so nothing looks silently lost.
        }

        return true;
    }
}
