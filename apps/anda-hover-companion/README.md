# ANDA Companion (Notula)

A prototype for FDA DPM review project managers (RPMs). Select an ANDA number anywhere and a small card shows the goal date
against the forecast, what needs attention, a WATCH line (weekends, federal holidays, passed goals), the PSG and RLD dates, and
a status line to copy. It can also review a draft message against the record, and answer a question about any selected text or spreadsheet cells
(quick checks run on your PC; a prompt of your own is shown to you first and sent only if you press Send).

It comes in two parts that work alone or together:

- **Windows app** (`app/`): a tray app (.NET 8, Avalonia) with shortcuts, the card, a lookup box, the draft check, a watch list, your own
  Excel or CSV portfolio and notes folder, a live **ANDA to-do list**, a separate **My to-do list** for your daily workload, ANDA related or not (rough highlighted notes become suggested items, read by plain rules with no AI), **your own actions** (a prompt, an AI request or an agent request on a
  shortcut you choose), a connector for **agents such as Alert Hub, Patent Analysis and Triaging Agent**, and writing actions that work only through an
  approved provider. Everything you may want to change (keys, files, connection, model name, your own API, your own agents and actions,
  writing style, look, optional Outlook) is in **Settings**, which arranges itself (it opens on what needs a look and keeps the rest collapsed), with no rebuild.
- **Edge extension** (`extension/`): works in Edge and other Chromium browsers. It runs only when asked, on the one tab, with no network
  access. With the app connected (native messaging) it uses the app's portfolio and rules.

**Where the newer design stands (rounds 18 and 19).** New work is built and published first in the browser simulation, for the owner to review, and ported to the Windows app
afterwards. **In the Windows app today:** the Loupe icon, the default card **T · Pocket Plain** (one answer first, one main action), the tray icon's amber dot, shown only while something needs the person today (round 22), the
N and A number prefixes (N210793 is an NDA and is refused; A210793 is read as ANDA 210793) and the new default shortcuts. **Only in the simulation so far:** the looks U and V, One Bar,
Today, recent and kept ANDAs, spacing and text size (round 18), card details with Copy, notices near the clock with division news, and one question before sending (round 19), and,
from rounds 20 to 22, **W · Graphite Gauge as the default look** (the glass sheet in quiet graphite with a gauge beside the answer; V · Glass Sheet was the default of rounds 20 and 21), Enter to add a note, a settable *Open ANDA page* button, a live clock, grammar rules and a marked draft in Review, quick notes typed on the card, Proofread and Fact-check that work with no AI, the notes and open items shown for recall under
a review, and a desktop that looks like Windows 11. The card's starting transparency is 90% in both (it was 92%).
`DESIGN.md` §11b to §11f describe them; `mockup/proofread-golden.json` holds the cases a port of the proofreader must reproduce; `demo/TRIAL-SHEET.md` is the five-task way to judge whether the design helps in daily use.

All data in the demos is fictional. This is a prototype, not an FDA product. It has not been used by a real RPM, on real data, in Edge
itself, or on a managed FDA computer. Please read `VERIFICATION.md` for what has and has not been proven.

## What each thing is for

| Path | What it is | Read or use it when |
|---|---|---|
| `PROPOSAL.md` | The pilot proposal: problem, scope, what is asked of the division | Deciding whether to run a pilot |
| `ROADMAP.md` | Tester feedback, each request's honest status, decisions made on the owner's behalf, the five-minute Drugs@FDA check | Planning the next step |
| `DESIGN.md` | How the card, consent models (whisper, peek first, always), looks and keyboard behavior are meant to work | Changing the interface |
| `SECURITY.md` | What the app and the extension can and cannot read or send, row by row | Talking to IT or security |
| `VERIFICATION.md` | Every claim with its evidence and its limit; every defect found; what each round does not prove | Before repeating any claim to someone else |
| `ELSA-INTEGRATION.md` | How the writing actions connect to the agency's approved model (today through Settings, My API), and what is still unknown | When Elsa API access exists |
| `app/` | The Windows app source, its tests (`AndaCompanion.Tests`) and a preview project | Building or testing the app (`app/README.md`) |
| `extension/` | The Edge extension source, unit tests, browser tests and the golden file the app wrote | Loading it unpacked in Edge (`extension/README.md`) |
| `extension/tests/golden.json` | Cases written by the app's own code that the extension must reproduce exactly | Keeping the two implementations in agreement |
| `mockup/windows-simulation.html` | A browser simulation of the whole experience (card, right-click, watch list, ANDA to-do list, My to-do list and Notes to my to-do list, your own actions and agents, draft check, Settings that arrange themselves, guided steps). Calls no model and no agent, contacts no server, installs nothing | Showing the idea on a managed computer |
| `mockup/appearance-lab.html` | The 22 light looks side by side (T, U and V are new in round 18) | Choosing a look |
| `demo/` | `START-HERE.txt`, `DEMO.md`, a launcher and a sample memo for the demo build | Running the demo build on a machine IT has allowed |

## Run the checks

```bash
dotnet test app/AndaCompanion.Tests                                  # app: 933 pass and 11 are skipped on Linux; the Windows-only tests run on Windows
node --test extension/tests/unit/*.test.mjs                          # extension rules, safety, manifest, service
NODE_PATH=$(npm root -g) node extension/tests/e2e/standalone.cjs     # real Chromium, extension alone (needs playwright)
dotnet build app/AndaCompanion && NODE_PATH=$(npm root -g) node extension/tests/e2e/native.cjs   # extension with the real host
```

The workflow `.github/workflows/anda-companion-windows.yml` builds the app on Windows and Linux, runs the tests, runs the app's on-device self-test on Windows and publishes the demo build.
