# ANDA Companion (Notula) — Windows app (v0)

A .NET 8 / Avalonia tray application implementing the design in `../DESIGN.md`.
**All bundled data is fictional.** Not an FDA product; a concept prototype.

> **Not in this app yet (built in the browser simulation in rounds 20 to 22, for review first):** quick notes typed on the card, Enter to add a note, Proofread and
> Fact-check that work with no AI, and the notes and open items shown for recall under a review. The card's starting transparency is 90% here too.
>
> **What this app has of the newer design.** The **Loupe** icon, with no amber at any size; **eight looks, the same as the simulation** (W, V, U, T, K, P and the compact boxes L and O), with
> **W · Graphite Gauge** as the default (one answer first with a gauge beside it, then the facts, and one main action, Copy status, which Enter
> triggers). W and V follow the transparency setting; the blur of the simulation is not drawn here. A tray icon that shows a dot for one thing, the number for two to nine and “9+” beyond, with a
> setting for when it is counted again (only when the app is used, also once just after midnight, or also every hour); an amber dot on the tray icon **only while something needs the person today** (an action item that says it is due today or overdue, or a goal date that is today or has passed; never for a sample record). At rest the tray icon carries no amber at all (round 22); the short number forms (**N210793 is an NDA and is refused; A210793 is read as ANDA 210793**); and
> the default shortcuts **Ctrl+Alt+Shift+R** (Review a draft) and **Ctrl+Alt+Shift+L** (My to-do list), which replaced Ctrl+Alt+D and Ctrl+Alt+L because Word uses those. A shortcut
> you already saved is kept. **Not in this app yet, only in the browser simulation** (`../mockup/windows-simulation.html`): One Bar, Today, recent and kept ANDAs,
> spacing and text size, card details with Copy, notices near the clock with division news, and the one question before sending. See `../DESIGN.md` §11b and §11c.

## Quick setup (under 5 minutes)

**No .NET needed:** every green run of the *ANDA Companion (Windows)* workflow publishes
`anda-companion-demo-win-x64`: a single self-contained `AndaCompanion.exe` plus a launcher, a sample
memo and a demo guide (`../demo/DEMO.md`). Extract, double-click `Start-Demo.bat`, pin the tray icon.

1. `dotnet run --project AndaCompanion` (or double-click the published `AndaCompanion.exe`).
2. First run opens **Settings** automatically: pick an appearance (the default is **T · Pocket
   Plain**), a consent model, auto-hide and your transparency — a demo card appears alongside so
   every choice previews live.
3. Optional, the day API access is approved: open **Settings, Connection**. For Claude, set
   `ANTHROPIC_API_KEY` (agency-approved channel), choose Claude, check the model name and press **Test**.
   For any other API, choose **My API** and fill in its address, header, request body and reply path; the token
   stays in an environment variable you name. No reinstall, no code change. For **Elsa**, follow
   `../ELSA-INTEGRATION.md`: until its two small translations are written, My API covers it.

## What works today

- **Tray app** — the Loupe icon (an indigo plate and a white ring; amber appears only as the dot or the number), no notifications. Menu: lookup, demo cards, quick
  Appearance switch, **Settings…**, **Prompt library…**. All persisted to
  `%AppData%\AndaCompanion\settings.json`.
