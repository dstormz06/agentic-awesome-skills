using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AndaCompanion.Models;
using AndaCompanion.Services;
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

        // an ANDA the file has no reference drug for is not called stale: there is no date to be stale
        var none = f.Describe(Rec() with { ActiveIngredient = "SOMETHING ELSE ENTIRELY" }, new ReferenceTime(t0 + 400 * Day, 45, ny));
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
    public void Settings_move_to_version_4_by_themselves_and_a_backup_carries_the_new_number()
    {
        string dir = Temp.NewDir(), path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":3,\"Tone\":\"Warm\",\"TimeZone\":\"America/Denver\",\"StaleAfterDays\":14}");      // written by round 31
        var s = SettingsService.Load(path);
        Assert.Equal((4, 45, "America/Denver", 14, "Warm"), (s.SchemaVersion, s.RefStaleAfterDays, s.TimeZone, s.StaleAfterDays, s.Tone));
        Assert.Equal(4, Settings.CurrentSchema); Assert.Null(SettingsService.LoadNote);
        SettingsService.Save(s, path); Assert.Contains("\"SchemaVersion\": 4", File.ReadAllText(path));

        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Tone\":\"Warm\",\"Provider\":3}");                        // three versions back: each step is taken
        s = SettingsService.Load(path);
        Assert.Equal((4, 45, "", 7, 30, 30), (s.SchemaVersion, s.RefStaleAfterDays, s.TimeZone, s.StaleAfterDays, s.BackupCopies, s.ArchiveAfterDays));

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
}
