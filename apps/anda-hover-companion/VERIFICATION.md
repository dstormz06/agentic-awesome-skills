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
| The browser simulation matches the app's logic | 30 recognizer cases, the status line, the offline brief and the placement rule were compared in a browser against the C# tests' expectations: no differences | The simulation is a port, not the app. The window chrome, hotkey and clipboard hand-off are simulated and labeled as such |

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
| R7 | **"NDA 021234" was recognized as an ANDA number**, so a real ANDA with that number would show the wrong card | Test authoring | Recognizer refuses labeled non-ANDA numbers; 14 tests; ported to the simulation |
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
chat and browser windows (labeled "Outlook-style", "Teams-style", "Edge-style", with no logos or real products) plus an optional
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
| **The extension's JavaScript says exactly what the app says.** Same calendar, watch flags, urgency, cards, draft check, CSV and .xlsx reading | `extension/tests/golden.json` is written by the app's own code (148 date flags, every day of 2026-2027, 27 cards under three clocks, 12 drafts, 30 recognizer texts, 8 portfolio files with 17 date formats and non-dates, an .xlsx with a hidden sheet); the JavaScript reproduces every case. The app's test fails if the app drifts from the file. Both run on Linux and Windows. Breaking the JavaScript on purpose fails 3 of the 7 parity tests | A fixed set of cases, not a proof. A rule not in the fixture could differ |
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
agreement on its cases only. No live model, no real RPM workbook, no real Drugs@FDA file (still unreachable from the build environment), so the RLD line stays labeled unverified.

## Round 9 — a full audit of the extension and its host, and a check that the documents say what the code does

Scope: the owner asked to verify and audit everything and to rebuild the affected artifacts. Method: re-run every suite, read the new code again looking for
what the tests would not catch (large inputs, hostile pages, stale state, wrong launch mode, claims in the documents), fix, add a test for each fix.

Local results on this code (read from the runs, not recalled): C# **446 passed, 8 skipped** (Windows-only); extension unit **31 passed**; standalone browser test **63 checks**;
native-messaging test **11 checks**; contrast audit of the extension palette 0 failures; `npm run validate` and `npm run security:docs` pass; a search of the app folder
for key and token patterns finds only placeholder strings in the tests (`sk-ant-test-not-a-real-key`), no key or certificate files. The extension's private signing key is not in the repository.

CI: commit **`292363e`** (run 16): Linux and Windows jobs green. On Windows the extension's 31 unit tests pass (read from the log) and the self-test is **46 PASS, 0 WARN, 0 FAIL, 1 SKIP**
(the live Claude connection, by design); the real exe answered over stdin/stdout for the native host in **571 ms** on the first try. The run's own xUnit totals were not re-read; the 446 above are the local Linux count.

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
| R41 | The two **published pages** (desktop simulation, appearance lab) were never republished after rounds 7 to 9: the simulation was still the Nov 1 version without the whisper default, watch list, draft check or Edge right-click menu, and the lab still showed eleven looks with the dark graphite K. I had refreshed the downloadable copies and said so, which is not the same thing | The owner asked whether the simulation artifact was rebuilt; I compared the live pages with the repo files | Both republished to their existing links from the repo files; the live pages' scripts and styles were then compared with the repo byte for byte. Rule from now on: any change to `mockup/*.html` is followed by a republish and a comparison of the live page |
| R42 | Documents said things that were no longer true: `PROPOSAL.md` (the director one-pager) listed the testers' requests as "not built" and omitted the extension, the watch list and the weekend flags; `DESIGN.md` §12 still showed "copy-ready status lines" as next to build and the grounded proofread as not built, and its list was numbered out of order | Reading every document against the code and `ROADMAP.md` | Both rewritten to the current status; the proposal also states the rollback for the Edge connection and what the evidence does not show |
| R43 | The pitch said more than the evidence: "Tested on a real Windows machine" (it is a cloud build machine, not an agency workstation); slide 3 said "Built" without saying what that means; slide 5 said proofread, rewrite and fact-check were "built, needs an approved model" without "never run on a live model"; the slide 4 notes still told the run 12 story | Reading every slide, note and the one-pager | Wording fixed in the deck, notes and one-pager; deck, one-pager and PDFs rebuilt and each page looked at |

Also checked, no change needed: the app's Claude request sends only the model, a token limit, the system text and the messages (no sampling parameters, no thinking setting, no prefill), which matches the current API reference, including its rejection of sampling parameters from 2026-10-21. `claude-opus-4-8` is still on Anthropic's current model list (checked against the reference cached 2026-10-06); a newer Opus exists. It was left alone on purpose: newer models think by default and the 16-token connection test would need a larger budget. Nothing here has been run against the live API.

### What round 9 does not prove

Not run in **Edge**, not on Outlook on the web, Teams on the web or SharePoint, not with a **screen reader** (the keyboard path and the labels are checked by script only),
no managed computer, no signed build, no live model, no real RPM workbook, no real Drugs@FDA file. The timing numbers are from one Linux container; they show the work is no longer
quadratic, not a promise on a given PC. The audit is mine and of my own code; it is not an independent review, and a person who did not write it should still read `SECURITY.md`.

## Round 10 — the owner's test of the simulation: shortcuts, reference dates, connection status, own API, writing preferences

Scope: the owner tried the published simulation and reported that Ctrl+Alt+Space did nothing, that Settings offered no other shortcuts or ways to start it, that no
PSG or RLD dates appeared, and that there was no place to point to a portfolio, notes, an API or writing preferences. They asked that the app always say whether an API is
connected, and whether a middle-click trigger is possible. They asked to be shown options for the Bronze look and four new looks before anything is final.

**What was true.** The Windows app already had rebindable shortcuts, My data and writing instructions; the simulation showed only a hint paragraph where those sections belong, and
its three shortcuts were hard-coded, so the owner could not find them or change one. The cause of Ctrl+Alt+Space doing nothing on the owner's computer is **not known**: the likeliest
explanation is that another program already owns that combination (many apps claim a global shortcut), and the browser never sees a key another program took. That is a hypothesis, not
something observed.

Local results on this code: C# **492 passed, 8 skipped**; extension unit **31**; standalone browser test **68 checks**; native-messaging test **11 checks**; simulation: all seven
earlier suites pass and the new one has **65 checks**; `npm run validate` and `npm run security:docs` pass.

| Claim | Evidence | Limit |
|---|---|---|
| **Both reference lines are always on the card** (PSG and RLD LABEL): a date with its source, or a brief reason there is none | `ReferenceLinesTests`, the card window test, the regenerated golden file (the extension's JavaScript reproduces it), and browser checks in the extension and the simulation. The fictional demo records carry made-up dates that say so | There is **no live PSG date**: no official source is connected. RLD comes from a Drugs@FDA file the person points to, or from Update now |
| **Update now** fetches a newer Drugs@FDA file only when pressed, from an https address the person pastes, and replaces the old file only if the new one reads as a real Drugs@FDA file | `ReferenceUpdateTests` with a fake server: a good file replaces, a web page does not, a bad address or a 404 fetches nothing or changes nothing | **Never run against FDA** (FDA hosts were unreachable from the build environment); the app does not know or guess FDA's file address |
| **The app says whether an API is connected**: offline, set up but not tested, connected, or not reachable; never "connected" on a setting alone | `ConnectionStatusTests`, card and Settings window tests, the simulation (which can never say Connected) | "Connected" has been seen only against a fake server |
| **My API**: a provider described in Settings, https only, the token only ever a variable's name, bad templates and addresses refused in words, errors never show the reply | `CustomApiTests` (24 cases) against a fake handler, including a draft with quotes and newlines that cannot break out of the request | **Never run against any real service** |
| **My writing**: tone, style, sign-off feed the writing actions; **quick replies** fill from the card with no AI | `QuickReplyTests`, `Settings` round trip, the card button and menu in the simulation | The writing actions themselves are still never run against a live model |
| **Middle-click trigger**, off by default, exactly the chosen key held, never swallows the click | The decision rule is unit-tested; the real hook and the injected clicks are exercised by the Windows self-test check `mouse-trigger` | Windows runner, not a person on a real machine; apps that scroll or select on a middle-click behave their own way |
| **Shortcuts**: a live line per shortcut names one that another program owns | `HotkeyService.Report` tests; the simulation re-binds, refuses one modifier, the Windows key and duplicates, and shows a taken shortcut | The real registration result on the owner's computer has not been seen |

CI: commit **`78c7f5d`** (run 18): Linux and Windows jobs green. The Windows self-test fails the job on any FAIL, so it passed, and its table includes the new **`mouse-trigger` PASS** ("a plain
middle-click was ignored while Ctrl was required, and Ctrl + middle-click reached the app; the click was passed on untouched"), the Settings window rendering at 492x776 px, and the earlier
checks unchanged (hotkeys delivered, select-and-peek, placement, compact boxes). The run's own xUnit totals were not re-read; the 492 above are the local Linux count.

### Defects caught in round 10

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R44 | In the simulation, a pinned card sat above the tray menu and above Settings, so neither could be clicked | The new simulation test | The tray menu is above every card; a window opened on purpose is above a pinned card |
| R45 | The simulation's "Claude access is approved" switch did not update the connection pill unless a card was open | The new simulation test | The switch repaints the pill |
| R46 | The simulation only hinted at Settings that the app really has, and hard-coded its three shortcuts | The owner's test | Real, working (but simulated) sections for shortcuts, other ways to start it, My data, My writing and the connection |
| R47 | The card hid the PSG and RLD lines when no source was set, so they looked missing | The owner's test | Always two lines, with a brief reason |
| R48 | Nothing on screen said whether an API was connected | The owner's test | Pill, banner and tooltip, driven by what really happened |

### What round 10 does not prove

No run on the owner's computer, so the Ctrl+Alt+Space cause is a hypothesis. No real service for My API, no FDA host for Update now, no person on a real machine for the middle-click.
The simulation is a mock: it checks the My API form but sends nothing, and its "My data" pickers are simulated on purpose, because real files should not go into a hosted page.
The four Bronze header alternatives and four new looks are **proposals** on a separate page; none is in the app or the lab until the owner chooses.

## Round 11: change things in the app, Outlook (optional), a pattern audit, names, and a writing pass

Local results on this code: C# **557 passed, 8 skipped**; extension unit **31**; standalone browser test and native-messaging test both pass; simulation: all nine suites pass
(the new one has **56 checks**); `npm run security:docs` passes.

| Claim | Evidence | Limit |
|---|---|---|
| **Auto-hide is 10 seconds by default** in the app, the extension and the simulation | `AutoHideTests`, `SettingsTests`, the extension's default settings, simulation suites 4 and 9 | A real-mouse hover check on Windows is still open |
| **The Claude model name is a Settings field**; an invalid name falls back to the default | `CustomizationTests` (factory, status text, damaged settings file) | Never run against the live API |
| **Back up, restore and reset settings, lenses and quick replies**; no secret and no file location inside; a damaged, oversized or newer file changes nothing; out-of-range values are repaired | `CustomizationTests`, `Round11UiTests` (two-step reset), simulation suite 9 (a real file round trip, pasted text, three refusals, a repaired file, reset) | The operating system's own save and open dialogs are not exercised by the tests |
| **A Go to list** jumps to any Settings section | `Round11UiTests`, simulation suite 9 | |
| **Outlook (optional, read-only, off by default)**: device sign-in, `Mail.Read` only, token in memory, a search only for a six-digit number, plain-text results, one plain sentence per failure | `OutlookTests` (44 cases) against a fake Microsoft server: pending, slow-down, declined and expired codes; both clouds use only their own addresses; the request is a GET with a Bearer token and no write scope; anything that is not exactly six ASCII digits makes no request; control characters are cleaned; 401, 403, 429 and 500 are explained without the server text; the token is absent from saved settings and from a backup; sign-out ends a sign-in in progress; a token expires by the clock; the card's drawer exists only when Outlook is on and searches only when opened | **Never run against Microsoft.** Written from Microsoft's public documentation. The US Government addresses are from memory and unverified. The `$search` result order and the consent an organization requires are unverified. A person with a real tenant must try it |
| **The simulation rehearses Outlook** with a made-up code and made-up messages; it contacts nothing | Simulation suite 9 | It proves the screens, not the connection |
| **The documents are shorter and plainer, and say what the code does now** (the deck gained a Settings slide; the one-pager is still one page) | Rebuilt and checked page by page; the claims in them were re-read against this table | A reader has not yet judged whether they read well |

CI: code commit **`bba1417`** (run 19): Linux and Windows jobs green. The Windows self-test is **47 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design), including
`mouse-trigger` and the Settings window rendering at 492x788 px with the new sections; the Edge extension's 31 unit tests pass on both machines (read from the log). The run's own xUnit totals
were not re-read; the 557 above are the local Linux count. The later commit changed only the simulation and this file, which the workflow does not watch.

### Defects caught in round 11

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R49 | The Outlook number check used `$` and `\d`, so `216482` followed by a new line, or six non-ASCII digits, would have passed | Writing the edge-case tests | `^[0-9]{6}\z`, with both cases tested |
| R50 | The Application ID check accepted forms Microsoft does not show (braces, no hyphens) | Comparing the app with the simulation's rule | Only the hyphenated form is accepted, tested both ways |
| R51 | If a second Sign in cancelled the first, the first could still write "Sign-in cancelled" over the second | Reading the cancel path | Only the current sign-in reports a cancellation |
| R52 | The simulation reused the name `pick`, so the whole page failed to start | The first run of the existing suites | Renamed; never published |
| R53 | The deck, one-pager, proposal, roadmap, app README and demo guide still said Outlook was "decided against", counted 440 tests and gave a 20 s auto-hide | The owner's request, then a search for each claim | All updated; the roadmap keeps the earlier decision for the extension, which still has no mailbox access |
| R54 | The one-pager overflowed to a second page after the new wording | Rendering it | Trimmed and re-rendered: one page |

### What round 11 does not prove

Nothing here has run against Microsoft, a live model, a real service or the owner's computer. The three **appearance** items are still proposals: the audit found three
geometric backgrounds in two looks (D and F), and none is removed until the owner chooses from the Options page. The ten **names** are proposals; availability and any
trademark question have not been checked.

## Round 12: the name, no patterns, and four new Bronze looks

The owner kept the name **ANDA Companion** and added the nickname **Notula**, chose the recommended Plain paper (D) and Bronze band (F), and asked for every geometric
background to be removed. Local results on this code: C# **588 passed, 8 skipped**; extension unit **31**; standalone browser test and native-messaging test both pass; simulation:
all ten suites pass (the new one has **39 checks**); `npm run security:docs` passes.

| Claim | Evidence | Limit |
|---|---|---|
| **The name is ANDA Companion (Notula)** wherever the name appears: window titles, tray tooltip, notices, the extension's name and right-click menu, the simulation, the Appearance Lab, the documents, the deck and the one-pager | A search of the whole project for the old name and for any "ANDA Companion" without the nickname; simulation suite 10 checks the page title, every occurrence and the tray label | Folder, executable, settings folder, registry keys and the workflow name keep their technical names on purpose. Availability and trademark have not been checked |
| **No look draws a grid, hatching, facets or texture**: D has a solid square bar and no grid; F has a solid 5 px band and a plain header; the Ticket's dotted rule and the simulation's dashed list rules are plain lines | `Round12Tests` (no hatch or grid in code, F and D structure), `CompactRenderTests`, simulation suite 10 (no repeating gradient, no grid token, no facets, in the page and in the lab) | Soft color fades (the Gauge header, E's bar, the simulated desktop) and the animated loading bar were not counted as patterns. Progress markers such as ticks, rings and dots show a value and are kept |
| **Four new looks, P Gauge, Q Folder, R Rail and S Tag**, in the app, the simulation and the Appearance Lab, each with its own layout and every fact on the card | `Round12Tests` (every fact on each new look; the dial, the tab and square corner, the days column, the centered head), headless renders of both records, contrast and palette tests that run over every look, simulation suite 10 (every fact, shapes, AA text contrast on D, F, P, Q, R, S) | Q has no folded corner and S has a plain bar instead of a ruler, to keep patterns out. In the Windows app the Gauge's dial sits between the goal and the forecast rows, not beside them. The extension has one fixed card look and no appearance switch |

CI: commit **`aad40dd`** (run 21, the final state of this round's code and documents; run 20 on the commit before it was replaced by it): Linux and Windows jobs green. The Windows self-test is
**51 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design). It now renders the four new looks (`render-card-p`, `-q`, `-r`, `-s`: all PASS at 424x720 px) and the Settings window
at 492x788 px, and `mouse-trigger` and the earlier checks still pass. The extension's 31 unit tests pass on both machines. The run's own xUnit totals were not re-read; the 588 above are the local Linux count.

### Defects caught in round 12

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R55 | In the Tag layout the progress caption sat on top of the confidence text, because the caption kept an old grid column from its previous home | A render of the new look | The column is set explicitly |
| R56 | The headless test session was started and stopped for each test class, and sometimes failed on Avalonia's font manager ("fonts:SystemFonts"), so two unrelated tests failed in one full run | The full suite, run after adding a test class | One shared session for the whole run; two full runs passed |
| R57 | The Ticket's stub rule was dotted and the simulation's list rules dashed: small repeating patterns | A search for dashes and dots after the owner asked for no patterns | Plain lines |

### What round 12 does not prove

Not run in real Edge, on a managed computer, against a live model or Microsoft, or on the owner's machine. The new looks have been rendered headlessly and in a browser, not seen on a real Windows display.

## Round 13: a pre-demo audit and a courteous tone pass

The owner asked for a triple check before a director's test and for everything to sound slightly polite and professional. Local results on this code: C# **663 passed, 8 skipped**;
extension unit **31**; standalone browser test **68 checks**; native-messaging test **11 checks**; simulation suites 1 to 12 all pass, including a new look-by-viewport matrix of
**1,090 checks**, a director's-walkthrough suite of **24 checks** and a card-and-window width suite of **254 checks**; `npm run security:docs` passes.

| Claim | Evidence | Limit |
|---|---|---|
| **Every full look in the app shows its facts and stays inside its window** in five situations: a sparse record, very long text, the short peek, the expanded mail drawer, and a healthy record | `LookRobustnessTests` (75 cases: the 15 full looks by 5 situations; no text outside the card, the number shown, PSG and RLD lines on every full card, no "NaN"), headless renders. The four compact boxes have their own render tests | Headless frames are used for layout only; no real Windows display was used |
| **Every look in the simulation opens inside the window at seven screen sizes** (1920x1080, 1366x768, 1280x720, 1024x768, 768x1024, 390x844 and 360x640) for two records, with no inner horizontal scroll and no page-level scroll; the Lab and the options page also have no sideways scroll at 1440, 768 and 360 px | Simulation suite 11 (1,090 checks: 19 looks by 2 records by 4 checks, plus the per-size and Lab checks) | A Chromium engine, not Edge; a real phone was not used. The matrix checks position and overflow, not text contrast; contrast is covered by suites 10 and the C# palette tests |
| **The main demo steps work in the simulation**: the urgent peek, Full brief, the PSG and RLD lines, the offline pill, the 10-second auto-hide (and a pinned card staying), the invite on a healthy ANDA, an NDA number declining to open a card, the lookup box, the draft check, the tray menu, the Watch list and Settings | Simulation suite 12 (24 checks, one of which waits 11 seconds in real time) | Simulated shortcuts and clipboard; "Copy status" and the clipboard restore are covered by earlier suites, not this one; the Windows build was not re-run by hand |
| **Wording is courteous and brief** across the app (window titles, hints, notices, errors), the extension, the simulation, the documents, the deck and the one-pager | A search of the whole project; the notice-length test (50 characters at most) still passes; golden strings changed in C#, JavaScript and `golden.json` together | A matter of taste: the owner may want some lines softer or firmer |

CI: commit **`e550d41`** (run 22): Linux and Windows jobs green. The Windows self-test is **51 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design), including all
nineteen looks' renders, the Settings window, the hotkeys delivered as real keystrokes and Select & Peek end to end. The later commits of this round change only the simulation, the Appearance Lab
and these notes, which do not start a build. The run's own xUnit totals were not re-read; the 663 above are the local Linux count.

### Defects caught in round 13

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R58 | In the Rail look the "DAYS TO GOAL" caption was clipped in the app, and the simulation scrolled sideways at 1920, 1366 and 1280 px for one record; on a 390 px phone the "FORECAST" label touched its date | The new robustness test, the matrix and a screenshot | Wider column, a wrapped caption, a narrower label, a phone rule and a slightly wider phone label |
| R59 | The simulation's guide said "eight steps" above nine items | A fresh read | "nine steps" |
| R60 | The old uppercase name survived in `START-HERE.txt` (the earlier search was case-sensitive) | A case-insensitive search | Renamed |
| R61 | Adding "Please" made one capture notice longer than 50 characters, and two extension tests expected the old capital letter | The notice-length test and the extension test | Shorter wording; tests updated |
| R62 | The round 12 note said "40 checks" for a suite that has 39 | A recount | Corrected |
| R63 | **In the simulation the card became a full-width bottom sheet whenever the window was 1040 px wide or narrower, or the pointer was touch**, and the Settings, Lookup, Draft, Watch list and Prompt library windows stretched edge to edge at the same widths. A narrow preview pane (as in the Claude artifact viewer) therefore looked like a phone, and the card was "full width" almost every time. It is a floating 372 px card, as in the Windows app | The owner, while demoing; the earlier suites checked that the card stayed inside the window, not how wide it was | The bottom sheet and the full-width windows now apply only at 600 px or less (a phone held upright). Above that the card and the windows keep their own width and stay inside the window. New simulation suite 13 (254 checks: four looks and six windows at ten window sizes, touch and mouse) |

### What round 13 does not prove

Not run in real Edge, on a managed computer, against a live model or Microsoft, on a real RPM workbook or a real Drugs@FDA file, or on the owner's machine. The Windows build
has not been re-run by hand. Name availability and trademark are unchecked.

## Round 14: a second pre-demo audit, with tests that were not run before

The owner asked again for an explicit test of everything before a director's test. This round added checks of kinds not run earlier: the simulation inside a frame (as the artifact viewer shows it), an automated accessibility scan,
keyboard-only use, and a differential fuzz of the number recognizer and the draft check across the app's C#, the extension's JavaScript and the simulation. Local results on this code: C# **663 passed, 8 skipped**; extension unit **31**;
standalone browser test **68 checks**; native-messaging test **11 checks**; simulation suites 1 to 17 all pass; `npm run security:docs` passes.

| Claim | Evidence | Limit |
|---|---|---|
| **The demo path works when the page is shown inside a frame of any width** from 1100 down to 390 px: peek, full brief, lookup, draft check, Watch list and Settings open inside the frame, at the right width, with no sideways scroll | Simulation suite 14 (152 checks, eight frame widths) | A Chromium frame on a local file, not the viewer itself |
| **No WCAG 2.0/2.1 A or AA failure, and no best-practice failure, that axe-core 4.10.2 can find** in the simulation (desktop and phone, eight card looks, the expanded card and all six windows), the Appearance Lab or the options record | Simulation suite 15 (34 scans, zero violations) | An automated scan finds only part of what a screen-reader user meets; the manual 508 pass in "Still open" is not done |
| **The page can be used from the keyboard alone**: every Tab stop is a visible control with a focus ring; the lookup box takes the keyboard at once; Tab moves through the card; Esc closes the card and the draft check; E expands the card and S copies the status | Simulation suite 16 (12 checks) | Chromium only |
| **The number recognizer gives the same answer in the app, the extension and the simulation** on 20,000 generated strings, and the draft check gives the same findings in the app and the extension on 6,000 generated drafts, with no exception in either | A differential fuzz run on this code (generated corpus, seeded, kept out of the repository), plus eight new fixed cases in the shared golden fixture that the app, the extension and simulation suite 17 must all reproduce (38 cases) | Generated text, not real emails. The fuzz compares implementations with each other; it cannot show that all three are wrong in the same way |

CI: commit **`01f2739`** (run 23): Linux and Windows jobs green. The Windows self-test is **51 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design), with all nineteen looks rendered,
the hotkeys delivered as real keystrokes and Select & Peek working end to end. The extension's 31 unit tests pass on both machines, so the app and the extension still reproduce the shared fixture,
including the eight new recognizer cases. The run's own xUnit totals were not re-read; the 663 above are the local Linux count.

A second run, **run 24** (commit **`b5e5e87`**), rebuilt the Windows demo bundle after `DESIGN.md` was corrected (R69): Linux and Windows jobs green again, with the same Windows self-test result
(**51 PASS, 0 WARN, 0 FAIL, 1 SKIP**). The demo bundle to download is the one from run 24, not run 23.

### Defects caught in round 14

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R64 | **A number written as `NDA No123456` or `NDA Number066785` (no space after the label) was treated as an ANDA by the app and the extension**, although the simulation rejected it. Showing an ANDA card for a labeled NDA number is the wrong-card case the recognizer exists to prevent | The differential fuzz (25 of 20,000 strings) | The label pattern now accepts a digit straight after "No" or "Number" in all three; eight cases added to the golden fixture |
| R65 | The app's recognizer accepted non-ASCII digits (for example full-width `２１６４８２`) as a number, where the extension and simulation did not; the card lookup then reported it as unknown, and an ASCII number later in the same text could be skipped | The same fuzz (437 differences) | The app's pattern is now ASCII digits only, like the other two; two golden cases added |
| R66 | The simulation's lookup box took focus 30 ms after it opened, so very fast typing (or a script) was lost | A keyboard test | Focus is set at once, with the delayed focus kept as a backstop |
| R67 | The simulation had no main landmark or top-level heading, jumped from a level-1 heading to level 3, and the options record had invalid table roles on a list and no language | The accessibility scan | A main landmark, one `h1`, level-2 headings for the windows, valid roles and an explicit page language |
| R68 | "999999 is not a recognized ANDA" and "Not a recognized ANDA number." were blunt and imprecise: the first means the number is not in the available data, the second that nothing like an ANDA number was typed | A wording review | "999999 is not in the available records." and "Please enter a six-digit ANDA number." in the app, the extension, the native host and the simulation |
| R69 | `DESIGN.md` (which is also copied into the Windows demo bundle) still quoted the old lookup message "Not a recognized ANDA" after R68 changed it, because the search for the old wording covered code and tests but not the documents | The owner asked whether the published artifacts and files were all current; the check of every sent file and of the documents against the code | `DESIGN.md` quotes the new messages; the search now covers every document type. A new CI run rebuilds the demo bundle so it carries the corrected file |

### What round 14 does not prove

Not run in real Edge, inside the Claude artifact viewer itself, on a managed computer, against a live model or Microsoft, on a real RPM workbook or a real Drugs@FDA file, or on the owner's machine. The Windows build has not been run by hand.
Name availability and trademark are unchecked.

## Round 15: your own actions and agents, a live to-do list, grouped Settings, and 30% transparency

The owner asked for: lines that separate the kinds of settings; transparency from 100% down to 30%; a place for a project manager to add their own prompt or agent, started by the shortcut they choose; a live to-do list;
a way to call DPM's two tested agents (Alert Hub and Triaging Agent) and show what they find on the card or in the menu, and to register agents of their own; a test in the browser; and an adversarial review.
Local results on this code: C# **762 passed, 8 skipped**; extension unit **31**; standalone browser test **68 checks**; native-messaging test **11 checks**; simulation suites 1 to 17 all pass (including 1090, 254, 152 and 38 checks in the large ones);
a new simulation suite for this round, **143 checks**; a new accessibility scan of the new windows and states, **16 scans, no violations**; `npm run security:docs` passes.

