using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace COMrade.ViewModels;

/// <summary>A tab of the main window: a device, or the settings page.</summary>
public abstract class TabViewModel : ObservableObject, IDisposable
{
    private bool _isCompact;

    /// <summary>Full title, e.g. "COM5 · USB-SERIAL CH340"; also the tooltip.</summary>
    public abstract string Header { get; }

    /// <summary>Title in compact tab mode, e.g. "COM5".</summary>
    public virtual string ShortHeader => Header;

    /// <summary>What the tab strip shows: the short title for unselected tabs in compact mode.</summary>
    public string TabTitle => IsCompact ? ShortHeader : Header;

    /// <summary>Set by the window for the tabs that show their short title.</summary>
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (SetProperty(ref _isCompact, value))
                OnPropertyChanged(nameof(TabTitle));
        }
    }

    /// <summary>Window title while the tab is selected.</summary>
    public abstract string WindowTitle { get; }

    public virtual void Dispose()
    {
    }
}
