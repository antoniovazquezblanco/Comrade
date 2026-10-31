using System;
using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace COMrade.Views;

/// <summary>The port list shown while no tab is open.</summary>
public sealed partial class PortPickerView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(PortListViewModel), typeof(PortPickerView), new PropertyMetadata(null));

    public PortPickerView() => InitializeComponent();

    public event EventHandler<PortItemViewModel>? PortSelected;

    public PortListViewModel? ViewModel
    {
        get => (PortListViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void OnSelectClick(object sender, RoutedEventArgs e) =>
        PortSelected?.Invoke(this, (PortItemViewModel)((Button)sender).Tag);
}
