# ANDA Hover Companion — UX & Product Design Proposal

**A Windows tray micro-app for FDA OGD Regulatory Project Managers.**
Point at an ANDA number anywhere on screen → get a calm, one-glance briefing card:
status, clock, the one thing that needs you, and a grounded AI brief of risks and blind spots.

> Status: design proposal + interactive mockup (`mockup/anda-card.html`). No FDA data,
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
numbers. Invalid → in manual modes, a 1-line inline whisper ("Not a recognized ANDA");
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

**Appearance is a preference, never information.** The card ships with **five
selectable appearances** (Windows app: Settings → Appearance). **A · Quiet Fluent is
the default**; the rest are user options. All five share identical anatomy, slot
order, status semantics, and interactions — switching appearance never moves, adds,
or removes information:

| | Appearance | Character | Progress form |
|---|---|---|---|
| **A** | **Quiet Fluent** *(default)* | Native Windows calm — white surface, steel-blue accent, system type | Thin 4 px bar |
| B | Docket Ledger | Typeset federal docket — warm paper, archival green, serif title, stamp seal | Milestone ticks |
| C | Signal | Mission-control dark — deep navy, steel-blue accent, mono numerals; commits to dark | Ring |
| D | Draft | Engineer's drawing — federal navy ink on white, graph grid, drafting mono; dark mode is a true blueprint | Hatched rule |
| E | Instrument | Medical-device porcelain — slate steel on clinical white, capsule geometry, soft large radii | Capsule |

**Palette policy.** Accent hues are anchored to the **U.S. Web Design System** federal
palette (e.g. D's navy = USWDS `primary-dark #1a4480`, blueprint ground = `primary-darker
#162e51`) so every appearance reads government-professional, and the status colors
(good / warn / bad) keep one consistent semantic family across all five. Within that
conservative range, the appearances differentiate through *material, typography, and
geometry* — paper and serif, grid and mono, glass, porcelain — rather than louder hues.

- **Native-feeling Windows 11:** Mica/acrylic card, 10 px radius, Segoe UI Variable,
  system light/dark followed automatically, `prefers-reduced-motion` respected
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

## 10. Mockup — the design studio

Open **`mockup/anda-card.html`** in any browser, desktop or phone. It demonstrates,
with fictional data:

- **Five appearances** (the §5 table), switchable live — the studio picker is the
  prototype of the app's Settings → Appearance page. A is the default.
- **Three consent models** (§2), switchable live, plus long-press on touch.
- The forecast-first clock (goal vs. forecast with delta + confidence), the single
  needs-attention signal, the streamed companion brief, pin / expand / keyboard.
- **Touch-first presentation:** on phones and tablets the card presents as a bottom
  sheet with a drag handle, scrim, safe-area padding, and 44 px targets — tested
  against iOS-style touch emulation.
- The invalid-number whisper, light/dark themes (all appearances except C, which is
  deliberately dark-only).

## 11. Implementation status — the Windows app

**`app/`** contains the working v0: a .NET 8 / Avalonia tray application with all five
appearances (A default) selectable from the tray menu, all three consent models,
global hotkeys (`Ctrl+Alt+A` Select & Peek, `Ctrl+Alt+Space` lookup), the
forecast-first card with AHEAD line and expandable drawer, persisted settings, and a
companion brief via the official Anthropic SDK (`claude-opus-4-8`) with a deterministic
offline fallback. A headless preview harness renders the real card to PNGs for review
without a Windows box. See `app/README.md`.