- **Settings window** — Appearance (eight looks since round 23, **light-only by product decision**; **W ·
  Graphite Gauge is the default**, T · Pocket Plain was before it): six full cards (W, V, U, T, K, P, none with a pattern behind it) and two **compact boxes** (L, O). A setting saved with a retired look falls back to the default.
  When the tray icon is counted again (three choices),
  Auto-hide (Off / 10 / 20 / 40 s, default 10), Consent model (**Whisper invite is the default** for
  new installs; an install that already saved a choice keeps it), **urgent items** (in whisper mode an
  at-risk or late ANDA opens a peek by itself), the weekend/holiday window, **My data**,
  **standing instructions** for the writing actions, **four rebindable shortcuts** (Select & Peek, Lookup, Review a draft, Ask), an optional
  **usage log** (off by default), **card transparency slider** (40–100%, starting at 90% since round 20, 92% before; text always stays opaque),
  companion lens picker, and the **connection panel** (Auto, Offline, Claude with its model name, or My API) with one-click Test.
  A **Go to** list at the top jumps to any section (and opens it); its names are in sentence case, with abbreviations such as ANDA and API kept (`SettingsSections.SentenceCase`). With **Show all tools** on, Settings is eight collapsible sections, each with a strong line under it: *Look and feel*, *How it
  starts*, *Dates*, *My files and writing*, *My to-do list*, *My actions, agents and ANDA to-do list*, *AI and connections*, *Maintenance*. The **focused edition, which is the default**, shows six: *Look and feel* (back since round 19, with every look and the transparency slider), *How it starts*, *My files and writing*, *Ask: my stored prompts*, *AI and connections* and *Maintenance*. **They arrange
  themselves:** when Settings opens, everything is collapsed except what needs a look (a shortcut Windows did not register, a portfolio file that could not be
  read, an AI connection that was never tested, an agent or action that is not ready), what a new user needs first (shortcuts, portfolio) and up to two sections
  opened three or more times; each open section says why. *Suggested view*, *Expand all* and *Collapse all* are always there. Only a count per section is kept (`settings.json`; no text), can be forgotten, and stops when the
  arranging is switched off. `Services/SettingsAdvisor.cs`, `UI/SettingsWindow.Adaptive.cs`.
- **My actions** — your own prompt, AI request, agent request or to-do item, started by a keyboard shortcut you choose (or from the tray menu's
  *My actions*). Four kinds: *Add to my to-do list*; *Fill my prompt and show it* (no AI is called; copy the text); *Ask my AI* (shows exactly what
  would be sent and sends only after you press Send, unless you turn that off for that one action); *Run one of my agents*. Placeholders such as
  `{number}`, `{drug}`, `{goal}`, `{forecast}` are filled from the card, and `{selection}` is the text you selected, sent to an AI or agent only
  fenced as data. Up to eight; three starters, none with a shortcut. A press with nothing useful selected asks for an ANDA number instead of doing
  nothing. `Services/MyActions.cs`, `UI/ActionRunner.cs`, `UI/ActionWindow`.
- **My agents** — **Alert Hub**, **Patent Analysis** and **Triaging Agent** are listed, **switched off and without an address**: this app does not know their
  addresses or message format and does not guess, so each needs the details your IT team gives you (address, token header, the *name* of the
  environment variable that holds the token, request body, reply path). Your own agents use the same form (up to eight). An agent is asked only
  when you press Run on a card or use its action, unless you set it to run by itself: when a full card opens, or only for an at-risk ANDA. A
  peek never asks an agent, and an automatic agent cannot use `{selection}`. Its answer appears in the card's *Agent findings* drawer as the
  agent's own words (plain text, never run, never passed on); an item joins your to-do list only when you press its button. A restored backup
  brings every agent back switched off. `Services/Agents.cs`.
- **ANDA to-do list** — *ANDA to-do list…* in the tray menu: your own items plus items from your data (at-risk ANDAs, weekend and holiday dates, action
  items), by the same rules as the card and the watch list, worst first. Finish, snooze or delete; ticking off an item from your data keeps it done
  until the situation changes. It refreshes itself only while it is open (every 15 s, 30 s, 1 min or 5 min, or never) and at once when the list
  changes; a changed portfolio file is read at the next refresh; nothing runs when it is closed. It is kept in `todo.json`, plain text on this PC, and
  is never sent anywhere. `Services/Todo.cs`, `UI/TodoWindow`.
- **My to-do list** — a second, separate mode for your daily workload (any task, ANDA related or not), apart from the ANDA to-do list (which is built from your ANDA data): its own shortcut (chosen in Settings; **Ctrl+Alt+Shift+L** at
  first; *None* is allowed, and the tray menu still opens it), its own windows and its own file (`mytodo.json`, plain text on this PC, never sent anywhere).
  **Notes to my to-do list**: select rough notes anywhere and press the shortcut (or paste them) and they are read **by plain rules, with no AI**
  (`Services/NoteParser.cs`) into suggested items: a tidy title (filler such as "don't forget to" removed), a due day ("tomorrow", "by Friday", "Nov 12", "the 1st"),
  *urgent* ("asap", "!!"), a repeat ("every Friday", "monthly on the 1st"), Work / Personal / Other and an ANDA number kept so its card can be opened. A remark that
  is not a task is offered but not ticked. Each suggestion is editable, and **nothing is added until you press Add** (or, if you chose *Add them at once* for the shortcut,
  all task-like lines are added with an **Undo** for that batch). **My to-do list** groups the list as overdue, today, the next seven days and anytime, says what to
  start with, counts what is urgent, shows how today is going, lets you quick-add by typing a sentence (it shows what it understood first), edit, move to tomorrow, delete (with Undo), filter by
  Work/Personal/Other, move overdue items to today and clear finished ones. Items that repeat come back by themselves; a missed repeating item moves to its next day
  instead of piling up. It refreshes itself only while open, and when the date changes. Limits: 500 items, 200 characters each; notes up to 6,000 characters, 30 suggestions.
  `Services/MyTodo.cs`, `UI/MyTodoWindow`, `UI/NotesWindow`, `UI/ItemFields.cs`, `App.MyTodo.cs`.
