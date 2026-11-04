using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using COMrade.Core.Sessions;
using COMrade.Core.Settings;
using COMrade.Core.Shortcuts;
using COMrade.Terminal;
using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace COMrade.Views;

/// <summary>The contents of a device tab: toolbar, bars, settings card or terminal, status bar.</summary>
public sealed partial class DeviceTabView : UserControl, IDisposable
{
    private readonly DeviceTerminal _terminal;
    private readonly AppSettingsStore _settings;
    private bool _wasOpen;

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

        // Hidden tabs must hide the terminal itself, see TerminalView.
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Terminal.Visibility = Visibility);
    }

    public DeviceTabViewModel ViewModel { get; }

    public void FocusTerminal() => Terminal.FocusTerminal();

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
            case ShortcutAction.Copy:
                Terminal.Copy();
                return true;
            case ShortcutAction.Paste:
                Terminal.Paste();
                return true;
            case ShortcutAction.ClearTerminal:
                ViewModel.ClearCommand.Execute(null);
                return true;
            case ShortcutAction.StartOrStopLogging:
                ViewModel.IsLogging = !ViewModel.IsLogging;
                return true;
            case ShortcutAction.ScrollToTop:
                Terminal.ScrollToTop();
                return true;
            case ShortcutAction.ScrollPageUp:
                Terminal.ScrollPages(-1);
                return true;
            case ShortcutAction.ScrollPageDown:
                Terminal.ScrollPages(1);
                return true;
            case ShortcutAction.ScrollToBottom:
                Terminal.ScrollToBottom();
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        ViewModel.PickLogFile = null;
        _settings.Changed -= OnSettingsChanged;
        ViewModel.TerminalSettingsChanged -= OnTerminalSettingsChanged;
        ViewModel.TerminalMessage -= OnTerminalMessage;
        ViewModel.ClearRequested -= OnClearRequested;
        ViewModel.PropertyChanged -= OnPropertyChanged;
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

        var keymap = settings.Keymap;
        SetToolTip(OpenPortButton, "Open the port", ShortcutAction.OpenOrClosePort);
        SetToolTip(ClosePortButton, "Close the port", ShortcutAction.OpenOrClosePort);
        SetToolTip(BreakButton, "Send a break of 250 ms", ShortcutAction.SendBreak);
        SetToolTip(ClearButton, "Clear the terminal", ShortcutAction.ClearTerminal);
        SetToolTip(LogButton, "Log this session to a file", ShortcutAction.StartOrStopLogging);
        SetToolTip(ScrollToBottomButton, "Show the newest output", ShortcutAction.ScrollToBottom);

        void SetToolTip(DependencyObject element, string text, ShortcutAction action) =>
            ToolTipService.SetToolTip(element, DisplayText.WithKeys(text, keymap.KeysFor(action)));
    }

    private void OnClearRequested(object? sender, EventArgs e) => _terminal.Clear();

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

    // Typing can start as soon as the port opens.
    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var isOpen = ViewModel.Session.State is PortSessionState.Open;
        if (isOpen && !_wasOpen && Visibility is Visibility.Visible)
            Terminal.FocusTerminal();
        _wasOpen = isOpen;
    }

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
