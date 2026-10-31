using System;
using System.ComponentModel;
using COMrade.Core.Sessions;
using COMrade.Terminal;
using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace COMrade.Views;

/// <summary>The contents of a device tab: toolbar, bars, settings card or terminal, status bar.</summary>
public sealed partial class DeviceTabView : UserControl, IDisposable
{
    private readonly DeviceTerminal _terminal;
    private bool _wasOpen;

    public DeviceTabView(DeviceTabViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        _terminal = new DeviceTerminal(viewModel.Session, Terminal, viewModel.Settings.Terminal, viewModel.History);
        viewModel.TerminalSettingsChanged += OnTerminalSettingsChanged;
        viewModel.TerminalMessage += OnTerminalMessage;
        viewModel.ClearRequested += OnClearRequested;
        viewModel.PropertyChanged += OnPropertyChanged;

        // Hidden tabs must hide the terminal itself, see TerminalView.
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Terminal.Visibility = Visibility);
    }

    public DeviceTabViewModel ViewModel { get; }

    public void FocusTerminal() => Terminal.FocusTerminal();

    /// <summary>The terminal's character grid, in physical pixels; null until it has been laid out.</summary>
    public (int CellWidth, int CellHeight, int GridWidth, int GridHeight)? GetGrid() => Terminal.GetGrid();

    public void Copy() => Terminal.Copy();

    public void Paste() => Terminal.Paste();

    public void Dispose()
    {
        ViewModel.TerminalSettingsChanged -= OnTerminalSettingsChanged;
        ViewModel.TerminalMessage -= OnTerminalMessage;
        ViewModel.ClearRequested -= OnClearRequested;
        ViewModel.PropertyChanged -= OnPropertyChanged;
        _terminal.Dispose();
    }

    private void OnTerminalSettingsChanged(object? sender, EventArgs e) => _terminal.Settings = ViewModel.Settings.Terminal;

    private void OnTerminalMessage(object? sender, string message) => _terminal.Show(message);

    private void OnClearRequested(object? sender, EventArgs e) => _terminal.Clear();

    // Typing can start as soon as the port opens.
    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var isOpen = ViewModel.Session.State is PortSessionState.Open;
        if (isOpen && !_wasOpen && Visibility is Visibility.Visible)
            Terminal.FocusTerminal();
        _wasOpen = isOpen;
    }
}
