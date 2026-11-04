using System;
using COMrade.Core.Shortcuts;
using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using Windows.System;

namespace COMrade.Views;

/// <summary>The settings page, shown in a tab.</summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsView(SettingsTabViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SettingsTabViewModel ViewModel { get; }

    // The folder picker needs the window it belongs to.
    private async void OnBrowseLogFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (await picker.PickSingleFolderAsync() is { } folder)
            ViewModel.LogFolder = folder.Path;
    }

    private void OnForgetClick(object sender, RoutedEventArgs e) =>
        ViewModel.ForgetCommand.Execute((string)((Button)sender).Tag);

    // Keys pressed with Ctrl reach the view model from the window's keyboard hook, before this
    // button sees them; the button sees the rest.
    private void OnShortcutClick(object sender, RoutedEventArgs e) => ViewModel.StartRecordingShortcut(ActionOf(sender));

    private void OnShortcutKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Tab moves on, which cancels.
        if (!ViewModel.IsRecordingShortcut || e.Key is VirtualKey.Tab)
            return;

        if (e.Key is VirtualKey.Escape)
            ViewModel.StopRecordingShortcut();
        else
            ViewModel.RejectShortcutKey(e.Key);
        // Enter and Space would click the button again.
        e.Handled = true;
    }

    private void OnShortcutLostFocus(object sender, RoutedEventArgs e) => ViewModel.StopRecordingShortcut();

    private void OnResetShortcutClick(object sender, RoutedEventArgs e) => ViewModel.ResetShortcut(ActionOf(sender));

    private void OnRemoveShortcutClick(object sender, RoutedEventArgs e) => ViewModel.RemoveShortcut(ActionOf(sender));

    private static ShortcutAction ActionOf(object sender) => (ShortcutAction)((Button)sender).Tag;
}
