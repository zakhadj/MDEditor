using System.Windows;
using System.Windows.Input;
using MdEditor.ViewModels;

namespace MdEditor.Views;

public partial class FindReplaceWindow : Window
{
    private readonly FindReplaceViewModel _vm;
    private readonly MainWindow _owner;

    public FindReplaceWindow(MainViewModel mainViewModel, MainWindow owner)
    {
        _owner = owner;
        _vm = new FindReplaceViewModel(mainViewModel);
        _vm.MatchFound += Vm_MatchFound;
        DataContext = _vm;
        InitializeComponent();
    }

    private void Vm_MatchFound(object? sender, FindMatchEventArgs e)
    {
        _owner.SelectRangeInActiveEditor(e.Tab, e.Start, e.Length);
    }

    public void SetMode(bool replaceMode)
    {
        _vm.IsReplaceMode = replaceMode;
        _vm.ResetSearchPosition();
        Title = replaceMode ? "Remplacer" : "Rechercher";
        ReplacePanel.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        FindButtonsPanel.Visibility = replaceMode ? Visibility.Collapsed : Visibility.Visible;
        ReplaceButtonsPanel.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e) => HandleEnterKey(e);

    private void ReplaceTextBox_KeyDown(object sender, KeyEventArgs e) => HandleEnterKey(e);

    private void HandleEnterKey(KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (_vm.IsReplaceMode)
        {
            _vm.ReplaceInCurrentTabCommand.Execute(null);
        }
        else
        {
            _vm.FindInCurrentTabCommand.Execute(null);
        }

        // Without this, the keystroke keeps propagating after FindInCurrentTab moves focus
        // to the main editor (to select the match), and AvalonEdit then inserts a newline
        // for the same Enter press.
        e.Handled = true;
    }
}
