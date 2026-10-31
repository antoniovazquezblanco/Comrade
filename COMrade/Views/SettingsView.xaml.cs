using System;
using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

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
}
