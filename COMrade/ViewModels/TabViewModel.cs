using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMrade.ViewModels;

/// <summary>A tab of the main window: a device, or the settings page.</summary>
public abstract class TabViewModel : ObservableObject, IDisposable
{
    /// <summary>Title in the tab strip.</summary>
    public abstract string Header { get; }

    /// <summary>Window title while the tab is selected.</summary>
    public abstract string WindowTitle { get; }

    public virtual void Dispose()
    {
    }
}
