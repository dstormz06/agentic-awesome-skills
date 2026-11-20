# Live demo guide: ANDA Companion (Notula)

Thank you for trying it. Fictional data only. A concept prototype, not an FDA product. About five minutes.

> **If your organization manages your computer (for example, FDA staff), please do not download or run the Windows
> build on it until your IT or security team has approved it.** It is an unsigned program that sends keystrokes and
> reads the clipboard, so it needs review first. For FDA staff that is the Product Catalog and security review.
> Until then, use the **browser simulation** or the deck on a device you are allowed to use. The Windows build is
> for a personal or approved device, with fictional data only.

> **What this Windows build has of the newer design.** The Loupe icon, eight looks with **X · Graphite Calendar** as the default (one answer, the days to the goal date beside it, the calendar under it, and one main action: Add a note, or Enter; S copies the status line), a dot or a number on
> the tray icon only while something in your own sheet needs you today (the sample data never lights it), and the shortcuts **Ctrl+Alt+Shift+R** (Review a draft: Proofread and Fact-check by fixed rules with no AI, one question before you send, and your notes for recall) and **Ctrl+Alt+Shift+L** (My to-do list). One Bar, Today, recent ANDAs, card details
> with Copy and notices near the clock are shown in the **browser simulation** only. To judge the design for daily use, follow `TRIAL-SHEET.md`.

## Pick a path

| Path | What you need | Use it when |
|---|---|---|
| **Windows build** | The `anda-companion-demo-win-x64` zip from the latest passing run of the *ANDA Companion (Windows)* workflow, on any Windows 10/11 64-bit PC | You want the real thing, with real shortcuts |
| **Edge extension** | Edge on a computer where you may add an extension, and the `extension` folder of this repository (see `extension/README.md`) | You want the right-click menu and the draft check in your browser. Not on a managed computer before IT approves it |
| **Browser simulation** | A browser. Nothing installs. You choose which look-alike apps to show (mail, chat, browser, terminal) | Your computer blocks unsigned programs, it is managed and awaiting approval, or you are on a phone or tablet |

The simulation ports the app's logic (recognizer, placement, consent flow, offline brief, status line) into a mock
desktop, and says so on screen. The shortcut, clipboard and file pickers are simulated there. In the Windows build they are real.

## Set up the Windows build (2 minutes)

1. Right-click the zip and choose **Extract All**. Do not run it from inside the zip.
2. Optional: check the download. `Get-FileHash .\AndaCompanion.exe -Algorithm SHA256` must match `SHA256SUMS.txt`.
3. Double-click **AndaCompanion.exe**, then open **sample-memo.txt** in Notepad. (There is no launcher script: the demo keeps its settings in the `data` folder beside the program.) Windows may say "Windows protected your PC", because the build is not code-signed.
   That is expected. If your organization allows it, choose **More info**, then **Run anyway**.
4. Find the tray icon near the clock. Windows 11 hides new icons under **^**, so drag it onto the taskbar.
5. Right-click the icon. The last gray line of the menu says which shortcuts are live. All four should show a tick.
6. Optional, 1 minute: run `AndaCompanion.exe --selftest report` in a Command Prompt in the folder, then open
   `report\report.md`. Please do not touch the keyboard or mouse while it runs.

## The five-minute script