- **Compact boxes (L–O)** — a Strip, a Tile, a Ticket and a Badge, about 340 × 63–81, 196 × 123–145,
  300 × 64–68 and 150–185 × 38 px (measured on Windows), so a card never blankets the email being read. They make **no AI call**,
  **never take keyboard focus** (typing in the email continues undisturbed), and open their sibling
  look's full card on a click or when the same hotkey is pressed again on the same number.
- **Auto-hide** — a card or box hides itself after the chosen idle time. It never hides while the
  pointer is over it, while it is pinned, or while the drawer is open (`UI/AutoHide.cs`, unit-tested
  with a fake clock).
- **Prompt Library** — users store and run their own named lenses that steer the
  brief's emphasis ("Facility focus", "Director summary", "First-cycle watch" ship as
  starters). Lenses are guidance, never authority — grounding rules always outrank
  them (see ../SECURITY.md).
- **Global hotkeys** — `Ctrl+Alt+A`: peek at the current text selection (Select & Peek);
  `Ctrl+Alt+Space`: lookup window; `Ctrl+Alt+Shift+R`: review the selected draft; `Ctrl+Alt+Shift+P`: ask about a selection. All four are
  **rebindable in Settings** (at least two modifiers including Ctrl or Alt; letters, digits, F1–F12 or
  Space; never the Windows key; no two the same). The hotkey is always the consent act: a card opens
  only after your own selection and shortcut. Registration is **checked**: if another program owns a
  combination you get a plain-language notice with the fallback, and the tray menu shows which
  hotkeys are live.
- **Consent, with the urgent rule** — default **Whisper**: a small invite you click. If the ANDA is
  urgent (a "Bad" attention item, a forecast 7 or more days past goal (3/7/14 selectable), or a
  goal date that already passed) and the rule is on, the invite is skipped and a **peek** opens
  (no AI call). `Services/UrgencyPolicy.cs`, unit-tested.
- **Weekend and federal-holiday watch** — `Services/BusinessCalendar.cs` computes the U.S. federal
  holidays from the statutory rules (Saturday observed Friday, Sunday observed Monday; Inauguration
  Day is regional) and is checked against OPM's published 2026–2027 dates in the tests.
  `Services/WatchRules.cs` turns that into flags: a goal or forecast date on a weekend or holiday (with
  the nearest business days before and after), within 3 business days of a holiday (0/2/3/5
  selectable), a goal that already passed or is within 5 days, and stale or missing data. One
  **WATCH** line on the card, the full list with each flag's evidence and source in the drawer.
  It states what the data shows and says "this tool does not decide": it never claims how GDUFA treats
  such a date. Closures declared by executive order or a funding lapse are not predictable: add them
  to `closures.txt` in the data folder (`2025-12-24 Executive order closing`).
- **Review a draft** — `Ctrl+Alt+Shift+R` or tray → *Review a draft…* opens a window with the selected text
  and checks it **at once, with no AI and no network**: the ANDA numbers it names, any goal or forecast
  date it gives, and on-track or late wording, against the record; then the draft against itself and the
  calendar (a weekday that does not belong to its date, a deadline on a weekend, a federal holiday or in the
  past, two different goal dates for one ANDA, a placeholder left in, a word typed twice, an ANDA number with
  the wrong number of digits). It lists differences and says exactly what it checked and did not: a clean
  result means none of those checks found a problem, not that the draft is correct. Spelling, grammar and
  other wording need an approved AI connection (Proofread, Rewrite, Fact-check, each with a preview before
  anything is sent). `Services/DraftCheck.cs`, `Services/DraftReview.cs`.