| Claim | Evidence | Limit |
|---|---|---|
| **Settings has seven groups under headings, a strong line between groups and a fine line between settings**, in the app and the simulation | `Round15UiTests` (the seven headings in order, the slider range) and simulation suite 18 (headings, lines drawn at 2 px and 1 px, sections in the right groups, the Go to list reaches the new sections) | Layout judged from headless and Windows-runner renders, not by a person |
| **Transparency runs from 100% down to 30%**: the slider, the card surface at 30% is about 30% opaque while its text stays fully opaque, and a saved or restored value outside the range (or not a number) is brought in | `Round15Tests` (`NormalizeOpacity`, load repair), a 30% card shot in the Windows self-test (run 25, PASS), simulation suite 18 (computed color alpha 0.30; 5 becomes 30, text becomes 100) | Readability at 30% over a busy background was not judged by a person. The hint says text is easier to read at a higher value |
| **A person can write their own actions** (add to my to-do list, fill my prompt, ask my AI, run an agent), give each a shortcut, and run it by the shortcut or from the tray menu. A shortcut follows the core rules and cannot duplicate another (core or custom) | `Round15Tests`, `ActionRunnerTests` (21 tests of what each kind does), `Round15UiTests` (the editor, the shortcut box, refusals), simulation suite 18 (editor, shortcut capture, duplicates, running each kind, empty selection asks for a number, unknown number, terminal) | The delivery of a person's **own** shortcut as a real keystroke was not run on Windows (the three core ones were, in run 25); it uses the same registration and the same thread. `App.Actions.cs` is thin glue over `ActionRunner` and is covered by building and by the runner's tests, not by a test of its own |
| **Nothing is sent to an AI or an agent until Send is pressed** (unless that one action is set to send at once); the preview shows exactly the text, and the selected text is fenced as data and cannot close its own tag; a late reply to a closed window is dropped; two presses send once | `ActionRunnerTests` (nothing seen by the fake AI or the fake server before Send; the system message and the shown text are what was sent), `Round15UiTests`, simulation suite 18 | Tested against fakes. A person can still send text they should not |
| **Alert Hub and Triaging Agent are listed, switched off and without an address**, with a connector that takes an address, token header, the NAME of the token variable, a body and a reply path. Findings appear in the card's drawer as the agent's own words; a line joins the to-do list only on a click | `Round15Tests` (presets, set-up check, nothing sent for an agent that is off or incomplete, one request per agent and ANDA, cache and expiry, failure messages without the service's reply), `Round15UiTests` (drawer hidden with none on and on a peek, Run, findings, Add, failure, automatic runs), simulation suite 18 | **Not connected to either agent: their addresses and message formats are not known and are not guessed.** Tested against a fake server only; never against a real agent. Findings are shown on the card, not in the tray menu |
| **An agent runs by itself only if the person chose that for that agent** (when a full card opens, or only when at risk); a peek never runs one; an automatic agent cannot use `{selection}`, and the card's own request passes no selected text | `Round15UiTests` (peek, expanding a peek, at-risk and not at-risk), `Round15Tests` (automatic plus `{selection}` is refused or repaired), simulation suite 18 | Off by default; no agent has been set up in any test other than a fake |
| **The to-do list is live while open and idle when closed**: it shows the person's items and items from the data (the same rules as the card and the watch list), updates at once when the list changes, refreshes at the chosen interval only while open, and closing stops the timer. Finished, snoozed and ticked-off items behave as described; a damaged or oversized file is ignored | `Round15Tests` (store, builder, file repair, concurrency, 3,000 ANDAs in under 5 s with the data items capped at 400), `Round15UiTests` (the window, the timer starting, changing and stopping, nothing refreshing after close), simulation suite 18 (including a fake clock: refreshed after 30 s, nothing while closed) | Long-running behavior (hours open) was not observed. One person, one PC: **there is no shared supervisor list** |
| **A backup or a hand-edited file can no longer switch on a connection that sends data or a token away**: a restore returns My API to Auto and brings every agent back off and not automatic, and says so | `Round15Tests` (a hostile backup naming an attacker's address and a secret's variable name), `Round11Tests` (updated), simulation suite 18 | The address and variable name are kept so the person can read them; switching them on again is the person's own act |
| **The new windows pass the same accessibility scan as the rest**: Settings with the new editors, the to-do list, the action window (preview and reply), the card's drawer (not asked, answered, failed) and the tray menu, on desktop and phone | Simulation suite 19 (16 scans, axe-core 4.10.2, no violations) | An automated scan finds only part of what a screen-reader user meets |
| **The new windows render on a real Windows machine**: the to-do list, an action window with a reply, the card's agents drawer (canned text, no network) and the 30% card | Windows self-test in run 25 (render-todo, todo-list-content, render-action-window, action-window-reply, render-agents-drawer, agents-drawer-finding, render-card-k-transparent-30: all PASS) | The screenshots were not opened by a person here |

CI: commit **`c663690`** (run 25): Linux and Windows jobs green, including the Windows self-test with the new window checks above.

The final code of this round is commit **`95b9743`** (run 26): Linux and Windows jobs green; the extension's 31 unit tests pass on both machines; the Windows self-test is **58 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design), including the to-do list, the action window, the agents drawer and the 30% card. The run's own xUnit totals were not re-read; the 762 above are the local Linux count. The demo bundle to download is the one from run 26.

### Defects caught in round 15

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R70 | The shared connection (My API, and so the agents) **followed redirects with the token header attached**, so a redirect could have sent the token to another host; a reply had **no size limit**; and placeholders were filled one after another, so a value containing `{{x}}` could be filled again | Adversarial reading of the connector while adding agents | No redirects, a 2 MB reply limit, one-pass fill; tests for each |
| R71 | Two simultaneous asks of the same agent for the same ANDA could both go out (`GetOrAdd` may run its factory twice) | Review of the single-request rule | A lock around a plain dictionary; a test with many callers sends one |
| R72 | Items from the fictional demo data were judged against the real calendar, so every demo ANDA looked overdue | A test of the to-do list on the demo records | The demo records are judged at their own extract date, as the watch rules do |
| R73 | **Expanding a peek with "Full brief" never showed the agents drawer or ran an automatic agent** (the drawer was prepared only when a card was first shown) | Mirroring the card in the simulation | The drawer is prepared when a peek expands; a test covers it |
| R74 | After a failed ask the card said "Not asked yet" (the failure was not remembered) and the button still said "Run" | A test of the failure path | The latest failure is shown with its message and "Try again"; a failure is still never counted as a finding |
| R75 | Cleaning a selection or a reply had a line-ending branch that could never run, so a lone carriage return joined two lines | Reading the code against the simulation's port | Line endings are normalised first; a test covers CRLF and lone CR |
| R76 | **A restored backup could switch on My API or an agent**, and the token variable named in it (any variable, for example one holding another service's key) would be sent to the address in the same file the first time a card opened | Adversarial review: "what if the backup is not the person's own?" | A restore never switches a connection on (see the table); the existing restore test was changed on purpose |
| R77 | **In the simulation, a restored shortcut could carry markup and ran script** (checked: before this round a 28-character value did run), and tray-menu labels were built from text as markup | The same review, then a check against the version from before this round | Shortcuts must be a few known modifiers in a fixed order and one known key; text is escaped or set as text; attack cases added to suite 18. The Windows app was never affected: it draws text, not markup |
| R78 | In the simulation the drawer did not say "Asking…" because the state was recorded after the first paint | The new suite | The task is recorded first |
| R79 | In the simulation a reply lost its line breaks (the cleaning removed them), so the suggested items ran together | A screenshot | Cleaning keeps line breaks, as the app does |
| R80 | The action editors' agent lists went stale after an agent was added or renamed, and a restore or reset left the editors holding the old lists | Review of the Settings editors | The lists refresh in place; a restore and a reset rebuild the editors |
| R81 | The Settings control count in an existing test no longer matched the new controls | The full test run | The test now counts them (five to-do refresh choices and the new panels) |

### What round 15 does not prove

Not run against a real Alert Hub, a real Triaging Agent or any live model, so the form may need a different body shape once their details are known. Not run in real Edge, inside the Claude artifact viewer, on a managed computer, on a real RPM workbook or by a real project manager.
A person's own shortcut has not been delivered as a real keystroke on Windows. The tray menu's new entries were built but not clicked on Windows. The simulation's replies from "an AI" and "an agent" are labeled stand-ins built from the record's own rules, not real answers.
A supervisor view across several project managers is not built.

## Round 16: My to-do list, Notes to my to-do list, Settings that arrange themselves, 40–100% transparency, a third agent

The owner asked for: Settings that adapt (collapse most sections, open what is needed most, with a walk-through); transparency from 100% down to 40%, starting at 92%; a separate to-do mode for the daily workload (any task, ANDA related or not),
unlike the ANDA to-do list, on its own shortcut chosen in Settings, turning highlighted rough notes into suggested items; a place for the project manager's own prompt or agent (done in round 15); the three tested agents (Alert Hub, Patent Analysis, Triaging Agent);
a first day that a computer novice can manage; the two best ideas for DPM leadership and RPMs; a test in the browser; and an adversarial review.

**Correction after the first delivery of this round.** The owner reviewed it and said two things: the sample note ("Don't forget to call the plumber about the leak under the sink tomorrow!!") was not professional, and the new list is **not a checklist** but a to-do list for a review project manager's daily workload, not only ANDA related.
Both were right. The first delivery called the feature "My checklist", described it as "work or home", used household samples throughout (the chat note, the empty-state example, the placeholder, the self-test notes, the shared fixture and most tests) and had a reader vocabulary weighted toward the house.
The redo renamed the feature everywhere (app, simulation, documents, tests, file `mytodo.json`), re-framed it as the daily workload, replaced every sample with review-workload examples (facility inspection date, information request, status update for a supervisor, timesheet, annual training), made the categories Work, Personal and Other,
added review-workload words and verbs to the reader, added an urgent count, and re-ran everything. Household words stay in the reader only as vocabulary for the Personal category; the demo and the screens no longer show them. The earlier build was never installed anywhere, so the renamed settings and file have no migration from the old names.
The two lists stay separate on purpose: **My to-do list** (typed or taken from notes) and the **ANDA to-do list** (built from the portfolio file). Merging them is a design choice for the owner; it has not been made.

Local results on this code: C# **879 passed, 10 skipped** (eight Windows-only, one that saves pictures, one differential check, each skipped with its reason); extension unit **31**; the simulation suites all pass (suite 20, this round's, **135 checks**; suite 21, the accessibility scan of the new surfaces, **16 scans, no violations**; suite 18, the previous round's, **144**; the matrix of looks and sizes, **1,090**; the in-frame demo, **152**; the earlier accessibility scans, no violations); the notes reader's shared fixture, **53 cases**, and a differential check against the app on **32,168** random notes (about 77,000 items), identical.

| Claim | Evidence | Limit |
|---|---|---|
| **My to-do list is a separate feature for the daily workload**: its own shortcut (chosen in Settings; Ctrl+Alt+L at first; none allowed), windows and file, apart from the ANDA to-do list and from the data | `MyTodoTests` (store, repeats, month ends, damaged and hand-edited files, many writers, the grouped view), `MyTodoUiTests`, `Round16Tests`, simulation suite 20 | One person on one PC; nothing is shared |
| **Highlighted notes become suggested items by plain rules, with no AI**, each editable, and **nothing is added until Add** (or, by the person's choice, at once with an Undo for the batch) | The same tests: a paragraph of workload notes gives four items with their day, urgency and category (and a fifth, a remark, offered but not ticked); an unreadable day blocks the whole batch and is marked beside the item; add-at-once and Undo | The reader is rules, not understanding. It can split a sentence wrongly or miss a date written in a way it does not know; it never invents a day. No times of day. No yearly repeat. Tried on made-up notes only |
| **The browser copy of the reader gives the app's answer** | The 53-case fixture `mockup/mytodo-golden.json` (written by the app, read by both; every output was read by a person before it was accepted); `AndaCompanion.Tests/fuzz`: random notes (typos, other scripts, odd digits, long runs, notes over the limit) read by both: 32,168 notes, 0 differences | A fixture and random notes are not proof for every possible text |
| **Hostile or odd notes cannot crash, hang or run anything**: markup is shown as text, a huge paste is cut to 6,000 characters at a finished sentence, at most 30 items are offered, digits of other scripts are safe | `MyTodoTests` (hostile input, the limit), `Round16NoteParserAttackTests`, simulation suite 20 | No time limit exists in the browser copy; the app's patterns each have a 250 ms limit |
| **Settings arrange themselves**: eight collapsible sections; everything collapsed except what needs a look, what a new user needs first and up to two sections opened three or more times; every open section says why; Expand all, Collapse all and Suggested view; "Go to" opens a collapsed section; a Getting started card until hidden | `SettingsAdvisorTests`, `AdaptiveSettingsUiTests`, simulation suite 20 | Whether a real first-day user finds it easier is not measured. The rules are the author's |
| **Only a count per section is kept**, once per visit, never text; it can be forgotten or switched off; it is not in a backup; a file from another PC cannot plant it; a Reset forgets it | The same tests and `Round16Tests` | |
| **Card transparency runs 40–100% and starts at 92%** (an old 30% becomes 40%, a non-number becomes solid) | `Round15Tests`, `Round16Tests`, simulation suite 18, Windows run 27 (a 40% render) | **Contrast measured in the simulation:** at 100% every full look keeps text at 4.61:1 or better; at 92% over a light backdrop 4.56:1 or better; at 92% over a **black** window the default look K still holds 4.96:1, but A, B, C, D, E and G fall below 4.5:1 (lowest 3.85:1), and at 80% over black every look does. The labels could be darkened if the owner wants that; it has not been done. Not seen by a person on a real desktop |
| **Alert Hub, Patent Analysis and Triaging Agent are listed, off and without an address**, never guessed; they cannot be removed, and a restore brings each back off | `Round15Tests`/`Round16Tests`, simulation suite 20 | **Not connected to any of the three**: their addresses and message formats are not known here. Direct calls were never made |
| **The new windows pass the same accessibility scan as the rest** (Settings in three states, My to-do list, an editor with a problem, Notes to my to-do list, a problem beside an item, the tray menu; desktop and phone) | Simulation suite 21 (axe-core 4.10.2) | An automated scan finds a share of problems; no screen reader was used |
| **The new windows render on a real Windows machine and the to-do shortcut is delivered as a real keystroke** | Windows self-test, run 29 (commit `4b26d14`): render-notes-to-my-todo-list, render-my-todo-list, my-todo-from-notes (five suggestions read from rough notes, four added), hotkeys-delivered (Ctrl+Alt+L among the four), render-card-k-transparent-40 | A person has not used it |

CI: commit **`4b26d14`** (run 29, the redo): Linux and Windows jobs green. The Windows self-test is **61 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design), with the two new windows rendered, `my-todo-from-notes` (5 suggestions, 4 added) and `hotkeys-delivered` (Ctrl+Alt+L as a real keystroke). The first delivery of this round was run 27 (commit `cbcc8bf`) and its fix-up run 28 (commit `a0da4ad`); this run supersedes both for the to-do list.

### Defects caught in round 16

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R82 | The second "!" of "tomorrow!!" was dropped at a sentence boundary (the urgency was lost), and a lower-case word after a full stop did not start a new sentence | The first run of the paragraph test | A sentence keeps every closing mark and may begin with any letter, digit, quote or bracket |
| R83 | A greeting rule could swallow a task ("Thanks, please send the report"); "Review and approve the memo" left a one-word half; "1/2 cup" read as 2 January; capital words read as letter bullets | Reading the first version against hand-written cases | Word-count and verb checks; both halves of a split need two words; a fraction guard; the letter bullet is case-sensitive |
| R84 | **A note containing digits of another script (Arabic-Indic, full-width, mathematical) made the reader throw a format error** that nothing caught, in the window that opens on whatever text the person had selected | The differential check, then a test | Digits are written `[0-9]` in every pattern, and the public entry points also catch parse errors and return nothing; seven cases |
| R85 | The browser copy read 8 of 2,514 random notes differently from the app: a sharp s became "SS", the word boundary knew only ASCII letters, an astral digit counted as a word character | The same check | The copy keeps one character for one character and imitates the app's word boundary, including 16-bit pairs |
| R86 | Editing an item more than 30 days overdue and pressing Save without touching its day moved it a year ahead | Designing the editor's day field | A day left as loaded is kept exactly; a year is shown when it is not this one; days are written the same in every language setting |
| R87 | A repeating item added or edited without a day was stored with no date, so it showed in the wrong group and never came back on time | The same design review | A repeating item always gets the first day that fits |
| R88 | The notes window named a color that does not exist, which would have thrown when an item was added | Checking every resource name the new windows use against the theme | The existing color is used |
| R89 | Resetting a shortcut to its default, when that default was already taken, left the clashing value in memory | Review while adding the shortcut | The reset is refused, the earlier value is kept and the clash is explained |
| R90 | The tray menu kept naming the shortcuts as they were at start-up after Settings changed them | Review | The labels refresh when Settings change |
| R91 | Simulation: Settings scrolled sideways at 320 px | A narrower phone test than the earlier suites used | Both rows wrap |
| R92 | Simulation: the earlier contrast checks skipped translucent surfaces, so the new 92% start was measured against the wrong color | The owner's new starting value | The checks composite the card over a chosen backdrop; a phone bottom sheet is always solid |
| R93 | Simulation: opening the list from Notes left Notes in front of it | A screenshot | The window just opened is in front |
| R94 | Simulation: a transparency that was not a number became 92% instead of solid, unlike the app | The previous round's suite, run against the new code | Solid, as in the app |
| R95 | Older tests: Settings sections now start collapsed, so controls inside were not on screen; one example shortcut collided with the default | The full runs | The helpers expand every section; the example changed |
| R96 | **Unprofessional and mis-framed first delivery**: a household sample with "!!" as the headline example, "checklist" as the name, "work or home" as the purpose, household samples in the demo, the fixture and the tests | **The owner**, reading the demo | Everything renamed and re-framed, samples replaced; see the correction above |
| R97 | **The mechanical rename broke the simulation's Settings**: the new section key was lower case, but the page builds element ids from the key, so the page threw two errors and the shortcut box stopped capturing keys | Suite 20, the first run after the rename | The key is written as the id expects; the suite now passes |
| R98 | **A note longer than the 6,000-character limit could end with half a sentence ("Submit the repo") that became an item of its own** | Suite 20 (a repeated-sentence check began to give 2, not 1) | At the limit the reader drops the unfinished last sentence or line (looking back at most 300 characters), in the app and the simulation, with a unit test and over-limit cases in the differential check |
| R99 | The longer placeholder of the new add box was cut off in the real window ("...with the t") | Looking at the rendered Windows window | A shorter example |

### What round 16 does not prove

Not run on a real Windows desktop by a person: the shortcut was delivered as a keystroke by the self-test, but no one selected real notes in real mail and watched the window. The notes reader has only been tried on made-up and random text.
Not connected to Alert Hub, Patent Analysis or Triaging Agent, and not tried against a live model. Not tested with a screen reader. Whether the adaptive Settings and the Getting started card make day 1 easier for a computer novice is a hypothesis; nobody has tried it.
Secondary text and labels dip below 4.5:1 over a dark window at the 92% start for six of the alternative looks (A to E and G); the default look does not. A supervisor view across several people's lists is not built.
Reading limits that remain by design: a word such as "monthly" in "submit the monthly report" is read as a repeat; a date after two tasks joined by "and" belongs only to the task it follows; "every year" is not a repeat the app knows (daily, weekdays, weekly and monthly only). Each is visible in the fields and can be changed before Add.
"Anything else?" was answered by a search for casual wording across the app, simulation, extension, tests and documents (exclamations, slang, household examples, junk test strings), by reading every visible line of the new windows, and by looking at renders of the real Windows windows. That is a search, not a guarantee: wording that no search term covers can still be missed.

## Round 17: a focused product (lookup, review a draft, Ask about a selection) and no Getting started card

After the owner's supervisor saw the product, the instructions were: keep the app focused for leadership; put **ANDA lookup** and **reviewing a draft** first and expand them; remove the Getting started section; lower the load on the person, taking inspiration from Apple's design and
user experience; offer ideas that would sell it; remember that most leadership do not keep their own Excel or ANDA portfolio; add a mode that runs the person's own or stored prompts on a selected passage or on selected spreadsheet cells (compare, find repeats),
with suggestions that fit what was selected; **suggest first and ask questions before building anything**; and do an adversarial review. The order was kept: a written proposal with an adversarial review and ten questions (each with a recommended default) came first.
The owner answered "go with all of your recommendations", and asked for a walk-through of something automatic, adaptive and proactive for the person's own data or portfolio file; that was **not built** (it needs the owner's answers; see the reply and `ROADMAP.md`).

What the adversarial review found before any code was written: the shortcut the owner expected for the new mode (Ctrl+Alt+M) is Word's *New comment*, and this app, while it runs, takes it first (see R108); leadership have no portfolio, so lookup and review must work on the fictional or sanctioned data alone;
"instantly sell" cannot be promised by software; the public ANDA data that would suit leadership (Drugs@FDA, Orange Book, PSG) could only be checked at headline level from here, and the field layouts were not checked, so nothing was built on them.

Local results on this code: C# **933 passed, 11 skipped, 0 failed** (eight Windows-only, two that save pictures for a person to look at, one differential check; each skipped with its reason); extension unit **31 passed**; simulation: suite 22, this round's, **94 checks**; the two parity scripts, **46 of 46 review cases** and **21 of 21 selection cases (147 field comparisons)** identical to the app's fixtures; every other simulation suite passes too, after the expectations that changed on purpose were updated (suites 2, 6, 8 and 20): the matrix of looks and sizes **1,090**, the in-frame demo **152**, the floating-window suite **254**, round 15's **144**, round 16's **133**, and the accessibility scans (rounds 14, 15 and 16, **16 scans each**, and this round's, suite 23, **18 scans**: the focused Settings, Ask in five states, the draft review and the focused tray menu, desktop and phone) with **no violations**. The review fixture (46 cases) and the selection fixture (21 cases) are written by the app and read by both; neither was changed to make the browser copy pass.

CI: commit **`b71b0f4`** (run 31): Linux and Windows jobs green. The Windows self-test is **63 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design), including `hotkeys-delivered` with all four shortcuts pressed as real keystrokes (Ctrl+Alt+A, Ctrl+Alt+Space, Ctrl+Alt+D, **Ctrl+Alt+Shift+P**), `render-ask-about-a-selection` and `ask-quick-check`. The first run of this round, run 30 (commit `8aabbba`), had 62 PASS and 1 FAIL, `ask-quick-check`, which was a real defect (R110); run 31 supersedes it.

| Claim | Evidence | Limit |
|---|---|---|
| **The product opens focused**: Lookup, Review a draft and Ask; the two to-do lists, agents, the appearance picker, dates and the transparency slider appear only after *Show all tools*; nothing is deleted and choices come back | `AskTests` (the five Settings groups, the default look and a solid card), `AdaptiveSettingsTests`, simulation suite 22 (A and B) | Whether it lowers a real person's load is a hypothesis. Nobody has timed it |
| **No Getting started card and no step list** | Suite 22 (A2, A3); the setting was removed from the saved file, the backup and the tests | A person who wanted the three steps has nothing in their place by design |
| **Review a draft does more, with no AI and no network**: the record check, then the draft against itself and the calendar (weekday and date, deadline on a weekend, holiday or in the past, two goal dates for one ANDA, placeholders, repeated words, ANDA numbers with the wrong number of digits); the result says what was and was not checked | `DraftReviewTests`, the 46-case fixture `mockup/review-golden.json` (written by the app, every output read by a person before it was accepted), the parity script | It compares only with the data this tool has. Spelling, grammar and meaning need an approved AI. A clean result is not a guarantee |
| **Ask about a selection** (`Ctrl+Alt+Shift+P`): says what was selected, suggests one button, never runs anything by itself; quick checks (describe, duplicates, blanks, totals, compare two columns) use fixed rules on the PC, with no AI and nothing sent | `SelectionHelpersTests`, `AskTests`, the 21-case fixture `mockup/selection-golden.json`, simulation suite 22 (C) | **Selection capture from real Excel was never tried**: clipboard hand-off, copy mode, protected or IRM-labeled workbooks, merged cells and very large ranges are untested. Cells are read from tab-separated text, to 20,000 characters, 1,000 rows and 30 columns |
| **A stored prompt or an instruction typed in Ask is filled in and shown first**; an *Ask my AI* prompt goes out only after Send on the preview and only to the connection in Settings; the stored prompt itself is never changed | `AskTests`, suite 22 (D) | Never run against a live model |
| **A table or list is not matched to one ANDA's record**, and a prompt that does not use `{selection}` gets the selection added at the end | `AskTests`, suite 22 (D) | A paragraph that names one known ANDA still fills that ANDA's details, as Select & Peek does |
| **The Ask shortcut is repaired, kept apart from the others and travels in a backup** | `AskTests` (repair, registration, load, backup), `HotkeyTests`, suite 22 (E); Windows run 31 (`hotkeys-registered`, `hotkeys-delivered`) | Delivered as an injected keystroke on a CI machine, not typed by a person; the clash with Word's own shortcuts (R108) is not tested here |
| **Hostile selections are text**: markup is shown and never run, a huge paste is cut, a table over 1,000 rows is read to 1,000 | Suite 22 (F) | |
| **Ask fits a phone** (390 px) and a tablet (820 px) without sideways scrolling | Suite 22 (F) | |

### Defects caught in round 17

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R100 | A review that found a note still began "nothing found", contradicting the note beneath it | Reading the fixture output | The line says "no differences, but please read the note(s) below" when there are notes |
| R101 | An impossible deadline date such as February 30 was skipped without a word | Reading the fixture output | It is reported as "not a real date" |
| R102 | February 29 written without a year was called "not a real date" | The same reading | Without a year only a day that no year has (February 30, April 31) is flagged |
| R103 | Totals added up a column of ANDA numbers as if they were amounts | A test sample | A column of numbers with five or more digits and no commas, decimals or signs is called a column of identifiers and is not totalled |
| R104 | "1 other entries were not a number" | Reading the output | The plural follows the count |
| R105 | **A stored prompt run on a selected table filled in the details of the first ANDA found in it and left the table out of the prompt** | Running Ask once in the browser | A table or list has no "record"; the selection is always added to the prompt; the window says that details of one ANDA stay empty |
| R106 | A UI test that failed inside the headless session without closing its window broke the session, and every later UI test failed with an unrelated font-resource error | The first full C# run after the focus change | The failing tests were fixed and now close their windows; the lesson is recorded in the tests' comments |
| R107 | Older tests expected the removed Getting started card, the step list, controls that are now hidden or Settings sections that open by themselves | The first full runs | C#: those tests turn *Show all tools* on. Simulation: `#all` in the address, and the helpers expand Settings. The expectations about the review text and the tray name were updated |
| R109 | **The Windows self-test would have failed on the new shortcut**: it presses every registered shortcut as Ctrl+Alt+key, so Ask (Ctrl+Alt+Shift+P) would have been reported as "never reached the app" | Reading the self-test against the new default, before the first Windows run | It now presses each shortcut's own modifiers (Ctrl, Alt, Shift) and releases them in reverse; a new check, `ask-quick-check`, renders Ask on a real renderer and runs Find duplicates |
| R110 | **The result of an Ask quick check vanished a quarter of a second after it appeared** on a real Windows machine: the text box announced a change for text that had not changed, and Ask read the selection again and cleared the result. A person who pressed *Find duplicates* quickly after the window opened would have lost it | **The Windows self-test, on its first run of the new window** (the check `ask-quick-check`, 62 of 63 passed); the headless tests never saw it because they do not wait | Ask re-reads only when the text really differs from what it last read. A test reproduces it (it fails without the fix) and the self-test now waits past the pause before looking |
| R108 | **The app's global shortcuts take Word's own.** Published lists give Ctrl+Alt+D (insert endnote), Ctrl+Alt+M (new comment) and Ctrl+Alt+L (list-number field) to Word; while this app runs it registers Ctrl+Alt+D (Review a draft) and Ctrl+Alt+L (My to-do list) first, so Word never sees them | The adversarial review, before building | **Not changed for the existing shortcuts**: moving them is the owner's decision. The new Ask shortcut avoids the problem (three modifiers). Recorded in `SECURITY.md`. The lists were not checked on a machine here |

### What round 17 does not prove

Not run on a real Windows desktop by a person; the Windows self-test (run 31) rendered the new windows and delivered the new shortcut as a keystroke, which is not the same as someone using it. Selection capture from Excel is the largest gap: it is a copy, a read and a restore, as for Select & Peek, but Excel has its own copy modes
(a marching-ants range, a protected sheet, an IRM-labeled workbook), and none were tried.
The expanded Review catches inconsistencies a person would see in a minute; it does not know what is true of an application. The quick checks report repeats, blanks, totals and differences between two columns; they do not decide what a repeat means.
Public sources for lookup (Drugs@FDA, Orange Book, Paragraph IV, PSG) were not read at field level; hosts were unreachable from here and the app's own Drugs@FDA reader is still marked unverified in its source. Nothing in the product depends on them.
Apple's guidance was used as principles (one primary action, answer first, fewer choices up front, no teaching screens), not as a look; there is one solid light look. Nobody has measured whether leadership finds it easier.

## Round 18: a calmer, more professional design and the Loupe icon, built in the simulation first

The instruction was to make the interface and the app more professional and polished, "similar to Apple design" if that suited, and to build it so a project manager would want to use it every day. It asked for **six unusual alternatives and six unusual icons first**, questions before any edit, and a proof that the answers were at least 98 percent certain. It also asked for an adversarial review, a rebuild and republish of everything, and a proofread. The order was kept. A private visual proposal came first (six interfaces, six icons, an adversarial review and eleven questions, each with a recommended default). The owner answered: **go with all of the recommendations, pick the Loupe icon, and add the new interfaces to the current options.**