| Time | Do | Say |
|---|---|---|
| 0:00 | In the memo, double-click **216482** and press **Ctrl+Alt+A**. | "I select a number in any window and press a key, and the answer is there: fourteen days late, what needs me, and the one thing worth a look. It fits my laptop's screen." Click **More** (or press **E**). |
| 0:30 | Point at the clock block. | "The forecast is the headline: goal Oct 3, forecast Oct 17, 14 days late, moderate confidence. These numbers come from the data layer. The AI never writes a date." |
| 1:00 | Point at NEEDS YOU, LATEST, AHEAD, then WATCH. | "What needs me, what just happened, and what lands on me next. The goal date is a Saturday, so it shows the Friday and Monday either side. It never says how a goal-date rule applies. That stays my call." |
| 1:30 | Point at PSG and RLD LABEL, then the gray pill and the brief label. | "Two reference dates, always: a date and where it came from, or why there is none. The pill says no API is connected, and it says Connected only after a real request has succeeded. The brief is labeled OFFLINE: assembled from the record, no model." |
| 1:50 | Click **Copy status** and paste it into the memo. | "The sentence I retype a dozen times a week, in one click." |
| 2:15 | Select **NDA 021234** and press Ctrl+Alt+A. | "The same six digits but a different application type. It declines to guess and opens the lookup box. A wrong card is worse than none." |
| 2:45 | Copy any text, open a card, then paste. | "It read my selection by copying it, then put my clipboard back exactly as it was." |
| 3:15 | *Optional.* Tray, then Settings, **Look and feel**. Pick **L · Graphite Strip**, select a number and press Ctrl+Alt+A. Click the strip to open the full card. Switch back to **X**. | "Eight looks. The small boxes stay out of the way of an open email, make no AI call and never take the keyboard. A card hides itself after 10 seconds idle, but never while I am reading it." |
| 3:45 | Select the draft reply in the memo and press **Ctrl+Alt+Shift+R**. | "It checks my draft against the record, with no AI: a wrong goal date, 'on track' against a late forecast, a number it does not know. It says what it checked and what it did not." (The memo has a draft with three deliberate mistakes.) |
| 3:55 | Select the **spreadsheet cells** in the memo and press **Ctrl+Alt+Shift+P**. Press the suggested **Find duplicates**, then **Compare columns**. | "I select cells and ask a question about them. These checks run on my PC with fixed rules: no AI, nothing sent. A prompt of my own is filled in and shown to me first, and goes out only if I press Send. It cannot tell me what a repeat means. That stays mine." |
| 3:58 | *Optional.* Right-click the tray icon, **More**, then **Compare two files…**, then **Use the sample files**. Point at the summary, the bar and the table. Change **Match rows by** once. | "Two versions of the same report. It matches the rows by the ANDA number and tells me what changed: three rows, one new, one gone, a forecast that moved 14 days. Fixed rules on my PC, no AI, nothing sent. It compares values, not formulas or formatting, and it says so. If I want it written up, my AI gets the summary and the changes after I have seen them, never the files." |
| 3:59 | *Optional.* In the same window, **Use two sample letters**. Point at the changed paragraph. Then paste "The goal date moved to Oct 17, 2026, 21 days later" into **Check an AI answer** and press **Check the answer**. | "Two drafts of one letter, compared paragraph by paragraph, on my PC. One of my own prompts can be run on this, and I see every character first. And when an AI gives me an answer, this checks its numbers and dates against the files: here it lists '21', which stands in neither file. It checks that values exist, not that the reasoning is right." |
| 3:59 | *Optional.* Paste a text with "I hope this helps" and "[Your Name]" into **Review a draft**. | "It does not claim to know who or what wrote a text; nothing can. It lists what is worth a second look, and the response is the same whoever wrote it: check the facts and the citations, ask for the source, follow our office's policy." |
| 4:00 | Click the tray icon: **Today** opens. Press **Show all, with reasons…**. | "Today is where my day starts: what needs a look, the ANDAs I keep, the recent ones. Show all lists every ANDA in my data with something worth a look, worst first. Nothing runs in the background." |
| 4:05 | *Optional (Show all tools).* Right-click the tray icon, **Lists**, then **ANDA to-do list…**. Add an item, tick one, snooze one. | "My own items and what my data says needs a look, worst first. It updates itself only while this window is open. Nothing runs when it is closed, and the file stays on this PC." |
| 4:08 | *Optional (Show all tools).* In the memo, select a paragraph of rough notes (the simulation has one in Chat: "notes to self"), press **Ctrl+Alt+Shift+L**. Untick one, change one, press **Add**, then open **My to-do list…** from the tray. | "A second, separate list for my daily workload, ANDA related or not. My notes are read by simple rules on this PC, with no AI, and I decide what goes on the list. Nothing is added until I press Add." |
| 4:10 | Settings: use the **Go to** list to show Shortcuts, **My actions**, **My agents**, Connection, Outlook and Back up. | "Everything I might want to change is here, with no rebuild: my own keys and actions, my files, my API or model name, my writing style, and a backup I can move to another PC. Alert Hub, Patent Analysis and Triaging Agent are listed but switched off: we do not know their addresses yet, and the app does not guess." |
| 4:30 | Open the **browser simulation**, select 216482 in the Browser window and right-click. | "This is the Edge extension. It runs only when I ask, on the one tab, and makes no network requests." |
| 4:45 | Stop. | The ask: a Product Catalog and security review, then a four-week offline pilot. See `PROPOSAL.md`. |