- **Ask about a selection** — select text or spreadsheet cells anywhere and press `Ctrl+Alt+Shift+P` (or tray → *Ask about a selection…*). The
  window shows what was selected, says in words what it looks like (one ANDA number, a table of rows and columns, a list, text) and offers **one
  suggested button** that fits (*Find duplicates*, *Look it up* or *Review as a draft*); nothing runs until a button is pressed. **Quick checks run on this PC
  with fixed rules, with no AI and nothing sent:** *Describe*, *Find duplicates*, *Find blanks*, *Totals* and *Compare two columns*. Each says what it found and how
  much it looked at, and a result can be copied. Below that, any of your **stored prompts** (Settings, *Ask: my stored prompts*) or an **instruction typed
  there** is filled in with the selection and **shown in a window first**; an *Ask my AI* prompt is sent only if you press Send on that preview, and only to
  the connection set up in Settings. A prompt without `{selection}` gets the selection added at the end. A table or list holds many ANDAs, so none is
  picked as "the" record and details such as `{number}` stay empty (the window says so). The selection is read the way Select & Peek reads it (a copy,
  then your clipboard is put back; nothing in terminals) and is never kept. Limits: cells are read from the tab-separated text that Excel puts on the
  clipboard, up to 20,000 characters, 1,000 rows and 30 columns; **not tried against real Excel**. `Services/SelectionHelpers.cs`, `UI/AskWindow.*`, `App.Ask.cs`.
- **The focused edition** — the product opens with what the supervisor asked for: **ANDA lookup** and **reviewing a draft**, plus **Ask**. Hidden until
  *Settings, Maintenance, Show all tools* is ticked: the two to-do lists (and their shortcut), agents and the dates section. The looks, the transparency slider and
  the tray menu's *Appearance* list are in both editions (since round 19: the owner asked for the looks back in Settings). Nothing is deleted, and what was chosen comes back when all tools are shown. There is no *Getting started* card, and the demo has no list of steps. `Edition` in `Services/SettingsService.cs`.
- **Writing actions (Proofread, Rewrite, Fact-check)** — buttons in the draft window, **off until an
  approved model is connected**. Each shows a **preview of the exact text that will be sent** (your draft,
  the matching records for Fact-check, your standing instructions and chosen lens) and sends nothing
  until you press Send. The result is **Copy only**: the app never pastes, replaces or sends for you.
  The draft is fenced as data and the instructions cannot change facts. `Services/WritingActions.cs`;
  tested with a fake provider only: **it has never been run against a live model**.
- **My data** — Settings → *My data*: point to your own **Excel (.xlsx) or CSV** portfolio, map columns
  (the app suggests them from the header names; you can override), and a **notes folder**
  (.txt, .md, .docx). Read-only and local; Excel can stay open. Unknown values show as "—", never an
  estimate. Hard limits on file size, rows and columns; no macros, formulas or external links are
  read. `Services/Portfolio/`. Tested on generated sample files, **not on a real RPM workbook**.
