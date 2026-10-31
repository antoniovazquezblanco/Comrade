using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using COMrade.Core.Settings;
using COMrade.Core.Terminal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace COMrade.ViewModels;

/// <summary>
/// The find panel of a device tab: searches what its terminal has shown, and new output as it
/// arrives. Lines are numbered from the first output, so a match keeps its number as the oldest
/// lines are dropped (see <see cref="TerminalHistory.DroppedLines"/>).
/// </summary>
public sealed partial class FindViewModel : ObservableObject, IDisposable
{
    private readonly TerminalHistory _history;
    private readonly AppSettingsStore _settings;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _newOutput;
    private int _newOutputPending;
    private bool _highlight;

    // While a search or new output makes the list: the selection it gets is chosen by no one.
    private bool _listing;

    // The number of the last line searched, which may have grown since.
    private long _lastLine;

    public FindViewModel(TerminalHistory history, AppSettingsStore settings)
    {
        _history = history;
        _settings = settings;
        _highlight = settings.Current.HighlightMatches;
        settings.Changed += OnSettingsChanged;

        // New output is searched a few times a second at most, however often it arrives.
        _newOutput = _ui.CreateTimer();
        _newOutput.Interval = TimeSpan.FromMilliseconds(300);
        _newOutput.IsRepeating = false;
        _newOutput.Tick += (_, _) => SearchNewOutput();
        history.Written += OnWritten;
    }

    /// <summary>
    /// The output was searched anew, as the query or match case changed or the user searched again;
    /// raised after the matches are listed.
    /// </summary>
    public event EventHandler? Searched;

    /// <summary>
    /// The user chose the selected match, to scroll the terminal to: clicked it, or reached it with
    /// Enter, the arrows or the previous and next buttons. Searches and new output choose none.
    /// </summary>
    public event EventHandler? Chosen;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial FindResult? Selected { get; set; }

    public ObservableCollection<FindResult> Matches { get; } = [];

    public string Query
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                Search();
        }
    } = string.Empty;

    public bool MatchCase
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                Search();
        }
    }

    /// <summary>
    /// Whether the terminal shows the matches highlighted while the panel is open: on until turned
    /// off, which is remembered, for every tab.
    /// </summary>
    public bool Highlight
    {
        get => _highlight;
        set
        {
            if (SetProperty(ref _highlight, value))
                _settings.Update(settings => settings with { HighlightMatches = value });
        }
    }

    /// <summary>Why the terminal does not show the selected match, when it does not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    public bool HasNote => Note is not null;

    /// <summary>For example "3 of 12", "1000+ matches" or "No matches".</summary>
    public string CountText
    {
        get
        {
            if (string.IsNullOrEmpty(Query))
                return string.Empty;
            if (Matches.Count == 0)
                return "No matches";
            var total = Matches.Count >= OutputSearch.MaximumMatches ? $"{Matches.Count}+" : $"{Matches.Count}";
            return Selected is { } selected ? $"{Matches.IndexOf(selected) + 1} of {total}" : $"{total} matches";
        }
    }

    /// <summary>Opens the panel with the output as it is now.</summary>
    public void Open()
    {
        IsOpen = true;
        Refresh();
    }

    /// <summary>A match clicked in the list: chosen again if it was already selected.</summary>
    public void Choose(FindResult result)
    {
        if (result == Selected)
            Chosen?.Invoke(this, EventArgs.Empty);
        else
            Selected = result;
    }

    /// <summary>Where <paramref name="result"/> is in the output kept now; null when its line no longer is.</summary>
    public (IReadOnlyList<string> Lines, int Index)? Locate(FindResult result)
    {
        var (output, dropped) = _history.Snapshot();
        var lines = OutputSearch.Lines(output);
        var index = result.Line - 1 - dropped;
        return index >= 0 && index < lines.Count ? (lines, (int)index) : null;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _history.Written -= OnWritten;
        _newOutput.Stop();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Note = null;
    }

    /// <summary>Searches all the output again.</summary>
    [RelayCommand]
    private void Refresh() => Search();

    [RelayCommand]
    private void Next() => Move(1);

    [RelayCommand]
    private void Previous() => Move(-1);

    [RelayCommand]
    private void CopyLine()
    {
        if (Selected is null)
            return;
        var package = new DataPackage();
        package.SetText(Selected.Match.Text);
        try
        {
            Clipboard.SetContent(package);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another program holds the clipboard; the copy is lost, as in any app.
        }
    }

    private void Move(int step)
    {
        if (Matches.Count == 0)
            return;
        var index = Selected is { } selected ? Matches.IndexOf(selected) : -1;
        Choose(Matches[(index + step + Matches.Count) % Matches.Count]);
    }

    // Every tab's panel follows the choice made in any of them.
    private void OnSettingsChanged(object? sender, EventArgs e) =>
        SetProperty(ref _highlight, _settings.Current.HighlightMatches, nameof(Highlight));

    // The selected match stays selected while it is still found, as when matching case or
    // searching again; otherwise the last match is, as the most recent output is usually what is
    // looked for.
    private void Search()
    {
        var (output, dropped) = _history.Snapshot();
        var lines = OutputSearch.Lines(output);
        var selected = Selected?.Position;
        _listing = true;
        try
        {
            Matches.Clear();
            foreach (var match in OutputSearch.Find(lines, Query, MatchCase))
                Matches.Add(Result(match, dropped));
            Selected = Matches.FirstOrDefault(match => match.Position == selected) ?? Matches.LastOrDefault();
        }
        finally
        {
            _listing = false;
        }

        _lastLine = dropped + lines.Count;
        Note = null;
        OnPropertyChanged(nameof(CountText));
        Searched?.Invoke(this, EventArgs.Empty);
    }

    // Raised on the thread that wrote the output.
    private void OnWritten(object? sender, byte[] data)
    {
        if (Interlocked.Exchange(ref _newOutputPending, 1) == 0)
            _ui.TryEnqueue(() => _newOutput.Start());
    }

    partial void OnSelectedChanged(FindResult? value)
    {
        if (!_listing && value is not null)
            Chosen?.Invoke(this, EventArgs.Empty);
    }

    // The matches in new output join the list, and those on lines dropped since leave it. The last
    // line searched may have grown, so it is searched again. The selection stays as it was, and so
    // does the terminal, where the user is looking.
    private void SearchNewOutput()
    {
        Interlocked.Exchange(ref _newOutputPending, 0);
        if (!IsOpen || Query.Length == 0)
            return;

        var (output, dropped) = _history.Snapshot();
        var lines = OutputSearch.Lines(output);
        var from = Math.Max(dropped + 1, _lastLine);
        var selected = Selected?.Position;

        _listing = true;
        try
        {
            while (Matches.Count > 0 && Matches[0].Line <= dropped)
                Matches.RemoveAt(0);
            while (Matches.Count > 0 && Matches[^1].Line >= from)
                Matches.RemoveAt(Matches.Count - 1);
            foreach (var match in OutputSearch.Find(lines, Query, MatchCase, (int)(from - 1 - dropped)))
                Matches.Add(Result(match, dropped));
            while (Matches.Count > OutputSearch.MaximumMatches)
                Matches.RemoveAt(0);

            // A match taken out and found again on the line that grew is selected again.
            if (selected is not null)
                Selected = Matches.FirstOrDefault(match => match.Position == selected);
        }
        finally
        {
            _listing = false;
        }

        _lastLine = dropped + lines.Count;
        OnPropertyChanged(nameof(CountText));
    }

    private static FindResult Result(SearchMatch match, long dropped) => new(match with { Line = (int)(dropped + match.Line) });
}