## If someone asks

| Question | Honest answer |
|---|---|
| Is this connected to real ANDA data? | No. Every record is fictional. A real data layer would read only a sanctioned extract (see `DESIGN.md` §6). |
| Can it call our Alert Hub, Patent Analysis and Triaging Agent? | Not yet. A connector is built and all three agents are listed in Settings, switched off. It needs each agent's address and message format from DPM IT, and it has never been tried against either. Until then, nothing is sent for them. When set up, an agent runs only when I press Run, or by itself if I choose that for that one agent (never on a peek, and never using selected text). Its answer shows on the card as the agent's own words. |
| Is Elsa integrated? | Not yet. Elsa is set up as a placeholder: selection, configuration, the https-only guard, timeout, fallback and the Settings status line are built and tested. Its two request-and-reply translations are not written, because Elsa's API is not known and is not guessed. In the meantime **Settings, Connection, My API** lets anyone describe an API in a form, with no code. See `ELSA-INTEGRATION.md`. |
| Does it call Claude today? | No. It is offline by default. If Claude is chosen, the model name is a Settings field (the default, `claude-opus-4-8`, was on Anthropic's list when last checked, 2026-10-06, and a newer model exists). Use whichever model your agency approves. The request sends only the model, a token limit, the system text and the message. It has **not** been run against the live API. |
| Has it run on Windows? | Yes, in an automated run on a cloud Windows machine at 1024×768: shortcuts registered and delivered, Select & Peek captured a real selection and restored the clipboard, Segoe UI rendered, and cards stayed on screen. See `VERIFICATION.md` for each run and its limits. |
| Can I right-click a selected number? | In **Edge**, yes, with the extension (peek, check as a draft, scan the page). Windows does not let one program add a right-click item to other programs, so in desktop Outlook or Teams use the shortcut. An optional middle-click is also available (Settings, Other ways to start it). The extension was tested in Chromium, not yet in Edge itself, and not on Outlook on the web. |
| Does it read my mailbox? | Not by default. The app reads only what you select. There is an **optional Outlook setting**, off by default, that lists the latest messages mentioning the open card's six-digit number: read-only, memory-only sign-in on Microsoft's own page, nothing saved, nothing sent to any AI. It needs your organization to register an app and allow mail access. It was tested against a stand-in server only, never against Microsoft. The Edge extension has no mailbox access. |
| Does it report how much I use it? | No. An optional usage log, **off until you switch it on**, counts how often each tool is used: counts only, never an ANDA number, a name or any text. *Show my usage…* in Settings shows the last 7 and 30 days. It stays on your PC; you copy it if you choose to share it. Nothing is reported for you. |
| Can it compare two Excel files? | Yes, two versions of the same report (`.xlsx` or CSV; one sheet per workbook, named in the window). It compares text, numbers and dates in the columns both files have, on your PC, with no AI. It does not see formulas, formatting, hidden rows or other sheets. It has not been tried on a real report. |
| Will it work on my managed workstation? | Unknown, and please do not try it there before IT approves it. Application allow-listing, endpoint protection and clipboard-monitoring tools can block an unsigned program or its keystroke injection. Run the self-test first. |
| Does copying touch my clipboard history or DLP? | Yes, briefly. The selection is on the clipboard for a moment, so Win+V clipboard history and clipboard-monitoring tools can see it. Your own clipboard is restored afterwards. Turn **Select & Peek** off in Settings to avoid this. The lookup box needs no clipboard. |
| What about non-US keyboards? | Ctrl+Alt is also AltGr on several layouts (for example German and French), so Ctrl+Alt+A may type a character. Choose other keys in Settings, Shortcuts, or use the tray icon. |
| Why does nothing happen in my admin window? | Windows blocks injected keystrokes into elevated windows. The app then reports nothing selected and opens the lookup box. |

## Troubleshooting

- **No tray icon.** Look under **^**. If you ran the launcher twice, a message said it is already running.
- **A shortcut does nothing.** Another program owns it. The app shows a notice at start-up, and Settings, Shortcuts says which one.
  Choose another, or use Ctrl+Alt+Space or the tray icon.
- **A crash.** A `crash.log` is written to the `data` folder next to the launcher.
- **Remove it.** Choose Exit from the tray menu, then delete the folder. Nothing is installed.