- **Reference dates (RLD labeling, PSG)** — the card **always shows two lines**, PSG and RLD LABEL: a date with where it came from, or a brief
  reason there is none ("not available: …"). The fictional demo records carry made-up dates labeled "fictional demo date". **RLD labeling** is the
  most recent labeling approval date in a Drugs@FDA data file *you* download and point to, **or fetch with Update now** from an https address you paste
  (only when you press it, kept only if it reads as a real Drugs@FDA file; this build does not know or guess FDA's address).
  **PSG** is shown only if your own sheet has a PSG date column, labeled "from your sheet, not an
  official source". No official PSG source is connected, so there is no live PSG date. If a lookup fails it says "not available" and
  never guesses. **The Drugs@FDA file layout the parser expects has not been checked against a real
  download** (FDA's sites were not reachable from the build environment), so treat RLD as unverified
  until someone opens a real file. `Services/ReferenceData.cs`.
- **Other ways to start it** — Settings → *Shortcuts* shows a live line per shortcut (active, or "already used by another program" with what to do), so a
  shortcut another program owns is never a mystery. Settings → *Other ways to start it*: an optional **middle-click** on selected text (Windows mouse hook,
  **off by default**, a chosen key held, Ctrl by default; it never swallows the click). `Services/MouseTrigger.cs`; the rule is unit-tested and the real hook is
  exercised by the Windows self-test.
- **Is it connected?** — a pill on every card, a banner at the top of Settings and the tray tooltip say **OFFLINE · no API connected**, **set up, not tested**,
  **CONNECTED** (only after a request really succeeded) or **NOT REACHABLE**. `Services/ConnectionStatus.cs`.
- **My API** — Settings → *Connection*: choose Auto, Offline only, Claude or **My API**, a provider described in Settings (address, token header, the NAME of the
  environment variable holding the token, model, request body template, reply path, timeout) instead of in code. https only; the token is never stored; errors never
  show the reply. `Services/CustomApi.cs`. Tested against a fake server only; **never run against a real service**.
- **Claude model** — Settings → *Connection* has a model-name box (default `claude-opus-4-8`, checked against Anthropic's list on 2026-10-06; a newer
  model exists). Change it when your agency approves another; an invalid name falls back to the default. `Services/SettingsService.cs`.
- **Outlook (optional, read-only, off by default)** — Settings → *Outlook*: the Application (client) ID your organization registered, your directory, and
  Microsoft 365 or US Government. **Sign in** uses Microsoft's device sign-in (you type a short code on Microsoft's own page; the app never sees your
  password). The token lives in memory only and ends with the app or **Sign out**. An open card's *Recent Outlook mail* drawer searches only for that
  card's six-digit number (`Mail.Read`, never a write or send permission) and shows sender, subject, date and a short preview as plain text. Nothing is
  saved or sent to any AI service. `Services/Outlook.cs`. Written from Microsoft's public documentation and tested against a stand-in server only;
  **never run against Microsoft**. It needs your organization to register an app and allow mail access.
- **Back up, restore, reset** — Settings → *Back up, restore, reset*: save your settings, lenses and quick replies to a file, restore them on another PC,
  or reset to a new install. A backup holds no secret and no file location, and a damaged or newer file is refused without changing anything.
  `Services/SettingsBackup.cs`.
- **My writing** — Settings → *My writing*: tone, style, sign-off, and **quick replies** (templates the card fills in from its record with no AI; the Quick replies
  button or the R key copies one). `Services/QuickReplies.cs`.
- **Watch list** — tray → *Watch list…*: every ANDA in your data (the demo records, or your portfolio) that has something worth a look, worst
  first, with the same rules the card uses. Computed when you open it; nothing runs in the background. Notes (a date near a holiday, no goal date) are
  hidden until you ask. Click a row to open its card. `Services/WatchList.cs`.
- **Edge extension, together with this app** — the same exe is the browser extension's native-messaging host: when Edge starts it with an
  extension origin as the first argument it answers on stdin/stdout (no window, no tray, read-only, no network) with the cards, draft checks,
  settings and watch list built from **your** portfolio file and settings, so the extension and the app show the same facts. Settings,
  *Edge extension*, **Connect** writes a manifest and a current-user registry value that allows exactly one extension ID; **Disconnect** removes
  them. `Services/NativeHost.cs`, `NativeHostRegistration.cs`, `CardModel.cs`. The extension itself is in `../extension/` and also works
  without this app; see its README.
- **Usage log (optional, off by default)** — a local CSV of `date,event,seconds`, from a fixed list of
  event names. It never contains an ANDA number, text, a name or a path. `Services/UsageLog.cs`.
- **Select & Peek, safely** — it reads the selection by sending Ctrl+C, and every step exists
  because the naive version hurts someone: terminals are skipped (Ctrl+C there is an interrupt);
  it waits for you to release Ctrl+Alt so the target sees Ctrl+C; success is detected by the
  clipboard sequence number, so a selection equal to your clipboard still works; your clipboard is
  snapshotted in every plain-memory format and put back (oversized clipboards are refused, not
  half-saved); it runs off the UI thread. Settings can turn it off entirely, leaving the lookup box.
  Known limits: elevated (run-as-administrator) windows ignore injected keys; on keyboard layouts
  where Ctrl+Alt is AltGr the hotkey may type a character; and for a moment the copied selection is on the
  clipboard, so Windows clipboard history and clipboard-monitoring tools can see it. UI Automation capture (no keystroke) is the
  planned replacement.
- **The card** — forecast-first clock (goal vs. forecast + delta + confidence), single
  needs-attention line, latest event, AHEAD anticipation line, the WATCH line, the RLD/PSG lines when
  there is something to show, companion brief, expandable
  drawer (watch list, pending items, notes, forecast drivers, sister ANDAs), pin (`Space`),
  expand (`E`), copy brief (`C`), `Esc` to close.
- **Companion brief** — provider-pluggable via the `IChatCompleter` seam
  (`Services/BriefProvider.cs`). Today: the official Anthropic SDK (the model named in Settings, `claude-opus-4-8` by default)
  when `ANTHROPIC_API_KEY` is set; otherwise a deterministic offline brief. **Elsa is a wired
  placeholder** (`Services/Elsa.cs`, `Services/ElsaMapping.cs`): provider selection
  (`ANDA_COMPANION_PROVIDER=elsa`), configuration from `ANDA_ELSA_*` environment variables, an
  https-only guard, timeout, error handling, offline fallback and the Settings status line are
  done and tested; the only code left is the two translations in `ElsaMapping.cs` (request and
  reply), because Elsa's API shape is not known and is not guessed. MyTodo:
  `../ELSA-INTEGRATION.md`. Grounding rules, fencing, fallback and UI are untouched by a backend
  swap. Facts and the forecast number never come from any model.
- **GDUFA-versioned goal-date rules** — `Services/GoalDateRules.cs` encodes the
  amendment→goal-date logic behind an `IGoalDateRules` interface: `Gdufa3Rules` (from
  the Sept 2024 amendments guidance + GDUFA III commitment letter; only source-confirmed
  tiers are filled, unencoded tiers fail loudly) and a `Gdufa4Rules` placeholder that
  activates when the FY2028–2032 commitment letter is published.
- **Consent stage-one surfaces** — whisper invite chip (self-fades in 3 s) and the
  compact peek card (no AI call until expanded).

- **Placement** — cards, notices and invites are placed from their measured size: below-right of
  the cursor, flipped at an edge, clamped to the work area. A card taller than the screen scrolls.
- **Recognizer** — six digits, optionally prefixed `ANDA`. A number labeled NDA, BLA, IND, DMF, MF
  or NDC is refused, because those are also six digits and showing the wrong card is worse than none.
  This holds even when the digits follow the label with no space (`NDA No123456`). Only ASCII digits 0 to 9
  count, so a full-width or other non-ASCII digit string is never read as a number. The extension and the browser
  simulation use the same rule, and a shared fixture (`extension/tests/golden.json`) keeps the three in step.
- **Accessibility** — every text ink meets WCAG AA (4.5:1) on every appearance, including the tinted header bands, seals and chips some looks add, enforced by tests. Compact boxes are mouse-and-hotkey only by design (they never take focus); the full card they open is keyboard-complete.

Not built: a right-click item inside desktop programs (Windows does not allow it; the Edge extension provides it in Edge pages),
an official PSG source, and a live Elsa or Claude call. Planned next: UI Automation text-under-cursor
capture (slots in behind `TextCaptureService` without touching callers), real data adapters, MSIX
packaging, code signing. See `../ROADMAP.md` for what is built and what is not.

## Build & run

```powershell
# on Windows (run)
dotnet run --project AndaCompanion

# publish a single-file exe (what CI does)
dotnet publish AndaCompanion -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

The project also cross-compiles from Linux/macOS (`dotnet build`).

**Portable mode.** `ANDA_COMPANION_HOME=<folder>` moves every file the app writes (settings, prompts,
crash log) into that folder. `Start-Demo.bat` uses it, so the demo never touches your profile.

## Tests

```bash
dotnet test AndaCompanion.Tests                              # everything (Windows-only tests skip elsewhere)
dotnet test AndaCompanion.Tests --filter "Category!=Windows" # what runs on Linux/macOS
```

762 cross-platform tests at the time of writing (recognizer, status line, GDUFA rules, the federal-holiday
calendar against OPM's dates, watch rules, urgency, draft check, portfolio and notes readers, writing prompts,
hotkey parsing and registration with a fake API, brief fencing and fallback, provider selection, settings,
contrast, placement, the to-do list, custom actions, the agent connector, headless renders and interaction tests of the real windows) plus Windows-only tests that use real Win32 (clipboard snapshot/restore, hotkey registration and
conflict, owner window). Tests never touch the real `%AppData%`.

## Self-test — evidence from the machine it runs on

```powershell
AndaCompanion.exe --selftest report
```

Renders every window to PNG, then exercises the Windows integration for real and writes
`report\report.md` and `report.json`: hotkeys registered and delivered (it presses them), tray icon,
Segoe UI present, clipboard snapshot/restore, card placement at five screen anchors, and an end-to-end
Select & Peek from a real selection. It also renders the to-do list, an action window and the card's agents drawer, using canned text, so no agent or AI is contacted. Each check is PASS, WARN (this machine needs attention), SKIP
(could not be tested, with the reason) or FAIL. It is hermetic (own data folder) and makes no network
calls. Your clipboard is restored afterwards. Do not type or click while it runs.

## Continuous verification on a real Windows machine

`.github/workflows/anda-companion-windows.yml` builds, runs all tests, publishes the single-file exe,
runs `--selftest` on the Windows runner and uploads the demo bundle, the self-test report with its
screenshots, and the test results. Outcomes and their limits are recorded in `../VERIFICATION.md`.

## Preview harness (no Windows box needed)

Renders the real card in all fifteen full looks, and the four compact boxes, to PNGs via Avalonia's headless platform:

```bash
dotnet run --project AndaCompanion.Preview -- ./shots
```

Linux renders use fallback fonts, not Segoe UI. Treat them as layout evidence; the Windows self-test
screenshots are the typography evidence.

## Claude configuration

Set `ANTHROPIC_API_KEY` to enable generated briefs. The request uses the official SDK and the model named in
Settings, `claude-opus-4-8` by default (it matched Anthropic's model list when last checked, 2026-10-06, and a newer
model exists). **It has not been run against the live API** (no key in the build environment). Moving to a newer model needs a larger token budget and an
effort setting, because newer models always think and thinking tokens count toward the limit; the
deterministic offline brief covers any failure. For any real FDA deployment this
must instead point at an agency-approved channel (e.g. Claude on Amazon Bedrock in
GovCloud) — see DESIGN.md §7 — and the data layer must be a sanctioned extract. The
shipped build is offline-first by design.

## Layout

```
AndaCompanion/
  App.axaml(.cs)        tray icon, menus, hotkey→consent→card flow
  Theming/              15 light-only looks: palettes + structural signatures (USWDS-anchored)
  UI/CardWindow.*       the peek card (all slots, mini/full, pin/expand)
  UI/CompactCardWindow  the compact boxes L–O (no AI call, never focused)
  UI/AutoHide.cs        the auto-hide rule (pure) and its timer
  UI/ChipWindow.*       whisper invite + invalid-number notice
  UI/LookupWindow.*     Ctrl+Alt+Space lookup
  UI/DraftWindow.*      Ctrl+Alt+Shift+R review a draft and the writing actions (preview before send, Copy only)
  UI/AskWindow.*        Ctrl+Alt+Shift+P ask about a selection: quick checks, stored prompts, an instruction typed there (App.Ask.cs opens it)
  UI/SettingsWindow.*   appearance, consent, urgent rule, shortcuts and other ways to start it, My data, My writing, connection and My API, Outlook, backup, usage log
  UI/QuickRepliesWindow.* the person's own quick replies (templates filled from the card)
  UI/HotkeyCapture.cs   press-a-shortcut box used by the shortcut settings
  UI/WatchListWindow.*  the portfolio-wide watch list
  UI/ProgressControls   bar / ticks / ring / solid / capsule / line / dots / step / timeline / gauge
  Services/             hotkeys (checked, rebindable), text capture + clipboard snapshot, settings, brief services,
                        Elsa placeholder (Elsa.cs + ElsaMapping.cs), BusinessCalendar + WatchRules,
                        UrgencyPolicy, DraftCheck, WritingActions, ReferenceData, UsageLog, WatchList,
                        Outlook (optional read-only mail), SettingsBackup (back up, restore, reset),
                        CardModel + NativeHost + NativeHostRegistration (the Edge extension's host),
                        Portfolio/ (xlsx/csv reader, column detection, notes index), self-test, app paths
  Data/                 IAndaRepository + fictional mock data + identifier registry
AndaCompanion.Tests/    xUnit suite (cross-platform + Windows-only)
AndaCompanion.Preview/  headless screenshot harness
../demo/                sample memo, launcher, START-HERE and the demo guide (bundled by CI)
../mockup/              the Appearance Lab (appearance-lab.html) and the desktop simulation (windows-simulation.html)
../extension/           the Edge/Chromium extension: standalone or together with this app (own README, tests and golden fixture)
```
