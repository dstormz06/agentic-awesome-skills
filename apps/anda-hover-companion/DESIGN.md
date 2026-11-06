# ANDA Companion (Notula) — UX & Product Design Proposal

**A Windows tray micro-app for FDA OGD Regulatory Project Managers.**
Point at an ANDA number anywhere on screen → get a calm, one-glance briefing card:
status, clock, the one thing that needs you, and a grounded AI brief of risks and blind spots.

> Status: design proposal + interactive mockups (`mockup/appearance-lab.html`, `mockup/windows-simulation.html`). No FDA data,
> systems, or branding are used — all names, numbers, and records in the mockup are fictional.

---

## 1. Design north star

**The RPM's question is never "show me everything about this ANDA." It is:
"what is this, where does it stand, what needs me — and when will it *really* finish?"**

The official goal date is a commitment; the product's primary deliverable is the
**true forecast** — an evidence-based projected action date shown *against* the goal
date, with its delta, its confidence, and its drivers. Everything else on the card
exists to explain or protect that forecast. The app is a **companion**: it watches the
RPM's back — flagging assumptions, patterns, gaps, risks, blind spots, brewing
conflicts, and communication friction — and tries to surface what the RPM will need
*before* they ask. Answered in **under 5 seconds, with zero clicks**; everything else
*available* but never *present* by default.

### The four things that make it a daily lifesaver

1. **A forecast you can say out loud.** Before any meeting, the RPM knows the honest
   projected date, its delta vs. goal, and the drivers — with the tool's published
   calibration to back it up. No more hedge-guessing timelines from memory.
2. **Zero-navigation answers.** Point at a number in Outlook, Excel, or a PDF and know
   status + next action in five seconds. That kills the dozens of daily
   system-of-record round-trips that are the real time sink of the job.
3. **Never blindsided.** The AHEAD line reads the meeting/memo horizon and says what
   will be asked of you before it lands — walking into a huddle already holding the
   answer is the moment this tool wins converts.
4. **Cross-ANDA memory.** Sister-application dot-connecting (same applicant, facility,
   or drug substance) stops depending on the one person who "remembers what happened on
   the sister" — the precedent surfaces itself, cited.

Three principles drive every decision:

1. **Progressive disclosure, three levels.** A glyph → a peek card → an expanded drawer.
   The RPM chooses how deep to go; the app never chooses for them.
2. **One primary signal.** The card surfaces exactly **one** "needs attention" line —
   the most urgent item — not a list of twelve statuses. If nothing needs attention,
   that slot simply doesn't exist (absence is information).
3. **Deterministic facts and AI synthesis never mix.** Dates, statuses, submission
   history — and the forecast *number* itself — render from data and models, not from
   the LLM. Claude writes only the clearly-labeled "companion brief," streamed in after
   the facts are already on screen, with citations back to the notes/submissions it
   drew from.

---

## 2. Activation model — why "hover everywhere" is the wrong default

True global hover (the app watches every mouse movement and pops a card whenever the
cursor crosses six digits) is the highest-cognitive-load version of this idea: it
interrupts work the RPM didn't ask to interrupt, fires false positives on phone numbers
and tracking IDs, and trains the user to fear their own cursor. An app that appears
unbidden is a distraction machine, not a lifesaver.

**The two-stage consent contract.** In every mode, recognition and opening are separate
acts. Stage 1 is always passive or transient (a mark, an invite, a compact peek) and costs
nothing to ignore; stage 2 is always a deliberate act by the user. A full card never
appears without stage 2. The user picks their model in settings — all ship, one default.

**What the Windows app ships (2026-11-02):** three models — Full card, Peek first and **Whisper invite, the default
for new installs** (an install that already saved a choice keeps it). In Whisper mode there is one controlled
exception, switchable in Settings: an **urgent** ANDA (a bad attention item, a forecast late by 7 days or more (3, 7 or 14, as
chosen), or a goal date that has passed) skips the invite and opens a **peek**. It still starts only from the
person's own selection and shortcut, the peek makes no AI call, and typing a number in the lookup box (itself the
consent) opens the full card.

| # | Consent model | Stage 1 (free to ignore) | Stage 2 (opens the card) | Best for |
|---|---|---|---|---|
| ① | **Mark → Tap** *(default)* | Recognized numbers get only a quiet dotted underline + 5 px dot. Nothing moves, nothing pops. | Click / tap the marked number. | Minimum motion; users who want total stillness. |
| ② | **Whisper invite** | Rest the cursor on a number 350 ms (or long-press on touch) → a small pill appears: `● ANDA 216482 · Peek ›`. It self-fades in 3 s if ignored. | Tap the pill. | Users who want an affordance offered, never imposed. |
| ③ | **Peek → Full** | First tap opens only a compact peek: identity + goal clock, ~120 px, no AI call yet. | Second tap or **Full brief ↓** expands to the complete card (and only then is the AI brief generated). | Cautious rollout; also minimizes API spend. |
| ④ | **Long-press (touch, always on)** | On any touch device, in any model, long-press ≈ 450 ms summons the whisper invite. | Tap the invite. | Tablets / phones, where hover doesn't exist. |

