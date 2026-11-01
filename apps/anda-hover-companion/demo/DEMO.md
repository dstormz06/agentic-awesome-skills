# Live demo guide — ANDA Hover Companion

Fictional data only. A concept prototype, not an FDA product. About five minutes.

## Pick a path

| Path | What you need | Use it when |
|---|---|---|
| **Windows build** | The `anda-companion-demo-win-x64` zip from the latest passing run of the *ANDA Companion (Windows)* workflow, on any Windows 10/11 64-bit PC | You want the real thing, with real hotkeys |
| **Browser simulation** | A browser. Nothing installs | Your computer blocks unsigned programs, or you are on a phone or tablet |

The simulation is a faithful port of the app's logic (recognizer, placement, consent flow, offline
brief, status line) inside a mock desktop. It is labelled as a simulation on screen. The hotkey and
clipboard hand-off are simulated there; in the Windows build they are real.

## Two-minute set-up (Windows build)

1. Right-click the zip → **Extract All**. Do not run from inside the zip.
2. Check the download (optional): `Get-FileHash .\AndaCompanion.exe -Algorithm SHA256` must match `SHA256SUMS.txt`.
3. Double-click **Start-Demo.bat**. The build is not code-signed, so Windows may say "Windows protected your PC".
   That is expected for a prototype. **More info → Run anyway**, if your organization allows it.
4. Find the tray icon near the clock. Windows 11 hides new icons under **^**. Drag it onto the taskbar.
5. Right-click the icon. The menu's last grey line says which hotkeys are live. Both should show a tick.
6. Optional, 1 minute: `AndaCompanion.exe --selftest report` from a Command Prompt in the folder, then open
   `report\report.md`. Do not touch the keyboard or mouse while it runs.

## The five-minute script

| Time | Do | Say |
|---|---|---|
| 0:00 | In the memo, double-click **216482**. Press **Ctrl+Alt+A**. | "I select a number in any window and press a hotkey. Nothing ever opens by itself." |
| 0:30 | Point at the clock block. | "The forecast is the headline: goal Oct 3, forecast Oct 17, 14 days late, moderate confidence. These numbers come from the data layer. The AI never writes a date." |
| 1:00 | Point at NEEDS YOU, LATEST, AHEAD. | "One thing that needs me, what just happened, and what lands on me next." |
| 1:30 | Point at the brief label. | "No Claude access yet, so this is labelled OFFLINE BRIEF: assembled from the record, no model. When access is approved it says generated, verify before acting." |
| 1:45 | Click **Copy status**, paste into the memo. | "The sentence I retype a dozen times a week, in one click." |
| 2:15 | Select **NDA 021234**, press Ctrl+Alt+A. | "Same six digits, different application type. It declines to guess and opens the lookup box. Showing the wrong card is worse than showing none." |
| 2:45 | Copy any text, trigger a card, then paste. | "It read my selection by copying it, then put my clipboard back exactly as it was." |
| 3:15 | Tray → Settings. Switch appearance and consent model. Drag the transparency slider. | "Five looks, one anatomy. Three consent models, so my team chooses how much happens without a second act." |
| 4:00 | Tray → Prompt library. Pick a lens. Press **Test** in Settings. | "Our own prompts steer emphasis but can never override grounding. Test shows offline today. The day access is approved: set the key, press Test, done." |
| 4:45 | Stop. | The ask: API access through an agency-approved channel. See `PROPOSAL.md`. |

## If someone asks

| Question | Honest answer |
|---|---|
| Is this connected to real ANDA data? | No. Every record is fictional. A real data layer reads only a sanctioned extract (see `DESIGN.md` §6). |
| Does it call Claude today? | No. It is offline by default. The Claude path uses the official SDK and the `claude-opus-4-8` model ID, which matches Anthropic's current model list, but it has **not** been exercised against the live API. |
| Has it run on Windows? | Yes, in an automated run on a Windows build-26100 machine (the Windows 11 24H2 / Server 2025 generation) at 1024×768: hotkeys registered and delivered, Select & Peek captured a real selection and restored the clipboard, Segoe UI rendered, cards stayed on screen. See `VERIFICATION.md` round 5 for the run and its limits. |
| Will it work on my managed workstation? | Unknown. Application allow-listing, endpoint protection and clipboard-monitoring tools can block an unsigned program or its keystroke injection. Run the self-test there first. |
| Does copying touch my clipboard history or DLP? | Yes, briefly. The selection is on the clipboard for a moment, so Win+V clipboard history and any clipboard-monitoring tool can see it. Your own clipboard is restored afterwards. Turn **Select & Peek** off in Settings to avoid this; the lookup box needs no clipboard. |
| What about non-US keyboards? | Ctrl+Alt is also AltGr on several layouts (for example German and French), so Ctrl+Alt+A may type a character. The tray icon and Ctrl+Alt+Space path avoid selection capture. A rebindable hotkey is on the list. |
| Why does nothing happen in my admin window? | Windows blocks injected keystrokes into elevated windows. The app then reports nothing selected and opens the lookup box. |

## Troubleshooting

- **No tray icon**: look under **^**. If you ran the launcher twice you will have seen a message saying it is already running.
- **Ctrl+Alt+A does nothing**: another program owns it. The app shows a notice at start-up and the tray line says so.
  Use Ctrl+Alt+Space or the tray icon.
- **Crash**: a `crash.log` is written to the `data` folder next to the launcher.
- **Remove it**: Exit from the tray menu, then delete the folder. Nothing is installed.
