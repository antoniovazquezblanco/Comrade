using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace COMrade.Views;

/// <summary>Picks the tab header template for a device tab or the settings tab.</summary>
public sealed partial class TabTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Device { get; set; }

    public DataTemplate? Settings { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is SettingsTabViewModel ? Settings : Device;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