Alongside the consent model, the activation *inputs* ship in this order: hotkey
Point & Peek (hover + double-`Ctrl`), Select & Peek (selection + hotkey, covers
Citrix/PDF/OCR), allow-listed dwell recognition per app (off by default), and tray
search (`Ctrl+Alt+Space`) as the path that always works.

**Validation before anything renders:** captured text is matched against
`^(?:ANDA[-\s#]?)?(\d{6})$`, then checked against a locally cached index of real ANDA
numbers. Invalid → in manual modes, a 1-line inline whisper ("Please enter a six-digit ANDA number." in the lookup box; "<number> is not in the available records." for a well-formed number with no record);
in dwell mode, **nothing at all** (silence beats a false alarm).

---

## 3. The three disclosure levels

### Level 0 — the glyph (dwell mode only)
A 16 px dotted underline + tiny badge under the number. It says only "I recognize this;
ask me if you want." Costs ~0 attention; dismisses itself when the cursor leaves.

### Level 1 — the Peek Card (the product)
A single ~360 px-wide card, anchored near the cursor, that answers the four questions
top-to-bottom in a **fixed slot order** — the RPM's eye learns positions, so reading
becomes recognition, not scanning:

```
┌──────────────────────────────────────────────┐
│ ANDA 216482  ✓ Verified          [pin][exp]  │  ← identity + trust, actions appear on hover only
│ Metformin HCl ER Tablets, 500 mg             │
│ Meridian Pharmaceuticals · Cycle 2           │
├──────────────────────────────────────────────┤
│ GOAL      Oct 3, 2026 · 80 days  ▓▓▓▓▓░░ 62% │  ← the commitment: one date, one thin bar
│ FORECAST  Oct 17 · ▲ +14 d vs goal · moderate│  ← THE headline: the true forecast + delta
├──────────────────────────────────────────────┤
│ ⚠ Needs you · IR response due in 6 days      │  ← ONE line, only if something needs attention
├──────────────────────────────────────────────┤
│ Latest: Amendment (minor) received Jun 30    │  ← most recent event, one line
├──────────────────────────────────────────────┤
│ AHEAD  Thu huddle — status requested on this │  ← anticipation: what lands on the RPM
│        ANDA + sister (memo, Jul 14)          │    next (only when ≤ 14 days out)
├──────────────────────────────────────────────┤
│ COMPANION · generated — verify   [sources]   │  ← streams in ~1–2 s, never blocks the card
│ Forecast runs 14 days past goal: applicant's │
│ last two IR responses averaged 31 days vs a  │
│ 21-day ask. Untested assumption: "no new     │
│ facility inspection."                        │
├──────────────────────────────────────────────┤
│ [Sister ANDA ·1] [Notes ·3] [Open in system↗]│  ← chips: doors, not content
└──────────────────────────────────────────────┘
```

Hard constraints that keep the card calm:

- **Fixed anatomy.** Every card, every ANDA, same slots, same positions. Slots with
  nothing to say collapse (no "None", no empty sections).
- **Latency budget:** card shell + cached facts in **<150 ms**; AI brief streams into
  its own slot afterward with a subtle shimmer. The user is never staring at a spinner.
- **~10 lines max.** If a fact doesn't change what the RPM does next, it lives in Level 2.
- **No charts.** The only graphic is one 4 px progress bar. Numbers are text.
- **Color is status-only, never color-alone.** Amber/red lines always carry an icon +
  words; everything else is neutral ink. (Status hues reserved per accessibility rules;
  works in high-contrast mode.)
- **Dismissal is free.** Card closes on `Esc`, on click-outside, or when the cursor
  leaves card + trigger area (300 ms grace). **Pin** (`Space` or 📌) keeps it up and
  makes it draggable for side-by-side work.

### Level 2 — the expanded drawer
`E` or the expand icon grows the same card in place (never a new window) to ~480 px with
collapsed accordion sections, opened one at a time:

- **Pending & urgent items** — the full list behind the one-line signal (IRs, DRLs,
  amendments in queue, inspection holds), each with owner discipline + due date.
- **Recent project notes** — last 3, verbatim, newest first, author + date.
- **Forecast & assumptions** — progress %, projected action date, and the explicit
  assumptions behind them ("assumes no new facility inspection needed").
- **Sister / related ANDAs** — same applicant or same drug substance; each row is
  itself hoverable → opens its own peek card (the app becomes self-similar).
- **Timeline** — submission history, most recent first.

Everything in Level 2 has a **copy** affordance (RPMs paste into emails constantly) and
a deep link to the record in the system of record.

---

## 4. States (design them or they design you)

| State | Behavior |
|---|---|
| Verified, healthy | Green ✓ chip; no attention slot; card is short. |
| Verified, at risk | Amber/red attention line (one), sorted by urgency: overdue > due ≤7 d > blocked > FYI. |
| Not a valid ANDA | Manual modes: one-line whisper. Dwell mode: silence. |
| Data stale / source offline | Facts render from cache with a quiet `cached · 2 h ago` badge; AI brief disabled with "needs fresh data" note rather than summarizing stale data. |
| AI unavailable / declined | Facts still render fully — the card degrades to a great deterministic tooltip. AI slot shows a one-line retry. |
| No access to this ANDA | "You don't have access to 21xxxx" — never partial data. |

