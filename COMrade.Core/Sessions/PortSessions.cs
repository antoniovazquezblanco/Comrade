using COMrade.Core.Ports;
using COMrade.Core.Serial;

namespace COMrade.Core.Sessions;

/// <summary>The open device tabs; at most one per physical device.</summary>
/// <remarks>Used from the UI thread only.</remarks>
public sealed class PortSessions(PortList ports, OpenSerialConnection openConnection, TimeProvider? time = null)
{
    private readonly List<PortSession> _sessions = [];

    public IReadOnlyList<PortSession> Sessions => _sessions;

    /// <summary>Returns the session already showing this device, or creates one.</summary>
    public PortSession GetOrCreate(SerialPortInfo port, SerialSettings settings)
    {
        var existing = _sessions.FirstOrDefault(session => session.Port.Identity.IsSameDeviceAs(port.Identity));
        if (existing is not null)
            return existing;

        var created = new PortSession(port, settings, ports, openConnection, time);
        _sessions.Add(created);
        return created;
    }

    /// <summary>Closes the session's port and forgets the session.</summary>
    public async Task CloseAsync(PortSession session)
    {
        if (_sessions.Remove(session))
            await session.DisposeAsync();
    }
}
