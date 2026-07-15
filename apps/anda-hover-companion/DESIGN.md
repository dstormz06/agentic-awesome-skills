# ANDA Hover Companion — UX & Product Design Proposal

**A Windows tray micro-app for FDA OGD Regulatory Project Managers.**
Point at an ANDA number anywhere on screen → get a calm, one-glance briefing card:
status, clock, the one thing that needs you, and a grounded AI brief of risks and blind spots.

> Status: design proposal + interactive mockup (`mockup/anda-card.html`). No FDA data,
> systems, or branding are used — all names, numbers, and records in the mockup are fictional.

---

## 1. Design north star

**The RPM's question is never "show me everything about this ANDA." It is:
"what is this, where does it stand, what needs me, and when is the clock?"**

Everything below optimizes for answering those four questions in **under 5 seconds,
with zero clicks**, and making everything else *available* but never *present* by default.

Three principles drive every decision:

1. **Progressive disclosure, three levels.** A glyph → a peek card → an expanded drawer.
   The RPM chooses how deep to go; the app never chooses for them.
2. **One primary signal.** The card surfaces exactly **one** "needs attention" line —
   the most urgent item — not a list of twelve statuses. If nothing needs attention,
   that slot simply doesn't exist (absence is information).
3. **Deterministic facts and AI synthesis never mix.** Dates, statuses, and submission
   history render directly from source data. Claude writes only the clearly-labeled
   "AI brief" slot, streamed in after the facts are already on screen, with citations
   back to the notes/submissions it drew from.

---

## 2. Activation model — why "hover everywhere" is the wrong default

True global hover (the app watches every mouse movement and pops a card whenever the
cursor crosses six digits) is the highest-cognitive-load version of this idea: it
interrupts work the RPM didn't ask to interrupt, fires false positives on phone numbers
and tracking IDs, and trains the user to fear their own cursor. An app that appears
unbidden is a distraction machine, not a lifesaver.

Recommended activation ladder (ship in this order):

| Mode | Gesture | Default | Notes |
|---|---|---|---|
| **Point & Peek** | Hover the cursor over an ANDA number, tap a hotkey (e.g. double-`Ctrl` or `Ctrl+Alt+A`) | ✅ on | Zero false positives; the user's intent is explicit. Reads the text under the cursor via UI Automation, falls back to selected text, then to a small OCR crop. |
| **Select & Peek** | Select any text containing an ANDA number, same hotkey | ✅ on | Works in apps UI Automation can't read (Citrix, PDFs, images via OCR). |
| **Dwell hover (trusted apps only)** | 600 ms dwell over a recognized ANDA pattern in apps the user has allow-listed (e.g. Outlook, Excel) | ⬜ off | Even then: dwell first shows only a 16 px underline glyph; the card opens on a further 400 ms dwell or a click of the glyph. Two-stage consent, no surprise cards. |
| **Tray search** | Click tray icon or `Ctrl+Alt+Space`, type/paste a number | ✅ on | The fallback that always works; also the demo path. |

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
│ GOAL  Oct 3, 2026 · 80 days      ▓▓▓▓▓░░ 62% │  ← the clock: one date, one thin bar
├──────────────────────────────────────────────┤
│ ⚠ Needs you · IR response due in 6 days      │  ← ONE line, only if something needs attention
├──────────────────────────────────────────────┤
│ Latest: Amendment (minor) received Jun 30    │  ← most recent event, one line
├──────────────────────────────────────────────┤
│ AI BRIEF · generated — verify    [sources]   │  ← streams in ~1–2 s, never blocks the card
│ Two disciplines complete; biopharm IR is the │
│ critical path. Sister ANDA 214990 received a │
│ facility-related IR last cycle — same site.  │
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

## 6. Where Claude fits (and where it deliberately doesn't)

**Claude never renders facts.** Dates, statuses, submission events, and note text come
straight from the data layer. Claude does exactly one job per card: the **AI brief** —
2–4 sentences synthesizing risk, blind spots, cross-ANDA signals, and unstated
assumptions from the structured record + recent notes.

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

**App shell:** .NET 8 + WinUI 3 (or WPF) tray app is the pragmatic choice for an FDA
Windows estate — first-class UI Automation for text-under-cursor, MSIX/Intune
deployment, and native acrylic. (Tauri/Rust is a fine alternative if the team prefers
web UI inside the card.) Local cache in SQLite, populated from whatever sanctioned
exports/APIs the systems of record allow.

---

## 7. What's intentionally *not* in v1

Cutting these is a feature — each one erodes the "calm" contract:

- ❌ Notifications, badges, or any proactive interruption
- ❌ A dashboard window / "my portfolio" view (different product)
- ❌ Editing data or writing notes back (read-only builds trust first)
- ❌ Charts, gauges, KPI tiles inside the card
- ❌ Chat with the AI inside the card (a "ask a follow-up" door can open the org's
  approved Claude surface later)
- ❌ Auto-hover everywhere (dwell mode stays allow-listed and off by default)

---

## 8. Success measures

- **Time-to-answer:** median < 5 s from gesture to the RPM knowing status + next action
  (baseline: minutes of system-of-record navigation).
- **Zero-regret rate:** % of card opens the user doesn't immediately dismiss (>80 %).
- **AI trust:** % of briefs where the user opens `[sources]` and doesn't flag an error;
  a one-click 👎 on the brief feeds review.
- **Silence:** false-activation reports per user-week ≈ 0.

---

## 9. Mockup

Open **`mockup/anda-card.html`** in any browser. It demonstrates, with fictional data:

- The trigger experience (recognized ANDA numbers inside a fake email/tracker page)
- The peek card: healthy and at-risk scenarios, streaming AI brief, pin, expand
- The invalid-number whisper, light/dark themes, keyboard shortcuts (`Esc`, `E`, `Space`)
