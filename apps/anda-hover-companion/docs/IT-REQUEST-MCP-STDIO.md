# Request to IT: may ANDA Companion (Notula) start one approved MCP server on this PC?

**Draft for the owner to send.** Since round 41 the controls below are built into the app, but nothing can start: no program is
named on any PC, and a program can be named only in Settings, by its full path and the SHA-256 IT gives. Until IT answers, none is
entered, so the app starts nothing.

## What is asked

MCP (Model Context Protocol) servers offer tools such as a search or a lookup. ANDA Companion already connects to MCP servers that
answer over https, after the person presses a button and has seen exactly what will be sent. Some MCP servers instead run **as a
program on the person's own PC** (the "stdio" kind): the app would start the program, write requests to its standard input and read
answers from its standard output.

We ask whether the app may start **one specific, reviewed server program**, named by IT, and no other.

## Why we ask first

- Starting a child program is what endpoint security (CrowdStrike Falcon) watches for, and may flag.
- Most stdio servers are scripts run by Node.js or Python, often installed from a public registry. Our rules allow no unapproved
  installs and no .bat or .ps1 scripts. A server would only be possible if IT approves that exact program and how it is run.

## What the app does once a program is named (built in round 41, inert until then)

| Control | What the app does |
|---|---|
| Which program | Only the program named in Settings: the full path of an .exe on a drive of this PC (no network share, no script such as .bat, .cmd or .ps1), with the SHA-256 IT gives. Once the file is open, Windows' own path for it must be the path typed, so a network drive, a link or a short name is refused. The file's SHA-256 is checked before every start, and the file is held open (no writing, no deleting) from that check until the program has started; a different file is refused |
| How it starts | Directly, never through a shell (no cmd.exe, no PowerShell), with no window; the arguments are the ones typed in Settings, as IT names them |
| When | Only when the person presses Test, or List tools, and then presses again after a sentence that names the program (an edit in between asks again); never at start-up, never by itself, and never again by itself after it stopped |
| How long | Its input is closed when MCP tools is closed or the test ends; after two seconds it is stopped, with anything it started. When the app ends, it is stopped at once, with anything it started. If the app itself crashes, the program is not stopped |
| What it receives | The tool's name and the arguments the person was shown first, as for an https server |
| What it can reach | Whatever the program itself does, with the person's rights. The app passes it no token and starts it without the variables that hold the app's tokens; other environment variables are passed on, as to any program the person starts |
| What the check covers | The SHA-256 covers the .exe file only, not files it loads (DLLs, scripts, settings). Please name a program in a folder only administrators can change, such as C:\Program Files |
| What is kept | Counts only in the app's event log ("MCP program started", "MCP program stopped", characters sent); never the arguments or answers. A restored backup brings the program back switched off |
| Limits | Each answer read up to 1 MB; 30 seconds for a whole operation; a program that breaks off, floods or runs out of time is stopped |

## What we need from IT

1. Yes or no to letting the app start one approved program, as above.
2. If yes: the program's full path, its SHA-256, and its arguments, to be typed in Settings (no rebuild of the app is needed). Also how
   Falcon should treat the start: an exclusion, or simply a review of the alert.
3. Whether the program may reach the network, and which hosts.
4. Whether records rules apply to anything the program keeps on its own.

Until IT answers, no program is named, so only MCP servers over https are used.
