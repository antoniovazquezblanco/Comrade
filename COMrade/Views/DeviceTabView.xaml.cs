using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using COMrade.Core.Shortcuts;
using COMrade.Terminal;
using COMrade.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using Windows.UI.Core;
using VirtualKey = Windows.System.VirtualKey;

namespace COMrade.Views;

/// <summary>The contents of a device tab: toolbar, bars, terminal and find panel, status bar.</summary>
public sealed partial class DeviceTabView : UserControl, IDisposable
{
    private readonly DeviceTerminal _terminal;
    private readonly AppSettingsStore _settings;
    private readonly DispatcherQueueTimer _highlightLater;
    private bool _wasOpen;
    private OutputView? _shownView;

    // The search the terminal shows highlighted, if any, and whether it was searched again since.
    private Marks? _highlighted;
    private bool _searchedAgain;

    public DeviceTabView(DeviceTabViewModel viewModel, AppSettingsStore settings)
    {
        ViewModel = viewModel;
        viewModel.PickLogFile = PickLogFileAsync;
        _settings = settings;
        InitializeComponent();

        ApplySettings();
        settings.Changed += OnSettingsChanged;

        _terminal = new DeviceTerminal(viewModel.Session, Terminal, viewModel.Settings.Terminal, viewModel.History);
        viewModel.TerminalSettingsChanged += OnTerminalSettingsChanged;
        viewModel.TerminalMessage += OnTerminalMessage;
        viewModel.ClearRequested += OnClearRequested;
        viewModel.PropertyChanged += OnPropertyChanged;
        Terminal.IsScrolledUpChanged += OnIsScrolledUpChanged;

        // Hidden tabs must hide the terminal itself, see TerminalView; the traffic stops following.
        TrafficPane.Model = viewModel.Traffic;
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => ApplyView());
        ApplyView();

