using COMrade.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace COMrade.Views;

/// <summary>The fields of a device's settings, bound to a <see cref="DeviceSettingsEditor"/>.</summary>
public sealed partial class DeviceSettingsForm : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(DeviceSettingsEditor), typeof(DeviceSettingsForm), new PropertyMetadata(null));

    public DeviceSettingsForm() => InitializeComponent();

    public DeviceSettingsEditor? Editor
    {
        get => (DeviceSettingsEditor?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }
}