---

## 5. Visual language

**Appearance is a preference, never information.** The Windows app ships with **nineteen selectable looks** today
(Settings → Appearance): fifteen full cards (A–K and P–S) and four compact boxes (L–O). **Round 18 adds three more, T to V,
in the simulation and the lab: 22 looks in all, 18 full cards and four compact boxes.** **No look draws a grid, hatching, texture or
other pattern behind its content** (the owner's decision, 2026-11-03). **In the Windows app, K · Graphite Quiet is still the default**
(changed from A on 2026-11-02 by the product owner). **In the simulation and the lab the default is T · Pocket Plain**, chosen by the
owner on 2026-11-06; the Windows app takes it when the new design is ported (§11b). All full looks share identical anatomy, slot
order, status semantics, and interactions — switching appearance never moves, adds,
or removes information:

| | Appearance | Character | Progress form |
|---|---|---|---|
| A | Quiet Fluent | Native Windows calm — white surface, steel-blue accent, system type | Thin 4 px bar |
| B | Docket Ledger | Typeset federal docket — warm paper, archival green, serif title, stamp seal | Milestone ticks |
| C | Signal | Light steel command panel — cool sky surface, federal blue, mono numerals | Ring |
| D | Draft | Engineer's drawing — federal navy ink on plain white paper, drafting mono, square corners | Solid square bar |
| E | Instrument | Medical-device porcelain — slate steel on clinical white, capsule geometry, soft large radii | Capsule |
| F | Agency Bronze | Warm greige and bronze, a solid 5 px bronze band across the top, plain header, filled tag chips (an agency-tool feel; no logo, name or pattern borrowed) | Thin bar |
| G | Harbor Teal | Cool teal with a **status spine** down the left edge (amber = late, green = on track); compact spacing | Thin bar |
| H | Sage Clinical | Soft sage band, generous radii and spacing; the calmest | Ten dots |
| I | Indigo Dossier | A formal case file — double rule, serif name and labels | Five-step stepper |
| J | Cobalt Timeline | Gold top rule; goal against forecast drawn on one runway, so the gap is the picture | Timeline |
| **K** | **Graphite Quiet** *(app default until the port)* | Monochrome in soft slate (accent `#56626E`, not near-black) — color appears only where status needs it; outlined attention, text-only chips; quiet in a meeting, sharp on a projector | Line with a dot |
| L | Graphite Strip | Compact (about 340 × 63–81 px): number, goal, forecast, status edge | — |
| M | Fluent Tile | Compact (about 196 × 123–145 px): the forecast gap in large type, one attention line | — |
| N | Bronze Ticket | Compact (about 300 × 64–68 px): a slim ticket with a stub | — |
| O | Teal Badge | Compact (about 150–185 × 38 px): a status pill with the number and the gap | — |
| P | Bronze Gauge | Bronze palette; soft round corners, pill-shaped warnings, a header wash | Half dial |
| Q | Bronze Folder | Bronze palette on warm paper; the number on a bronze tab, a plain edge, underlined chips | Line with a dot |
| R | Bronze Rail | Bronze palette; a tinted column carries the days to goal in large figures, the detail reads down the right | Solid square bar |
| S | Bronze Tag | Bronze palette; an ID-tag head (a small slot, centered), goal and forecast in two columns | Thin bar |
| T | Pocket Plain | *Round 18; simulation and lab only; the default there.* White card, soft slate ink, one amber accent. One answer first: how many days later or earlier than the goal, with its confidence and source; then the facts; one main action, **Copy status**, pinned at the bottom | Thin bar |
| U | Calendar Ribbon | *Round 18; simulation and lab only.* The same answer, with the weeks around the goal and the forecast drawn out: weekends, a federal holiday and the gap. Always has a written twin of the ribbon for screen readers and for the copy | Thin bar |
| V | Glass Sheet | *Round 18; simulation and lab only.* The same layout on a frosted sheet. At least 86% opaque, and solid when Windows asks for less transparency | Thin bar |

**Compact boxes (L–O).** They exist so the tool never blankets the email being read or
written. A compact box borrows a full look's palette (K, A, F, G respectively), shows only
facts already on the record, **makes no AI call**, and **never takes keyboard focus**, so typing in the
email continues undisturbed and a stray Enter cannot expand it. The deliberate second act is a
click, or pressing the same hotkey again on the same number; either opens the sibling look's
full card. Trade-off, stated plainly: because a compact box is not focused it is a pointer-and-hotkey surface;
the full card it opens is the keyboard-complete one.

**Auto-hide.** A card or compact box hides itself after Off / 10 / 20 / 40 s with no pointer or key
activity (default 10 s). It **never** hides while the pointer is over it, while the card is pinned, or
while the drawer is expanded: each is an "I am using this" signal, and taking a card away mid-sentence
is worse than leaving it. The rule is a pure function (`AutoHidePolicy.ShouldHide`) with a fake-clock
test.

**Nothing too dark (product owner, 2026-11-02).** Beyond the light-only rule, no accent or progress fill may be near-black (a test enforces a minimum luminance on every look's accent and fill), and every progress fill must stay at least 3:1 against its track. Text inks stay dark enough for AA contrast; that is legibility, not decoration.

**Light-only, by product decision.** All dark variants are retired (C was redesigned
from dark glass into a light steel panel). A per-user **transparency slider** (40–100%, **starting at 92%**,
card surface only — text and status inks always stay opaque) provides the "sits
lightly on my screen" quality dark themes used to serve. At the starting 92% every full look keeps its text at AA over a light
backdrop, and the default look K does so even over a black window (4.96:1); looks A to E and G fall below 4.5:1 over a black window (lowest
3.85:1), and every look does at 80% over black. That is why the hint says to raise the value over a dark or busy background.

**Palette policy.** Accent hues are anchored to the **U.S. Web Design System** federal
palette (e.g. D's navy = USWDS `primary-dark #1a4480`, C's federal blue = USWDS
`primary #005ea2`) so every appearance reads government-professional, and the status colors
(good / warn / bad) keep one consistent semantic family across every look. Within that
conservative range, the appearances differentiate through *material, typography, and
geometry* — paper and serif, grid and mono, glass, porcelain — rather than louder hues.

- **Native-feeling Windows 11:** Mica/acrylic card, 10 px radius, Segoe UI Variable,
  light-only (§5), `prefers-reduced-motion` respected
  (fade 120 ms → no animation).
- **Density:** 13 px base text, 11 px metadata, 4/8 px spacing grid. One weight change
  (semibold) for the identity line and the goal date — nothing else competes.
- **Ink hierarchy, not boxes:** primary / secondary / muted text colors do the layering;
  a single hairline divider between slots; no nested cards, no shadowed sub-panels.
- **The tray icon is silent.** No badges, no toasts, no notifications by default.
  (An optional "morning digest on hover over tray icon" can come later — opt-in.)

Full keyboard path: hotkey → card focused → `E` expand · `Space` pin · `C` copy brief ·
`O` open in system of record · `1–5` open accordion sections · `Esc` close. Screen-reader:
card is a single labeled dialog; slots are a list; AI brief announced as "AI-generated summary."

---

## 6. The true forecast — the product's core deliverable

The forecast is **computed, not vibed**. The projected action date comes from a
deterministic model — historical cycle times for comparable reviews (stage, discipline
mix, applicant response history, facility status) — adjusted by explicitly flagged
risks. It always renders with three companions: the **delta vs. goal** (▲ +14 d /
▼ 9 d ahead, icon + words, never color alone), a **confidence level**, and a **why ›**
door to the drivers and assumptions behind it.

A forecast is only "true" if it's accountable: the app tracks its own calibration
(median |forecast − actual| over trailing months) and shows it in the drivers panel —
"forecasts within ±8 days over the last 6 months." When the RPM can audit the tool's
track record, the forecast earns the right to be the headline.

## 7. The companion doctrine (where Claude fits — and deliberately doesn't)

The companion's job is to watch the RPM's back and connect dots ahead of need. Per
card, Claude scans the structured record, recent notes, correspondence metadata, and
sister-ANDA history for exactly these classes of signal:

- **Assumptions** — unstated or untested premises in the current plan ("assumes no new
  facility inspection"), surfaced verbatim so they can be challenged.
- **Patterns & trends** — applicant response-time history, repeat deficiencies,
  discipline bottlenecks, drift across cycles.
- **Cross-ANDA signals** — sister/related applications sharing an applicant, drug
  substance, or facility, and what happened there.
- **Conflicts & misunderstandings** — commitments in notes that contradict the tracker,
  scope confusion or friction evident in correspondence, misaligned expectations.
- **Gaps, risks, blind spots, pitfalls** — what the record is *missing* relative to
  this stage, and what typically goes wrong at it.
- **Interruptions ahead** — items that will land on the RPM (or need chasing) in the
  next 1–2 weeks, so nothing arrives as a surprise. This one is visible on the card
  itself: the **AHEAD** line renders the single nearest upcoming demand (meeting where
  this ANDA is on the agenda, response due in, deliverable owed), with its source —
  and collapses entirely when nothing is within 14 days.

**Grounded tone, enforced by prompt and schema.** The brief cuts the fluff: no
preamble, no hedging filler, no restating what the card already shows. Every claim is
quantified where possible and cites a note/submission ID (`source_refs` in the output
schema — the UI's `sources ›` door). If the record is insufficient, the brief says
what's missing instead of inferring. 2–4 sentences, hard cap.

**Claude never renders facts.** Dates, statuses, submission events, note text, and the
forecast number itself come from the data layer and forecast model. Claude explains,
flags, and connects — in one clearly labeled slot.

Recommended API shape (Anthropic SDK):

- **Model:** `claude-opus-4-8` (on AWS deployments, `anthropic.claude-opus-4-8` via the
  Bedrock Mantle client — see deployment note below).
- **One call per card open**, streamed, so first tokens land in ~1 s.
- **Structured outputs** (`output_config.format` with a JSON schema):
  `{ brief, risks[], assumptions[], watch_items[], source_refs[] }` — the UI renders the
  schema, so the model can't destabilize the layout.
- **Prompt caching:** frozen system prompt + RPM playbook/definitions first with a
  `cache_control` breakpoint; per-ANDA facts after it. Repeat lookups get ~90 % of input
  at cache-read pricing.
- **Adaptive thinking** left on; `output_config.effort` at `low`/`medium` — this is a
  synthesis task, and low effort keeps latency tight.
- **Grounding:** the prompt includes only the structured record + last N notes; the
  schema's `source_refs` forces every claim to cite a note ID or submission ID, which the
  UI turns into the `[sources]` affordance. Instruct: "if the record is insufficient,
  say what's missing rather than inferring."
- **Cost envelope:** ~3–4 K input (mostly cached) + ~300 output ≈ **$0.01–0.03 per
  lookup** — negligible against RPM time saved.

**Deployment reality check (important for the pitch):** ANDA records are nonpublic and
commercially confidential. This must run through an FDA-approved channel — practically,
Claude on **AWS GovCloud / Amazon Bedrock** (FedRAMP-authorized path) with agency
approval, not the public consumer app, and with a minimal-necessary-fields prompt policy
(no unnecessary applicant PII, nothing retained client-side beyond the local cache).
Leading with this in the OGD/ORO conversation builds credibility.

**App shell (as built):** .NET 8 + **Avalonia** tray app (`app/`). Chosen over
WPF/WinUI for one decisive reason: the UI cross-compiles and headless-renders on any
CI box, so every card state is pixel-verified without a Windows machine, while still
publishing a plain single-file Windows `.exe` (MSIX/Intune-friendly). Windows-native
integration (global hotkeys, clipboard capture, and the planned UI Automation
text-under-cursor) is P/Invoke behind service interfaces, so nothing in the UI layer
is framework-hostage. Local cache in SQLite (planned), populated from whatever
sanctioned exports/APIs the systems of record allow.

---

## 8. What's intentionally *not* in v1

Cutting these is a feature — each one erodes the "calm" contract:

- ❌ Notifications, badges, or any proactive interruption
- ❌ A dashboard window / "my portfolio" view (different product)
- ❌ Editing data or writing notes back (read-only builds trust first)
- ❌ Charts, gauges, KPI tiles inside the card
- ❌ Chat with the AI inside the card (a "ask a follow-up" door can open the org's
  approved Claude surface later)
- ❌ Auto-hover everywhere (dwell mode stays allow-listed and off by default)

---

## 9. Success measures

- **Forecast accuracy & calibration:** median |forecast − actual action date| and the
  share of actuals landing inside the stated confidence band — published inside the app.
- **Time-to-answer:** median < 5 s from gesture to the RPM knowing status + next action
  (baseline: minutes of system-of-record navigation).
- **Zero-regret rate:** % of card opens the user doesn't immediately dismiss (>80 %).
- **AI trust:** % of briefs where the user opens `[sources]` and doesn't flag an error;
  a one-click 👎 on the brief feeds review.
- **Silence:** false-activation reports per user-week ≈ 0.

---

## 10. Mockups — the Appearance Lab and the desktop simulation

The original single-card design studio (`mockup/anda-card.html`, five looks) was **retired on 2026-11-02**: it
duplicated what the two files below do better, and kept the old A-default and five-look picture alive. It stays in git
history. Open these in any browser, desktop or phone, with fictional data:

- **`mockup/appearance-lab.html`** — a comparison wall of all **22 looks** (the §5 table) rendering the same
  record side by side, with the at-risk and on-track records switchable, and the four compact boxes shown over an open
  email. It is the quickest way to compare looks and the source of each look's palette and structure.
- **`extension/`** — the Edge/Chromium extension (see its README): the same card, draft check and watch rules in the browser, reached by the right-click
  menu, three shortcuts or the toolbar button, standalone or through the app's native-messaging host. It is not a mockup: it loads in a browser engine
  and is tested end to end there, including against the real host process. The simulation below shows its right-click menu in the Browser window.
- **`mockup/windows-simulation.html`** — a Windows 11 desktop in the browser (default **T**) that ports the app's
  logic: the recognizer, the placement rule, the three consent models (§2, **whisper default with the urgent-peek
  rule**), the offline brief, auto-hide, the compact boxes (a click or the hotkey again expands them), the **WATCH
  line with the federal-holiday calendar** (OPM dates, 3-business-day window) and the **draft check** (`Ctrl+Alt+D`).
  My data, the writing actions and rebinding shortcuts need real files and a model connection, so the simulation only
  describes them. It also shows the Edge extension's **right-click menu** (Browser window only, where the extension runs) and the app's **Watch list**. It shows the forecast-first clock, the single
  needs-attention signal, a sample streamed companion brief (no model is called), pin / expand / keyboard, and the
  invalid-number whisper. **On phones and tablets the card presents as a bottom sheet** with a drag handle, scrim and
  safe-area padding, with no horizontal scroll at 390 px. It is a port, not the app: the window chrome, the hotkey and
  clipboard hand-off are simulated.

All looks are light-only (§5).

## 11. Implementation status — the Windows app

**`app/`** contains the working v0: a .NET 8 / Avalonia tray application with all nineteen
looks (K default; four of them compact boxes) selectable from the tray menu, auto-hide, all three consent models
(Whisper default, with the urgent-peek rule), four rebindable global hotkeys (`Ctrl+Alt+A` Select & Peek,
`Ctrl+Alt+Space` lookup, `Ctrl+Alt+D` review a draft, `Ctrl+Alt+Shift+P` ask about a selection), the weekend and federal-holiday WATCH line, the draft
check and the writing actions (Proofread / Rewrite / Fact-check, preview before send, Copy only), reading
the person's own Excel or CSV portfolio and notes folder, one RLD-labeling date from a downloaded Drugs@FDA
file, an optional usage log, the
forecast-first card with AHEAD line and expandable drawer, persisted settings, and a
companion brief via the official Anthropic SDK (`claude-opus-4-8`) with a deterministic
offline fallback. Also shipped: the Settings window (transparency slider, Claude
connection panel with one-click Test), the Prompt Library (user-stored lenses that
steer the brief without overriding grounding), and a first-run setup that lands a new
user in under five minutes. A headless preview harness renders the real windows to
PNGs for review without a Windows box. See `app/README.md`, `SECURITY.md`, and
`PROPOSAL.md` (the director one-pager for the pilot ask).

**Live demo and verification.** A GitHub Actions workflow builds the app, runs the full test suite on
Windows and Linux, publishes a single-file Windows executable with a launcher, sample memo and demo
guide (`demo/`), and runs the app's own `--selftest` on a real Windows machine. `mockup/windows-simulation.html`
is a browser simulation of the whole tray flow for anyone who cannot run an unsigned program. What was and
was not verified, with the defects each step found, is recorded in `VERIFICATION.md` round 5.

**The Edge extension (`extension/`).** The same card in the browser, standalone or together with the app (round 8 in
`VERIFICATION.md`): a right-click menu on a selected number, three suggested shortcuts, a toolbar popup (look up, draft,
watch list, this page), a draft check and a page scan. It runs only when asked, on the one tab (`activeTab`, no host
permissions), makes no network request and uses no AI. It carries its own copy of the rules, kept identical to the app's by a
golden file the app writes. With the app connected it uses the app's portfolio and settings through native messaging, for one
extension ID only. The popup's tabs follow the ARIA tabs pattern (arrow keys, one tab stop); it has not been tried with a
screen reader. Tested in Chromium, not yet in Edge.

**Round 15: my actions, agents and the to-do list.** Three additions that keep the same rules as the card.
*My actions*: the person writes a prompt (or a to-do line) and gives it a shortcut; the shortcut is the consent act, as for every other
shortcut. A prompt that goes to an AI or an agent first shows the exact text and waits for Send; "send at once" is a per-action opt-in. *Agents*:
Alert Hub, Patent Analysis and Triaging Agent are listed but off, because their addresses and formats are not known and are not guessed; each is reached through the
same connector as "My API". A peek never asks an agent; the *Agent findings* drawer belongs to the full card and shows each answer as the agent's own
words, never as a fact from the record. *ANDA to-do list*: one list of the person's own items and items derived from their data, worst first, refreshed only
while it is open. Nothing is added to it by a reply: the person ticks the lines they want and presses a button. *Settings* is arranged in groups
under headings, with a strong line between groups and a fine line between settings, so a long page can be scanned.

**Round 16: My to-do list, Notes to my to-do list and Settings that arrange themselves.** *My to-do list* is a second, separate mode (as the draft check is
apart from the card): its own shortcut chosen in Settings, its own windows and its own file, for the person's daily workload (any task, ANDA related or not), with nothing
taken from their data. Highlighted or pasted notes are read **by plain rules, with no AI**, into suggested items; the reader shows what it understood
(title, day, urgent, repeat, Work/Personal/Other) in fields the person can change, never invents a day, marks a remark as "not a task" instead of ticking it, and adds
nothing until the person presses Add (or, by their own choice, adds at once with an Undo for the batch). A repeating item comes back by itself and a missed
one moves on instead of piling up. *Settings* became eight collapsible sections: everything is collapsed except what needs a look, what a new user needs
first and up to two sections opened often, each with a one-line reason; Expand all, Collapse all and Suggested view are always there, and a Getting
started card with three plain steps stays until it is hidden. Nothing moves while the person works.

## 11a. The focused edition (round 17), and the design rules it follows

The division's supervisor asked that the product stay focused for leadership: **ANDA lookup** and **reviewing a draft first**. Leadership mostly does not keep their own Excel portfolio of ANDAs, so a feature that
needs one cannot be the opening act. The product therefore opens with three things and hides the rest behind *Settings, Maintenance, Show all tools* (nothing is deleted, and choices come back):

1. **Lookup**: select a number, press the key, read the card (goal against forecast, what needs attention, WATCH, PSG and RLD dates).
2. **Review a draft**: checks a message against the record, then against itself and the calendar, with no AI and no network, and says what it checked and what it did not.
3. **Ask about a selection**: select text or cells, see what it looks like, run a fixed-rule quick check on the PC or a prompt of the person's own, which is shown before anything is sent.

Rules taken from Apple's published human-interface guidance (clarity, deference, depth; progressive disclosure; one clear action; plain labels), applied on Windows without imitating Apple's look:

- **One primary action per window.** Ask suggests a single button for what was selected; the rest waits under quick checks and prompts.
- **Answer first.** A review begins with one line (what was checked, and whether anything needs a second look); detail follows.
- **Fewer choices up front.** Five Settings groups instead of eight; one solid look; no transparency or appearance picker until asked for. Hidden is not removed.
- **Nothing happens by itself.** A suggestion is only ever a button. Quick checks run when pressed. Nothing is sent without a preview and a press of Send.
- **No teaching screens.** There is no *Getting started* card and no step list; the first run is the tool.

What this does **not** prove: whether a real person finds it lighter. That is a hypothesis to time with testers, with and without the hidden tools.

## 11b. The round 18 design: a calmer card, built for the daily habit (simulation first)

**Order of work, agreed with the owner:** the design is built and published in the simulation first. The owner reviews it. Only then is it
ported to the Windows app (§11b, "What is and is not in the Windows app"). Nothing below is claimed for the Windows app until that port is done and
its CI run is green.

**Why.** The first look had many options and many equal-weight lines. A project manager who opens it fifty times a day wants the
answer, one next action, and nothing to learn. The rules are taken from Apple's published human-interface guidance (clarity, deference,
one primary action, answer first, progressive disclosure) and applied on Windows without any Apple type, symbols or marks.

**The owner's decisions (2026-11-06).**

- The new default is **T · Pocket Plain**. **U · Calendar Ribbon** and **V · Glass Sheet** are selectable looks. The 19 earlier looks stay
  under *Show all tools* and are not changed. All looks are light only.
- The icon is **Loupe**: an indigo plate, a white ring and an amber center. A small amber dot on the tray icon means *urgent* (below).
- **One Bar** is an option for the lookup window (*Settings, Look and feel*, with *Show all tools* on); **Today** is a small window in the tray menu, also under *Show all tools*. Neither is in the focused edition.
- *Comfortable* or *Compact* spacing, and *Standard*, *Larger* or *Largest* text, are in *Settings, How it starts*. They change this app's
  windows only.
- Recent ANDAs and *Keep in my list* are saved on the PC, with a *Clear recents* button and an off switch.

**The default card (T).**

1. **The answer first.** One large figure says how many days later or earlier than the goal the forecast falls. The goal, the forecast and the
   forecast's confidence sit on the line beneath it, so the figure is never shown alone and cannot read as more certain than it is.
2. **One main action.** *Copy status* is pinned at the bottom of the card and Enter triggers it. Everything else is a quiet, secondary
   control.
3. **Everything else is one step away.** The facts (goal, forecast, WATCH, PSG, RLD) read below the answer. The drawer and the other
   tools are unchanged.
4. **Calendar Ribbon (U)** draws the weeks around the goal and the forecast, with weekends and the federal holiday marked and the gap shaded. It
   is a picture, so it has a written twin: the same facts in one sentence, which is also what a screen reader is given. When the two dates are
   more than fourteen weeks apart the ribbon is left out and the sentence stands alone. It is tested across a year end, because a ribbon that is wrong in the last week of December is worse than none.
5. **Glass Sheet (V)** is the only look that is translucent. The sheet is never less than 86% opaque; its label and body text keep their contrast (4.5:1 and 7:1) even over a black window at that
   floor, measured in the browser; and it is solid when the system asks for reduced transparency.

**One Bar (lookup window option).** One field. Type a number to see the record; with text selected it offers *Review* and *Ask* as rows.
**Every suggestion is a row you press. Nothing opens by itself.** Arrow keys move, Enter opens, Escape closes. It suggests only what its fixed
rules recognize: it never calls a model to decide what you meant.

**Today (tray menu, under *Show all tools*).** A small two-pane window. The left pane lists what needs a look (the same urgency and WATCH rules
as the card and the watch list), then the ANDAs kept in my list, then recent ones. The right pane shows the chosen one with *Copy status*. It reads
your data when you open it; nothing runs while it is closed. **Limit:** for someone with no portfolio file it shows only recents and kept ANDAs,
so it is of little use to leadership and is therefore not in the focused edition.

**Recent ANDAs and *Keep in my list*.** The last eight six-digit numbers looked up are remembered on this PC, so the lookup window offers them at once. Up to five can
be kept with *Keep in my list* on the card. Only six-digit numbers are stored (no notes, no drafts, no selection text), in the app's settings file
on the PC, and never sent anywhere. *Clear recents* empties them; switching *Remember* off clears them and stops the saving. Kept ANDAs are part of a
settings backup; recents are not, because they are the PC's own history. This changes the older statement that the app keeps nothing: see `SECURITY.md`.

**The tray icon and the amber dot.** The Loupe reads on a light and on a dark taskbar because the plate has a faint inner edge. The dot appears when
any known ANDA is **urgent**: its forecast is seven or more days after the goal, or a WATCH flag is serious. It is deliberately *not* "something needs a look", because
a dot that is almost always on is ignored. **The dot changes only when the app reads data** (for example when a portfolio file is loaded or a card is
opened). There is no background polling.

**Writing standard.** US English, sentence case, no exclamation marks, no casual words, one set of terms:

| Term | Means | Do not write |
|---|---|---|
| ANDA number | The six digits, with or without the word ANDA | application number, ID |
| Goal date | The official date of the goal | target, deadline |
| Forecast | The tool's projected action date | prediction, estimate |
| Gap | Days later (+) or earlier (-) than the goal | slippage, variance |
| Urgent | Forecast seven or more days after the goal, or a serious WATCH flag | critical, alert |
| WATCH | The line of weekends, holidays, passed goals and similar | warnings |
| Review | Checks a draft against the record and against itself | check, QA |
| Ask | Answers a question about the selection | chat |
| Quick check | A fixed rule that runs on this PC, with no model | AI check |
| Keep in my list | Saves an ANDA in the person's short list | favorite, bookmark, star |

`tools/wording-check.mjs` reads every window, message, document and the simulation, and lists anything that breaks these rules. Run it with `node tools/wording-check.mjs`.

**What is and is not in the Windows app today.** *In the app:* the Loupe icon (the executable, the tray, the extension's icons), US
spellings in the app's messages. *Only in the simulation and the lab:* the T, U and V looks as the default, One Bar, Today, recents and
*Keep in my list*, spacing and text size, and the tray dot. *Next step, after the owner's review:* port these to the app, restyle the
extension card, and run the Windows CI self-test, which renders each window and presses real keys.

**What this does not prove.** Whether a real RPM finds it better. Only the trial sheet in `demo/TRIAL-SHEET.md` can show that: five everyday tasks, timed against the
earlier design, by people who use the real application every day. Real Windows rendering (fonts, scaling, the taskbar) can only be checked in the CI renders
and on a real PC.

## 12. Engagement roadmap — seven features that make it the daily habit

Engagement here means *the tool pays the user every time they touch it* — never
streaks, badges, or noise. Ranked by payoff-per-build-effort:

1. **Copy-ready status lines** *(built: the Copy status button)* — every card gets a one-tap "Copy as
   status line": a perfectly formatted sentence for emails and trackers ("ANDA 216482
   (metformin ER 500 mg): cycle 2, 62%, goal Oct 3, forecast Oct 17 (+14 d, moderate);
   IR response due Jul 21."). RPMs write that sentence dozens of times a week; the tool
   that writes it for them gets opened reflexively.
2. **Morning Runway** *(partly built: the on-demand Watch list covers the whole portfolio, worst first; the
   tray-hover strip and agenda meetings are not)* — hover the tray icon for a slim, pull-only strip: today across
   *your whole portfolio* — top three clocks, items due this week, meetings where your
   ANDAs are on an agenda. The first thing checked with coffee; still zero notifications.
3. **Huddle Mode** *(not built; a concept in the pitch)* — one hotkey before a meeting pins mini-cards for every ANDA on the
   agenda in a neat row; `Esc` sweeps them away. Walking into the huddle pre-armed is
   the moment that converts the skeptics.
4. **"What changed since Friday"** *(not built)* — per-ANDA diff view: which facts moved since you
   last looked (new submission, slipped forecast, closed item). Answers the Monday
   question directly instead of forcing a re-read.
5. **Time-saved meter** *(not built; the pilot times lookups with a stopwatch instead)* — a quiet counter in Settings ("~47 lookups this week ≈ 3.1 h
   of navigation avoided") — engagement for the user, adoption evidence for the
   director, and the pilot metric for the proposal, all in one number.
6. **Division calibration board** *(not built)* — the competition showpiece: a shared, read-only page
   of the tool's forecast accuracy by division ("median error ±8 days, trailing 6
   months"). An AI tool that publishes its own report card is what "first place in AI
   automation" looks like to a review committee.
7. **Grounded proofread** *(built: Ctrl+Alt+D draft check with no AI, plus Proofread, Rewrite and Fact-check that stay off until
   a model is approved; never run against a live model)* — a second hotkey on the same
   select-and-press mechanism. It checks a selected draft against the record before
   anything is sent: dates, drug names and numbers that do not match are flagged first,
   deterministically and offline, then optional AI suggestions for wording. It never
   rewrites in place; the user copies the corrected text. Constraints that must hold:
   the selected text is arbitrary user content, so it is a wider data surface than the
   structured card record and needs its own approval; it uses its own hotkey so it can
   never fire by accident; the app shows exactly what will be sent; and it costs roughly
   2,000 tokens per proofread, so it is metered separately from card briefs.
