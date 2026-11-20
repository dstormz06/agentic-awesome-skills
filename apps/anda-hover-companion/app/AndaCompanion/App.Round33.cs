using Avalonia;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.UI;

namespace AndaCompanion;

// Round 33: recent and kept ANDAs, One Bar, Today, and the card's details and spacing. The rules are in Services (Recents, OneBar, Today,
// CardDetails, Display); this file only joins them to the windows.
public partial class App
{
    private TodayWindow? _todayWindow;

    /// <summary>A record with what Today needs to know about it, by the same rules as the card: whether it is urgent, and its watch flags.</summary>
    private Today.Look LookAt(AndaRecord r) => new(r, UrgencyPolicy.IsUrgent(r, Today(), _calendar, _settings.UrgentLateDays),
        WatchRules.Evaluate(r, Today(), _calendar, new WatchSettings(_settings.HolidayLeadDays, _settings.StaleAfterDays, NowMs: Clock.Ms(Clock.UtcNow()))));

    /// <summary>An ANDA was looked up: it goes to the front of the recent ones, when the person lets the app remember them. Kept on this PC only.</summary>
    private void NoteRecent(string number)
    {
        var after = Recents.After(_settings.Recents, number, _settings.RememberRecents);
        if (after.SequenceEqual(_settings.Recents)) return;
        _settings.Recents = after;
        SettingsService.Save(_settings);
    }

    /// <summary>One press of "Keep in my list" / "Remove from my list" on a card.</summary>
    private string ToggleKept(string number)
    {
        var (pins, result) = Recents.PinAfter(_settings.Pins, number);
        if (result is "kept" or "removed") { _settings.Pins = pins; SettingsService.Save(_settings); }
        return result;
    }

    private IReadOnlyList<(string Id, bool Kept)> ListedForLookup() =>
        Recents.Listed(_settings.Pins, _settings.RememberRecents ? _settings.Recents : new List<string>(), x => _repository.Find(x) is not null).Select(x => (x, _settings.Pins.Contains(x))).ToList();

    /// <summary>What the lookup window offers now: the kept and recent numbers, and, when the person chose One Bar, Review and Ask.</summary>
    private LookupOptions LookupOptionsNow() => new(_settings.LookupStyle == "bar", ListedForLookup, _repository.Find, OpenDraft, text => OpenAsk(text));

    /// <summary>What a card needs for its details and for "Keep in my list". A sample card opened beside the person's own data offers no list.</summary>
    private void JoinCard(CardWindow card, bool own)
    {
        card.CardFields = () => _settings.CardFields;
        card.DataFileName = () => _portfolio?.FileName;
        if (own) { card.IsKept = number => _settings.Pins.Contains(number); card.ToggleKept = ToggleKept; }
        card.ApplySpacing(Display.Spacing(_settings.Density));
        DisplayScale.Apply(card, DisplayScale.Current);      // before it is shown, so that it is placed at the size it will have
    }

    private IEnumerable<AndaRecord> Records() => _repository.Numbers.Select(n => _repository.Find(n)).Where(r => r is not null).Select(r => r!);
    /// <summary>Whose data a look is of: the sample records, or the sheet at its path (as a fingerprint). Round 36 review.</summary>
    private string LookSource() => ReferenceEquals(_repository.Current, _demo) ? "demo" : "file:" + LastLook.Fingerprint((_settings.PortfolioPath ?? "").ToUpperInvariant());
    /// <summary>False while the person's sheet cannot be read (the sample records stand in for it): nothing is then compared or kept.</summary>
    private bool DataReadable() => _portfolio is null || _portfolio.Status.Ok;

    private void OpenToday()
    {
        if (_todayWindow is not null) { _todayWindow.RefreshWeek(); _todayWindow.Activate(); return; }
        // the records are read once when Today opens, for its groups, what moved and next week (round 36 review: it was read up to four times)
        var records = Records().ToList();
        _todayWindow = new TodayWindow(new TodayHooks(
            All: () => records.Select(LookAt).ToList(),
            Find: id => _repository.Find(id) is { } r ? LookAt(r) : null,
            Pins: () => _settings.Pins, Recents: () => _settings.RememberRecents ? _settings.Recents : new List<string>(),
            OpenCard: id =>
            {
                if (_repository.Find(id) is not { } r) return;
                var cursor = TextCaptureService.GetCursorPosition();
                OpenCard(r, new PixelPoint(cursor.X, cursor.Y), mini: false);
            }, ShowAll: OpenWatchList,                            // round 34: the watch list has no tray item; it opens from Today
            // round 36: what moved since the look kept when Today was last closed, kept again when it closes; and next week, from Thursday noon.
            // Round 37: with the switch off there is no group, nothing is read and nothing is kept.
            Looks: () =>
            {
                if (!_settings.SinceLastLook) return null;
                var now = LastLook.Of(records, Clock.UtcNow(), LookSource());
                var (compare, note, keep) = LastLook.Decide(LastLook.Load(AppPaths.LastLookFile), now, DataReadable());
                if (note is null && compare is not null && DateTime.TryParse(compare.At, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var at))
                    note = LastLook.SaidNote(Clock.Ms(DateTime.SpecifyKind(at, DateTimeKind.Utc)), Clock.Zone);
                return ((LastLook.Look? Kept, LastLook.Look Now, string? Note, bool Keep)?)(compare, now, note, keep);
            },
            KeepLook: look => LastLook.KeepIfOn(_settings, AppPaths.LastLookFile, look, Clock.UtcNow()),   // what was shown, kept when Today closed; not if switched off meanwhile
            Week: () => { var wall = Clock.Wall(Clock.UtcNow()); return NextWeek.Of(DateOnly.FromDateTime(wall), wall.Hour, records, _calendar, NextWeek.TasksOf(_todo.Items, _myTodo.Items)); }));
        _todayWindow.Closed += (_, _) => _todayWindow = null;
        _todayWindow.Show();
        RefreshTrayState();
    }
}
