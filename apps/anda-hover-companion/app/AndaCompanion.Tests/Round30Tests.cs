using System.Text.Json;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>
/// Round 30: a copy of each list after every change, one press to put a copy back, a number of copies the person chooses; the archive of
/// finished to-do items, searched and put back from; the days before a finished item is archived. The simulation wrote
/// mockup/round30-golden.json (tools/round30-golden.cjs); the app must give the same answers.
/// </summary>
[Collection("Global state")]
public class Round30Tests
{
    private static string Folder(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found") }.Concat(parts).ToArray());
    }
    private static JsonDocument Golden() => JsonDocument.Parse(File.ReadAllText(Folder("mockup", "round30-golden.json")));
    private static void Fail(List<string> bad) => Assert.True(bad.Count == 0, bad.Count + " differences from the simulation:\n" + string.Join("\n", bad.Take(8)));
    private static void Same(List<string> bad, string what, string expected, string actual)
    {
        if (expected != actual) bad.Add($"{what}\n  expected: {JsonSerializer.Serialize(expected.Length > 500 ? expected[..500] : expected)}\n  actual:   {JsonSerializer.Serialize(actual.Length > 500 ? actual[..500] : actual)}");
    }
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static long Ms(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();

    /// <summary>A clock a test moves by hand.</summary>
    private sealed class Clock { public DateTime Now = new(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc); public DateTime Tick(double seconds = 60) => Now = Now.AddSeconds(seconds); }

    // ---------------------------------------------------------------- the rules, held to the simulation's answers

    [Fact]
    public void The_day_a_moment_falls_on_and_which_copies_stay_are_the_simulations_answers()
    {
        using var doc = Golden(); var bad = new List<string>(); var root = doc.RootElement; int n = 0;
        Same(bad, "choices", JsonSerializer.Serialize(new { k = root.GetProperty("keepChoices"), kd = root.GetProperty("keepDefault"), d = root.GetProperty("archiveDaysChoices"), dd = root.GetProperty("archiveDaysDefault") }),
            JsonSerializer.Serialize(new { k = Backups.KeepChoices, kd = Backups.KeepDefault, d = Backups.ArchiveDaysChoices, dd = Backups.ArchiveDaysDefault }));
        foreach (var c in root.GetProperty("days").EnumerateArray())
            Same(bad, $"day {++n}", c.GetProperty("day").GetString()!, Backups.LocalDay(c.GetProperty("ms").GetInt64(), c.GetProperty("tz").GetString()));
        Assert.True(n >= 20, "cases are missing"); n = 0;
        foreach (var c in root.GetProperty("plans").EnumerateArray())
        {
            n++;
            var (keep, remove) = Backups.Plan(c.GetProperty("stamps").EnumerateArray().Select(s => s.GetInt64()), c.GetProperty("keep").GetInt32(), c.GetProperty("tz").GetString());
            Same(bad, $"plan {n} keep", string.Join(",", c.GetProperty("result").GetProperty("keep").EnumerateArray().Select(s => s.GetInt64())), string.Join(",", keep));
            Same(bad, $"plan {n} remove", string.Join(",", c.GetProperty("result").GetProperty("remove").EnumerateArray().Select(s => s.GetInt64())), string.Join(",", remove));
        }
        Assert.True(n >= 12, "cases are missing"); n = 0;
        foreach (var c in root.GetProperty("restamps").EnumerateArray())
            Same(bad, $"restamp {++n}", string.Join(",", c.GetProperty("moved").EnumerateArray().Select(m => m[0].GetInt64() + ">" + m[1].GetInt64())),
                string.Join(",", Backups.Restamp(c.GetProperty("stamps").EnumerateArray().Select(t => t.GetInt64()), c.GetProperty("now").GetInt64()).Select(m => m.Old + ">" + m.New)));
        Assert.True(n >= 6, "cases are missing"); Fail(bad);
    }

    [Fact]
    public void The_archive_is_shown_and_searched_exactly_as_the_simulation_does()
    {
        using var doc = Golden(); var bad = new List<string>(); var root = doc.RootElement; int n = 0;
        var lines = root.GetProperty("archive").EnumerateArray().ToList();
        string Shape(IReadOnlyList<TodoArchive.Row> rows, int total, int found) =>
            string.Join("\n", rows.Select(r => $"{r.Id}|{r.Title}|{r.Number ?? "(none)"}|{r.ArchivedOn}|{r.Why}|{r.RestoredOn ?? "(not put back)"}")) + $"\ntotal {total}, found {found}";
        string Wanted(JsonElement e) =>
            string.Join("\n", e.GetProperty("rows").EnumerateArray().Select(r => $"{r.GetProperty("id").GetString()}|{r.GetProperty("title").GetString()}|{r.GetProperty("number").GetString() ?? "(none)"}|{r.GetProperty("archivedOn").GetString()}|{r.GetProperty("why").GetString()}|{r.GetProperty("restoredOn").GetString() ?? "(not put back)"}"))
            + $"\ntotal {e.GetProperty("total").GetInt32()}, found {e.GetProperty("found").GetInt32()}";
        foreach (var c in root.GetProperty("archiveRows").EnumerateArray())
        {
            n++;
            var (rows, total, found) = TodoArchive.Rows(lines, c.GetProperty("query").GetString(), c.GetProperty("max").GetInt32());
            Same(bad, $"view {n}", Wanted(c.GetProperty("result")), Shape(rows, total, found));
        }
        Assert.True(n >= 14, "cases are missing");
        var empty = TodoArchive.Rows(Array.Empty<JsonElement>(), "", 0);
        Same(bad, "empty", Wanted(root.GetProperty("emptyArchive")), Shape(empty.Rows, empty.Total, empty.Found));
        Fail(bad);
    }

    // ---------------------------------------------------------------- copies of a list

    private static (Backups Backups, Clock Clock, string Dir, string File, int[] Keep) Make(int keep = 30, Action? reload = null)
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "todo.json"); var clock = new Clock(); int[] k = { keep };
        var b = new Backups(Path.Combine(dir, "backups"), () => k[0], () => NewYork, () => clock.Now);
        b.Register(new("todo", "ANDA to-do list", file, "item", "items", TodoService.CountIn, reload ?? (() => { })));
        return (b, clock, dir, file, k);
    }

    [Fact]
    public void A_copy_is_kept_after_every_change_and_the_same_text_is_never_copied_twice()
    {
        var (b, clock, dir, file, _) = Make();
        Assert.False(b.Keep("todo")); Assert.Empty(b.Copies("todo"));                                         // no list yet: nothing to copy, nothing thrown
        File.WriteAllText(file, "{\"Items\":[]}");
        Assert.True(b.Keep("todo")); Assert.False(b.Keep("todo"));                                           // the same text is not copied again
        File.WriteAllText(file, "{\"Items\":[{\"Id\":\"a\",\"Title\":\"One\"}]}");
        Assert.True(b.Keep("todo"));                                                                         // the clock has not moved: the copy still gets its own name
        clock.Tick(); File.WriteAllText(file, "{\"Items\":[{\"Id\":\"a\",\"Title\":\"One\"},{\"Id\":\"b\",\"Title\":\"Two\"}]}"); b.Written(file);        // told by the write itself
        b.Written(Path.Combine(dir, "settings.json")); b.Written(Path.Combine(dir, "other.json"));          // a file that is not one of the lists is not copied
        var copies = b.Copies("todo");
        Assert.Equal(3, copies.Count);
        Assert.Equal(new[] { "2 items", "1 item", "0 items" }, copies.Select(c => b.Size(b.Lists[0], c)));    // newest first
        Assert.Equal(new[] { "todo.20261118-200100-000.json", "todo.20261118-200000-001.json", "todo.20261118-200000-000.json" }, copies.Select(c => Path.GetFileName(c.Path)));
        Assert.All(copies, c => Assert.Equal(DateTimeKind.Utc, c.Utc.Kind));
        clock.Now = clock.Now.AddHours(-5); File.WriteAllText(file, "{\"Items\":[]}");                       // a clock set back never writes over a copy
        Assert.True(b.Keep("todo")); Assert.Equal(4, b.Copies("todo").Count); Assert.Equal("0 items", b.Size(b.Lists[0], b.Copies("todo")[0]));
        // Files in the folder that are not copies are left alone and not listed.
        File.WriteAllText(Path.Combine(dir, "backups", "todo", "todo.not-a-stamp.json"), "{}"); File.WriteAllText(Path.Combine(dir, "backups", "todo", "notes.txt"), "x");
        Assert.Equal(4, b.Copies("todo").Count);
        // A list too large to be one of ours is not copied, and a copy that cannot be read says so.
        File.WriteAllText(b.Copies("todo")[0].Path, "{ not json"); Assert.Equal("cannot be read", b.Size(b.Lists[0], b.Copies("todo")[0]));
        Assert.Null(Backups.ItemsIn("[1,2")); Assert.Equal(2, Backups.ItemsIn("[1,2]")); Assert.Equal(3, Backups.ItemsIn("{\"Notes\":{\"216482\":[1,2],\"214990\":[3]}}", "Notes")); Assert.Null(Backups.ItemsIn("{}", "Items"));
    }

    [Fact]
    public void Only_the_number_of_copies_the_person_chose_stays_and_a_busy_day_cannot_push_out_the_days_before_it()
    {
        var (b, clock, dir, file, keep) = Make(keep: 10);
        clock.Now = new DateTime(2026, 11, 1, 17, 0, 0, DateTimeKind.Utc);
        for (int day = 0; day < 14; day++) { File.WriteAllText(file, $"{{\"Items\":[],\"day\":{day}}}"); Assert.True(b.Keep("todo")); clock.Tick(86400); }      // one change a day for two weeks
        Assert.Equal(10, b.Copies("todo").Count);                                                           // ten days, ten copies: the four oldest went
        for (int i = 0; i < 25; i++) { File.WriteAllText(file, $"{{\"Items\":[],\"burst\":{i}}}"); Assert.True(b.Keep("todo")); clock.Tick(60); }                // then a busy day
        var copies = b.Copies("todo");
        Assert.Equal(19, copies.Count);                                                                     // the newest ten, and the last copy of each of nine earlier days
        Assert.Equal(10, copies.Select(c => Backups.LocalDay((long)(c.Utc - DateTime.UnixEpoch).TotalMilliseconds, NewYork)).Distinct().Count());
        Assert.Equal("{\"Items\":[],\"burst\":24}", File.ReadAllText(file));                                // the list itself is never touched
        keep[0] = 60; Assert.Equal(0, b.Prune()); Assert.Equal(19, b.Copies("todo").Count);                 // a larger number removes nothing
        keep[0] = 7; Assert.Equal(0, b.Prune());                                                            // not one of the choices: read as 30
        keep[0] = 10; int changed = 0; b.Changed += () => changed++;
        for (int i = 0; i < 3; i++) { File.WriteAllText(file, $"{{\"Items\":[],\"more\":{i}}}"); b.Keep("todo"); clock.Tick(60); }
        Assert.Equal(19, b.Copies("todo").Count); Assert.Equal(3, changed);                                 // it stays at the number chosen, and an open window is told
    }

    [Fact]
    public void The_night_the_clocks_change_neither_splits_nor_merges_a_day_of_copies()
    {
        // Nov 1, 2026 in New York has 25 hours: from 04:00 UTC to 05:00 UTC the next day. Mar 8 has 23: from 05:00 UTC to 04:00 UTC the next day.
        Assert.Equal("2026-10-31", Backups.LocalDay(Ms("2026-11-01T03:59:59Z"), NewYork));
        Assert.Equal("2026-11-01", Backups.LocalDay(Ms("2026-11-01T04:00:00Z"), NewYork));
        Assert.Equal("2026-11-01", Backups.LocalDay(Ms("2026-11-01T05:30:00Z"), NewYork));                  // 1:30 for the first time
        Assert.Equal("2026-11-01", Backups.LocalDay(Ms("2026-11-01T06:30:00Z"), NewYork));                  // 1:30 for the second time
        Assert.Equal("2026-11-01", Backups.LocalDay(Ms("2026-11-02T04:59:59Z"), NewYork));
        Assert.Equal("2026-11-02", Backups.LocalDay(Ms("2026-11-02T05:00:00Z"), NewYork));
        Assert.Equal("2026-03-08", Backups.LocalDay(Ms("2026-03-08T05:00:00Z"), NewYork));
        Assert.Equal("2026-03-08", Backups.LocalDay(Ms("2026-03-09T03:59:59Z"), NewYork));
        Assert.Equal("2026-03-09", Backups.LocalDay(Ms("2026-03-09T04:00:00Z"), NewYork));
        Assert.Equal("2026-11-01", Backups.LocalDay(Ms("2026-11-01T03:59:59Z"), "No/SuchZone"));            // a zone that is not known is read as UTC
        // Ten copies today fill the newest ten. Of the three older ones, two fall on Nov 1 in New York although they are on two days in UTC:
        // only the later of them stays there, and both stay when the days are counted in UTC.
        long now = Ms("2026-11-18T20:00:00Z"); var burst = Enumerable.Range(0, 10).Select(i => now - i * 60000L).ToList();
        long late = Ms("2026-11-02T03:30:00Z"), early = Ms("2026-11-01T04:30:00Z"), before = Ms("2026-11-01T03:30:00Z");
        var ny = Backups.Plan(burst.Concat(new[] { late, early, before }), 10, NewYork);
        Assert.Equal(new[] { early }, ny.Remove); Assert.Contains(late, ny.Keep); Assert.Contains(before, ny.Keep);
        var utc = Backups.Plan(burst.Concat(new[] { late, early, before }), 10, TimeZoneInfo.Utc);
        Assert.Equal(new[] { before }, utc.Remove); Assert.Contains(late, utc.Keep); Assert.Contains(early, utc.Keep);
    }

    [Fact]
    public void One_press_puts_a_copy_back_and_that_can_be_undone()
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "todo.json"), log = Path.Combine(dir, "events.csv"); var clock = new Clock();
        var todo = new TodoService(file);
        var b = new Backups(Path.Combine(dir, "backups"), () => 30, () => NewYork, () => clock.Now);
        b.Register(new("todo", "ANDA to-do list", file, "item", "items", TodoService.CountIn, () => todo.Reload()));
        SafeFile.Written += b.Written;
        try
        {
            EventLog.Configure(log);
            Assert.True(todo.Add("Send the meeting minutes", "216482", null, null, "You", Today).Added); clock.Tick();
            Assert.True(todo.Add("Ask about the facility inspection", null, null, null, "You", Today).Added); clock.Tick();
            var copies = b.Copies("todo");
            Assert.Equal(new[] { "2 items", "1 item" }, copies.Select(c => b.Size(b.Lists[0], c)));           // each change made its own copy, told by the write itself
            int changed = 0; todo.Changed += () => changed++;

            var same = b.Restore("todo", copies[0].Utc);
            Assert.Equal((false, "ANDA to-do list: that copy is what is there now. Nothing was changed."), same); Assert.Equal(2, todo.Items.Count);

            var back = b.Restore("todo", copies[1].Utc);
            Assert.True(back.Ok);
            Assert.Equal("ANDA to-do list: restored to the copy of Nov 18, 2026, 8:00:00 PM UTC. What was there a moment ago is still among the copies, so this can be undone.", back.Message);
            Assert.Equal(new[] { "Send the meeting minutes" }, todo.Items.Select(i => i.Title)); Assert.Equal(1, changed);      // the running list follows at once
            var after = b.Copies("todo");
            Assert.Equal(new[] { "1 item", "2 items", "1 item" }, after.Select(c => b.Size(b.Lists[0], c)));   // the restored list is the newest copy; nothing was removed
            clock.Tick();
            Assert.True(b.Restore("todo", after[1].Utc).Ok);                                                 // undone by putting back the copy made a moment before
            Assert.Equal(new[] { "Send the meeting minutes", "Ask about the facility inspection" }, todo.Items.Select(i => i.Title));

            Assert.Equal((false, "That copy is no longer kept. Nothing was changed."), b.Restore("todo", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.Equal((false, "That copy is no longer kept. Nothing was changed."), b.Restore("no-such-list", after[1].Utc));
            var bad = b.Copies("todo").Last(); File.WriteAllText(bad.Path, "{ this is not a list");
            Assert.Equal((false, "That copy could not be read. Nothing was changed."), b.Restore("todo", bad.Utc)); Assert.Equal(2, todo.Items.Count);

            var events = EventLog.Read().Entries.Select(e => (e.Type, e.Detail)).ToList();
            Assert.Equal(new[] { ("file", "a list was restored from a backup"), ("file", "a list was restored from a backup") }, events);
            Assert.DoesNotContain("minutes", File.ReadAllText(log));
        }
        finally { SafeFile.Written -= b.Written; EventLog.Configure(null); }
    }

    // ---------------------------------------------------------------- the archive

    [Fact]
    public void A_finished_item_is_archived_after_the_days_the_person_chose_and_can_be_put_back_as_an_open_item()
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "todo.json");
        var todo = new TodoService(file);
        string id = todo.Add("Finished ten days ago", "216482", "a note", null, "You", Today).Item!.Id;
        Assert.True(todo.SetDone(id, true, Today.AddDays(-10)));
        todo.Prune(new HashSet<string>(), Today);
        Assert.Single(todo.Items); Assert.False(File.Exists(todo.ArchiveFile));                              // 30 days by default: it stays
        todo.ArchiveAfterDays = 7; todo.Prune(new HashSet<string>(), Today);
        Assert.Empty(todo.Items);
        var (rows, total, found) = TodoArchive.Rows(TodoArchive.Lines(file), "");
        Assert.Equal((1, 1), (total, found));
        Assert.Equal(new TodoArchive.Row(id, "Finished ten days ago", "216482", "2026-11-18", "finished more than 7 days ago", null), rows[0]);      // the app's own lines are read as the simulation's are
        Assert.Equal(1, TodoArchive.Rows(TodoArchive.Lines(file), "TEN DAYS").Found); Assert.Equal(0, TodoArchive.Rows(TodoArchive.Lines(file), "eleven").Found);

        int before = File.ReadAllLines(todo.ArchiveFile).Length;
        var put = todo.PutBack(id, Today);
        Assert.Equal((true, "“Finished ten days ago” is back on the ANDA to-do list, as an open item."), put);
        var item = Assert.Single(todo.Items);
        Assert.Equal((id, false, null, "216482", "a note"), (item.Id, item.Done, item.DoneOn, item.Number, item.Note));
        Assert.Equal(before + 1, File.ReadAllLines(todo.ArchiveFile).Length);                                // a line was added, none was removed
        Assert.Equal("2026-11-18", TodoArchive.Rows(TodoArchive.Lines(file), "").Rows[0].RestoredOn);
        Assert.Equal((false, "That item was already put back."), todo.PutBack(id, Today));
        Assert.Equal((false, "That item was already put back."), todo.PutBack("never-archived", Today));

        // Archived again and put back again: each time the newest line is the one that counts.
        todo.SetDone(id, true, Today); Assert.Equal(1, todo.ClearCompleted(Today)); Assert.True(todo.PutBack(id, Today).Ok); Assert.Single(todo.Items);
        Assert.Equal(new string?[] { "2026-11-18", "2026-11-18" }, TodoArchive.Rows(TodoArchive.Lines(file), "").Rows.Select(r => r.RestoredOn));

        // The archive is a file anyone can edit: what comes back from it is cleaned, and a line that is not the archive's is passed over.
        File.AppendAllLines(todo.ArchiveFile, new[]
        {
            "not json at all", "{\"archivedOn\":\"2026-11-18\",\"why\":\"x\",\"item\":{\"Id\":\"hand\",\"Title\":\"  A   title\\twith\\nbreaks  \",\"Number\":\"21648\",\"Note\":\"" + new string('n', 900) + "\",\"Due\":\"soon\",\"Done\":true}}",
            "{\"archivedOn\":\"2026-11-18\",\"why\":\"x\",\"item\":{\"Id\":\"blank\",\"Title\":\"   \"}}", new string('x', 20000),
        });
        Assert.True(todo.PutBack("hand", Today).Ok);
        var hand = todo.Items.Single(i => i.Id == "hand");
        Assert.Equal(("A title with breaks", null, 500, null, false), (hand.Title, hand.Number, hand.Note!.Length, hand.Due, hand.Done));
        Assert.Equal((false, "That archived item has no title, so it cannot be put back. Nothing was changed."), todo.PutBack("blank", Today));
        Assert.Equal((false, "That item is on the list already. Nothing was changed."), new Func<(bool, string)>(() => { File.AppendAllLines(todo.ArchiveFile, new[] { "{\"archivedOn\":\"2026-11-18\",\"why\":\"x\",\"item\":{\"Id\":\"hand\",\"Title\":\"again\"}}" }); return todo.PutBack("hand", Today); })());
    }

    [Fact]
    public void My_to_do_list_follows_the_same_number_of_days_and_puts_an_item_back_the_same_way()
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "mytodo.json");
        var list = new MyTodoService(file);
        string id = list.Add("File the weekly report", TodoCategory.Other, null, true, Repeat.None, -1, "214990", "You", Today.AddDays(-10)).Item!.Id;
        Assert.True(list.SetDone(id, true, Today.AddDays(-10)));
        list.Refresh(Today); Assert.Single(list.Items);
        list.ArchiveAfterDays = 7; list.Refresh(Today); Assert.Empty(list.Items);
        var row = Assert.Single(TodoArchive.Rows(TodoArchive.Lines(file), "weekly").Rows);
        Assert.Equal((id, "File the weekly report", "214990", "finished more than 7 days ago", null), (row.Id, row.Title, row.Number, row.Why, row.RestoredOn));
        Assert.Equal((true, "“File the weekly report” is back on My to-do list, as an open item."), list.PutBack(id, Today));
        var item = Assert.Single(list.Items);
        Assert.Equal((id, false, null, true, "214990"), (item.Id, item.Done, item.DoneOn, item.High, item.Number));
        Assert.Equal((false, "That item was already put back."), list.PutBack(id, Today));
        Assert.Equal(2, File.ReadAllLines(list.ArchiveFile).Length);
    }

    // ---------------------------------------------------------------- settings, and the other three lists

    [Fact]
    public void Settings_from_before_round_30_move_forward_and_a_value_that_is_not_a_choice_becomes_the_usual_one()
    {
        string path = Path.Combine(Temp.NewDir(), "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Tone\":\"Warm\",\"Provider\":3}");                    // written by round 28 or 29
        var s = SettingsService.Load(path);
        Assert.Equal((Settings.CurrentSchema, 30, 30, "Warm"), (s.SchemaVersion, s.BackupCopies, s.ArchiveAfterDays, s.Tone));
        Assert.Null(SettingsService.LoadNote);                                                               // nothing the person set was changed, so nothing is said
        Assert.True(Settings.CurrentSchema >= 2);                                                            // round 31 moved it to 3; see Round31Tests
        s.BackupCopies = 100; s.ArchiveAfterDays = 365; SettingsService.Save(s, path);
        var again = SettingsService.Load(path);
        Assert.Equal((100, 365), (again.BackupCopies, again.ArchiveAfterDays));
        File.WriteAllText(path, "{\"SchemaVersion\":2,\"BackupCopies\":7,\"ArchiveAfterDays\":-1}");
        var odd = SettingsService.Load(path);
        Assert.Equal((30, 30), (odd.BackupCopies, odd.ArchiveAfterDays));
        File.WriteAllText(path, "{\"SchemaVersion\":2,\"BackupCopies\":\"many\",\"ArchiveAfterDays\":60,\"Tone\":\"Warm\"}");      // one value that cannot be read is repaired alone
        var typed = SettingsService.Load(path);
        Assert.Equal((30, 30, "Warm"), (typed.BackupCopies, typed.ArchiveAfterDays, typed.Tone));
        Assert.Equal((30, 30), (new Settings().BackupCopies, new Settings().ArchiveAfterDays));
    }

    [Fact]
    public void Every_list_tells_when_it_was_written_and_reads_its_file_again_after_a_restore()
    {
        string dir = Temp.NewDir(); var written = new List<string>(); void Note(string p) { lock (written) written.Add(Path.GetFileName(p)); }
        SafeFile.Written += Note;
        try
        {
            var todo = new TodoService(Path.Combine(dir, "todo.json")); var mine = new MyTodoService(Path.Combine(dir, "mytodo.json"));
            var notes = new QuickNotesService(Path.Combine(dir, "quick-notes.json")); var prompts = new PromptLibraryService(Path.Combine(dir, "prompts.json")); var replies = new QuickReplyService(Path.Combine(dir, "replies.json"));
            lock (written) written.Clear();
            todo.Add("One", null, null, null, "You", Today); mine.Add("Two", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Today);
            Assert.True(notes.Add("216482", "A note").Ok); prompts.Upsert(new LensPrompt("Mine", "Be brief.")); Assert.Null(replies.Upsert(new QuickReply("Mine", "Thank you.")));
            Assert.Equal(new[] { "todo.json", "mytodo.json", "quick-notes.json", "prompts.json", "replies.json" }, written);

            // Each file is replaced from outside, as a restore does it; each list then shows what the file holds.
            string earlier(string name) => File.ReadAllText(Path.Combine(dir, name));
            string notesText = earlier("quick-notes.json"), promptsText = earlier("prompts.json"), repliesText = earlier("replies.json");
            Assert.True(notes.Add("216482", "A second note").Ok); prompts.Upsert(new LensPrompt("Another", "Be thorough.")); replies.Upsert(new QuickReply("Another", "Noted."));
            File.WriteAllText(Path.Combine(dir, "quick-notes.json"), notesText); File.WriteAllText(Path.Combine(dir, "prompts.json"), promptsText); File.WriteAllText(Path.Combine(dir, "replies.json"), repliesText);
            notes.Reload(); prompts.Reload(); replies.Reload();
            Assert.Single(notes.For("216482")); Assert.DoesNotContain(prompts.Prompts, p => p.Name == "Another"); Assert.Contains(prompts.Prompts, p => p.Name == "Mine");
            Assert.DoesNotContain(replies.Replies, r => r.Name == "Another"); Assert.Contains(replies.Replies, r => r.Name == "Mine");
        }
        finally { SafeFile.Written -= Note; }
    }

    // ---------------------------------------------------------------- after the separate review

    [Fact]
    public void A_restore_is_refused_when_what_is_there_now_cannot_be_copied_first()
    {
        // The reviewer's two cases. First: the list grew past what the app reads, outside the app. Nothing may be put over it.
        var (b, clock, dir, file, _) = Make();
        File.WriteAllText(file, "{\"Items\":[{\"Id\":\"a\",\"Title\":\"One\"}]}"); Assert.True(b.Keep("todo")); clock.Tick();
        var copy = b.Copies("todo")[0];
        string big = "{\"Items\":[],\"pad\":\"" + new string('x', (int)SafeFile.MaxBytes + 10) + "\"}";
        File.WriteAllText(file, big);
        Assert.Equal((false, "What is there now could not be read, so it could not be copied first. Nothing was changed."), b.Restore("todo", copy.Utc));
        Assert.Equal(big.Length, new FileInfo(file).Length);

        // Second: a copy of what is there now cannot be written (the place its file would take is not free). Again nothing is put over it.
        File.WriteAllText(file, "{\"Items\":[{\"Id\":\"a\",\"Title\":\"One\"},{\"Id\":\"b\",\"Title\":\"Two\"}]}");
        Directory.CreateDirectory(Path.Combine(dir, "backups", "todo", "todo." + clock.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
        Assert.False(b.Keep("todo"));
        Assert.Equal((false, "What is there now could not be copied first, so nothing was changed."), b.Restore("todo", copy.Utc));
        Assert.Contains("\"Two\"", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(Path.Combine(dir, "backups", "todo"), "*.tmp"));                     // and a half-made copy is not left behind
        clock.Tick();
        var ok = b.Restore("todo", copy.Utc);                                                               // once it can be copied, it is, and the restore goes ahead
        Assert.True(ok.Ok); Assert.DoesNotContain("\"Two\"", File.ReadAllText(file));
        Assert.Contains(b.Copies("todo"), c => b.Read(c)!.Contains("\"Two\""));                              // what was there is among the copies: it can be undone
    }

    [Fact]
    public void A_copy_the_list_could_not_read_is_never_put_in_its_place()
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "todo.json"); var clock = new Clock();
        var todo = new TodoService(file);
        var b = new Backups(Path.Combine(dir, "backups"), () => 30, () => NewYork, () => clock.Now);
        b.Register(new("todo", "ANDA to-do list", file, "item", "items", TodoService.CountIn, () => todo.Reload()));
        todo.Add("One", null, null, null, "You", Today); b.Keep("todo"); clock.Tick();
        todo.Add("Two", null, null, null, "You", Today); b.Keep("todo"); clock.Tick();
        var old = b.Copies("todo")[1];
        foreach (string damaged in new[] { "{\"Items\":[1,2,3]}", "{\"Items\":\"none\"}", "[]", "{}", "null", "", "{\"Items\":[{\"Id\":" })
        {
            File.WriteAllText(old.Path, damaged);
            Assert.Equal("cannot be read", b.Size(b.Lists[0], old));                                        // the reviewer's first example was shown as "3 items"
            Assert.Equal((false, "That copy could not be read. Nothing was changed."), b.Restore("todo", old.Utc));
            Assert.Equal(2, todo.Items.Count); Assert.True(File.Exists(file));
        }
        Assert.Empty(Directory.GetFiles(dir, "todo.unreadable-*"));                                          // the list's file was never set aside by a restore
        // The readers of the other four lists say the same of a damaged copy, and count a good one.
        Assert.Null(MyTodoService.CountIn("{\"Items\":[1]}")); Assert.Equal(0, MyTodoService.CountIn("{\"Items\":[]}"));
        Assert.Null(QuickNotesService.CountIn("[]")); Assert.Null(QuickNotesService.CountIn("{\"Notes\":[1]}")); Assert.Equal(1, QuickNotesService.CountIn("{\"Notes\":{\"216482\":[{\"Id\":\"n1\",\"Text\":\"A note\"}],\"not-a-number\":[{\"Id\":\"n2\",\"Text\":\"x\"}]}}"));
        Assert.Null(PromptLibraryService.CountIn("{}")); Assert.Equal(1, PromptLibraryService.CountIn("[{\"Name\":\"Mine\",\"Text\":\"Be brief.\"},{\"Name\":\"\",\"Text\":\"x\"}]"));
        Assert.Null(QuickReplyService.CountIn("{\"a\":1}")); Assert.Equal(1, QuickReplyService.CountIn("[{\"Name\":\"Mine\",\"Text\":\"Thank you.\"}]"));

        // If the running list cannot take the copy after all, what was there is put back and the message says so.
        var c = new Backups(Path.Combine(dir, "other"), () => 30, () => NewYork, () => clock.Now); string other = Path.Combine(dir, "other.json");
        c.Register(new("todo", "ANDA to-do list", other, "item", "items", TodoService.CountIn, () => throw new InvalidOperationException("the list could not read it")));
        File.WriteAllText(other, "{\"Items\":[]}"); c.Keep("todo"); clock.Tick();
        File.WriteAllText(other, "{\"Items\":[{\"Id\":\"a\",\"Title\":\"One\"}]}"); c.Keep("todo"); clock.Tick();
        Assert.Equal((false, "That copy could not be used by the list. What was there is back in place."), c.Restore("todo", c.Copies("todo")[1].Utc));
        Assert.Contains("\"One\"", File.ReadAllText(other));
    }

    [Fact]
    public void Two_changes_in_one_millisecond_get_two_copies_and_a_clock_that_was_ahead_does_not_stay_the_newest()
    {
        var (b, clock, dir, file, keep) = Make(keep: 10);
        string folder = Path.Combine(dir, "backups", "todo");
        string List(int n) => "{\"Items\":[" + string.Join(",", Enumerable.Range(0, n).Select(i => $"{{\"Id\":\"i{i}\",\"Title\":\"Item {i}\"}}")) + "]}";
        // Within one millisecond: the names are in whole milliseconds, so the second must take the next one.
        for (int i = 0; i < 5; i++) { clock.Now = clock.Now.AddTicks(1500); File.WriteAllText(file, List(i)); Assert.True(b.Keep("todo")); }
        Assert.Equal(5, b.Copies("todo").Count); Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        Assert.Equal(File.ReadAllText(file), b.Read(b.Copies("todo")[0]));                                   // the newest copy is the list as it is

        // A copy from a clock that was a year ahead, and one from the end of time (the reviewer's file name): neither stays "the newest".
        File.WriteAllText(Path.Combine(folder, "todo.20271118-200000-000.json"), List(7)); File.WriteAllText(Path.Combine(folder, "todo.99991231-235959-999.json"), List(8));
        clock.Tick(); File.WriteAllText(file, List(9));
        Assert.True(b.Keep("todo"));
        var copies = b.Copies("todo");
        Assert.All(copies, c => Assert.True(c.Utc <= clock.Now));
        Assert.Equal(List(9), b.Read(copies[0])); Assert.Equal(List(8), b.Read(copies[1])); Assert.Equal(List(7), b.Read(copies[2]));      // kept, in their order, just before now
        // Then one change a day for 15 days with ten kept: one copy for each of ten days, which a copy from the future used to prevent.
        for (int d = 0; d < 15; d++) { clock.Tick(86400); File.WriteAllText(file, List(10 + d)); Assert.True(b.Keep("todo")); }
        Assert.Equal(10, b.Copies("todo").Select(c => Backups.LocalDay((long)(c.Utc - DateTime.UnixEpoch).TotalMilliseconds, NewYork)).Distinct().Count());
        Assert.Equal(0, b.WouldRemove(10)); Assert.Equal(0, b.WouldRemove(100)); keep[0] = 30;
        Assert.Equal(0, b.WouldRemove(30)); Assert.Equal(b.Copies("todo").Count, 10);                       // asking how many would go removes nothing
    }

    [Fact]
    public void Putting_an_item_back_says_it_worked_only_when_the_list_was_saved()
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "todo.json");
        var todo = new TodoService(file);
        string id = todo.Add("Send the meeting minutes", null, null, null, "You", Today).Item!.Id;
        todo.SetDone(id, true, Today); Assert.Equal(1, todo.ClearCompleted(Today));
        int lines = File.ReadAllLines(todo.ArchiveFile).Length;
        Directory.CreateDirectory(file + ".tmp");                                                          // the list cannot be written while this is in the way
        Assert.Equal((false, "The list could not be saved just now. Nothing was changed."), todo.PutBack(id, Today));
        Assert.Empty(todo.Items); Assert.Equal(lines, File.ReadAllLines(todo.ArchiveFile).Length);          // not on the list, and the archive does not say it was put back
        Assert.Empty(new TodoService(file).Items);
        Directory.Delete(file + ".tmp");
        Assert.True(todo.PutBack(id, Today).Ok);                                                            // so it can still be put back once the list can be saved
        Assert.Single(new TodoService(file).Items);

        string mineFile = Path.Combine(dir, "mytodo.json"); var mine = new MyTodoService(mineFile);
        string mid = mine.Add("File the weekly report", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Today).Item!.Id;
        mine.SetDone(mid, true, Today); Assert.Equal(1, mine.ClearFinished(Today));
        Directory.CreateDirectory(mineFile + ".tmp");
        Assert.Equal((false, "The list could not be saved just now. Nothing was changed."), mine.PutBack(mid, Today));
        Assert.Empty(mine.Items); Assert.Single(File.ReadAllLines(mine.ArchiveFile));
        Directory.Delete(mineFile + ".tmp"); Assert.True(mine.PutBack(mid, Today).Ok);
    }

    [Fact]
    public void A_finished_item_is_never_out_of_sight_between_the_day_its_list_stops_showing_it_and_the_day_it_is_archived()
    {
        string dir = Temp.NewDir();
        var todo = new TodoService(Path.Combine(dir, "todo.json")) { ArchiveAfterDays = 365 };
        string id = todo.Add("Finished ten days ago", "216482", null, null, "You", Today.AddDays(-10)).Item!.Id; todo.SetDone(id, true, Today.AddDays(-10));
        string recent = todo.Add("Finished yesterday", null, null, null, "You", Today).Item!.Id; todo.SetDone(recent, true, Today.AddDays(-1));
        todo.Prune(new HashSet<string>(), Today);
        Assert.Equal(2, todo.Items.Count);                                                                   // both are on the list, neither is archived
        Assert.Equal(new[] { new WaitingItem(id, "Finished ten days ago", "216482", Today.AddDays(-10)) }, todo.Waiting(Today));      // the one the list no longer shows is found here
        Assert.True(todo.SetDone(id, false, Today)); Assert.Empty(todo.Waiting(Today));                     // "Open it again"

        var mine = new MyTodoService(Path.Combine(dir, "mytodo.json")) { ArchiveAfterDays = 365 };
        string mid = mine.Add("Filed the report", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Today.AddDays(-10)).Item!.Id; mine.SetDone(mid, true, Today.AddDays(-10));
        mine.Refresh(Today);
        var w = Assert.Single(mine.Waiting(Today)); Assert.Equal((mid, "Filed the report", Today.AddDays(-10)), (w.Id, w.Title, w.Finished));
        Assert.DoesNotContain(MyTodoBuilder.Build(mine.Items, Today, 0, null).Groups.SelectMany(g => g.Items), i => i.Id == mid);      // the list itself no longer shows it
    }

    [Fact]
    public void A_settings_backup_or_a_reset_cannot_lower_the_number_of_copies_kept()
    {
        string dir = Temp.NewDir();
        var prompts = new PromptLibraryService(Path.Combine(dir, "prompts.json")); var replies = new QuickReplyService(Path.Combine(dir, "replies.json"));
        var colleague = new Settings { BackupCopies = 10, ArchiveAfterDays = 7, Tone = "Warm" };
        string backup = SettingsBackup.Export(colleague, prompts, replies);
        Assert.DoesNotContain("\"BackupCopies\": 10", backup); Assert.DoesNotContain("\"ArchiveAfterDays\": 7", backup);      // the two choices belong to this PC, like the lists: a backup does not carry them
        Assert.Contains("\"Tone\": \"Warm\"", backup);
        var mine = new Settings { BackupCopies = 100, ArchiveAfterDays = 365 };
        Assert.True(SettingsBackup.Import(backup.Replace("\"Tone\"", "\"BackupCopies\": 10, \"ArchiveAfterDays\": 7, \"Tone\""), mine, prompts, replies).Ok);
        Assert.Equal((100, 365, "Warm"), (mine.BackupCopies, mine.ArchiveAfterDays, mine.Tone));             // even a backup that names them does not change them
        SettingsBackup.Reset(mine, prompts, replies);
        Assert.Equal((100, 365), (mine.BackupCopies, mine.ArchiveAfterDays));
    }

    [Fact]
    public void A_large_or_damaged_archive_is_read_in_bounded_pieces_and_put_backs_are_matched_in_one_pass()
    {
        string dir = Temp.NewDir(), file = Path.Combine(dir, "todo.json"), archive = TodoArchive.FileFor(file);
        using (var w = new StreamWriter(archive))
        {
            w.WriteLine("{\"archivedOn\":\"2026-11-01\",\"why\":\"x\",\"item\":{\"Id\":\"first\",\"Title\":\"Before the long line\"}}");
            w.Write(new string('x', 3_000_000)); w.WriteLine();                                             // one line far too long: passed over, never held whole
            w.WriteLine("{\"archivedOn\":\"2026-11-02\",\"why\":\"x\",\"item\":{\"Id\":\"second\",\"Title\":\"After the long line\"}}");
            for (int i = 0; i < 50_000; i++) { w.WriteLine($"{{\"archivedOn\":\"2026-11-03\",\"why\":\"x\",\"item\":{{\"Id\":\"n{i}\",\"Title\":\"Item {i}\"}}}}"); w.WriteLine($"{{\"restoredOn\":\"2026-11-04\",\"id\":\"n{i}\"}}"); }
            w.Write("{\"archivedOn\":\"2026-11-05\",\"why\":\"x\",\"item\":{\"Id\":\"last\",\"Title\":\"No line break at the end\"}}");
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (rows, total) = TodoArchive.Read(TodoArchive.Lines(file));
        Assert.Equal(50_003, total);
        Assert.Equal(new[] { "first", "second" }, rows.Take(2).Select(r => r.Id)); Assert.Equal("last", rows[^1].Id);
        Assert.Equal(50_000, rows.Count(r => r.RestoredOn == "2026-11-04"));
        Assert.Equal(new[] { "last", "second", "first" }, TodoArchive.Search(rows, "line").Select(r => r.Id));      // searching what was read does not read the file again
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "reading took " + clock.Elapsed);            // the reviewer measured a minute for this before the change
        Assert.Null(TodoArchive.Find<TodoItem>(file, "n49999")); Assert.Equal("last", TodoArchive.Find<TodoItem>(file, "last")!.Id);
        // An item archived twice with no put-back between is one row, by its newest line; a number must be text of exactly six digits.
        var twice = TodoArchive.Read(new[]
        {
            JsonSerializer.SerializeToElement(new { archivedOn = "2026-11-01", why = "a", item = new { id = "t", title = "Earlier line", number = "216482\n" } }),
            JsonSerializer.SerializeToElement(new { archivedOn = "2026-11-02", why = "b", item = new { id = "t", title = "Newest line", number = 216482 } }),
        });
        Assert.Equal(new TodoArchive.Row("t", "Newest line", null, "2026-11-02", "b", null), Assert.Single(twice.Rows));
    }
}
