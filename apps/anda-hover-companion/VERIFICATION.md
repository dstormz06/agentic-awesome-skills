# Verification report — Windows app delivery (v0)

Scope: rigorous verification of the previous delivery — the Windows app
(`app/`), the federal palette retune, and the claims made about them.
Method: re-verify claims → re-derive/re-run → line audit → stress-test →
fidelity check → fix and repeat. Traceability: each fix is in commit history
on `claude/anda-hover-tooltip-app-lmlkb6`; this report is versioned with it.

## 1. Claim-by-claim verification of the previous response

| Claim | Verdict | Evidence |
|---|---|---|
| "Compiles clean" | ✅ Confirmed (re-run) | `dotnet build` → 0 warnings, 0 errors, after every fix below |
| "Screenshots are the actual app" | ✅ Confirmed | Produced by `AndaCompanion.Preview` headless harness from the built assembly |
| "All five appearances, A default" | ✅ Confirmed | Stress check: `Appearances.All.Count == 5`; `new Settings().Appearance == A`; 7 rendered frames |
| "Settings persist to AppData" | ✅ Confirmed (executed) | Round-trip test: save D/Dark/Whisper → reload → equal |
| "Companion brief via official Anthropic SDK, offline fallback" | ✅ / ⚠️ | Offline path executed (tests below). Anthropic path compiles against SDK 12.35.1 but is **not runtime-tested** (no key in this environment) |
| "Hotkeys, clipboard capture work" | ⚠️ Unverifiable here | Windows-only P/Invoke; compiles, logic reviewed; **needs a Windows smoke test** — flagged, not asserted |
| "Runs with `dotnet run` on Windows / publishes single exe" | ⚠️ Expected, unverified | Standard Avalonia deployment; cannot be executed in this Linux container |

## 2. Re-derivation / re-execution

- Full rebuild of both projects from clean state: **0 errors**.
- Headless re-render of all 7 card states (A light/dark, B expanded, C, D
  light/dark, E healthy): all frames produced and visually inspected,
  including the previously unchecked healthy-card state (attention slot
  correctly absent, green "▼ 9 d ahead of goal").
- 100+ executable checks run against the real assembly (validator,
  repository, settings, appearance invariants, every palette color parse,
  offline brief): **all pass** (`scratchpad` stress runner; assertions listed in §4).

## 3. Line audit — findings and dispositions

| # | Finding | Severity | Disposition |
|---|---|---|---|
| F1 | `AndaValidator` regex matched the first 6 digits of longer numbers (a 7-digit or 10-digit string produced a false ANDA) | **Bug** | Fixed: `(?<!\d)(\d{6})(?!\d)` boundaries; regression checks added |
| F2 | DESIGN.md §7 still recommended "WinUI 3 (or WPF)" while the shipped app is Avalonia — doc/code contradiction | **Doc bug** | Fixed: §7 now documents the Avalonia decision and its rationale |
| F3 | App's default consent was `PeekFirst`; DESIGN.md names model ① as the default | Inconsistency | Fixed: default is now `Full` (the deliberate hotkey press is model ①'s stage two); README annotated |
| F4 | Dead code: no-op `Closed` handler in `App.OpenCard`; stale `_card` reference after close | Code quality | Fixed: handler now nulls the reference correctly |
| F5 | `ChipWindow` fade timer kept ticking after close | Minor leak | Fixed: timer stopped on `Closed` |
| F6 | Floating package versions (`11.3.*`, `Anthropic *`) — not reproducible | Traceability | Fixed: pinned to Avalonia 11.3.18 / Anthropic 12.35.1 (the resolved versions) |
| F7 | Sister-hop repositioned the card by +10/+14 px on every click (drift); sister chip shown even when count = 0 | Polish bug | Fixed: card positions once per open; chip hidden at zero |
| F8 | Mockup CSS header comment still said "3 visual directions" | Stale comment | Fixed |
| F9 | C's progress ring renders left-aligned in the app vs. right-aligned in the web mockup | Divergence | **Accepted, logged** — no information difference; will align in the polish pass |

## 4. Stress-test results (all executed, all passing)

Validator: plain/prefixed/dashed/hash/embedded forms extract; 5-digit,
7-digit, 10-digit-phone, null, and whitespace inputs all reject; first of two
numbers wins. Repository: known found, unknown null, healthy record has no
attention, at-risk record forecast is late. Settings: full round-trip through
disk, defaults A + Full. Appearances: exactly five, exactly one single-theme
(C), five distinct progress forms, **every color token in every palette
parses** (guards typos in the 10 palettes). Offline brief: non-empty, marked
not-generated, handles the no-attention record.

## 5. Fidelity to intent

- "Build the actual Windows app with all appearances as options" → built; five
  appearances selectable in the tray menu, persisted, **A default** per your
  decision. ✅
