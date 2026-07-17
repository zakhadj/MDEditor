using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MdEditor.ViewModels;

public class FindMatchEventArgs : EventArgs
{
    public DocumentTabViewModel Tab { get; }
    public int Start { get; }
    public int Length { get; }

    public FindMatchEventArgs(DocumentTabViewModel tab, int start, int length)
    {
        Tab = tab;
        Start = start;
        Length = length;
    }
}

public partial class FindReplaceViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;

    private int _currentTabOffset;
    private int _allTabsOffset;
    private int _allTabsTabIndex = -1;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _isReplaceMode;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public event EventHandler<FindMatchEventArgs>? MatchFound;

    public FindReplaceViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    partial void OnSearchTextChanged(string value) => ResetSearchPosition();

    partial void OnMatchCaseChanged(bool value) => ResetSearchPosition();

    public void ResetSearchPosition()
    {
        _currentTabOffset = 0;
        _allTabsOffset = 0;
        _allTabsTabIndex = -1;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void FindInCurrentTab()
    {
        var tab = _mainViewModel.ActiveTab;
        if (tab == null || string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        var idx = _mainViewModel.SearchService.FindNext(tab.Document.Text, SearchText, _currentTabOffset, MatchCase);
        if (idx < 0)
        {
            StatusMessage = "Aucune occurrence trouvée.";
            _currentTabOffset = 0;
            return;
        }

        _currentTabOffset = idx + SearchText.Length;
        StatusMessage = string.Empty;
        MatchFound?.Invoke(this, new FindMatchEventArgs(tab, idx, SearchText.Length));
    }

    [RelayCommand]
    private void FindInAllTabs()
    {
        var tabs = _mainViewModel.Tabs;
        if (tabs.Count == 0 || string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        if (_allTabsTabIndex < 0 || _allTabsTabIndex >= tabs.Count)
        {
            _allTabsTabIndex = Math.Max(0, _mainViewModel.ActiveTab == null ? 0 : tabs.IndexOf(_mainViewModel.ActiveTab));
            _allTabsOffset = 0;
        }

        var startIndex = _allTabsTabIndex;

        // Walk forward through every other tab (no wrap) from the current offset.
        for (var i = 0; i < tabs.Count; i++)
        {
            var tabIndex = (startIndex + i) % tabs.Count;
            var tab = tabs[tabIndex];
            var searchFrom = tabIndex == startIndex ? _allTabsOffset : 0;
            var idx = _mainViewModel.SearchService.IndexOf(tab.Document.Text, SearchText, searchFrom, MatchCase);
            if (idx >= 0)
            {
                _allTabsTabIndex = tabIndex;
                _allTabsOffset = idx + SearchText.Length;
                StatusMessage = string.Empty;
                MatchFound?.Invoke(this, new FindMatchEventArgs(tab, idx, SearchText.Length));
                return;
            }
        }

        // Nothing forward in any tab: wrap back within the starting tab's own prefix.
        var wrapIdx = _mainViewModel.SearchService.IndexOf(tabs[startIndex].Document.Text, SearchText, 0, MatchCase);
        if (wrapIdx >= 0 && wrapIdx < _allTabsOffset)
        {
            _allTabsTabIndex = startIndex;
            _allTabsOffset = wrapIdx + SearchText.Length;
            StatusMessage = string.Empty;
            MatchFound?.Invoke(this, new FindMatchEventArgs(tabs[startIndex], wrapIdx, SearchText.Length));
            return;
        }

        StatusMessage = "Aucune occurrence trouvée dans les onglets ouverts.";
        _allTabsTabIndex = -1;
        _allTabsOffset = 0;
    }

    [RelayCommand]
    private void ReplaceInCurrentTab()
    {
        var tab = _mainViewModel.ActiveTab;
        if (tab == null)
        {
            return;
        }

        var result = _mainViewModel.SearchService.ReplaceAll(tab.Document.Text, SearchText, ReplaceText, MatchCase, out var count);
        if (count > 0)
        {
            tab.Document.Text = result;
        }

        StatusMessage = count > 0 ? $"{count} remplacement(s) effectué(s)." : "Aucune occurrence trouvée.";
        ResetSearchPosition();
    }

    [RelayCommand]
    private void ReplaceInAllTabs()
    {
        var total = 0;
        foreach (var tab in _mainViewModel.Tabs)
        {
            var result = _mainViewModel.SearchService.ReplaceAll(tab.Document.Text, SearchText, ReplaceText, MatchCase, out var count);
            if (count > 0)
            {
                tab.Document.Text = result;
                total += count;
            }
        }

        StatusMessage = total > 0
            ? $"{total} remplacement(s) effectué(s) dans {_mainViewModel.Tabs.Count} onglet(s)."
            : "Aucune occurrence trouvée.";
        ResetSearchPosition();
    }
}
