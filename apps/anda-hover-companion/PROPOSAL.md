# One-pager: pilot proposal for the ANDA Hover Companion

*Template for the director ask. Adapt names and numbers to your division. Keep to one page. All data in the
prototype is fictional, and it is not an FDA product. Status as of 2026-11-03.*

**The ask.** Approve a **Product Catalog and security review** (the agency's route for new software and any AI
use) for a **4-week pilot** of a small Windows tray tool, and an optional Edge extension, with the **DPM AI Testers
group**. The pilot runs **offline first**: no AI call, no network. AI is a later, separate step, only through an
agency-approved platform (Elsa first) and only after that platform's own approval.

**What it does.** An RPM selects an ANDA number anywhere on screen and presses a hotkey (or right-clicks it in Edge).
A small card shows the goal date against a forecast, the one item that needs attention, a WATCH line when a goal or
forecast date falls on a weekend or federal holiday, what lands next, and a one-click **Copy status** sentence for the
email or tracker reply. Everything on the card comes from the record; nothing is generated. An optional AI-written
brief is clearly labelled "generated, verify before acting" and is off until approved.

**Where it fits the work, and how each is measured** (hypotheses to confirm with the testers):

| Use case | Where it plugs in | Measure | Today |
|---|---|---|---|
| Status request → status sentence | Replying to an email or tracker question; hotkey, or right-click in Edge | Seconds to answer, timed before and during the pilot | Built |
| Draft check: a draft's numbers, dates and status against the record | Before sending a status email | Mismatches caught per week | Built |
| Weekend and federal-holiday flags on goal and forecast dates | On the card, before a date is quoted | Near-misses caught early | Built |
| Watch list: every ANDA with something worth a look, worst first | Start of day, opened on demand | Near-misses caught early | Built |
| Huddle prep: a ranked glance at every ANDA on an agenda | Before the weekly huddle | Prep minutes per huddle | Next (concept only) |

"Built" means it exists and passes automated tests. No tester has used it and none of it has touched real data.

**Pilot scorecard.** Adoption (testers using it, lookups per person per week). Time saved = lookups × (baseline
seconds − pilot seconds) ÷ 60. Accuracy: spot-check cards against the system of record, and **any wrong fact pauses
the pilot**. Risk: zero data outside the approved boundary. Cost. One satisfaction question. Targets are set with
leadership **after** a one-week baseline, not before. At the stated volume (about 8 status requests per person per
week) the time saved per person is minutes a week, so the case rests on accuracy and not missing dates as much as on
speed.

**Data handling.** The prototype ships with fictional data and has no telemetry. Offline mode makes no network call.
The Windows app can read a portfolio file the tester chooses, locally and read-only; that is tested on sample files
only and would be used on real files only after approval. The Edge extension runs only when asked, on the one tab,
makes no network request and uses no AI; its optional link to the app is for the current user and allows exactly one
extension. A later AI step would send only the short structured record fields, never documents or email bodies,
through the approved platform, and the app is read-only. An optional usage log would record only date, event type
and seconds, never ANDA numbers or text, off by default and readable by the tester. Full threat model: `SECURITY.md`.

**Cost envelope.** Offline mode costs nothing. A brief is generated only when the full card opens, and the planning
figure is about 3,000 tokens per brief (about 800 input plus 150 output tokens measured on sample records). At about
8 requests per tester per week that is roughly 35 briefs and about 100,000 tokens per tester per month, or about 1
million tokens a month for ten testers: a few dollars a month at public Sonnet-class list prices, about 2% of one
50M-token monthly allowance (assuming that allowance is per user; to be confirmed). **Elsa's own cost model is not
known**, so these are estimates the pilot replaces with measured use.

**Pilot plan (4 weeks).** Week 0: stopwatch baseline, about 20 timed lookups. Weeks 1 to 4: offline pilot, weekly
scorecard. Decision at week 4: expand, change, or stop. Rollback: exit the app and delete its folder (nothing is
installed system wide); if the Edge connection was turned on, press Disconnect in the app's Settings first, which
removes one current-user registry entry, and remove the extension in Edge.

**What the testers asked for, and where it stands** (two AI workgroup leads, after a demo; detail in `ROADMAP.md`).
*Built:* draft check; weekend and federal-holiday flags; watch list; whisper-first with an urgent peek; rebindable
shortcuts; right-click in Edge, alone or with the app (tested in Chromium, not yet in Edge itself). *Built, sample files
only:* their own Excel or CSV portfolio and notes folder. *Built, never run against a live model:* proofread, rewrite
and fact-check, off until a model is approved. *Partly:* the latest RLD-labeling date from a Drugs@FDA file the tester
downloads (layout not yet checked against a real file) and a PSG date only from their own sheet (no official source
is connected). *Decided against:* mailbox-level Outlook context.

**Evidence, and what it does not show.** Over 440 automated tests and 46 on-device checks run on every change on
Windows and Linux build machines in the cloud, and the latest build has no failures. It has **not** been run on an
agency workstation, the build is unsigned and needs IT review first, no live AI call has run, and nothing has been
used by a real tester. `VERIFICATION.md` lists every claim with its evidence, its limit and each defect found.

**Why this fits.** It is AI where AI is accountable: deterministic facts first, a clearly fenced generated layer
second, and a model backend behind one interface (`ELSA-INTEGRATION.md`), so the approved platform is a
configuration change and not a rewrite. Goal-date logic is versioned per GDUFA cycle: GDUFA III encoded today,
GDUFA IV a drop-in when its commitment letter finalizes.