What was agreed, and what was done in this round:

- The new default card is **T · Pocket Plain**, with **U · Calendar Ribbon** and **V · Glass Sheet** beside it; **One Bar** is an option for the lookup window and **Today** a small window under *Show all tools*. The 19 earlier looks are unchanged and stay under *Show all tools*. All looks are light only.
- **Spacing** (Comfortable or Compact) and **text size** (Standard, Larger, Largest) are in *Settings, How it starts*. **Recent ANDAs** (the last eight) and **Keep in my list** (up to five) are saved on the PC, with *Clear recents* and an off switch.
- The **Loupe** icon is in the program, the tray and the extension. The tray icon gets a small amber dot only for *urgent*, and only when the app reads data.
- US English and one set of terms throughout; `tools/wording-check.mjs` reads every window, message and document and lists breaches.
- **Order of work, as agreed: the simulation first, the Windows app only after the owner has looked at it.** So the Windows app has the new **icon** and US spellings in its messages, and nothing else from this round.

What the adversarial review found before any code was written, and how each was handled: the large figure could look more certain than it is (it always sits above the goal, the forecast and the forecast's confidence); the ribbon is a picture, so it has a written twin and drops out when the dates are far apart; One Bar could feel like a command line that guesses (every suggestion is a row you press, from fixed rules, with no model); Today is of little use to a leader who keeps no portfolio (it is therefore not in the focused edition); an amber dot that is nearly always on is ignored (it means *urgent*, by the card's own rule, not "needs a look"); glass can be hard to read (at least 86 percent opaque, and solid when the system asks for less transparency); saving recent ANDAs changes the earlier statement that the app keeps no history (only six-digit numbers, on this PC, clearable, switch-off, not in a backup; `SECURITY.md`); real Windows rendering can only be checked in the CI renders; and whether any of this helps in daily use is **unproven**, which is why `demo/TRIAL-SHEET.md` exists.

Local results on this code: C# **933 passed, 11 skipped, 0 failed**; extension unit **31 passed**, and the standalone browser run **all pass**; simulation: this round's suite 24, **101 checks**, and suite 25 (axe-core 4.10.2), **18 scans, no violations**; the older suites, with the expectations that this round changes updated (default look K to T, 19 to 22 looks, 15 to 18 cards in the lab): suites 2 to 22 all pass (for example 41, 13, 68, 26, 38, 15, 65, 56, 39, 24, 254, 12, 144, 133 and 94 checks), the combination matrix passes **1,090 of 1,090**, the in-frame check **152 of 152**, the axe scans of earlier rounds report no violations, and the two parity scripts still match the app's fixtures (**46 of 46 review cases; 21 selection cases, 0 field mismatches**); `npm run validate` and `npm run security:docs` pass; `node tools/wording-check.mjs` reports **0 findings in 132 files**.

CI: commit **`4958f13`** (run 32): Linux and Windows jobs green. The Windows self-test is **63 PASS, 0 WARN, 0 FAIL, 1 SKIP** (the live Claude connection, by design), including the tray-icon check with the new Loupe icon, `hotkeys-delivered` with all four shortcuts and the renders of every window. The self-test renders the Windows app, which still uses the earlier design apart from the icon, so it says nothing about the simulation's new card. The later commit that adds this entry changes only documents.

