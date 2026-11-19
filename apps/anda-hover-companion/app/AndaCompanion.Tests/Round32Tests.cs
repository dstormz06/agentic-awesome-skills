using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 32: the Drugs@FDA reference file says when it was saved and how long ago, and is marked STALE after a number of days of its own
/// (45 unless the person changes it), by the owner's decision of Nov 19. The simulation wrote mockup/round32-golden.json
/// (tools/round32-golden.cjs); the app must give the same answers.
/// </summary>
[Collection("Global state")]
public class Round32Tests : IDisposable
{
    private readonly string _zoneBefore = Clock.ZoneId;
    private readonly Func<DateTime> _nowBefore = Clock.UtcNow;
    public void Dispose() { Clock.ZoneId = _zoneBefore; Clock.UtcNow = _nowBefore; }

    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private const long Day = 86400000;

    // A fixture in the layout Drugs@FDA is understood to use. It is NOT a real FDA file.
    private static string Zip(DateTime savedUtc)
    {
        string path = Path.Combine(Temp.NewDir(), "drugsatfda.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, string text) { var e = zip.CreateEntry(name); using var w = new StreamWriter(e.Open(), Encoding.Latin1); w.Write(text); }
            Put("Products.txt", "ApplNo\tProductNo\tForm\tStrength\tReferenceDrug\tDrugName\tActiveIngredient\tReferenceStandard\n021574\t001\tTABLET, EXTENDED RELEASE;ORAL\t500MG\t1\tFICTIONAL-ER\tMETFORMIN HYDROCHLORIDE\t0\n");
            Put("Submissions.txt", "ApplNo\tSubmissionClassCodeID\tSubmissionType\tSubmissionNo\tSubmissionStatus\tSubmissionStatusDate\n021574\t1\tSUPPL\t11\tAP\t2025-09-17 00:00:00\n");
            Put("SubmissionClass_Lookup.txt", "SubmissionClassCodeID\tSubmissionClassCode\tSubmissionClassCodeDescription\n1\tLABELING\tLabeling\n");
        }
        File.SetLastWriteTimeUtc(path, savedUtc);
        return path;
    }
    private static AndaRecord Rec() => new Data.MockAndaRepository().Find("216482")! with { Strength = "500 MG" };

