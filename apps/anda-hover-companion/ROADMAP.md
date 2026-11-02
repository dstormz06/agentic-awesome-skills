# Roadmap: tester feedback and decisions

Source: comments from two AI workgroup leads after a demo, plus the product owner's decisions. **Nothing below is
built unless it says so.** Dated 2026-11-02.

## Decisions already made

| Decision | Choice |
|---|---|
| Date shown for PSG updates | Date only, the latest of any draft, revision or final |
| Source of the RLD labeling date | The most recent date in Drugs@FDA |
| Lead time for weekend and holiday warnings | 3 business days before (adjustable) |
| Status-request volume used for planning | About 8 per tester per week |
| Usage log | Yes, minimal: date, event type, seconds; never ANDA numbers or text; off by default |
| Demo for the pitch | Deck and browser simulation only; nothing installed on agency machines |
| Default mode | Whisper invite for new installs (not yet changed in the app; existing installs keep their saved choice) |

## Requested features

| Request | Proposed design | Phase |
|---|---|---|
| Proofread, rewrite, fact-check a draft, using the RPM's own instructions | An Actions menu on a selection. **Draft check** (no AI) compares numbers, dates and status in a draft with the record. **Proofread, Rewrite, Fact-check** use the RPM's saved prompts plus standing instructions, through an approved platform only, with a "what will be sent" preview. Output has a Copy button; no auto-replace. "Fact-check" means checked against the RPM's portfolio, not the system of record, and is labelled that way | 1 |
| Whisper default, automatic expansion for urgent or at-risk ANDAs | Whisper invite by default. When a rule fires (IR due within N days, forecast late by N days) the invite turns amber and opens as a peek. Still starts from the user's selection. An opt-in once-a-day watch list is a later option | 1 |
| Custom shortcuts, right-click menu | Rebindable hotkeys in Settings. A system-wide right-click item is not possible (Windows does not let one app extend other apps' menus); the real routes are an Edge extension or an Outlook add-in, each a separate approved package | Hotkeys 1, add-ins later |
| Optional Outlook context | Local, read-only, search by ANDA number, subject/sender/date only, nothing sent to AI unless clicked; safest as an Outlook add-in; needs IT approval; untested here | 3 |
| RPM's own Excel portfolio, notes and context folders | Settings → My data: choose the workbook, map columns, choose folders. Read-only, local. Built first against a fictional sample workbook; real files only after approval. Shows only what the sheet contains, never an invented forecast | 1 |
| Latest PSG and RLD-labeling dates | One line each on the card, from public FDA bulk files downloaded and matched locally, so nothing about which ANDAs a person tracks leaves the PC. Shows the source and "as of" date; "not checked" if it fails, never a guess | 1b |
| Weekend and federal-holiday flags on goal dates and deadlines | Pure calendar rule using the OPM holiday list (a Saturday holiday is observed Friday, a Sunday holiday Monday). The card also shows the nearest business day before and after. It does **not** claim how GDUFA treats such a date | 1 |
| Watch the user's back | A rule layer (weekend/holiday, PSG/RLD changed, IR due soon, stale data, draft mismatch, sister-ANDA conflict, missing field); one line on the card, the rest in the drawer; each flag shows its evidence, source and date and can be snoozed | 1 |

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
