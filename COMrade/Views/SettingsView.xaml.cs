using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    private void OnForgetClick(object sender, RoutedEventArgs e) =>
        ViewModel.ForgetCommand.Execute((string)((Button)sender).Tag);
}