        // Highlights wait for typing to pause. A tab moved to another window shows its highlights
        // again.
        _highlightLater = DispatcherQueue.CreateTimer();
        _highlightLater.Interval = TimeSpan.FromMilliseconds(250);
        _highlightLater.IsRepeating = false;
        _highlightLater.Tick += (_, _) => Highlight();
        viewModel.Find.Searched += OnSearched;
        viewModel.Find.Chosen += OnChosen;
        viewModel.Find.PropertyChanged += OnFindChanged;
        ScheduleHighlight();
    }

    public DeviceTabViewModel ViewModel { get; }

    /// <summary>Puts the keyboard focus in the terminal, or in the analysis when it shows.</summary>
    public void FocusTerminal()
    {
        if (ViewModel.View is OutputView.Analysis)
            TrafficPane.Focus(FocusState.Programmatic);
        else
            Terminal.FocusTerminal();
    }

    /// <summary>Opens the find panel with the latest output and puts the cursor in it; not in the analysis.</summary>
    public void ShowFind()
    {
        if (!ViewModel.CanFind)
            return;
        ViewModel.Find.Open();
        DispatcherQueue.TryEnqueue(() =>
        {
            FindBox.Focus(FocusState.Programmatic);
            FindBox.SelectAll();
        });
    }

    private void OnFindClick(object sender, RoutedEventArgs e) => ShowFind();

    private void OnCloseFindClick(object sender, RoutedEventArgs e) => CloseFind();

    private void CloseFind()
    {
        ViewModel.Find.CloseCommand.Execute(null);
        FocusTerminal();
    }

    private void OnFindKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down):
                ViewModel.Find.PreviousCommand.Execute(null);
                break;
            case VirtualKey.Enter:
                ViewModel.Find.NextCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnFindPanelKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not VirtualKey.Escape)
            return;
        CloseFind();
        e.Handled = true;
    }

    // Searching again marks what arrived since too.
    private void OnSearched(object? sender, EventArgs e)
    {
        _searchedAgain = true;
        ScheduleHighlight();
    }

    // Only a match the user chooses moves the terminal: at once, while its colour of its own
    // follows once the selection rests.
    private void OnChosen(object? sender, EventArgs e)
    {
        RevealSelected();
        ScheduleHighlight();
    }

    private void OnMatchClick(object sender, ItemClickEventArgs e) => ViewModel.Find.Choose((FindResult)e.ClickedItem);

    private void OnFindChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FindViewModel.Selected) or nameof(FindViewModel.Highlight) or nameof(FindViewModel.IsOpen))
            ScheduleHighlight();
    }

    private void ScheduleHighlight()
    {
        _highlightLater.Stop();
        _highlightLater.Start();
    }

    // The terminal highlights the matches while the panel is open and asks for it. Showing the
    // output again takes a moment for a long history, so it happens only when the marks change;
    // the view stays where it was.
    private void Highlight()
    {
        var find = ViewModel.Find;
        Marks? wanted = find.IsOpen && find.Highlight && find.Query.Length > 0
            ? new Marks(find.Query, find.MatchCase, find.Selected?.Position)
            : null;
        if (wanted == _highlighted && (wanted is null || !_searchedAgain))
            return;

        _terminal.Repaint(wanted?.Query, wanted?.MatchCase ?? false, wanted?.Selected);
        _highlighted = wanted;
        _searchedAgain = false;
    }

    // The terminal scrolls to the selected match, or the panel says why it cannot.
    private void RevealSelected()
    {
        var find = ViewModel.Find;
        if (find.Selected is not { } selected)
        {
            find.Note = null;
            return;
        }

        var shown = find.Locate(selected) is var (lines, index) && _terminal.Reveal(lines, index);
        find.Note = shown ? null : "The terminal no longer keeps this line, only the list does.";
    }

    // Keep the selected match in view as previous / next move through the list.
    private void OnMatchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView list && list.SelectedItem is { } item)
            list.ScrollIntoView(item);
    }

    /// <summary>The terminal's character grid, in physical pixels; null until it has been laid out.</summary>
    public (int CellWidth, int CellHeight, int GridWidth, int GridHeight)? GetGrid() => Terminal.GetGrid();

    /// <summary>Runs a shortcut that acts on the tab; false for any other.</summary>
    public bool RunShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.OpenOrClosePort:
                ViewModel.OpenOrCloseCommand.Execute(null);
                return true;
            case ShortcutAction.SendBreak:
                ViewModel.BreakCommand.Execute(null);
                return true;
            case ShortcutAction.Copy when AnalysisShows:
                TrafficPane.Copy();
                return true;
            case ShortcutAction.Copy:
                Terminal.Copy();
                return true;
            case ShortcutAction.Paste:
                if (!AnalysisShows)
                    Terminal.Paste();
                return true;
            case ShortcutAction.Find:
                ShowFind();
                return true;
            case ShortcutAction.ClearTerminal:
                ViewModel.ClearCommand.Execute(null);
                return true;
            case ShortcutAction.StartOrStopLogging:
                ViewModel.IsLogging = !ViewModel.IsLogging;
                return true;
            case ShortcutAction.ScrollToTop when AnalysisShows:
                TrafficPane.ScrollToTop();
                return true;
            case ShortcutAction.ScrollToTop:
                Terminal.ScrollToTop();
                return true;
            case ShortcutAction.ScrollPageUp when AnalysisShows:
                TrafficPane.ScrollPages(-1);
                return true;
            case ShortcutAction.ScrollPageUp:
                Terminal.ScrollPages(-1);
                return true;
            case ShortcutAction.ScrollPageDown when AnalysisShows:
                TrafficPane.ScrollPages(1);
                return true;
            case ShortcutAction.ScrollPageDown:
                Terminal.ScrollPages(1);
                return true;
            case ShortcutAction.ScrollToBottom when AnalysisShows:
                TrafficPane.ScrollToBottom();
                return true;
            case ShortcutAction.ScrollToBottom:
                Terminal.ScrollToBottom();
                return true;
            default:
                return false;
        }
    }

    // Copying and scrolling act on the analysis while it shows.
    private bool AnalysisShows => ViewModel.View is OutputView.Analysis;

    public void Dispose()
    {
        ViewModel.PickLogFile = null;
        ViewModel.Traffic.IsActive = false;
        TrafficPane.Model = null;
        _settings.Changed -= OnSettingsChanged;
        ViewModel.TerminalSettingsChanged -= OnTerminalSettingsChanged;
        ViewModel.TerminalMessage -= OnTerminalMessage;
        ViewModel.ClearRequested -= OnClearRequested;
        ViewModel.PropertyChanged -= OnPropertyChanged;
        ViewModel.Find.Searched -= OnSearched;
        ViewModel.Find.Chosen -= OnChosen;
        ViewModel.Find.PropertyChanged -= OnFindChanged;
        _highlightLater.Stop();
        _terminal.Dispose();
    }

    private void OnTerminalSettingsChanged(object? sender, EventArgs e) => _terminal.Settings = ViewModel.Settings.Terminal;

    private void OnTerminalMessage(object? sender, string message) => _terminal.Show(message);

    // The settings page saves on the UI thread.
    private void OnSettingsChanged(object? sender, EventArgs e) => ApplySettings();

    // The terminal follows the appearance in the settings, also while it is open, and the
    // tooltips the keys of the shortcuts.
    private void ApplySettings()
    {
        var settings = _settings.Current;
        Terminal.Appearance = settings.TerminalOrDefault;
        TrafficPane.Appearance = settings.TerminalOrDefault;

        var keymap = settings.Keymap;
        SetToolTip(OpenPortButton, "Open the port", ShortcutAction.OpenOrClosePort);
        SetToolTip(ClosePortButton, "Close the port", ShortcutAction.OpenOrClosePort);
        SetToolTip(BreakButton, "Send a break of 250 ms", ShortcutAction.SendBreak);
        SetToolTip(ClearButton, "Clear the terminal and the analysis", ShortcutAction.ClearTerminal);
        SetToolTip(FindButton, "Find in the output", ShortcutAction.Find);
        SetToolTip(LogButton, "Log this session to a file", ShortcutAction.StartOrStopLogging);
        SetToolTip(ScrollToBottomButton, "Show the newest output", ShortcutAction.ScrollToBottom);

        void SetToolTip(DependencyObject element, string text, ShortcutAction action) =>
            ToolTipService.SetToolTip(element, DisplayText.WithKeys(text, keymap.KeysFor(action)));
    }

    // What the terminal shows highlighted: the matches of a search, the selected one apart.
    private readonly record struct Marks(string Query, bool MatchCase, (int Line, int Column)? Selected);

    // The panel lists what is shown: nothing, once cleared.
    private void OnClearRequested(object? sender, EventArgs e)
    {
        _terminal.Clear();
        _highlighted = null;
        if (ViewModel.Find.IsOpen)
            ViewModel.Find.RefreshCommand.Execute(null);
    }

    private void OnIsScrolledUpChanged(object? sender, EventArgs e) =>
        ScrollToBottomButton.Visibility = Terminal.IsScrolledUp ? Visibility.Visible : Visibility.Collapsed;

    // The terminal takes the focus first: the button disappears once at the bottom.
    private void OnScrollToBottomClick(object sender, RoutedEventArgs e)
    {
        Terminal.FocusTerminal();
        Terminal.ScrollToBottom();
    }

    // The flyout closes so the tab shows the detection and what the device prints.
    private void OnDetectSpeedClick(object sender, RoutedEventArgs e)
    {
        PortButton.Flyout.Hide();
        ViewModel.DetectSpeedCommand.Execute(null);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(DeviceTabViewModel.View))
            ApplyView();

        // Typing can start as soon as the port opens.
        var isOpen = ViewModel.Session.State is PortSessionState.Open;
        if (isOpen && !_wasOpen && Visibility is Visibility.Visible && !AnalysisShows)
            Terminal.FocusTerminal();
        _wasOpen = isOpen;
    }

    // The terminal or the analysis. The terminal is a native window, so it hides itself; the
    // analysis follows the traffic only while it shows. Find searches the terminal: it closes for
    // the analysis. A view chosen takes the keyboard focus.
    private void ApplyView()
    {
        var analysis = AnalysisShows;
        var shown = Visibility is Visibility.Visible;
        TrafficPane.Visibility = analysis ? Visibility.Visible : Visibility.Collapsed;
        Terminal.Visibility = shown && !analysis ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.Traffic.IsActive = shown && analysis;
        if (analysis && ViewModel.Find.IsOpen)
            ViewModel.Find.CloseCommand.Execute(null);

        if (_shownView is { } before && before != ViewModel.View && shown)
            FocusTerminal();
        _shownView = ViewModel.View;
    }

    private void OnTerminalViewClick(object sender, RoutedEventArgs e) => ViewModel.View = OutputView.Terminal;

    private void OnAnalysisViewClick(object sender, RoutedEventArgs e) => ViewModel.View = OutputView.Analysis;

    // The Save dialog needs the window it belongs to. It starts in the log folder the first time
    // and then where the last log was saved.
    private async Task<string?> PickLogFileAsync(string fileName, string folder)
    {
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(fileName),
            DefaultFileExtension = ".log",
            SettingsIdentifier = "SessionLog",
        };
        if (Directory.Exists(folder))
            picker.SuggestedStartFolder = folder;
        picker.FileTypeChoices.Add("Log or text file", [".log", ".txt"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
