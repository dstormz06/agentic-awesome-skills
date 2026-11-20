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
│ GOAL      Oct 3, 2026 · 79 days  ▓▓▓▓▓░░ 62% │  ← the commitment: one date, one thin bar
│ FORECAST  Oct 17 · ▲ +14 d vs goal · moderate│  ← THE headline: the true forecast + delta
├──────────────────────────────────────────────┤
│ ⚠ Needs you · IR response due in 5 days      │  ← ONE line, only if something needs attention
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

**Appearance is a preference, never information.** Since round 23 the Windows app and the simulation offer **the same eight looks**
(Settings → Appearance): six full cards (W, V, U, T, K, P) and two compact boxes (L, O); see §11g. Fifteen earlier looks were retired; the lab keeps them, folded.
*(Before round 23: twenty in the app, twenty-three in the simulation.)* **No look draws a grid, hatching, texture or
other pattern behind its content** (the owner's decision, 2026-11-03). **The default is T · Pocket Plain**, chosen by the owner on 2026-11-06, in the simulation,
the lab and, since round 19, the Windows app (K · Graphite Quiet was the default from 2026-11-02 until then, and A before it). All full looks share identical anatomy, slot
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
| K | Graphite Quiet *(the default before T)* | Monochrome in soft slate (accent `#56626E`, not near-black) — color appears only where status needs it; outlined attention, text-only chips; quiet in a meeting, sharp on a projector | Line with a dot |
| L | Graphite Strip | Compact (about 340 × 63–81 px): number, goal, forecast, status edge | — |
| M | Fluent Tile | Compact (about 196 × 123–145 px): the forecast gap in large type, one attention line | — |
| N | Bronze Ticket | Compact (about 300 × 64–68 px): a slim ticket with a stub | — |
| O | Teal Badge | Compact (about 150–185 × 38 px): a status pill with the number and the gap | — |
| P | Bronze Gauge | Bronze palette; soft round corners, pill-shaped warnings, a header wash | Half dial |
| Q | Bronze Folder | Bronze palette on warm paper; the number on a bronze tab, a plain edge, underlined chips | Line with a dot |
| R | Bronze Rail | Bronze palette; a tinted column carries the days to goal in large figures, the detail reads down the right | Solid square bar |
| S | Bronze Tag | Bronze palette; an ID-tag head (a small slot, centered), goal and forecast in two columns | Thin bar |
| **T** | **Pocket Plain** *(default)* | *Round 18; in the Windows app since round 19.* White card, soft slate ink, one amber accent. One answer first: how many days later or earlier than the goal, with its confidence and source; then the facts; one main action, **Copy status**, pinned at the bottom | Thin bar |
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
from dark glass into a light steel panel). A per-user **transparency slider** (40–100%, **starting at 90%** since round 20; 92% before,
card surface only — text and status inks always stay opaque) provides the "sits
lightly on my screen" quality dark themes used to serve. At the starting value (checked again at 90%) every full look keeps its text at AA over a light
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
  line with the federal-holiday calendar** (OPM dates, 3-business-day window) and the **draft check** (`Ctrl+Alt+Shift+R`).
  My data, the writing actions and rebinding shortcuts need real files and a model connection, so the simulation only
  describes them. It also shows the Edge extension's **right-click menu** (Browser window only, where the extension runs) and the app's **Watch list**. It shows the forecast-first clock, the single
  needs-attention signal, a sample streamed companion brief (no model is called), pin / expand / keyboard, and the
  invalid-number whisper. **On phones and tablets the card presents as a bottom sheet** with a drag handle, scrim and
  safe-area padding, with no horizontal scroll at 390 px. It is a port, not the app: the window chrome, the hotkey and
  clipboard hand-off are simulated.

All looks are light-only (§5).

## 11. Implementation status — the Windows app

**`app/`** contains the working v0: a .NET 8 / Avalonia tray application with twenty
looks (T default; four of them compact boxes) selectable from the tray menu, auto-hide, all three consent models
(Whisper default, with the urgent-peek rule), four rebindable global hotkeys (`Ctrl+Alt+A` Select & Peek,
`Ctrl+Alt+Space` lookup, `Ctrl+Alt+Shift+R` review a draft, `Ctrl+Alt+Shift+P` ask about a selection), the weekend and federal-holiday WATCH line, the draft
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
- **No teaching screens.** There is no *Getting started* card and no step list; the first run is the tool. (Changed in round 32 by the owner's decision: a guide of five steps opens once, at the first start; see §11p.)

What this does **not** prove: whether a real person finds it lighter. That is a hypothesis to time with testers, with and without the hidden tools.

## 11b. The round 18 design: a calmer card, built for the daily habit (simulation first)

**Order of work, agreed with the owner:** the design is built and published in the simulation first. The owner reviews it. Only then is it
ported to the Windows app (§11b, "What is and is not in the Windows app"). Nothing below is claimed for the Windows app until that port is done and
its CI run is green.

**Why.** The first look had many options and many equal-weight lines. A project manager who opens it fifty times a day wants the
answer, one next action, and nothing to learn. The rules are taken from Apple's published human-interface guidance (clarity, deference,
one primary action, answer first, progressive disclosure) and applied on Windows without any Apple type, symbols or marks.

**The owner's decisions (2026-11-06).**

- The default of round 18 was **T · Pocket Plain**, with **U · Calendar Ribbon** and **V · Glass Sheet** beside it. In round 20 the owner made **V · Glass Sheet the default** (§11d), and in round 22 asked for a new default that blends it with Graphite and the gauge: **W · Graphite Gauge** (§11f). The Windows app still opens in T until the glass looks are ported. The 19 earlier looks are not changed.
  Every look can be chosen in *Settings, Look and feel* and in the tray menu, with or without *Show all tools* (round 19). All looks are light only.
- The icon is **Loupe**: an indigo plate and a white ring. *(Until round 23 it had an amber center.)* Amber appears only on the tray icon, as a dot or a number, when something needs the person today (below).
- **One Bar** is an option for the lookup window (*Settings, Look and feel*, in both editions since round 19); **Today** is a small window in the tray menu under *Show all tools*, and is not in the focused edition.
- *Comfortable* or *Compact* spacing, and *Standard*, *Larger* or *Largest* text, are in *Settings, How it starts*. They change this app's
  windows only.
- Recent ANDAs and *Keep in my list* are saved on the PC, with a *Clear recents* button and an off switch.

**The default card (T).**

1. **The answer first.** One large figure says how many days later or earlier than the goal the forecast falls. The goal, the forecast and the
   forecast's confidence sit on the line beneath it, so the figure is never shown alone and cannot read as more certain than it is.
2. **One main action.** It is pinned at the bottom of the card and Enter triggers it. Everything else is a quiet, secondary
   control. *(Copy status in rounds 18 to 20; Add a note since round 21 in the simulation and round 24 in the app, with Copy status as a small button and the S key.)*
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

**The tray icon and the amber dot.** The Loupe reads on a light and on a dark taskbar because the plate has a faint inner edge. *Until round 22* the dot appeared when
any known ANDA was **urgent** (a forecast seven or more days after the goal, or a serious WATCH flag). With most portfolios, and always with the sample data, that was
all the time, and the icon itself carried an amber center, so the tray looked as if it always had something to say. **Since round 22** (§11f) the icon at rest has no
amber, and the dot means one thing: *something needs you today*. **In the app the dot changes only when the app reads data** (for example when a portfolio file is loaded
or a card is opened). There is no background polling, which also means that after midnight the dot catches up the next time the app reads data.

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

**What is and is not in the Windows app today.** *In the app:* the Loupe icon, the default card T with its one answer and its one main action, the tray dot, US
spellings. *Only in the simulation and the lab:* the looks U and V, One Bar, Today, recents and *Keep in my list*, spacing and text size. *Next:* port these, restyle the
extension card, and run the Windows CI self-test, which renders each window and presses real keys.

**What this does not prove.** Whether a real RPM finds it better. Only the trial sheet in `demo/TRIAL-SHEET.md` can show that: five everyday tasks, timed against the
earlier design, by people who use the real application every day. Real Windows rendering (fonts, scaling, the taskbar) can only be checked in the CI renders
and on a real PC.

## 11c. Round 19: details you can copy, a tool that speaks up, and one question before sending (simulation first)

**The owner's rule for this round: nothing the tool raises should take more than two minutes to act on.** So every new thing ends in one action: Copy, Open the card, Snooze,
or one question to answer.

**Number forms.** The agency writes applications as a letter and six digits. **N is an NDA and A is an ANDA.** "N210793" is refused, with the reason, exactly like "NDA 210793";
"A210793" is read as ANDA 210793, and the card shows the number that was read. The app, the extension and the simulation give the same answer on 47 shared cases.

**Shortcuts.** *Review a draft* is now `Ctrl+Alt+Shift+R` and *My to-do list* `Ctrl+Alt+Shift+L`, because Word uses `Ctrl+Alt+D` and `Ctrl+Alt+L` and an app that registers a
shortcut takes it from every other program. A shortcut someone saved is kept. Microsoft's published lists could not be read from the build environment, so the new pair is
chosen from memory of those lists and should be tried once in Word and Outlook.

**The answer figure** on the default card is 30 px beside 15 px words (it was 46 px): large enough to be read first, not so large that it shouts.

**Card details (simulation; not yet in the app).** Expanding a card (E) brings up *Applicant and project*: applicant with its short name in parentheses, established name,
project description, US agent and its email, applicant email, the RPM's own notes, and the locations of the latest 356h and Module 3.2.P.1. Each has **Copy**, and **Copy all** gives
one labeled line per field. The rules:

- **Every value is a column of the person's own sheet.** A column the sheet lacks reads "not in your sheet" and has no Copy button. Nothing is guessed.
- **Nothing is looked up on the web.** A short name for an applicant comes from the sheet. A web search would tell a search engine which applicant is being worked on, and a
  guessed abbreviation could end up in an action letter.
