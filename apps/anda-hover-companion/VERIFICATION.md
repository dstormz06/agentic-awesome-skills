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
Run 8 (commit `f8c1c49`, the **final** commit: it changes only the quoted box sizes in a tagline string, DESIGN and the lab) is
green on both jobs; its self-test exits non-zero on any FAIL, so a green job means zero FAIL. The counts above were read from run 7's log,
not re-read from run 8's.

### Defects caught in round 6

| # | Finding | Found by | Disposition |
|---|---|---|---|
| R15 | **`ElsaSettings` is a record, and a record prints every member, so any log line or exception formatting it would have printed the token** | A test written to assert the token never appears | `ToString()` overridden to redact; the test now guards it |
| R16 | A dashed `Line` shape in the ticket forced a 100 px row height, so the "64 px" ticket was 117 px tall | Headless size test | Replaced with a purpose-built control that takes its row's height |
| R17 | Compact boxes took keyboard focus when shown: someone typing an email would have kept typing into the box, and a stray Enter or Space would have expanded it | Design review of the interaction | Never activated; expansion needs a click or the hotkey again on the same number; a test asserts it |
| R18 | Sizes were quoted from Linux renders (fallback font); Segoe UI measured wider (strip 81 not 78, tile 145 not 139) | Windows self-test | Taglines, DESIGN and the lab quote the Windows ranges |
| R19 | The tray icon was drawn in the look's accent; with K that is graphite, a dark tile on a dark Windows taskbar | Review of the K default | Tray icon is a fixed federal blue in every look |
| R20 | The compact box's "needs you" label wrapped in the lab and the simulation | Browser render | Stub widened and set to no-wrap in all three renderers |

### Housekeeping in round 6

The original design studio `mockup/anda-card.html` (five looks, A default) was **retired**, because it had fallen behind the app and
the other two mockups. References in `DESIGN.md` and `app/README.md` now point at the lab and the simulation. Earlier rounds of this
report that mention the studio describe a file that is no longer in the tree (it is in git history).

### Things the owner should decide, not assume

- **K's accent is graphite (`#33373C`).** The surface is light and the look is the one picked from the lab, but the primary
  buttons, checkboxes and the progress line are a dark ink, not a colour. If "no dark colours" is meant to include that,
  say so and the accent can move to a mid slate; nothing else changes.
- **Auto-hide defaults to 20 s.** A card opens next to the pointer, not under it, so a reader who keeps the mouse still
  and does not touch the keyboard will see it hide. Off is one click in Settings; 40 s is the gentler choice.
- **Existing installs keep their saved look.** Only new installs get K.

## Still open

GDUFA III tier table (needs the commitment-letter text and the dual-source rule); a rebindable hotkey for
non-US layouts; UI Automation capture; code signing; the live Claude call; **the live Elsa call (the two
translations in `ElsaMapping.cs`, once the API is available)**; a 508 pass with a screen reader; a real-email-client
check that compact boxes do not disturb typing; a real-mouse hover check for auto-hide on Windows.
`npm run test` for the whole repository stops in this authoring environment at an unrelated test that needs a
newer `git` (`--ref-format=reftable`); `npm run validate` and `npm run security:docs` pass.
