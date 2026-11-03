# ANDA Hover Companion

A prototype for FDA DPM review project managers (RPMs): select an ANDA number anywhere and get a small card with the goal date against the forecast,
what needs attention, a WATCH line (weekends, federal holidays, passed goals), the status line to copy, and a check of a draft message against the record.

It comes in two parts that work alone or together:

- **Windows app** (`app/`): a tray app (.NET 8, Avalonia). Hotkeys, card, lookup, draft check, watch list, "My data" (Excel or CSV portfolio, notes folder), writing actions for an approved provider only.
- **Edge extension** (`extension/`): works in Edge and other Chromium browsers. Runs only when asked, on the one tab, with no network access. With the app connected (native messaging) it uses the app's portfolio and rules.

All data in the demos is fictional. This is a prototype, not an FDA product. It has not been used by a real RPM, on real data, in Edge itself, or on a managed FDA computer.
Read `VERIFICATION.md` for what has and has not been proven.

## What each thing is for

| Path | What it is | Read or use it when |
|---|---|---|
| `PROPOSAL.md` | The pilot proposal: problem, scope, what is asked of the division | Deciding whether to run a pilot |
| `ROADMAP.md` | Tester feedback, each request's honest status, decisions made on the owner's behalf, the five-minute Drugs@FDA check | Planning the next step |
| `DESIGN.md` | How the card, consent models (whisper, peek first, always), looks and keyboard behaviour are meant to work | Changing the interface |
| `SECURITY.md` | What the app and the extension can and cannot read or send, row by row | Talking to IT or security |
| `VERIFICATION.md` | Every claim with its evidence and its limit; every defect found; what each round does not prove | Before repeating any claim to someone else |
| `ELSA-INTEGRATION.md` | How the writing actions would connect to the agency's approved model, and what is still unknown | When Elsa API access exists |
| `app/` | The Windows app source, its tests (`AndaCompanion.Tests`) and a preview project | Building or testing the app (`app/README.md`) |
| `extension/` | The Edge extension source, unit tests, browser tests and the golden file the app wrote | Loading it unpacked in Edge (`extension/README.md`) |
| `extension/tests/golden.json` | Cases written by the app's own code that the extension must reproduce exactly | Keeping the two implementations in agreement |
| `mockup/windows-simulation.html` | A browser simulation of the whole experience (card, right-click, watch list, draft check, guided steps). Calls no model, installs nothing | Showing the idea on a managed computer |
| `mockup/appearance-lab.html` | The 15 light looks side by side | Choosing a look |
| `demo/` | `START-HERE.txt`, `DEMO.md`, a launcher and a sample memo for the demo build | Running the demo build on a machine IT has allowed |

## Run the checks

```bash
dotnet test app/AndaCompanion.Tests                                  # app: 446 pass on Linux, plus Windows-only tests on Windows
node --test extension/tests/unit/*.test.mjs                          # extension rules, safety, manifest, service
NODE_PATH=$(npm root -g) node extension/tests/e2e/standalone.cjs     # real Chromium, extension alone (needs playwright)
dotnet build app/AndaCompanion && NODE_PATH=$(npm root -g) node extension/tests/e2e/native.cjs   # extension with the real host
```

The workflow `.github/workflows/anda-companion-windows.yml` builds the app on Windows and Linux, runs the tests, runs the app's on-device self-test on Windows and publishes the demo build.
