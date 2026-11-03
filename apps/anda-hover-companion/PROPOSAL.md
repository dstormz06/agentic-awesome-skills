# One-pager: pilot proposal for the ANDA Companion (Notula)

*A template for the director ask. Please adapt the names and numbers to your division, and keep it to one page. All
data in the prototype is fictional, and it is not an FDA product. Status as of 2026-11-03.*

**The ask.** Approve a **Product Catalog and security review** (the agency's route for new software and any AI use)
for a **4-week pilot** of a small Windows tray tool, and an optional Edge extension, with the **DPM AI Testers
group**. The pilot is **offline first**: no AI call and no network. AI is a later, separate step, only through an
agency-approved platform (Elsa first), and only after that platform's own approval.

**What it does.** A project manager selects an ANDA number anywhere on screen and presses a key (or right-clicks it in
Edge). A small card shows the goal date against the forecast, the one item that needs attention, a WATCH line when a
date falls on a weekend or federal holiday, what lands next, and two reference dates (PSG and RLD labeling). One click
copies a status sentence for the email or tracker reply. Everything on the card comes from the record; nothing is
generated. An optional AI-written brief is labelled "generated, verify before acting" and stays off until approved.

**Yours to adjust, with no rebuild.** Shortcuts, files, connection, writing style and look are all set in the app's
Settings: your own keys; your Excel or CSV portfolio, notes folder and Drugs@FDA file; Offline, Claude (with its model
name) or your own API described in a form; tone, sign-off and quick replies; nineteen looks. Settings can be backed up,
restored and reset. A backup holds no secret and no file path.

**Where it fits the work, and how each is measured** (hypotheses to confirm with the testers):

| Use case | Where it plugs in | Measure | Today |
|---|---|---|---|
| Status request to status sentence | Replying to an email or tracker question; key, or right-click in Edge | Seconds to answer, timed before and during the pilot | Built |
| Draft check: a draft's numbers, dates and status against the record | Before sending a status email | Mismatches caught per week | Built |
| Weekend and federal-holiday flags on goal and forecast dates | On the card, before a date is quoted | Near-misses caught early | Built |
| Watch list: every ANDA with something worth a look, worst first | Start of day, opened on demand | Near-misses caught early | Built |
| Huddle prep: a ranked glance at every ANDA on an agenda | Before the weekly huddle | Prep minutes per huddle | Next (concept only) |

"Built" means it exists and passes automated tests. No tester has used it, and none of it has touched real data.

**Pilot scorecard.** Adoption (testers using it, lookups per person per week). Time saved is lookups × (baseline
seconds − pilot seconds) ÷ 60. Accuracy: spot-check cards against the system of record; **any wrong fact pauses the
pilot**. Risk: no data outside the approved boundary. Cost. One satisfaction question. Targets are set with leadership
**after** a one-week baseline, not before. At about 8 status requests per person per week, the time saved is minutes
a week, so the case rests on accuracy and not missing a date as much as on speed.

**Data handling.** The prototype ships with fictional data and has no telemetry. Offline mode makes no network call.
The Windows app can read a portfolio file the tester chooses, locally and read-only; that is tested on sample files
only, and would be used on real files only after approval. The Edge extension runs only when asked, on the one tab,
makes no network request and uses no AI. Its optional link to the app is for the current user and allows exactly one
extension. A later AI step would send only the short structured record fields, never documents or email bodies, through
the approved platform, and the app stays read-only. An optional usage log records only date, event type and seconds,
never ANDA numbers or text; it is off by default and readable by the tester. The optional **Outlook** setting (off by
default) signs in on Microsoft's own page, reads mail read-only, searches only for the six-digit number on the open
card, and keeps nothing. Full threat model: `SECURITY.md`.

**Cost envelope.** Offline mode costs nothing. A brief is generated only when the full card opens, at a planning figure
of about 3,000 tokens (about 800 input plus 150 output tokens measured on sample records). At about 8 requests per
tester per week, that is roughly 35 briefs and 100,000 tokens per tester per month, or about 1 million tokens a month
for ten testers. That is a few dollars a month at public Sonnet-class list prices, and about 2% of one 50M-token monthly
allowance (assuming the allowance is per user; please confirm). **Elsa's own cost model is not known**, so these are
estimates that the pilot will replace with measured use.

**Pilot plan (4 weeks).** Week 0: a stopwatch baseline of about 20 timed lookups. Weeks 1 to 4: the offline pilot, with
a weekly scorecard. Week 4: decide to expand, change or stop. To roll back, exit the app and delete its folder
(nothing is installed system-wide). If the Edge connection was turned on, press Disconnect in Settings first, which
removes one current-user registry entry, and then remove the extension in Edge.

**What the testers asked for, and where it stands** (two AI workgroup leads, after a demo; detail in `ROADMAP.md`).
*Built:* draft check; weekend and federal-holiday flags; watch list; whisper first with an urgent peek; shortcuts you
can change; right-click in Edge, alone or with the app (tested in Chromium, not yet in Edge itself). *Built, sample
files only:* their own Excel or CSV portfolio and notes folder. *Built, never run against a live model:* proofread,
rewrite and fact-check, off until a model is approved. *Built, optional, stand-in server only:* read-only Outlook mail
about the open ANDA; it needs the organization to register an app and allow mail access. *Partly:* the latest
RLD-labeling date from a Drugs@FDA file the tester downloads (layout not yet checked against a real file), and a PSG
date only from their own sheet (no official source is connected).

**Evidence, and what it does not show.** Over 500 automated tests and more than 45 on-device checks run on every
change, on cloud Windows and Linux build machines; the latest build has no failures. It has **not** been run on an
agency workstation. The build is unsigned and needs IT review first, no live AI call has run, and no real tester has
used it. `VERIFICATION.md` lists every claim with its evidence, its limit and each defect found.

**Why this fits.** It puts AI where AI is accountable: deterministic facts first, a clearly fenced generated layer
second, and the model behind one interface (`ELSA-INTEGRATION.md`), so the approved platform is a setting and not a
rewrite. Goal-date logic is versioned per GDUFA cycle: GDUFA III is encoded today, and GDUFA IV can drop in when its
commitment letter is final.
