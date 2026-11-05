using System.Text.Json;
using System.Text.Json.Nodes;
using AndaCompanion.Services;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 16: the note reader (messy text to suggested items) and the to-do list store with its smart view. All offline.</summary>
public class MyTodoTests
{
    private static readonly DateOnly Wed = new(2026, 11, 4);          // a Wednesday
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static MyTodoService NewStore(out string path) { path = Path.Combine(Temp.NewDir(), "mytodo.json"); return new MyTodoService(path); }
    private static MyTodoService NewStore() => NewStore(out _);

    // ---------- reading messy notes ----------

    [Fact]
    public void A_messy_paragraph_becomes_separate_items_with_their_dates_priority_and_category()
    {
        const string notes = "Don't forget to confirm the facility inspection date with the team tomorrow ASAP. Also email the team lead re: the Thursday huddle. " +
                             "Need to submit the IR response for ANDA 216482 by Friday. errands: book the dentist, pick up the dry cleaning";
        var items = NoteParser.Parse(notes, Wed);
        Assert.Equal(4, items.Count);

        Assert.Equal("Confirm the facility inspection date with the team tomorrow ASAP", items[0].Title);
        Assert.Equal((TodoCategory.Work, D(2026, 11, 5), true, true), (items[0].Category, items[0].Due, items[0].High, items[0].Actionable));

        Assert.Equal("Email the team lead re: the Thursday huddle", items[1].Title);                 // "Also" is not part of the task
        Assert.Equal((TodoCategory.Work, D(2026, 11, 5), false), (items[1].Category, items[1].Due, items[1].High));

        Assert.Equal("Submit the IR response for ANDA 216482 by Friday", items[2].Title);
        Assert.Equal((TodoCategory.Work, D(2026, 11, 6), "216482"), (items[2].Category, items[2].Due, items[2].Number));

        Assert.Equal("Errands: book the dentist, pick up the dry cleaning", items[3].Title);
        Assert.Equal(TodoCategory.Personal, items[3].Category);
        Assert.True(items[3].Actionable);                                                   // an errands line is a task
    }

    [Fact]
    public void Bullets_numbers_and_checkboxes_are_removed_and_each_line_is_one_item()
    {
        var items = NoteParser.Parse("- call the applicant\n* submit the timesheet\n1. email Pat\n2) book the conference room\n[ ] order the printer toner\n• update the tracking sheet", Wed);
        Assert.Equal(new[] { "Call the applicant", "Submit the timesheet", "Email Pat", "Book the conference room", "Order the printer toner", "Update the tracking sheet" }, items.Select(i => i.Title));
        Assert.All(items, i => Assert.True(i.Actionable));
    }

    [Theory]
    [InlineData("Call the applicant and email the team lead", 2)]                       // both halves start with an action
    [InlineData("Review the file and the attachments", 1)]                   // "the attachments" does not start with an action, so it stays one task
    [InlineData("Review and approve the memo", 1)]                           // a one-word half would be meaningless
    [InlineData("Send the draft then email the team lead", 2)]
    [InlineData("Call Dr. Smith about the labeling. Ask about the 3 p.m. meeting today", 2)]   // abbreviations and p.m. do not end a sentence
    [InlineData("Update the tracker, file the memo, and call the applicant tomorrow", 3)]
    [InlineData("Attach the memo, the cover letter, the table", 1)]           // a list of things is one item
    [InlineData("Call Sam, please", 1)]
    public void Tasks_are_split_only_where_it_is_safe(string text, int expected) => Assert.Equal(expected, NoteParser.Parse(text, Wed).Count);