- **The person chooses the columns** (*Settings, My files and writing, Card details*; twelve are offered in the simulation, in a fixed order). The choice travels in a backup.
- **Text is text.** A formula or markup in a cell is shown and copied as written, never run.
- When a connection to the agency's own data (through Elsa) exists, the same slots can be filled from it. That connection is not built: its fields are not known here.

**Watch-back (simulation; not yet in the app).** The first thing in this product that speaks without being asked, so it is **off until switched on**.

- One short notice near the clock, bottom right, above the taskbar. It never takes the keyboard and hides by itself after 12 seconds (it waits while the pointer is on it).
- It says the ANDA number and a short reason from the data, by the card's own rules, most urgent first. **It never shows the person's notes**, so nothing private appears in a meeting.
- **At most 1, 3 or 5 a day** (3 to start). *Open the card*, *Snooze for today* or *Dismiss*. When the limit is reached, Settings says how many more are waiting in *News and alerts*.
- The app would check when it starts, when the portfolio file changes and once each morning. There is no network use. The simulation checks when a box is ticked, when the demo
  date changes and on *Check now*, and says so.
- **Silence does not mean all is well.** A notice fires only on what the data shows, and the screen says that.

**Division news, the status hub (simulation; not yet in the app).** The division keeps one Word document in SharePoint; a line added there appears as a notice on every RPM's
screen and in *News and alerts* (tray menu).

- **How it is read:** from the copy of the document that OneDrive syncs to the PC, as a local file. That needs no new network address and no sign-in by the app. Reading
  SharePoint directly would need an application registration approved by IT; it is not built.
- **How the document is written:** one paragraph per line of news, starting with its date. Lines from the last 14 days are shown, newest first; a line dated in the future is ignored.
- **A push channel is a risk, stated plainly:** anyone who can edit that document can put words on every screen. So a line is plain text, cut to 240 characters, never run, and
  counted in the same daily limit. Two hundred new lines still produce at most five notices a day.
- Division news cannot be switched on without a document, and a restored backup cannot switch it on either: the document's location belongs to the PC.

**One question before sending (simulation; not yet in the app or the extension).** Review now ends with a single line: **the one question that would close the most important
open gap, or "No open gaps."** It is chosen by fixed rules, in this order: a date or status that differs from the record; a number that is not in the data; a day that does not
match its date, or a deadline on a weekend, a holiday or in the past; a mention of an attachment (the tool cannot see attachments). It gives facts and one question, **not a
verdict on whether to reply, or to whom**: that stays with the person, and the line says so. The shared review result is unchanged, so the app, the extension and the simulation
still agree on the 46 shared cases.

**Considered and not built, with the reason** (the owner's list of 2026-11-12; details in `ROADMAP.md`): live web search for abbreviations (leaks the applicant; unsafe in a
letter); TickTick (an outside cloud service; ANDA tasks would leave the agency); automatic determination of the earliest lawful approval date, First Generic or Noteworthy status
(legal and policy calls; the tool can show inputs and say "possible, please confirm", never decide or add to a tracker); Apple Intelligence (Mac only; the users are on Windows);
code extensions written by users (a security risk; *My actions*, *My agents* and the chosen columns cover the need); a rewrite as a Power App (it cannot register a global shortcut,
read a selection in another program or open a card at the pointer; Power BI suits a leadership dashboard); pasting into other programs (Copy only, for now).

**The looks stay in reach, and the Go to list reads like a list (simulation and app).** Round 17 hid *Look and feel* behind *Show all tools* and drew the card solid in the
default look. The owner went looking for the looks and could not find them, so the rule was wrong for the person it was written for: **every look, the compact boxes and the
transparency slider are in Settings in both editions, the tray menu keeps its *Appearance* list, and what a person chooses applies at once.** *Show all tools* now governs
tools only (the two to-do lists, agents, dates). A new install still opened in T · Pocket Plain at the 92% the owner chose in round 16 (round 20 changed both: §11d). The **Go to** list showed the small
capital labels as they are drawn on the page, which made a column of capitals; it now shows them in sentence case, keeping abbreviations and names (ANDA, API, PC, Drugs@FDA,
Alert Hub, Select & Peek). The labels on the page are unchanged.

**What round 19 does not prove.** That a notice near the clock helps more than it interrupts; three a day is a guess. That the Word document is a workable way to run division
news: nobody has written one. That the chosen shortcuts are free on a real FDA laptop. Nothing in this section has been used by a real RPM.

## 11d. Round 20: glass by default, notes you can write, a proofreader that works with no AI (simulation first)

The owner's requests of 2026-11-12, in the owner's words: the default appearance "should use the glass design"; transparency "from 40 percent to 100 percent but the default
should be 90 percent"; "make sure all of the features and sub features/functions of the proofread mode/tool will work flawlessly … you can actually proofread and also factcheck and
also cross references/leveraging the user's ANDA portfolio or the user's personal notes"; show "the user's most recent and relevant notes +/- relevant to-do list items at the bottom
row and as auto collapsed"; "the user can create personal notes or quick notes of the particular ANDA"; and make the simulation "actually look like windows 11". The name stays
**Notula**.

**Glass by default, at 90% (simulation and lab; the transparency start also in the app).** V · Glass Sheet is the default look and the first in the list. The sheet used to be
fixed at 86%; it now **follows the transparency setting**, which runs 40 to 100% and starts at **90%** for every look. Glass blurs what is behind it, and the action area at
the bottom blurs the facts that scroll under it, so no text shows through the buttons. A system that asks for less transparency gets a solid card. The Windows app's start is
90% too; **its default look is still T · Pocket Plain**, because the blur behind a rounded card has to be seen on a Windows desktop before it becomes a default there.

**Quick notes on the card (simulation; not yet in the app).** The *Notes* button and the **N** key open the card fully, show the notes and put the pointer in a box: *Add a
quick note for ANDA 216482*. Ctrl+Enter or *Save note* saves it. A person's own notes are listed above the project notes of the record, newest first, each with Copy; their own
also have Edit and Delete, with Undo. Decisions, and why: (1) **the app keeps quick notes in its own file on the PC; the sheet and the notes folder stay read-only**, as they
always were; (2) a note is **text, never markup**, at most 500 characters, at most 50 per ANDA; (3) **a note is never sent anywhere, never shown in a notice near the clock and
not put in the settings backup** (a backup is a file people send to each other); (4) what was typed and not saved is kept if the card closes; the first Escape leaves the box and
the second closes the card; (5) while the pointer is in the box, the card's one-letter shortcuts are just letters.

**Proofread and Fact-check that work with no AI (simulation; not yet in the app or the extension).** The Review window had three buttons that were switched off until a model is
approved. Now **Proofread** and **Fact-check** work at once, by fixed rules on the PC, and *Check everything* runs both; the AI versions stay beside them, still off.
- **Proofreading** is a short, stated list: about 120 common slips of spelling (US English); **names in the person's own data** that are one slip away (“Metfromin” against the
  drug of the ANDA the draft names); *ANDA* and *FDA* in capitals, “an ANDA”, a lone “i”; a space before a mark or missing after one; doubled marks and spaces; a sentence that
  starts with a small letter; brackets and quotation marks that do not close; two date styles; a very long sentence. Every change that needs no guessing can be copied as a
  **corrected draft** or applied to the box in the window. **The app never types into the email.** The section says it is not a full spelling or grammar check.
- **Fact-check** is the earlier review (numbers, goal and forecast dates, on-track or late wording, weekdays, deadlines) plus three more facts from the sheet: the **strength**,
  the **review cycle** and the **due date of the open action item**. A date in the draft that also stands in one of the person's notes is traced to that note. A strength is
  compared only in the sentence that names the ANDA, because the next sentence often speaks of a sister product (the demo email does exactly that).
- **For recall, at the bottom and closed:** one row for each ANDA of the person's data that the draft names (three at most), with the count of notes and open items. Opened, it
  shows the notes the draft touches first (a shared date or word), then the newest, four at most, then the open items of the record and of both to-do lists, and *Open the card*.
- The shared review result is unchanged, so the app, the extension and the simulation still agree on the shared cases; the new parts are a layer that will be ported with its own
  shared cases.

**A desktop that looks like Windows 11 (simulation only).** A wallpaper drawn in the page (three to choose from: light blue, dark blue, plain), a centered taskbar with a search
box, a line under the open app and weather at the left, rounded windows with caption buttons, Mail in the manner of the new Outlook, Chat in the manner of Teams, a browser with
a tab and an address pill, and a dark terminal. **Everything is drawn here from shapes: no logo, no image file and no artwork of any company or of the agency is used**, the
window titles still say "Outlook-style", "Teams-style" and "Edge-style", and the banner still says the page is a simulation with fictional data and is not an FDA product. The
added pieces are decoration, hidden from screen readers; the text people select and every control are where they were.

**What round 20 does not prove.** That glass reads well on a real Windows desktop, a projector or a television. That the proofreading list catches what an RPM actually
mistypes: it is a first list, and it will miss most grammar. That three extra facts are the right three. That anyone wants notes in a tool that also reads a notes folder.
Nothing in this section has been used by a real RPM, and none of it except the 90% start is in the Windows app.

## 11e. Round 21: Enter adds a note, a live clock, a proofreader that reads grammar, and a review of the whole (simulation first)

The owner's requests of 2026-11-12, later the same day: a desert photograph as the desktop; the *Copy status* bar "thinner/shorter and more transparent", with Enter going
"straight to adding a new notes" and the recent notes below; a small button to "the specific ANDA webpage … customizable as a new settings"; "a tad bit of professional shadow
and Apple design feelings"; "always use today's date and time … current to the second"; "your proofreader and fact checking still not working? Why can you just be seamlessly
integrating to any local default AI"; a pitch deck; and "find the 5 biggest useability design or code problems. Fix the important ones without changing the main concept."

