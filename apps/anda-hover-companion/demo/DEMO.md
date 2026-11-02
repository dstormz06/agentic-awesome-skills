# Live demo guide — ANDA Hover Companion

Fictional data only. A concept prototype, not an FDA product. About five minutes.

> **If you work at an organization that manages your computer (for example, FDA staff): do not download or run the
> Windows build on that computer until your IT or security team has approved it.** It is an unsigned program that sends
> keystrokes and reads the clipboard, so it needs review first (for FDA staff, the Product Catalog and security review).
> Until then, use the **browser simulation** or the deck on a device you are allowed to use. The Windows build is for
> a personal or approved device, with fictional data only.

## Pick a path

| Path | What you need | Use it when |
|---|---|---|
| **Windows build** | The `anda-companion-demo-win-x64` zip from the latest passing run of the *ANDA Companion (Windows)* workflow, on any Windows 10/11 64-bit PC | You want the real thing, with real hotkeys |
| **Edge extension** | Edge, on a computer where you are allowed to add an extension, and the `extension` folder of this repository (see `extension/README.md`: Load unpacked, then right-click a selected ANDA number) | You want the right-click menu and the draft check in your browser, with the demo records or your own spreadsheet. Not on a managed computer before IT approves it |
| **Browser simulation** | A browser. Nothing installs. Pick which look-alike apps to show (mail, chat, browser, terminal) | Your computer blocks unsigned programs, you are on a managed computer awaiting approval, or you are on a phone or tablet |

The simulation is a faithful port of the app's logic (recognizer, placement, consent flow, offline
brief, status line) inside a mock desktop. It is labelled as a simulation on screen. The hotkey and
clipboard hand-off are simulated there; in the Windows build they are real.

## Two-minute set-up (Windows build)

1. Right-click the zip → **Extract All**. Do not run from inside the zip.
2. Check the download (optional): `Get-FileHash .\AndaCompanion.exe -Algorithm SHA256` must match `SHA256SUMS.txt`.
3. Double-click **Start-Demo.bat**. The build is not code-signed, so Windows may say "Windows protected your PC".
   That is expected for a prototype. **More info → Run anyway**, if your organization allows it.
4. Find the tray icon near the clock. Windows 11 hides new icons under **^**. Drag it onto the taskbar.
5. Right-click the icon. The menu's last grey line says which hotkeys are live. All three should show a tick.
6. Optional, 1 minute: `AndaCompanion.exe --selftest report` from a Command Prompt in the folder, then open
   `report\report.md`. Do not touch the keyboard or mouse while it runs.

## The five-minute script

| Time | Do | Say |
|---|---|---|
| 0:00 | In the memo, double-click **216482**. Press **Ctrl+Alt+A**. | "I select a number in any window and press a hotkey. The default is a small invite, but this ANDA is at risk, so a small peek opens by itself. It still started from my own selection and key." Click **Full brief ↓**. |
| 0:30 | Point at the clock block. | "The forecast is the headline: goal Oct 3, forecast Oct 17, 14 days late, moderate confidence. These numbers come from the data layer. The AI never writes a date." |
| 1:00 | Point at NEEDS YOU, LATEST, AHEAD, then WATCH. | "One thing that needs me, what just happened, and what lands on me next. WATCH: the goal date is a Saturday, and it shows the Friday and Monday either side. It never says how a goal-date rule applies; that is still my call." |
| 1:30 | Point at the brief label. | "No Claude access yet, so this is labelled OFFLINE BRIEF: assembled from the record, no model. When access is approved it says generated, verify before acting." |
| 1:45 | Click **Copy status**, paste into the memo. | "The sentence I retype a dozen times a week, in one click." |
| 2:15 | Select **NDA 021234**, press Ctrl+Alt+A. | "Same six digits, different application type. It declines to guess and opens the lookup box. Showing the wrong card is worse than showing none." |
| 2:45 | Copy any text, trigger a card, then paste. | "It read my selection by copying it, then put my clipboard back exactly as it was." |
| 3:15 | Tray → Settings. Pick **L · Graphite Strip**, select a number again and press Ctrl+Alt+A: a small strip appears and does not take focus. Click it, or press Ctrl+Alt+A again, to open the full card. Then switch back to the default **K**. Show Auto-hide and the transparency slider. | "Fifteen looks, one anatomy; K is the default. The compact boxes stay out of the way of an open email, make no AI call, and never steal the keyboard. A card hides itself when idle but never while I am reading it. Three consent models, so my team chooses how much happens without a second act." |
| 3:45 | Select the draft reply in the memo and press **Ctrl+Alt+D**. | "It checks my draft against the record, with no AI: a wrong goal date, 'on track' against a late forecast, a number it does not know. It says what it checked and what it did not." (The memo has a sample draft paragraph with three deliberate mistakes.) |
| 4:00 | Tray → **Watch list…**. | "Every ANDA in my data that has something worth a look, worst first. The same rules as the card, run when I open it. Nothing runs in the background." |
| 4:15 | Open the **browser simulation**, select 216482 in the Browser window and right-click. | "This is the Edge extension. It runs only when I ask: right-click, three shortcuts, or the toolbar button. On its own it uses the demo records or a spreadsheet I choose; with the desktop app connected it uses the app's data and settings. It cannot see any other tab, and it makes no network requests." |
| 4:30 | Tray → Prompt library. Pick a lens. Press **Test** in Settings. | "Our own prompts steer emphasis but can never override grounding. Test shows offline today. The day access is approved: set the key (Claude) or follow the Elsa checklist, press Test, done. The Elsa wiring is already in; only its two request-and-reply translations wait for the API." |
| 4:45 | Stop. | The ask: API access through an agency-approved channel. See `PROPOSAL.md`. |

