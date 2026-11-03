# ANDA Hover Companion — Windows app (v0)

A .NET 8 / Avalonia tray application implementing the design in `../DESIGN.md`.
**All bundled data is fictional.** Not an FDA product; a concept prototype.

## Quick setup (under 5 minutes)

**No .NET needed:** every green run of the *ANDA Companion (Windows)* workflow publishes
`anda-companion-demo-win-x64`: a single self-contained `AndaCompanion.exe` plus a launcher, a sample
memo and a demo guide (`../demo/DEMO.md`). Extract, double-click `Start-Demo.bat`, pin the tray icon.

1. `dotnet run --project AndaCompanion` (or double-click the published `AndaCompanion.exe`).
2. First run opens **Settings** automatically: pick an appearance (the default is **K · Graphite
   Quiet**), a consent model, auto-hide and your transparency — a demo card appears alongside so
   every choice previews live.
3. Optional, the day API access is approved: open **Settings, Connection**. For Claude, set
   `ANTHROPIC_API_KEY` (agency-approved channel), choose Claude, check the model name and press **Test**.
   For any other API, choose **My API** and fill in its address, header, request body and reply path; the token
   stays in an environment variable you name. No reinstall, no code change. For **Elsa**, follow
   `../ELSA-INTEGRATION.md`: until its two small translations are written, My API covers it.

## What works today

- **Tray app** — quiet icon, no notifications. Menu: lookup, demo cards, quick
  Appearance switch, **Settings…**, **Prompt library…**. All persisted to
  `%AppData%\AndaCompanion\settings.json`.
- **Settings window** — Appearance (fifteen looks, **light-only by product decision**; **K ·
  Graphite Quiet is the default**): eleven full cards (A–K) and four **compact boxes** (L–O),
  Auto-hide (Off / 10 / 20 / 40 s, default 10), Consent model (**Whisper invite is the default** for
  new installs; an install that already saved a choice keeps it), **urgent items** (in whisper mode an
  at-risk or late ANDA opens a peek by itself), the weekend/holiday window, **My data**,
  **standing instructions** for the writing actions, **three rebindable shortcuts**, an optional
  **usage log** (off by default), **card transparency slider** (85–100%; text always stays opaque),
  companion lens picker, and the **connection panel** (Auto, Offline, Claude with its model name, or My API) with one-click Test.
  A **Go to** list at the top jumps to any section.
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
  `Ctrl+Alt+Space`: lookup window; `Ctrl+Alt+D`: check the selected draft. All three are
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
- **Check a draft** — `Ctrl+Alt+D` or tray → *Check a draft…* opens a window with the selected text
  and checks it **at once, with no AI and no network**: the ANDA numbers it names, any goal or forecast
  date it gives, and on-track or late wording, against the record. It lists differences and says
  exactly what it checked and did not. `Services/DraftCheck.cs`.
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
  reason there is none ("not available: …"). The fictional demo records carry made-up dates labelled "fictional demo date". **RLD labeling** is the
  most recent labeling approval date in a Drugs@FDA data file *you* download and point to, **or fetch with Update now** from an https address you paste
  (only when you press it, kept only if it reads as a real Drugs@FDA file; this build does not know or guess FDA's address).
  **PSG** is shown only if your own sheet has a PSG date column, labelled "from your sheet, not an
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
  reply), because Elsa's API shape is not known and is not guessed. Checklist:
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
- **Recognizer** — six digits, optionally prefixed `ANDA`. A number labelled NDA, BLA, IND, DMF, MF
  or NDC is refused, because those are also six digits and showing the wrong card is worse than none.
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

557 cross-platform tests at the time of writing (recognizer, status line, GDUFA rules, the federal-holiday
calendar against OPM's dates, watch rules, urgency, draft check, portfolio and notes readers, writing prompts,
hotkey parsing and registration with a fake API, brief fencing and fallback, provider selection, settings,
contrast, placement, headless renders of the real windows) plus Windows-only tests that use real Win32 (clipboard snapshot/restore, hotkey registration and
conflict, owner window). Tests never touch the real `%AppData%`.

## Self-test — evidence from the machine it runs on

```powershell
AndaCompanion.exe --selftest report
```

Renders every window to PNG, then exercises the Windows integration for real and writes
`report\report.md` and `report.json`: hotkeys registered and delivered (it presses them), tray icon,
Segoe UI present, clipboard snapshot/restore, card placement at five screen anchors, and an end-to-end
Select & Peek from a real selection. Each check is PASS, WARN (this machine needs attention), SKIP
(could not be tested, with the reason) or FAIL. It is hermetic (own data folder) and makes no network
calls. Your clipboard is restored afterwards. Do not type or click while it runs.

## Continuous verification on a real Windows machine

`.github/workflows/anda-companion-windows.yml` builds, runs all tests, publishes the single-file exe,
runs `--selftest` on the Windows runner and uploads the demo bundle, the self-test report with its
screenshots, and the test results. Outcomes and their limits are recorded in `../VERIFICATION.md`.

## Preview harness (no Windows box needed)

Renders the real card in all eleven full looks, and the four compact boxes, to PNGs via Avalonia's headless platform:

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
  UI/DraftWindow.*      Ctrl+Alt+D draft check and the writing actions (preview before send, Copy only)
  UI/SettingsWindow.*   appearance, consent, urgent rule, shortcuts and other ways to start it, My data, My writing, connection and My API, Outlook, backup, usage log
  UI/QuickRepliesWindow.* the person's own quick replies (templates filled from the card)
  UI/HotkeyCapture.cs   press-a-shortcut box used by the shortcut settings
  UI/WatchListWindow.*  the portfolio-wide watch list
  UI/ProgressControls   bar / ticks / ring / hatch / capsule / line / dots / step / timeline + D's grid surface
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
