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
| Custom shortcuts | **Built** | Three rebindable shortcuts in the app (peek, lookup, check a draft), validated; three suggested shortcuts in the extension, rebindable in Edge | |
| Right-click menu after selecting text | **Built, as an Edge extension; tested in Chromium, not in Edge** | `extension/`: right-click on a selection offers *peek*, *check this text as a draft* and *scan this page*; also shortcuts and a toolbar popup (look up, draft, watch list, this page). Works **standalone** (demo records or your own Excel/CSV) or **together with the app** over native messaging | Windows cannot add an item to other programs' menus, so this does not cover desktop Outlook, the new Outlook for Windows or the Teams desktop app. Not tried on Outlook on the web, Teams on the web or SharePoint. The browser's own menu and shortcut UI is not automated |
| Optional Outlook context | **Decided: no mailbox integration; the extension covers Outlook on the web with what it can do safely** | In Outlook on the web (an Edge page): peek at a selected number, check the message you are writing, scan the open page for ANDA numbers. Reads only what you select or what is on the screen, on your click | No mailbox search, no sender or subject index. That needs a Microsoft Graph app registration and admin consent, cannot be tested from here, and would change "reads only what you select" |
| RPM's own Excel portfolio, notes and context folders | **Built, tested on sample files only** | Settings, My data: choose an .xlsx or .csv, check or override the column mapping, choose a notes folder (.txt, .md, .docx). Read-only, local; unknown values show as "—", never an invented forecast | Not tested on a real RPM workbook. Real files only after approval |
| Latest PSG and RLD-labeling dates | **Partly built, unverified** | One date each on the card. RLD labeling: the most recent date in a Drugs@FDA data file the RPM downloads and points to. PSG: only a date in the RPM's own sheet, labelled "from your sheet, not an official source". "Not checked" when it cannot tell | No official PSG source is connected. The Drugs@FDA file layout the parser expects was **not checked against a real file** (FDA hosts were unreachable from the build environment). The app downloads nothing |
| Weekend and federal-holiday flags on goal dates and deadlines | **Built** | Statutory U.S. federal holidays (Saturday observed Friday, Sunday observed Monday), checked against OPM's 2026-2027 list; flags a weekend or holiday date, a date within 3 business days of a holiday (0, 2, 3 or 5 selectable), with the nearest business days either side. Executive-order closures can be added in `closures.txt` | It does **not** say how GDUFA treats such a date. Closures declared at short notice are not predicted |
| Watch the user's back | **Built (the parts decided below)** | WATCH line on the card, full list with evidence and source in the drawer: weekend or holiday date, goal passed or within 5 days, data older than 7 days (real data only), a PSG date after submission, no goal date. **Watch list**: every ANDA with something worth a look across your whole portfolio, worst first, computed when you open it (app tray menu and the extension's popup) | Decided not to build for now: an IR-due-soon rule, a sister-ANDA conflict rule, snooze, a scheduled daily list (reasons below) |

## Decided on the owner's behalf (2026-11-02)

The owner asked for every open decision to be made, not asked. Each is a default that Settings can change, recorded here so it can be challenged
with pilot evidence rather than argued from memory.

| Open item | Decision | Why |
|---|---|---|
| What counts as "urgent" | A bad attention item, a forecast **7 or more days past the goal** (3, 7 or 14 selectable), or a **goal date that has passed**. A goal within 5 days is a warning, not urgent | A week is long enough that a late forecast is rarely noise and short enough to act on. Passed goal dates are the one case where waiting is plainly wrong. Revisit with the pilot's own numbers |
| Default consent | **Whisper**, with the urgent peek on (the owner's choice), in the app and the extension. An existing app install keeps the choice it saved | Nothing opens unasked; the one exception is the rule above, and it can be switched off |
| Auto-hide | **20 seconds**, never while the pointer is on it or the drawer is open; 0, 10, 20 or 40 selectable | Long enough to read a card, short enough not to linger over an email |
| Stale data | Real data older than **7 days** is flagged; the fictional demo is never "stale" | A stale fact shown confidently is the riskiest kind |
| Holiday window | **3 business days**; 0, 2, 3 or 5 selectable (the owner's choice) | |
| Extra closures | Not predicted. Enter them in `closures.txt` (app) or the extension's Settings | Executive-order closures and funding lapses cannot be known in advance |
| The extension's consent, urgent rule and holiday window | **The same as the app's**, and when connected it **follows the app's settings** unless switched off | One product, one set of habits |
| AI in the extension | **None.** No model, no network request | An extension that can call out is a larger review. Proofread, Rewrite and Fact-check stay in the app, behind an approved provider |
| The extension's default data | The **fictional demo** until you import a spreadsheet or connect the app; **no silent fallback** between sources | Showing demo data as if it were yours, or yours as if it were the app's, is the worst failure |
| Extension shortcuts | Suggested **Alt+Shift+A / D / S**; rebind in `edge://extensions/shortcuts` | Edge reserves Ctrl+Shift+A (tab search); a suggestion Edge already uses is dropped, not forced |
| Outlook context | **No mailbox integration.** The extension covers Outlook on the web with selection, draft check and page scan | See the status table |
| PSG | Show **only a date from the person's own sheet**, labelled as theirs. No official source is connected | No source could be verified from here (FDA hosts were unreachable); an unverified source shown as official would be a false fact |
| RLD labeling date | Keep the Drugs@FDA reader, **labelled unverified** until a real file has been read once. See the five-minute check below | The reader was written against FDA's documented layout and a fixture, not a real download |
| Not building now: IR-due-soon rule, sister-ANDA conflict, snooze, a scheduled daily list | **Not now** | An IR-due rule needs a date column most sheets will not have, and the attention line already carries it when the data has it. A sister-conflict rule needs domain rules that cannot be verified from here. **Snooze hides a warning**, which cuts against the point of the tool; if it is ever built, "bad" flags should not be snoozable. A scheduled list needs a background process, and "nothing runs unasked" is a feature. The on-demand Watch list covers the need |
| Live model call | Not exercised. **No call to a model is made until IT approves a provider**; the first live call, when approved, is a single `Test` button press | There is no key or approved endpoint here |
| Code signing | **Out of scope for this build.** Needs the organization's certificate. Until then the build is unsigned and must not be run on a managed computer before IT review | |
| How the extension is distributed for a pilot | **IT-managed install** of the folder or a packed file (`ExtensionInstallForcelist` or an allowlist). Never the public Edge Add-ons store | A public listing is the wrong place for an internal prototype |
| The extension's ID | Derived from a public `key` in the manifest, so the app can allow exactly one ID. The private key is not kept in the repository | A packed or store build gets another ID; enter it in the app's Settings |

### Five minutes to verify the Drugs@FDA reader (needs a computer that can reach FDA)

1. Download the Drugs@FDA data files ZIP from the FDA page below. Do not unzip it.
2. In the app, **Settings, My data, Drugs@FDA file, Choose…**, pick that .zip.
3. Add one real ANDA to your sheet with its active ingredient, dosage form and strength, then open its card.
4. Compare the "RLD LABEL" date with the Drugs@FDA website for that product. If they differ, or the card says "not checked", send the message it shows: it names the missing column.
Until someone has done this once, the RLD line stays labelled unverified.

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