## If someone asks

| Question | Honest answer |
|---|---|
| Is this connected to real ANDA data? | No. Every record is fictional. A real data layer reads only a sanctioned extract (see `DESIGN.md` §6). |
| Is Elsa integrated? | It is wired as a placeholder, not integrated. Selection, configuration, the https-only guard, timeout, fallback and the Settings status line are built and tested; the two translations that depend on Elsa's API are not written because that API is not known and is not guessed. See `ELSA-INTEGRATION.md`. |
| Does it call Claude today? | No. It is offline by default. The Claude path uses the official SDK and the `claude-opus-4-8` model ID, which matches Anthropic's current model list, but it has **not** been exercised against the live API. |
| Has it run on Windows? | Yes, in an automated run on a Windows build-26100 machine (the Windows 11 24H2 / Server 2025 generation) at 1024×768: hotkeys registered and delivered, Select & Peek captured a real selection and restored the clipboard, Segoe UI rendered, cards stayed on screen. See `VERIFICATION.md` round 5 for the run and its limits. |
| Can I right-click a selected number? | In **Edge**, yes, with the extension (peek, check as a draft, scan the page). Windows does not let one program add a right-click item to other programs, so inside desktop Outlook or Teams use the hotkey instead. The extension was tested in Chromium, not yet in Edge itself, and not on Outlook on the web. |
| Does it read my mailbox? | No. The extension reads only what you select, the message you are writing when you ask for a draft check, or the visible text of the page when you ask it to scan. There is no mailbox search; that would need an admin-approved Microsoft Graph registration and is not built. |
| Will it work on my managed workstation? | Unknown, and do not try it there before IT approves it. Application allow-listing, endpoint protection and clipboard-monitoring tools can block an unsigned program or its keystroke injection. Run the self-test there first. |
| Does copying touch my clipboard history or DLP? | Yes, briefly. The selection is on the clipboard for a moment, so Win+V clipboard history and any clipboard-monitoring tool can see it. Your own clipboard is restored afterwards. Turn **Select & Peek** off in Settings to avoid this; the lookup box needs no clipboard. |
| What about non-US keyboards? | Ctrl+Alt is also AltGr on several layouts (for example German and French), so Ctrl+Alt+A may type a character. The tray icon and Ctrl+Alt+Space path avoid selection capture. The three shortcuts are rebindable in Settings. |
| Why does nothing happen in my admin window? | Windows blocks injected keystrokes into elevated windows. The app then reports nothing selected and opens the lookup box. |

## Troubleshooting

- **No tray icon**: look under **^**. If you ran the launcher twice you will have seen a message saying it is already running.
- **Ctrl+Alt+A does nothing**: another program owns it. The app shows a notice at start-up and the tray line says so.
  Use Ctrl+Alt+Space or the tray icon.
- **Crash**: a `crash.log` is written to the `data` folder next to the launcher.
- **Remove it**: Exit from the tray menu, then delete the folder. Nothing is installed.