| Claim | Evidence | Limit |
|---|---|---|
| **The Loupe icon is used in the program, the tray and the extension, and reads on a light and a dark taskbar** | `Assets/loupe.svg` and `make-icons.mjs` generate `app.ico`, `tray-32.png`, `tray-32-alert.png` and the extension's four icons; suite 24 (A); a contact sheet of the icon on light and dark taskbar colors; the C# tests still pass with the new resources | **Not seen on a real Windows taskbar** at the person's display scaling; the Windows self-test checks that a tray icon exists, not how it looks |
| **A new default card leads with one answer (days later or earlier than the goal) and one main action, *Copy status*, pinned at the bottom; Enter copies** | Suite 24 (B), screenshots at 1366 and 390 px, suites 4 and 10 (default look T, 22 looks) | Whether a real person finds it faster is **not measured** (`demo/TRIAL-SHEET.md`) |
| **Calendar Ribbon draws the weeks around the goal and forecast, with weekends and federal holidays, a written twin for screen readers, and nothing when the dates are more than fourteen weeks apart** | Suite 24 (C), including a goal on December 28 and a forecast on January 8 of the next year (C13, C14) | Tested on the sample records and constructed dates, not on a portfolio |
| **Glass Sheet is at least 86 percent opaque and its label and body text keep their contrast (4.5:1 and 7:1) over a black window at that floor** | Suite 24 (D), computed in the browser; suite 25 (axe) | Contrast on a real, busy desktop wallpaper was not measured. The solid fallback for reduced transparency is a rule in the page; Windows' own setting was not exercised |
| **One Bar offers Review and Ask as rows from fixed rules; nothing opens by itself** | Suite 24 (G) | Rules read the selection as text; never tried against real copied Excel cells |
| **Today lists what needs a look (the card's own urgency and WATCH rules), then kept and recent ANDAs, and reads data only when opened** | Suite 24 (H) | With no portfolio there is little to show; not meaningful for leadership |
| **Recent ANDAs and Keep in my list store only six-digit numbers, can be cleared and switched off; kept ANDAs travel in a backup and recents do not** | Suite 24 (F), and (J) for backup, restore and a hand-edited backup file | **Designed in the simulation. The Windows app does not store them yet**, so `SECURITY.md` says so |
| **Spacing and text size change the card and the app's windows, not other programs** | Suite 24 (E) | Applies to this page only; the Windows app will need its own pass at 150 and 200 percent display scaling |
| **The tray dot means urgent and changes only when data is read** | Suite 24 (I) | The Windows tray was not drawn; there is no timer to test |
| **The wording follows one standard (US English, sentence case, no exclamation marks, one glossary)** | `tools/wording-check.mjs` (0 findings in 132 files) and `codespell` (only deliberate typos in test data) | The check reads words, not meaning; a person should still read the result |
| **The existing 19 looks, the focused edition and all earlier behavior are unchanged** | Suites 2 to 22 and the matrix (1,090 combinations), the frame check (152), C# 933 | Suites that assert the old default were updated on purpose; see R114 |

### Defects caught in round 18

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R111 | **The simulation threw at load**: replacing the Today markup left an unterminated HTML comment that swallowed the watch list's window, so a script looked up an element that did not exist | The first run of suite 24 (a page error at load) | The comment is closed; suite 24 now fails on any page error |
| R112 | On the new default card, a long brief pushed *Copy status*, the one main action, below the visible part of the card | Looking at a screenshot of the longest record | The action area is pinned to the bottom of the card |
| R113 | In the lab, the ribbon text came out at the browser's default size because a font shorthand with a variable was invalid | Looking at a lab screenshot | The three font properties are set separately; seen again in a lab screenshot |
| R114 | Older suites still expected the old default (K), 19 looks and 15 lab cards | The first full runs | Suites 4, 10 and 22 updated deliberately (the default look is T, 22 looks, 18 lab cards); no other expectation changed |
| R115 | **Stale statements about the product**: "nineteen looks" and "K is the default" in the lab and the design document, a Getting started card still described in a roadmap row and a code comment, and "check a draft" wording | The new wording check, then reading each hit | Rewritten for what is true now; the extension's own "Check a draft" label, which is a record-only check, is kept and marked as intended |
| R116 | **British spellings** in product text and documents (labelled, colour, centred, grey, recognise, cancelled), against a US-English audience | A search before the proofread | Changed in the app's messages, the extension, the simulation, the lab and the documents; the two tests that read these strings were updated |
| R117 | A bulk spelling change renamed a variable in `ProgressControls.cs` and the project would not build, once through a name clash and once through an over-eager replacement | The first C# build after the change | Fixed; the variable has its own name. Lesson: a spelling fix is not applied to code identifiers without a build |
| R118 | The document claimed the ribbon was tested across a year end when no such test existed | Re-reading the document against the suite | Two checks added (C13, C14); the document now says only what the suite shows |
| R119 | The first trial sheet compared the new design with the earlier one in a way that could not be done (recents and Today are not part of a look) | Re-reading the sheet | The sheet gives, for each task, the earlier way and the new way |

Not changed on purpose, and still the owner's decision: the **global shortcuts that Word also uses** (R108: Ctrl+Alt+D and Ctrl+Alt+L). The owner has not yet answered which "bottom alert statuses" were meant earlier, so nothing was built for them.

### What round 18 does not prove

Nobody has used the new design for a working day, and the trial sheet has not been run. The Windows app does not have the new card yet, so nothing in this round about the card, the looks, One Bar, Today, recents, spacing or text size is a claim about the app; it is a claim about the simulation and the lab. Real Windows rendering (fonts, display scaling, the taskbar, the translucent sheet) can only be judged from the CI renders and on a real PC. The Loupe icon was judged on a contact sheet, not on a taskbar. The wording check finds words that break the standard; it cannot tell whether a sentence is clear. Apple's guidance was used as principles; no Apple type, symbols or marks are used. The automatic and proactive dataset idea was a written walk-through only and is **not built**; it needs the owner's answers.

## Round 18b: a fresh-eyes break test before a live presentation to a director

The owner was to present on the day and a director would test the product live, on a Windows laptop, in Edge and Chrome, with no internet. The instruction was: save the working version first; have a fresh agent with no context from the session try to break it the way the director would; fix what it finds; make the user-facing text polite, precise and professional; ask before republishing anything live; and report what was tested, what broke, what was fixed and what is still untested.

**Saved first.** The working version was committed and pushed as `006e820` (Windows run 32 green), with frozen copies of the simulation and the lab kept as the known-good version. The live page (v16) was not touched by this round.

**The test.** The first attempt by the fresh agent failed before it tested anything (an API rate limit), so nothing is claimed from it. It was resumed later and ran for about 28 minutes, using 64 tool calls to run scripts against the simulation and the lab in Playwright Chromium with Windows-style Edge and Chrome user agents. It did not edit the repository. Its report listed **no P0 and five P1** defects, 19 P2 or P3 defects and 24 wording suggestions, and said what it could not test.

What it tested (its own account, partly checked by re-running its repros here): zero external requests, zero failed loads and zero console errors in direct mode across about 40 sessions; a hosted-page sandbox (an iframe without `allow-same-origin`, where storage and the clipboard are blocked); the clipboard granted, denied and missing; keyboard-only use of every window; seven viewport sizes with and without `#all`; axe-core on ten states (0 violations); Windows High Contrast, reduced motion and reduced transparency emulation; 270 window open and close cycles, 648 hostile text fills and a six-minute soak with no growth in memory or in the number of page elements; the lab at five sizes; every date and day count recomputed independently.

Local results on this code: C# **933 passed, 11 skipped, 0 failed**; extension unit **31 passed** and the standalone browser run **all pass**; simulation: the new suite 26, **49 checks** (one or more per finding above), and every earlier suite passes after its expectations were updated on purpose (41, 13, 68, 26, 38, 15, 65, 56, 39, 24, 254, 12, 144, 133, 94 and 101 checks), the combination matrix **1,090 of 1,090**, the in-frame check **152 of 152**, the review and selection parity scripts still identical to the app's regenerated fixtures (**46 of 46** and **21 cases, 0 field mismatches**), and all four accessibility scans with no violations; `node tools/wording-check.mjs` reports **0 findings in 132 files**. The page was also looked at again in screenshots at 1366 by 768 (cold page, default card, a look that shows day counts, the tray menu with all tools shown).

CI: commit **`892f16c`** (run 33): Linux and Windows jobs green, including the build, all tests, the extension check and the Windows self-test step (which fails the job on any failure; the report's counts were not read for this run). The two later commits change only the simulation's clock and this document, and did not start a run.

### Defects found and fixed in round 18b

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R120 | **The sample day counts were one too high** (80, 31 and "due in 6 days" against an extract date of Jul 16, when they are 79, 30 and 5); the Watch rule used the right arithmetic, so the page disagreed with itself. The same numbers were in the Windows app's sample records, the extension's copy and the shared golden file | The fresh agent (D1) | Corrected in all of them and in the tests; in the simulation the counts are now computed from the demo date |
| R121 | **"Pretend today is Oct 1" made the card contradict itself** (the card kept "80 days" while its WATCH line said "in 2 days"; the to-do list showed one overdue fact twice) | The fresh agent (D2) | Day counts and the attention line follow the pretend date; a fact is never listed twice in the to-do list, in the simulation and in the app (`Todo.cs`) |
| R122 | **"Copied" was shown when nothing was copied** (Quick replies, Ask, Backup, Outlook code) whenever the clipboard was refused, which is the normal state of a page hosted in a sandbox | The fresh agent (D3) | One helper, `writeClipboard`, falls back to the older copy command, and every "Copied" waits for its result; a refusal shows the text in a polite notice |
| R123 | **The shortcut boxes in Settings trapped the keyboard** (Tab, Shift+Tab, Escape and Enter were all swallowed) | The fresh agent (D4) | Tab and Shift+Tab move on and Escape leaves the box; a second Escape closes Settings |
| R124 | **With all tools shown, the tray menu ran off the top of a 1366 by 768 screen** and could not be scrolled | The fresh agent (D5) | It now stays on screen, scrolls, takes focus on opening and answers the arrow keys |
| R125 | The page logged two console errors per copy when a host blocks the clipboard by permissions policy | The fresh agent (D14) | The policy is checked before the clipboard is called |
| R126 | The translucent card let text from the windows behind it show through; Windows High Contrast kept it translucent and hid the selected state of the app toggles; reduced transparency was honored for one look only | The fresh agent (D6, D7) | The card blurs its background; reduced transparency makes every look solid; High Contrast uses system colors and a visible selected state |
| R127 | The whisper invite disappeared after 3 seconds, too short to click during a demonstration | The fresh agent (D8) | 6 seconds, in the simulation and in the app (`ChipWindow`) |
| R128 | The basic tray menu and Settings said the My to-do list shortcut was active when it does nothing there; the Lookup header ignored a rebound shortcut; the to-do footer showed the computer's clock against the frozen taskbar clock; Ask cut a long paste without saying so; adding `#all` to an open page did nothing; the four Reset buttons shared one name | The fresh agent (D9 to D13, D21) | Each corrected, with a check |
| R129 | **Wording**: the Review sentence "the record's forecast is +14 d vs goal" is now "14 days later than the goal" (simulation, app and extension, identical); "Check the number." is "Please check the number."; the unknown-ANDA notice, the lookup messages for NDA numbers and for digits with spaces, the banner, the Auto-connection hint, the basic-mode hint, the Reset message, the demo menu names, the relaunch notice, the lab heading and footer text and one grammar slip ("1 needs a look now") were rewritten | The fresh agent (section C) | Fixed; the two fixture files that the app writes were regenerated |
| R130 | Older suites expected the previous wording, counts and timing | The first full run | Updated on purpose; no other expectation changed |

### Reported by the fresh agent and not changed

- **At 1024 by 768 (a common projector size) the windows stack in one column** and the first screen shows only the Demo controls. A layout change this close to a presentation was judged riskier than the fault; use a wider screen or the browser's zoom out.
- The Demo controls panel and the Mail window are cut mid-line at 1366 by 768 (they scroll inside themselves); hint text is 11 px at standard size; the One Bar suggestion icons are plain text glyphs; a 600 by 300 window puts the bottom-sheet card partly off screen.
- The words "hotkey" and "shortcut" are both used; older suites assert some of them, so the rename was left for a later pass.
- Suspected only: a long press on a real Windows touch screen, which was exercised here only with synthesized touch. The Mail sample spreadsheet disagrees with the record on purpose, so that Review and Ask have something to find; a director comparing windows may ask.
- The short form "+14 d vs goal" is kept on the card and in the status line that is copied into email; it is compact and shared with the extension's tests.

### What round 18b does not prove

Real Edge and real Chrome on Windows were not available: Chromium on Linux with Windows user agents and viewports is the same rendering engine, not the same program. The real Segoe UI fonts were not installed, so text widths on the director's laptop are not known; the chip row on the card, the Copy status button and the Settings shortcut rows are the places to look. A real touch screen, a real projector or television, a screen reader and an operating-system High Contrast theme were not used. Whether another Windows program has taken a shortcut such as `Ctrl+Alt+Space` on that laptop cannot be predicted; the tray menu and the on-screen buttons always work. Nothing persists across a reload by design.

## Round 19: the owner's long list; the default card in the Windows app; details, notices and one question in the simulation

After the presentation fixes, the owner said "go with all of your recommendations" and sent a long list of ideas, asking to be walked through before anything was built and for brief questions. A written walk-through came first: what could be built from the person's own files, what needs access that does not exist, what was advised against and why, an honest statement of what the Review mode has and has not been tested for, and eight questions. The owner answered: republish; the figure "14" is far too big; **N before six digits means an NDA and A means an ANDA**; SFSV is the submission facility status view, to be connected later; the status hub shows text near the clock and is fed by **a Word document in SharePoint**; the name is "Notalus for now"; assume the sheet has basic applicant information, a project description and the RPM's notes; the proposed order of work is right; and "everything else, you decide".

What was done, in the agreed order:

1. **Republished** the round 18b fixes as version 17 of the live page, then this round's work as version 18.
2. **Quick fixes:** the answer figure is 30 px (was 46); "N210793" is refused as an NDA and "A210793" is read as ANDA 210793, in the app, the extension and the simulation; *Review a draft* and *My to-do list* default to `Ctrl+Alt+Shift+R` and `Ctrl+Alt+Shift+L`.
3. **Windows app:** the default card T · Pocket Plain, and the amber dot on the tray icon.
4. **Simulation (for the owner to review before any port):** card details with Copy and chosen columns; notices near the clock (watch-back) and division news from the Word document, with *News and alerts*; one question before sending.
5. **Two things the owner then asked for:** "what's happened to the … appearances and layout selections that I could pick from? They are not in the setting anymore" and "don't all capitalize all of the setting names in the drop down box/menu". Round 17 had hidden *Look and feel* behind *Show all tools*. It is now a section of both editions, in the simulation and in the app, with every look, the compact boxes and the transparency slider; the tray menu keeps *Appearance*; the choice applies at once. The **Go to** list in Settings shows its names in sentence case.

Not done, and said so: the rest of the port (text size, spacing, recent and kept ANDAs, the looks U and V, One Bar, Today); the port of this round's three simulation features to the app and the extension; **the rename**, because "Notalus" differs from the product's name "Notula" and a wrong spelling in 130 files is worse than one more question.

Local results on this code: C# **979 passed, 12 skipped, 0 failed**; extension unit **31 passed** and the standalone browser run all pass; the recognizer parity script, new this round, **47 cases, 0 mismatches** between the app's golden file and the simulation; simulation: this round's suite 27, **94 checks** (five accessibility scans among them, no violations), suite 28 for the looks and the Go to list, **44 checks**, and every earlier suite: the matrix **1,090**, the in-frame check **152**, four accessibility suites with no violations, the review parity **46 of 46**, the selection parity **21 cases, 0 mismatches**, and suites 2 to 26 (41, 13, 68, 26, 38, 15, 65, 56, 39, 24, 254, 12, 144, 133, 94, 101 and 49 checks), all passing; `node tools/wording-check.mjs` reports **0 findings in 133 files**.

CI: Windows run 36 **failed** on the first push of the new card (`card-placement-on-screen`, R133). Run 37 was cancelled by the next push. **Run 38** (`17f50bf`) passed after the fix. **Run 39** (`8e495e7`, with the looks back in Settings and the Go to list in sentence case) passed on Linux and, on Windows, built, passed every test including the Windows-only ones, published the executable and passed the app's own self-test.

| Claim | Evidence | Limit |
|---|---|---|
| **N is an NDA and A is an ANDA, everywhere** | `RecognizerTests` (new cases), the golden file regenerated by the app with nine new cases, extension unit tests, `r19_recognizer_parity.js` (47 of 47), suite 27 (A) | Only N and A were confirmed by the owner. Other single letters before six digits are read as before |
| **The default shortcuts no longer take Word's** | `HotkeyTests`, `SettingsTests`, suite 27 (B3 to B5); Windows run: `hotkeys-registered` and `hotkeys-delivered` name Ctrl+Alt+Shift+R | **Microsoft's shortcut lists could not be read from here** (support.microsoft.com is outside the allowed network), so the pair was chosen from memory. Not tried in Word or Outlook |
| **The Windows app opens with the default card T**: one answer above its two dates and confidence; Copy status below the scrolling part; Enter copies; a record with no forecast shows the plain goal row | `Round19Tests` (eleven tests), pictures of the card looked at, Windows renders `render-card-t`, `-expanded`, `-healthy`, `-peek` | The pictures were made by the headless renderer here and by CI on Windows; nobody has used the card on a Windows desktop |
| **The tray icon shows an amber dot while an ANDA is urgent, and its tooltip gives a count, never a number** | `Round19Tests` (the count equals the card's rule; the tooltip text) | The icon change itself is not observable in a headless test; the Windows self-test checks only that a tray icon exists |
| **Every look can be chosen without *Show all tools*, and the choice applies**: Look and feel is one of six sections of the focused Settings; each look in turn changes the page; transparency applies; switching *Show all tools* on and off keeps both; the tray menu offers *Appearance* | Suite 28 (L1 to L19); `AskTests`, `AdaptiveSettingsTests`, `Round19Tests` (the Settings window offers all twenty looks in the focused edition) | In the app the tray menu's contents are not observable in a headless test. Whether twenty and more looks are too many is not known |
| **The Go to list has no name in capitals**; abbreviations and names keep theirs (ANDA, API, PC, Drugs@FDA, Alert Hub, Select & Peek); choosing an entry still goes there; the labels on the page are unchanged | Suite 28 (J1 to J13, both editions, one accessibility scan); `Round19Tests` (sixteen labels and the real list of the Settings window) | The list of names kept in capitals is a fixed list; a new label with a new abbreviation would need adding |
| **Card details show columns of the person's sheet, each with Copy; a missing column says so; nothing is looked up** | Suite 27 (C1 to C16): order, short name, missing values, exact clipboard contents, chosen columns, backup and restore, a hand-edited backup | **Simulation only.** The real header row was assumed, not seen. The fictional sheet stands in for a file |
| **Watch-back is off until switched on; one notice at a time; a daily limit; never the keyboard; never notes** | Suite 27 (D1 to D20) | **Simulation only.** The app's triggers (start, file change, each morning) are described, not built. Whether three a day is right is a guess |
| **Division news is plain text from one document, newest first, last 14 days, cut to 240 characters, counted in the limit; it cannot be switched on without a document** | Suite 27 (E1 to E12, H1, H2, H5) | **Simulation only.** No Word document was read: the paragraphs are fictional. The synced-file route is a design decision that IT has not seen |
| **Review ends with one question, or "No open gaps."** | Suite 27 (F1 to F12); the review and selection parity scripts still match the app's fixtures | **Simulation only.** It cannot see recipients or attachments and says so. It is not advice on whether to reply |
| **Markup, formulas and floods are harmless** | Suite 27 (H1 to H6): markup shown as text and never run, a formula copied as text, 200 news lines giving at most five notices, a refused copy reported | The clipboard was refused by a stub, as in round 18b |
| **Everything earlier still works** | Every earlier suite, the matrix, the frame check, C# 979 | Expectations that this round changes on purpose were updated (shortcut names, the key the tests press, six sections in the focused Settings, the names in the Go to list) |

### Defects caught in round 19

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R131 | **A number written with an N opened an ANDA card** when an ANDA with the same six digits existed ("N210793" was read as 210793). The recognizer refused "NDA 210793" but not the agency's short form | The owner's answer that N means NDA, then reading the three recognizers | Refused in all three; nine cases added to the shared golden file |
| R132 | **In the Windows app a look's own seal colors never took effect**: a resource binding in the markup won over the colors set in code, so K, P to S and the stamp seals all showed the default green pill | Looking at the first picture of the new card | The seal is colored in code; a test reads the result |
| R133 | **The new card was taller than a 1024 by 768 screen** (753 px in a 720 px work area) and did not scroll: the allowance for the pinned Copy status area was a fixed number and too small | **The Windows self-test, run 36** (`card-placement-on-screen`) | The height limit now belongs to the card as a whole; a test squeezes the card and checks that the facts scroll and the action stays in view |
| R134 | A blanket replacement of the old shortcut text changed a comment into a false statement ("Ctrl+Alt+Shift+R is Word's Insert endnote") and four tests that used Ctrl+Alt+D as an example | Reading the diff; the first C# run | The comment states the facts; the tests were set to what they mean |
| R135 | Expanding a card showed nothing new until the person scrolled: the details were below the fold | Looking at a screenshot | Expanding brings the details into view |
| R136 | A test pressed the old Review shortcut under a second spelling and failed | The first run of the keyboard suite | The test presses the new shortcut |
| R138 | **The looks could not be chosen**: round 17 hid *Look and feel* behind *Show all tools* and forced the default look, so a person who opened Settings found no looks at all | **The owner**, looking for them | *Look and feel* is in both editions; the chosen look and transparency apply in both; tests that asserted the old rule were changed on purpose |
| R139 | **The Go to list was a column of capitals**: it copied the small capital labels of the page as they are stored | **The owner** | Sentence case in the list, with a fixed list of abbreviations and names; the page labels are unchanged |
| R140 | The description in the extension's `package.json` gave a test command that fails on current Node (`node --test tests/unit`) | Running it | It names the files, as the workflow does |
| R137 | The tray dot was refreshed before the data source was applied in Settings, so it could show the count of the previous file | Reading the code after writing it | The refresh follows the data change |

### What round 19 does not prove

No RPM has used any of it. The three simulation features are proposals to look at, not parts of the Windows app. The Word document for division news does not exist, and reading it from a synced folder has not been tried; neither has any connection to SharePoint, Elsa or CDER One Analytics. The real portfolio's columns were assumed. The new shortcuts were not tried in Word, Outlook or Excel. With the looks back in the focused edition the card is drawn at the 92% the owner chose in round 16, where round 17 drew it solid: it was looked at in the simulation over the Mail window, not on a Windows desktop. A notice near the clock can be seen by others in a meeting; the design limits what it shows and how often, and cannot make it private. "Things should not take more than two minutes to act on" is the owner's rule and was used to shape the features; no timing was measured.

## Round 20: glass by default at 90%, quick notes on the card, a proofreader that works with no AI, and a desktop that looks like Windows 11

The owner answered the open question ("Leave Notula alone") and asked, for a director's test the same day: the default appearance "should use the glass design"; transparency "from 40 percent to 100 percent but the default should be 90 percent"; "make sure all of the features and sub features/functions of the proofread mode/tool will work flawlessly … you can actually proofread and also factcheck and also cross references/leveraging the user's ANDA portfolio or the user's personal notes"; show "the user's most recent and relevant notes +/- relevant to-do list items at the bottom row and as auto collapsed"; add a way to "create personal notes or quick notes of the particular ANDA" on the hover card; and make the simulation "actually look like windows 11 at the FDA … to make my director feels at home".

What was done:

1. **Glass by default at 90%.** V · Glass Sheet is the default look in the simulation and the lab, listed first. The sheet follows the transparency setting (40 to 100%, starting at 90% for every look). The Windows app's start is 90% too. **The app's default look is still T · Pocket Plain**: glass there needs a blur behind a rounded card that has to be seen on a Windows desktop first.
2. **Quick notes on the card (simulation).** *Notes* or **N**: add, edit, delete with Undo, copy. Kept by the app; the sheet and the notes folder stay read-only.
3. **Proofread and Fact-check with no AI (simulation).** The two buttons, which were switched off until a model is approved, now work by fixed rules; a corrected draft can be copied or applied in the tool's own window. Fact-check adds strength, review cycle and the due date of the open item from the sheet, and traces a date to the note it stands in.
4. **Notes and open items for recall (simulation).** One closed row per ANDA at the bottom of the Review window.
5. **The Windows 11 look (simulation).** Wallpaper (three choices), taskbar, windows, and Mail, Chat, browser and terminal in the manner of the real programs, **drawn from shapes with no logo, image file or agency artwork**.

Not done, and said so: none of items 2 to 4 is in the Windows app or the extension; V is not in the app; no real FDA desktop was seen, so "at the FDA" is Windows 11 as it ships, in blues, and nothing that claims to be the agency's.

Local results on this code: C# **979 passed, 12 skipped, 0 failed** (the contrast checks now run at 90%); extension unit **31 passed** and the standalone browser run all pass; simulation: this round's suite 29, **122 checks** (seven accessibility scans among them, no violations), and every earlier suite: the matrix **1,090**, the in-frame check **152**, four accessibility suites with no violations, the review parity **46 of 46**, the selection parity **21 cases, 0 mismatches**, the recognizer parity **47 cases, 0 mismatches**, and suites 2 to 28 (41, 13, 68, 26, 38, 15, 65, 56, 39, 24, 254, 12, 144, 133, 94, 101, 49, 94 and 44 checks), all passing; `node tools/wording-check.mjs` reports **0 findings in 133 files**.

CI: **Run 40** (`df57d89`, this round) passed on Linux and, on Windows, built, passed every test including the Windows-only ones, published the executable and passed the app's own self-test. Only the 90% start touches the app in this round; the rest is in the simulation, which the workflow does not run.

| Claim | Evidence | Limit |
|---|---|---|
| **A new page opens in V · Glass Sheet at 90%; the slider runs 40 to 100 and governs the glass; every other look starts at 90% too** | Suite 29 (G1 to G11); suites 4, 18, 20, 22, 24 and 28 updated to the new default and start; `Round15Tests`, `Round16Tests`, `SettingsTests` for the app's 0.90 | The app's default look is unchanged (T). Glass was looked at over the three drawn wallpapers, not on a Windows desktop |
| **Nothing readable shows through the action area of the glass card** | Suite 29 (G10); pictures before and after | Blur is a browser feature here; where it is missing the area is solid |
| **Quick notes: add, edit, delete with Undo, copy; text only; 500 characters and 50 per ANDA; unsaved text survives closing; never in a notice or the backup; the card's letter shortcuts do not fire while typing** | Suite 29 (N1 to N30, with one accessibility scan and a refused clipboard) | **Simulation only**, and notes last until the page is reloaded. Where a real notes file should live is not decided |
| **Proofread works with no AI**: five drafts corrected exactly as expected, seven clean drafts raise nothing, brackets and date styles are counted, a 120,000 character draft is handled, markup in a draft is text | Suite 29 (P1 to P16) | **Simulation only.** About 120 words and a dozen rules: it is not a spelling or grammar checker and says so. The word list was written here, not taken from RPM mail |
| **Fact-check reads strength, cycle and due date from the sheet, traces dates to notes, and does not guess** (two ANDAs in one sentence, a sister product in the next sentence, spreadsheet rows, unknown numbers) | Suite 29 (F1 to F11) | **Simulation only.** The three extra facts were chosen here. A fact the sheet does not hold cannot be checked |
| **The shared review is unchanged** | Suite 29 (P16); the review parity script, 46 of 46; the selection parity script | The new layer has no shared cases with the app yet, because the app does not have it |
| **Recall: one closed row per ANDA of the draft, three at most; notes the draft touches first; open items from the record and both to-do lists; text only** | Suite 29 (R1 to R12, two accessibility scans, and the real path: select the draft in Mail, press the shortcut) | **Simulation only.** "Relevant" means a shared date or word of four letters or more |
| **The desktop looks like Windows 11 and uses no logo or image file; every added piece is hidden from screen readers; selecting text and every shortcut work as before** | Suite 29 (W1 to W14: three backgrounds each scanned, five screen sizes); every earlier suite; pictures at 1920, 1366, 1024 and 390 px looked at | Likeness is a judgment made here from memory of the real programs. Real Segoe fonts were not installed |
| **Everything earlier still works** | Every earlier suite, the matrix, the frame check | Expectations changed on purpose: the default look, the 90% start, the names of the Review buttons, the closing line of the fact check |

### Defects caught in round 20

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R141 | **Proofread and Fact-check did nothing without a model**: the buttons were greyed out, so the "proofreader" could not proofread | **The owner** | Both work by fixed rules; the AI versions stay beside them, off |
| R142 | The *Notes* button on the card answered "This is not connected in the prototype" | Reading the code for the owner's request | It opens the notes, with the box to add one |
| R143 | **On the glass card, facts scrolling under the Copy status area showed through the buttons** | Looking at the first picture | The area blurs what is under it; solid where blur is missing |
| R144 | **A false alarm in the new strength check**: in the demo email the sentence after "ANDA 214990 - the 750 mg strength" speaks of "the 500 mg application", and it was read as a wrong strength for 214990 | Suite 29 (F4), written from the demo email | A strength is compared only in the sentence that names the ANDA |
| R145 | The list of ANDAs named in a draft missed a number that stood close after another | Suite 29 (R8) | The recognizer's own list is used |
| R146 | **The wallpaper made the desktop a layer of its own, so an open card covered the tray menu and, on a phone, the dimmer covered Settings** | Three earlier suites (3, 8 and 9), which timed out clicking | The desktop is not a layer; the drawing sits behind by order |
| R147 | The search boxes drawn in the Mail and Chat title bars failed the contrast check | The accessibility scans of suite 29 | Solid boxes with dark text |
| R148 | Window titles were cut off ("Inbo…") at 1366 and 1280 px | Looking at the picture; suite 29 (W6, W14) | The search box gives way when the bar is narrow |
| R149 | Two desktop icons were drawn on top of each other | Looking at the picture | Placed one under the other; a check compares their boxes |
| R150 | **An older fault: pressing R on a small peek card opened the quick replies menu in the top-left corner of the screen** | A picture taken by a script that typed into a peek by mistake | The menu opens at the card |
| R151 | Pressing N on a peek did nothing | The same picture | It opens the full card with the notes |
| R152 | The line that says what was not checked stood between the facts | Looking at the picture | It comes after every fact |
| R153 | The rule for a space before a mark missed "216482 .It" | The first trial of the rules | The mark moves back and the space goes after it |

### What round 20 does not prove

No RPM and no director has used any of it. The glass card, the notes and the proofreader exist only in the browser simulation; the Windows app has the 90% start and nothing else from this round. The proofreading list will miss most grammar and some spelling, and it has not been run on real mail, so its false alarms on real text are not known. The agency's desktops were not seen: the look is Windows 11 as it ships, and no agency artwork is used, on purpose. Real Edge and Chrome on Windows, a projector, a touch screen and a screen reader were not used, as before.

## Round 21: Enter adds a note, a live clock, a proofreader that reads grammar, a pitch deck, and a review of the whole

The owner asked, the same day as round 20: a desert photograph as the Windows background; the *Copy status* bar "thinner/shorter and more transparent", with Enter going "straight to adding a new notes" and the recent notes shown below; a small button to "the specific ANDA webpage … customizable as a new settings"; "a tad bit of professional shadow"; "always use today's date and time … current to the second and using EST time as default or based on current regional time clock/timezone"; "your proofreader and fact checking still not working? Why can you just be seamlessly integrating to any local default AI like Apple Intelligence or Windows built in AI?"; "a professional and polished pitch deck"; and "Rigorously review this entire application … Find the 5 biggest useability design or code problems. Fix the important ones without changing the main concept."

What was done (all in the browser simulation unless said otherwise):

1. **The card's actions.** *Add a note* is the main action (Enter), slim and tinted; *Copy status* is a small button and the S key; *Open ANDA page* goes to an `https` address the person sets.
2. **A live clock.** The real time to the second and the real date, in the computer's time zone; the sample dates move forward by whole weeks so they read as current.
3. **The desktop.** A drawn desert under a blue sky is the default background; layered shadows.
4. **The proofreader.** The reported fault was reproduced first: in a sandboxed frame, as the hosted page runs, the rules worked but their results were below the fold, and a sentence with ordinary grammar mistakes went unremarked. Results now come first, the draft is shown with its changes marked, and about thirty grammar rules were added.
5. **An AI built into the PC.** Used only where the browser already has an on-device model, on a click, with the answer marked as unchecked.
6. **The review.** Five problems named in `DESIGN.md` §11e; four fixed, one (the app trails the simulation) recorded with a first step: the rules as 32 shared cases in `mockup/proofread-golden.json`.
7. **A pitch deck** of 13 slides, as a separate artifact. Its figures that nobody has measured are left as bracketed blanks.

Not done, and said so: nothing of this round is in the Windows app or the extension; the owner's photograph is not used (the site could not be reached from the build environment); no real on-device model was available to test with; Apple Intelligence cannot be used at all on Windows.

Local results on this code: C# **979 passed, 12 skipped, 0 failed** (unchanged: the app was not touched); extension unit **31 passed**; simulation: this round's suite 30, **97 checks** (accessibility scans among them, no violations), and every earlier suite, after the updates listed under R163 (one check of suite 29, that the recall row is the last thing in the Review window, had not been brought up to date with the folded AI options that now follow it; it was corrected in round 22); `node tools/wording-check.mjs` reports **0 findings**.

CI: the Windows app was not changed in this round, so no new Windows run was started. The last one before it, run 40 on `df57d89` (round 20), was green.

| Claim | Evidence | Limit |
|---|---|---|
| **Enter adds a note; S and the small button copy the status; the main action is 34 px high and tinted; every action is on screen** | Suite 30 (A1 to A16, in four looks, with an accessibility scan); suites 24 and 29 updated on purpose | **Simulation only.** In the Windows app Enter still copies the status |
| **Open ANDA page accepts only a plain `https` address with `{anda}`, puts only a six-digit number into it, and says so when there is none** | Suite 30 (U1 to U10: ten addresses, a hand-edited backup, a pinned card) | **Simulation only**: the look-alike browser shows where it would go; no page is opened. The address shown at first is an example |
| **The clock is the real one, to the second, in the region's time zone, and the sample data follows it**: goal still a Saturday, 73 to 79 days out; the open item due in 2 to 8 days; the mail, the draft, the notes, the news and the review tell one story | Suite 30 (C1 to C12: Eastern, Pacific, Berlin and Tokyo; across a midnight, a new year and a leap day, with the browser's clock set) | Checked with a set clock, not by waiting. The rule "whole weeks" means the sample Thursday can be up to six days ago |
| **With `?clock=fixed` nothing moved**: every earlier suite passes unchanged against Jul 16, 2026 | Every earlier suite | The suites no longer exercise the live clock except suite 30 |
| **The desert is drawn in the page; four backgrounds; no image file** | Suite 30 (D1 to D3, with a scan of the whole desktop); suite 29 (W) | It is a drawing in the manner of the photograph, not the photograph |
| **Grammar rules correct five bad drafts exactly and stay silent on eight good ones** (there/their/they're, its/it's, your/you're, then/than, to/too, could of, verb agreement, me and him, missing apostrophes, a/an, capitals of days and months) | Suite 30 (G1 to G3); `mockup/proofread-golden.json` (32 cases) and its parity script | About thirty rules with their context words. They will miss most grammar that needs meaning, and they have not been run on real mail |
| **The draft is shown with each change marked, as text; Apply here makes every change; a date that does not exist is named** | Suite 30 (G4 to G8) | The marked view shows the first 4,000 characters |
| **Results are in view when Review opens at 1366 by 768; the AI options are last and folded; an empty window says what to do** | Suite 30 (L1 to L6, with a scan) | Not checked on a real laptop screen with real fonts |
| **The on-device AI is used only when its model is already installed; never downloaded; shown first, run on a click, answer unchecked and Copy only; a failure is reported as text** | Suite 30 (I1 to I9, against stand-ins for an available, a failing and a refusing model) | **No real model answered.** In the build browser the feature exists and reports its model as not installed, which is the state the page refuses to change |
| **Quick notes survive a reload where the browser allows a store, say so where it does not, and a tampered store is read safely; Reset clears them** | Suite 30 (S1 to S5) | A hosted page may be refused a store; then notes last until a reload, as before |
| **What the card scrolls to stops above the action area; the Lookup box's Enter acts once** | Suite 30 (V1 to V3, A15); suites 6 and 8, which found both | |

### Defects caught in round 21

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R154 | **Review's results were below the fold**: under the text box came the AI buttons and two paragraphs about them, so on a 768 px screen the window looked as if nothing had happened | **The owner** ("still not working"), then a reproduction in a sandboxed frame | Results come right under the buttons, with a summary line; the AI options are folded at the bottom |
| R155 | **The proofreader missed ordinary grammar**: "Their going … me and him will … ANDA 216482 are … there goal date" raised only two spelling items | The same reproduction | About thirty grammar rules, each with its context; the draft with changes marked |
| R156 | **Enter in the Lookup box was handled twice**: it opened the card and then acted on the card too. For rounds it silently copied the status line over whatever was on the clipboard; with this round's change it would have jumped into the notes | Suite 8, which could no longer reach a control behind an unexpectedly expanded card | A key typed into a box belongs to that box; a check holds the clipboard and the card's state |
| R157 | What the card scrolled to could sit under the action area that stays at the bottom | Suite 6 (a click that could not land) | The card's scrolling leaves room for the action area |
| R158 | Quick notes were lost on a reload | The review | Kept in the browser's store where allowed; the box says which; Reset clears them |
| R159 | A date that does not exist ("Jan 37") passed the fact check | A mistake in the script that made a picture for the deck | Dates that do not exist are named with the reason |
| R160 | After *Open ANDA page* the card stayed in front of the page it had opened | This round's own suite (a click that could not land) | The card steps aside unless pinned |
| R161 | An AI button's explanation replaced the review's results | Reading the code while moving it | It appears with the AI options |
| R162 | The first version of the on-device AI code treated a model that is not installed as ready, which would have started a download of several gigabytes on a click | Asking the build browser what it really reports | Only an installed model counts; the page says so otherwise |
| R163 | Older suites expected the previous main action, button labels and default background | The full run | Updated on purpose |

### What round 21 does not prove

No RPM, director or investor has used any of it. The Windows app has none of this round and still lacks rounds 18 to 20 beyond the default card, the tray dot and the 90% start: **what a person would install is not what the simulation shows**. The proofreader is a list of rules: it has no sense of meaning, and its false alarms on real mail are unknown. No on-device model was available, so that path is proven only against stand-ins. The live clock was checked by setting the browser's clock. The desert is a drawing. The deck's market, price, pilot size and funding figures are blanks for the owner; none was researched. Whether a federal employee may take this to a private investor is a question for the agency's ethics and technology-transfer offices, and is not answered here.

## Round 22: a quieter default look, a thinner taskbar, and a tray icon that speaks only when something needs the person today

The owner asked, after seeing round 21: "make the windows bottom taskbar a bit thinner/shorter"; "for the default appearance can you create a new one that would replace the current default. This new default will blend the best features of graphite Guaze while still keeping the Enter button and other smaller buttons and features at the bottom"; "for the current tray icon design, don't have it to look like there's currently a new alert … this should only be triggered when there's an actual scenarios such as a specific ANDA is due today, potential customer service issues, immediate risks or safety issues, an email that was deemed either urgent or important and it has not been responded in 2 days"; and, again, "Find the 5 biggest useability design or code problems. Fix the important ones without changing the main concept."

"graphite Guaze" was read as the two existing looks **K · Graphite Quiet** and **P · Bronze Gauge**. That reading is an assumption; the previous default is one click away.

What was done:

1. **W · Graphite Gauge, the new default** (simulation and Appearance Lab): the glass sheet of V in the graphite palette of K, with the gauge of P beside the answer, and the same action area at the bottom.
2. **The taskbar** of the look-alike desktop is 40 px high (it was 48).
3. **The tray icon** carries no amber at rest, and the dot means *something needs you today* (simulation; the picture and the first two reasons also in the Windows app). The tooltip and the tray menu count the reasons; *News and alerts* lists them; each opens its card.
4. **The review.** Five problems named in `DESIGN.md` §11f; four fixed, one stated as a limit with an open decision.

Not done, and said so: W is not in the Windows app, which still opens in T · Pocket Plain; the app's dot has no "mark in the sheet" and no mail reason; the app recounts the dot only when it reads data, so not at midnight; the pitch deck's three pictures were retaken with the new look, and its text was not changed.

Local results on this code: C# **980 passed, 12 skipped, 0 failed**; extension unit **31 passed**, and its end-to-end test passes; simulation: this round's suite 31, **85 checks** (accessibility scans among them, no violations), and every earlier suite, after the updates listed under R173 (33 suite files in all, none with a failure, each run against the final page); `node tools/wording-check.mjs` reports **0 findings in 133 files**.

CI: run 41 on `963a441` is green on Windows (build, every test including the Windows-only ones, the extension's rules against the app's output, the single-file program, the app's own self-test, the demo bundle) and on Linux. The commit that follows it changes only the simulation (the notice that says where the dot is) and documents, which the Windows workflow does not build.

| Claim | Evidence | Limit |
|---|---|---|
| **The page opens in W · Graphite Gauge**: graphite colors, a gauge beside the answer that follows the record (62%, 88%) and is read out in words, the same six actions pinned at the bottom, Enter still adds a note | Suite 31 (W1 to W9, W13); suites 4, 22, 24, 28 and 29 updated on purpose | **Simulation and Lab only.** "Graphite Gauge" is a reading of the owner's words |
| **Nothing collides**: on the small card, with the largest text and widest spacing, and at 1920, 1366, 1280, 1024 and 390 px wide; the sheet follows the transparency setting from 40% to 100% | Suite 31 (W4, W12, W14, W16, W23) | Not seen on a real laptop screen with real fonts; the blur behind a rounded card has not been seen on a real Windows desktop |
| **The other 22 looks did not change** | Before the stylesheet change and after it, the card was rendered in eight looks, full and small, plus Settings: 17 pictures, each identical byte for byte. Suite 31 (W17 to W21); the older suites | Eight of 23 looks were compared as pictures; the rest are covered by the older suites' checks, not by pictures |
| **The taskbar is 40 px**, everything in it fits, every button is at least 24 px high, the tray menu and a card opened low stay above it, the live clock fits | Suite 31 (T1 to T7, with a scan) | |
| **At rest the tray icon has no amber**, in the simulation and in the app's picture | Suite 31 (A2); C# `The_tray_picture_carries_amber_only_when_something_needs_the_person`, which reads the pixels of both pictures | The larger app icon keeps its amber center |
| **The dot means "needs you today"**: due today or overdue yes, due tomorrow no; goal today or passed yes, goal tomorrow no; a late forecast or an item due in five days no | Suite 31 (A1, A3, A4, A12 to A18); C# `The_tray_dot_is_only_for_what_needs_the_person_today_and_its_tooltip_never_names_an_ANDA` | In the app, "due today" is read from the words of the action item, because the sheet has no due-date column the app reads |
| **A mark in the sheet lights it and is shown on the card**; a mark that carries markup is shown as plain text, runs nothing and is cut to 120 characters; a malformed mark is ignored | Suite 31 (A19 to A25, with a scan) | **Simulation only**: the app reads no such column |
| **An urgent message with no reply lights it after two days**, only while Outlook is connected and signed in; one day is too soon; an answered message never counts | Suite 31 (A26 to A30, A34) | **Simulation only, with a made-up message.** The app reads mail for the open ANDA on request, never in the background, so it cannot know this today |
| **The dot can be explained**: the tooltip and the spoken label count and never name an ANDA; the tray menu counts; *News and alerts* lists each reason, follows changes while open, opens the card, and says so when there is nothing | Suite 31 (A4 to A11, A31 to A33, with a scan) | |
| **The tooltip keeps the connection and the count together** | Suite 31 (A35 to A37) | |
| **The dot is recounted when the date changes** | Suite 31 (A39, A40: the browser's clock is run across midnight) | **Simulation only.** In the app the dot changes only when the app reads data |
| **A what-if is never a switch with no visible effect**: with the icon behind the ^ arrow, a notice says where the dot is; behind the arrow the icon shows it | Suite 31 (A41 to A43) | On Windows an icon behind the arrow shows nothing on the taskbar either; the person pins it once |
| **The Appearance Lab shows W first, as the only default, with today's actions; 23 designs** | Suite 31 (R2 to R4, with a scan); suites 10 and 24 | |

### Defects caught in round 22

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R164 | **The tray icon at rest carried the alert color**: an amber center in the lens, the same amber as the dot | **The owner** ("don't have it to look like there's currently a new alert"), then a close-up picture of the tray | No amber at rest, in the simulation and in the app's picture; a test reads the pixels |
| R165 | **The dot was on all the time**: it followed "urgent", which the sample data always meets, and in the app nothing ever cleared it | The owner; reading `TrayState` | The dot means "needs you today"; the sample records never raise it in the app |
| R166 | Nothing said why the dot was on | The review | Tooltip count, tray menu count, *Needs you today* in News and alerts, the mark on the card |
| R167 | **Two writers for one tooltip**: the connection text and the alert count each replaced the other on the tray icon | Reading the code for the test of R166 | One function writes both |
| R168 | A rule about "today" was not recounted when the date changed | The review | Recounted at the date change in the simulation; a stated limit and an open decision in the app |
| R169 | The stylesheet named every look in every rule of the answer-first family (29 selectors to edit for W) | Building W | Two attributes set in one function; 17 pictures identical before and after |
| R170 | A mark from the sheet would have been put into the watch list as markup | Writing the attack case | Escaped; a test tries markup |
| R171 | Text scrolling under the pinned action area showed through the glass | A picture of the new look | The action area is 97% solid |
| R172 | The Appearance Lab and five documents named a default and a main action that no longer existed | The review | Corrected |
| R174 | Switching on *An item is due today* showed nothing: the tray icon sits behind the ^ arrow until it is pinned | Preparing the walk-through for the owner | A notice says where the dot is; the icon behind the arrow shows the dot too |
| R173 | Older suites expected V as the default, 22 looks, the old tray rule and the old taskbar height | The full run | Updated on purpose |

### What round 22 does not prove

No RPM or director has used it. "Graphite Gauge" is a reading of two words; the owner may have meant something else. The Windows app has the tray rule and picture only: **it still opens in T · Pocket Plain and has none of rounds 20 to 22 beyond that and the 90% start.** The app's "due today" depends on the words in the sheet's action item. Nothing in the app yet reads a risk, safety or service mark, and nothing may read mail in the background without a decision by the owner and IT. The tray picture was checked as pixels, not seen in a real Windows notification area, where icons are drawn at 16 to 24 px.

## Round 23: eight looks in both programs, a number on the tray icon, a choice of when it is counted, and three proposals for the default

The owner answered the four questions of round 22: (1) W is what was meant, "but I prefer the visual/line graph from appearance U's 'GOAL AND FORECAST ON THE CALENDAR' … add either a brief visual or clear auto adaptive text colors and styles to indicate 'days until' the actual GDUFA Goal Date? Show me 3 new appearances first"; (2) the app may count at midnight, "can the user has at least two choices in settings for this too? And can you replace the dot to number 2 or 3 if there are more than 1 alert?"; (3) the large icon should lose its amber center too; (4) port to the app, and "cut down to only top 8 appearances for both app and simulation". Also: "for all the presentations and pitch decks, ensure the tones and writing must always be professional brief, polite, and precise".

What was done:

1. **Three proposals, shown and not switched on** (Appearance Lab, with pictures sent to the owner): X · Graphite Calendar, Y · Graphite Runway, Z · Graphite Countdown. Each is W with U's calendar and a "days to the goal date" indicator that changes at 30 days, at 7 days, on the day and after it, in words as well as in color.
2. **A number on the tray icon** (simulation and app): a dot for one thing, the number for two to nine, "9+" beyond.
3. **A choice of when the icon is counted again** (simulation and app): only when the app is used; also once a day just after midnight (default); also every hour.
4. **The Loupe without amber at every size** (app icon, extension icons, simulation).
5. **Eight looks in both programs**: W (default), V, U, T, K, P, L, O. **W, V and U are new to the Windows app**, which now opens in W.
6. **The pitch deck's wording** was tightened and its figures brought up to date.

Not done, and said so: none of X, Y, Z is the default yet (the owner chooses); which eight looks to keep was the builder's proposal, acted on; the app does not draw the blur of the glass looks; notes, Enter-adds-a-note, Open ANDA page, the proofreading rules, card details, notices, One Bar and Today are still simulation only.

Local results on this code: C# **1,032 passed, 13 skipped, 0 failed** (1,036 with the picture test switched on); extension unit **31 passed**; simulation: this round's suite 32, **58 checks**, and every earlier suite after the updates listed under R181 (**34 suite files in all, none with a failure, each run against the final page**); `node tools/wording-check.mjs` reports **0 findings in 135 files**.

CI: run 42 on `2a9a7f9` is green on Windows (build, every test including the Windows-only ones, the extension's rules against the app's output, the single-file program, the app's own self-test, which now also draws W, V and U, and the demo bundle) and on Linux.

| Claim | Evidence | Limit |
|---|---|---|
| **Three proposals keep U's calendar and the same actions, and say the days to the goal date at five levels** (quiet, amber, amber on a tint, solid amber on the day, solid red after it), each in words | Suite 32 (L1 to L8, with an accessibility scan, contrast included, at every level) | **Lab only: a drawing with fixed dates.** The thresholds (30 and 7 days) are a proposal. None is in the simulation or the app |
| **Eight looks are offered, the same and in the same order in both programs**; a retired or unknown look changes nothing; a backup or a saved setting that names one falls back to the default | Suite 32 (E1 to E8); C# `The_app_offers_the_same_eight_looks_in_the_same_order_as_the_simulation`, which reads the simulation's list, and `A_saved_look_that_is_no_longer_offered_falls_back…` (all 23) | Which eight is a proposal. The retired looks' styles are still in the page, unused |
| **W, V and U in the app**: W shows the half dial beside the answer and says it in words; U draws the weeks from a Monday, names a holiday, and writes the same sentence as the simulation; no dial or calendar without the data | C# `Round23Tests` (the dial, the calendar, the window of weeks, the three looks); pictures of the app's cards were looked at; the Windows self-test draws them | **The blur is not drawn in the app.** Seen as headless pictures, not on a real Windows desktop |
| **A dot for one thing, the number for two to nine, 9+ beyond**, on both copies of the simulation's icon; the tooltip and the spoken label give the true count | Suite 32 (N1 to N13); C# `The_tray_picture_follows_the_count`, `Every_tray_picture_exists_and_a_number_is_larger_than_the_dot` (reads the pixels) | The number is about 7 px high at tray size. **Not seen in a real Windows notification area** |
| **Three choices for when the icon is counted again**, the same words in both programs; a bad value falls back to once a day; at midnight the simulation counts for two of the three and not for "only when I use the app"; "every hour" picks up a change at the next hour | Suite 32 (C1 to C10, the browser's clock run across midnight and across an hour); C# `The_scheduled_count_is_never_once_after_midnight_or_every_hour` (including a day when US clocks change and a new year), `The_count_setting_has_three_choices…`, `Settings_offers_the_three_choices…` | **The app's timer itself was not run across a real midnight**: its arithmetic is tested, and the timer is one line that calls it |
| **No copy of the Loupe carries amber at rest** | Suite 32 (N14); C# `The_Loupe_carries_no_amber_at_any_size` | |

### Defects caught in round 23

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R175 | In proposal Y the words "Today" and "Goal" ran into each other when the goal date was close | A picture of the 5-day state | "Today" moves above the line when less than a fifth of the clock is left |
| R176 | In proposal X the answer broke onto two lines beside a wide count | The same picture | The count is a fixed 96 px, the width of W's dial |
| R177 | The dates under the calendar had their descenders cut (the picture was 2 px too short), in the Lab and in the simulation | A picture of the proposals | The picture is 80 units high in both |
| R178 | The app's self-test saved and reloaded a retired look, which would have reported a failure on Windows | Reading the self-test after the cut | It uses an offered look; its default check names W |
| R179 | The first pictures of the app's new looks were taken during the card's entrance animation and came out dark | Looking at them | The picture test waits for the animation |
| R180 | A test loop moved to the new looks expected the classic card's rows on answer-first cards | The full run | The loop covers the classic looks; suite 24 covers the others |
| R181 | Older suites and C# tests expected 23 looks, the old default in the app, the old Lab layout and retired looks by name | The full run | Updated on purpose; checks of retired looks removed |

### What round 23 does not prove

No RPM or director has used it. The choice among X, Y and Z, and the thresholds, are the owner's. The eight looks kept are a reading of "top 8". The app's W, V and U and its numbered tray pictures were seen only as pictures made without a screen: **nobody has seen them on a real Windows desktop**, and the app's glass has no blur. The scheduled count is the app's first timed read of the portfolio file; it was approved by the owner, not by IT. The app still trails the simulation on notes, the proofreader and the rest of rounds 18 to 22.

## Round 24: X is the default in both programs; notes, Enter adds a note and Open ANDA page come to the Windows app

The owner, on the three proposals and the eight looks of round 23: "I prefer x. Yes to the second question. Proceed with all". That was read as: make X the default in both programs in W's place (as proposed), and go on with the port, starting with notes (as recommended). The rest of the port was not reached in this round.

What was done:

1. **X · Graphite Calendar is the default** in the simulation, the Appearance Lab and the Windows app, and takes W's place in the eight.
2. **One rule for the days to the goal date**, with 20 shared cases (`mockup/countdown-golden.json`) that both programs answer alike.
3. **Notes in the Windows app**: Enter, N or the button opens a new note; Ctrl+Enter saves; change, delete with Undo, copy; kept in `quick-notes.json` in the app's own folder.
4. **Copy status is a small button and the S key** in the app, as in the simulation since round 21.
5. **Open ANDA page works in the app**: an `https` address set in Settings, handed to the default browser.

Not done, and said so: the proofreader and its recall row, card details with Copy, notices near the clock and division news, the one question before sending, text size and spacing, recent and kept ANDAs, One Bar and Today are still simulation only; the app still draws no blur behind the glass looks.

Local results on this code: C# **1,086 passed, 15 skipped, 0 failed** (1,092 with the picture tests switched on); extension unit **31 passed**; simulation: this round's suite 33, **53 checks**, the new parity script (20 cases, no mismatch), and every earlier suite after the updates listed under R188 (**36 suite files in all; 35 finished with no failure. Correction made in round 25: suite 31 stopped before its last four checks, waiting for a tile this round folded away; all 55 checks it reached had passed, and with the wait corrected it passes 59 of 59. See R195**); `node tools/wording-check.mjs` reports **0 findings in 137 files**.

CI: run 44 on `20f6b22` is green on Windows (build, every test including the Windows-only ones, the extension's rules against the app's output, the single-file program, the app's own self-test, which draws the card in X, and the demo bundle) and on Linux. Run 43, on the commit before it (`a322fb5`), had passed its build and tests on Windows when the next push cancelled it during the self-test; run 44 contains everything run 43 did.

| Claim | Evidence | Limit |
|---|---|---|
| **Both programs open in X**; W is defined and not offered; a setting or backup that names W opens in X | Suite 33 (X1, X2, X19); C# `X_is_the_default_takes_Ws_place_in_the_eight_and_W_is_still_defined`; the offered-looks parity test of round 23, which reads the simulation's list | |
| **X shows the answer first, the days to the goal date beside it, and the calendar under it**; nothing collides, on the small card, with the largest text, and from 390 to 1920 px wide | Suite 33 (X3 to X10, X16 to X18, X21, with accessibility scans); C# `The_default_card_shows_the_days_to_the_goal_date_beside_the_answer_and_the_calendar_under_it`; pictures of both programs' cards were looked at | The app's card was seen as pictures made without a screen, not on a Windows desktop |
| **The count changes at 30 days, at 7 days, on the day and after it, in words, ground and weight, not in color alone**, and both programs agree | Suite 33 (X11 to X15, each level scanned for contrast); `mockup/countdown-golden.json` with its parity script and C# `The_days_to_goal_rule_answers_the_shared_cases_exactly_as_the_simulation_does`; C# `Every_level_keeps_its_text_readable_on_its_ground` (measured contrast) | The thresholds were proposed by the builder and accepted with the look, not tested with users |
| **In the app, Enter goes to a new note and Ctrl+Enter saves it**; the button counts project notes and the person's own; on the small card the full card opens first; every full look behaves the same | C# `Round24NotesTests` (Enter, the small card, three other looks) | **Keys were raised by the test, not typed on a real keyboard** |
| **A key typed in the note box belongs to the box**: S does not copy, E does not fold, a plain Enter is a new line, Escape leaves the box without closing the card | C# `A_key_typed_in_the_note_box_belongs_to_the_box` | |
| **A half-typed note keeps the card open when another window is clicked** | C# `A_half_typed_note_keeps_the_card_open_when_another_window_is_clicked` | The test reads the rule; no real window was clicked |
| **A note can be changed, deleted, put back and copied; one ANDA holds 50; a note is cleaned as the simulation cleans it** | C# `Round24NotesTests` (the store: add, list, change, delete, restore, the limit, the cleaning) | The cleaning rule is tested by the same examples in words, not by a shared file |
| **Notes survive a restart; a damaged file is set aside and never overwritten; a file edited by hand is read safely; a folder that cannot be written is reported** | C# `Notes_survive_a_restart…`, `A_file_edited_by_hand_is_read_safely`, `A_folder_that_cannot_be_written…` | The file is not encrypted |
| **Open ANDA page opens exactly one https address with six digits in it**; `http`, `javascript:`, `file:`, a space, a quote, a user name and password, a missing `{anda}` and an address over 300 characters are refused with the simulation's words; a browser that cannot start is reported | C# `Round24NotesTests` (eleven addresses, seven bad numbers, the card, Settings) | **No real browser was started**: the tests replace the hand-off. The address is the person's own setting |
| **A long holiday name is not cut on the calendar** | Suite 33 (X22, with the real clock set to Nov 12, 2026) | Two long names in one window could still touch each other |

### Defects caught in round 24

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R182 | **"Birthday of Martin Luther King, Jr." was cut at the left edge of the calendar** | A picture of X with today's real dates | A name near an edge is anchored to that edge |
| R183 | **In the app, Ctrl+Enter in the note box added a line and saved nothing**: the box handles Enter before the card sees it | The first run of the new tests | The box's two keys (Ctrl+Enter, Escape) are caught on the way down |
| R184 | **The app's "Open in system of record" button did nothing at all** | Reading the card's code for the port | It is now Open ANDA page, and it works or says why not |
| R185 | A source comment said the app writes "exactly two JSON files"; it writes several | Adding the notes file | Corrected: it writes only inside its one folder |
| R186 | After a note was added from outside the card, the button's count was stale until the next paint | A picture | The count is painted whenever the notes open |
| R189 | **A click into the email would have closed the card and thrown away a half-typed note** | Reading the new code as a person would use it | The card stays open while the box holds unsaved text |
| R187 | A test waited across midnight with the card set to hide after ten seconds, and read a hidden card | The first run of suite 33 | The test keeps the card open |
| R188 | Older suites and C# tests expected W as the default, Copy status as the main action and Enter to copy | The full runs | Updated on purpose; suite 31's section on W as the default was removed, and suite 33 covers the default |

### What round 24 does not prove

No RPM or director has used it. **The app now writes the person's own notes to a file**, by the owner's direction; whether that folder is the right place, and whether such notes fall under records rules, has not been asked of IT. Nobody has typed a note, pressed Enter or opened a page in the app on a real Windows desktop: the tests raise the keys and replace the browser. The app has no blur behind X. The proofreader, card details, notices, One Bar and Today are still only in the simulation.

## Round 25: Review works with no AI in the Windows app (Proofread, the extra fact checks, recall, one question), tested twice

The owner, with a guide attached on protecting a personal software prototype: "Proceed with all. Anything else? Test and validate everything twice before finalizing anything. Are you extremely sure?; next, see the attached and let me know your thought? Walk me through." That was read as: go on with the port in the order recommended (the proofreader first), run every suite twice on the final code before anything is committed or published, and give an assessment of the guide in the reply. The guide is the owner's document and is not in this repository; nothing in it was acted on as an instruction.

What was done:

1. **Proofread in the Windows app** (`Services/Proofreader.cs`): the simulation's rules, one for one.
2. **The extra fact checks, the recall row and the one question before sending** (`Services/DraftExtras.cs`).
3. **The Review window** shows them: three views (Check everything, Proofread, Fact-check), the draft with each change marked, *Copy corrected draft*, *Apply here*, the question, and the person's notes and open items, folded. The AI actions moved into a folded *AI options* section and are unchanged.
4. **A larger shared file** (`mockup/proofread-stress-golden.json`): 372 awkward drafts and 8 with a quick note, answered by the simulation; the app must reproduce every answer, and now also the earlier review's findings and the question for each draft.
5. **One fix in the simulation**, found by the port ("cycle 0").

Not done, and said so: card details with Copy, notices near the clock and division news, text size and spacing, recent and kept ANDAs, One Bar and Today are still simulation only, as is the on-device AI option in Review; the app still draws no blur; the extension has none of this round.

**Every suite was run twice on the final code**; the app was rebuilt from nothing between its two runs, and the simulation suites ran one after another the first time and in three groups side by side the second. Results, identical in both runs: C# **1,104 passed, 15 skipped, 0 failed**; extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **37 suite files, none with a failure**, among them the three parity scripts for this work (32 cases, 372 + 8 cases, 20 cases; no mismatch); `node tools/wording-check.mjs` **0 findings in 139 files**; `npm run validate` and `npm run security:docs` pass.

CI: **no Windows run had started for the round 25 commit (`ec2479b`) twelve minutes after the push**, although the commit changes files the workflow watches, and a manual start was refused to this session (403). The last Windows run is still run 44, on the round 24 code. Until a run on this code is read, "the same rules on Windows" in the table below rests on the local runs only. A second commit under the app's folder was pushed to give the trigger another chance; what happened is in the reply to the owner for this round.

| Claim | Evidence | Limit |
|---|---|---|
| **The app's Proofread gives the simulation's answer**: the same items, in the same order and words, the same corrected text and the same number of changes | C# `The_app_gives_the_simulations_answer_for_every_shared_case` (32 drafts) and `…for_several_hundred_awkward_drafts` (372: a seeded mix of the shared drafts, dropped and swapped letters, doubled marks, tabs, line ends, no-break and thin spaces, curly quotes, a Kelvin sign, a long s, a byte-order mark and a NUL). A control was run: with one expected answer altered, both tests fail and name the draft | **A short list of fixed rules, not a spelling or grammar checker.** It will miss most errors and can be wrong about a correct sentence; it says so in the window. Same answers proves the port, not the rules |
| **The extra fact checks give the simulation's answer**: strength, review cycle, the due date of the open item, a date that does not exist | The same two files | In the app the due date is read from the record's first action item that carries a date. **A portfolio file has no action items, so with a person's own sheet this check is silent.** The cycle is read only where the record says "Cycle N" |
| **The earlier review and the one question also match on the awkward drafts** | The stress file holds the review findings and the question for each of the 372 drafts; C# compares both. C# `One_question_is_chosen_in_a_fixed_order` (nine drafts, one per rule) | The question sees only the selected text: not recipients, not attachments |
| **A date that also stands in the person's own note is pointed out, and recall puts the note the draft touches first** | Eight drafts with a quick note in the stress file; C# `A_date_that_also_stands_in_the_persons_own_note…` builds the note through the app's own notes file and compares the fact lines and the recall order | The to-do items in recall are tested in C# only (`Recall_lists_open_items…`): the simulation's file has none |
| **The corrected draft is the draft with exactly the marked changes**; marks are in order and never overlap; a third pass never changes anything more | C# `The_corrected_draft_is_the_draft_with_exactly_the_marked_changes` (all 372), `Proofreading_twice_changes_nothing_more…` | A second pass can still find something the first uncovered; that is the simulation's behavior too |
| **Nothing is typed into an email**: *Copy corrected draft* leaves the box as it was and says "copied" only once the clipboard has taken it; *Apply here* changes only the app's own box | C# `Apply_here_changes_only_the_apps_own_box_and_Copy_says_what_it_did` | **The Windows clipboard was not used**: the test runs without a screen |
| **The three views show their own part and say which is selected**; a repeated word is not reported twice in *Check everything*; the question is hidden in *Proofread* | C# `Proofread_and_Fact_check_each_show_their_own_part…` | Buttons were raised by the test, not clicked with a mouse |
| **An empty box says so; a clean draft reads "No open gaps."; the sample draft is offered only with the sample records** | C# `An_empty_box_says_so_and_a_clean_draft_has_no_open_gaps` | |
| **Recall opens the card that was asked for**, and names the button for a screen reader | C# `Recall_shows_the_persons_own_note_first_and_opens_the_card_that_was_asked_for` | No screen reader was run |
| **A very long draft is answered quickly, and markup in a draft is only ever text** | C# `An_empty_draft_gives_nothing_and_a_very_long_one_is_answered_quickly` (about 260,000 characters, all three checks, under one second here), `A_slip_in_a_name…and_markup_is_only_ever_text` | Each pattern has a two-second limit; a draft that reached it would get a shorter list, not an error |
| **The same rules on Windows** | The tests above run on the Windows CI machine; the app's own self-test there now proofreads a sentence and counts three fact differences | CI is a server, not a person's desktop |
| **The window was looked at** | Four pictures made by the test run (all views, a messy draft, recall open) | Made without a screen; **not seen on a Windows desktop** |

### Defects caught in round 25

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R190 | **In the simulation, "cycle 0" in a draft was reported as "the draft says cycle undefined"** | Writing the same rule in C#, where the shortcut that caused it does not exist | Both programs say "cycle 0"; the stress file holds the case |
| R191 | The app's new *Copy corrected draft* would have said "copied" whether or not Windows took the text (the fault fixed in the simulation in the break test) | Reading the new code against the list of past defects | It waits for the clipboard and says so if copying was not allowed |
| R192 | In the Review window, long lines ran under the scroll bar | A picture | A wider margin beside the scroll bar |
| R193 | The pitch deck still said 1,085 tests and that the proofreader was only in the simulation | Checking every document against the change | Three slides corrected |
| R195 | **The round 24 record said all 36 simulation suite files passed. One did not finish**: suite 31 stopped before its last four checks (the Appearance Lab), waiting to see a tile that round 24 had folded away. Its exit code said so and was not read | **Running everything twice and reading every exit code**, as the owner asked | The wait was corrected; the suite passes 59 of 59 in both runs, so the product was right and the report was not. The round 24 record is corrected below, and the runner's exit codes are now checked by a command, not by eye |
| R194 | While writing the C# file, escaped characters (a no-break space, line separators) were turned into the characters themselves, and the build stopped | The compiler | Restored as escapes; `A_pattern_is_read_as_the_simulation_reads_it` now holds the reading of each such character |

### What round 25 does not prove

No RPM or director has used it. **Nobody has run the new window on a real Windows desktop**: not the clipboard, not the scroll bar, not a real email selection. The rules are few and fixed; agreement with the simulation says the two programs are the same, not that either is right about English. With a person's own sheet the due-date check is silent and the cycle check depends on the wording of one line. The recall row reads the notes file whose place and records status have not been asked of IT. **The repository this work is pushed to is public, and the simulation's link opens for anyone who has it**; whether that suits the owner's plans is the owner's decision, raised in the reply to this round.

## Round 26: Compare two files, four reading prompts, an invitation and a note from a selection, My usage; a security review; tested twice

The owner passed on a request from leadership (choose two Excel files and have them compared and summarized, with clear pictures) and a list of notes, and asked for a walk-through and questions first. After the answers ("usually two versions or reports of the same datasets"; "build for both"; "do both"; "all extra items") the owner added: recommend new features that would make this a daily tool, use the repository's Product Design Studio, DevOps & Cloud and API Platform Builder skill sets where they help, "then perform a thorough AAS Security Engineer & AAS Secure App Builder."

What was done:

1. **Compare two files**, in the simulation and in the Windows app (`Services/FileCompare.cs`, `UI/CompareWindow`): fixed rules on the PC, a summary in plain sentences, two simple charts, the changed cells, Copy and Save, and a narrative through the person's AI after the usual preview.
2. **In Ask about a selection:** four reading prompts; *Save a calendar invitation* when the selection names a date; *Add to the notes of ANDA …* when it names an ANDA in the person's data.
3. **My usage:** the counts of the optional usage log for the last 7 and 30 days, in a window, to copy if the person chooses.
4. **A shared file** (`mockup/compare-golden.json`, written by `tools/compare-golden.cjs`): 20 comparisons, 14 invitations, 3 usage cases, the prompts and the event names, answered by the simulation; the app must reproduce every answer.
5. **A security review** with the checklists of the two skill sets the owner named, and six fixes from it (see the table of defects).

Which skill sets were used, and for what: **Security Engineer** (`security-auditor`, `vulnerability-scanner`, `top-web-vulnerabilities`, `sast-configuration`) and **Secure App Builder** (`cc-skill-security-review`, `security-and-hardening`, `frontend-security-coder`, `secrets-management`) supplied the review checklist: secrets, input and file limits, output encoding, sensitive data, dependencies, static analysis, and the section on AI features (treat model output as untrusted, fence what is sent, keep secrets out of prompts, bound what is consumed). **Product Design Studio** (`ui-a11y`, `ui-review`) supplied the design checks: contrast of text and of graphics, nothing said by color alone, the size of controls, labels, reduced motion, no sideways scrolling on a phone. Measuring against them found two faults (R208, R209). **API Platform Builder** (`api-security-best-practices`: validate type, size and content of a file; allow lists, not block lists; bound what a caller can ask for) was read against the file reader and the narrative path. The narrative adds no new address and no new request shape, and the reader already held those limits, so nothing there had to change. **DevOps & Cloud** (`github-actions-templates`) was read against the Windows workflow. It already pinned each action to a commit, ran with read-only permission, kept no credentials and had time limits; two steps from the skill's security-scanning section were added to its Linux job (the package advisory check and a build with every .NET security rule on, either of which fails the run), and the shared answer files were added to the paths that start it. Both steps were run here exactly as written. **The workflow itself has not run since**; see the CI paragraph.

Not done, and said so: the extension has none of this round; the simulation reads CSV only (the app reads `.xlsx` too); the earlier list of things that are only in the simulation is unchanged.

**Every suite was run twice on the final code**; the app was rebuilt from nothing between its two runs. Results, identical in both runs: C# **1,132 passed, 15 skipped, 0 failed** (1,138 with the picture tests switched on; 28 of the tests are new in this round); extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **38 suite files, none with a failure** (1,729 checks counted by the runner in each pass), among them the new suite for this round (77 checks) and the parity scripts; `tools/compare-golden.cjs` **20 comparisons, 14 invitations, 3 usage cases, 0 mismatches**; `tools/proofread-stress.cjs` **372 cases, 0 mismatches**; `node tools/wording-check.mjs` **0 findings in 144 files**; `npm run validate` and `npm run security:docs` pass. Every exit code was read. Earlier runs are not counted, because the code changed after them: two full runs of the simulation suites and one of the app passed while the review's fixes were still going in, and a third simulation run was under way when the wording after a save was changed, so the one suite that reads that wording failed in it (37 of 38), for that reason.

CI: **no Windows run started for this round's commits (`8d4042e1`, `aeb2364a`) in more than an hour after the push**, although they change the app, the demo notes and the workflow file itself. The same happened for the two round 25 commits, and a manual start was refused to this session then (403). The last Windows run is still run 44, on the round 24 code, from Nov 12. Until a run on this code is read, "the same rules on Windows", the self-test additions, the system-folder setting for native calls and the two new workflow steps are claims about code that has run on Linux only. The owner can start the workflow from the repository's Actions tab; why pushes stopped starting it is not known here.

| Claim | Evidence | Limit |
|---|---|---|
| **The app compares two tables exactly as the simulation does**: the same key column and reason, the same counts, the same changed cells, new and removed rows, summary, report, pasted report and AI digest | C# `Comparing_two_files_gives_the_simulations_answer_for_every_shared_case` (20 pairs: the samples, a chosen key, a key that is not there, repeated and blank keys, numbers and dates written differently, no shared column, empty files, short rows, repeated headings, markup, 130 rows, 70 removed rows, 16 columns, leap days, values like formulas, very long values) | Agreement says the two programs are the same, not that the rules suit every report. **No real report was used** |
| **Numbers and dates are compared as what they are** | In the shared file: "$1,000" and "1000.00", "50%" and "50.0%", "2026-10-03" and "10/3/2026" are equal; "2/30/2026" is not a date and is compared as text; simulation C11 | Dates are read month first; a date with a two-digit year, or day first, is compared as text. Only the forms listed in DESIGN §11j are read |
| **A workbook and a CSV can be compared, and the window names the sheet that was read** | C# `A_workbook_is_compared_with_a_CSV_and_the_window_names_the_sheet_that_was_read` (a date cell in Excel against a written date: 14 days later; a hidden sheet is passed over) | One sheet per workbook: the first visible sheet with an ANDA column, otherwise the first visible sheet. **Made with the test's own small workbook, not with Excel** |
| **The simulation never claims that a file was saved** | Simulation S11 to S13 (in a normal tab the file arrives and the sentence says a download was tried; inside a frame that forbids downloads no file arrives and the sentence is the same) | In the published link *Save a report* and *Save a calendar invitation* therefore save nothing; Copy works there. **Not tried in the published link itself** |
| **What a file contains is only ever text** | Simulation C22, C23 (an image tag with a script in a cell builds nothing and runs nothing, and is escaped in the saved page); C# `Copy_save_and_the_narrative_hand_over_only_what_they_say`, `The_saved_report_can_load_and_run_nothing` | The saved page was read as text by the tests, not opened in a browser by a person |
| **A copied report cannot become a spreadsheet formula** | C# `A_copied_report_never_starts_a_line_with_a_formula` (five kinds of first character), `An_ordinary_report_is_copied_unchanged`; simulation S1 to S4; the pasted form of all 20 shared pairs is in the shared file | **Not pasted into Excel.** The apostrophe is the usual guard and is shown by some programs as a character |
| **A selection cannot add a line of its own to a calendar file** | C# `A_selection_cannot_add_a_line_of_its_own_to_a_calendar_file` and simulation S7: text that tries to add an attendee, an organizer and an alarm, using every kind of line break and several control characters; every line of the file is one the program wrote; two such texts are in the shared file | **The file was not opened in Outlook** or any calendar |
| **An invitation is made exactly as the simulation makes it** | C# `A_calendar_invitation_is_made_exactly_as_the_simulation_makes_it` (14 texts: a date with and without a year, a date that does not exist, "3/4" and "10/12" that are not dates, a leap day, yesterday's date, a long first line, a backslash and an emoji) | An all-day event on the first real date in the text; a time of day is not read |
| **A very long value is shown cut and still compared whole** | C# `A_very_long_value_is_shown_cut_but_still_compared_whole`; simulation S6; one shared pair | |
| **A large file does not freeze the window** | C# `Large_files_are_read_and_compared_off_the_UI_thread…` (3,000 rows through the real buttons; the key column changed afterwards) and `The_largest_table_the_reader_accepts_is_compared_in_good_time` (20,000 rows by 30 columns: about 2 seconds here) | Timed on this server. **A window was never watched while a real 25 MB workbook was read** |
| **The narrative is given the summary and the listed changes, never the files, and only after the preview** | Simulation C19, C20 (the preview holds the prompt and a changed row and not a row that did not change; nothing is sent); C# `Copy_save_and_the_narrative…` (at most 1,900 characters) | **No model was called.** What a model writes was not judged |
| **Four reading prompts go through the preview** | Simulation A10 to A12; C# `Four_reading_prompts_are_offered_and_each_goes_through_the_preview` | No model was called |
| **A note is added only for an ANDA that the selection names and the data holds, and a cut note says so** | Simulation A1, A3, A7 to A9; C# `A_selection_with_a_date_offers_an_invitation…`, `A_note_that_cannot_be_added_says_why…` | |
| **My usage shows counts and nothing else can come out of it** | C# `The_usage_log_reader_takes_only_dates_and_known_event_names` (lines with an ANDA number, a command and markup are skipped), `The_usage_summary_counts_the_last_7_and_30_days…`, `The_usage_window_shows_the_summary…`; `A_pair_of_files_is_counted_once_however_often_it_is_looked_at`; simulation U1 to U8 (off until switched on; while off nothing is counted; only the fixed event names can be counted) | It counts uses, **not minutes saved**. Nothing is reported for the person, by design |
| **Native calls go only to three Windows libraries, loaded from the system folder** | C# `Native_calls_go_only_to_three_Windows_libraries…`; a build with every .NET security rule switched on raises none (it raised one rule, 51 times, before) | **This setting has not yet run on Windows**; the Windows workflow would show it |
| **No secret, no vulnerable package** | A search for key and token patterns finds only the placeholder strings in tests; `dotnet list package --vulnerable --include-transitive` and `--deprecated` report none | Checked against the public advisory list on the day of the review |
| **The new windows pass the accessibility scan, say nothing by color alone, and fit a phone** | Simulation C3, C12, U3, A2 (scans), C27 (the scan in five looks), C6 and C7 (each part of the bar is named with its number and the bar has a label), C28 (phone width) | The scan is automated; **no screen reader was run** and no keyboard-only walk of these windows was done |
| **Every part of a chart stands at least 3 to 1 against the window, and every control is at least 24 pixels tall** | Simulation S9 (six looks) and S10; C# `Every_part_of_a_chart_stands_at_least_3_to_1_against_the_window_in_every_look` (all eight looks) | Buttons are 34 pixels tall, above the 24 that WCAG 2.2 AA asks and below the 44 the design skill recommends for touch; the product is used with a mouse and keyboard |
| **The same rules on Windows** | The app's self-test now compares the two sample files, checks a pasted line and makes an invitation | CI is a server, not a person's desktop; see the CI paragraph |

### Defects caught in round 26

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R196 | A bare "3/4" in a selection was read as March 4 and offered as an invitation | Writing the shared cases | A date written with numbers needs a year; "3/4" and "10/12" are shared cases |
| R197 | Two files with no column heading in common were reported as "the same rows and the same values"; two empty files likewise | Writing the shared cases | Each is said in its own sentence |
| R198 | **The simulation did not escape a semicolon in a calendar file** | The app's parity test, the first time it ran | Fixed in the simulation; the shared file was written again |
| R199 | One comparison was counted twice in My usage, in both programs, when the key column was changed | A test of the counts | Counted once for a pair of files |
| R200 | **A copied report could begin a line with a cell value or a file name. Pasted into a spreadsheet, a line that starts with `=`, `+`, `-` or `@` is a formula** | The security review (input that becomes output in another program) | Such a line gets an apostrophe in front when copied; the screen and the saved page show the value as it is |
| R201 | The rarer line breaks (U+2028, U+2029, U+0085) and other control characters in a selection reached the calendar file unescaped | The security review | They become plain line breaks or are removed, in both programs; tested with text that tries to add an attendee and an alarm |
| R202 | A cell of any length was put on the screen and into the report whole | The security review (bound what is consumed) | Values and row names are shown cut at 200 characters and headings at 80, never through the middle of a character; matching and comparing use the whole value |
| R203 | **The app read and compared files on the UI thread: 20,000 rows by 30 columns took about 2 seconds, during which the window would not answer** | Measuring it | Reading, and comparing more than 2,000 rows, moved off the UI thread, with "Reading the file…" and "Comparing…" |
| R204 | The saved report relied on escaping alone | The security review (defense in depth) | It also carries a content policy that lets it load and run nothing |
| R205 | 51 native calls did not say where their library is loaded from (.NET rule CA5392) | A build with every security rule switched on | One assembly-wide setting: the Windows system folder only; a test lists the three libraries |
| R206 | For a workbook with several sheets, the window did not say which sheet it had compared | Checking the documents against the code | The sheet is named beside the file name |
| R208 | **The "Same" part of the row bar stood about 2 to 1 against the window in every look**, below the 3 to 1 that graphics need. In the app it was drawn at 45% strength | Measuring the charts against the design skill's contrast rule | Drawn at full strength in both programs; a check in each program holds 3 to 1 for every part in every look |
| R212 | **That fix made two neighbors look alike**: at full strength "Same" and "Changed" were nearly the same gray in the default look | Looking at the picture of the app's window | "Same" is an outline and the three kinds of change are filled, in both programs, so the parts differ by more than color; the checks hold the outline too |
| R209 | The two file choosers in the simulation were 20 pixels tall | Measuring every control in the window | 36 pixels, with a button in the page's own style |
| R210 | The app's summary ended in a line of 11.5-point text, and its closing note said a workbook's "first sheet" is read, which is not the rule | The design skill's smallest text size; reading the picture | 12; the note says one sheet is read and named beside the file name |
| R211 | **The record did not hold the fix of R199 in the app**: no test counted the comparisons | Checking each row of this table against a test by name | C# `A_pair_of_files_is_counted_once_however_often_it_is_looked_at` |
| R213 | **The simulation said "The report was saved to your downloads" and "saved as invitation.ics" whether or not the browser saved anything. A page shown inside another page, as the published link is, is not allowed to save files, so there the sentence would have been false** (the same kind of fault as the false "Copied" of the break test) | Reading the rules of the place the simulation is published, before publishing | The page says a download was tried and what to do if nothing was saved, as the backup in Settings already did. Checks S11 to S13 run it inside a frame that forbids downloads: no file, and no claim of one. The Windows app is not affected: it writes the file itself and reports a failure |
| R214 | The subject of commit `aeb2364a` says "fix" for the save wording, but that commit holds only the description in DESIGN.md; the change itself is in `8d4042e1` | Reading the two commits after pushing | Stated here; a pushed commit is not rewritten |
| R207 | The fault of R194 came back three times while writing this round: an escaped semicolon typed as two characters arrived as one | The compiler, and one failing check | Such text is now written by a script and read back; no escape of that kind is typed by hand |

### What round 26 does not prove

No RPM or director has used it. **No real report was compared**, and no file made by Excel itself: the workbook in the tests is the test's own. **Nobody has run the new windows on a real Windows desktop**: not the file dialogs, not the clipboard, not a paste into Excel, not the invitation in Outlook. The narrative and the reading prompts were never sent to a model. The comparison covers text, numbers and dates in the columns both files have, one sheet per workbook; it does not see formulas, formatting, hidden rows, merged cells or a second sheet, and it reads dates month first. My usage counts uses; it does not measure time saved, and it reports nothing by itself. The security review was done by this tool against published checklists; it is not an assessment by the agency's security office, and no penetration test was done.

## Round 27: two documents and a stored prompt, a check of an AI answer, signs worth a second look, five daily helpers; a security review; tested twice

The owner asked, in three messages, for the ability to "detect any and all AI content and how to respond accordingly maybe based on predicted AI model", for a way to "select or upload two documents/files/dataset and ask to run a saved prompt to analyze/compare/contrast meticulously", and for anything else that would help daily. Each was answered with a proposal first; the owner then wrote "Build everything and go with all of your recommendations".

**What was recommended instead of what was asked, and why.** The tool does not detect AI-written text and does not name a model. No reliable way to do either exists, and a wrong flag on a person's writing does harm. The recommendation the owner accepted is "signs worth a second look, never a verdict", one fixed way to respond, what a file says about itself quoted as the file's own statement, and an optional second opinion through the person's AI. This is recorded here so that no later reader takes the feature for a detector.

**Three questions were left open and were decided by the builder**, so that the work could go on; each is the owner's to change: the work is in the ANDA Companion only (the personal app in the other repository was not touched); the response says "Follow your office's policy on AI-written content" because that policy is not known to this project; PDF files are not read, because that needs a component from outside.

What was done:

1. **Two documents compared**, paragraph by paragraph, by fixed rules, in the simulation (text, Markdown) and the Windows app (also Word `.docx`; `Services/DocCompare.cs`, `Services/DocumentReader.cs`).
2. **One of the person's stored prompts on what was compared**, with the differences only by default and the beginning of each file only when ticked; always shown first.
3. **A check of an AI answer** against the comparison and the files, with no AI.
4. **Signs worth a second look** and **a follow-up for a reply**, in Review a draft (`Services/SecondLook.cs`).
5. **A report against my data**, **My sheet since last week** (`Services/SheetCopies.cs`), **a hand-off page** and **a meeting page** (`UI/HandoffWindow.cs`, `Services/HandoffData.cs`).
6. **A shared file** (`mockup/round27-golden.json`, written by `tools/round27-golden.cjs`): 14 pairs of documents and one very long pair, 6 prompt materials, 8 answers, 9 texts for signs, 14 reminders, 4 pages and 10 reports, answered by the simulation; the app must reproduce every answer.
7. **A security review** of the new work with the same two skill sets as in round 26, and seven fixes from it (R221 to R227 below).

Not done, and said so: PDF; the extension has none of this round; the simulation reads CSV, text and Markdown only, and shows the weekly copy with its two sample sheets; the earlier list of things that are only in the simulation is unchanged.

**Every suite was run twice on the final code**; the app was rebuilt from nothing before each of its two runs. Results, identical in both runs: C# **1,157 passed, 15 skipped, 0 failed** (25 of the tests are new in this round: 8 that hold the app to the simulation's answers, 12 of the windows, 5 from the security review); extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **39 suite files, none with a failure** (1,800 checks counted by the runner in each pass), among them the new suite for this round (71 checks) and the parity scripts; `tools/round27-golden.cjs`, `tools/compare-golden.cjs` and `tools/proofread-stress.cjs` **0 mismatches**; `tools/wording-check.mjs` **0 findings**; the repository's `npm run validate` and `npm run security:docs` pass. A build with every .NET security rule switched on raises no security rule, and the package check reports no known vulnerability. Runs made earlier in the round on code or tests that then changed (two full C# runs and part of one simulation run) are not counted.

CI: **no Windows run started for any of this round's commits (`116ae32c`, `5e258f9d`, `82572dc6`)**, as for rounds 25 and 26. The last Windows run is still run 44, on the round 24 code, from Nov 12; why pushes no longer start the workflow is not known to this session, and a manual start was refused to it earlier (403). **Three rounds of app code (25, 26 and 27) have therefore never been built or self-tested on Windows.** Until a run on this code is read, "the same rules on Windows", the additions to the self-test and the two security steps of the workflow are claims about code that passed on this Linux server only. The owner can start the workflow from the repository's Actions tab.

| Claim | Evidence | Limit |
|---|---|---|
| **The app compares two documents exactly as the simulation does** | C# `Two_documents_are_compared_exactly_as_the_simulation_compares_them` (14 pairs: the sample letters, the same text twice, two empty files, an empty file against one with text, a paragraph changed and one added, different line breaks and spaces, two paragraphs that changed places, a paragraph with nothing in common, 40 paragraphs, 90 paragraphs that all differ, one very long paragraph, markup and text like a formula, tabs and blank lines, accented letters and an emoji) and `Two_documents_too_long_to_be_matched_are_compared_in_order_and_the_summary_says_so` (2,100 paragraphs that all differ) | Paragraphs as plain text. **Formatting, tracked changes, comments, headers, footnotes, tables inside a document and pictures are not compared**, and the summary says so. Beyond 2,000 paragraphs the middle is compared in order, and the summary says so |
| **A Word document is read as its body text, and nothing in it is followed** | C# `A_Word_document_is_read_as_its_body_text_and_what_the_file_says_about_itself_is_only_quoted`, `A_file_that_is_not_a_readable_document_is_said_plainly_and_nothing_in_it_is_followed` (a document type definition is refused; a file that is not a zip, an empty one and one with a NUL are said plainly) | **Made with the test's own small `.docx`, not with Word.** Of a tracked change the inserted text is read and the deleted text is not. Text boxes, headers and footnotes are not read |
| **What a file says about itself is quoted and never believed** | The same test: "The file says: created by …; last saved by …; … Anyone can change these fields, so they prove nothing." | The fields are whatever the file holds |
| **A stored prompt gets the differences, and the files only when asked, and is always shown first** | Simulation P1 to P11; C# `A_stored_prompt_gets_the_differences_and_the_files_only_when_asked_and_is_always_shown_first`, `A_prepared_prompt_is_never_sent_without_the_preview_and_its_note_says_what_it_holds_and_how_long_it_is` (a prompt stored as "send without the preview" is still shown first; making the preview sends nothing) ; the material of 6 cases is in the shared file | **No model was called.** What a model answers was not judged. Up to 6,000 characters of each file, so a long file is given in part, and the heading says how much |
| **Giving a prompt the files is a choice for one pair of files** | Simulation S1; C# `Giving_a_prompt_the_files_themselves_is_chosen_anew_for_every_pair_of_files` (eight ways a file can change) | |
| **Text in a file cannot close the fence around it** | Simulation P10; the shared cases for cleaning | The fence and the fixed instruction lower the risk that text in a file steers a model; they do not remove it. The answer is shown to copy and is never acted on |
| **An AI answer is checked as the simulation checks it** | C# `An_AI_answer_is_checked_exactly_as_the_simulation_checks_it` (8 answers), `An_AI_answer_is_checked_against_the_comparison_and_the_files_and_what_stands_nowhere_is_listed`; simulation A1 to A4, S4 | **It checks that each number, date and name with a digit stands somewhere in the comparison or the files. It cannot tell whether the wording or the reasoning is right**, whether a value is used for the right thing, or whether something was left out. Words without a digit are not checked |
| **The check stays quick however large the files are, and says when it read only part** | Simulation S2 (two million characters of facts), S3, S5, S6; C# `The_check_of_an_answer_takes_time_in_proportion_to_the_text_and_reads_an_answer_up_to_a_fixed_length`, `An_answer_is_checked_against_the_first_million_characters_of_a_very_large_file_and_the_window_says_so` | Timed on this server (under one second) |
| **Signs worth a second look are the simulation's, and never a verdict** | C# `Signs_worth_a_second_look_are_the_simulations_and_never_a_verdict` (9 texts; no item may say "written by", "AI-generated" or "probably"), `Review_lists_signs_worth_a_second_look_without_a_verdict_and_offers_a_follow_up_for_a_reply`; simulation V1, V2, V9, V10, V12 | **This is not a detector.** A text with none of these signs can be AI-written, and a text with all of them can be a person's. The list of phrases is short and English. Whether a cited source exists is not checked: the tool says so and asks the person to check |
| **A follow-up is offered only for a request with a date, and is added once** | C# `A_reply_asked_for_by_a_date_is_found_exactly_as_the_simulation_finds_it` (14 texts: with and without a year, with a weekday, a day that passed yesterday, a day long past, a date that does not exist, a date that asks for nothing, a bare "12/3"); simulation V3 to V5, V9 | **The mailbox is not read**: the tool cannot know whether a reply came. The first such date in the draft is used |
| **A report is checked against my data as the simulation checks it** | C# `A_report_is_checked_against_my_data_exactly_as_the_simulation_checks_it` (10 reports), `A_report_is_checked_against_my_data_and_the_window_asks_for_one_file_only`; simulation R1 to R8 | **Only the goal date and the forecast date**, for rows with a six-digit number in an ANDA column. The person's data is not the system of record. **Never tried on a real report** |
| **No copy of the sheet is kept until the person switches it on, and switching it off deletes them** | C# `One_copy_of_the_sheet_is_kept_for_a_week_the_newest_five_stay_and_switching_off_deletes_them_all`, `My_sheet_since_last_week_keeps_nothing_until_switched_on_and_then_compares_with_the_last_copy`; simulation W1 to W5 | In the simulation the "copy" is a sample sheet. **The app's copies were written to a test folder on this server, never on Windows.** A copy is as sensitive as the sheet |
| **A backup from another PC cannot switch the weekly copy on or off** | C# `A_backup_from_another_PC_cannot_switch_the_weekly_copy_of_the_sheet_on_or_off` (also a backup edited by hand, and a reset) | |
| **A hand-off page is written as the simulation writes it, holds notes only when ticked, and is never sent** | C# `A_hand_off_page_is_written_exactly_as_the_simulation_writes_it` (4 pages), `The_hand_off_page_is_made_from_my_own_data_and_notes_and_is_copied_or_saved_only_when_asked`, `A_selection_that_names_ANDAs_offers_a_meeting_page_for_exactly_those`; simulation H1 to H12 | Plain text from the person's own data; it is not the system of record and says so. **Nobody has handed one to a colleague** |
| **A very large document is refused without being held in memory** | C# `A_document_is_read_up_to_a_fixed_size_and_a_larger_one_is_refused_in_plain_words` (a 55 MB text part inside a small file is refused in well under a second) | The limits are 25 MB for the file and 5,000,000 characters of text |
| **What a file or an answer contains is only ever text** | Simulation D8, D9, A3, V10 (an image tag with a script builds nothing and runs nothing); C# `Two_documents_are_read_from_disk_compared_by_paragraphs_and_a_table_with_a_document_is_refused_in_words` (the saved page escapes it; no copied line starts like a formula) | The saved page was read as text by the tests, not opened in a browser by a person |
| **The new windows pass the accessibility scan and fit a phone** | Simulation D5, A4, R3, W3, H8, V6 (scans), D1 (the chosen way is named to a screen reader), H10, H11 (phone width) | The scan is automated; **no screen reader was run** |
| **The same rules on Windows** | The app's self-test now compares the two sample letters, checks one answer, lists the signs of one text, finds one reply date and checks one report | CI is a server, not a person's desktop; see the CI paragraph |

### Defects caught in round 27

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R215 | A reply asked for by a date with no year that had just passed ("by Jul 15", read on Jul 16) was offered as a follow-up for the next year | Writing the shared cases | With no year: this year if the day is still to come; the next year only if the day passed more than 180 days ago; otherwise nothing is offered |
| R216 | An unfilled placeholder ("[insert", "[Your Name]") was described as "wording that an AI assistant often leaves behind" | Reading the sentence on the screen | It is called what it is: "a placeholder that was not filled in" |
| R217 | In a report checked against the data, a date from the person's data was shown in a different form from the report's | Reading the picture | Dates from the data are shown as "Oct 3, 2026" |
| R218 | The row "Match rows by" stayed on the screen for documents, which have no rows: its `hidden` attribute was overridden by a style on the same element | The browser suite | A style rule honors `hidden` |
| R219 | The meeting-page button named a count without a limit; the page holds at most eight ANDAs | Writing the test | The button and the page agree (up to eight) |
| R220 | **Commit `5e258f9d` was pushed with a line that `tools/wording-check.mjs` flags** (a sample of leftover assistant wording, with an exclamation mark, in the self-test). The check was not run before that push | Running the check in this round's full pass | The line is marked as a sample; the check is part of both passes and reports nothing |
| R221 | **The tick "Also give it the beginning of each file" stayed on** when another pair of files was chosen, so file text could go into a preview the person did not expect | The security review (consent must be specific) | Taken off whenever a file changes or another way of comparing is chosen, in both programs; simulation S1 and a C# test |
| R222 | **A settings backup from another PC could switch the weekly copy of the sheet on** | The security review (who can change a setting that makes the tool keep data) | The setting belongs to the PC, like the paths of the person's files; a C# test restores a backup, a backup edited by hand, and resets |
| R223 | **A small Word file could unpack to a very large text**, all of it read into memory before anything was said | The security review (bound what is consumed) | The reader stops at a fixed number of characters and the document is refused at 5,000,000 characters of text, without being held |
| R224 | **The check of an answer slowed with the square of the text**: each word with a digit was compared with every date, each date in the answer with every date in the files, and each value to list with every value listed. With large files the window would have stopped answering | Reading the rule for what it does with the largest input it accepts | One pass, with sets; the shared answers are unchanged (0 mismatches); two million characters of facts and an answer at the limit take under a second in both programs. **The slow form was not timed before it was replaced** |
| R225 | The answer box took 20,000 characters and cut a longer paste without saying so, so a check could pass on half an answer | The security review, while bounding the check | The box takes 200,000 characters; a longer answer is not checked and is said to be too long |
| R226 | The check searched files of any length | The same | The first 1,000,000 characters of each file, and the window says when that limit was reached |
| R227 | **"Include my notes" was ticked when a hand-off or meeting page opened.** Notes are the person's own and can be candid, and the page is made to be passed on | Reading the documents against the owner's rule on keeping data safe, after the first full C# run | Off until ticked, each time a page opens, in both programs; simulation H4, H12 and the C# test |
| R229 | **The rule for documents too long to be matched (more than 2,000 paragraphs between the first and the last difference) had no test in either program**, while this record was about to say that it had | Checking each row of the table above against the shared cases | A shared case made by a rule (2,100 paragraphs that all differ) and a C# test; the shared file was written again |
| R228 | Slips in the tests themselves while writing them (a boolean printed "False" where the shared file says "false", a length of 29 expected as 28, an expectation of three signs where the rule that two overlapping phrases count once gives two, a test file that overwrote a week it meant to keep) | The tests failing | Corrected in the tests; the product was right in each case |

### What round 27 does not prove

No RPM or director has used it. **No real document, report or sheet was read**: the Word file in the tests is the test's own, not one written by Word, and the reports are fictional. **Nobody has run the new windows on a real Windows desktop**: not the file dialogs, the clipboard, the saved page or the weekly copies. **No model was called**: not for a stored prompt on files, not for the second opinion; what a model does with the differences or with the beginning of two files was not seen, and how well the fence holds against text written to steer a model was not tested against a model. **The signs worth a second look are not a detector and were not measured as one**: there is no rate of right and wrong answers, by design, because the feature gives no answer. The check of an answer finds values that stand nowhere; it does not find a true value used wrongly, a wrong conclusion, or an omission. The document comparison sees paragraphs of plain text only. The office's policy on AI-written content was not read; the tool points to it. This review is the builder's own, made with the repository's skill sets; it is not an agency security assessment.

## Round 28: an audit against the owner's standing rules, an independent adversarial review, and the fixes; tested twice

The owner gave standing rules for tools built for a federal Windows 11 computer (Edge, Microsoft 365, endpoint security; no script launchers, no unapproved installs; nothing sent by default; no automatic deletion of what may be a record; Section 508; a private risk log; an adversarial review by someone other than the builder; "tested" only for what was seen passing). The brief's fields were blank, so three questions were asked first. The answers: audit the existing ANDA Companion and change nothing; push to the working branch only; keep the risk log in the session and in chat.

**The audit** (no file changed) listed 27 gaps in a risk log. **A separate reviewer**, which had not built the product and was told to fix nothing, attacked it on the owner's six points (bad input, damaged settings and data, a clock or time zone that changes, starting offline, keyboard and screen reader, text written to steer an AI) and returned 14 findings, 12 of them reproduced by running a probe. Its probes ran against the built program and the simulation; no model, agent or network was used. The owner then wrote: "Yes fix and proceed with all recommendations". Every finding that is a fix was fixed; what is a feature or the owner's decision was listed and left. This run does not use the word that at the agency means formal validation: these are tests that were run and seen to pass.

What was fixed (numbers are the risk log's):

| # | Finding | Fix | Held by |
|---|---|---|---|
| 1 | **With default settings the app used Claude whenever `ANTHROPIC_API_KEY` existed on the PC**; opening a full card then sent record fields, without the person switching anything on in the app | A new install is "Offline only". "Auto" stays as a choice. Settings from an earlier version that say "Auto" are moved to "Offline only" once, with a sentence at start. A restore never switches a connection on | C# `A_new_install_uses_no_AI_even_when_a_key_is_on_the_PC`, `Settings_from_an_earlier_version_are_moved_forward…`, `A_restore_keeps_the_lists…`; simulation X1, B3 |
| 14 | **A damaged or cut-off `settings.json` reset every setting, and the next save overwrote it**; the write was not atomic; no version | Set aside as `settings.unreadable-…json`; the file of the last good start is used, else the defaults; written beside the file and moved over it; one unreadable value is repaired alone; a version, a migration, and a copy of a newer file before it is read | C# `A_cut_off_settings_file_is_set_aside_never_overwritten_and_the_last_good_settings_come_back` (seven shapes of a bad file), `One_value_that_cannot_be_read_is_repaired_alone…` |
| 15 | **A damaged or oversized `todo.json` or `mytodo.json` loaded as an empty list and was overwritten by the next change**; `Version` was written and never read | Set aside, never written over; a newer version is copied before it is read; said once at start | C# `A_damaged_or_oversized_to_do_file_is_set_aside_and_never_overwritten` (two lists, four shapes), `A_list_written_by_a_newer_version_is_copied…` |
| 16 | **Free text from the sheet went into AI and agent prompts outside the fence**, while SECURITY.md said records reach a model only inside one | Each such value is one line, with hidden characters removed, and cannot write a tag of the fence. SECURITY.md is corrected and says what remains: the words of a sheet cell are still words | C# `A_value_from_the_sheet_cannot_write_a_tag_of_the_fence…`, the shared `fence` cases; simulation F2 |
| 17 | **The fence was passed by characters that look like "<" or cannot be seen**, and the preview could not show them; an opening tag was not caught | Hidden characters are removed and counted on a line the preview shows; look-alike brackets count as "<"; both tags are caught | C# `Text_for_an_AI_is_fenced_and_a_value_is_guarded_exactly_as_in_the_simulation` (10 shared cases); simulation F1 |
| 18 | **Simulation: a prompt name was put into the page as markup**, so script in a restored backup ran when the sample brief was shown | The name is text | Simulation X2 (the reviewer's own attack, now a check) |
| 19 | **A workbook could make the sheet reader throw past the loader, which runs at every start** | The loader takes any failure and says the file could not be read; a date number past the last day stays a number; the reader stops at a fixed number of characters | C# `A_date_number_past_the_last_day_a_date_can_hold_stays_a_number`. **The catch-all itself was read, not exercised**: no file that still makes the reader throw was found |
| 20 | **The default invite could not be accepted from the keyboard** | The same shortcut again on the same number accepts it, and the invite says so to a screen reader | Simulation K1, K2. **In the app this was read, not run**: the path needs a real global shortcut |
| 21 | **Simulation: seven windows did not take focus when opened, seven dropped it to the page on Escape, at phone width Tab reached controls behind the window, Escape closed windows in a fixed order; notices were not announced. App: status lines not announced, four boxes without a name** | A window takes the keyboard when it opens and gives it back where it was; Escape closes the window in use; Tab stays inside the open window at phone width; a status element says each notice. App: five status lines are live regions, four boxes are named | Simulation K3 to K8 (all 16 windows; 57 Tab presses). App: by reading the markup |
| 22 | **A restore of a backup without prompts or replies deleted the person's own**; nothing was kept of what a restore replaced; a `prompts.json` with an empty entry threw, and a cut-off one was replaced by the starters | Absent lists are left alone; what a restore replaces is saved first as a backup of its own; empty entries are left out; a cut-off file is set aside | C# `A_restore_keeps_the_lists_a_backup_does_not_hold…`, `Prompts_and_replies_with_empty_entries_load…`; simulation B1, B2 |
| 4 | **Finished to-do items were deleted by the app** after 30 days, when a list was full, and on "Clear finished" | Added to an archive file beside the list, which is only added to; if it cannot be written, the items stay | C# `Finished_items_leave_the_list_for_an_archive_file_and_stay_when_the_archive_cannot_be_written`; simulation A1, A2 |
| 25 | A damaged weekly copy of the sheet blocked a new copy for that week | It is set aside and the week is kept again | C# `A_damaged_copy_of_the_sheet_is_set_aside_so_that_the_week_can_be_kept_again` |
| 24 | The age of an agent's finding used the wall clock; the simulation read the time zone once | UTC, and a finding dated after now is not fresh; the zone is asked each time | C# `A_finding_is_not_fresh_after_the_clock_was_set_back…`. The tray timer is unchanged |
| 7 | `demo/Start-Demo.bat` | Removed; a `data` folder beside the program does what it did; no script file of that kind is in the product's folder | C# `A_data_folder_beside_the_program_keeps_the_demo_out_of_the_profile_with_no_launcher_script` |
| 26 | The browser suites existed only in the builder's session | 40 suites, a runner and a README in `tools/sim-tests/`, with no machine path in them | Both passes below were run from there |

**Every suite was run twice on the final code**; the app was rebuilt from nothing before each of its runs. Results, identical in both runs: C# **1,172 passed, 15 skipped, 0 failed** (15 of the tests are new in this round: 13 in `Round28Tests`, one that holds the app to the shared fence cases, one for the age of an agent's finding); extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **40 suite files, none with a failure** (1,818 checks counted by the runner in each pass), run from `tools/sim-tests/` with `run-all.cjs`, among them the new suite for this round (18 checks); `tools/round27-golden.cjs` (now with 10 fence cases), `tools/compare-golden.cjs` and `tools/proofread-stress.cjs` **0 mismatches**; `tools/wording-check.mjs` **0 findings**; the repository's `npm run validate` and `npm run security:docs` pass. **Three earlier browser runs of this round are not counted**: one was stopped, and two each caught something that was then corrected (R246, R247). After the two counted passes one suite was changed in where it writes a picture (R248) and was run twice more alone; nothing else changed.

CI: **no Windows run started for this round's commits (`6ae89e51`, `a1cfc4a7`, `a6bfc539`) either.** The last Windows run is still run 44, on the round 24 code, from Nov 12. **Four rounds of app code (25 to 28) have never been built or self-tested on Windows**, and this round changed how the app reads and writes every file of the person's own. The owner can start the workflow from the repository's Actions tab.

### Defects caught in round 28

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R230 | An AI connection was on by default when a key was on the PC (risk 1) | The audit, reading the rule "off by default" against the code | Fixed |
| R231 | A damaged settings file was overwritten (risk 14) | The independent review, reproduced | Fixed |
| R232 | A damaged to-do file was overwritten, and the record said only that it "is ignored" (risk 15) | The independent review, reproduced | Fixed; the earlier sentence in this document stands as written then, and is corrected here |
| R233 | Sheet values reached AI prompts outside the fence, against SECURITY.md (risk 16) | The independent review, reproduced in the assembled prompt | Reduced, and the document corrected. Words in a cell can still try to steer a model |
| R234 | The fence let through look-alike and invisible characters and an opening tag (risk 17) | The independent review, reproduced | Fixed in both programs, with shared cases |
| R235 | A prompt name ran as markup in the simulation (risk 18): the same kind of hole as R-numbered findings of round 15 | The independent review, reproduced | Fixed |
| R236 | A workbook could stop the app at start (risk 19) | The independent review; the exception reproduced | Fixed; the catch-all not exercised |
| R237 | The invite and several windows could not be used from the keyboard as the record implied (risks 20, 21) | The independent review, reproduced in the simulation | Fixed in the simulation; in the app by reading |
| R238 | A restore deleted lists it did not hold (risk 22) | The independent review, reproduced | Fixed |
| R239 | Finished items were deleted automatically (risk 4) | The audit | Archived instead. The daily counts and weekly copies beyond five are still removed |
| R240 | **A restored backup could switch Claude or "Auto" on**: only "My API" was switched off by a restore | Writing the fix for R230 | A restore brings every connection back as "Offline only" |
| R241 | **The two programs' pattern engines disagreed on whether "ſelection" (a long s) is "selection" when case is ignored** | The shared fence cases, the first time the app ran them | The long s is named in the rule, so both agree |
| R242 | Before this round a neutralized closing tag was rewritten in small letters; the fix keeps the person's letters | Writing the shared file again and reading what changed | One shared answer changed, on purpose |
| R243 | The fault of R194 and R207 came back twice: characters typed as escapes arrived as the characters themselves, in the new suite and in the runner | Reading the files back | Converted by a script and counted; the product files were written by script from the start |
| R244 | Two checks of the round 15 suite expected the old sentence and the old default | The first run of the suites from the repository | Updated on purpose: the sentence now says the items are kept, and a restore leaves the connection off |
| R245 | Six C# tests expected "Auto" or an unfenced look | The first full C# run of the round | Updated on purpose; one of them showed that a "null" in a settings file must keep its old meaning, which the first form of the new reader had changed |

| R246 | Renaming the desktop icon that was called after the removed launcher left the product's name three times without "(Notula)", against the naming rule of round 12 | The round 12 suite, in the first full pass | Corrected; the pass was stopped and both passes were run again from the start on the corrected file |

| R247 | The corrected label, with the full name, was too long for a desktop icon: two icons overlapped. One more check, in the round 11 suite, still expected a reset to return the connection to "Auto" | The suites of rounds 20, 21 and 11, in the next full pass | The icon is labeled "Notula" and read out with the full name; the check expects "Offline only". That pass is not counted either |

| R248 | **One suite wrote its picture over a tracked file** (`out/r16_transparency_40.png`): in the builder's session it had written into its own folder, and from the repository it wrote into the product's | `git status` after the two counted passes | The file was restored; the suite writes the picture to the temporary folder and was run twice more |

### What round 28 does not prove, and what is still open

**Nothing here ran on Windows**, and no Windows build has started since Nov 12. No model was called: what a model does with a guarded value or a strengthened fence is not known. **No screen reader was run**; focus and announcements were checked as positions and attributes, not as speech. The app's side of the keyboard and screen-reader fixes was read, not run. The independent review did not read the Outlook, Elsa and My API request code, and did not load the extension.

Left as they are, each in the risk log: the app reads a selection by sending Ctrl+C and reading the clipboard, can install a global mouse hook (off by default), and ships screen-capture code for its self-test; the program is unsigned; the simulation needs JavaScript; no first-run guide, no backups of stored items on every change with one-click restore, no archive window, no event log, no connection indicator in every window, no version and change-log screen, no time-zone setting; a default model name and two settings live in code or environment variables; the tray timer uses the wall clock; the simulation keeps no file before a restore; the extension has none of rounds 26 to 28; the built-in prompts have never been tried on sample inputs with a model.

## Round 29: what is switched on, said in every window; an event log; what changed; a separate adversarial review; tested twice

After the report on round 28 the owner wrote: "1. Do everything. 2. Republish everything 3. I think JavaScript should be ok". "Do everything" is read as: build the standing rules that the audit listed as not met, in rounds, the simulation first. This round is the transparency rule: an always-visible line that says "Offline · Local only" or names each connection, with what is sent, where and what is kept; a local event log that is only added to, can be read and exported, and never holds the person's content; and a version and change log in the tool. It also ends the last case in which switching something off deleted data: kept copies of the sheet. "JavaScript should be ok" is recorded as the owner's judgment, not as a confirmation from IT. This document does not use the word that at the agency means formal validation: these are tests that were run and seen to pass.

**Built, simulation first, then the app, answer for answer:** the line at the foot of every window (`connections()` / `Connections.Describe`); the list of connections; the event log (`eventEntry` / `EventLog`); *About and what changed*; kept copies deleted only by the person, with a second press. `mockup/round29-golden.json`, written by the simulation, holds 8 states of what is switched on, 34 events, the log's 34 sentences, 13 changes of what is switched on, the CSV and the change list; the app's tests reproduce every one.

**A static security pass by the builder** (reading only; nothing was scanned or probed) used the Security Engineer and Secure App Builder skill sets as checklists, as in rounds 26 to 28, and found three things in the new code, all fixed with tests in both programs: a log detail could begin with `=`, `+` or `-`, which a spreadsheet reads as a formula when the export is opened; the log reader held every line of the file in memory; and a connection's name could carry invisible characters that reorder what is read, while a look-alike letter in an address was shown as typed (now shown in the form the network uses).

**A separate reviewer**, which had not built the round and was told to change nothing, then attacked it on six points: can the log hold what it may not; is the line ever wrong; is "only added to" true; can kept copies go without the person; can the line be used by keyboard and screen reader; do the sentences on screen match the code. It ran probes against the simulation in a browser and against the built app's services; it used no model, no agent and no network. It returned **12 findings, 9 of them reproduced by a probe and 3 from reading**. All 12 were fixed, in both programs where both were affected, each with a test. It also listed what held: every call that writes to the log passes fixed words; the file is only ever appended to; the line follows every path that changes a setting; the line agrees with what can really send; no copy is made while keeping is off; the simulation's line is a real button reached by Tab.

| # | The reviewer's finding | Fix | Held by |
|---|---|---|---|
| 1 | **High. The app itself deleted kept copies of the sheet from the sixth week on**, against the sentence on screen ("only you delete them"), the change list and `SECURITY.md` | The app never deletes a copy by itself. The window says how many are kept | C# `One_copy_of_the_sheet_is_kept_for_a_week_and_the_app_never_deletes_a_copy_by_itself` (seven weeks, seven copies) |
| 2 | Deleting while keeping was switched on kept a fresh copy in the same press, while the window said "Nothing else was changed" | Nothing is read again after a delete; the window says that keeping is still on and when the next copy is made | C# window test (`whileOn`: the sheet is read 0 times); simulation W8 |
| 3 | A request that timed out or was canceled left no line; a failure was labeled "Sending did not complete" although the text had left | A line is written **before** the request goes; a second line says "no answer" or "timed out or canceled"; the kind reads "No answer, or the request failed" | C# `A_request_to_an_AI_is_written…` (five lines for three requests), `Asking_an_agent_is_written…`; simulation E6, E11 |
| 4 | "Test" in Settings and the Outlook sign-in are requests too, and were not written | Each is written before it goes | C# `A_connection_test_a_sign_in_and_a_download_are_requests_too…` |
| 5 | **The rule let a name, an address or a path into the log** ("John Smith, 12 Main St" passed), and a line added by hand was shown; `SECURITY.md` claimed otherwise | A detail is kept only when it is one of the log's own 34 sentences, when written, read and exported | Shared cases (21 of the 34 events are withheld); C# `Every_call_that_writes_to_the_event_log_writes_one_of_the_logs_own_sentences`, which reads the source; simulation E4, E8, E10 |
| 6 | In the simulation the line's name for a screen reader was the same fixed words whatever was switched on | It is read out as shown, then what a press does | Simulation C11 |
| 7 | Moving a connection somewhere else (another service, address or agent) wrote nothing; nothing was written at start, so the log could show "off" with no "on" | "AI connection changed", "agents changed", "Outlook connection changed"; one line at start | 13 shared changes and 8 start lines; simulation C12, E9 |
| 8 | A double-click or a held Enter deleted; in the simulation the question survived closing the window | The second press counts only after 0.7 seconds; the question is withdrawn on close, on a change of view and when the button loses the keyboard | C# window test (`twice`, `left`); simulation W4 to W7 |
| 9 | The Connections window could be taller than the screen; windows that fit themselves did not leave room for the line | The list scrolls inside the window; the six windows that fit themselves leave 28 pixels | C# `r.scrolls`. **Read, not seen on a real screen** |
| 10 | Three sentences in the list did not match the code: the whole record goes to the AI, not "as the card shows it"; an agent's answer stays in memory until the app closes; Outlook's sign-in address was not named | The three sentences say what the code does | Shared cases; C# window test |
| 11 | A very long log held the window while it was read and exported; a line a spreadsheet had saved again without quotes was dropped silently; the open window did not show a new line | The reader keeps the newest lines only and takes no lock; saving writes line by line off the UI thread; an unquoted line is read; lines that are not the log's are counted and said; the window follows a new line | C# `The_event_log_file_is_only_added_to…` |
| 12 | Small differences between the two programs; at phone width the line was at the very end of a long window | The same sentence after a delete; an agent's send is written as an agent's; the line stays at the foot of a window that scrolls | Simulation W8, E11, K3 |

**Every suite was run twice on the final code**; the app was rebuilt from nothing before each of its runs. Results, identical in both runs: C# **1,186 passed, 15 skipped, 0 failed** (14 of the tests are new in this round: 6 that hold the app to the simulation's answers and to the file, 5 of the windows, 2 written after the review, 1 for an agent's line in the log); extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **41 suite files, none with a failure** (**1,860 checks** counted by the runner in each pass; the first pass ran the suites one after another, the second three side by side), among them this round's suite (42 checks); `tools/round29-golden.cjs`, `tools/round27-golden.cjs`, `tools/compare-golden.cjs` and `tools/proofread-stress.cjs` **0 mismatches**; `tools/wording-check.mjs` **0 findings**; the repository's `npm run validate` and `npm run security:docs` pass. Every exit code was read. Runs made earlier in the round are not counted, because the code changed after them: one full C# run and one full run of the 41 suites before the review, and one of each while the review's fixes were going in (the second caught R270).

**Not proven by this round**, and what to do about each: the app's windows have not been seen on a real screen with the line in them (their height, where the keyboard starts after the line is added, what a screen reader says): a Windows run and a keyboard walk; **no Windows build has started since Nov 12**, so rounds 25 to 29 have never been built or self-tested on Windows; no request was really sent, so the lines for a test, a sign-in and a download are held by tests with stand-in answers; file locking was tried on Linux only; a log of millions of lines still takes about a second to open (measured by the reviewer before the change, not measured again); the extension has none of this round; the built-in prompts have never been run on a model.

### Defects caught in round 29

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R249 | The new line's style name was already used by the banner in Settings, which changed that banner | The first picture of the simulation | Renamed before the first suite run |
| R250 | An event detail could hold a six-digit number ("100000 characters") | Reading the rule against its own claim | Counts are written with commas; later replaced by the fixed list (R261) |
| R251 | In the hour that occurs twice when the clocks go back, the time zone was shown as "EDT" for both | The new test for daylight time | The abbreviation is decided from the moment in UTC |
| R252 | Three browser checks still expected that switching the weekly copy off deletes the copies, and that the AI options are the last control in Settings | The first trial run of the 41 suites | Updated on purpose |
| R253 | A detail could begin like a spreadsheet formula | The static security pass | Fixed; later covered by the fixed list |
| R254 | The log reader held every line in memory | The static security pass | Fixed |
| R255 | An invisible character in a connection's name could reorder the line; a look-alike letter in an address was shown as typed | The static security pass | Fixed in both programs |
| R256 | The app deleted kept copies beyond five (finding 1) | The separate review, reproduced | Fixed |
| R257 | A delete kept a fresh copy in the same press (finding 2) | The separate review, reproduced | Fixed |
| R258 | A timed-out request left no line; a wrong label (finding 3) | The separate review, reproduced | Fixed |
| R259 | Tests and the Outlook sign-in were not written (finding 4) | The separate review, from reading | Fixed |
| R260 | Destination changes and the state at start were not written (finding 7) | The separate review, reproduced | Fixed |
| R261 | **The log could hold a name or an address, and `SECURITY.md` said it could not** (finding 5) | The separate review, reproduced in both programs | Fixed with a fixed list of sentences; the document now says what the code does |
| R262 | The simulation's line was not read out as shown (finding 6) | The separate review, reproduced | Fixed |
| R263 | A double-click deleted (finding 8) | The separate review, reproduced | Fixed in both programs |
| R264 | The Connections window and the line were not budgeted for the screen (finding 9) | The separate review, from reading | Changed; not seen on a real screen |
| R265 | Three sentences did not match the code (finding 10) | The separate review, from reading | Corrected in both programs |
| R266 | Long logs, unquoted lines, a window that did not follow (finding 11) | The separate review, reproduced | Fixed |
| R267 | Small differences between the programs (finding 12) | The separate review, reproduced | Fixed |
| R268 | **"Update now" had been written to the log as "(detail withheld…)" since the app stage**: its sentence held an "@", which the first rule did not allow | Reading every call against the new list | The sentence is "request for the Drugs at FDA file", and a test now reads the source for every call |
| R269 | The first form of the line that stays in sight let text show below it in the window's padding | A picture, at phone and desktop width | Corrected and looked at again |
| R270 | That correction made three windows scroll sideways at phone width | The round 16 suite, in a trial run of the 41 suites | Corrected; that run is not counted |

CI: **no Windows run has started for this round's commits either.** The last Windows run is still run 44, on the round 24 code, from Nov 12. The owner can start the workflow from the repository's Actions tab.

## Round 30: a copy of each list after every change, one press to put a copy back, the archive searched and put back from; a separate adversarial review; tested twice

This round builds the owner's standing rule for stored items: a local backup on every change and daily, a number of versions the person sets, one-click restore; finished items archived after a number of days the person sets, searchable and restorable; and no deletion by the tool on its own. One question was asked first, because a set number of versions and "no automatic deletion" cannot both hold for the copies. The owner's answer (Nov 18): **keep a set number; older backup copies may be removed by the app if the screen says so; the lists and the archive are never removed.** This document does not use the word that at the agency means formal validation: these are tests that were run and seen to pass.

**Built, simulation first, then the app, held to shared answers** (`mockup/round30-golden.json`: 20 moments and the day each falls on, 12 sets of copies and which stay, 6 sets from a clock that was ahead, 14 views of an archive with damaged lines): copies of five lists after every change; which copies stay (`backupPlan` / `Backups.Plan`); one-press restore; the *Backups and archive* window; the archive searched and put back from (`archiveRows` / `TodoArchive`); the days before archiving as a choice; settings version 2. In the simulation a full My to-do list had still dropped its oldest finished item where the app archives it; that was corrected (R272).

**A static security pass by the builder** (reading only; nothing was scanned or probed), with the Security Engineer and Secure App Builder skill sets as checklists, noted three things in the new code and held them until the separate review had returned, so as not to change the code under it: a copy is a plain file, and was checked only by a shallow count before it was put back; the archive was read again at every key in the search box; and the copies folder needed a stated bound. The review reproduced the first two (its findings 8 and 11) and they were fixed with it; the bound (at most 199 copies of a list) is stated in `SECURITY.md`.

**A separate reviewer**, which had not built the round and was told to change nothing, attacked it on six points: loss of data; the rule against automatic deletion; time; the archive; settings; keyboard and plain language. It ran probes against the simulation in a browser and against the built app's services and window; it used no model and no network. It returned **12 findings, all 12 reproduced by a probe**. All were fixed in both programs where both were affected, each with a test. It also listed what held: the two programs' rule for which copies stay agreed on 400 random sets; both clock-change nights agreed; the newest copy always survived; the archive was only ever added to; no markup ran; the event log got fixed sentences only.

| # | The reviewer's finding | Fix | Held by |
|---|---|---|---|
| 1 | **High. A restore put a copy over the present list when no copy of the present list could be made** (the list had grown past the size the app reads; or the copies folder could not be written), **and still said it could be undone** | Before anything is written, what is there now must be among the copies; otherwise the restore is refused and nothing is changed | C# `A_restore_is_refused_when_what_is_there_now_cannot_be_copied_first` (both of the reviewer's cases) |
| 2 | A finished item was invisible from day 8 until the day it was archived, which this round let reach a year | The window lists finished items that are still on a list and no longer shown there, searches them, and opens one again | C# `A_finished_item_is_never_out_of_sight…`, window test; simulation F6, F7 |
| 3 | Restoring a settings backup lowered the number of copies silently, and copies were then removed | The two choices belong to this PC: a backup does not carry them; a restore or reset leaves them alone | C# `A_settings_backup_or_a_reset_cannot_lower_the_number_of_copies_kept`; simulation S1, S2 |
| 4 | One arrow key on the "Keep" box removed copies at once, with no question and no way back | A smaller number says how many copies would go and offers one button; nothing is removed, and the number is not changed, until it is pressed | Window test (`asked`, `withdrawn`, `fewer`); simulation N1 to N1d |
| 5 | The keyboard was left on nothing after "Restore this copy" and "Put back on the list"; a copy made elsewhere reset the chosen copy | The keyboard returns to that list's copies, or to the search box; the chosen copy stays chosen | Window test (`focusAfterRestore`, `focusAfterPut`, `stays`, `startsOnKeep`); simulation F1, F2 |
| 6 | "Put back" said it worked when the list could not be saved; after a restart the item was on no list and the archive said it had been put back | The list is saved first and the archive is told second; a failed save leaves both as they were and says so | C# `Putting_an_item_back_says_it_worked_only_when_the_list_was_saved` |
| 7 | Two writes within one millisecond made no second copy and left a temporary file holding the list | Copies are stamped in whole milliseconds, as they are named; a half-made copy is always removed | C# `Two_changes_in_one_millisecond_get_two_copies…` |
| 8 | A damaged copy (`{"Items":[1,2,3]}`) was shown as "3 items" and restored "successfully" into an empty list; a notes file set aside earlier was overwritten; the message read "The Notes on ANDAs is…" | A copy is read by the list's own reader before it is counted or used; if the running list cannot take it after all, what was there is put back; a file is set aside under a name that says when, never over an earlier one; the sentence was rewritten | C# `A_copy_the_list_could_not_read_is_never_put_in_its_place` (seven damaged shapes, all five readers) |
| 9 | A copy from a clock that had been ahead stayed "the newest" and broke one copy a day; two copies a year apart read alike | Such a copy is given a moment before now at the next change; copies are shown with their year | Shared cases (6); C# same test as 7; simulation F3, F5 |
| 10 | Restoring a copy from before an archive run archived the same item again; after one put-back a second button stayed | An item archived again before it was put back is shown once, by its newest line | Shared cases; C# archive test; simulation F8 |
| 11 | A large archive held the window (60 seconds to match 50,000 put-backs; each key in the search box read the file again); one very long line took 2.5 GB of memory | Put-backs are matched in one pass; the window reads the archive once and searches what it read; the file is read in pieces and a line over 16 KB is never held | C# `A_large_or_damaged_archive_is_read_in_bounded_pieces…` (100,003 lines and one 3 MB line) |
| 12 | Smaller: a copy named for the year 9999 made copying throw at start; "(as it is now)" was said of a copy that did not hold the list; a number given as a number made the page's archive rule throw; the app took "123456" followed by a line break as a number; a box in the simulation was named only "Keep"; the app's window opened with the keyboard on nothing | Each corrected; start-up is guarded | C# tests above; simulation F4, F9; shared cases |

**Every suite was run twice on the final code**; the app was rebuilt from nothing before each of its runs. Results, identical in both runs: C# **1,204 passed, 15 skipped, 0 failed** (18 of the tests are new in this round: 17 in `Round30Tests`, 1 of the window); extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **42 suite files, none with a failure** (**1,907 checks** counted by the runner in each pass; the first pass ran the suites one after another, the second three side by side), among them this round's suite (47 checks); `tools/round30-golden.cjs`, `tools/round29-golden.cjs`, `tools/round27-golden.cjs`, `tools/compare-golden.cjs` and `tools/proofread-stress.cjs` **0 mismatches**; `tools/wording-check.mjs` **0 findings**; the repository's `npm run validate` and `npm run security:docs` pass. Every exit code was read. Runs made earlier in the round are not counted, because the code changed after them: one full run of each program before the review, and one of each after its fixes.

**Not proven by this round**, and what to do about each: nothing ran on Windows (**no Windows build has started since Nov 12**; rounds 25 to 30 have never been built or self-tested there), so file locking, a path the account may not write, and what Narrator says in the new window are unknown: a Windows run and a keyboard walk; a truly full disk was not tried (a place that cannot be written stood in for it); the PC's time zone changing while the app runs; a restore racing with a save from another thread (the reviewer forced this with a second thread and the restore was undone every time; the app itself saves its lists and restores on one thread, so it cannot happen there, and this is by design, not by test); how long a 100,000-line archive takes to open after the change (about a second before it; not measured again); the search treats a dotted capital I and a final sigma differently in the two programs; the extension has none of this round.

### Defects caught in round 30

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R271 | The new window was wired before the page had read its own rules, so the page raised an error at load and nothing after it ran | The first run of the tool that writes the shared answers | Wired after the page's code has been read, as the line of round 29 is |
| R272 | In the simulation, a full My to-do list dropped its oldest finished item; the app has archived it since round 28 | Reading the list's code while planning this round | The simulation archives it ("the list was full"); held by simulation D4 |
| R273 | A restore went ahead with no safety copy (finding 1) | The separate review, reproduced | Fixed |
| R274 | Finished items out of sight for up to a year (finding 2) | The separate review, reproduced in both programs | Fixed in both |
| R275 | A settings restore removed copies unasked (finding 3) | The separate review, reproduced in both programs | Fixed in both |
| R276 | An arrow key removed copies (finding 4) | The separate review, reproduced in both programs | Fixed in both |
| R277 | The keyboard left on nothing (finding 5) | The separate review, reproduced in both programs | Fixed in both |
| R278 | "Put back" said it worked when the list was not saved (finding 6) | The separate review, reproduced | Fixed |
| R279 | No copy within one millisecond; temporary files left (finding 7) | The separate review, reproduced | Fixed |
| R280 | A damaged copy restored into an empty list; a set-aside file overwritten; a sentence that did not read (finding 8) | The separate review, reproduced | Fixed |
| R281 | A clock that was ahead; no year in a copy's time (finding 9) | The separate review, reproduced in both programs | Fixed in both, with shared cases |
| R282 | The archive doubled after a restore (finding 10) | The separate review, reproduced in both programs | Fixed in both, with shared cases |
| R283 | A large or damaged archive held the window and the memory (finding 11) | The separate review, reproduced | Fixed; opening time not measured again |
| R284 | Six smaller things (finding 12) | The separate review, reproduced or read | Fixed; the two letters the search treats differently are recorded as a known limit |

CI: **no Windows run has started for this round's commits either.** The last Windows run is still run 44, on the round 24 code, from Nov 12. The owner can start the workflow from the repository's Actions tab.

## Round 31: every time in the person's time zone, and data that says how old it is; a separate adversarial review; tested twice

This round builds the owner's standing rule for time: a live clock; a time zone found by itself and changeable, shown with its abbreviation and the right daylight time; storage in UTC; "how long ago" brought up to date every minute; and for every piece of data its source, its "as of" time and a stale label after a period the person sets. The owner said to go ahead on Nov 19. This document does not use the word that at the agency means formal validation: these are tests that were run and seen to pass.

**Built, simulation first, then the app, held to shared answers** (`mockup/round31-golden.json`: 506 moments written in 22 zones, among them both nights the clocks change in 2026 from each side, a leap day, and years in which a zone kept other hours than it keeps now; 24 ages; 88 data lines; 9 moments at and past the ends of what can be written; 12 sentences of Settings; 3 hand-off pages): six rules (`zoneClock`, `zoneAbbr`, `timeLabel`, `timeOfDay`, `relTime`, `freshLine` / `Services/Clock.cs`); a Time part in Settings, Look and feel; the card's data line; the watch rule `stale-data` by the same reckoning; settings version 3.

**A static security pass by the builder** (reading only; nothing was scanned or probed), with the Security Engineer and Secure App Builder skill sets as checklists, found nothing new that is sent, kept or read, and noted three things, held until the separate review had returned so as not to change the code under it: a number far outside any real date made the label fail instead of saying "time not known" (reproduced in the simulation, outside the repository); the abbreviation rule gave a wrong abbreviation for years in which a zone kept other hours (Arizona in 1967); and on a computer with no data for the chosen zone the app would write UTC times under an "EST" label. The review reproduced the third (its finding 7) and met the first as a stored note that broke the card. All three were fixed with it.

**A separate reviewer**, which had not built the round and was told to change nothing, was given the eight claims of the round and a list of places to dig. It ran probes against the simulation in a browser and against the built app's services and windows; it used no network. It returned **15 findings and three smaller ones, 13 of the 15 reproduced by a probe and two reasoned from the code and marked so**. All were fixed in both programs where both were affected, each with a test, except where the table says what was left and why. It also reported what held: this round's app tests passed under five machine time zones, and the whole app suite passed under Pacific/Auckland and America/Los_Angeles.

| # | The reviewer's finding | Fix | Held by |
|---|---|---|---|
| 1 | **High. The app's browser host ignored both new settings**: with Hawaii and 30 days chosen, the app's card said the data was fresh in Hawaii time while the browser extension was told it was stale, with an Eastern time | The host sets the chosen zone, reckons "stale" with the chosen days, and tells the browser the card's own data line | C# `The_browser_is_told_what_the_app_shows…` |
| 2 | **High. The hand-off and meeting pages said "as of today" over stale data**, and carried no source or STALE; the page is handed to a colleague | The first line says "made on"; a line "Data: …" carries the card's own data line, STALE included, in both programs | Shared cases (3 pages); C# same test; simulation R5, R6; round 27 suites updated |
| 3 | The compact boxes showed goal and forecast with no word about the data, in both programs | A compact box writes "STALE: older than N days" on itself and carries the whole data line as its tooltip and as what a screen reader is told. **Left as a limit: source and "as of" are not printed on the box itself, which is too small for them; one click opens the card** | C# `A_compact_box_writes_STALE_on_itself…` (two looks); simulation R7, R8; pictures |
| 4 | The simulation had no stale rule: with stale data its watch list and to-do lists said nothing, while the app flags every ANDA | The simulation's watch rules have `stale-data`, in the app's words, reckoned by the rule of the card's line | Simulation R1 to R4 |
| 5 | An open card's watch line kept "9 days old" while its data line said "12 days ago"; the minute timer also counted "a watch flag shown" in the usage log, though nothing else was to be done by it | The watch line is reckoned again with the data line and painted again when its words changed; a tick is not counted | C# card test (states 8, 9), `…a_minute_does_not_count_as_a_card_shown` (with a control) |
| 6 | After a change of zone the watch flag named another day than the card's line; the portfolio's status line kept the old zone | The flag's day is taken from the moment, in the zone in use now; the status line is made when it is asked for | C# `The_browser_is_told…` (Eastern, then UTC) |
| 7 | A zone the computer cannot find became UTC without a word: Settings said "Eastern time (EST)" and a UTC time was written under "EST" | A chosen zone the computer has no data for is not used: the computer's own is, and Settings says "This computer has no data for Eastern time…". A zone asked for by name and not found is written as UTC and says UTC. An abbreviation is now written only when the zone really is at its standard time or an hour ahead of it | C# `A_zone_this_computer_has_no_data_for…`, window test; shared cases (12 sentences); simulation R14, R15 |
| 8 | (Reasoned, not run.) "Automatic" did not follow a change of the zone in Windows while the tray app runs | The computer's zone is asked afresh at most once a minute. **Not run on Windows** | Read; the app suite |
| 9 | The reference file's line gave its day without a year, no age and no STALE | The day carries its year, in both programs. **Left open: no age and no STALE on the reference file. A public file that changes about monthly would read STALE nearly always with the person's 7 days; whether it gets its own number of days is asked of the owner** | C# `WritingAndReferenceTests`; simulation t8 |
| 10 | Under "Automatic", Detroit and Indianapolis read "UTC-5", and Windows' zone for Puerto Rico reads "UTC-4" and would be named "America/La Paz" | The tool knows the abbreviations of 33 zones of the United States, not nine; Settings names the computer's zone as the computer names it. **Left as it is: Windows has no zone of Puerto Rico's own, so "Automatic" there reads UTC-4 (right, without an abbreviation); "Atlantic (Puerto Rico)" can be chosen** | Shared cases (22 zones); C# zone test |
| 11 | The clock in Settings could be 59 seconds behind the taskbar | The timer fires as each minute of the clock begins, in both programs | Simulation R17; C# by reading (the window test drives a shortened timer) |
| 12 | The simulation's suite passed with the minute timer deleted | Two checks drive a clock of the test's own and call nothing; on a copy with the timer removed the suite fails (tried) | Simulation R17, R18 |
| 13 | The Arizona choice was cut off in the app's Settings box | A shorter name, a wider box, and a test that measures the longest choice against the box | C# window test (`fits`) |
| 14 | The simulation's sample data, "read at 7:12", read 8:12 and 6:12 on the mornings the clocks change | 7:12 on the wall clock on those mornings as well | Simulation R16 |
| 15 | Changing the zone, or restoring a settings backup that carries one, could remove a backup copy unasked: the copies' days were counted in the chosen zone | Copies are counted by the computer's days, whatever zone times are written in | C# `Backup_copies_are_counted_by_this_computers_days…`; simulation R11 |
| + | A stored note with a number that is no date broke the card; a note written while a day is pretended carried the real day; notes on an open card kept the old zone | A number that is no date is "time not known" everywhere, and such a note keeps the words it was saved with; the pretended day is carried; a change of zone rewrites an open card's notes and an agent's "Asked at" | Shared cases (9 ends); simulation R9, R10, R12, R13; C# `A_change_of_zone_in_Settings_rewrites_the_notes…` |

**Every suite was run twice on the final code**; the app was rebuilt from nothing before each of its runs. Results, identical in both runs: C# **1,222 passed, 15 skipped, 0 failed** (18 of the tests are new in this round: 12 in `Round31Tests`, 6 of the windows); extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **43 suite files, none with a failure** (**1,958 checks** counted by the runner in each pass; the first pass ran the suites one after another, the second three side by side), among them this round's suite (51 checks); `tools/round31-golden.cjs`, `tools/round30-golden.cjs`, `tools/round29-golden.cjs`, `tools/round27-golden.cjs`, `tools/compare-golden.cjs` and `tools/proofread-stress.cjs` each report 0 mismatches; `tools/wording-check.mjs` 0 findings; `npm run validate` and `npm run security:docs` pass. Pictures of the stale card, a stale compact box in two looks and the Time part of Settings were taken and looked at; the long explanation under the Time part runs past the lower edge of that picture, as the foot of earlier Settings pictures does.

**Not proven by this round**, and what to do about each: nothing ran on Windows (**no Windows build has started since Nov 12**; rounds 25 to 31 have never been built or self-tested there). Unknown until it does: whether .NET there finds the ten zones by their IANA names (it needs the system's ICU; if it does not, the app now says so and uses the computer's zone, and that path was tried here only by standing in for such a computer); what "Automatic" reads in each United States zone as Windows names them; whether the zone follows a change made in Windows within the minute; what Narrator says for the status line. The extension has not had this round: it still writes the time a file was saved without a zone, and its own card does not carry the new line (the app's host now tells it the line when the two are connected). Times before 2007 and zones outside the 33 the tool knows are written with their difference from UTC, by design. "Today", for due dates and day counts, is the computer's day whatever zone is chosen; Settings says so. The usage log and the names of weekly copies still go by the computer's day.

### Defects caught in round 31

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R285 | In the app, the watch line stayed hidden when data turned stale on an open card | The new window test | Fixed |
| R286 | The card's data line was cut off at the right edge, so the word STALE was not on the screen | Looking at the picture | Fixed: the line wraps |
| R287 | Three times were still written without a zone: the to-do list's "Updated", an agent's "Asked at", a note's time (the last was also kept as a local time) | The builder, reading for every place a time is written | Fixed in both programs; a note is kept as its moment in UTC |
| R288 | Two scripts and the fixture shared with the extension gave different answers on a computer in another time zone | The first trial run after the change | Each names its zone |
| R289 | A comment added by the builder swallowed the rest of its line, and the backups had no clock | Seven round 30 tests, at once | Fixed |
| R290 to R304 | The 15 findings of the separate review, in the table above | The separate review | Fixed, except: no age or STALE on the reference file (9, asked of the owner); Puerto Rico under "Automatic" on Windows (10); source and "as of" not printed on a compact box (3) |
| R305 | The three smaller things in the last row of the table | The separate review | Fixed |
| R306 | A number that is no date made the label fail; a wrong abbreviation for years a zone kept other hours | The builder's static pass | Fixed: "time not known"; abbreviations only from 2007 and only at the zone's standard time or an hour ahead of it |

CI: **no Windows run has started for this round's commits either.** The last Windows run is still run 44, on the round 24 code, from Nov 12. The owner can start the workflow from the repository's Actions tab.

## Round 32: first steps, a guide of five steps; the reference file's own days; a separate adversarial review; tested twice

Two decisions of the owner on Nov 19 are built in this round. **The reference file**: the Drugs@FDA file changes about monthly, so it is marked STALE after a number of days of its own, 45 unless changed. **The guide**: the standing rules ask for a first-run guide that can be skipped, has at most five steps, is opened again from Help and uses labeled sample data; the project had removed such a panel in round 17 at the owner's direction, so the owner was asked, and chose a guide that opens by itself once, at the first start, with five named steps. This document does not use the word that at the agency means formal validation: these are tests that were run and seen to pass.

**Built, simulation first, then the app, held to shared answers** (`mockup/round32-golden.json`: 98 reference files; the five steps; 20 views of a step; the five sentences the guide says; 6 ways a shortcut is named; 7 starts): `refSaved` / `Clock.RefSaved` and a third choice in Settings; settings version 4; `GUIDE_STEPS`, `guideStep`, `guideShortcut` / `Services/Guide.cs`; the guide's window in both programs; *Help: first steps…* in the tray menu. In the app the guide takes the place of the sample card that used to open by itself at the first start.

**A static security pass by the builder** (reading only; nothing was scanned or probed), with the Security Engineer and Secure App Builder skill sets as checklists, found nothing new that is sent, kept or read, and noted three things, held until the separate review had returned: where the settings cannot be saved the app cannot remember that the guide was shown, so it would open at every start; a note typed on the sample card is kept under the number 216482 among the person's own notes; and opening the sample card is opening a card, so with an AI connection switched on the fictional sample record is sent like any other (declared in `SECURITY.md`; with the default, nothing is sent). The review reproduced the first two (its findings 9 and 11) and they were fixed with it.

**A separate reviewer**, which had not built the round and was told to change nothing, was given the round's claims, among them that every sentence of the guide is true of the product. It ran probes against the simulation in a browser and against the built app's windows, and applied mutations to a copy of the code. It returned **15 findings, 13 reproduced by a probe**. All were fixed in both programs where both were affected, each with a test, except where the table says what was left. It also reported what held: the reference rule agreed between the two programs on 4,016 random cases; this round's app tests passed under three machine time zones; the new Settings label is not cut off.

| # | The reviewer's finding | Fix | Held by |
|---|---|---|---|
| 1 | **High. In the app, once Enter had put the caret in the note box, Esc never closed the card** (the code asked two things that cannot take the keyboard to take it). Older than this round; the guide taught exactly that sequence | Esc leaves the note box and gives the keyboard to the card; the next Esc closes it. The guide's sentence now reads "Enter starts a note of your own and Esc closes the card" | C# `On_the_card_Enter_starts_a_note_Esc_leaves_the_note_box_and_the_next_Esc_closes_the_card` |
| 2 | In the simulation, "Press Esc to close it" closed the guide, not the card | With a card on the screen and only the guide open, Esc closes the card first | Simulation R2, R3 |
| 3 | The sample card hid itself after ten seconds while the guide still said it "is open" | A card opened by the guide stays until it is closed; the sentence says "was opened" | Simulation R14, R15 (with a control); C# by reading the app's one call (`stay: true`) |
| 4 | With a compact look chosen, the app's button opened a compact box, which takes no keyboard, so Esc closed the guide | The guide always opens the full card | Simulation R1, R4; C# `The_guides_buttons_open…` and the text of the app's call |
| 5 | **The app's wiring for this round was untested: six mutations survived the whole suite** (the guide at every start; the card without the file's age; Help opening another window; the person's own record shown as "the sample"; the wrong section; the wrong window) | The first-start rule (`FirstStart`) and the guide's wiring (`FirstStepsWiring`) were taken out of the app's composition and are tested as code; the few lines that remain in the composition are held by reading their text. All six mutations, applied together to a copy, now fail two tests | C# `At_the_first_start_the_guide_opens_once…`, `The_guides_buttons_open…`; the mutated copy (tried) |
| 6 | "This guide stays open behind it" was false in the simulation: the guide sat in front and, at phone width, covered the button that chooses the file | What the guide opens is in front of it; a press on the guide brings it forward again; the sentences say "in front of this guide" | Simulation R7, R8, R18 |
| 7 | Four sentences of the guide were not true of the product: "not copied anywhere" (a weekly copy can be kept), "not sent anywhere" (an AI connection sends the record), "nothing leaves this PC unless you switch a connection on" (two buttons go online without being connections), "every window says at its foot" (the card and the compact box do not) | Each was rewritten to what the product does | Shared cases (the five steps); the review's own reading |
| 8 | In the simulation, with the person's own file chosen, the "sample" card read "YOUR FILE" | A card opened by the guide reads as sample data whatever file is chosen | Simulation R5, R6 |
| 9 | Where the settings cannot be saved, the guide opened at every start without a word | The guide still opens, and says at its first step that it will open again and where to find it; saving the settings now reports whether it worked | C# `At_the_first_start…` (a save that fails, a save that throws, a path that cannot be written); simulation R16 |
| 10 | Settings and the card could name different days for the reference file | One sentence makes both (`Saved`, `LoadedMessage`); Settings asks for it each time it is shown | C# `Settings_and_the_card_name_one_day_and_one_age…`; simulation R11 |
| 11 | A note typed on the sample card went into the person's own notes under 216482 | With the person's own data loaded, notes are off on the sample card and the guide says so. **Left as it is where no data of the person's own is loaded: a note on a sample record is then a note on sample data, as it always was** | C# `The_guides_buttons_open…` (`notes=False`) |
| 12 | A 400-day-old file with no matching drug said nothing of its age; Settings never said STALE; the browser's card gets the words but no flag | An old file is said to be old where no drug matched; Settings says STALE in the card's words. **Left: the extension's card shows the words without the look; the extension has not had rounds 26 to 32** | C# same test as 10; simulation R12 |
| 13 | Step 2 named a shortcut that Windows had refused | A shortcut another program holds is not taught: the step says so and where to choose another | Shared cases (6); C# `The_guides_buttons_open…`; simulation R10 |
| 14 | Documents: the app's README still described the old first run; the design still said "no step list"; "changes nothing" was not exact (opening Settings from the guide counted a section as used) | Corrected; a section the guide opens is not counted | C# `A_section_the_guide_opens_in_Settings_is_not_counted…` (with a control) |
| 15 | Smaller: Help while the guide was open reset it in the simulation and not in the app; closing the guide at a first start left the keyboard on nothing; the app announced the step's place without its title; a backup written before a setting existed set the person's choice back to the usual value | The guide keeps its place in both; the keyboard goes to the taskbar; the title is announced; a setting a backup does not hold is left as it is (as a list it does not hold always was) | Simulation R9, R13, R17; C# `A_backup_written_before_a_setting_existed…`, window test |

**Every suite was run twice on the final code**; the app was rebuilt from nothing before each of its runs. Results, identical in both runs: C# **1,237 passed, 15 skipped, 0 failed** (15 of the tests are new in this round: 9 in `Round32Tests`, 6 of the windows); extension unit **31 passed** and its end-to-end run **ALL PASS**; simulation: **44 suite files, none with a failure** (**2,019 checks** counted by the runner in each pass; the first pass ran the suites one after another, the second three side by side), among them this round's suite (61 checks); the seven shared-answer tools (`round32`, `round31`, `round30`, `round29`, `round27`, `compare`, `proofread-stress`) each report 0 mismatches; `tools/wording-check.mjs` 0 findings; `npm run validate` and `npm run security:docs` pass. Pictures of the guide in both programs were taken and looked at; the first picture of the simulation's guide showed it jammed into a corner without margins, which was corrected (R309).

**Not proven by this round**, and what to do about each: nothing ran on Windows (**no Windows build has started since Nov 12**; rounds 25 to 32 have never been built or self-tested there), so the guide's place on the screen beside the card it opens, its order among other topmost windows, what Narrator says as the steps change, and the real first start of an installed copy are unknown: a Windows run and a keyboard walk. The first start itself is held by a rule that is tested and by one line of the app's start-up that is read, not run. Whether five steps are the right five is a question for the trial with colleagues, not for a test. The extension has no guide and no reference-file age of its own.

### Defects caught in round 32

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R307 | The simulation's sample reference file was always "18 days old": its saved moment moved with the clock | A new check | Fixed: the moment is fixed when the file is chosen |
| R308 | Parts of the guide that were marked hidden were on the screen, among them a button with no name | The accessibility scan | Fixed |
| R309 | The simulation's guide opened in the corner of the screen without margins | Looking at the picture | Fixed |
| R310 | The guide's first line named the product without "(Notula)"; an older check still expected the earlier version label | The project's naming check; the trial run | Fixed |
| R311 | The shortcut and the connection line were written into the guide's text in two passes, so one could be read as a mark for the other; the app's guide lost the keyboard when the foot line was put under it | New app tests | Fixed in both programs |
| R312 to R326 | The 15 findings of the separate review, in the table above | The separate review | Fixed, except what the table says was left (11 in part, 12 in part) |
| R327 | A comment in a scratch command hid that a copy had not been made, and a run of unmutated code was nearly taken for a mutation run | The builder, reading the output | Redone on a real copy; recorded here because a check that cannot fail is the defect this review was about |

CI: **no Windows run has started for this round's commits either.** The last Windows run is still run 44, on the round 24 code, from Nov 12. The owner can start the workflow from the repository's Actions tab.

## Round 33: the Windows app and the Edge extension brought level with the simulation; a separate adversarial review; tested twice

The owner's go-ahead of Nov 19. **The Edge extension** (version 0.2.0) gains what rounds 29 to 32 gave the other two programs, where it applies to an extension: every card's data line (source, as of when in the person's time zone, how long ago, STALE past the days chosen), written again as each minute begins; the time zone and days in its Settings; the saved moment of an imported spreadsheet kept in UTC; what is switched on, said at the top of Settings and the foot of the toolbar window; first steps of its own; a version and what changed. **The Windows app** gains the five features the simulation has had since rounds 18 and 19: card details with Copy, larger text and compact spacing, recent and kept ANDAs, One Bar, and Today.

**Built, held to shared answers.** The simulation's rules for the five features were separated from their windows without changing what they do (one change on purpose: Today's "needs a look" is the most pressing first), and `mockup/round33-golden.json` holds the app to them (18 views of the details, 28 values, 32 steps of the lists, 56 sets of One Bar rows, 19 lookup messages, the Today views and sentences, the names of the Copy buttons, which typed texts are nearly a number, the display factors). The extension's `src/core/time.js` is the simulation's own text for writing a time; a test compares the two texts and holds the copy to all of round 31's and round 32's answers.

**A static security pass by the builder** (reading only; nothing was scanned or probed), with the Security Engineer and Secure App Builder skill sets as checklists: no markup sink, network call, `eval` or process start in the new code of any program; no user content in any log; the extension asks for no new permission; the new messages to its service worker are answered only for the extension's own pages and scripts, and the values they carry are cut to a length and read as text. New stored items are listed in SECURITY.md. Nothing new leaves the PC.

**A separate reviewer**, which had not built the round and was told to change nothing, ran probes in a browser against the simulation and the extension, and against the built app's windows, and broke a copy of the code on purpose to see what the tests miss. A first attempt stopped on a usage limit before it reported; its copy of the code was left with seven changes applied, so the second reviewer re-checked everything on unchanged code and none of the first attempt's results are used. It returned **17 findings, all fixed**:

| # | The reviewer's finding | Fix | Held by |
|---|---|---|---|
| 1 | **Extension: the minute put another file's line on an open card**: a card opened from one file, after a newer file was imported, read "Newer.csv · as of … (just now)" above the old file's data; a sample card left open after the source changed began to name the person's file | The minute ages only the moment the card was built from; with the desktop app, the app's new line is used only while it names the same source | Unit test with two files, a sample card and the app; the browser test, which had written the old behaviour down as correct, now checks the new one |
| 2 | App: a change of text size did not reach windows opened at standard size, Settings included | Every window is remembered when it opens, at any size | Window test (card, Today, then a change) |
| 3 | One Bar (app): a move with the arrow keys, then typing, then Enter opened an empty review instead of the card | Typing starts again at the first row, as the simulation does | Window test |
| 4 | App: Settings at larger text grew past the screen | Its height is fitted in its own units, and again when the size changes while it is open | Window test on a 1366 × 768 screen |
| 5 | Today (both): a pressed row lost the keyboard, and the whole right-hand side was read out again | The keyboard stays on the row; one short sentence is said | Window test; simulation R6 |
| 6 | A restored backup that switched remembering off kept and offered the recent numbers (both) | They are forgotten, and nothing offers them while remembering is off | Rule test; simulation R8 |
| 7 | "Reset all settings" kept the recent numbers in the app and not in the simulation | A reset forgets them in both | Rule test |
| 8 | A kept number no longer in the data could not be seen or removed, yet used one of the five places | *Clear my list* in Settings (both) | Window test; simulation R9 |
| 9 | One Bar: a move with the arrow keys was never said to a screen reader (both) | The move is said; in the simulation the row is also the box's active descendant | Window test; simulation R4 |
| 10 | Extension guide: the sample card's key hints were not true there (E did nothing, Esc closed the whole guide), and Sister ANDA did nothing | E, S and Esc work on the card where it is; Esc closes the card only; no sister is offered | Browser test |
| 11 | Extension: "Kept by the extension: nothing" could be untrue while an imported spreadsheet is stored | The sentence says what is kept | Unit test |
| 12 | Extension Settings: the clock was read out every minute, and the connection sentence on every unrelated change | No live role on the clock; the sentence is written only when it changes | Unit test (source); browser test |
| 13 | App: a slow "Copied" landed on the next card | The message is dropped when the card has moved on | Window test with a held clipboard |
| 14 | The Copy buttons' names were lowered into the wrong words ("Copy us agent") (both) | Capitals are kept ("Copy US agent email") | Shared answers; window test; simulation R1 |
| 15 | One Bar: Enter on an NDA number opened a review of it; the new reason was reachable only by the button, which in the app read "Peek" | A first row says it is not an ANDA number and Enter shows why; the app's button reads "Go" and opens the selected row | Shared answers; window test; simulation R2, R3 |
| 16 | A test that could not fail on the build machines, which run in UTC | It is pinned to a zone with daylight time | The faulty reading was put back: caught under UTC |
| 17 | Today: the marks' meaning was carried by color only (both) | Each mark is also a word beside the number | Window test; simulation R5 |

**The faults put back on purpose.** The reviewer's first count: of 15 changes to the app, five went unnoticed by the whole suite (recent numbers not stored, a "Copied" left on the next ANDA, no limit to the screen, Today reading the kept list once, a pressed row not repainted); of 17 changes to the extension, four (the open card's number not checked, the clock never refreshed, the legacy time read as UTC on a UTC machine, and one the browser test catches). After the fixes, each of the twelve app faults found or fixed in this round was put back one at a time in a copy, and **each one fails a test**; so do the three extension faults tried the same way.

**Every suite was run twice on the final code** (after two earlier attempts were thrown away, R352 to R354); the app was rebuilt from nothing before each of its runs. Results, identical in both runs: C# **1,269 passed, 15 skipped, 0 failed** (32 of the tests are new in this round: 16 in `Round33Tests`, 16 of the windows); extension unit **66 passed** (also under `TZ=UTC`) and its three browser runs (on its own, round 33 with an accessibility scan, and with the real desktop app) **ALL PASS**; simulation: **45 suite files, none with a failure** (**2,054 checks** counted by the runner in each pass; the first pass ran the suites one after another, the second three side by side), among them this round's suite (34 checks); the eight shared-answer tools (`round33`, `round32`, `round31`, `round30`, `round29`, `round27`, `compare`, `proofread-stress`) each report 0 mismatches; `tools/wording-check.mjs` 0 findings; `npm run validate` and `npm run security:docs` pass. Pictures of the card's details, the card at the largest text with compact spacing, One Bar, Today, and the extension's guide, Settings and toolbar window were taken and looked at.

**Not proven by this round**, and what to do about each: nothing ran on Windows (**no Windows build has started since Nov 12**; rounds 25 to 33 have never been built or self-tested there, and this session may not start the workflow), so an enlarged card at a screen edge, a 1.3 Settings window on a real small laptop, the real clipboard, and Narrator on the new windows are unknown: a Windows run and a keyboard walk. The extension ran in Chromium, not in Edge. The app's start-up lines that join the features are held by reading source text. Larger text in the app enlarges whole windows; whether colleagues prefer that to the simulation's way is for the trial.

### Defects caught in round 33

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R328 | Three boxes on the extension's Settings page had no name for a screen reader (older than this round) | The accessibility scan added to the extension's browser test | Fixed |
| R329 | The extension's browser test read UTC half way through: a time zone set page by page is dropped for every extension page when one of them closes | A failing check of the zone sentence | Fixed: the test browser gets its zone as a whole |
| R330 | Compact spacing missed the card's parts that a look moves elsewhere | The window test of compact spacing | Fixed: the nine parts are named |
| R331 | Today had no line saying what is switched on, and the line was not enlarged with a window at larger text | Looking at the picture | Fixed, both orders tested |
| R332 | A portfolio's "Established name" or "Project description" column, already used for the drug or the notes, was not shown under Details | A rule test with a full sheet | Fixed: shown under both; "My notes" is then a column of its own or nothing |
| R333 | The first separate review stopped on a usage limit and left its copy of the code with seven changes applied | The second reviewer | None of its results used; all re-checked on unchanged code |
| R334 to R350 | The 17 findings of the separate review, in the table above | The separate review | Fixed |
| R351 | The extension's browser test had written down as correct a card that began to name a newer file | The separate review's finding 1 | Corrected to check the right behaviour |
| R352 | Two older test classes (round 15's agent tests and the action-runner tests) wrote to the app's one event log without being held apart from the tests that read it; a round 15 test failed once in the first counted pass | The first counted pass | Fixed: both are in the held-apart group; that pass was thrown away and both passes started again |
| R353 | An older auto-hide test failed now and then, mostly in the first full run after a rebuild: it gave two cards 0.3 s before hiding and then pinned one, and on a slow first drawing the second card hid before the key that pins it arrived ("TopLevel must be a headless window") | The full runs before the counted passes; the full error text from rebuilt copies | Fixed in the test: 1.5 s, checked over up to 8 s. Not a product fault: the shortest real setting is 10 s. The first counted passes were thrown away and both started again |
| R354 | Three checks in the round 18 and round 19 simulation suites still described the behaviour the review's fixes changed on purpose (a Today row's text, a restore with remembering off, a Copy name in lower case); and in updating one of them the builder first fixed a value the check is meant to read, which would have let it pass whatever the code did | The first counted pass; the builder's own re-read | Updated on purpose, with a new check that a restore with remembering on still leaves this PC's own numbers; the fixed value replaced by the value read after the restore |

CI: **no Windows run has started for this round's commits either.** GitHub refused to let this session start it ("Resource not accessible by integration"). The last Windows run is still run 44, on the round 24 code, from Nov 12. The owner can start the workflow from the repository's Actions tab.

## Round 34: the shortcut opens the card, the answer first, Today as home; a separate adversarial review; tested twice

The owner's choices of Nov 20, from the suggestions written after round 33 (A1, A2, C1): **the person's own shortcut opens the card directly** (in the simulation, the app and the Edge extension; Whisper stays a choice, and a middle-click still asks first); **the card shows the answer first**, with the calendar, Latest, Ahead, the reference dates, the brief and the key hint one press away under More or E (simulation and app); **a click on the tray icon opens Today**, which is in the everyday tray menu and leads to the watch list with *Show all, with reasons…*; the watch list's own tray item is gone (simulation and app). Settings stored before round 34, when Whisper was the default, move to Full once: the app writes its settings in form 6 and says so in one sentence; the extension writes form 3 and says so once on its Settings page.

**Built, held to shared answers.** `tools/round34-golden.cjs` writes `mockup/round34-golden.json` from the simulation: the usual way to start, the three labels, the first step for all 24 combinations of way to start, urgent rule, urgency and shortcut or middle-click, the watch line's tail, the version and what changed. The app (`Round34Tests`) and the extension (`round34.test.mjs`, the 12 cases started by a shortcut) give the same answers. (The simulation's own check against that file guards against later drift only; it cannot disagree with itself on the day it is written.) `mockup/round29-golden.json` was written again for one change of wording, "when a card is opened in full", and nothing else changed in it.

**Found while building, before the review:** the tray icon's spoken label still said "Click for lookup" after its first refresh (simulation); a change of text size with a card open let the card run past the taskbar (simulation; it showed once the card became short); the watch list opened from Today did not take the keyboard, so Escape closed Today instead (simulation); a card without a forecast would have shown an empty calendar after More (app); a peek's "Full brief" led to a card whose brief was hidden; and, with an AI connection on, the brief would have been asked for although it is not shown. All are fixed with a test (R355 to R360).

**A static security pass by the builder** (reading only; nothing was scanned or probed), with the Security Engineer and Secure App Builder skill sets as checklists: no new outbound host, permission, hook, capture, clipboard use or stored item; new text on screen is set as text, never as markup; less is sent than before, because with an AI connection on neither the brief nor an agent set to run by itself is asked before the card is opened in full. The one stored change is the move from Whisper to Full, flagged in the risk log.

**A separate reviewer**, which had not built the round, read the change, ran the suites and probes in a copy, and broke the copy on purpose; it changed nothing in the repository. Its eleven findings, all fixed:

| # | The reviewer's finding | Fix | Held by |
|---|---|---|---|
| 1 | **A test edit switched off three checks**: a comment put in the middle of a line in the extension's safety test hid the checks of three settings that fall back to their usual value | The comment moved to the end of the line; every line added this round was searched for the same mistake (none) | The removed allow-list put back: caught |
| 2 | App: the move from Whisper to Full, and its sentence, came back at every start until something saved the settings; and the event log called it a file that "could not be used" | Moved settings are written back once; the event log writes nothing for a planned move (its sentences are shared with the simulation) | Rule test; the fault put back: caught |
| 3 | Simulation: a brief left waiting on a closed card was asked for by a later peek when E was pressed | What waits is forgotten when the card closes or opens as a peek; a peek never asks | Simulation C11b; the fault put back: caught |
| 4 | App: after a peek's "Full brief" the calendar never appeared (the simulation shows it) | The calendar's "has two dates" no longer depends on how the card started | Window test; the fault put back: caught |
| 5 | **An agent set to run by itself was still asked while the card showed the answer only**, in both programs; the wording "when a full card opens" no longer told the truth | Such agents wait for the card to be opened in full (More, E, Full brief or a note), like the brief; the wording is now "when a card is opened in full" in Settings, Connections and the documents | Simulation t18; app window test with a counting connection; the fault put back: caught |
| 6 | App: Tab reached Today's *Show all* before the rows above it | It comes after the rows, as it is drawn | Window test; simulation T5a; the fault put back: caught |
| 7 | After More the keyboard landed on a button that still said "Expand card" | The button says "Collapse card" or "Expand card", and in the simulation carries `aria-expanded` | Window test; simulation C11c, C11d; the fault put back: caught |
| 8 | The demo guide, the trial sheet and the start-here text still described the invite and the Watch list item; the middle-click hint did not say that it asks first | All updated; the hint says so in both programs | Simulation C11e |
| 9 | App: in Full mode, a middle-click invite stayed up after the shortcut opened the card | A card that opens closes a waiting invite, as in the simulation | Source check (it needs the whole app running) |
| 10 | A deliberate Whisper could still be moved without a word: in the app when the file could not be read and the last good copy was older; in the extension always | The app says what moving the last good copy changed, and the last good copy is written after the move; the extension says it once on its Settings page and saves the move | Rule tests in both |
| 11 | Gaps in the tests: Ahead not checked under More; nothing for the brief after a peek, agents, the repeated sentence or the extension's saved move; the tray wiring only read as text | Each added except the tray wiring, which needs the whole app on Windows | As above |

**The faults put back on purpose.** Before the review, five faults put into the app (answer-first switched off, the brief asked at once, the middle-click ignored, the move skipped, Show all always offered) and four into the simulation (Whisper as default, More-only parts drawn, the tray opening the lookup box, the watch list without the keyboard) were each caught. After the fixes, the five fixes in the app were each undone in a copy and each was caught; the simulation's brief fix likewise.

{{COUNTS}}

**Not proven by this round**, and what to do about each: nothing ran on Windows (no Windows build since Nov 12; this session may not start the workflow), so the tray icon's single click, the card's real height on a 1366 × 768 screen, the settings move on a real profile and Narrator on More and Show all are unknown: a Windows run and a keyboard and Narrator walk. The extension ran in Chromium, not Edge. Going back to an older version and forward again would move a Whisper chosen in between once more; that is not handled. The card is not yet at the 420 px once suggested (513 to 601 px at standard text in the simulation).

### Defects caught in round 34

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R355 | The tray icon's spoken label said "Click for lookup" after its first refresh (simulation) | The new round 34 suite (T1) | Fixed |
| R356 | A change of text size with a card open let the card run past the taskbar (simulation) | An older suite's check (round 18, E7) once the card became short | Fixed: the card is fitted again |
| R357 | The watch list opened from Today did not take the keyboard; Escape closed Today (simulation) | An older suite (the extension pages, t7) | Fixed: it opens like every window, above Today |
| R358 | An empty calendar would have shown after More on a card without a forecast (app) | The stricter calendar test written while updating it | Fixed |
| R359 | A peek's "Full brief" led to a card whose brief was hidden | The builder, reading the change | Fixed in both programs |
| R360 | With an AI connection on, the brief would have been asked for although it is not shown | The builder, reading the change against the data rules | Fixed in both programs |
| R361 to R371 | The eleven findings of the separate review, in the table above | The separate review | Fixed |
| R372 | Fourteen older simulation suites, eight older app tests and two extension tests still described the previous defaults (Whisper first, everything on the card, the Watch list tray item, round 33 as the newest) | The first full runs after the change | Each updated to the new behaviour with its intent kept; where a check was about the whisper path it now chooses Whisper |
| R373 | Two checks still assumed what the review's fixes changed: an app test opened cards without More and waited for an agent set to run by itself; a simulation check waited for a card opened by "Full brief" to hide itself, which an open drawer never does | The first counted pass | Both updated with their intent kept (More is pressed; the card is folded before the auto-hide wait); that pass was thrown away and both passes started again on the committed code |
| R374 | **A failed check that the suite runner did not count as a failed suite**: twelve older simulation suites print FAIL but never set their exit code, so "46 of 46 suites exit 0" was shown while one check (the AI brief's label, which now waits for More) had failed. The totals line did show it | The builder, reading why the totals said 2 failed while every suite said 0 | The check presses More first; the runner now counts a suite that prints a failed check as failed. Both passes started again |

CI: **no Windows run has started for this round's commits either.** The last Windows run is still run 44, on the round 24 code, from Nov 12. The owner can start the workflow from the repository's Actions tab.

## Still open

GDUFA III tier table (needs the commitment-letter text and the dual-source rule); a real-keyboard check of rebound shortcuts on
non-US layouts (rebinding is built); UI Automation capture; code signing; the live Claude call; **the live Elsa call (the two
translations in `ElsaMapping.cs`, once the API is available)**; a 508 pass with a screen reader; a real-email-client
check that compact boxes do not disturb typing; a real-mouse hover check for auto-hide on Windows; **Alert Hub, Patent Analysis and Triaging Agent details from DPM IT and a first real call to each**; a delivered-keystroke check of a person's own shortcut on Windows; a shared supervisor list (needs an approved place). **From rounds 18 to 22: the rest of the port of the simulation's design to the Windows app** (notices near the clock and division news; card details, text size and spacing, recents, One Bar and Today were ported in round 33, the proofreading rules, the recall row and the one question in round 25) **and all of it to the extension**; the new Review window seen and used on a real Windows desktop; an ethics and ownership answer on the project, and whether a public repository and an open link suit the owner's plans (the owner's decisions, raised in round 25); where the notes file should live and whether records rules cover it (for IT); the blur of the glass looks on a real Windows desktop; a Flag column and a mail rule for the tray dot in the app; the tray picture seen in a real Windows notification area. **From round 26:** a real report compared, and a workbook saved by Excel itself; a copied report pasted into Excel; the invitation opened in Outlook; the narrative and the reading prompts sent to an approved model; a Windows run of this round's code; a review by the agency's security office; whether leadership wants a shared usage figure, and if so where it may live and how consent is asked.
**From round 27:** a Windows run of rounds 25 to 27 (none has started since Nov 12); a real Word file, a real report and a real sheet in the new windows; one of the person's prompts on two real files with a real model, and the fence tried against text written to steer a model; the office's policy on AI-written content, to replace the general line; whether PDF should be read, which needs an outside component; the hand-off page tried by two colleagues.
`npm run test` for the whole repository stops in this authoring environment at an unrelated test that needs a
newer `git` (`--ref-format=reftable`); `npm run validate` and `npm run security:docs` pass.