- "Top 4 daily lifesavers" → delivered and recorded in DESIGN.md §1. ✅
- "Colors not FDA friendly — you suggest" → retuned to USWDS-anchored palette
  with documented policy. ✅
- Honest divergences, stated at delivery and re-confirmed here: text capture is
  Select & Peek (UIA under-cursor is the next milestone); Windows-runtime
  behaviors need one smoke test on a real Windows box; the Anthropic call path
  is compile-verified only.

## 6. Iteration log

Pass 1 surfaced F1–F9 → fixes applied → pass 2: rebuild clean, stress suite
green, all 7 frames re-rendered and inspected. No open discrepancies besides
the two ⚠️ environment-limited items in §1, which are constraints of this
container, not defects in the deliverable.

---

# Round 2 — verification of the light-only / Prompt Library / security delivery

Same protocol, applied to the next delivery (commit `2851c3a`). Findings:

| # | Finding | Class | Disposition |
|---|---|---|---|
| G1 | README and the chat summary claimed settings changes "preview live," but `ApplySettings` closed the open card — a claim/behavior mismatch found by re-deriving the code path | **Claim bug** | Fixed: the open card now rebuilds in place with the new appearance/transparency, making the claim true |
| G2 | The 88%-transparency screenshot was shipped **unviewed**, and the headless capture composites over black — making the translucent card look *darker*, the opposite of the light-only direction | **Process + evidence bug** | Fixed: harness now composites all frames over a light desktop tone (`#E9E7E3`); shot re-rendered and visually inspected this time |
| G3 | Three stale light/dark references survived the light-only conversion (app README layout note; DESIGN §5 visual-language bullet; DESIGN §10 mockup bullet) plus a stale "blueprint ground" palette mention | Doc drift | All four corrected |
| G4 | Settings window could be opened multiple times from the tray | Polish bug | Fixed: single instance, re-activated if already open |
| G5 | F9 from round 1 (ring left-aligned in the app vs right in the mockup) | Carryover | **Closed**: ring right-aligns when the appearance uses the ring form |
| G6 | Dev screenshot scripts for the web studio (`shot2.js`/`shot3.js`) still reference the removed theme buttons | Dev-tooling only | Logged, not fixed — they are scratchpad tools outside the repo; the committed preview harness is current |

Re-execution after fixes: both projects build with 0 errors; the stress suite
(78 checks: validator boundaries, repository, settings round-trip incl.
opacity + lens, appearance invariants, every palette color parse, prompt
library CRUD, offline brief) passes in full; all 8 frames re-rendered over the
light backdrop and inspected. Environment-limited items unchanged from round
1: Windows-runtime smoke test and a live Anthropic call still require a
Windows box / API key respectively.

---

# Round 3 — surgical-edit application (user-confirmed set S1–S6 + provider seam)

Applied exactly as proposed and confirmed; verified by rebuild (0 errors), an
extended 88-check stress suite, and visual inspection of re-rendered frames.

| Edit | Applied at | Verified by |
|---|---|---|
| S1 offline label honesty | `CardWindow.axaml` (`BriefVerify`) + `LoadBriefAsync` | Rendered frame shows "OFFLINE BRIEF · assembled from the record" |
| S2 null-free record JSON | `GeneratedBriefService.RecordJson` | Options set `WhenWritingNull`; logic untouched |
| S3 brief MaxTokens 1024→400 | `GeneratedBriefService.BriefMaxTokens` | Ceiling only; no path change |
| S4 offline brief de-duplication | `OfflineBriefService` | Stress checks: no goal/forecast date restated; leads with open item / precedent |
| S5 compressed system prompt | `GeneratedBriefService.SystemPrompt` | Rule-for-rule mapping documented in the source comment |
| S6 GDUFA-versioned goal rules | new `Services/GoalDateRules.cs` | Encoded tiers match cited sources; unencoded tiers return null (fail-loud); GDUFA IV placeholder inert |
| Provider seam (Elsa-ready) | new `Services/BriefProvider.cs`; factory rewired | Stress: no key → offline; reserved provider "elsa" → offline; Anthropic path compiles against SDK 12.35.1 |

Accuracy policy note for S6: only tiers confirmed against the Sept 2024
amendments guidance / GDUFA III commitment letter are encoded (standard
original ANDA 10 months; major amendment without preapproval inspection
8 months). All other tiers deliberately return "not encoded" until transcribed
from the commitment letter tables — the forecast layer must never guess a
regulatory number. GDUFA IV rules are a placeholder pending the FY2028–2032
commitment letter (negotiations ongoing as of mid-2026).

---

# Round 4 — T1–T4, Apple-inspired polish, universality seam

