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

## Still open

GDUFA III tier table (needs the commitment-letter text and the dual-source rule); a real-keyboard check of rebound shortcuts on
non-US layouts (rebinding is built); UI Automation capture; code signing; the live Claude call; **the live Elsa call (the two
translations in `ElsaMapping.cs`, once the API is available)**; a 508 pass with a screen reader; a real-email-client
check that compact boxes do not disturb typing; a real-mouse hover check for auto-hide on Windows; **Alert Hub, Patent Analysis and Triaging Agent details from DPM IT and a first real call to each**; a delivered-keystroke check of a person's own shortcut on Windows; a shared supervisor list (needs an approved place).
`npm run test` for the whole repository stops in this authoring environment at an unrelated test that needs a
newer `git` (`--ref-format=reftable`); `npm run validate` and `npm run security:docs` pass.
