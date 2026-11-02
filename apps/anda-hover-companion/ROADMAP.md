# Roadmap: tester feedback and decisions

Source: comments from two AI workgroup leads after a demo, plus the product owner's decisions. Dated 2026-11-02, updated
with the Phase 1 build the same day. The status table below says exactly what exists and what does not.

## Decisions already made

| Decision | Choice |
|---|---|
| Date shown for PSG updates | Date only, the latest of any draft, revision or final |
| Source of the RLD labeling date | The most recent date in Drugs@FDA |
| Lead time for weekend and holiday warnings | 3 business days before (adjustable) |
| Status-request volume used for planning | About 8 per tester per week |
| Usage log | Yes, minimal: date, event type, seconds; never ANDA numbers or text; off by default |
| Demo for the pitch | Deck and browser simulation only; nothing installed on agency machines |
| Default mode | Whisper invite for new installs. Built: an install that already saved a choice keeps it |

## Requested features: status

Updated with the Phase 1 build. "Built" means it exists and passes automated tests; **none of it has been used by a real
tester, with real data, or against a live model.**

| Request | Status | What exists | What does not |
|---|---|---|---|
| Proofread, rewrite, fact-check a draft with the RPM's own instructions | **Built, never run against a live model** | **Draft check** (no AI, no network): compares the ANDA numbers, goal and forecast dates and on-track or late wording in a draft with the record. **Proofread, Rewrite, Fact-check** buttons in the same window use the RPM's standing instructions plus a chosen lens, show the exact text first, send nothing until Send, and give a Copy button only | No live Elsa or Claude call. Off until a provider is approved. Fact-check means checked against the RPM's own portfolio, not the system of record, and says so |
| Whisper default, automatic expansion for urgent or at-risk ANDAs | **Built** | New installs start in Whisper. In whisper mode an urgent ANDA (a bad attention item, a forecast 7 or more days late, or a passed goal date) skips the invite and opens a peek. Still starts only from the person's selection and shortcut. Switch off in Settings | An opt-in once-a-day watch list is not built. Installs that already saved a choice keep it |
| Custom shortcuts | **Built** | Three rebindable shortcuts (peek, lookup, check a draft), validated | A system-wide right-click item is not possible in Windows. Real routes are an Edge extension or an Outlook add-in, each a separate approved package: **not built** |
| Optional Outlook context | **Not built** | Nothing | Local, read-only, search by ANDA number, nothing sent unless clicked; safest as an Outlook add-in; needs IT approval |
| RPM's own Excel portfolio, notes and context folders | **Built, tested on sample files only** | Settings, My data: choose an .xlsx or .csv, check or override the column mapping, choose a notes folder (.txt, .md, .docx). Read-only, local; unknown values show as "—", never an invented forecast | Not tested on a real RPM workbook. Real files only after approval |
| Latest PSG and RLD-labeling dates | **Partly built, unverified** | One date each on the card. RLD labeling: the most recent date in a Drugs@FDA data file the RPM downloads and points to. PSG: only a date in the RPM's own sheet, labelled "from your sheet, not an official source". "Not checked" when it cannot tell | No official PSG source is connected. The Drugs@FDA file layout the parser expects was **not checked against a real file** (FDA hosts were unreachable from the build environment). The app downloads nothing |
| Weekend and federal-holiday flags on goal dates and deadlines | **Built** | Statutory U.S. federal holidays (Saturday observed Friday, Sunday observed Monday), checked against OPM's 2026-2027 list; flags a weekend or holiday date, a date within 3 business days of a holiday (0, 2, 3 or 5 selectable), with the nearest business days either side. Executive-order closures can be added in `closures.txt` | It does **not** say how GDUFA treats such a date. Closures declared at short notice are not predicted |
| Watch the user's back | **Partly built** | WATCH line on the card, full list with evidence and source in the drawer: weekend or holiday date, goal passed or within 5 days, data older than 7 days (real data only), a PSG date after submission, no goal date | Not built: IR-due-soon as its own rule, sister-ANDA conflict, snooze, a daily list |

## Not verified yet (check before building)

- Whether FDA publishes a bulk, machine-readable PSG list with dates, or only web pages. The live database was not opened.
- The exact labeling-supplement fields in the Drugs@FDA Submissions table.
- How GDUFA handles a goal date that falls on a weekend or holiday.
- Closures declared by executive order (for example OPM's Dec 24 and 26, 2025 closing) and funding lapses cannot be predicted; the
  holiday list is an updatable file with an "as of" date.

## Official sources found

- [Drugs@FDA data files](https://www.fda.gov/drugs/drug-approvals-and-databases/drugsfda-data-files): a ZIP of tab-delimited tables including Submissions; the page says it updates each morning, Monday to Friday.
- [openFDA downloads](https://open.fda.gov/data/downloads/): the same data as zipped JSON.
- [FDA Product-Specific Guidances page](https://www.fda.gov/drugs/guidances-drugs/product-specific-guidances-generic-drug-development) and its [searchable database](https://www.accessdata.fda.gov/scripts/cder/psg/index.cfm?event=Home.Letter&searchLetter=I).
- [OPM federal holidays](https://www.opm.gov/policy-data-oversight/pay-leave/federal-holidays/).

New outbound hosts if built: fda.gov, accessdata.fda.gov, open.fda.gov (all public). They would be declared in `SECURITY.md` and
the Product Catalog text. Offline mode needs none of them.