| # | Item | Verified by |
|---|---|---|
| T1 | Copy-ready status line (`Services/StatusLine.cs`, "Copy status" chip, `S` key, "Copied ✓" confirm) | Pure function; rendered chip; stress checks |
| T2 | Section 508 groundwork: `AutomationProperties.Name` on card, tool buttons, chips; visible accent focus rings app-wide | Rendered; names set per record |
| T3 | GDUFA III tier table completion | **NOT done — deliberately.** fda.gov blocks automated fetch; the dual-source rule means unverified tiers stay "not encoded" rather than guessed. Requires manual transcription from the commitment-letter PDF. |
| T4 | "Data as of" freshness stamp under the identity block | Rendered on both records |
| — | Apple-inspired polish: 160/180 ms fade+scale card entrance, pressed-state scaling, hover fills, card-material tooltips, filled accent primary pills, thin slider | Rendered across card / Settings / Prompt Library |
| — | Universality seam (`Data/EntityKinds.cs`): identifier recognition is a registry; `AndaRecord.Kind` drives all labels | Validator delegates to registry; stress suite unchanged and green |

## Defects caught and fixed in this round

| # | Finding | Class | Disposition |
|---|---|---|---|
| H1 | Progress **value** was animated 0 → target; the card displayed a wrong percentage for ~half a second, and screenshots captured it mid-sweep | **Data-integrity bug** | Value animation removed; entrance motion kept. Rule recorded in code: motion may never misrepresent a fact. |
| H2 | Preview harness loaded only FluentTheme, so app-level styles (tooltips, focus rings, primary pills) were absent from every screenshot — evidence diverging from the product | **Evidence bug** | Harness now subclasses the real `App`, skipping only tray/hotkeys |
| H3 | `ToolTip` has no `BoxShadow` property (build error) | Build | Removed; border + material carry the effect |
| H4 | Consent label ① ran into the panel edge; Prompt Library subtitle clipped behind the Done pill | Layout | Both shortened/wrapped |

Environment-limited items unchanged: Windows-runtime smoke test (hotkeys, clipboard,
tray) and a live Anthropic call still require a Windows box / API key.

---

# Round 5 — live-demo delivery: a real Windows machine, a test suite, a browser simulation

**What changed in method.** Until now every screenshot came from a Linux headless renderer with fallback fonts,
and nothing Windows-specific (hotkeys, clipboard, tray, Segoe UI, placement) had ever run. This round adds
GitHub Actions on `windows-latest` as the Windows box: it builds, runs every test, publishes the single-file
`.exe`, and runs the app's own `--selftest` on the machine. The self-test report is read back from the job log.

## Evidence, with its limits

| Claim | Evidence | Limit |
|---|---|---|
| The code builds and 143 tests pass on Windows; 136 pass on Linux (7 Windows-only tests skip there) | Workflow runs 1 to 6 on `windows-latest` and `ubuntu-latest`. The definitive run is **run 6, commit `f86dd70`, all jobs green** | Counts are for the commit named in each run |
| Self-test summary on the definitive run | 24 PASS, 0 WARN, 0 FAIL, 1 SKIP (the live Claude connection, by design) | One machine, one resolution |
| Both hotkeys register, and pressing them as real keystrokes reaches the app | Self-test `hotkeys-registered`, `hotkeys-delivered` | The runner is a clean machine: no competing program owns the keys |
| Select & Peek works end to end: a real selection is captured, also when equal to the clipboard, "nothing selected" is clean, and the clipboard is restored byte-for-byte every time | Self-test `select-and-peek-end-to-end`, `clipboard-snapshot-restore` | Tested against the app's own text box. Not against Word, Outlook, a browser or a PDF reader |
| Segoe UI Variable Text, Cascadia Mono and a serif fall-back resolve | Self-test `fonts` | Windows build 26100 (Windows 11 24H2 / Server 2025 generation). Windows 10 falls back to Segoe UI |
| Every window renders on a real desktop, with real fonts | Self-test `render-*` (PNG per window, plus desktop captures) | The PNGs live in the run's artifact; the artifact host is not reachable from the authoring environment, so they were not viewed here |
| Cards open exactly where the placement rule says at five screen anchors (for example the tray corner opens at 589,48 for a 424×657 card on a 1024×720 work area) and stay inside the work area when expanded | Self-test `card-placement-on-screen`, with its per-anchor trace, run 6 | One resolution (1024×768, 1× scaling). High-DPI and multi-monitor are untested |
| The single-file `.exe` starts and runs | The self-test ran from the published exe | Unsigned; SmartScreen behavior on a user's machine is documented, not tested |
| The browser simulation matches the app's logic | 30 recognizer cases, the status line, the offline brief and the placement rule were compared in a browser against the C# tests' expectations: no differences | The simulation is a port, not the app. The window chrome, hotkey and clipboard hand-off are simulated and labelled as such |

**Not verified, stated plainly:** a live Anthropic call (no key in any environment used); behavior on a managed
agency workstation (allow-listing, endpoint protection, clipboard monitoring); non-US keyboard layouts where
Ctrl+Alt is AltGr; elevated target windows (documented as a limit); Windows 10; a real mouse and display
(the runner is virtual). Windows 11 Pro/Enterprise features such as Windows Sandbox were not used.

