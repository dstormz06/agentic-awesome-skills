# One-pager: pilot proposal for the ANDA Hover Companion

*Template for the director ask. Adapt names and numbers to your division. Keep to one page. All data in the
prototype is fictional, and it is not an FDA product.*

**The ask.** Approve a **Product Catalog and security review** (the agency's route for new software and any AI
use) for a **4-week pilot** of a small Windows tray tool with the **DPM AI Testers group**. The pilot runs
**offline first**: no AI call, no network. AI is a later, separate step, only through an agency-approved platform
(Elsa first) and only after that platform's own approval.

**What it does.** An RPM selects an ANDA number anywhere on screen and presses a hotkey. A small card shows the goal
date against a forecast, the one item that needs attention, what lands next, and a one-click **Copy status** sentence
for the email or tracker reply. Everything on the card comes from the record; nothing is generated. An optional
AI-written brief is clearly labelled "generated, verify before acting" and is off until approved.

**Where it fits the work, and how each is measured** (hypotheses to confirm with the testers):

| Use case | Where it plugs in | Measure |
|---|---|---|
| Status request → status sentence | Replying to an email or tracker question | Seconds to answer, timed before and during the pilot |
| Draft check: a draft's numbers, dates and status against the record | Before sending a status email | Mismatches caught per week |
| Huddle prep: a ranked glance at every ANDA on an agenda | Before the weekly huddle | Prep minutes per huddle |
| Next-7-days watch: IRs and goal dates landing on you | Start of day | Near-misses caught early |

**Pilot scorecard.** Adoption (testers using it, lookups per person per week). Time saved = lookups × (baseline
seconds − pilot seconds) ÷ 60. Accuracy: spot-check cards against the system of record, and **any wrong fact pauses
the pilot**. Risk: zero data outside the approved boundary. Cost. One satisfaction question. Targets are set with
leadership **after** a one-week baseline, not before. At the stated volume (about 8 status requests per person per
week) the time saved per person is minutes a week, so the case rests on accuracy and not missing dates as much as on
speed.

**Data handling.** The prototype reads no agency data and has no telemetry. Offline mode makes no network call. A
later AI step would send only the short structured record fields, never documents or email bodies, through the
approved platform, and the app is read-only. An optional usage log would record only date, event type and seconds,
never ANDA numbers or text, off by default and readable by the tester. Full threat model: `SECURITY.md`.

**Cost envelope.** Offline mode costs nothing. A brief is generated only when the full card opens, and the planning
figure is about 3,000 tokens per brief (about 800 input plus 150 output tokens measured on sample records). At about
8 requests per tester per week that is roughly 35 briefs and about 100,000 tokens per tester per month, or about 1
million tokens a month for ten testers: a few dollars a month at public Sonnet-class list prices, about 2% of one
50M-token monthly allowance (assuming that allowance is per user; to be confirmed). **Elsa's own cost model is not
known**, so these are estimates the pilot replaces with measured use.

**Pilot plan (4 weeks).** Week 0: stopwatch baseline, about 20 timed lookups. Weeks 1 to 4: offline pilot, weekly
scorecard. Decision at week 4: expand, change, or stop. Rollback is deleting the folder; nothing is installed system
wide.

**What the testers asked for next (not built; see `ROADMAP.md`).** Proofread, rewrite and fact-check a draft using
the RPM's own saved instructions; their own Excel portfolio and notes folder as the data source; rebindable
shortcuts; weekend and federal-holiday flags on goal dates; the latest PSG and RLD-labeling dates from official
sources; whisper-invite as the default with automatic expansion for urgent or at-risk ANDAs; and, later and
only with IT approval, optional Outlook context.

**Why this fits.** It is AI where AI is accountable: deterministic facts first, a clearly fenced generated layer
second, and a model backend behind one interface (`ELSA-INTEGRATION.md`), so the approved platform is a
configuration change and not a rewrite. Goal-date logic is versioned per GDUFA cycle: GDUFA III encoded today,
GDUFA IV a drop-in when its commitment letter finalizes.
