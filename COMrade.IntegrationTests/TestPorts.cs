namespace COMrade.IntegrationTests;

/// <summary>Real ports to test against, named by environment variables; tests needing one are skipped otherwise.</summary>
internal static class TestPorts
{
    /// <summary>Any real serial port; nothing needs to be connected to it.</summary>
    public static string Any => Require("COMRADE_TEST_PORT", "any real serial port, e.g. COM5");

    /// <summary>A port whose TX pin is wired to its RX pin, so everything sent comes back.</summary>
    public static string Loopback => Require("COMRADE_LOOPBACK_PORT", "a port with TX wired to RX");

    private static string Require(string variable, string description)
    {
        var port = Environment.GetEnvironmentVariable(variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(port), $"Set {variable} to {description} to run this test.");
        return port!;
    }
}
