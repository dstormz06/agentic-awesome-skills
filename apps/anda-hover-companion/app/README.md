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
3. Optional, the day API access is approved: set `ANTHROPIC_API_KEY` (agency-approved
   channel), press **Test** in Settings. Done — no reinstall, no code change. For **Elsa**, follow
   `../ELSA-INTEGRATION.md`: the app is already wired for it; two small methods remain to be written
   when Elsa's API is available.

## What works today

- **Tray app** — quiet icon, no notifications. Menu: lookup, demo cards, quick
  Appearance switch, **Settings…**, **Prompt library…**. All persisted to
  `%AppData%\AndaCompanion\settings.json`.
- **Settings window** — Appearance (fifteen looks, **light-only by product decision**; **K ·
  Graphite Quiet is the default**): eleven full cards (A–K) and four **compact boxes** (L–O),
  Auto-hide (Off / 10 / 20 / 40 s, default 20), Consent model (Full default = DESIGN.md model ①),
  **card transparency slider** (85–100%; text always stays opaque), companion lens picker, and the
  **AI connection panel** (Claude or Elsa) with one-click Test.
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
  `Ctrl+Alt+Space`: lookup window. The hotkey is always the consent act — nothing ever
  opens on its own. Registration is **checked**: if another program owns a combination you get a
  plain-language notice with the fallback, and the tray menu shows which hotkeys are live.
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
  needs-attention line, latest event, AHEAD anticipation line, companion brief, expandable
  drawer (pending items, notes, forecast drivers, sister ANDAs), pin (`Space`),
  expand (`E`), copy brief (`C`), `Esc` to close.
- **Companion brief** — provider-pluggable via the `IChatCompleter` seam
  (`Services/BriefProvider.cs`). Today: the official Anthropic SDK (`claude-opus-4-8`)
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

Planned next: UI Automation text-under-cursor capture (slots in behind
`TextCaptureService` without touching callers), real data adapters, MSIX packaging, code signing.

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

136 cross-platform tests (recognizer, status line, GDUFA rules, brief fencing and fallback, provider
selection, settings, contrast, placement, hotkey registration with a fake API, headless renders of the real
windows) plus Windows-only tests that use real Win32 (clipboard snapshot/restore, hotkey registration and
conflict, owner window). Tests never touch the real `%AppData%`.

## Self-test — evidence from the machine it runs on

```powershell
AndaCompanion.exe --selftest report
```

Renders every window to PNG, then exercises the Windows integration for real and writes
`report
eport.md` and `report.json`: hotkeys registered and delivered (it presses them), tray icon,
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

Set `ANTHROPIC_API_KEY` to enable generated briefs. The request uses the official SDK and the
`claude-opus-4-8` model ID, which matches Anthropic's current model list; **it has not been run against the
live API** (no key in the build environment). Moving to a newer model needs a larger token budget and an
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
  UI/ProgressControls   bar / ticks / ring / hatch / capsule / line / dots / step / timeline + D's grid surface
  Services/             hotkeys (checked), text capture + clipboard snapshot, settings, brief services,
                        Elsa placeholder (Elsa.cs + ElsaMapping.cs), self-test, app paths (portable mode)
  Data/                 IAndaRepository + fictional mock data + identifier registry
AndaCompanion.Tests/    xUnit suite (cross-platform + Windows-only)
AndaCompanion.Preview/  headless screenshot harness
../demo/                sample memo, launcher, START-HERE and the demo guide (bundled by CI)
../mockup/              the Appearance Lab (appearance-lab.html) and the desktop simulation (windows-simulation.html)
```