    [Theory]
    [InlineData("by Friday", 2026, 11, 6)]
    [InlineData("next Friday", 2026, 11, 6)]                                 // the first Friday after today
    [InlineData("this Wednesday", 2026, 11, 4)]
    [InlineData("next Wednesday", 2026, 11, 11)]
    [InlineData("on Monday", 2026, 11, 9)]
    [InlineData("tomorrow", 2026, 11, 5)]
    [InlineData("tonight", 2026, 11, 4)]
    [InlineData("EOD", 2026, 11, 4)]
    [InlineData("in 3 days", 2026, 11, 7)]
    [InlineData("in 2 weeks", 2026, 11, 18)]
    [InlineData("next week", 2026, 11, 9)]                                   // the Monday after today
    [InlineData("end of week", 2026, 11, 6)]
    [InlineData("EOM", 2026, 11, 30)]
    [InlineData("next month", 2026, 12, 1)]
    [InlineData("11/12", 2026, 11, 12)]
    [InlineData("1/5", 2027, 1, 5)]                                          // long past this year, so next year
    [InlineData("11/2", 2026, 11, 2)]                                        // two days past: kept, it is overdue
    [InlineData("Nov 12, 2026", 2026, 11, 12)]
    [InlineData("12 Dec", 2026, 12, 12)]
    [InlineData("2026-12-01", 2026, 12, 1)]
    [InlineData("on the 15th", 2026, 11, 15)]
    [InlineData("this weekend", 2026, 11, 7)]
    [InlineData("over the weekend", 2026, 11, 7)]
    [InlineData("by fri", 2026, 11, 6)]
    public void Due_dates_are_read_from_the_words(string phrase, int y, int m, int d) =>
        Assert.Equal(D(y, m, d), NoteParser.ReadDue("Finish the report " + phrase, Wed));

    [Theory]
    [InlineData("Add 1/2 inch to the margin")]
    [InlineData("He sat down and may need 5 minutes")]
    [InlineData("We discussed Friday's meeting")]
    [InlineData("Plain words with no date")]
    [InlineData("Version 13/45 is wrong")]                                   // not a real month
    public void Words_that_only_look_like_dates_are_not_dates(string text) => Assert.Null(NoteParser.ReadDue(text, Wed));

    [Fact]
    public void Repeats_are_read_from_the_words_and_give_a_first_due_date()
    {
        var weekly = NoteParser.ParseOne("Send the status update to my supervisor every Tuesday", Wed)!;
        Assert.Equal((Repeat.Weekly, 2, D(2026, 11, 10), TodoCategory.Work), (weekly.Repeat, weekly.RepeatDay, weekly.Due, weekly.Category));
        var daily = NoteParser.ParseOne("Check my email daily", Wed)!;
        Assert.Equal((Repeat.Daily, Wed), (daily.Repeat, daily.Due));
        var weekdays = NoteParser.ParseOne("Review the incoming queue every weekday", Wed)!;
        Assert.Equal((Repeat.Weekdays, Wed), (weekdays.Repeat, weekdays.Due));
        var monthly = NoteParser.ParseOne("Submit the expense report on the 1st every month", Wed)!;
        Assert.Equal((Repeat.Monthly, 1, D(2026, 12, 1)), (monthly.Repeat, monthly.RepeatDay, monthly.Due));
        var plainWeekly = NoteParser.ParseOne("Submit my timesheet weekly", Wed)!;
        Assert.Equal((Repeat.Weekly, (int)DayOfWeek.Wednesday), (plainWeekly.Repeat, plainWeekly.RepeatDay));
        Assert.Equal(Repeat.None, NoteParser.ParseOne("Call Sam on Friday", Wed)!.Repeat);          // one day name is a date, not a repeat
    }

    [Theory]
    [InlineData("URGENT: call the applicant", true)]
    [InlineData("Send the memo asap", true)]
    [InlineData("This is important, call Sam", true)]
    [InlineData("Call Sam!!", true)]
    [InlineData("Call Sam when you can", false)]
    public void Priority_is_read_from_the_words(string text, bool high) => Assert.Equal(high, NoteParser.ParseOne(text, Wed)!.High);

    [Fact]
    public void Greetings_headers_and_links_are_dropped_but_a_task_after_a_greeting_is_kept()
    {
        Assert.Empty(NoteParser.Parse("Hi Sam,\nThanks,\nRegards\nFrom: Pat <pat@example.test>\nSubject: huddle\nhttps://example.test/a", Wed));
        var kept = NoteParser.Parse("Thanks, please send the report by Friday", Wed);
        Assert.Equal("Send the report by Friday", Assert.Single(kept).Title);
    }

