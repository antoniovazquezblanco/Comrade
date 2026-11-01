using System.Globalization;
using System.Text;
using COMrade.Core.Settings;

namespace COMrade.Core.Logging;

/// <summary>
/// Writes what a terminal shows to a UTF-8 text file: by default without colour and cursor codes,
/// optionally with the time at the start of every line. Output is buffered and flushed at least
/// once a second, so little is lost if COMrade stops unexpectedly.
/// </summary>
/// <remarks>Thread-safe: received data arrives on a background thread.</remarks>
public sealed class SessionLog : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private readonly StreamWriter _writer;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly AnsiStripper? _stripper;
    private readonly bool _timestamps;
    private readonly TimeProvider _time;
    private readonly ITimer _flushTimer;
    private readonly StringBuilder _text = new();
    private char[] _chars = new char[1024];
    private bool _atLineStart = true;
    private bool _disposed;

    private SessionLog(string path, LogSettings settings, TimeProvider time, FileMode mode = FileMode.CreateNew)
    {
        Path = path;
        _timestamps = settings.Timestamps;
        _stripper = settings.KeepColourCodes ? null : new AnsiStripper();
        _time = time;

        // Others may read the file while it is written, e.g. to follow it in an editor.
        var file = new FileStream(path, mode, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _flushTimer = time.CreateTimer(_ => Flush(), null, FlushInterval, FlushInterval);
    }

    public string Path { get; }

    /// <summary>Bytes written to the file so far, flushed or not.</summary>
    public long Length
    {
        get
        {
            lock (_lock)
            {
                if (_disposed)
                    return new FileInfo(Path).Length;
                _writer.Flush();
                return _writer.BaseStream.Position;
            }
        }
    }

    /// <summary>
    /// Starts a log in the settings' folder, named after the device and the time, e.g.
    /// "Router console-2026-11-01_12-34-56.log"; never overwrites an existing file.
    /// </summary>
    public static SessionLog Start(string device, LogSettings settings, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var folder = settings.FolderOrDefault;
        Directory.CreateDirectory(folder);
        var name = System.IO.Path.GetFileNameWithoutExtension(FileName(device, time));

        for (var attempt = 1; ; attempt++)
        {
            var path = System.IO.Path.Combine(folder, attempt == 1 ? $"{name}.log" : $"{name} ({attempt}).log");
            if (File.Exists(path))
                continue;
            try
            {
                return new SessionLog(path, settings, time);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Created by someone else in the meantime: try the next name.
            }
        }
    }

    /// <summary>
    /// Starts a log in a file the user chose, replacing it if it exists: the Save dialog has
    /// already asked about that.
    /// </summary>
    public static SessionLog StartAt(string path, LogSettings settings, TimeProvider? time = null) =>
        new(path, settings, time ?? TimeProvider.System, FileMode.Create);

    /// <summary>The name a new log gets, e.g. "Router console-2026-11-01_12-34-56.log".</summary>
    public static string FileName(string device, TimeProvider? time = null) =>
        $"{SafeFileName(device)}-{(time ?? TimeProvider.System).GetLocalNow():yyyy-MM-dd_HH-mm-ss}.log";

    /// <summary>Logs what the terminal shows, as UTF-8 bytes.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            var count = _decoder.GetCharCount(data, flush: false);
            if (count > _chars.Length)
                _chars = new char[Math.Max(count, _chars.Length * 2)];
            var chars = _chars.AsSpan(0, _decoder.GetChars(data, _chars, flush: false));

            _text.Clear();
            if (_stripper is null)
                _text.Append(chars);
            else
                _stripper.Strip(chars, _text);

            foreach (var chunk in _text.GetChunks())
                WriteLines(chunk.Span);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (!_disposed)
                _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _flushTimer.Dispose();
            _writer.Dispose();
        }
    }

    // Writes text, starting each line with the time when timestamps are on.
    private void WriteLines(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            if (_timestamps && _atLineStart)
                _writer.Write(_time.GetLocalNow().ToString("[yyyy-MM-dd HH:mm:ss.fff] ", CultureInfo.InvariantCulture));

            var newLine = text.IndexOf('\n');
            var line = newLine < 0 ? text : text[..(newLine + 1)];
            _writer.Write(line);
            _atLineStart = newLine >= 0;
            text = text[line.Length..];
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]).Trim().TrimEnd('.');
        return safe.Length > 0 ? safe : "COMrade";
    }
}
