# ANDA Hover Companion — Windows app (v0)

A .NET 8 / Avalonia tray application implementing the design in `../DESIGN.md`.
**All bundled data is fictional.** Not an FDA product; a concept prototype.

## Quick setup (under 5 minutes)

1. `dotnet run --project AndaCompanion` (or double-click the published `AndaCompanion.exe`).
2. First run opens **Settings** automatically: pick an appearance, a consent model,
   and your transparency — a demo card appears alongside so every choice previews live.
3. Optional, the day API access is approved: set `ANTHROPIC_API_KEY` (agency-approved
   channel), press **Test** in Settings. Done — no reinstall, no code change.

## What works today

- **Tray app** — quiet icon, no notifications. Menu: lookup, demo cards, quick
  Appearance switch, **Settings…**, **Prompt library…**. All persisted to
  `%AppData%\AndaCompanion\settings.json`.
- **Settings window** — Appearance (all five, **light-only by product decision**, A
  default), Consent model (Full default = DESIGN.md model ①), **card transparency
  slider** (85–100%; text always stays opaque), companion lens picker, and the
  **Claude connection panel** with one-click Test.
- **Prompt Library** — users store and run their own named lenses that steer the
  brief's emphasis ("Facility focus", "Director summary", "First-cycle watch" ship as
  starters). Lenses are guidance, never authority — grounding rules always outrank
  them (see ../SECURITY.md).
- **Global hotkeys** — `Ctrl+Alt+A`: peek at the current text selection (Select & Peek);
  `Ctrl+Alt+Space`: lookup window. The hotkey is always the consent act — nothing ever
  opens on its own.
- **The card** — forecast-first clock (goal vs. forecast + delta + confidence), single
  needs-attention line, latest event, AHEAD anticipation line, companion brief, expandable
  drawer (pending items, notes, forecast drivers, sister ANDAs), pin (`Space`),
  expand (`E`), copy brief (`C`), `Esc` to close.
- **Companion brief** — via the official Anthropic SDK (`claude-opus-4-8`) when
  `ANTHROPIC_API_KEY` is set; otherwise a deterministic offline brief assembled from the
  record. Facts and the forecast number never come from the model.
- **Consent stage-one surfaces** — whisper invite chip (self-fades in 3 s) and the
  compact peek card (no AI call until expanded).

Planned next: UI Automation text-under-cursor capture (slots in behind
`TextCaptureService` without touching callers), real data adapters, MSIX packaging.

## Build & run

```powershell
# on Windows (run)
dotnet run --project AndaCompanion

# publish a single-file exe
dotnet publish AndaCompanion -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The project also cross-compiles from Linux/macOS (`dotnet build`), which is how CI and
the preview harness work.

## Preview harness (no Windows box needed)

Renders the real card in every appearance to PNGs via Avalonia's headless platform:

```bash
dotnet run --project AndaCompanion.Preview -- ./shots
```

## Claude configuration

Set `ANTHROPIC_API_KEY` to enable generated briefs. For any real FDA deployment this
must instead point at an agency-approved channel (e.g. Claude on Amazon Bedrock in
GovCloud) — see DESIGN.md §7 — and the data layer must be a sanctioned extract. The
shipped build is offline-first by design.

## Layout

```
AndaCompanion/
  App.axaml(.cs)        tray icon, menus, hotkey→consent→card flow
  Theming/              5 light-only appearance palettes (USWDS-anchored)
  UI/CardWindow.*       the peek card (all slots, mini/full, pin/expand)
  UI/ChipWindow.*       whisper invite + invalid-number notice
  UI/LookupWindow.*     Ctrl+Alt+Space lookup
  UI/ProgressControls   bar / ticks / ring / hatch / capsule + D's grid surface
  Services/             hotkeys, text capture, settings, brief services (offline + Anthropic)
  Data/                 IAndaRepository + fictional mock data
AndaCompanion.Preview/  headless screenshot harness
```