    [Fact]
    public void A_sentence_that_reads_like_a_note_is_offered_but_not_ticked()
    {
        var note = Assert.Single(NoteParser.Parse("The inspection report was very clear and complete.", Wed));
        Assert.False(note.Actionable);
        Assert.True(Assert.Single(NoteParser.Parse("Call the applicant.", Wed)).Actionable);
    }

    [Fact]
    public void The_same_task_written_twice_is_offered_once_and_the_list_is_capped()
    {
        Assert.Single(NoteParser.Parse("Call Sam\ncall sam\nCALL SAM.", Wed));
        string many = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"- call person {i}"));
        Assert.Equal(NoteParser.MaxItems, NoteParser.Parse(many, Wed).Count);
    }

    [Fact]
    public void Odd_and_hostile_input_never_throws_never_hangs_and_stays_within_limits()
    {
        var rng = new Random(20261104);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string[] nasty =
        {
            "", "   ", "\n\n\n", "\0\u0007\u001b[31m", new string('a', 50_000), new string('!', 20_000), string.Concat(Enumerable.Repeat("1/2/3 ", 5000)),
            new string(' ', 10_000) + "x", string.Concat(Enumerable.Repeat("and ", 5000)), string.Concat(Enumerable.Repeat("call and call ", 3000)), "a." + new string(' ', 8000) + "B.",
            "😀😀😀 call 😀 tomorrow", "‮Call Sam‬", "Call Sam on 99/99/9999 and 0/0 and 2026-13-45",
        };
        foreach (var text in nasty)
        {
            var items = NoteParser.Parse(text, Wed);
            Assert.True(items.Count <= NoteParser.MaxItems);
            Assert.All(items, i => { Assert.InRange(i.Title.Length, 1, NoteParser.MaxTitleChars + 1); Assert.DoesNotContain('\n', i.Title); Assert.DoesNotContain('\0', i.Title); });
            NoteParser.ParseOne(text, Wed);
        }
        for (int n = 0; n < 400; n++)
        {
            var chars = new char[rng.Next(0, 400)];
            for (int i = 0; i < chars.Length; i++) chars[i] = "abc xyzCALL.,;:!?-/\n0123456789'\"()[]•tomorrow friday"[rng.Next(0, 47)];
            var items = NoteParser.Parse(new string(chars), Wed);
            Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Title)));
        }
        Assert.True(sw.ElapsedMilliseconds < 8000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Notes_longer_than_the_limit_are_cut_at_a_finished_sentence_never_leaving_a_fragment_as_an_item()
    {
        string text = string.Concat(Enumerable.Repeat("Submit the report. ", 400));                  // 7,600 characters: the limit falls inside "report"
        var item = Assert.Single(NoteParser.Parse(text, Wed));
        Assert.Equal("Submit the report", item.Title);

        string fitted = NoteParser.FitToLimit(text);
        Assert.True(fitted.Length <= NoteParser.MaxInputChars);
        Assert.EndsWith("Submit the report.", fitted);
        Assert.Equal("short", NoteParser.FitToLimit("short"));
        Assert.Equal(new string('x', NoteParser.MaxInputChars), NoteParser.FitToLimit(new string('x', NoteParser.MaxInputChars + 50)));     // nothing to cut back to within 300 characters: kept as it is

        // lines without full stops: the unfinished last line is dropped, a finished one is kept
        string lines = string.Join("\n", Enumerable.Range(0, 400).Select(i => "- email the team lead " + i));
        var fromLines = NoteParser.Parse(lines, Wed);
        Assert.DoesNotContain(fromLines, i => i.Title.EndsWith(" lead", StringComparison.Ordinal));
        Assert.Equal(1, NoteParser.Parse(lines, Wed).Count(i => i.Title == "Email the team lead 0"));
    }

    [Fact]
    public void A_very_long_sentence_is_shortened_at_a_word_with_an_ellipsis()
    {
        var item = Assert.Single(NoteParser.Parse("Call " + string.Join(" ", Enumerable.Repeat("everyone", 60)), Wed));
        Assert.True(item.Title.Length <= NoteParser.MaxTitleChars);
        Assert.EndsWith("…", item.Title);
    }

    // ---------- the store ----------

    [Fact]
    public void Items_are_added_once_edited_finished_counted_and_deleted()
    {
        var store = NewStore();
        var added = store.Add("  Call   the applicant ", TodoCategory.Work, Wed.AddDays(1), true, Repeat.None, -1, null, "Notes", Wed);
        Assert.True(added.Added);
        Assert.Equal("Call the applicant", added.Item!.Title);
        Assert.False(store.Add("call the applicant", TodoCategory.Work, null, false, Repeat.None, -1, null, "You", Wed).Added);          // the same open item is not added twice
        Assert.True(store.Add("call the applicant", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed).Added);          // another category is another item
        Assert.Equal("Please type what needs doing.", store.Add("   ", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed).Reason);

        Assert.Null(store.Edit(added.Item.Id, "Call the applicant about the information request", TodoCategory.Work, Wed, false, Repeat.None, -1));
        Assert.Equal("Call the applicant about the information request", store.Items.First(i => i.Id == added.Item.Id).Title);
        Assert.Equal("Please type what needs doing.", store.Edit(added.Item.Id, "  ", TodoCategory.Work, null, false, Repeat.None, -1));

        Assert.True(store.SetDone(added.Item.Id, true, Wed));
        Assert.Equal(1, store.FinishedInLast(7, Wed));
        Assert.True(store.SetDone(added.Item.Id, false, Wed));                                             // un-ticking takes the count back
        Assert.Equal(0, store.FinishedInLast(7, Wed));
        store.SetDone(added.Item.Id, true, Wed); store.SetDone(added.Item.Id, true, Wed);                  // ticking twice counts once
        Assert.Equal(1, store.FinishedInLast(7, Wed));
        Assert.Equal(0, store.FinishedInLast(7, Wed.AddDays(-1)));                                          // nothing finished yet on the day before
        Assert.Equal(1, store.FinishedInLast(7, Wed.AddDays(6)));
        Assert.Equal(0, store.FinishedInLast(7, Wed.AddDays(7)));

        Assert.Equal(1, store.ClearFinished());
        Assert.True(store.Delete(store.Items.Single().Id));
        Assert.False(store.Delete("nope"));
        Assert.Empty(store.Items);
    }

    [Fact]
    public void Items_that_repeat_come_back_by_themselves_and_a_missed_one_does_not_pile_up()
    {
        var store = NewStore();
        var daily = store.Add("Check my email", TodoCategory.Work, Wed, false, Repeat.Daily, -1, null, "You", Wed).Item!;
        var weekly = store.Add("Send the weekly summary", TodoCategory.Personal, D(2026, 11, 2), false, Repeat.Weekly, (int)DayOfWeek.Monday, null, "You", D(2026, 11, 2)).Item!;
        var friday = store.Add("Weekly report", TodoCategory.Work, D(2026, 11, 6), false, Repeat.Weekdays, -1, null, "You", Wed).Item!;
        var missed = store.Add("Update the tracking sheet", TodoCategory.Work, D(2026, 10, 30), false, Repeat.Daily, -1, null, "You", D(2026, 10, 30)).Item!;
        string Due(string id) => store.Items.Single(i => i.Id == id).Due!;
        bool Done(string id) => store.Items.Single(i => i.Id == id).Done;

        store.Refresh(Wed);                                                                                // a missed daily item moves to today, one copy only
        Assert.Equal("2026-11-04", Due(missed.Id));
        Assert.Equal(4, store.Items.Count);
        Assert.Equal("2026-11-09", Due(weekly.Id));                                                        // a weekly item missed on Monday moves to the next Monday

        store.SetDone(daily.Id, true, Wed);
        Assert.True(Done(daily.Id));
        Assert.False(store.Refresh(Wed));                                                                  // still the same day: it stays ticked
        Assert.True(Done(daily.Id));
        Assert.True(store.Refresh(Wed.AddDays(1)));                                                        // the next morning it is open again, due that day
        Assert.False(Done(daily.Id));
        Assert.Equal("2026-11-05", Due(daily.Id));

        store.SetDone(friday.Id, true, D(2026, 11, 6));                                                    // done on Friday: next is Monday, not Saturday
        store.Refresh(D(2026, 11, 7));
        Assert.Equal("2026-11-09", Due(friday.Id));

        store.SetDone(friday.Id, true, D(2026, 11, 9));
        Assert.Equal(0, store.ClearFinished());                                                            // repeating items are never cleared as "finished"
        Assert.Equal(4, store.Items.Count);
    }

    [Fact]
    public void A_monthly_item_comes_back_on_its_day_of_the_month_and_short_months_use_their_last_day()
    {
        var store = NewStore();
        var rent = store.Add("Submit the expense report", TodoCategory.Work, D(2026, 11, 1), false, Repeat.Monthly, 1, null, "You", D(2026, 11, 1)).Item!;
        store.SetDone(rent.Id, true, D(2026, 11, 1));
        store.Refresh(D(2026, 11, 2));
        Assert.Equal("2026-12-01", store.Items.Single().Due);

        var late = store.Add("Month end report", TodoCategory.Work, D(2026, 1, 31), false, Repeat.Monthly, 31, null, "You", D(2026, 1, 31)).Item!;
        store.SetDone(late.Id, true, D(2026, 1, 31));
        store.Refresh(D(2026, 2, 1));
        Assert.Equal("2026-02-28", store.Items.Single(i => i.Id == late.Id).Due);                          // February has no 31st
    }

    [Fact]
    public void An_item_finished_early_does_not_come_back_on_the_day_it_was_due()
    {
        var store = NewStore();
        var weekly = store.Add("Team update", TodoCategory.Work, D(2026, 11, 9), false, Repeat.Weekly, (int)DayOfWeek.Monday, null, "You", Wed).Item!;
        store.SetDone(weekly.Id, true, Wed);                                                                // done on Wednesday, due next Monday
        store.Refresh(D(2026, 11, 5));
        Assert.Equal("2026-11-16", store.Items.Single().Due);
    }

    [Fact]
    public void Overdue_items_that_do_not_repeat_can_be_moved_to_today_together_and_old_finished_ones_are_forgotten()
    {
        var store = NewStore();
        store.Add("Old one", TodoCategory.Other, Wed.AddDays(-3), false, Repeat.None, -1, null, "You", Wed);
        store.Add("Not yet", TodoCategory.Other, Wed.AddDays(2), false, Repeat.None, -1, null, "You", Wed);
        var old = store.Add("Finished long ago", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed).Item!;
        store.SetDone(old.Id, true, Wed.AddDays(-45));
        Assert.Equal(1, store.RollOverOverdue(Wed));
        Assert.Equal("2026-11-04", store.Items.Single(i => i.Title == "Old one").Due);
        Assert.Equal("2026-11-06", store.Items.Single(i => i.Title == "Not yet").Due);
        Assert.True(store.Refresh(Wed));
        Assert.DoesNotContain(store.Items, i => i.Title == "Finished long ago");
        Assert.Equal(0, store.FinishedInLast(7, Wed));
    }

    [Fact]
    public void The_list_survives_a_restart_a_damaged_file_and_hand_edited_values()
    {
        var store = NewStore(out string path);
        store.Add("Call Sam", TodoCategory.Work, Wed, true, Repeat.Weekly, 3, "216482", "Notes", Wed);
        var again = new MyTodoService(path);
        var item = Assert.Single(again.Items);
        Assert.Equal(("Call Sam", TodoCategory.Work, "2026-11-04", true, Repeat.Weekly, 3, "216482", "Notes"), (item.Title, item.Category, item.Due, item.High, item.Repeat, item.RepeatDay, item.Number, item.Source));

        File.WriteAllText(path, "{ not json");
        Assert.Empty(new MyTodoService(path).Items);
        File.WriteAllText(path, new string(' ', 5 * 1024 * 1024));
        Assert.Empty(new MyTodoService(path).Items);

        File.WriteAllText(path, JsonSerializer.Serialize(new { Items = new object[]
        {
            new { Id = "a", Title = "  Fine  ", Category = 9, Due = "garbage", Repeat = 77, RepeatDay = 400, Number = "12", Source = new string('x', 500) },
            new { Id = "a", Title = "Same id", Category = 1 }, new { Id = "b", Title = "   " }, null!,
        } }));
        var repaired = new MyTodoService(path).Items;
        Assert.Equal(2, repaired.Count);
        Assert.Equal(repaired.Count, repaired.Select(i => i.Id).Distinct().Count());
        var fine = repaired.First(i => i.Title == "Fine");
        Assert.Equal((TodoCategory.Other, (string?)null, Repeat.None, -1, (string?)null), (fine.Category, fine.Due, fine.Repeat, fine.RepeatDay, fine.Number));
        Assert.True(fine.Source.Length <= 40);
    }

    [Fact]
    public void The_list_is_limited_and_writers_in_many_threads_never_corrupt_the_file()
    {
        var store = NewStore(out string path);
        Parallel.For(0, 60, n => store.Add($"Item {n}", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed));
        Assert.Equal(60, new MyTodoService(path).Items.Count);
        for (int i = 0; i < MyTodoService.MaxItems + 20; i++) store.Add($"More {i}", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed);
        Assert.Equal(MyTodoService.MaxItems, store.Items.Count);
        Assert.Contains("is full", store.Add("One too many", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed).Reason);
    }

    // ---------- the smart view ----------

    [Fact]
    public void The_view_groups_by_urgency_puts_high_priority_first_and_says_how_today_is_going()
    {
        var store = NewStore();
        store.Add("Late thing", TodoCategory.Work, Wed.AddDays(-2), false, Repeat.None, -1, null, "You", Wed);
        store.Add("Today low", TodoCategory.Personal, Wed, false, Repeat.None, -1, null, "You", Wed);
        store.Add("Today high", TodoCategory.Personal, Wed, true, Repeat.None, -1, null, "You", Wed);
        store.Add("Soon", TodoCategory.Work, Wed.AddDays(3), false, Repeat.None, -1, null, "You", Wed);
        store.Add("Far away", TodoCategory.Work, Wed.AddDays(30), false, Repeat.None, -1, null, "You", Wed);
        store.Add("No date", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed);
        var finished = store.Add("Finished", TodoCategory.Other, null, false, Repeat.None, -1, null, "You", Wed).Item!;
        store.SetDone(finished.Id, true, Wed);

        var view = MyTodoBuilder.Build(store.Items, Wed, store.FinishedInLast(7, Wed));
        Assert.Equal(new[] { "OVERDUE", "TODAY", "COMING UP · NEXT 7 DAYS", "ANYTIME", "DONE IN THE LAST 7 DAYS" }, view.Groups.Select(g => g.Heading));
        Assert.Equal(new[] { "Today high", "Today low" }, view.Groups[1].Items.Select(i => i.Title));
        Assert.Equal(new[] { "Far away", "No date" }, view.Groups[3].Items.Select(i => i.Title).OrderBy(x => x));
        Assert.Equal((6, 1, 1, 4, 1, "Late thing"), (view.Open, view.Overdue, view.DoneToday, view.TodayTotal, view.Finished7, view.Focus));

        var home = MyTodoBuilder.Build(store.Items, Wed, 0, TodoCategory.Personal);
        Assert.Equal(new[] { "Today high", "Today low" }, home.Groups.Single().Items.Select(i => i.Title));
        Assert.Null(home.Focus is null ? null : (home.Focus == "Today high" ? null : home.Focus));
        Assert.Empty(MyTodoBuilder.Build(Array.Empty<MyTodoItem>(), Wed, 0).Groups);
    }

    // ---------- the shared fixture with the browser simulation ----------

    private static readonly string[] GoldenCases =
    {
        "Don't forget to confirm the facility inspection date with the team tomorrow ASAP. Also email the team lead re: the Thursday huddle. Need to submit the IR response for ANDA 216482 by Friday. errands: book the dentist, pick up the dry cleaning",
        "- call the applicant\n* submit the timesheet\n1. email Pat\n2) book the conference room\n[ ] order the printer toner\n• update the tracking sheet",
        "Call the applicant and email the team lead", "Review the file and the attachments", "Review and approve the memo", "Send the draft then email the team lead",
        "Call Dr. Smith about the labeling. Ask about the 3 p.m. meeting today",
        "Send the status update to my supervisor every Tuesday", "Check my email daily", "Review the incoming queue every weekday", "Submit the expense report on the 1st every month", "Submit my timesheet weekly",
        "URGENT: call the applicant", "Send the memo asap", "Call Sam!!", "Call Sam when you can",
        "Hi Sam,\nThanks,\nRegards\nFrom: Pat <pat@example.test>\nSubject: huddle\nhttps://example.test/a", "Thanks, please send the report by Friday",
        "The inspection report was very clear and complete.", "Call Sam\ncall sam\nCALL SAM.",
        "Finish the report by Friday", "Finish the report next Friday", "Finish the report next Wednesday", "Finish the report in 2 weeks", "Finish the report next week",
        "Finish the report EOW", "Finish the report EOM", "Finish the report next month", "Finish the report 11/12", "Finish the report 1/5", "Finish the report 11/2",
        "Finish the report Nov 12, 2026", "Finish the report 12 Dec", "Finish the report 2026-12-01", "Finish the report on the 15th", "Finish the report by fri", "Finish the report tonight",
        "Add 1/2 inch to the margin", "He sat down and may need 5 minutes", "We discussed Friday's meeting", "Version 13/45 is wrong",
        "Call Sam on 99/99/9999 and 0/0 and 2026-13-45", "Remember to renew my certification every year", "Need to review ANDA 214990 forecast ASAP and update the status memo",
        "Meeting notes: team agreed on timeline. Action: Pat to send the draft labeling. Follow up with the applicant next Monday. Update the tracking sheet",
        "Update the tracker, file the memo, and call the applicant tomorrow", "Organize the shared folder this weekend", "Attach the memo, the cover letter, the table", "Call Sam, please", "Assess the facility inspection findings and route them to the team lead by Monday", "Follow-up with the applicant on the information request", "", "   ",
    };

    private static JsonObject GoldenJson()
    {
        string D8(DateOnly? d) => d is { } v ? v.ToString("yyyy-MM-dd") : "";
        JsonObject Item(SuggestedItem i) => new()
        {
            ["title"] = i.Title, ["category"] = (int)i.Category, ["due"] = D8(i.Due), ["high"] = i.High, ["repeat"] = (int)i.Repeat, ["repeatDay"] = i.RepeatDay,
            ["number"] = i.Number ?? "", ["actionable"] = i.Actionable,
        };
        var cases = new JsonArray();
        foreach (var text in GoldenCases)
        {
            var one = NoteParser.ParseOne(text, Wed);
            cases.Add(new JsonObject
            {
                ["text"] = text,
                ["items"] = new JsonArray(NoteParser.Parse(text, Wed).Select(i => (JsonNode)Item(i)).ToArray()),
                ["one"] = one is null ? null : Item(one),
                ["due"] = D8(NoteParser.ReadDue(text, Wed)),
            });
        }
        return new JsonObject { ["version"] = 1, ["today"] = "2026-11-04", ["cases"] = cases };
    }

    private static string GoldenPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("anda-hover-companion folder not found"), "mockup", "mytodo-golden.json");
    }

    /// <summary>The browser simulation has its own copy of the note reader. This file is the contract between the two: the app writes what it produces, the simulation's test must reproduce it.</summary>
    [Fact]
    public void The_note_reader_matches_the_fixture_the_simulation_must_reproduce()
    {
        string json = GoldenJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        string path = GoldenPath();
        if (Environment.GetEnvironmentVariable("ANDA_WRITE_GOLDEN") == "1") File.WriteAllText(path, json.Replace("\r\n", "\n") + "\n");
        Assert.True(File.Exists(path), "mytodo-golden.json is missing: run this test with ANDA_WRITE_GOLDEN=1 and commit the file");
        Assert.Equal(JsonNode.Parse(File.ReadAllText(path))!.ToJsonString(), JsonNode.Parse(json)!.ToJsonString());
    }
}