**The card's actions.** The main action is **Add a note** (Enter): a slim, tinted button, 34 px high, in place of the solid 44 px bar. **Copy status** is a small button and the
**S** key; one line under the buttons says so. The notes are the first section of the drawer, so Enter shows the box with the newest notes right below it. **Open ANDA page**
goes to an address the person sets in *Settings, My files and writing*, with `{anda}` where the number goes. Only a plain `https` address is opened, only the six digits are
put into it, and with no address the button says so. After the page opens the card steps aside unless it is pinned. In the Windows app Enter still copies the status, because
notes are not in the app yet.

**A live clock, and sample data that follows it.** The taskbar shows the real time to the second and the real date, in the time zone of the person's computer (Eastern Time if
the browser does not say). The sample records were written around Thursday, Jul 16, 2026; so that they read as current, **every date in them moves forward by a whole number of
weeks**: a Saturday goal date stays a Saturday, "Thursday huddle" stays true, the goal is 73 to 79 days away and the open item is due in 2 to 8 days, whatever the day. "Today",
for every rule, is the real today. With `?clock=fixed` the page stays at Jul 16, 2026, which is what the test suites use.

**The desktop.** A desert under a blue sky is the default background, **drawn in the page**: the photograph the owner pointed to could not be fetched from the build
environment, and a photograph would be the first image file in a page that has none. The card and the app windows have a quieter, layered shadow.

**The proofreader.** The review found why it read as "not working": the results were below the fold, under the AI buttons and two paragraphs about them, and the rules
missed ordinary grammar. Now the results come right under the buttons, with one summary line; the AI options are folded at the bottom; **the draft is shown with each change
marked**; and about thirty grammar rules join the spelling list (words that sound alike, a verb that does not agree, a missing apostrophe, *a* or *an*, a date that does not
exist), each needing the words around it before it speaks. It is still a list of fixed rules and says so.

**An AI built into the PC.** Apple Intelligence runs only on Apple devices, and the people this is for work on Windows. Windows has an on-device model on Copilot+ PCs,
and Edge and Chrome expose one to web pages on some computers. The simulation asks the browser whether it has one **that is already on the PC**; if so, the three AI buttons
use it, after showing what will be given to it, and mark the answer as unchecked and Copy only. It never starts a model download. In the build browser the feature exists
but its model is not installed, so this path was tried against a stand-in, not a real model. For the Windows app it would be one more provider, off until IT approves it.

**The review: the five largest problems, and what was done.**
1. *Results below the fold in Review, and an empty window that explained nothing.* Fixed.
2. *A proofreader too thin to trust.* Grammar rules, the marked draft, and dates that do not exist. Still rules, not a language model.
3. *One keystroke handled twice.* Enter in the Lookup box opened the card and then also acted on the card (it copied the status without saying so). Fixed.
4. *Notes that vanish on a reload, in a feature whose point is recall.* Kept in the browser's store for the page where that is allowed; the box says which. Fixed.
5. *The simulation and the Windows app have drifted apart*, and the newest rules had no shared test cases. The cases are now a file in the repository
   (`mockup/proofread-golden.json`, 32 cases) that a port must reproduce. **The port itself is not done**; that is the largest open item.
Also fixed on the way: what the card scrolled to could end up under the action area; a card stayed in front of the page it had just opened.

## 11f. Round 22: a quieter default, a thinner taskbar, and a tray icon that speaks only when it should (simulation first; the tray rule also in the app)

**The new default look: W · Graphite Gauge.** The owner asked for a default that "blends the best features" of the graphite and gauge looks "while still keeping the
Enter button and other smaller buttons and features at the bottom". W takes three things from three looks: the **quiet graphite palette** of K (color only where the
status needs it), the **gauge** of P (how much of the review clock has run, now beside the answer, with the percentage in words for a screen reader), and the **glass
sheet and action area** of V (one answer first; *Add a note* on Enter, then the small buttons, pinned to the bottom; 90% to start, 40 to 100%). V · Glass Sheet is the
next look in the list, one click away. (The Windows app received W, V and U in round 23, §11g.)

**The taskbar** of the look-alike desktop is 40 px high (it was 48), with its icons, search box and clock scaled to fit. Every button is still at least 24 px.

**The tray icon.** The owner: "don't have it to look like there's currently a new alert … this should only be triggered when there's an actual scenario". Two things
made it look that way. The Loupe had an amber center, the same amber as the dot, so the icon at rest already carried the alert color; and the dot followed the
"urgent" rule, which the sample data always meets. Now:

- **At rest the tray icon has no amber.** (In round 22 the larger app icon kept its amber center; in round 23 the owner asked for it to go there too.)
- **The dot means "something needs you today"**, and nothing else: an action item due today or overdue; a goal date that is today or has passed; an ANDA the person's
  sheet marks as an immediate risk, a safety issue or a customer service issue; a message marked urgent that has had no reply for two days. A forecast that is late, a
  goal date that is close, or an item due in five days does not light it: the card and the watch list already say those.
- **The dot can always be explained.** The tooltip counts ("2 things need your attention today") and never names an ANDA; the tray menu line reads *News and alerts…
  (2 today)*; that window opens with *Needs you today*, one line per reason, each of which opens its card; a mark from the sheet is the first line of the card's watch row.
  With nothing to report the window says so and states the rule.
- **"Today" is recounted when the date changes**, so an item due tomorrow lights the dot after midnight without anything being pressed (simulation).
- **An icon behind the ^ arrow shows nothing on the taskbar**, on Windows and here. So that the two what-ifs of the demonstration are never switches with no visible
  effect, a short notice says where the dot is. In daily use the person pins the icon to the taskbar once; `demo/DEMO.md` says how.

*What is real and what is not.* In the **Windows app** the dot now follows the first two reasons, read from the person's own sheet (a goal date; an action item whose
text says it is due today or overdue), never from a sample record, and it is recounted only when the app reads data. The **sheet's mark** and the **unanswered urgent
message** exist in the simulation only: the app has no such column yet, and it reads mail only for the open ANDA, on request, never in the background. Whether the app
should look at the portfolio file once at midnight to recount is a decision for the owner, because it would be the app's first scheduled read.

**The review: the five largest problems, and what was done.**
1. *A tray icon that always looked as if it had news*, and a dot nobody could explain. Fixed as above, in the simulation and (for the rule and the picture) in the app.
2. *Two writers for one tooltip.* The connection status and the alert count each replaced the other's text on the tray icon, so whichever ran last won. One function
   now writes both. Fixed.
3. *A rule about "today" that was never recounted when today changed.* Fixed in the simulation; in the app it is a stated limit and an open decision (above).
4. *The stylesheet named every look in every rule* of the answer-first family: adding W meant editing 29 selectors, and the next look would have meant about 50. The
   family and the glass behavior are now two attributes set in one function; the card was compared pixel for pixel in eight looks before and after, with no difference. Fixed.
5. *The Appearance Lab and several documents described a product that no longer existed* (the wrong default, the old main action). The Lab now shows W first with
   today's actions; the documents were corrected. **The larger form of the same problem is unchanged: the Windows app trails the simulation by rounds 18 to 22.**
Also fixed on the way: a mark from the sheet was put into the watch list as markup, not as text (it is escaped now); text scrolling under the pinned action area
showed through the glass (the area is 97% solid).

## 11g. Round 23: eight looks in both, a number on the tray icon, a choice of when it is counted, and three proposals for the default