## Defects caught this round

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R1 | Hotkey registration was unchecked; a taken combination failed silently | Audit | Checked; plain-language notice and tray status line; second instance refused |
| R2 | Ctrl+C was synthesized into terminals, where it is an interrupt | Audit | Terminals skipped by window class |
| R3 | Ctrl+C was sent while Ctrl+Alt was still held, so the target saw Ctrl+Alt+C | Audit | Waits for release; masking key; bounded wait |
| R4 | Clipboard restore used a null owner window, so `SetClipboardData` fails and the user's clipboard could be emptied | Audit | Real owner window on its own message pump; Windows self-test proves restore |
| R5 | A selection equal to the clipboard was treated as "nothing selected"; restore was text-only; capture blocked the UI thread | Audit | Sequence-number detection; every plain format snapshotted; runs off-thread |
| R6 | Cards used fixed-size guesses and could hang off-screen from the tray | Audit | Placement from measured size; pure, tested rule |
| R7 | **"NDA 021234" was recognized as an ANDA number**, so a real ANDA with that number would show the wrong card | Test authoring | Recognizer refuses labelled non-ANDA numbers; 14 tests; ported to the simulation |
| R8 | **Tertiary text color failed WCAG AA on all five appearances** (2.8–3.4:1; 4.5:1 required) | Measured | Darkened to ≥4.6:1; contrast tests enforce all inks and status colors |
| R9 | **The expanded card was ~750 px tall and ran off a 720 px work area** (a 768 px laptop) | First Windows run | Card body scrolls when it would not fit |
| R10 | Drawer sections rendered as shrink-wrapped boxed buttons of unequal width | Rendered review | Flat full-width rows |
| R11 | The card was placed from a window size that had not settled; at the right edge it opened ~200 px left of where the placement rule puts it | Windows self-test trace | Placed after the brief lands (≤150 ms); self-test asserts the exact position |
| R12 | The card could overflow the bottom edge when its brief loaded after placement | Browser simulation test | Clamped after the brief lands, in the app and the simulation |
| R13 | Settings was ~770 px tall with no scroll | Rendered review | Scrolls on short screens |
| R14 | The self-test's "blank image" heuristic mis-flagged small valid images | First Windows run | Threshold scales with image area |

R7, R8, R9 and R11 are defects a user would have met. R9 and R11 were invisible in every earlier round
because no earlier round ran on Windows.

## Round 6 — K default, fifteen looks, compact boxes, auto-hide, Elsa placeholder

Scope: the product owner asked for K to be the default, the four new compact looks (L–O), an auto-hide feature, and Elsa
integration "as a placeholder, super easy to incorporate later". Same method as round 5: build it, run it on the Windows
runner, read the self-test report back from the job log, and say what the run does not prove.

| Claim | Evidence | Limit |
|---|---|---|
| K · Graphite Quiet is the default; A–E keep their saved numeric values, so an existing tester's choice does not change; an unknown saved value falls back to K | Unit tests `There_are_fifteen_looks_and_K_is_the_default`, `Saved_settings_keep_their_numeric_meaning…`, `An_existing_testers_choice_is_kept…`, `Unknown_appearance_numbers_and_auto_hide_values_fall_back_safely`; self-test `settings-and-prompts` | A fresh install gets K. An existing install keeps what it saved |
| All fifteen looks render on Windows with Segoe UI Variable | Self-test `render-card-*` (K ×5 states, A to J) and `render-compact-*` (L, M, N, O) all PASS with plausible sizes | The PNGs are in the run artifact; the artifact host is not reachable from the authoring environment, so these were reviewed from the headless Linux renders of the same code (fallback fonts), not the Windows PNGs |
| Every text ink meets WCAG AA on every look, including the tinted header bands (F, H), seals, tag chips and the filled chip | `AppearanceTests` (computed contrast, all 15, plus `Added_surfaces_keep_their_text_readable`); lab audit `0 failing` | Computed ratios, not a screen-reader or low-vision session |
| Compact boxes are much smaller than the card, stay inside the work area at five anchors, and Enter expands | Self-test `compact-boxes`: strip 340×81, tile 196×145, ticket 300×68, badge 181×38 dip, each under 35% of the full card's area | One resolution (1024×768, 100% scaling); no DPI sweep |
| Compact boxes never take focus | Unit test `A_compact_box_never_takes_focus…` (`ShowActivated` false, not active after show) | **Not exercised against a real email client on Windows.** The runner has no Outlook; whether a particular app steals focus back is untested |
| Auto-hide hides an idle card, and never one that is hovered, pinned or expanded | `AutoHideTests` (policy and controller with a fake clock, 18 cases); `CompactRenderTests` (real timers, headless); self-test `auto-hide` (a 1 s card gone by 3 s on the Windows runner) | The runner's pointer is not over the card, so "hover keeps it open" is proven by the policy test and the browser simulation, not by a real mouse on Windows |
| Elsa placeholder: every configuration state resolves to a plain message; plain `http://` is refused; the token never appears in any message or printed settings object; an error status never exposes the body; a failing Elsa falls back to the offline brief | `ElsaTests` (20 cases, fake HTTP handler); self-test `elsa-placeholder`. Defect found by a test: `ElsaSettings.ToString()` would have printed the token (see R15) | **No call to a real Elsa endpoint exists or was made.** Its request and response shape is unknown and is deliberately not guessed |
| The browser simulation matches: default K, all fifteen looks, compact boxes, auto-hide | 68 Playwright checks (progress form per look, timeline geometry equals the app's formula, spine, compact sizes, no focus, hotkey-again expands, auto-hide with hover / pin / expand / Off, 390 px width) plus the earlier 30 recognizer cases and the placement port | A port, not the app |

Counts on the final commit are in the run named below. Run 7 (commit `58764d7`): Windows job **276 tests, 0 failed**;
self-test **41 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design); Linux job green.
Run 8 (commit `f8c1c49`) was green on both jobs. The **final code commit is `60cf348`** (run 10): the "nothing too dark" palette change
(K accent `#56626E`, fills nudged in F, G, H, J). Run 10 is green on both jobs, including the Windows self-test, which exits non-zero on any
FAIL, so a green job means zero FAIL. The 276 / 41-pass figures above were read from run 7's log; the 271 cross-platform tests were re-run
locally on the final code (269 plus the two new contrast tests). Run 10's own counts were not re-read from its log.

