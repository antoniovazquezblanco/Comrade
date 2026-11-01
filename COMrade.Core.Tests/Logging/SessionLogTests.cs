using System.Text;
using COMrade.Core.Logging;
using COMrade.Core.Settings;
using Microsoft.Extensions.Time.Testing;

namespace COMrade.Core.Tests.Logging;

public sealed class SessionLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"comrade-tests-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 12, 34, 56, TimeSpan.Zero));

    public SessionLogTests() => _time.SetLocalTimeZone(TimeZoneInfo.Utc);

    private LogSettings Settings => new(Folder: _folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static string Read(SessionLog log)
    {
        log.Dispose();
        return File.ReadAllText(log.Path);
    }

    [Fact]
    public void Log_is_named_after_the_device_and_the_time()
    {
        using var log = SessionLog.Start("Router console", Settings, _time);

        Assert.Equal(Path.Combine(_folder, "Router console-2026-11-01_12-34-56.log"), log.Path);
    }

    [Fact]
    public void Characters_not_allowed_in_file_names_are_replaced()
    {
        using var log = SessionLog.Start("a/b:c*", Settings, _time);

        Assert.Equal("a_b_c_-2026-11-01_12-34-56.log", Path.GetFileName(log.Path));
    }

    [Fact]
    public void An_existing_log_is_never_overwritten()
    {
        using var first = SessionLog.Start("COM5", Settings, _time);
        using var second = SessionLog.Start("COM5", Settings, _time);

        Assert.Equal("COM5-2026-11-01_12-34-56 (2).log", Path.GetFileName(second.Path));
    }

    [Fact]
    public void Text_is_logged_without_colour_codes()
    {
        var log = SessionLog.Start("COM5", Settings, _time);

        log.Write("\e[32mready\e[0m\r\n"u8);

        Assert.Equal("ready\r\n", Read(log));
    }

    [Fact]
    public void Colour_codes_can_be_kept()
    {
        var log = SessionLog.Start("COM5", Settings with { KeepColourCodes = true }, _time);

        log.Write("\e[32mready\e[0m\r\n"u8);

        Assert.Equal("\e[32mready\e[0m\r\n", Read(log));
    }

    [Fact]
    public void Each_line_starts_with_the_time_it_began()
    {
        var log = SessionLog.Start("COM5", Settings with { Timestamps = true }, _time);

        log.Write("boot"u8);
        _time.Advance(TimeSpan.FromMilliseconds(250));
        log.Write(" done\r\nnext\r\n"u8);

        Assert.Equal("[2026-11-01 12:34:56.000] boot done\r\n[2026-11-01 12:34:56.250] next\r\n", Read(log));
    }

    [Fact]
    public void Characters_split_across_reads_are_logged_whole()
    {
        var log = SessionLog.Start("COM5", Settings, _time);
        var bytes = Encoding.UTF8.GetBytes("Ángulo ✓");

        log.Write(bytes.AsSpan(0, 1));
        log.Write(bytes.AsSpan(1));

        Assert.Equal("Ángulo ✓", Read(log));
    }

    [Fact]
    public void Output_reaches_the_file_within_a_second()
    {
        using var log = SessionLog.Start("COM5", Settings, _time);
        log.Write("pending"u8);

        _time.Advance(TimeSpan.FromSeconds(1));

        using var reader = new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.Equal("pending", new StreamReader(reader).ReadToEnd());
    }

    [Fact]
    public void Missing_folders_are_created()
    {
        using var log = SessionLog.Start("COM5", Settings with { Folder = Path.Combine(_folder, "a", "b") }, _time);

        Assert.True(File.Exists(log.Path));
    }

    [Fact]
    public void A_chosen_file_is_replaced()
    {
        var path = Path.Combine(_folder, "chosen.log");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "an older log that is much longer than the new one");

        var log = SessionLog.StartAt(path, Settings, _time);
        log.Write("new\r\n"u8);
        log.Dispose();

        Assert.Equal("new\r\n", File.ReadAllText(path));
    }

    [Fact]
    public void The_suggested_name_is_the_device_and_the_time()
    {
        Assert.Equal("a_b_c_-2026-11-01_12-34-56.log", SessionLog.FileName("a/b:c*", _time));
    }
}
