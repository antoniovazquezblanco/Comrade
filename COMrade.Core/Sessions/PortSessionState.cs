namespace COMrade.Core.Sessions;

public enum PortSessionState
{
    /// <summary>The tab is open but the port is not.</summary>
    Closed,

    /// <summary>Trying to open the port.</summary>
    Opening,

    /// <summary>The port is open; data flows both ways.</summary>
    Open,

    /// <summary>The device went away; the port will reopen when it comes back.</summary>
    Waiting,

    /// <summary>The port could not be opened or failed; see <see cref="PortSession.LastError"/>.</summary>
    Error,
}