### Defects caught in round 6

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R15 | **`ElsaSettings` is a record, and a record prints every member, so any log line or exception formatting it would have printed the token** | A test written to assert the token never appears | `ToString()` overridden to redact; the test now guards it |
| R16 | A dashed `Line` shape in the ticket forced a 100 px row height, so the "64 px" ticket was 117 px tall | Headless size test | Replaced with a purpose-built control that takes its row's height |
| R17 | Compact boxes took keyboard focus when shown: someone typing an email would have kept typing into the box, and a stray Enter or Space would have expanded it | Design review of the interaction | Never activated; expansion needs a click or the hotkey again on the same number; a test asserts it |
| R18 | Sizes were quoted from Linux renders (fallback font); Segoe UI measured wider (strip 81 not 78, tile 145 not 139) | Windows self-test | Taglines, DESIGN and the lab quote the Windows ranges |
| R19 | The tray icon was drawn in the look's accent; with K that is graphite, a dark tile on a dark Windows taskbar | Review of the K default | Tray icon is a fixed federal blue in every look |
| R20 | The compact box's "needs you" label wrapped in the lab and the simulation | Browser render | Stub widened and set to no-wrap in all three renderers |
| R21 | **Progress fills in F, H and J were below 3:1 against their tracks** (2.99, 2.96, 2.61; G borderline at 3.12), so the bar was hard to tell from its track for some viewers | A new non-text-contrast test written while lightening K | Fills nudged slightly deeper (still mid-tone, not dark); the test now guards all eleven looks. The value is also shown as text, so this was a margin issue, not a lost fact |

### Pitch refresh (2026-11-02, documents and simulation only; no app code changed)

After an IT notice about AI sites and feedback from two AI workgroup leads: the browser simulation now shows look-alike mail,
chat and browser windows (labelled "Outlook-style", "Teams-style", "Edge-style", with no logos or real products) plus an optional
terminal, and lets the tester choose which to show. Verified in a browser: 26 new checks (layout, no overlaps, picker, hotkey from each
app, phone width), the earlier 38 + 68 + recognizer and placement checks still pass. The deck, one-pager, `PROPOSAL.md` and
`demo/` guides were rewritten around workflow use cases and measures, the Product Catalog path, and the stated volume (about 8
status requests per tester per week); `ROADMAP.md` records the tester feedback and decisions. **Everything in the roadmap is not built.**
Corrections: the demo guides now say not to run the unsigned Windows build on a managed computer before IT approves it (an earlier
version suggested running the self-test there).

### Housekeeping in round 6

The original design studio `mockup/anda-card.html` (five looks, A default) was **retired**, because it had fallen behind the app and
the other two mockups. References in `DESIGN.md` and `app/README.md` now point at the lab and the simulation. Earlier rounds of this
report that mention the studio describe a file that is no longer in the tree (it is in git history).

### Things the owner should decide, not assume

- ~~K's accent is graphite (`#33373C`)~~ **Decided: "don't make anything too dark."** K's accent is now soft slate `#56626E`
  (white text on it 6.1:1), its fill `#6E7A86`, its text ink softened to `#2A2E33` (still 13:1 on the surface). Every
  look's accent and fill is held above a minimum luminance by a test.
- **Auto-hide defaults to 20 s.** A card opens next to the pointer, not under it, so a reader who keeps the mouse still
  and does not touch the keyboard will see it hide. Off is one click in Settings; 40 s is the gentler choice.
