using System.ComponentModel;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using COMrade.Core.Ports;
using COMrade.Core.Settings;
using Microsoft.Win32.SafeHandles;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace COMrade.Core.Mcp;

public enum McpServerState
{
    /// <summary>AI access is off: nothing listens.</summary>
    Off,

    /// <summary>Waiting for assistants on the pipe.</summary>
    Listening,

    /// <summary>Another COMrade of the same user already serves assistants.</summary>
    Unavailable,
}

/// <summary>
/// COMrade's MCP server. While AI access is on it listens on a named pipe that only the current
/// user can open, and gives each assistant that connects through the relay (COMrade.Mcp.exe) its
/// own MCP session with the tools of <see cref="AssistantTools"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class McpPipeServer : IAsyncDisposable
{
    // For the tools' parameters and results (see AssistantResults).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>How often to try again to take the pipe from another COMrade.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private static readonly MethodInfo[] ToolMethods = typeof(AssistantTools)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
        .ToArray();

    private readonly IAssistantHost _host;
    private readonly PortList _ports;
    private readonly DeviceSettingsStore _devices;
    private readonly AppSettingsStore _settings;
    private readonly string _version;
    private readonly string _pipeName;
    private readonly TimeProvider _time;
    private readonly AssistantActivity _activity;
    private readonly Lock _lock = new();
    private readonly List<Task> _running = [];
    private CancellationTokenSource? _listening;
    private McpServerState _state;
    private int _clients;
    private bool _disposed;

    /// <param name="version">COMrade's version, told to assistants.</param>
    /// <param name="pipeName">The pipe to listen on; <see cref="McpPipe.Name"/> unless a test chooses another.</param>
    public McpPipeServer(
        IAssistantHost host,
        PortList ports,
        DeviceSettingsStore devices,
        AppSettingsStore settings,
        string version,
        string? pipeName = null,
        TimeProvider? time = null)
    {
        _host = host;
        _ports = ports;
        _devices = devices;
        _settings = settings;
        _version = version;
        _pipeName = pipeName ?? McpPipe.Name;
        _time = time ?? TimeProvider.System;
        _activity = new AssistantActivity(_time);
    }

    /// <summary><see cref="State"/> or <see cref="Clients"/> changed. Raised on any thread.</summary>
    public event EventHandler? Changed;

    public McpServerState State
    {
        get
        {
            lock (_lock)
                return _state;
        }
    }

    /// <summary>Assistants connected now.</summary>
    public int Clients => Volatile.Read(ref _clients);

    /// <summary>Listens while the settings allow it, from now on.</summary>
    public void Start()
    {
        _settings.Changed += OnSettingsChanged;
        Apply();
    }

    /// <summary>Stops listening and disconnects every assistant.</summary>
    public async ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        Task[] running;
        lock (_lock)
        {
            _disposed = true;
            StopListening();
            running = [.. _running];
        }

        await Task.WhenAll(running);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Apply();

    // Listens unless AI access is off. Switching between per tab and all ports needs nothing:
    // the tools check the setting on every call.
    private void Apply()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            var on = _settings.Current.AiAccess is not AiAccess.Off;
            if (on && _listening is null)
            {
                _listening = new CancellationTokenSource();
                Track(ListenAsync(_listening.Token));
            }
            else if (!on && _listening is not null)
            {
                StopListening();
            }
        }
    }

    // Must be called holding the lock.
    private void StopListening()
    {
        _listening?.Cancel();
        _listening?.Dispose();
        _listening = null;
        SetState(McpServerState.Off);
    }

    private async Task ListenAsync(CancellationToken stop)
    {
        var first = true;
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(_pipeName, first);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try again later: the other COMrade may close.
                if (first)
                    SetStateUnlessStopped(McpServerState.Unavailable, stop);
                if (!await DelayAsync(RetryDelay, stop))
                    return;
                continue;
            }

            first = false;
            SetStateUnlessStopped(McpServerState.Listening, stop);
            try
            {
                await pipe.WaitForConnectionAsync(stop);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                return;
            }
            catch (IOException)
            {
                // The client went away before the connection completed.
                await pipe.DisposeAsync();
                continue;
            }

            Track(ServeAsync(pipe, stop));
        }
    }

    // A pipe instance only the current user can open, and only from this machine: .NET's own
    // CurrentUserOnly would let the same user in over the network (SMB), so the pipe is created
    // here with PIPE_REJECT_REMOTE_CLIENTS. The owner is the user, also when elevated, as
    // McpPipe.ConnectAsync checks. The first instance must create the pipe: if it exists,
    // another COMrade of this user serves assistants, and joining its pipe would split them.
    private static NamedPipeServerStream CreatePipe(string name, bool first)
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        var descriptor = security.GetSecurityDescriptorBinaryForm();

        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
            };
            var openMode = PipeAccessDuplex | FileFlagOverlapped | (first ? FileFlagFirstPipeInstance : 0);
            var handle = CreateNamedPipe($@"\\.\pipe\{name}", openMode, PipeRejectRemoteClients, PipeUnlimitedInstances, 0, 0, 0, ref attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw error == ErrorAccessDenied
                    ? new UnauthorizedAccessException($"The pipe {name} already exists.")
                    : new IOException($"The pipe {name} could not be created.", new Win32Exception(error));
            }

            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally
        {
            pinned.Free();
        }
    }

    // One assistant: an MCP session over the pipe until either side closes it.
    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        var tools = new AssistantTools(_host, _ports, _devices, _settings, _activity, _time);
        Interlocked.Increment(ref _clients);
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await using var transport = new StreamServerTransport(pipe, pipe, "COMrade");
            await using var server = McpServer.Create(transport, Options(tools));
            await server.RunAsync(stop);
        }
        catch (Exception)
        {
            // The assistant disconnected or COMrade stopped listening; whatever else goes wrong in
            // an assistant's session ends that session, never COMrade.
        }
        finally
        {
            await pipe.DisposeAsync();
            _activity.Release(tools);
            Interlocked.Decrement(ref _clients);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private McpServerOptions Options(AssistantTools tools)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "COMrade", Version = _version },
            ServerInstructions = AssistantTools.Instructions,
            ToolCollection = [],
        };
        foreach (var method in ToolMethods)
            options.ToolCollection.Add(McpServerTool.Create(method, tools, new McpServerToolCreateOptions { SerializerOptions = Json }));
        return options;
    }

    // False when stopped meanwhile.
    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stop)
    {
        try
        {
            await Task.Delay(delay, _time, stop);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // Keeps the task until it ends, so that disposing waits for it.
    private void Track(Task task)
    {
        lock (_lock)
            _running.Add(task);
        _ = task.ContinueWith(
            done =>
            {
                lock (_lock)
                    _running.Remove(done);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void SetStateUnlessStopped(McpServerState state, CancellationToken stop)
    {
        lock (_lock)
        {
            if (!stop.IsCancellationRequested)
                SetState(state);
        }
    }

    // Must be called holding the lock.
    private void SetState(McpServerState state)
    {
        if (_state == state)
            return;
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint FileFlagFirstPipeInstance = 0x00080000;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const uint PipeUnlimitedInstances = 255;
    private const int ErrorAccessDenied = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(
        string name,
        uint openMode,
        uint pipeMode,
        uint maxInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeout,
        ref SecurityAttributes securityAttributes);
}