**Eight looks, the same in the app and the simulation.** The owner: "cut down to only top 8 appearances for both app and simulation". The eight are the six full
cards **W · Graphite Gauge** (default in round 23; X took its place in round 24, §11h), **V · Glass Sheet**, **U · Calendar Ribbon**, **T · Pocket Plain**, **K · Graphite Quiet**, **P · Bronze Gauge**, and the two
compact boxes **L · Graphite Strip** and **O · Teal Badge**. *Why these:* the four answer-first looks the owner chose or asked for in rounds 18 to 22; K, the calm
classic layout that projects well; P, the bronze the owner liked, with the dial W took from it; and one wide and one very small compact box. The choice is a
proposal that was acted on: the list is one line in each program, so a swap is small. A setting or a backup that names a retired look falls back to the default.
The fifteen retired looks keep their definitions (a compact box still borrows its sibling's colors) and are shown, folded, at the end of the Appearance Lab.

**W, V and U in the Windows app.** All three are T's layout. W adds the graphite colors and the half dial beside the answer; U adds the calendar under the
answer, drawn by the same rules as the simulation (a Monday start, weekends shaded, a federal holiday named, the gap drawn) with the same sentence under it;
V has slightly darker secondary text and rounder corners. **The app does not draw the blur** of the simulation's glass: on Windows a blurred window is blurred
as a rectangle, including the margin around the rounded card, and that has to be seen on a real desktop before it ships. The sheet follows the transparency
setting, as every card in the app does. In the app the main action is still *Copy status*, because notes are not ported.

**A number on the tray icon.** One thing that needs the person today shows the dot; two to nine show the number; more show "9+". The tooltip and the spoken
label always give the true count. Windows draws tray pictures at 16 to 24 px, so the number sits in a badge that covers the upper right quarter of the icon.

**When the icon is counted again** is now the person's choice, in *Settings, How it starts*: only when the app is used; also once a day, just after midnight
(the default, which the owner approved); or also every hour. A scheduled count looks at the portfolio file once, on the PC, and sends nothing. This replaces the
earlier rule that the app never reads on a timer; `SECURITY.md` says so.

**The Loupe has no amber at any size**, in the app's icon, the extension's icon and the simulation.

**Three proposals for a new default, shown first and not yet switched on** (the owner: "I prefer the visual/line graph from appearance U … add either a brief
visual or clear auto adaptive text colors and styles to indicate 'days until' the actual GDUFA Goal Date. Show me 3 new appearances first"). Each is W with U's
calendar, and each says how many days are left to the goal date:
- **X · Graphite Calendar**: the dial gives way to a count ("79 DAYS TO GOAL"); the calendar follows the answer. The smallest change.
- **Y · Graphite Runway**: one line for the review clock (how much has run, where today is, the days left), then the calendar.
- **Z · Graphite Countdown**: the days to the goal date become the first line, and the forecast gap the second. This changes what the card says first.

The indicator follows the distance, **in words as well as in color**: more than 30 days, quiet; 30 days or fewer, amber; 7 days or fewer, amber on a tint in
heavier type; the day itself, solid amber ("The goal date is today"); after it, solid red ("The goal date passed 3 days ago"). The thresholds are a proposal.
The Lab has a control that shows each state.

## 11h. Round 24: X is the default in both programs, and notes come to the Windows app

**The owner chose X · Graphite Calendar.** It took W's place in the eight and is the default in the simulation, the Appearance Lab and the Windows app: one answer
first (the forecast against the goal); beside it the days to the goal date; under it the calendar from U; the same actions at the bottom. W is still defined and
no longer offered; a setting or backup that names it opens in X. The two proposals that were not chosen (Y and Z) are folded at the end of the Lab.

**The days to the goal date** follow one rule in both programs, held as 20 shared cases in `mockup/countdown-golden.json`: more than 30 days, quiet; 30 or
fewer, amber ink and edge; 7 or fewer, amber on a tint in heavier type; the day itself, solid amber with the words "The goal date is today"; after it, solid
red with "The goal date passed N days ago". The figure and its caption change with it ("79 / DAYS TO GOAL", "Today / IS THE GOAL DATE", "3 / DAYS PAST GOAL"),
and a screen reader hears the sentence. Without a goal date in the data there is no count: never an estimate. The small card keeps the count and leaves the
calendar out.

**Notes in the Windows app** (the largest piece of the simulation the app lacked). On every full card:
- **Enter, N or the button goes straight to a new note**: the drawer opens at *My notes* with the cursor in the box. On the small card the full card opens first.
- **Ctrl+Enter saves.** A plain Enter in the box is a new line. Escape leaves the box; a second Escape closes the card, so a half-typed note is not thrown away
  by one keystroke. **A key typed in the box belongs to the box**: S does not copy the status and E does not fold the drawer.
- **A half-typed note keeps the card open** when another window is clicked: a person reads the email while writing the note. Once the note is saved, a click
  elsewhere closes the card as before.
- A note can be changed, deleted (with Undo) and copied. The newest is first. The button counts project notes and the person's own together.
- **Copy status** is the first small button and the S key. One line under the buttons says so.
- The rules are the simulation's: text only, 500 characters, 50 notes for one ANDA, a six-digit number as the key.

**Where a note lives.** In one file, `quick-notes.json`, in the app's own folder (`%AppData%\AndaCompanion`, or the folder the launcher names). It is written
to a temporary file and moved into place, so a crash cannot leave half a file. A file that cannot be read is set aside under another name, never overwritten,
and the card says so. The portfolio sheet and the notes folder are still only read. A note is never sent anywhere: not to an AI, not in a notice; and it is not
in the settings backup. **This is the first content of the person's own that the app writes to disk**; `SECURITY.md` records it, with the question for IT that
it raises.

**Open ANDA page** in the app hands one address to the default browser. The address is set in *Settings, My files and writing*, with `{anda}` where the number
goes; only `https`, no user name or password, at most 300 characters; only six digits are put into it; it is checked each time the button is pressed. With no
address, or a bad one, nothing is opened and the card says why. After the page opens the card steps aside unless it is pinned. The button used to read *Open
in system of record* and did nothing.

**Still only in the simulation after round 24:** Proofread and Fact-check with their recall row and the one question before sending (ported in round 25, §11i),
card details with Copy, notices near the clock and division news, text size and spacing, recent and kept ANDAs, One Bar and Today, and the blur behind the glass looks.

## 11i. Round 25: Review works with no AI in the Windows app, answer for answer with the simulation

The owner asked to proceed with the port and to test everything twice. The piece chosen first was the one a person uses on every message: *Review a draft*.

**What the Windows app has now.** The Review window opens on the selected text and runs three kinds of fixed rules on this PC. No AI is used and nothing is sent.

1. **Fact-check**: the earlier review (numbers, goal and forecast dates, on-track or late wording, weekdays, deadlines, placeholders) and, new here, the strength,
   the review cycle, the due date of the open action item, a date that does not exist, and a date that also stands in one of the person's notes.
2. **Proofread**: a short list of rules: about 140 common slips, a name one slip away from a name in the person's data for an ANDA the draft names, words that
   sound alike, a verb that does not agree, a missing apostrophe, "a" or "an", spacing, punctuation, capitals, brackets, date style and very long sentences.
   The draft is shown with each change marked. *Copy corrected draft* copies; *Apply here* changes only the app's own box. The app never types into an email.
3. **One question before you send**: the most important finding, as a question, or "No open gaps." It is hidden in the Proofread view, which is about wording only.

Under them, **For your recall**: for each of the first three ANDAs the draft names, the person's quick notes and the record's notes (the ones the draft touches
first, then the newest) and the open items from the record and both to-do lists, folded, with a button that opens the card.

Three buttons choose the view: *Check everything*, *Proofread*, *Fact-check*. The checks are the same in each; the view chooses what is shown, and the chosen
button says "selected" to a screen reader. The AI actions (Rewrite, Proofread with AI, Fact-check with AI) moved into a folded *AI options* section and work as before.

**How "the same as the simulation" is kept true.** The simulation's rules are JavaScript; the app's are C#. The two languages read a pattern differently (in
JavaScript a word character, a digit and a word boundary are ASCII, white space is a fixed list, and "$" is only the end of the text), so the app rewrites each
pattern once, in one place (`JsRegex` in `Services/Proofreader.cs`), instead of trusting each pattern to behave. The simulation writes its answers to two files:
`mockup/proofread-golden.json` (32 drafts written by hand) and `mockup/proofread-stress-golden.json` (372 drafts made by a seeded mix of those drafts, slips of
the fingers and awkward characters, and 8 with a quick note; written by `tools/proofread-stress.cjs`, which also holds the earlier review's findings and the one
question for each draft). The app's tests must reproduce every answer, character for character. They do.

**What differs on purpose, because the app's data differs.** The simulation's sample records carry a due date and an established name; a record in the app does
not have those fields. The app reads the due date from the record's first action item that says "due" with a date, the strength from the product name or the
Strength column, and the names from the product, the applicant line and the active ingredient. A portfolio file has no action items, so with a person's own
sheet the due-date check stays silent; that is stated, not hidden. A note label without a date ("Your sheet") is worded "in your notes (Your sheet)".

**One defect found in the simulation by doing the port:** "cycle 0" in a draft was reported as "cycle undefined". Both programs now say "cycle 0".

**Still only in the simulation after round 25:** card details with Copy, notices near the clock and division news, text size and spacing, recent and kept ANDAs, One Bar and
Today, the on-device AI option in Review, and the blur behind the glass looks.

## 11j. Round 26: Compare two files, four ways to read a text, an invitation and a note from a selection, My usage, and a security review

The owner's leadership asked for a way to choose two Excel files and have them compared, with a short summary and clear pictures. The owner answered four
questions first: the two files are usually **two versions of the same report**; build the fixed-rule comparison **and** the AI narrative; build it in the
simulation **and** the Windows app; and add all four smaller items. The work then went through a security review with the repository's own *AAS Security
Engineer* and *AAS Secure App Builder* skill sets, and its fixes are part of this round.

**Compare two files** (tray menu, in both editions). The person chooses an earlier and a later file (Excel `.xlsx` or CSV in the app; CSV in the browser
simulation). Everything is done on the PC by fixed rules; no AI is used and nothing is sent. For a workbook the app reads one sheet (the first visible sheet
with an ANDA column, otherwise the first visible sheet) and names it beside the file name.

1. **Rows are matched by the column that names each row.** The tool suggests the first column both files share in which every row has a value and no value
   repeats, preferring a heading that sounds like a number (ANDA, ID, No, #). The person can choose another shared column. With no such column, rows are
   compared by position, and the summary says so.
2. **Values are compared as what they are.** "1,000" equals "1000.0"; "10/3/2026" equals "Oct 3, 2026"; extra spaces do not count. A changed whole number
   shows the difference; a changed date shows the number of days earlier or later.
3. **What is shown:** a summary of a few plain sentences (rows the same, changed, new, no longer there; the columns with the most changes; the largest date
   change; columns only one file has; rows left out and why); one bar of the rows in four parts (rows that are the same as an outline, the three kinds of
   change filled); a bar for each of the eight columns that changed most; a table of the first 60 changed cells; and the lists of new and removed rows.
   Every part of each picture is also named with its number, so nothing is said by color alone.
4. **What can be taken away:** *Copy the summary*, *Copy the full report* (up to 50 changed rows and 50 names each of new and removed rows) and *Save a report*
   (one web page with no script and no outside address).
5. **Write a narrative with AI…** opens the usual preview of exactly what would be sent. It holds the summary and the listed changes, cut at 1,800
   characters, **never the files**. Nothing is sent until the person presses Send, and the answer is Copy only.

The closing line of every summary states the limit: values were compared as text, numbers and dates in the columns both files have; formulas, formatting,
hidden rows and other sheets were not compared. Limits: 25 MB, 20,000 rows and 200 columns in the app; 5 MB in the simulation.

**Four ways to have a text read** (Ask about a selection, under *Read it for me*): *Give me the TL;DR*, *What else should I know?*, *Onboard me on this
topic* and *Why does it matter?*. Each is a stored prompt that is filled in and shown first; it uses the person's AI connection only after Send.

**From a selection, with no AI:** when the selection names a date, *Save a calendar invitation* writes a small `.ics` file for an all-day event on the first
real date in the text (with no year, the next such day; a bare "3/4" is not read as a date). The person saves the file and opens it in their calendar; the app
sends nothing and adds nothing itself. When the selection names an ANDA that is in the person's data, *Add to the notes of ANDA …* puts the selection into
that ANDA's quick notes (the note file of round 24, with its 500-character limit, said when a note was cut).

**My usage** (in the app: Settings, *Show my usage…*; in the simulation: the tray menu, with all tools on). Leadership asked for outcomes that can be measured,
quietly, with the person told first. The answer is the usage log that already existed, switched **off** until the person turns it on, now with seven more
event names and a window that shows the counts for the last 7 and 30 days. It holds counts only, from a fixed list of event names: never an ANDA number, a
name or any text. It stays on the PC; the person copies the summary if they choose to share it. Nothing is reported for them.

**How "the same in both programs" is kept true.** As in round 25, the simulation writes its answers to a file, `mockup/compare-golden.json` (20 comparisons,
14 invitations, 3 usage cases, the prompts and the event list; written by `tools/compare-golden.cjs`), and the app's tests must reproduce every answer,
character for character. Writing the rules a second time found a real fault in the first: the simulation did not escape semicolons in a calendar file.

**What the security review changed.** Each item was a finding, a fix and a test in both programs:

1. **A pasted report could become a formula.** A row named `=HYPERLINK(…)`, or a file named `=x.csv`, began a line of the copied report; pasted into a
   spreadsheet, such a line is a formula. A copied line that starts with `=`, `+`, `-` or `@` now gets an apostrophe in front, which a spreadsheet reads as
   text. On the screen and in the saved page the value is shown exactly as it is in the file.
2. **Control characters could reach a calendar file.** Line breaks were already escaped; the rarer ones (U+2028, U+2029, U+0085) now become plain line
   breaks first, and other control characters are removed, so no part of a selection can start a line of its own in the file.
3. **A very long cell could flood the window and the report.** Values, row names and headings are shown cut (200 and 80 characters, never through the
   middle of a character); rows are still matched and compared on the whole value.
4. **A large workbook froze the window.** The app now reads the file, and compares more than 2,000 rows, off the UI thread, and says "Reading the file…"
   and "Comparing…". A comparison that runs into the pattern time limit is stopped and said plainly.
5. **The saved report** now carries a content policy that lets it load and run nothing, in addition to every value being escaped.
6. **Native calls** (user32, kernel32, gdi32) are now loaded from the Windows system folder only. This was the one rule the .NET security analyzers
   raised; after the change they raise none.

**What the design review changed.** Measured against the accessibility checklist of the repository's *Product Design Studio* skill set: the "Same" part of
the row bar stood about 2 to 1 against the window. It is now an outline at full strength beside three filled parts (each 5.9 to 1 or better in the looks
measured, held by a check in each program), so the parts differ by more than color; the file choosers in the simulation were 20 pixels tall and are now
36; the smallest text in the app's window is 12. Buttons stay 34 pixels tall, as everywhere else in the product: above the 24 that WCAG 2.2 AA asks,
below the 44 recommended for touch.

**What the simulation says after a save.** A web page can ask the browser to save a file but cannot learn whether it did, and a page shown inside another
page, as the published link is, is not allowed to. The simulation therefore says that a download was tried and what to do if nothing was saved, as the
backup in Settings already did, and never that a file was saved. The Windows app writes the file itself and reports a failure.

**What changed in the workflow** (from the *DevOps & Cloud* skill set's security-scanning section): the Linux job now also checks the packages against the
public advisory list and builds with every .NET security rule on; either stops the run. The shared answer files are among the paths that start it.

Checked and left as they were: no secret in the repository; no package with a known vulnerability; the workbook reader refuses document type definitions and
outside entities and caps unzipped size; the simulation builds its windows from text nodes only; what goes to an AI is the selection inside a fence, after a
preview, with the answer Copy only; the usage reader accepts only a date and a known event name.

**Still only in the simulation:** card details with Copy, notices near the clock and division news, text size and spacing, recent and kept ANDAs, One Bar and
Today, the on-device AI option in Review, and the blur behind the glass looks. The extension has none of round 26.

## 11k. Round 27: two documents and a stored prompt, a check of an AI answer, signs worth a second look, and five daily helpers

The owner asked three things in a row: whether the tool could "detect any and all AI content" and say how to respond, perhaps by the model that wrote it;
whether two documents, files or datasets could be chosen and one of the person's saved prompts run on them, to compare and contrast; and what else would make
the tool a daily help. Each was answered with a proposal first. The owner then wrote: "Build everything and go with all of your recommendations."

Three questions stayed open, and were decided as follows so that the work could go on. They are the owner's to change. **Built in the ANDA Companion only**
(the personal app in the other repository was not touched). **The office's policy on AI-written content is not known to this project**, so the tool says
"Follow your office's policy on AI-written content" and names no policy. **PDF files are not read**: that needs a component from outside, which is a decision
of its own.

### What was asked for and what was built instead: AI content

**The tool does not detect AI-written text, and does not name a model.** No reliable way to do either exists: detectors are wrong often enough to do harm,
both ways, and a wrong flag on a colleague's or an applicant's text is a serious thing. What was built, in Review a draft, in both programs, with no AI:

1. **Signs worth a second look.** Fixed rules list four kinds of thing, each quoted from the text: wording an AI assistant often leaves behind ("As an AI
   language model", "I hope this helps", "Here is a revised version"); placeholders that were not filled in ("[insert", "[Your Name]"); formatting marks that
   mail does not use (`**`, `#`, code fences); and citations that someone should confirm (CFR and U.S.C. sections, MAPP numbers, "guidance for industry").
   The list is headed "Worth a second look · not a verdict on who wrote it", and under it stands: "These are signs worth a second look, not a verdict.
   This tool cannot tell who or what wrote a text, and it does not say." With nothing found, it says so and adds: "That says nothing about who or what
   wrote the text."
2. **How to respond**, the same whoever wrote it: check each fact, date and citation against the source; where no source is given, ask the sender for it;
   follow your office's policy on AI-written content.
3. **What a file says about itself.** For a Word document the app quotes the file's own fields (created by, last saved by, last saved on, written with) as
   "The file says: …", followed by "Anyone can change these fields, so they prove nothing."
4. **A second opinion, if wanted**, through the person's own AI connection after the usual preview. The stored prompt asks for up to five signs on each
   side, quoted, forbids a verdict, a score or a percentage, and asks for the facts a careful reader should verify.

### Two files, documents or datasets, and one of my prompts

*Compare two files* now has three ways of working, chosen at the top of the window: **Two files**, **A report against my data** and **My sheet since last
week**.

**Two documents.** Beside tables, the window reads documents: Word (`.docx`), text and Markdown in the app; text and Markdown in the simulation. Two
documents are compared **paragraph by paragraph, as plain text, by fixed rules**: paragraphs that are the same are matched in order; between two matches, a
paragraph that shares at least half of its words with its neighbor is shown as *changed* (both forms, starting a little before the first difference), and
otherwise as *no longer there* and *new*. The summary ends: "Compared as plain text, paragraph by paragraph. Formatting, tracked changes, comments, headers,
footnotes and pictures were not compared." Of a tracked change in a Word document the inserted text is read and the deleted text is not, and the window says
so. A table and a document cannot be compared with each other, and the window says that in words. Limits: 2,000 paragraphs are matched (beyond that the
middle is compared in order, and the summary says so), 60 changes are listed, and the app reads a document up to 5,000,000 characters of text (the
simulation reads a file up to 5 MB).

**Ask with one of my prompts.** Under the comparison is a list of the person's own stored prompts (those that fill in a prompt or ask the AI). *Use this
prompt…* opens the usual preview. **The rules count; the AI explains.** By default the prompt is given only the differences the rules found. The files
themselves go in only when the person ticks "Also give it the beginning of each file (up to 6,000 characters each)", and then the preview shows them, with
their length. The tick is taken off again whenever a file changes or another way of comparing is chosen, so it is a choice made for one pair of files. A
prompt that fills in is shown to copy; a prompt that asks the AI is sent only after Send, and never without the preview, even when the stored prompt says
"send without the preview".

**Check an AI answer** (on the PC, no AI). The person pastes an answer about the files. Every number, date and name with a digit in it is looked up in the
comparison and in the two files, and those that stand nowhere are listed. The line under the result states the limit: "This checks only that each such value
in the answer also stands in the comparison or in the two files. It cannot tell whether the wording or the reasoning is right."

### Five daily helpers

1. **What changed since last week.** *My sheet since last week* compares the person's sheet today with a copy kept earlier. Copies are kept only after the
   person switches it on: one for each week, in the app's own folder on the PC (the app never deletes one by itself). Since round 29, switching it off keeps the copies that exist; only the person deletes them, with a second press to confirm.
2. **Hand-off page** (tray menu). One page of plain text for a colleague who covers: for each ANDA its status line, what needs a look, what is open, and,
   if ticked, the latest notes. It is made from the person's own data and notes by fixed rules; the person copies or saves it and decides who gets it. Its
   second line reads: "From my own data and notes on this PC. It is not the system of record."
3. **Meeting page.** In Ask about a selection, when the selection names ANDAs that are in the person's data (an agenda, for instance), one button makes the
   same kind of page for exactly those.
4. **Waiting on a reply.** When a draft in Review asks for something by a date ("Please respond by Dec 3, 2026"), one button puts "Follow up if no reply" on
   the to-do list for that day. The tool does not read mail, so it cannot know whether a reply came, and it says so.
5. **A report against my data.** One file is chosen. For the ANDAs it names, the goal date and the forecast date are compared with the person's data, and
   the differences are listed. Every result ends: "Only the goal date and the forecast date were checked. Your data is not the system of record."

### How "the same in both programs" is kept true

As before, the simulation writes its answers to a file, `mockup/round27-golden.json` (14 pairs of documents and one very long pair, 6 prompt materials, 8 answers, 9 texts for
signs, 14 reminders, 4 pages and 10 reports; written by `tools/round27-golden.cjs`), and the app's tests must reproduce every answer, character for
character.

### What the security review of this round changed

The same two skill sets as in round 26 supplied the checklist. Each item below was a finding, a fix and a test in both programs where it applies.

1. **The tick for sending the beginning of the files stayed on** for the next pair of files. It is now taken off whenever a file changes or another way of
   comparing is chosen.
2. **A backup from another PC could switch the weekly copy on.** Keeping copies is now one of the choices that belong to this PC: a restored backup, a
   backup edited by hand and a reset leave it as it is.
3. **A small Word file can unpack to a very large text.** The reader now stops at a fixed number of characters and refuses a document with more than
   5,000,000 characters of text, in plain words, without first holding it in memory.
4. **The check of an answer slowed with the square of the text.** It now reads the text once: two million characters of facts and an answer at the limit
   are checked in under a second here. Each file is searched up to its first 1,000,000 characters, and the window says when that limit was reached.
5. **The answer box cut a long paste at 20,000 characters without saying so**, so a check could pass on half an answer. The box now takes 200,000
   characters, and a longer answer is not checked and is said to be too long.
6. **"Include my notes" was ticked when a hand-off or meeting page opened.** Notes are the person's own and can be candid, and the page is made to be
   passed on. The box is now off until ticked, each time a page opens.

Checked and left as they were: the new simulation code builds its windows from text nodes only; a document is read with document type definitions and
outside entities refused; what a prompt is given is cleaned of control characters and put inside the same fence as any selection; a hand-off page is plain
text that is never sent; the weekly copies have fixed names inside the app's own folder; no package has a known vulnerability; a build with every .NET
security rule switched on raises none.

### What this round changes in an earlier promise

Round 26 said of the narrative that it holds the summary and the listed changes, "never the files". That is still true of *Write a narrative with AI…*. It is
no longer true of the whole window: *Ask with one of my prompts* can hold the beginning of each file, when the person ticks the box for that pair of files,
after a preview that shows every character. `SECURITY.md` states both.

**Still only in the simulation:** as listed under round 26. The extension has none of rounds 26 and 27. **Only in the Windows app:** Word documents and
Excel workbooks (the simulation reads CSV, text and Markdown), and real weekly copies (the simulation shows the feature with its sample sheets).

## 11l. Round 28: an audit against the owner's standing rules, an independent adversarial review, and the fixes

The owner set standing rules for every tool built for a federal Windows 11 desk (no script launchers, nothing sent by default, no automatic
deletion of what may be a record, Section 508, a private risk log, an adversarial review by someone other than the builder) and asked for the
existing product to be checked against them, changing nothing. The audit found 27 gaps; a separate reviewer, who had not built the product,
reproduced the most serious ones. The owner then wrote: "Yes fix and proceed with all recommendations". This section records what was fixed and
what was left, and why. New features were listed, not built.

**What was fixed, in order of seriousness.**

1. **An AI connection the person did not choose.** A new install is "Offline only". Before, the default "Auto" used Claude whenever a key
   happened to be on the PC. "Auto" is still offered, as a choice. Settings from an earlier version are moved forward once, and the app says so.
2. **Files that were overwritten when they could not be read.** Settings, both to-do lists, prompts and replies: a file that is damaged, cut
   off or too large is set aside under a name that says when, never written over. Writes are made beside the file and moved over it. Settings keep
   the file of the last good start and carry a version. What a store had to do is said once at start.
3. **Finished items deleted by the app.** They are now added to an archive file beside the list, which is only ever added to. A window to
   search and restore them is not built.
4. **Values from the sheet in a prompt for an AI, and the fence.** Characters nobody can see are removed and counted in the preview; a bracket
   that looks like "<" counts as one; neither tag of the fence can be written inside it; a value from the sheet is one line with nothing hidden.
   The security document said records reach a model only inside a fence; that was not true of the person's own prompts and is corrected.
5. **A prompt name shown as markup in the simulation.** It is text.
6. **A workbook that could stop the app at every start.** Whatever a file does to the reader, the app starts and says the file could not be
   read; the reader also stops at a fixed number of characters.
7. **Keyboard and screen reader.** The invite, which takes no focus, is accepted by pressing the same shortcut again, and says so. In the
   simulation a window takes the keyboard when it opens and gives it back where it was; Escape closes the window in use; at phone width Tab
   stays inside the open window; notices are announced. In the app, status lines are announced and four unnamed boxes have names.
8. **A restore.** It keeps the lists a backup does not hold, saves what it replaces as a backup of its own, and switches no connection on.
9. **Smaller ones.** A damaged weekly copy no longer blocks the week; the age of an agent's finding is measured in UTC; the simulation asks
   for the time zone each time.
10. **The `.bat` launcher is gone.** A folder named `data` beside the program keeps the demo out of the person's profile.
11. **The 40 browser suites are in the repository** (`tools/sim-tests/`), with a runner that needs no shell script. Until now they existed only
    in the builder's session.

**What was not changed, and why.** Each of these is the owner's decision or a feature, and all are in the risk log:

- The app still reads a selection by sending Ctrl+C and reading the clipboard (on by default), can install a global mouse hook for the optional
  middle-click trigger (off by default), and ships screen-capture code used only by its self-test. An endpoint-security product may read any of
  these as spyware-like. The lookup box and the extension's right-click menu need none of them.
- The Windows app is an unsigned program, which the target computers do not allow without approval. The simulation is one HTML file that needs
  JavaScript; whether that is approved there is not known.
- Not built: a short first-run guide; backups of stored items on every change with one-click restore; an archive window; an event log of
  connections, data sent and errors; a connection indicator in every window; a version and change-log screen; a time-zone setting.
- Still in code rather than in Settings: the default model name, the Elsa endpoint, and extra closure dates.
- The tray count's timer is still armed from the wall clock.

**The owner's answers after the report.** All three programs stay in scope (the Windows app, the simulation and the Edge extension), with the
points above about the app known. JavaScript in a local HTML file is, in the owner's words, thought to be acceptable on the target computer;
that is the owner's judgment, not a confirmation from IT, and it is recorded as such. Both published pages were republished on the owner's word.
The standards the audit listed as not met are to be built; they are taken in rounds, the simulation first.

## 11m. Round 29: what is switched on, said in every window; an event log; what changed

The owner's standing rules ask every tool to show its connections at all times ("Offline · Local only", or each by name with what is sent, where
and what is kept), to keep a local event log that is only added to, can be read and exported, and never holds the person's content, and to show its
version and a change log. The audit of round 28 listed all three as not met. After the report the owner wrote "Do everything", so they are built,
the simulation first. The remaining standards (backups of stored items with one-click restore and an archive window; a time-zone setting and
freshness labels; a first-run guide) are the next rounds.

**The line.** Every window carries one line at its foot. With nothing switched on it reads "Offline · Local only". Otherwise it names what is on:
the AI connection, each agent, Outlook. More than three are counted ("and 2 more"). It follows a change in Settings at once. It is a button: one
press opens the list.

**The list** (*Connections and event log*, also in the tray menu). For each connection, three sentences: what it sends, where, and what is kept.
They are fixed sentences, taken from what `SECURITY.md` already states and the code does; only the name and the host are filled in. For an address,
the host alone is shown. A last sentence names the two things that reach the network only when pressed ("Update now" and "Open ANDA page"), so
that the list is complete without calling them connections.

**"Switched on" and "connected" are different words on purpose.** The line is about settings, and is true as soon as a setting changes. The card's
label still says "connected" only after a real request succeeded, as it has since round 10. An AI connection counts as switched on only when it is
chosen in Settings and complete.

**The event log.** One line for each event: a time in UTC, one of seven kinds, and a short detail. It records that something happened and how
large it was, never what it was: "AI request, 1,234 characters", never the prompt; "agent request", never which agent; "settings restored from a
backup", never from where. The rule that keeps content out is in the one function that makes a line, so no caller can get around it: **a detail is
kept only when it is one of the log's own sentences**, a fixed list of 34 in which only a count varies. Anything else is written as "(detail
withheld: not one of the log's own sentences)". The first form of the rule allowed any plain words; the review showed that a name or an address
is plain words too, so the list replaced it. A request is written **before** it leaves, and a second line follows if no answer came, so a request
that timed out is in the log. One line at start says what is already on. The window shows times in the person's own time zone with its
abbreviation and the right daylight time; the file and the export hold UTC. The window can copy or save the log and has no way to clear it.

**About and what changed.** The version ("Prototype, round 29") and the changes of the last four rounds, the same list in both programs. It says
that the tool does not update itself.

**Kept copies of the sheet.** Switching the weekly copy off used to delete the copies. Now it keeps them and stops making new ones, and they can
still be compared. "Delete the kept copies…" asks with a second press, and the deletion is written to the event log.

**How the two programs are kept the same.** `mockup/round29-golden.json` (written by `tools/round29-golden.cjs`) holds the simulation's answers for
8 states of what is switched on, 34 events, the log's 34 sentences, 13 changes of what is switched on, the CSV and the change list; the app's tests must reproduce them character for character.

**In the app only:** the event log is a file (`events.csv`); in the simulation it lasts until the page is closed. **Not in the extension.**

**After the separate review** (12 findings; `VERIFICATION.md`, round 29): the app no longer deletes kept copies beyond five; a delete keeps no
fresh copy in the same press and cannot be set off by a double-click; the log holds only its own sentences and is written before a request
goes; connection tests, the Outlook sign-in and a change of destination are written too; the line is read out as it is shown; three sentences
in the list were corrected to what the code does (the whole record goes to the AI when a card opens; an agent's answer stays in memory until
the app closes; Outlook's sign-in address is named); the Connections list scrolls; and in the simulation the line stays in sight at the foot of
a window that scrolls.

## 11n. Round 30: a copy of each list after every change, one press to put a copy back, the archive searched and put back from

The owner's standing rules ask that stored items get a local backup on every change and daily, with a number of versions the person sets and
one-click restore; that finished items are archived after a number of days the person sets, and can be searched and restored; and that nothing
is deleted by the tool on its own. Round 28 stopped the deleting; this round builds the rest. Before it started the owner was asked one question,
because two of the rules pull against each other: a set number of versions means older copies go, and "no automatic deletion" means nothing goes.
The answer: **backup copies follow the number the person sets, and the screen says that older copies are removed; the lists and the archive
themselves are never removed by the app.**

**What is copied.** The five lists the person keeps in the tool: the ANDA to-do list, My to-do list, notes on ANDAs, the prompt library, and
quick replies. Settings are not among them: they have their own last good copy, their own export and their own copy before a restore (round 28).

**When.** After every change that is written, and once at start (so a list that was never copied, or changed while the app was closed, has a
copy). A list whose text equals its newest copy is not copied again. So the newest copy is always the list as it is now.

**How many stay.** One number N, chosen in the window (10, 30, 60 or 100; 30 unless changed): the newest N copies, and the last copy of each of
the newest N days that have one. The second half is the "daily" part of the rule, and it is what keeps a busy afternoon from pushing out last
week. A day is the person's day, in their time zone: the night the clocks change is one day, 25 or 23 hours long, not two days and not none.
Choosing a smaller number would remove copies, and removing cannot be undone, so it is asked about first: the window says how many copies
would go and offers one button to go ahead; until that button is pressed nothing is removed and the number is not changed. Choosing a larger
number removes nothing. The two choices of this window belong to this PC, like the lists: a settings backup does not carry them, and a
restore or a reset of settings leaves them alone, so neither can remove copies unasked.

**Putting a copy back** is one press. Nothing is asked, because nothing is lost: what was there a moment before is among the copies, what is
there after the restore becomes the newest copy, and the window says so. Undoing a restore is restoring the copy before it. That promise is
checked, not assumed: before anything is written, the app makes sure that what is there now is among the copies, and if it cannot be copied
(the copies folder cannot be written, or the list has grown past what the app reads) the restore is refused and nothing is changed. A copy is
read by the list's own reader before it is used, so a damaged copy is refused, not put in the list's place.

**The archive.** Finished items leave their list after a number of days the person chooses (7, 30, 90 or 365; 30 unless changed) and are kept in
the archive, as since round 28. The window now shows the archive of both lists, newest first, searches it by words in the title or digits of the
number, and puts an item back on its list as an open item. The archive only ever gains lines: putting an item back adds a line that says so,
and the item's own line stays. What comes back from the archive is cleaned the way a typed item is, because the archive is a plain file.

**Nothing is out of sight.** A list shows a finished item for seven days, and it is archived after the days the person chose, which can now be
a year. In between, the item is in the list's file but shown nowhere; the separate review found this. The window therefore also lists the
finished items that are still on a list and no longer shown there, searches them with the archive, and opens one again with one press.

**A clock that was ahead.** A copy stamped later than now was made while the PC's clock was ahead. Left alone it would stay "the newest" for
ever and every later copy would be filed on its day. At the next change such a copy is given the latest moments before now, in its order.
Copies are shown with their year.

**After the separate review** (12 findings; `VERIFICATION.md`, round 30): everything in the four paragraphs above that says "checked",
"asked about first" or "refused" was added or corrected then, with a test for each.

**Not built, and why.** A free manual order of items ("reorder"): both lists are ordered by urgency and date on purpose, so a hand-made order
would fight the rule that puts what is due first. An item is moved up by marking it urgent or giving it a date. This is in the report to the
owner as a question, not worked around. Also not built: backup copies of the weekly sheet copies or of the event log; the extension.

**How the two programs are kept the same.** `mockup/round30-golden.json` (written by `tools/round30-golden.cjs`) holds the simulation's answers
for 20 moments and the day each falls on (both clock-change nights in New York, and zones far from UTC), 12 sets of copies and which of them
stay, 6 sets of copies from a clock that was ahead, and 14 views of an archive with damaged lines in it; the app's tests reproduce them.

**In the app only:** the copies are files (`backups/<list>/<list>.<UTC time>.json` in the app's folder). In the simulation they last until the
page is closed.

## 11o. Round 31: every time in the person's time zone, and data that says how old it is

The owner's standing rules ask for a live clock; a time zone that is found by itself and can be changed, shown with its abbreviation and
the right daylight time; storage in UTC; "how long ago" that is brought up to date every minute; and, for every piece of data, its source,
its "as of" time and a stale label after a period the person sets. Before this round the app wrote times in three different forms, one of
them (the event log) with an abbreviation built from the words of the zone's Windows name, and the card said only "saved Nov 11".

**One clock, in both programs.** Six rules, the same in the simulation (`zoneClock`, `zoneAbbr`, `timeLabel`, `timeOfDay`, `relTime`,
`freshLine`) and in the app (`Services/Clock.cs`), held together by `mockup/round31-golden.json`: 225 moments written in 15 zones (both
nights the clocks change in 2026 from each side, a year's end, a leap day), 24 ages, 88 data lines.

- *The zone.* This computer's own, unless one of ten is chosen in Settings: Eastern, Central, Mountain, Arizona, Pacific, Alaska, Hawaii,
  Atlantic (Puerto Rico), UTC, or "Automatic". The choice is stored by the zone's IANA name; a name that is not one of the ten is read as
  "Automatic". The zone's rules come from the operating system (in the browser, from the browser).
- *The abbreviation.* The tool knows the winter and summer abbreviations of 33 zones of the United States (the eight offered, and those a
  computer set to "Automatic" may be in: Detroit, the Indiana and Kentucky zones, Boise, the Alaska zones, Adak, Guam, Samoa), and for each
  how far its standard time is from UTC. It writes the abbreviation only when the moment is in 2007 or later and the zone is then at that
  standard time (EST) or an hour ahead of it (EDT). In every other case (another zone, an earlier year, a zone this computer has no data
  for) it writes the difference from UTC ("UTC+5:30"), which is always right, where a guessed abbreviation might not be. On Windows the
  computer's zone has a Windows name ("Eastern Standard Time"); the app asks .NET for its IANA name first, so "Automatic" in Washington
  reads EST and EDT like "Eastern" does.
- *A moment as it is written.* "Nov 18, 2026, 3:12 PM EST", with seconds where the list is a log (the event log, the backup copies). A
  time of day alone, for something of today, is "3:12 PM EST" (the to-do list's "Updated", an agent's "Asked at", "checked" on the
  connection line). The year is always there: two copies a year apart must not read alike.
- *How long ago.* Whole units, never rounded up: under a minute is "just now"; then minutes, hours, days. A day is 24 hours of real
  time, so the week in which the clocks go back is still seven days. A time more than a minute later than now is not given an age at
  all: "later than now: please check this computer's clock".
- *The data line.* `source · as of <moment> (<how long ago>)`, and, once the data is older than the days chosen, ` · STALE: older than
  N days`. Exactly N days is not older than N days. A piece of data whose time is not known says "time not known" and is not called stale.

**Stored in UTC.** The event log and the names of backup copies already were. A note on an ANDA is now kept as its moment in UTC (a note
written by an earlier version, with its difference from UTC, is read as the same moment), and so is the time a settings backup was saved.
Changing the zone therefore changes how a time is written and never when it was; Settings says so in those words.

**Stale by one rule.** The card's line and the watch rule `stale-data` used to count differently (the rule in whole calendar days). They now
share one reckoning, to the minute, wherever the record carries the moment its data was taken and the caller knows the time
(`CardContext.Watch`, used by the card, the watch list, the to-do list and the hand-off page): the watch line appears at the minute the
card's line turns. Where a record has only a date, the rule counts whole days as before.

**The two timers.** The card and Settings each start a timer when they open and stop it when they close. Once a minute it reads the clock
and repaints one line (on the card also the watch line, when "stale" changed). Nothing else is done by them. A change of zone or of the
days in Settings repaints an open card, the Connections window and the Backups window at once.

**Where the two programs differ, on purpose.** The simulation's sample data is moved forward to stay current, so it is "read at 7:12 this
morning" and has an age. The app's sample records are fixed at Jul 16, 2026, and their rules treat that day as today, so the app says
*sample data, which does not change* in place of an age and never calls them stale. A file of the person's own behaves the same in both.

**What does not change with the zone.** Which day is "today", for due dates and day counts, stays this computer's day; Settings says so.
A person who chooses a zone other than the computer's sees times in that zone and due dates by the computer's calendar. This was left as it
is because every date rule in both programs is built on one "today", and moving it is a change to all of them, not to how a time is written.

**After the separate review** (15 findings and three smaller ones; `VERIFICATION.md`, round 31). What changed in the design: the browser
host follows both settings and gives the extension the card's own line; the hand-off and meeting pages say "made on" and carry a "Data:"
line; a compact box writes STALE on itself and carries the line as its tooltip; the simulation has the app's stale rule in its watch list; an
open card's watch line is reckoned again with its data line, and a tick is not counted in the usage log; the day in the watch flag and the
portfolio's status line follow a change of zone; a zone the computer has no data for is not used and Settings says so; the computer's own
zone is asked afresh at most once a minute, so "Automatic" follows a change made in Windows; the timer in Settings fires as each minute of
the clock begins; a moment outside the years 1900 to 9999 is "time not known"; and backup copies are counted by the computer's days, not
the chosen zone's, so that changing the zone (or restoring a settings backup that carries one) can never remove a copy. Left open and said
so: Windows has no zone of Puerto Rico's own, so "Automatic" there reads UTC-4; a compact box does not print source and "as of" on itself.

**The reference file, decided by the owner on Nov 19 and built at the start of round 32.** The Drugs@FDA file is a public file that changes
about monthly; with the seven days of the person's own data it would read STALE nearly always, and a mark that is always on teaches people
to ignore it. It therefore has a number of days of its own (14, 30, 45, 60 or 90; 45 unless changed, in Settings beside the other two
choices). The card's line reads *Sep 17, 2025 · Drugs@FDA, file saved Nov 1, 2026 (18 days ago)*, and *· STALE: older than 45 days* once
that is so, in words and by its look; it keeps step while the card is open; the app's browser host says the same. One rule in both
programs (`refSaved` / `Clock.RefSaved`), held by `mockup/round32-golden.json` (98 files). The settings file moves to version 4 by itself.

**Settings.** `TimeZone` and `StaleAfterDays` are in the settings file, which moves from version 2 to 3 by itself (the two take their
usual values: the computer's zone, seven days). A value that is not one of the choices becomes the usual one. A settings backup carries
both. The Time part is in "Look and feel", which is shown in the short edition of Settings, so the choice is reachable by default.

## 11p. Round 32: first steps, a guide of five steps; and the reference file's own days

The owner's standing rules ask for a first-run guide that can be skipped, has at most five steps, can be opened again from Help, and uses
sample data that is labeled as such. The project once had a first-run panel and removed it at the owner's direction (round 17: the
tool should work as it is, with nothing to set up first). So the owner was asked before one came back, and decided two things on Nov 19:
**the guide opens by itself once, at the first start, and never again by itself**; and **these five steps**.

1. *What this is.* What the card is for; that the tool starts with sample data, every record of which is fictional and says so on its card;
   that nothing leaves the PC unless a connection is switched on.
2. *Try it on a sample.* Select a number and press the person's own shortcut (the sentence names it), or press **Show the sample card**.
3. *Use your own data.* Where the portfolio file is chosen; that it is read on this PC. **Open Settings** opens that section.
4. *What is switched on.* The line at the foot of every window, with what it says at that moment. **Open Connections**.
5. *Where things are.* The tray menu, and that this guide is there under *Help: first steps*.

**How it behaves.** One step at a time, with its place ("Step 2 of 5"). *Next* is the default button, so Enter walks it; *Back*; *Skip the
guide* at every step (it reads *Close* on the last, where *Next* reads *Done*); Esc closes it. The guide asks for nothing and changes
none of the person's settings or data: its three buttons open a card of the bundled sample record (whatever data of the person's own is
loaded), Settings at the files, and the list of connections, and each says in a status line what it did. What it opens is in front of it.

**After the separate review** (15 findings; `VERIFICATION.md`, round 32). The sample card is always the full card, stays until it is closed
(a card that hid itself after ten seconds would have made the guide's sentence false), and Esc closes it before it closes the guide. With
the person's own data loaded, notes are off on the sample card and the guide says so, because a note is kept under the ANDA number and
would land among their own. Four sentences of the guide were not true of the product and were rewritten (what "leaves this PC", the weekly
copy, which windows carry the foot line, what Esc does on the card). A shortcut another program holds is not taught: step 2 says so.
Where the PC does not let the settings be saved, the guide says it will open again. Opening Settings from the guide is not counted as a
section the person uses. The first-start rule (`FirstStart`) and the guide's wiring (`FirstStepsWiring`) were taken out of the app's
composition so that they can be tested; what remains there is held by reading its text, and is said to be.

**Once.** In the app, the first start is the start at which the settings say the tool has not run before (`FirstRunDone`, which already
existed: until this round the sample card opened by itself at that start; the guide takes its place and offers the card as its second
step). That it was shown is saved *before* it opens, so a crash, a closed window or a second start cannot bring it up by itself again. A
diagnostic run never opens it. The mark belongs to the PC: a settings backup does not carry it and a reset does not clear it. A person who
had the tool before this round does not see the guide by itself; it is in the tray menu. In the simulation the mark is kept in the
browser's storage; a browser that is being driven by a test opens the guide by itself only when the address says `first=1`, so that the
suites written before it start as they always did.

**One text in both programs.** `GUIDE_STEPS` and `guideStep` in the simulation, `Guide.Steps` and `Guide.Step` in the app, held by
`mockup/round32-golden.json` (the five steps; 20 views of a step, with and without a shortcut, with several connection lines, and with
step numbers outside one to five; 7 starts). The shortcut and the connection line are written into the text in one pass, so nothing in them
is read as a mark.

**The reference file's own days** are described at the end of §11o.

## 11q. Round 33: the Windows app and the Edge extension are brought level

The simulation is where a feature is tried first. By round 32 it was ahead of the Windows app in five places (rounds 18 and 19) and ahead of the Edge extension in everything since round 26. Round 33 closes both gaps, by the owner's go-ahead of Nov 19.

**How the rules are shared.** For the app, the simulation's rules for the five features were first separated from the windows that show them (`detailRows`, `recentAfter`, `pinAfter`, `listedOf`, `barRows`, `lookupError`, `todayGroups`, `todayMain`), without changing what they do, and `tools/round33-golden.cjs` writes their answers to `mockup/round33-golden.json`. The app's rules (`Services/CardDetails.cs`, `Recents.cs`, `OneBar.cs`, `Today.cs`, `Display.cs`) must give the same answers. For the extension, `src/core/time.js` is the simulation's own text for writing a time; a test compares the two texts and holds the copy to the round 31 and round 32 answers.

**Card details.** Twelve columns of the person's own sheet can be shown on an expanded card, nine by default. A portfolio file gives them by header name (`PortfolioColumns.DetailSynonyms`); the table is kept apart from the thirteen fields the card already reads, because the extension mirrors those column for column. Two headers were already in use under another name before this round ("Established name" as the drug or ingredient, "Project description" as the notes) and are shown under both. Each value has Copy; "Copied" is said only when the text reached the clipboard, and otherwise the person is told to select it and press Ctrl+C (the values can be selected). The details are not part of what the browser extension is sent.

**Text size and spacing.** The simulation enlarges only the letters and lets lines wrap. In the app every size is written into the windows, so the app draws a window's whole content 1.15 or 1.3 times as large (`UI/DisplayScale.cs`): the letters grow by exactly the factor, and so does the window. A window with a fixed size grows with it, never beyond the screen it is on. The line at the foot that says what is switched on is inside the enlarged part. Compact spacing keeps 0.72 of the space above and below nine named parts of the card; the sides are left alone. Both follow the setting at once, on open windows too.

**Recent and kept ANDAs.** Recent: the last eight six-digit numbers whose card was opened, newest first, in this PC's settings; never in a backup; cleared when the person switches remembering off. Kept: five at most, by one press on the card; a sixth is refused in words; saved in a backup. The sample card that the guide opens beside the person's own data offers no list.

**One Bar.** A second style of the lookup window: a row for the ANDA when a number is typed, a row to review what was typed as a draft, and a row for Ask. Arrow keys move, Enter opens the selected row, and typing opens nothing. The simulation also builds rows from the text selected in its pretend windows. The app does not: reading a selection there would mean one more simulated copy on one more shortcut, and the lookup shortcut has never touched the clipboard. The lookup window now also says why something is not an ANDA number (an NDA number, a number written with a space).

**Today.** Left: what needs a look, the kept ones, the recent ones. Right: the selected ANDA with its headline, dates, what needs the person, the first watch flag, Copy status and Open the card. "Needs a look" is the most pressing first (red before amber, then by number), in both programs from this round; the app shows forty at most and says how many more are in the watch list. Each row's mark is a color and is also said in words in the row's name. The window reads the data when it is opened; nothing runs while it is closed.

**The extension.** Every card carries the data line of round 31 (source, as of when with the zone, how long ago, STALE past the days chosen), from the same rule as the app (`dataLine`); the fictional records say that they are sample data that does not change. While a card is open, on a page or in the toolbar window, the line is asked for again as each minute begins and only that line is rewritten. The moment a spreadsheet was saved is stored in UTC (a spreadsheet stored by version 0.1.0 as wall-clock parts is still read). Settings carry the number of their form. The Settings page and the toolbar window say what is switched on. First steps opens once in a tab after an installation; its own five steps describe the extension, and a test checks the statements that can be checked against the code. Version 0.2.0.

**After the separate review** (17 findings, all fixed; see VERIFICATION, Round 33): the extension's minute ages only the moment the open card was built from, so a card never starts naming a file imported after it opened; a change of text size reaches windows opened at standard size, and Settings stays on its screen at larger text; One Bar starts again at the first row when the person types, says each move to a screen reader, and answers Enter on an NDA number with the reason instead of a review; Today keeps the keyboard on a pressed row, says one short sentence, and shows each mark as a word; a restore or a reset forgets the recent numbers when remembering is off or reset; Settings gets *Clear my list*; the Copy buttons' names keep their capitals ("Copy US agent email").

**What is not the same, said plainly.** Larger text in the app enlarges the window, not only the letters. One Bar in the app does not read a selection. The app's Today caps "needs a look" at forty. Detail columns are found by header name only; there is no box in Settings to choose them by hand. The extension has no event log and no export, import or reset of its settings. Notices near the clock and division news (round 19 in the simulation) are still not in the app.

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
7. **Grounded proofread** *(built: Ctrl+Alt+Shift+R draft check with no AI, plus Proofread, Rewrite and Fact-check that stay off until
   a model is approved; never run against a live model)* — a second hotkey on the same
   select-and-press mechanism. It checks a selected draft against the record before
   anything is sent: dates, drug names and numbers that do not match are flagged first,
   deterministically and offline, then optional AI suggestions for wording. It never
   rewrites in place; the user copies the corrected text. Constraints that must hold:
   the selected text is arbitrary user content, so it is a wider data surface than the
   structured card record and needs its own approval; it uses its own hotkey so it can
   never fire by accident; the app shows exactly what will be sent; and it costs roughly
   2,000 tokens per proofread, so it is metered separately from card briefs.