- **Existing installs keep their saved look.** Only new installs get K.

## Round 7 — Phase 1: whisper default, urgent peek, watch, draft check, my data

Scope: the product owner asked for Phase 1 of the tester feedback in `ROADMAP.md`. Same method as rounds 5 and 6: build it,
run it on the Windows runner, read the counts from the job log. The Windows run on the first push (`dc030fc`, run 12) **failed**
(427 of 428 passed), which is why there is a second push; the final run is the one recorded here.

Final code commit **`9762220`** (run 13): Linux job green; Windows job **428 tests, 0 failed**; self-test **42 PASS, 0 WARN, 0 FAIL,
1 SKIP** (the live Claude connection, by design). Counts read from the job log. 421 of the 428 tests run on Linux (the rest are Windows-only).

| Claim | Evidence | Limit |
|---|---|---|
| The U.S. federal-holiday calendar is right for 2026 and 2027, including observed Friday and Monday dates | `CalendarAndWatchTests` against OPM's published list (2026: Jan 1, Jul 3, Nov 11, Dec 25; 2027: Jan 1, Jun 18, Jul 5, Nov 11, Dec 24); the same dates re-checked in the browser port; self-test `calendar-watch-draft-portfolio` on Windows | Rules, not a feed. Closures declared by executive order or a funding lapse are not predicted; they go in `closures.txt`. **No claim is made about how GDUFA treats a weekend or holiday goal date** |
| A goal or forecast date on a weekend or federal holiday, or within N business days of one, gets one WATCH line with its evidence and source | Unit tests (ordering, wording, 0/2/3/5 window), headless card tests, 41 browser checks | The demo records are fictional, and the demo clock is frozen at the extract date |
| Whisper is the default for new installs; an install that saved a choice keeps it; an urgent ANDA opens a peek | `FirstStepTests`, `SettingsTests` (round trip and normalisation), browser flow | Never used by a person. Urgent means a bad attention item, a forecast 7+ days late (3/7/14 selectable) or a passed goal date; these thresholds are a proposal |
| Three shortcuts are rebindable and validated; all three register on Windows | `HotkeyTests` (parser, validation, fake API), Windows test `Real_hotkey_registration…` (3 registered, 3 problems reported to a second copy), self-test `hotkeys-registered`: all three registered | All three are pressed as real keystrokes by the self-test (`hotkeys-delivered`). *(Corrected in round 8: this row first said only two were pressed; the code always pressed every registered shortcut and only the report's wording was stale.)* Rebinding through the Settings window was tested headless, not by a person on Windows |
| The draft check finds a wrong goal or forecast date, "on track" against a late forecast, "late" against an early forecast, and an unknown ANDA, and says what it did not check | `DraftCheckTests`, headless window tests, browser port (same sample draft) | Deterministic text rules, not language understanding; it only knows numbers, goal/forecast dates and a short list of on-track/late phrases |
| Proofread, Rewrite and Fact-check show the exact text first and send nothing until Send; the draft is fenced as data | `WritingAndReferenceTests`, `Phase1UiTests` with a fake completer | **Never run against a live model.** Provider prompts are untested for quality |
| The Excel/CSV portfolio and notes readers parse a workbook, detect and override columns, tolerate Excel having the file open, and refuse oversized or malformed files; unknown values stay "—" | `PortfolioTests` (generated .xlsx and .csv, caps, delimiter detection, notes index), self-test (sample CSV on Windows) | **Tested on generated files only, not on a real RPM workbook.** Real files only after approval |
| The RLD labeling date and PSG date lines show only what is known | Parser tests on a fixture that follows the layout the code expects; PSG only from the RPM's own sheet | **Unverified against real FDA files.** FDA hosts were unreachable from the build environment, so the Drugs@FDA layout and any PSG source were not checked. No official PSG source is connected |
| The browser simulation matches the app's new logic | 42 (adapted original flow) + 41 (new: calendar vs OPM, WATCH, urgency, draft check, tray, phone width) + 13 + 69 + 27 Playwright checks pass | A port, not the app. My data, the writing actions and rebinding are described there, not simulated |

### Defects caught in round 7

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R22 | **The K progress line threw (`Math.Clamp` min > max) when drawn at zero width**, hit when a record has no progress value (the control is hidden and has no size). The exception was masked in the tests by a font-manager error raised during teardown, which hid the cause for a long time. Whether it could occur in the shipped app on Windows was not established; the guard is cheap and tested | New headless test of a record with no progress | Draw nothing when the control has no size; clamp made safe; a regression test fails without the fix |
| R23 | A Windows-only test still expected two shortcuts after the third was added | The Windows run on the first push | Test updated; the run that was red is recorded above, not hidden |
| R24 | The xlsx reader skipped every other cell (an inner read loop over-consumed the stream) | `PortfolioTests` | Cells and strings now read through sub-readers |
| R25 | Within one severity, a forecast-date flag could outrank the goal-date flag | `CalendarAndWatchTests` | Explicit ordering: goal passed, goal soon, goal date, forecast date, then data flags |
| R26 | A "date is on a weekend" sentence said it twice ("falls on a weekend … is a Saturday") | Review of the wording | One plain sentence plus the nearest business days |

### What round 7 does not prove

No real FDA data, no real RPM workbook, no live Elsa or Claude call, no Outlook, no right-click menu, no check against FDA's current
files, no managed-workstation or signed-build run, no real-mouse hover test, no real email client. The Windows self-test did not render
the draft window or the WATCH line (round 8 adds both). Whisper-by-default and
the urgent-peek thresholds are decisions to test with the testers, not facts.

## Round 8 — the Edge extension (standalone and with the app), the host, the watch list, the owner's open decisions

Scope: the owner asked for everything, for every open decision to be made on their behalf (recorded in `ROADMAP.md`), and for an Edge extension that
works alone or together with the app. Same method: build it, run it, read the counts from the CI log, and say what the runs do not prove.

Commit **`e160ed3`** (run 14): Linux and Windows jobs green. Windows self-test **46 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design),
including the new `native-host-process`, `render-draft`, `render-watch-list` and `watch-line-on-card`. The extension's 18 parity and safety tests pass on both
runners (counts read from the Linux log; the Windows step is green). 444 non-Windows tests passed locally on this code; the run's own xUnit totals were not
re-read.

