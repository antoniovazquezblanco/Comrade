using System.IO.Pipes;
using System.Security.Principal;
using COMrade.Core.Mcp;
using COMrade.Core.Settings;

namespace COMrade.Core.Tests.Mcp;

public sealed class McpPipeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData(AiAccess.Off, true)]
    [InlineData(AiAccess.PerTab, false)]
    [InlineData(AiAccess.AllPorts, false)]
    public void The_relay_reads_ai_access_from_the_settings_file_as_saved(AiAccess access, bool off)
    {
        // Saving the defaults writes nothing: turn it on first, so that Off is written too.
        var path = Path.Combine(_folder, "settings.json");
        var store = new AppSettingsStore(path);
        store.Save(AppSettings.Default with { AiAccess = AiAccess.AllPorts });
        store.Save(AppSettings.Default with { AiAccess = access });

        Assert.Equal(off, McpPipe.IsAccessOff(File.ReadAllText(path)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{ \"aiAccess\": 2 }")]
    [InlineData("{ \"aiAccess\": \"Everything\" }")]
    [InlineData("{ not json")]
    public void Settings_that_do_not_turn_ai_access_on_leave_it_off(string json)
    {
        Assert.True(McpPipe.IsAccessOff(json));
    }

    [Fact]
    public void Each_windows_user_has_a_pipe_of_their_own()
    {
        Assert.Equal($"COMrade.Mcp.{WindowsIdentity.GetCurrent().User!.Value}", McpPipe.Name);
    }

    [Fact]
    public async Task A_pipe_the_user_does_not_own_is_refused()
    {
        // .NET's CurrentUserOnly makes the token's default owner the pipe's owner: in an elevated
        // process, such as a build agent's, that is the Administrators group rather than the user.
        using var identity = WindowsIdentity.GetCurrent();
        Assert.SkipWhen(identity.Owner == identity.User, "Only an elevated process can create a pipe its user does not own.");
        var name = $"COMrade.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var waiting = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => McpPipe.ConnectAsync(name, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await waiting;
    }
}
