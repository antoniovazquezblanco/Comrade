namespace COMrade.Core.Settings;

/// <summary>How tabs behave when there are more than fit in the window.</summary>
public enum TabMode
{
    /// <summary>Only the selected tab shows its full title; the others shrink to their port.</summary>
    Compact,

    /// <summary>Every tab keeps its full title; the tab strip scrolls.</summary>
    Scrolling,
}