The final code commit is **`142abe2`** (run 15): both jobs green again, now also running the 6 manifest and no-network tests (24 extension tests in all) on both runners. It changed no
app code, so the self-test counts above are run 14's; run 15's own counts were not re-read, only its green conclusion (the self-test step fails the job on any FAIL).

| Claim | Evidence | Limit |
|---|---|---|
| **The extension's JavaScript says exactly what the app says.** Same calendar, watch flags, urgency, cards, draft check, CSV and .xlsx reading | `tests/golden.json` is written by the app's own code (148 date flags, every day of 2026-2027, 27 cards under three clocks, 12 drafts, 30 recognizer texts, 8 portfolio files with 17 date formats and non-dates, an .xlsx with a hidden sheet); the JavaScript reproduces every case. The app's test fails if the app drifts from the file. Both run on Linux and Windows. Breaking the JavaScript on purpose fails 3 of the 7 parity tests | A fixed set of cases, not a proof. A rule not in the fixture could differ |
| The extension runs only when asked and cannot read other tabs or call out | `manifest.test.mjs`: permissions are exactly `activeTab, scripting, contextMenus, storage` (+ optional `nativeMessaging`), no host permissions, no content script, no web-accessible files, no `externally_connectable`, no inline script, no `fetch`/`XMLHttpRequest`/`WebSocket`/`eval` or URL in any source file; the UI uses a closed shadow root and no `innerHTML` | A static check of the files, not a network capture |
| It works in a real browser engine, on real pages, with no app | `tests/e2e/standalone.cjs`: **63 checks** in Chromium: whisper chip, urgent peek with its reason, full card, drawer, Copy status (clipboard content checked), NDA/short/unknown numbers not guessed, right-click text, all three consent models, draft check from a field and a contenteditable, page scan, a page with `default-src 'none'; style-src 'none'`, auto-hide (and never under the pointer), focus restored, popup (look up, draft, watch list), settings (.xlsx and .csv import, title row above the header, no column found then fixed by choosing one, wrong file type refused, no silent mixing of sources) | **Chromium, not Edge.** The browser's own context menu and shortcut handling cannot be pressed by automation: the tests call the code those call, on a copy of the extension that may read 127.0.0.1 and has an open shadow root |
| It works together with the app, over real native messaging | `tests/e2e/native.cjs`: **11 checks** with the real .NET host process started by Chromium: lookup, draft check and watch list answered from a portfolio file, the app's "peek first" consent followed (and not followed when switched off), a demo number is *not found* rather than silently shown, host not installed and a host that lists another extension both give a plain message. Also `NativeHostProcessTests` (real pipes, clean exit) and 17 framing and protocol tests | Linux host. On Windows the published exe is started the way the browser does by the self-test: **first answer 464 ms**, clean exit |
| Connect and Disconnect work, for one extension ID only | `NativeHostRegistrationTests` (manifest lists exactly one origin, ID format, files, data-folder pointer), a Windows-only test that round-trips the registry value in the current-user hive, headless Settings tests | **Edge itself reading that registry value was not observed**; the path is Microsoft's documented one |
| Watch list across the portfolio | `WatchListTests`, headless window tests, identical to the extension's in the fixture, render on Windows (`render-watch-list`) | |
| The new Windows windows render and the card shows the weekend flag | Self-test `render-draft`, `render-watch-list`, `watch-line-on-card` | One resolution |

