# ANDA Companion (Notula) for Edge (prototype)

A Microsoft Edge (Chromium) extension that does in the browser what the Windows tray app does on the desktop: select an ANDA number, get its
forecast card, with the weekend and federal-holiday **WATCH** line, a **draft check**, a portfolio-wide **watch list**, and a one-click
**status sentence**. It works **on its own** or **together with the desktop app**. Fictional demo data until you add your own.
Prototype, not an FDA product. It makes **no network requests and uses no AI**.

## What it does

| You do | It does |
|---|---|
| Select an ANDA number on a page, right-click, **ANDA Companion (Notula): peek at "…"** (or press **Alt+Shift+A**) | Whisper invite by default: a small chip you click. An at-risk or late ANDA opens a peek by itself and says why. Settings choose "peek first" or "full card" instead |
| Select text, or click into the message you are writing, then **ANDA Companion (Notula): check this text as a draft** (**Alt+Shift+D**) | Compares the ANDA numbers, goal and forecast dates and on-track or late wording with your data. No AI. Says what it did not check |
| Right-click the page, **scan this page for ANDA numbers** (**Alt+Shift+S**) | Lists the numbers on the page with a one-line status each. Reads the visible text on your click only |
| Click the toolbar button | **Look up** a number, **Draft**, **Watch list** (every ANDA with something worth a look, worst first), **This page** |

It runs only when you ask: a shortcut, the right-click menu or the toolbar button. There is no script running on every page. Settings, *Ways to start it* lists each of these and the key Edge actually gave every shortcut ("not set" when Edge or another extension has it), with a button to change them. A **middle-click** trigger is not offered here on purpose, because an extension can only watch the mouse by running a script on every page; the Windows app has one. Both cards always show a **PSG** line and an **RLD LABEL** line: a date with its source, or a brief reason there is none.

What the extension leaves to the desktop app, on purpose: quick replies, the Outlook setting, the connection to an API and any generated text. The extension never calls an API and has no access to mail.

## Two ways to use it

**Standalone** (nothing else installed). Settings, *Where the data comes from*:

- *Fictional demo records*: two ANDAs, to try it out.
- *A spreadsheet I choose*: an Excel (.xlsx) or CSV portfolio, read in the browser and kept in this browser profile. It shows only what your sheet
  says: a forecast or progress that is not in it shows as "—", never an estimate. Column detection by header names, with a mapping you can correct.
  A notes folder is not available in this mode.

**Together with the desktop app.** The app is the extension's data source and settings: one portfolio file, one set of rules, the app's notes folder
and Drugs@FDA reference file. Both draw the same card.

1. Install the desktop app, set its portfolio in **Settings, My data**.
2. In the app, **Settings, Edge extension, Connect** (current user, no admin rights). The extension ID is pre-filled.
3. In the extension's Settings choose **Allow the connection to the desktop app…** (Edge asks you to allow "communicate with cooperating native
   applications"), then choose *The desktop app* as the data source and press **Test**.

There is **no silent fallback**: if the chosen source cannot answer, you are told, rather than being shown another source's data.

## Install it (sideload, for the pilot)

1. Edge, `edge://extensions`, turn on **Developer mode**, **Load unpacked**, choose this `extension` folder.
2. Pin the toolbar button. Shortcuts: `edge://extensions/shortcuts` (Edge leaves out a suggestion that is already taken).

On a managed computer, do not do this before your IT or security team has approved it: developer mode and sideloading are often blocked by policy,
and that is the organization's policy working. The route for a pilot is an IT-managed install of this folder (or a packed file) through the
`ExtensionInstallForcelist` / `ExtensionInstallAllowlist` policies, never the public Edge Add-ons store.

## Permissions, and why

| Permission | Why | What it does not allow |
|---|---|---|
| `activeTab`, `scripting` | Draw the card and read your selection on the one tab you are in, only when you ask | Reading any other tab, or any page you have not asked about. There are **no host permissions** |
| `contextMenus` | The right-click items | |
| `storage` | Your settings and, if you import one, your spreadsheet, in this browser profile (`storage.local`, never `storage.sync`, so nothing goes to a cloud account) | |
| `nativeMessaging` (**optional**, asked for only when you connect the app) | Talk to the desktop app on this computer | Anything else: the app lists exactly one extension ID |

No remote code, no analytics, no network requests. The extension's pages run under `script-src 'self'`.

## How it stays identical to the app

The extension carries its own JavaScript copy of the rules so it works with no app. `tests/golden.json` is written by the **app's own code**
(`app/AndaCompanion.Tests/ExtensionGolden.cs`): the federal calendar for every day of 2026 and 2027, 148 date flags, 27 cards, 12 drafts, 30 recognizer texts,
and eight portfolio files (CSV dialects, 17 date formats and non-dates, an .xlsx with a hidden sheet). The extension's tests must reproduce every case exactly,
and the app's test fails if the app drifts from the committed file. To change a rule: change the app, run
`ANDA_WRITE_GOLDEN=1 dotnet test app/AndaCompanion.Tests --filter ExtensionGolden`, then make the JavaScript match.

## Tests

```bash
node --test extension/tests/unit/parity.test.mjs extension/tests/unit/safety.test.mjs extension/tests/unit/manifest.test.mjs extension/tests/unit/service.test.mjs     # no dependencies; run in CI on Linux and Windows
# end to end in a real browser engine (needs Playwright's Chromium; Edge is not available in the build environment):
NODE_PATH=$(npm root -g) node extension/tests/e2e/standalone.cjs
dotnet build app/AndaCompanion && NODE_PATH=$(npm root -g) node extension/tests/e2e/native.cjs   # the real .NET host over native messaging
```

The end-to-end tests load a copy of the extension that differs from the shipped one in only these ways: it may read `127.0.0.1` without a
user gesture, the shadow root is open so tests can look inside (the shipped file's `closed` is asserted), and the native test grants
`nativeMessaging`.

## Limits, stated plainly

- **Keyboard and screen reader.** The popup's tabs use the arrow keys and Home/End, and every control is a real button or field with a label; this is checked by script only, **not with a screen reader**.
- **Tested in Chromium, not in Edge.** Edge is the same engine and the manifest uses only shared APIs, but it has not been run in Edge itself.
- **The browser's own menu and shortcut handling is not automated.** Tests trigger the same code the menu item or shortcut calls; they cannot press a native context menu or an extension shortcut.
- **Not tested against Outlook on the web, Teams on the web or SharePoint.** The extension reads the selection and visible text, so it should work
  there, but those sites were not available to test.
- **It does not cover desktop Outlook, the new Outlook for Windows or the Teams desktop app**, which are not Edge pages. The Windows tray app covers those.
- **Not covered by the extension:** the notes folder and the Drugs@FDA RLD line (the app provides them in "together" mode), and the Proofread, Rewrite and Fact-check actions (they need an approved model; the extension has none).
- The extension ID is derived from the public `key` in `manifest.json`, so the app can allow exactly that ID. The matching private key is not in the repository; a
  packed or store build would get a different ID, which you then enter in the app's Settings.
- Every number shown is fictional unless you import your own.
