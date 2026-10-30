using CommunityToolkit.Mvvm.ComponentModel;

namespace COMrade.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = "COMrade";
}