### Defects caught in round 8

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R27 | The settings page offered "(none)" for a column, but clearing a mapping falls back to automatic detection (as in the app), so the label promised something false | `standalone.cjs` | Labelled "(automatic)" |
| R28 | Without the connection permission the app source said "This browser cannot connect", which is neither true nor useful | `standalone.cjs` | Says the connection is off and where to turn it on |
| R29 | The host's "not found" message read "…not in your portfolio (Your portfolio: Together.csv)" | `native.cjs` against the real host | Uses the file name |
| R30 | The extension and the app disagreed on a two-digit year: I assumed a pivot at 2029; the app's runtime pivots at 2049 | The fixture (`10/3/31` is 2031 in the app) | The extension follows the app |
| R31 | Round 7 said the third shortcut was never pressed by the self-test; the code pressed every registered shortcut and only the report's text was stale | Reading the code while adding checks | Report text fixed, round 7 corrected above |

### What round 8 does not prove

No run in **Edge** itself, none on **Outlook on the web, Teams on the web or SharePoint**, none of the browser's own context menu or shortcut UI, none of Edge Add-ons or enterprise-policy
installation, none on a managed computer, none with a signed build. The service-worker keep-alive of an open native port was not observed over a long session. The golden file proves
agreement on its cases only. No live model, no real RPM workbook, no real Drugs@FDA file (still unreachable from the build environment), so the RLD line stays labelled unverified.

## Round 9 — a full audit of the extension and its host, and a check that the documents say what the code does

Scope: the owner asked to verify and audit everything and to rebuild the affected artifacts. Method: re-run every suite, read the new code again looking for
what the tests would not catch (large inputs, hostile pages, stale state, wrong launch mode, claims in the documents), fix, add a test for each fix.

Local results on this code (read from the runs, not recalled): C# **446 passed, 8 skipped** (Windows-only); extension unit **31 passed**; standalone browser test **63 checks**;
native-messaging test **11 checks**; contrast audit of the extension palette 0 failures; `npm run validate` and `npm run security:docs` pass; a search of the app folder
for key and token patterns finds only placeholder strings in the tests (`sk-ant-test-not-a-real-key`), no key or certificate files. The extension's private signing key is not in the repository.

### Defects caught in round 9

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R32 | Finding the sister applications of a card compared every record with every other one (quadratic): a 5,000-row portfolio took too long to build all cards | Reading the code, then a timing test | Applicant index in the app and the extension; 5,000 rows build in under 6 s with the app's test, and in well under a second in the extension |
| R33 | The holiday list for a year was recomputed for every date asked about | Same timing test | Computed once per year |
| R34 | A selection inside a password or other sensitive field could be read as "the selected text" | Reading what the page reader touches | Sensitive fields read nothing; checked in the browser test |
| R35 | After the extension was updated, an already-open tab kept a dead script that threw on the next key press | Reasoning about extension updates | The script checks that its extension is still alive and goes quiet; checked by simulating a stale script |
| R36 | "Connect" in Settings, started from `dotnet run`, would have registered `dotnet` itself as the native-messaging host instead of the app | Reading the registration path | Refused unless the running file is the app itself; test added |
| R37 | A page scan re-read the imported portfolio once per number found | Reading the scan path | Read once per scan |
| R38 | The native test used dates in 2027 that would have become "past" and changed the expected card | Looking for clock-dependent tests | Dates moved; the rest of the suite already used a fixed clock |
| R39 | The popup's tabs could not be moved with the arrow keys (ARIA tabs pattern), every tab was a tab stop, and "Settings" was a link without a link target | Accessibility review | Arrow keys, Home and End; one tab stop; Settings is a button; 5 browser checks |
| R40 | `ProgressControl` threw when drawn at zero width (found when three headless card tests failed behind a masking teardown error) | Failing tests | Early return and a safe clamp; regression test |

### What round 9 does not prove

Not run in **Edge**, not on Outlook on the web, Teams on the web or SharePoint, not with a **screen reader** (the keyboard path and the labels are checked by script only),
no managed computer, no signed build, no live model, no real RPM workbook, no real Drugs@FDA file. The timing numbers are from one Linux container; they show the work is no longer
quadratic, not a promise on a given PC. The audit is mine and of my own code; it is not an independent review, and a person who did not write it should still read `SECURITY.md`.

## Still open

GDUFA III tier table (needs the commitment-letter text and the dual-source rule); a real-keyboard check of rebound shortcuts on
non-US layouts (rebinding is built); UI Automation capture; code signing; the live Claude call; **the live Elsa call (the two
translations in `ElsaMapping.cs`, once the API is available)**; a 508 pass with a screen reader; a real-email-client
check that compact boxes do not disturb typing; a real-mouse hover check for auto-hide on Windows.
`npm run test` for the whole repository stops in this authoring environment at an unrelated test that needs a
newer `git` (`--ref-format=reftable`); `npm run validate` and `npm run security:docs` pass.
