using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AndaCompanion.Models;
using AndaCompanion.Services;
using AndaCompanion.Theming;
using AndaCompanion.UI;
using Xunit;

namespace AndaCompanion.Tests;

/// <summary>Round 33 in the Windows app's windows: the card's details and its short list, the lookup window, Today, text size and spacing, and Settings.</summary>
[Collection("Global state")]
public class Round33UiTests : IClassFixture<HeadlessFixture>
{
    private readonly HeadlessFixture _fx;
    public Round33UiTests(HeadlessFixture fx) => _fx = fx;
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
    private static string Name(Control c) => AutomationProperties.GetName(c) ?? "";
    private static List<string> Texts(Visual v) => v.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();

    [Fact]
    public async Task An_expanded_card_shows_the_chosen_columns_each_with_Copy_and_says_what_was_copied()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var record = repo.Find("216482")!;
            var copied = new List<string>(); bool allow = true; IReadOnlyList<string> fields = CardDetails.DefaultFields;
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, CardFields = () => fields, DataFileName = () => null,
                CopyText = t => { if (allow) copied.Add(t); return Task.FromResult(allow); } };
            try
            {
                w.Show(record, new PixelPoint(40, 40), false); Settle();
                w.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e"); Settle();
                var exp = w.FindControl<Expander>("DetailsExpander")!; var panel = w.FindControl<StackPanel>("DetailsPanel")!; var msg = w.FindControl<TextBlock>("DetailsMsg")!;
                bool shown = exp.IsVisible && exp.IsExpanded && w.FindControl<StackPanel>("Drawer")!.IsVisible;
                var rows = panel.Children.OfType<Grid>().ToList();
                var buttons = panel.GetVisualDescendants().OfType<Button>().ToList();
                var names = buttons.Select(Name).ToList();
                var values = rows.Select(g => g.Children.OfType<SelectableTextBlock>().Single().Text ?? "").ToList();
                string count = w.FindControl<TextBlock>("DetailsCount")!.Text ?? "", source = w.FindControl<TextBlock>("DetailsSource")!.Text ?? "";
                Click(buttons.Single(b => (string?)b.Tag == "agentEmail")); string said1 = msg.Text ?? "";
                var all = w.FindControl<Button>("DetailsAll")!; bool allShown = all.IsVisible; Click(all); string said2 = msg.Text ?? "";
                allow = false; Click(buttons.Single(b => (string?)b.Tag == "applicant")); string said3 = msg.Text ?? ""; allow = true;
                // the person chooses no column, then the card is shown again
                fields = Array.Empty<string>(); w.Show(record, new PixelPoint(40, 40), false); Settle();
                var none = Texts(panel); bool allHidden = !all.IsVisible; string countNone = w.FindControl<TextBlock>("DetailsCount")!.Text ?? "";
                return (shown, rows.Count, names, values, count, source, said1, said2, said3, copied, none, allHidden, countNone, live: AutomationProperties.GetLiveSetting(msg));
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);

        Assert.True(r.shown); Assert.Equal(9, r.Count);
        Assert.Equal(new[] { "Copy applicant", "Copy established name", "Copy project description", "Copy US agent", "Copy US agent email", "Copy applicant email", "Copy my notes" }, r.names.ToArray());
        Assert.Equal("Meridian Pharmaceuticals, Inc. (fictional) (Meridian)", r.values[0]);
        Assert.Equal(new[] { CardDetails.NotInSheet, CardDetails.NotInSheet }, r.values.Skip(7).ToArray());      // this sample ANDA has no 356h and no 3.2.P.1 in its sheet
        Assert.Equal("· 7 of 9 in your sheet", r.count); Assert.Equal("From the fictional demo records.", r.source);
        Assert.Equal("Copied: US agent email.", r.said1); Assert.Equal("Copied: all the details shown.", r.said2);
        Assert.Equal("Windows did not allow copying. Please select the text and press Ctrl+C.", r.said3);                 // never "Copied" when it was not
        Assert.Equal(2, r.copied.Count); Assert.Equal("us.agent@northgate-agents.example", r.copied[0]);
        Assert.StartsWith("ANDA 216482\nApplicant: Meridian Pharmaceuticals, Inc. (fictional) (Meridian)\n", r.copied[1]); Assert.Equal(8, r.copied[1].Split('\n').Length);
        Assert.Equal(new[] { CardDetails.NoneChosen }, r.none.ToArray()); Assert.True(r.allHidden); Assert.Equal("", r.countNone);
        Assert.Equal(AutomationLiveSetting.Polite, r.live);
    }

    [Fact]
    public async Task A_card_opened_without_the_hooks_shows_neither_details_nor_the_list_button()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository();
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false };
            try { w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle(); return (w.FindControl<Expander>("DetailsExpander")!.IsVisible, w.FindControl<Button>("KeepChip")!.IsVisible); }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal((false, false), r);
    }

    [Fact]
    public async Task Keep_in_my_list_keeps_and_removes_and_the_sixth_is_refused_in_words()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var pins = new List<string>();
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, IsKept = n => pins.Contains(n),
                ToggleKept = n => { var (after, result) = Recents.PinAfter(pins, n); pins = after; return result; } };
            try
            {
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                var chip = w.FindControl<Button>("KeepChip")!; var msg = w.FindControl<TextBlock>("ChipMsg")!;
                var seen = new List<(string Label, string Name, string Pins, string Said)>();
                void Look() => seen.Add(((string)chip.Content!, Name(chip), string.Join(",", pins), msg.IsVisible ? msg.Text ?? "" : ""));
                Look(); Click(chip); Look(); Click(chip); Look();
                pins = new() { "100001", "100002", "100003", "100004", "100005" }; Click(chip); Look();
                pins.RemoveAt(0); Click(chip); Look();
                return (chip.IsVisible, seen);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.True(r.IsVisible);
        Assert.Equal(("Keep in my list", "Keep in my list", "", ""), r.seen[0]);
        Assert.Equal(("Remove from my list", "Remove from my list", "216482", ""), r.seen[1]);
        Assert.Equal(("Keep in my list", "Keep in my list", "", ""), r.seen[2]);
        Assert.Equal(("Keep in my list", "Keep in my list", "100001,100002,100003,100004,100005", "Your list holds 5 ANDAs. Remove one to keep another."), r.seen[3]);
        Assert.Equal(("Remove from my list", "Remove from my list", "100002,100003,100004,100005,216482", ""), r.seen[4]);      // room again: kept, and the sentence is gone
    }

    [Fact]
    public async Task The_lookup_window_offers_the_kept_and_recent_ANDAs_and_says_why_a_number_is_not_an_ANDA()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var opened = new List<string>();
            var plain = new LookupWindow(opened.Add); plain.Show(); Settle();
            bool plainRecent = plain.FindControl<WrapPanel>("RecentPanel")!.IsVisible, plainRows = plain.FindControl<StackPanel>("RowsPanel")!.IsVisible;
            plain.FindControl<TextBox>("Input")!.Text = "N210793"; Settle(); Click(plain.FindControl<Button>("GoBtn")!);
            string err = plain.FindControl<TextBlock>("ErrorText")!.Text ?? ""; bool errShown = plain.FindControl<TextBlock>("ErrorText")!.IsVisible;
            plain.FindControl<TextBox>("Input")!.Text = "216 482"; Settle(); bool errGone = !plain.FindControl<TextBlock>("ErrorText")!.IsVisible;
            Click(plain.FindControl<Button>("GoBtn")!); string err2 = plain.FindControl<TextBlock>("ErrorText")!.Text ?? "";
            plain.Close(); Settle();

            var w = new LookupWindow(opened.Add, new LookupOptions(false, () => new[] { ("214990", true), ("216482", false) }, repo.Find)); w.Show(); Settle();
            var panel = w.FindControl<WrapPanel>("RecentPanel")!;
            var buttons = panel.Children.OfType<Button>().ToList();
            var names = buttons.Select(Name).ToList(); string caption = panel.Children.OfType<TextBlock>().First().Text ?? "";
            bool rowsHidden = !w.FindControl<StackPanel>("RowsPanel")!.IsVisible, focusInBox = w.FocusManager?.GetFocusedElement() is TextBox;
            bool closed = false; w.Closed += (_, _) => closed = true;
            Click(buttons[0]);
            return (plainRecent, plainRows, err, errShown, errGone, err2, names, caption, rowsHidden, focusInBox, closed, opened);
        }, CancellationToken.None);
        Assert.False(r.plainRecent); Assert.False(r.plainRows);
        Assert.True(r.errShown); Assert.Equal("N210793 is an NDA number (N is for NDA, A is for ANDA). Please enter a six-digit ANDA number.", r.err);
        Assert.True(r.errGone); Assert.Equal("Please enter the six digits without spaces or dashes.", r.err2);
        Assert.Equal("RECENT", r.caption); Assert.Equal(new[] { "ANDA 214990, kept in my list", "ANDA 216482" }, r.names.ToArray());
        Assert.True(r.rowsHidden); Assert.True(r.focusInBox); Assert.True(r.closed); Assert.Equal(new[] { "214990" }, r.opened.ToArray());
    }

    [Fact]
    public async Task One_Bar_offers_rows_to_press_follows_the_arrow_keys_and_opens_nothing_by_itself()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var cards = new List<string>(); var drafts = new List<string>(); var asks = new List<string>();
            LookupWindow Open() { var x = new LookupWindow(cards.Add, new LookupOptions(true, null, repo.Find, drafts.Add, asks.Add)); x.Show(); Settle(); return x; }
            void Press(LookupWindow x, Key key, PhysicalKey physical) { x.FindControl<TextBox>("Input")!.Focus(); x.KeyPress(key, RawInputModifiers.None, physical, null); Settle(); }
            var w = Open();
            var titles0 = w.Rows.Select(x => x.Title).ToList(); string caption = w.FindControl<TextBlock>("Caption")!.Text ?? "";
            w.FindControl<TextBox>("Input")!.Text = "216482"; Settle();
            var titles1 = w.Rows.Select(x => x.Title).ToList(); string sub1 = w.Rows[0].Sub;
            var rowNames = w.FindControl<StackPanel>("RowsPanel")!.Children.OfType<Button>().Select(Name).ToList();
            int nothingYet = cards.Count + drafts.Count + asks.Count;                 // typing opened nothing
            Press(w, Key.Down, PhysicalKey.ArrowDown); int at1 = w.SelectedRow; Press(w, Key.Up, PhysicalKey.ArrowUp); Press(w, Key.Up, PhysicalKey.ArrowUp); int atWrapped = w.SelectedRow;
            Press(w, Key.Down, PhysicalKey.ArrowDown); int atFirst = w.SelectedRow;
            Press(w, Key.Enter, PhysicalKey.Enter);                                   // the first row: the card
            var w2 = Open(); w2.FindControl<TextBox>("Input")!.Text = "Please confirm the goal date."; Settle();
            var titles2 = w2.Rows.Select(x => x.Title).ToList();
            Press(w2, Key.Enter, PhysicalKey.Enter);                                  // the first row is now the review of what was typed
            var w3 = Open(); Press(w3, Key.Down, PhysicalKey.ArrowDown); Press(w3, Key.Enter, PhysicalKey.Enter);       // nothing typed: second row is Ask
            var w4 = Open(); w4.FindControl<TextBox>("Input")!.Text = "999999"; Settle(); string unknown = w4.Rows[0].Sub; Click(w4.FindControl<StackPanel>("RowsPanel")!.Children.OfType<Button>().ElementAt(1)); 
            return (titles0, caption, titles1, sub1, rowNames, nothingYet, at1, atWrapped, atFirst, titles2, unknown, cards, drafts, asks);
        }, CancellationToken.None);
        Assert.Equal("ONE BAR", r.caption);
        Assert.Equal(new[] { "Review a draft", "Ask about a selection" }, r.titles0.ToArray());
        Assert.Equal(new[] { "ANDA 216482 · Metformin HCl ER Tablets, 500 mg", "Review a draft", "Ask about a selection" }, r.titles1.ToArray());
        Assert.Equal("14 days later than the goal · needs you: IR response due in 5 days", r.sub1);
        Assert.EndsWith(". Selected: Enter opens it", r.rowNames[0]); Assert.DoesNotContain("Selected", r.rowNames[1]);
        Assert.Equal(0, r.nothingYet);
        Assert.Equal((1, 2, 0), (r.at1, r.atWrapped, r.atFirst));
        Assert.Equal(new[] { "Review what you typed as a draft", "Ask about a selection" }, r.titles2.ToArray());
        Assert.Equal("Not in your data", r.unknown);
        Assert.Equal(new[] { "216482" }, r.cards.ToArray());
        Assert.Equal(new[] { "Please confirm the goal date.", "" }, r.drafts.ToArray());      // Enter on the typed text, and the pressed "Review a draft" row of the unknown number
        Assert.Equal(new[] { "" }, r.asks.ToArray());
    }

    [Fact]
    public async Task Today_lists_what_needs_a_look_shows_the_selected_ANDA_and_copies_its_status_line()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var copied = new List<string>(); var opened = new List<string>(); bool allow = true;
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var w = new TodayWindow(new TodayHooks(() => repo.Numbers.Select(n => Find(n)!).ToList(), Find, () => new[] { "214990" }, () => new[] { "216482", "214990", "999999" }, opened.Add,
                t => { if (allow) copied.Add(t); return Task.FromResult(allow); }));
            w.Show(); Settle();
            var list0 = Texts(w.List); var main0 = Texts(w.Main); string? sel0 = w.Selected;
            var rowNames = w.List.Children.OfType<Button>().Select(Name).ToList();
            bool focusInSearch = w.FocusManager?.GetFocusedElement() is TextBox;
            Click(w.CopyButton); var afterCopy = Texts(w.Main).Last();
            allow = false; Click(w.CopyButton); var afterRefused = Texts(w.Main).Last(); allow = true;
            Click(w.OpenButton);
            Click(w.List.Children.OfType<Button>().First(b => (string?)b.Tag == "214990")); string? sel1 = w.Selected; var main1 = Texts(w.Main);
            w.Search.Text = "ANDA 216482"; Settle(); string? sel2 = w.Selected;
            w.Search.Text = "999999"; Settle(); string? sel3 = w.Selected;                 // a number the data does not know changes nothing
            bool closed = false; w.Closed += (_, _) => closed = true;
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle();

            var empty = new TodayWindow(new TodayHooks(() => Array.Empty<Today.Look>(), _ => null, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { })); empty.Show(); Settle();
            var emptyTexts = Texts(empty.List).Concat(Texts(empty.Main)).ToList(); empty.Close(); Settle();
            return (list0, main0, sel0, rowNames, focusInSearch, afterCopy, afterRefused, sel1, main1, sel2, sel3, closed, emptyTexts, copied, opened, title: w.Title);
        }, CancellationToken.None);
        Assert.Equal("216482", r.sel0);                                                   // the most pressing one is shown first
        Assert.Equal(new[] { "NEEDS A LOOK", "216482", "urgent", "14 d later · IR response due in 5 days", "214990", "worth a look", "9 d ahead", "KEPT IN MY LIST", "214990", "worth a look", "9 d ahead", "RECENT", "216482", "urgent", "14 d later · IR response due in 5 days" }, r.list0.ToArray());      // each mark is a word as well as a color
        Assert.Equal("ANDA 216482, urgent. 14 d later · IR response due in 5 days. Shown on the right", r.rowNames[0]);      // the color of the mark is also said in words
        Assert.Equal("ANDA 214990, worth a look. 9 d ahead", r.rowNames[1]);
        Assert.Equal(new[] { "ANDA 216482 · Metformin HCl ER Tablets, 500 mg", "14", "days later than the goal", "Goal Oct 3, 2026 · Forecast Oct 17, 2026 · moderate confidence", "⚠ NEEDS YOU", "IR response due in 5 days — Biopharmaceutics" }, r.main0.Take(6).ToArray());
        Assert.StartsWith("Watch: Goal date falls on a weekend: Sat Oct 3.", r.main0[6]);
        Assert.True(r.focusInSearch);
        Assert.Equal("Copied: the status line.", r.afterCopy);
        Assert.StartsWith("Windows did not allow copying, so here is the status line: ANDA 216482 (Metformin HCl ER Tablets, 500 mg): Cycle 2", r.afterRefused);
        Assert.Single(r.copied); Assert.Equal(StatusLine.Build(new Data.MockAndaRepository().Find("216482")!), r.copied[0]);
        Assert.Equal(new[] { "216482" }, r.opened.ToArray());
        Assert.Equal("214990", r.sel1); Assert.Equal(new[] { "ANDA 214990 · Metformin HCl ER Tablets, 750 mg", "9", "days ahead of the goal" }, r.main1.Take(3).ToArray());
        Assert.Equal("216482", r.sel2); Assert.Equal("216482", r.sel3); Assert.True(r.closed);
        Assert.Equal(new[] { Today.SaidNothing, Today.SaidChoose }, r.emptyTexts.ToArray());
        Assert.Equal("ANDA Companion (Notula) — Today", r.title);
    }

    [Fact]
    public async Task Larger_text_draws_a_window_larger_by_the_factor_once_and_standard_text_leaves_it_as_it_was()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository();
            double Width(double scale)
            {
                var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false };
                DisplayScale.Apply(w, scale);
                try { w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle(); return w.ClientSize.Width; }
                finally { w.Close(); Settle(); }
            }
            double w1 = Width(1), w115 = Width(1.15), w13 = Width(1.3);
            var fixedWin = new Window { Width = 400, Height = 300, MinWidth = 200, Content = new TextBlock { Text = "x" } };
            var inner = fixedWin.Content;
            DisplayScale.Apply(fixedWin, 1); bool untouched = ReferenceEquals(fixedWin.Content, inner);
            DisplayScale.Apply(fixedWin, 1.3); var host = fixedWin.Content as LayoutTransformControl; double a = fixedWin.Width, ah = fixedWin.Height, amin = fixedWin.MinWidth;
            DisplayScale.Apply(fixedWin, 1.3); DisplayScale.Apply(fixedWin, 1.15); bool sameHost = ReferenceEquals(fixedWin.Content, host); double b = fixedWin.Width;
            double factor = (host?.LayoutTransform as ScaleTransform)?.ScaleX ?? 0;
            fixedWin.Show(); Settle();
            DisplayScale.Set(1.3); double live = fixedWin.Width, of = DisplayScale.Of(fixedWin);        // the person changes the size while the window is open
            DisplayScale.Set(1); double back = fixedWin.Width; bool plain = host?.LayoutTransform is null; double current = DisplayScale.Current;
            DisplayScale.Apply(fixedWin, 99); double odd = fixedWin.Width;                            // a factor that is not a text size is standard text
            fixedWin.Close(); Settle(); DisplayScale.Set(1);
            // The line that says what is switched on is enlarged with the window, whichever of the two is applied first, and is never put there twice.
            int Bars(Window x) => x.GetVisualDescendants().OfType<Button>().Count(b => (string?)b.Tag == "onbar");
            bool Inside(Window x) => x.Content is LayoutTransformControl { Child: DockPanel { Tag: "with-onbar" } };
            Today.Look? None(string _) => null;
            TodayWindow NewToday() => new(new TodayHooks(() => Array.Empty<Today.Look>(), None, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }));
            var first = NewToday(); DisplayScale.Apply(first, 1.3); first.Show(); ConnectionBar.Attach(first); ConnectionBar.Attach(first); Settle();
            var second = NewToday(); second.Show(); ConnectionBar.Attach(second); DisplayScale.Apply(second, 1.3); ConnectionBar.Attach(second); Settle();
            var bars = (Bars(first), Inside(first), Bars(second), Inside(second));
            first.Close(); second.Close(); Settle();
            return (w1, w115, w13, untouched, hostOk: host is not null && ReferenceEquals(host.Child, inner), a, ah, amin, sameHost, b, factor, live, of, back, plain, current, odd, bars);
        }, CancellationToken.None);
        Assert.InRange(r.w115 / r.w1, 1.14, 1.16); Assert.InRange(r.w13 / r.w1, 1.29, 1.31);
        Assert.True(r.untouched); Assert.True(r.hostOk); Assert.True(r.sameHost);
        Assert.Equal((520.0, 390.0, 260.0), (r.a, r.ah, r.amin));
        Assert.Equal(460.0, r.b, 6); Assert.Equal(1.15, r.factor, 6);                                 // from the size it was built at, never from the last one
        Assert.Equal(520.0, r.live, 6); Assert.Equal(1.3, r.of, 6); Assert.Equal(400.0, r.back, 6); Assert.True(r.plain); Assert.Equal(1.0, r.current);
        Assert.Equal(400.0, r.odd, 6);
        Assert.Equal((1, true, 1, true), r.bars);
    }

    [Fact]
    public async Task Compact_spacing_makes_the_card_shorter_and_going_back_restores_it_exactly()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository();
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false };
            try
            {
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                var root = w.FindControl<Border>("Root")!; var chips = w.FindControl<WrapPanel>("ChipsWrap")!;
                double h0 = root.Bounds.Height; var m0 = chips.Margin;
                w.ApplySpacing(Display.Spacing("compact")); Settle(); double h1 = root.Bounds.Height; var m1 = chips.Margin;
                w.ApplySpacing(Display.Spacing("compact")); Settle(); double h2 = root.Bounds.Height;      // again: it does not add up
                w.ApplySpacing(Display.Spacing("comfortable")); Settle(); double h3 = root.Bounds.Height; var m3 = chips.Margin;
                return (h0, h1, h2, h3, m0, m1, m3, width: root.Bounds.Width);
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.True(r.h1 < r.h0 - 10, $"compact {r.h1} against comfortable {r.h0}");
        Assert.Equal(r.h1, r.h2, 3); Assert.Equal(r.h0, r.h3, 3); Assert.Equal(r.m0, r.m3);
        Assert.Equal((r.m0.Left, r.m0.Top * 0.72, r.m0.Right, r.m0.Bottom * 0.72), (r.m1.Left, r.m1.Top, r.m1.Right, r.m1.Bottom));      // above and below only: the sides are as they were
        Assert.Equal(372, r.width, 3);
    }

    [Fact]
    public async Task Settings_offers_the_new_choices_and_each_takes_effect_when_it_is_made()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            ThemeManager.Apply(Appearances.Default);
            var s = new Settings { Recents = new() { "216482", "214990" }, Pins = new() { "214990" } }; int applied = 0;
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => applied++);
            w.Show(); w.ExpandAll(); Settle();
            List<RadioButton> Radios(string panel) => w.FindControl<WrapPanel>(panel)!.Children.OfType<RadioButton>().ToList();
            var labels = Radios("TextSizePanel").Concat(Radios("DensityPanel")).Concat(Radios("LookupStylePanel")).Select(x => (string)x.Content!).ToList();
            var checkedAtFirst = Radios("TextSizePanel").Concat(Radios("DensityPanel")).Concat(Radios("LookupStylePanel")).Where(x => x.IsChecked == true).Select(x => (string?)x.Tag).ToList();
            Radios("TextSizePanel")[2].IsChecked = true; Settle(); Radios("DensityPanel")[1].IsChecked = true; Settle(); Radios("LookupStylePanel")[1].IsChecked = true; Settle();
            var chosen = (s.TextSize, s.Density, s.LookupStyle); int appliedAfterThree = applied;
            string hint = w.FindControl<TextBlock>("DisplayHint")!.Text ?? "";
            var status = w.FindControl<TextBlock>("RecentsStatus")!;
            Click(w.FindControl<Button>("RecentsClear")!); string said1 = status.Text ?? ""; int recents1 = s.Recents.Count; int pinsKept = s.Pins.Count;
            s.Recents = new() { "216482" };
            w.FindControl<CheckBox>("RecentsCheck")!.IsChecked = false; Settle(); string said2 = status.Text ?? ""; var off = (s.RememberRecents, s.Recents.Count);
            w.FindControl<CheckBox>("RecentsCheck")!.IsChecked = true; Settle(); string said3 = status.Text ?? "";
            var boxes = w.FindControl<WrapPanel>("CardFieldsPanel")!.Children.OfType<CheckBox>().ToList();
            var boxLabels = boxes.Select(b => (string)b.Content!).ToList(); int checkedBoxes = boxes.Count(b => b.IsChecked == true);
            boxes.Single(b => (string?)b.Tag == "fei").IsChecked = true; Settle(); boxes.Single(b => (string?)b.Tag == "applicant").IsChecked = false; Settle();
            var fields = s.CardFields.ToList(); string fieldsSaid = w.FindControl<TextBlock>("CardFieldsStatus")!.Text ?? "";
            foreach (var b in boxes) { b.IsChecked = false; } Settle(); string noneSaid = w.FindControl<TextBlock>("CardFieldsStatus")!.Text ?? "";
            w.Close(); Settle();
            var saved = SettingsService.Load();
            return (labels, checkedAtFirst, chosen, appliedAfterThree, hint, said1, recents1, pinsKept, said2, off, said3, boxLabels, checkedBoxes, fields, fieldsSaid, noneSaid, savedSize: saved.TextSize, savedFields: saved.CardFields.Count, savedStyle: saved.LookupStyle);
        }, CancellationToken.None);
        Assert.Equal(new[] { "Standard text", "Larger text", "Largest text", "Comfortable spacing", "Compact spacing", "Box: a number and Go", "One Bar: also offers Review and Ask" }, r.labels.ToArray());
        Assert.Equal(new[] { "standard", "comfortable", "box" }, r.checkedAtFirst.ToArray());
        Assert.Equal(("xl", "compact", "bar"), r.chosen); Assert.Equal(3, r.appliedAfterThree);
        Assert.Equal(Display.Hint, r.hint);
        Assert.Equal((Recents.SaidCleared, 0, 1), (r.said1, r.recents1, r.pinsKept));                  // clearing the recent ones leaves the kept ones
        Assert.Equal(Recents.SaidOff, r.said2); Assert.Equal((false, 0), r.off); Assert.Equal("", r.said3);
        Assert.Equal(CardDetails.Fields.Select(f => f.Label).ToArray(), r.boxLabels.ToArray()); Assert.Equal(9, r.checkedBoxes);
        Assert.Equal(new[] { "established", "project", "agentCo", "agentEmail", "applicantEmail", "myNotes", "doc356h", "doc32p1", "fei" }, r.fields.ToArray());
        Assert.Equal("9 of 12 columns are shown under “Details”.", r.fieldsSaid); Assert.Equal("No columns are chosen: a card shows no details.", r.noneSaid);
        Assert.Equal(("xl", 0, "bar"), (r.savedSize, r.savedFields, r.savedStyle));                    // each choice was saved when it was made
    }

    [Fact]
    public void The_app_joins_the_five_features_to_its_windows()
    {
        // The app itself cannot be started in a test (tray icon, global shortcuts). These are the lines that join the tested parts; a line that goes missing fails here.
        string Read(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
            return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "app", "AndaCompanion" }.Concat(parts).ToArray()));
        }
        string app = Read("App.axaml.cs"), more = Read("App.Round33.cs");
        Assert.Contains("DisplayScale.Watch(); DisplayScale.Set(Display.TextScale(_settings.TextSize));", app);                                   // at the start
        Assert.Contains("DisplayScale.Set(Display.TextScale(_settings.TextSize)); _card?.ApplySpacing(Display.Spacing(_settings.Density));", app);  // and when Settings change
        Assert.Contains("}, LookupOptionsNow()).Show();", app);
        Assert.Matches(@"_lastCard = \(record, near, mini\);\s+NoteRecent\(record\.Number\);", app);
        Assert.Matches(@"JoinCard\(card, own: notes\);\s+card\.Show\(record, near, mini\);", app);
        Assert.Matches(@"DisplayScale\.Apply\(compact, DisplayScale\.Current\);\s+compact\.ShowNear\(near\);", app);
        Assert.Contains("\"Today…\" => Item(\"Today…\", OpenToday),", app);                                                                    // round 34: in the focused menu too; round 35: built from TrayMenu
        Assert.Contains("new(_settings.LookupStyle == \"bar\", ListedForLookup, _repository.Find, OpenDraft, text => OpenAsk(text));", more);
        Assert.Contains("Recents.After(_settings.Recents, number, _settings.RememberRecents)", more);
        Assert.Contains("if (result is \"kept\" or \"removed\") { _settings.Pins = pins; SettingsService.Save(_settings); }", more);
        Assert.Contains("if (own) { card.IsKept = number => _settings.Pins.Contains(number); card.ToggleKept = ToggleKept; }", more);
        Assert.Contains("card.CardFields = () => _settings.CardFields;", more);
        Assert.Contains("card.ApplySpacing(Display.Spacing(_settings.Density));", more);
        Assert.Contains("DisplayScale.Apply(card, DisplayScale.Current);", more);
    }

    // ---------- after the separate review ----------

    [Fact]
    public async Task A_change_of_text_size_reaches_windows_opened_at_standard_size_and_Settings_stays_on_its_screen()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            DisplayScale.Set(1);
            var repo = new Data.MockAndaRepository();
            var card = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false };
            DisplayScale.Apply(card, 1); card.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
            Today.Look? None(string _) => null;
            var today = new TodayWindow(new TodayHooks(() => Array.Empty<Today.Look>(), None, () => Array.Empty<string>(), () => Array.Empty<string>(), _ => { }));
            DisplayScale.Apply(today, 1); today.Show(); Settle();
            double w0 = today.Width, c0 = card.ClientSize.Width;
            DisplayScale.Set(1.3); Settle();
            var after = (DisplayScale.Of(card), DisplayScale.Of(today), today.Width, card.ClientSize.Width / c0);
            // the screen is 1366 x 768 with a 40-pixel taskbar; at 1.3 the Settings window must still fit in it
            DisplayScale.RoomForTest = _ => new Size(1366, 728);
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            var settings = new SettingsWindow(new Settings(), new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            DisplayScale.Apply(settings, 1.3); settings.Show(); settings.ExpandAll(); Settle();
            var body = settings.FindControl<ScrollViewer>("Body")!;
            double maxAt13 = body.MaxHeight;
            DisplayScale.Set(1); Settle(); double maxAt1 = body.MaxHeight;
            // a window larger than the room is held to it
            var big = new Window { Width = 1200, Height = 700, Content = new TextBlock { Text = "x" } };
            DisplayScale.Apply(big, 1.3); var held = (big.Width, big.Height);
            DisplayScale.RoomForTest = null;
            card.Close(); today.Close(); settings.Close(); big.Close(); Settle(); DisplayScale.Set(1);
            return (w0, after, maxAt13, maxAt1, held, screenH: settings.Screens?.Primary is { } p ? p.WorkingArea.Height / p.Scaling : double.NaN);
        }, CancellationToken.None);
        Assert.Equal(640, r.w0);
        Assert.Equal(1.3, r.after.Item1, 6); Assert.Equal(1.3, r.after.Item2, 6); Assert.Equal(832, r.after.Item3, 6); Assert.InRange(r.after.Item4, 1.29, 1.31);
        Assert.Equal((1366.0, 728.0), r.held);
        if (!double.IsNaN(r.screenH))
        {
            Assert.True(r.maxAt13 < r.maxAt1, $"at 1.3 the sections get less room ({r.maxAt13}) than at standard size ({r.maxAt1})");
            Assert.True((r.maxAt13 + 52 + 40 + 34 + 14 + 20 + ConnectionBar.Height) * 1.3 <= r.screenH + 0.5 || r.maxAt13 <= 240, $"the enlarged Settings window fits its screen ({r.maxAt13} at 1.3, screen {r.screenH})");
        }
    }

    [Fact]
    public async Task One_Bar_starts_again_at_the_first_row_after_typing_says_each_move_and_explains_an_NDA_number_on_Enter()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var cards = new List<string>(); var drafts = new List<string>();
            LookupWindow Open() { var x = new LookupWindow(cards.Add, new LookupOptions(true, null, repo.Find, drafts.Add, _ => { })); x.Show(); Settle(); return x; }
            void Press(LookupWindow x, Key key, PhysicalKey physical) { x.FindControl<TextBox>("Input")!.Focus(); x.KeyPress(key, RawInputModifiers.None, physical, null); Settle(); }
            var w = Open();
            Press(w, Key.Down, PhysicalKey.ArrowDown); string said = w.Said;
            w.FindControl<TextBox>("Input")!.Text = "216482"; Settle(); int atAfterTyping = w.SelectedRow;
            Press(w, Key.Enter, PhysicalKey.Enter);
            var w2 = Open(); w2.FindControl<TextBox>("Input")!.Text = "N210793"; Settle();
            var first = w2.Rows[0]; Press(w2, Key.Enter, PhysicalKey.Enter);
            var err = w2.FindControl<TextBlock>("ErrorText")!; bool stillOpen = w2.IsVisible;
            string go = (string)w2.FindControl<Button>("GoBtn")!.Content!;
            w2.Close(); Settle();
            return (said, atAfterTyping, cards, drafts, first, errText: err.Text, errShown: err.IsVisible, stillOpen, go);
        }, CancellationToken.None);
        Assert.Equal("Ask about a selection. 2 of 2.", r.said);
        Assert.Equal(0, r.atAfterTyping); Assert.Equal(new[] { "216482" }, r.cards.ToArray());
        Assert.Equal(("!", "Not an ANDA number", "note"), (r.first.Icon, r.first.Title, r.first.Act));
        Assert.True(r.errShown && r.stillOpen); Assert.Equal("N210793 is an NDA number (N is for NDA, A is for ANDA). Please enter a six-digit ANDA number.", r.errText);
        Assert.Empty(r.drafts); Assert.Equal("Go", r.go);
    }

    [Fact]
    public async Task Pressing_a_row_of_Today_keeps_the_keyboard_on_it_says_one_short_sentence_and_reads_the_lists_as_they_are_now()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var pins = new List<string>();
            Today.Look? Find(string id) => repo.Find(id) is { } rec ? Round33Tests.Look(rec) : null;
            var w = new TodayWindow(new TodayHooks(() => repo.Numbers.Select(n => Find(n)!).ToList(), Find, () => pins, () => Array.Empty<string>(), _ => { }));
            w.Show(); Settle();
            var row = w.List.Children.OfType<Button>().First(b => (string?)b.Tag == "214990");
            row.Focus(); Click(row);
            var focused = w.FocusManager?.GetFocusedElement() as Button;
            string said = w.Said; bool mainIsQuiet = AutomationProperties.GetLiveSetting(w.Main) == AutomationLiveSetting.Off;
            pins.Add("214990"); w.Search.Text = "x"; Settle();                     // kept on a card meanwhile: the window shows it at its next paint
            var titles = Texts(w.List);
            w.Close(); Settle();
            return ((string?)focused?.Tag, focused?.BorderThickness.Left, said, mainIsQuiet, kept: titles.Contains("KEPT IN MY LIST"));
        }, CancellationToken.None);
        Assert.Equal("214990", r.Item1); Assert.Equal(2, r.Item2);
        Assert.Equal("Shown: ANDA 214990, 9 days ahead of the goal.", r.said);
        Assert.True(r.mainIsQuiet); Assert.True(r.kept);
    }

    [Fact]
    public async Task A_Copied_message_never_stays_on_or_lands_on_another_ANDA()
    {
        var r = await _fx.Session.Dispatch(async () =>
        {
            ThemeManager.Apply(Appearances.Default);
            var repo = new Data.MockAndaRepository(); var gate = new TaskCompletionSource<bool>(); bool slow = false;
            var w = new CardWindow(new OfflineBriefService(), repo.Find) { CloseWhenDeactivated = false, CardFields = () => CardDetails.DefaultFields,
                CopyText = _ => slow ? gate.Task : Task.FromResult(true) };
            try
            {
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                w.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e"); Settle();
                var msg = w.FindControl<TextBlock>("DetailsMsg")!;
                Click(w.FindControl<StackPanel>("DetailsPanel")!.GetVisualDescendants().OfType<Button>().First()); string first = msg.Text ?? "";
                w.Show(repo.Find("214990")!, new PixelPoint(40, 40), false); Settle(); bool clearedOnNew = !msg.IsVisible;
                slow = true; Click(w.FindControl<StackPanel>("DetailsPanel")!.GetVisualDescendants().OfType<Button>().First());
                w.Show(repo.Find("216482")!, new PixelPoint(40, 40), false); Settle();
                gate.SetResult(true); await Task.Delay(30); Settle();
                return (first, clearedOnNew, landed: msg.IsVisible ? msg.Text ?? "" : "");
            }
            finally { w.Close(); Settle(); }
        }, CancellationToken.None);
        Assert.Equal("Copied: applicant.", r.first); Assert.True(r.clearedOnNew); Assert.Equal("", r.landed);
    }

    [Fact]
    public async Task Clear_my_list_empties_the_kept_list_and_says_the_ANDAs_are_not_changed()
    {
        var r = await _fx.Session.Dispatch(() =>
        {
            using var home = new EnvScope(AppPaths.HomeEnvVar, Temp.NewDir());
            ThemeManager.Apply(Appearances.Default);
            var s = new Settings { Pins = new() { "214990", "999999" }, Recents = new() { "216482" } };
            var w = new SettingsWindow(s, new PromptLibraryService(Path.Combine(Temp.NewDir(), "p.json")), () => { });
            w.Show(); w.ExpandAll(); Settle();
            Click(w.FindControl<Button>("PinsClear")!);
            string said = w.FindControl<TextBlock>("RecentsStatus")!.Text ?? "";
            w.Close(); Settle();
            return (s.Pins.Count, s.Recents.Count, said, saved: SettingsService.Load().Pins.Count);
        }, CancellationToken.None);
        Assert.Equal((0, 1, Recents.SaidPinsCleared, 0), r);
    }

    [Fact]
    public void The_app_remembers_a_number_when_its_card_opens_and_offers_no_recent_number_while_remembering_is_off()
    {
        string Read(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.Name != "anda-hover-companion") dir = dir.Parent;
            return File.ReadAllText(Path.Combine(dir!.FullName, "app", "AndaCompanion", name));
        }
        string more = Read("App.Round33.cs");
        Assert.Matches(@"var after = Recents\.After\(_settings\.Recents, number, _settings\.RememberRecents\);\s+if \(after\.SequenceEqual\(_settings\.Recents\)\) return;\s+_settings\.Recents = after;\s+SettingsService\.Save\(_settings\);", more);
        Assert.Contains("_settings.RememberRecents ? _settings.Recents : new List<string>(), x => _repository.Find(x) is not null", more);
        Assert.Contains("Recents: () => _settings.RememberRecents ? _settings.Recents : new List<string>(),", more);
    }
}