    [Fact]
    public void When_a_reference_file_was_saved_and_whether_it_is_stale_are_the_simulations_answers()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round32-golden.json"))); var root = doc.RootElement; var bad = new List<string>(); int n = 0;
        Assert.Equal(JsonSerializer.Serialize(new { c = root.GetProperty("refChoices"), d = root.GetProperty("refDefault") }), JsonSerializer.Serialize(new { c = Clock.RefStaleChoices, d = Clock.RefStaleDefault }));
        foreach (var c in root.GetProperty("ref").EnumerateArray())
        {
            n++;
            long? saved = c.GetProperty("saved").ValueKind switch { JsonValueKind.Null => null, JsonValueKind.String => long.MaxValue, _ => c.GetProperty("saved").GetInt64() };
            var (text, stale) = Clock.RefSaved(saved, c.GetProperty("now").GetInt64(), c.GetProperty("days").GetInt32(), c.GetProperty("zone").GetString());
            string want = c.GetProperty("result").GetProperty("text").GetString() + "|" + c.GetProperty("result").GetProperty("stale").GetBoolean();
            if (want != text + "|" + stale) bad.Add($"case {n}: expected {want}, actual {text}|{stale}");
        }
        Assert.True(n >= 98, "cases are missing");
        Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(8)));
    }

    [Fact]
    public void The_cards_reference_line_says_how_old_the_file_is_and_STALE_after_its_own_days_not_the_days_for_my_data()
    {
        var saved = new DateTime(2026, 11, 1, 14, 30, 0, DateTimeKind.Utc); long t0 = Clock.Ms(saved);
        var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var f = DrugsAtFdaFile.TryLoad(Zip(saved), out _)!;
        Assert.Equal(saved, f.SavedUtc);

        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (18 days ago)", false), (f.Describe(Rec(), new ReferenceTime(t0 + 18 * Day, 45, ny)).RldText, f.Describe(Rec(), new ReferenceTime(t0 + 18 * Day, 45, ny)).RldStale));
        var at45 = f.Describe(Rec(), new ReferenceTime(t0 + 45 * Day, 45, ny));                                  // exactly 45 days is not older than 45 days
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (45 days ago)", false), (at45.RldText, at45.RldStale));
        var past = f.Describe(Rec(), new ReferenceTime(t0 + 45 * Day + 1, 45, ny));
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (45 days ago) · STALE: older than 45 days", true), (past.RldText, past.RldStale));
        var two = f.Describe(Rec(), new ReferenceTime(t0 + 18 * Day, 14, ny));
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (18 days ago) · STALE: older than 14 days", true), (two.RldText, two.RldStale));
        Assert.EndsWith("file saved Nov 1, 2026 (18 days ago)", f.Describe(Rec(), new ReferenceTime(t0 + 18 * Day, 5, ny)).RldText);       // a number that is not a choice is read as 45

        // without a time the line is the day alone, as before (what the fixture shared with the extension holds)
        Assert.StartsWith("Sep 17, 2025 · Drugs@FDA, file saved ", f.Describe(Rec()).RldText); Assert.DoesNotContain("ago", f.Describe(Rec()).RldText); Assert.False(f.Describe(Rec()).RldStale);

        // an ANDA the file has no reference drug for: nothing is called stale while the file is fresh (an old file is covered in a test of its own)
        var none = f.Describe(Rec() with { ActiveIngredient = "SOMETHING ELSE ENTIRELY" }, new ReferenceTime(t0 + 18 * Day, 45, ny));
        Assert.StartsWith("not available:", none.RldText); Assert.False(none.RldStale);

        // the resolver passes it on, for the app's card and for the browser host alike
        var viaResolver = ReferenceResolver.For(Rec(), f, "", "x.zip", loading: false, new ReferenceTime(t0 + 60 * Day, 45, ny));
        Assert.Equal((past.RldText.Replace("(45 days ago)", "(60 days ago)"), true), (viaResolver.RldText, viaResolver.RldStale));
        Assert.False(ReferenceResolver.For(Rec(), null, "", null, loading: false, new ReferenceTime(t0, 45, ny)).RldStale);

        // the browser host follows the person's days and zone
        Clock.UtcNow = () => saved.AddDays(50); Clock.ZoneId = "Pacific/Honolulu";
        string zip = Zip(saved);
        var host = NativeHostContext.Create(new Settings { ReferenceDataPath = zip, RefStaleAfterDays = 60, TimeZone = "Pacific/Honolulu" });
        Assert.Equal("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (50 days ago)", host.Reference!(Rec())!.RldText);
        host = NativeHostContext.Create(new Settings { ReferenceDataPath = zip, RefStaleAfterDays = 45, TimeZone = "Pacific/Honolulu" });
        Assert.Equal(("Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (50 days ago) · STALE: older than 45 days", true), (host.Reference!(Rec())!.RldText, host.Reference!(Rec())!.RldStale));
        Assert.Equal("A Drugs@FDA file older than 45 days is now marked STALE.", Clock.RefStaleSaid(45));
    }

    [Fact]
    public void Settings_written_before_round_32_gain_the_reference_days_and_a_backup_carries_them()      // round 33 moved the form on to 5; what round 32 added is still checked here
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":3,\"Tone\":\"Warm\",\"TimeZone\":\"America/Denver\",\"StaleAfterDays\":14}");      // written by round 31
        var s = SettingsService.Load(path);
        Assert.Equal((Settings.CurrentSchema, 45, "America/Denver", 14, "Warm"), (s.SchemaVersion, s.RefStaleAfterDays, s.TimeZone, s.StaleAfterDays, s.Tone));
        Assert.True(Settings.CurrentSchema >= 4); Assert.Null(SettingsService.LoadNote);
        SettingsService.Save(s, path); Assert.Contains($"\"SchemaVersion\": {Settings.CurrentSchema}", File.ReadAllText(path));

        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Tone\":\"Warm\",\"Provider\":3}");                        // three versions back: each step is taken
        s = SettingsService.Load(path);
        Assert.Equal((Settings.CurrentSchema, 45, "", 7, 30, 30), (s.SchemaVersion, s.RefStaleAfterDays, s.TimeZone, s.StaleAfterDays, s.BackupCopies, s.ArchiveAfterDays));

        File.WriteAllText(path, "{\"SchemaVersion\":4,\"RefStaleAfterDays\":90}"); Assert.Equal(90, SettingsService.Load(path).RefStaleAfterDays);
        File.WriteAllText(path, "{\"SchemaVersion\":4,\"RefStaleAfterDays\":7}"); Assert.Equal(45, SettingsService.Load(path).RefStaleAfterDays);          // 7 is a choice for my data, not for the reference file
        File.WriteAllText(path, "{\"SchemaVersion\":4,\"RefStaleAfterDays\":\"never\",\"Tone\":\"Warm\"}");
        s = SettingsService.Load(path); Assert.Equal((45, "Warm"), (s.RefStaleAfterDays, s.Tone));

        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        string backup = SettingsBackup.Export(new Settings { RefStaleAfterDays = 90 }, lenses, replies, new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc));
        var other = new Settings();
        Assert.True(SettingsBackup.Import(backup, other, lenses, replies).Ok); Assert.Equal(90, other.RefStaleAfterDays);
        SettingsBackup.Reset(other, lenses, replies); Assert.Equal(45, other.RefStaleAfterDays);
    }

    // ---------------------------------------------------------------- first steps

    [Fact]
    public void The_five_steps_of_the_guide_and_how_each_is_shown_are_the_simulations()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round32-golden.json"))); var root = doc.RootElement; var bad = new List<string>(); int n = 0;
        string Shape(GuideView g) => $"{g.Index}|{g.Place}|{g.Title}|{string.Join(" / ", g.Lines)}|{g.Action ?? "(none)"}|{g.Back}|{g.Next}|{g.Leave}";
        Assert.Equal(5, Guide.Steps.Count);                                                                       // the owner's rule: at most five
        Assert.Equal(JsonSerializer.Serialize(root.GetProperty("guideSteps").EnumerateArray().Select(s => new { t = s.GetProperty("title").GetString(), a = s.GetProperty("action").GetString(), l = s.GetProperty("lines").EnumerateArray().Select(x => x.GetString()).ToArray() })),
            JsonSerializer.Serialize(Guide.Steps.Select(s => new { t = s.Title, a = s.Action, l = s.Lines.ToArray() })));
        foreach (var c in root.GetProperty("guide").EnumerateArray())
        {
            n++; var at = c.GetProperty("i");
            double? i = at.ValueKind switch { JsonValueKind.Number => at.GetDouble(), JsonValueKind.String => double.TryParse(at.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : double.NaN, _ => null };
            var r = c.GetProperty("result");
            string want = $"{r.GetProperty("index").GetInt32()}|{r.GetProperty("place").GetString()}|{r.GetProperty("title").GetString()}|{string.Join(" / ", r.GetProperty("lines").EnumerateArray().Select(x => x.GetString()))}|{r.GetProperty("action").GetString() ?? "(none)"}|{r.GetProperty("back").GetBoolean()}|{r.GetProperty("next").GetString()}|{r.GetProperty("leave").GetString()}";
            string have = Shape(Guide.Step(i, c.GetProperty("shortcut").GetString(), c.GetProperty("on").GetString()));
            if (want != have) bad.Add($"view {n}:\n  expected {want}\n  actual   {have}");
        }
        Assert.True(n >= 20, "cases are missing");
        foreach (var c in root.GetProperty("opensAtStart").EnumerateArray())
        {
            bool? seen = c.GetProperty("seen").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };      // anything but a plain "yes" means it has not been shown
            if (c.GetProperty("opens").GetBoolean() != Guide.OpensAtStart(seen)) bad.Add($"start with {c.GetProperty("seen")}");
        }
        Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(6)));

        // every button a step names is one the window knows; no line is left with a mark in it
        Assert.All(Guide.Steps, s => Assert.Contains(s.Action, new[] { null, Guide.ShowSample, Guide.OpenSettings, Guide.OpenConnections }));
        for (int k = 0; k < 5; k++) Assert.All(Guide.Step(k, "Ctrl+Alt+A", "Offline · Local only").Lines, l => Assert.DoesNotContain("{", l));
        Assert.Equal("Select an ANDA number in any window, then press {connections}. Or press the button below to open the card of the sample ANDA 216482.", Guide.Step(1, "{connections}", "x").Lines[0]);      // what is written in is not read for marks
    }

    [Fact]
    public void What_the_guide_says_and_how_it_names_the_shortcut_are_the_simulations()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round32-golden.json"))); var root = doc.RootElement;
        var said = root.GetProperty("said");
        Assert.Equal((said.GetProperty("sample").GetString(), said.GetProperty("sampleNoNotes").GetString(), said.GetProperty("settings").GetString(), said.GetProperty("connections").GetString(), said.GetProperty("notRemembered").GetString()),
            (Guide.SaidSample, Guide.SaidSampleNoNotes, Guide.SaidSettings, Guide.SaidConnections, Guide.SaidNotRemembered));
        Assert.Equal(5, said.EnumerateObject().Count());
        int n = 0;
        foreach (var c in root.GetProperty("shortcuts").EnumerateArray())
        {
            n++; bool? working = c.GetProperty("working").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
            string text = Guide.Shortcut(c.GetProperty("key").GetString(), working);
            Assert.Equal(c.GetProperty("text").GetString(), text);
            Assert.Equal(c.GetProperty("line").GetString(), Guide.Step(1, text, "").Lines[0]);
        }
        Assert.True(n >= 6, "cases are missing");
    }

    [Fact]
    public void At_the_first_start_the_guide_opens_once_and_that_it_was_shown_is_saved_before_it_opens()
    {
        var order = new List<string>(); var s = new Settings();
        bool Save(Settings x) { order.Add("saved, done=" + x.FirstRunDone); return true; }
        void Open(string? note) => order.Add("opened, note=" + (note ?? "(none)"));
        Assert.True(FirstStart.Run(s, Save, Open, diagnosticRun: false));
        Assert.Equal(new[] { "saved, done=True", "opened, note=(none)" }, order);                                // saved first: a crash while it opens cannot bring it up again
        Assert.False(FirstStart.Run(s, Save, Open, diagnosticRun: false)); Assert.Equal(2, order.Count);          // the next start: not again

        order.Clear(); Assert.False(FirstStart.Run(new Settings(), Save, Open, diagnosticRun: true)); Assert.Empty(order);      // a diagnostic run never opens it, and marks nothing
        order.Clear(); var locked = new Settings();
        Assert.True(FirstStart.Run(locked, _ => false, Open, diagnosticRun: false));                             // a PC that does not let the settings be saved
        Assert.Equal(new[] { "opened, note=" + Guide.SaidNotRemembered }, order);
        order.Clear(); Assert.True(FirstStart.Run(new Settings(), _ => throw new UnauthorizedAccessException(), Open, diagnosticRun: false));
        Assert.Equal(new[] { "opened, note=" + Guide.SaidNotRemembered }, order);

        // the real save says whether it worked
        string dir = Temp.NewDir(), blocked = Path.Combine(dir, "a-file"); File.WriteAllText(blocked, "x");
        Assert.True(SettingsService.Save(new Settings(), Path.Combine(dir, "settings.json")));
        Assert.False(SettingsService.Save(new Settings(), Path.Combine(blocked, "settings.json")));              // a folder cannot be made where a file stands

        // the app uses this rule, offers the guide in the tray menu, and passes the reference file's time to its cards and to Settings
        string app = File.ReadAllText(Folder("app", "AndaCompanion", "App.axaml.cs")), ask = File.ReadAllText(Folder("app", "AndaCompanion", "App.Ask.cs"));
        Assert.Contains("FirstStart.Run(_settings, s => SettingsService.Save(s)", app);
        Assert.DoesNotContain("FirstRunDone", app);                                                               // nowhere else decides it
        Assert.Matches(@"new NativeMenuItem\(""Help: first steps…""\);\s+help\.Click \+= \(_, _\) => OpenFirstSteps\(null\);", app);
        Assert.Contains("FirstStepsWiring.Build(", ask); Assert.Contains("OpenFullCard(sample, at, mini: false, stay: true, notes: notes)", ask); Assert.Contains("_settingsWindow?.OpenSection(section)", ask);
        Assert.Matches(@"FirstStepsWiring\.Build\(\s*\(\) => _settings, _demo,", ask);                          // the bundled sample records, not the person's data
        Assert.Matches(@"_settingsWindow\?\.OpenSection\(section\) == true; \},\s+OpenConnections\), note\);", ask);      // the third button opens Connections, nothing else
        Assert.Contains("_drugsLoading, ReferenceResolver.TimeFor(_settings));", app);
        Assert.Contains("_drugs?.LoadedMessage(ReferenceResolver.TimeFor(_settings))", app);

        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        string backup = SettingsBackup.Export(new Settings { FirstRunDone = true }, lenses, replies, new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc));
        var fresh = new Settings();
        Assert.True(SettingsBackup.Import(backup, fresh, lenses, replies).Ok); Assert.False(fresh.FirstRunDone);      // a colleague's backup does not decide whether my PC has shown it
        var mine = new Settings { FirstRunDone = true };
        SettingsBackup.Reset(mine, lenses, replies); Assert.True(mine.FirstRunDone);                                 // and a reset does not bring it up again by itself
    }

    private sealed class OneRecord(AndaRecord? record) : Data.IAndaRepository
    {
        public AndaRecord? Find(string number) => record is not null && number == record.Number ? record : null;
        public IReadOnlyCollection<string> Numbers => record is null ? Array.Empty<string>() : new[] { record.Number };
    }

    [Fact]
    public void The_guides_buttons_open_the_bundled_sample_as_a_full_card_that_stays_Settings_at_the_files_and_Connections()
    {
        var calls = new List<string>(); var settings = new Settings { PeekHotkey = "Ctrl+Alt+A" };
        var statuses = new List<HotkeyStatus>(); bool own = false, sectionShown = true;
        var peek = new HotkeyBinding(HotkeyService.IdPeek, "Select & Peek", "Ctrl+Alt+A", 0, 0, "");
        FirstStepsHooks Build(Data.IAndaRepository sample) => FirstStepsWiring.Build(() => settings, sample, () => statuses, () => own,
            (r, notes) => calls.Add($"card {r.Number} demo={r.Demo} notes={notes}"), section => { calls.Add("settings " + section); return sectionShown; }, () => calls.Add("connections"));
        var hooks = Build(new Data.MockAndaRepository());

        Assert.Equal(Guide.SaidSample, hooks.ShowSample());
        own = true; Assert.Equal(Guide.SaidSampleNoNotes, hooks.ShowSample());                                    // my own data is loaded: a note on the sample would be kept under a number of mine
        Assert.Equal(Guide.SaidSettings, hooks.OpenSettings());
        sectionShown = false; Assert.Equal("Settings was opened, in front of this guide.", hooks.OpenSettings());      // it does not name a section it could not show
        Assert.Equal(Guide.SaidConnections, hooks.OpenConnections());
        Assert.Equal(new[] { "card 216482 demo=True notes=True", "card 216482 demo=True notes=False", "settings SecFiles", "settings SecFiles", "connections" }, calls);

        // a record that is not the bundled sample is never shown as "the sample"
        calls.Clear();
        var mine = new Data.MockAndaRepository().Find("216482")! with { Demo = false };
        Assert.Equal("The sample card could not be opened.", Build(new OneRecord(mine)).ShowSample());
        Assert.Equal("The sample card could not be opened.", Build(new OneRecord(null)).ShowSample());
        Assert.Empty(calls);

        // the shortcut: as chosen; not taught when Windows refused it; and what is switched on, asked each time
        Assert.Equal("Ctrl+Alt+A", hooks.Shortcut());                                                            // the keyboard service has not reported: not "refused"
        statuses.Add(new HotkeyStatus(peek, true, 0)); Assert.Equal("Ctrl+Alt+A", hooks.Shortcut());
        statuses[0] = new HotkeyStatus(peek, false, 1409);
        Assert.Equal("your shortcut (Ctrl+Alt+A is used by another program at the moment: please choose another in Settings)", hooks.Shortcut());
        settings.PeekHotkey = "Ctrl+Shift+K"; statuses.Clear(); Assert.Equal("Ctrl+Shift+K", hooks.Shortcut());
        Assert.Equal(Connections.Offline, hooks.Connections());
        Assert.Equal((FirstStepsWiring.SampleNumber, FirstStepsWiring.FilesSection), ("216482", "SecFiles"));
    }

    [Fact]
    public void Settings_and_the_card_name_one_day_and_one_age_for_the_reference_file_and_an_old_file_is_said_to_be_old_where_no_drug_matched()
    {
        var saved = new DateTime(2026, 11, 2, 3, 0, 0, DateTimeKind.Utc); long t0 = Clock.Ms(saved);
        var f = DrugsAtFdaFile.TryLoad(Zip(saved), out string atLoad)!;
        var hawaii = new ReferenceTime(t0 + 18 * Day, 45, TimeZoneInfo.FindSystemTimeZoneById("Pacific/Honolulu"));
        Assert.Equal("Drugs@FDA file loaded (1 reference products, file saved Nov 1, 2026 (18 days ago)).", f.LoadedMessage(hawaii));      // Nov 1 in Hawaii, as on the card
        Assert.EndsWith("file saved Nov 1, 2026 (18 days ago)", f.Describe(Rec(), hawaii).RldText);
        Assert.Equal(f.LoadedMessage(), atLoad);
        var old = new ReferenceTime(t0 + 400 * Day, 45, TimeZoneInfo.Utc);
        Assert.Equal("Drugs@FDA file loaded (1 reference products, file saved Nov 2, 2026 (400 days ago) · STALE: older than 45 days).", f.LoadedMessage(old));      // Settings says STALE as well

        var none = f.Describe(Rec() with { ActiveIngredient = "SOMETHING ELSE ENTIRELY" }, old);
        Assert.StartsWith("not available: ", none.RldText);
        Assert.EndsWith(" · Drugs@FDA, file saved Nov 2, 2026 (400 days ago) · STALE: older than 45 days", none.RldText);      // a newer file might have the date
        Assert.True(none.RldStale);
        var fresh = f.Describe(Rec() with { ActiveIngredient = "SOMETHING ELSE ENTIRELY" }, hawaii);
        Assert.DoesNotContain("file saved", fresh.RldText); Assert.False(fresh.RldStale);                         // a fresh file with no match says only that there is none

        Clock.UtcNow = () => saved.AddDays(18); Clock.ZoneId = "UTC";
        var time = ReferenceResolver.TimeFor(new Settings { RefStaleAfterDays = 14 });
        Assert.Equal((t0 + 18 * Day, 14, "UTC"), (time.NowMs, time.StaleDays, Clock.IanaId(time.Zone)));
    }

    [Fact]
    public void A_backup_written_before_a_setting_existed_leaves_that_setting_as_it_is()
    {
        string dir = Temp.NewDir();
        var lenses = new PromptLibraryService(Path.Combine(dir, "p.json")); var replies = new QuickReplyService(Path.Combine(dir, "r.json"));
        string backup = SettingsBackup.Export(new Settings { Tone = "Warm", TimeZone = "UTC", StaleAfterDays = 30, RefStaleAfterDays = 14 }, lenses, replies, new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc));
        var node = System.Text.Json.Nodes.JsonNode.Parse(backup)!;
        foreach (string gone in new[] { "TimeZone", "StaleAfterDays", "RefStaleAfterDays" }) Assert.True(node["Settings"]!.AsObject().Remove(gone));      // as round 30 wrote it
        string older = node.ToJsonString();
        Assert.Equal(new[] { "RefStaleAfterDays", "StaleAfterDays", "TimeZone" }, SettingsBackup.Absent(older).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(SettingsBackup.Absent(backup));

        var mine = new Settings { Tone = "Formal", TimeZone = "America/Denver", StaleAfterDays = 14, RefStaleAfterDays = 90 };
        Assert.True(SettingsBackup.Import(older, mine, lenses, replies).Ok);
        Assert.Equal(("Warm", "America/Denver", 14, 90), (mine.Tone, mine.TimeZone, mine.StaleAfterDays, mine.RefStaleAfterDays));      // what the backup holds is taken; what it does not hold stays
        Assert.True(SettingsBackup.Import(backup, mine, lenses, replies).Ok);
        Assert.Equal(("UTC", 30, 14), (mine.TimeZone, mine.StaleAfterDays, mine.RefStaleAfterDays));              // a backup that holds them is taken as before
    }
}
