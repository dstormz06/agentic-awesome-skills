# Request to IT: may ANDA Companion (Notula) start one approved MCP server on this PC?

**Draft for the owner to send. Nothing in it has been built.** The app does not start programs today, and will not until IT answers.

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

## What the app would do, if approved

| Control | Proposal |
|---|---|
| Which program | Only programs on an allow list IT sets (full path, and a SHA-256 hash of the file); anything else is refused |
| How it starts | Directly, never through a shell (no cmd.exe, no PowerShell); fixed arguments from the allow list; no arguments typed by the person |
| When | Only when the person presses List tools or Send, after a confirmation that names the program; never at start-up, never by itself |
| How long | Stopped when MCP tools is closed, and when the app exits |
| What it receives | The tool's name and the arguments the person was shown first, as for an https server |
| What it can reach | Whatever the program itself does; the app gives it no token unless the allow list names a variable for it |
| What is kept | Counts only in the app's event log (program started, stopped, characters sent); never the arguments or answers |
| Limits | Answers read up to 1 MB; 30 seconds per request; a program that misbehaves is stopped |

## What we need from IT

1. Yes or no to letting the app start one allow-listed program, as above.
2. If yes: the program, its path and hash, and its fixed arguments. Also how Falcon should treat the start: an exclusion, or
   simply a review of the alert.
3. Whether the program may reach the network, and which hosts.
4. Whether records rules apply to anything the program keeps on its own.

Until IT answers, only MCP servers over https are offered.
