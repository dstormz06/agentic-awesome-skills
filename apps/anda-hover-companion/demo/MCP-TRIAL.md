# MCP trial: one approved server, on a Windows PC

A step-by-step check that ANDA Companion (Notula) works with one real MCP server. It takes about 20 minutes. Run it on the Windows
PC where the app is installed, with the server your office approved. Use **fictional text only** in every argument: no application
numbers, applicant names, personal information or confidential commercial information.

## Before you start

| Need | Who | Done |
|---|---|---|
| The server's MCP address (https://…, the address its owner gives for MCP) | Server owner | ☐ |
| Approval to reach that host from this PC, and the host allowed through Zscaler | IT | ☐ |
| How the server wants its token: the header name (often Authorization) and the word before the token (often Bearer, or none) | Server owner | ☐ |
| The token set in a user environment variable through your approved channel, for example `MCP_TOKEN`. The app is never given the token itself | You and IT | ☐ |
| The app at round 40 or later (About and what changed says "Prototype, round 40") | You | ☐ |
| *Show all tools (the two to-do lists, agents and dates)* ticked in Settings (MCP tools is in the tray menu's More only with all tools on) | You | ☐ |

## Steps

Mark each step pass or fail and copy the app's sentence exactly when one is shown.

1. **Add the server.** Settings, AI and connections, MCP SERVERS: press *Add an MCP server*. Fill in a name, the address, the token header,
   the word before the token, and the variable's NAME (for example `MCP_TOKEN`). Tick *Use this server*.
   *Expect:* the box stays ticked. If it unticks, the sentence below it says why (address or header).
2. **Test.** Press *Test*. *Expect:* "Connected. N tools: …". Note N and the first names.
3. **What is switched on.** Tray menu, More, *Connections and event log…*. *Expect:* the server listed as "(MCP server)" with its host,
   and the event log showing "MCP servers switched on: 0 to 1" and "MCP tools listed, N tools": counts and changes, never content.
4. **List tools.** Tray menu, More, *MCP tools…*. Choose the server, press *List tools*. *Expect:* the same N tools; nothing else is sent.
5. **Look before sending.** Choose a harmless tool (a search or a count). Put fictional text in its arguments, for example
   `{"query": "Fictional: goal date question"}`. Press *Show what will be sent*. *Expect:* the server, its host, the tool and the
   arguments, exactly as typed. Change one letter. *Expect:* Send turns off until you look again.
6. **Send.** Look again, press *Send*. *Expect:* "Answered. The answer is shown here only and is not kept." and the answer as text.
   Press *Copy the answer*. *Expect:* "Copied."
7. **From Ask.** Select some fictional text in any program, press your Ask shortcut, then *Use with an MCP tool…*. *Expect:* MCP tools
   opens with that text in the first text argument, and nothing is sent until you press List tools and Send.
8. **A refusal.** In Settings, change the variable name to one that is not set (for example `MCP_TOKEN_NOT_SET`) and press *Test*.
   *Expect:* a sentence saying that variable is not set; nothing sent. Put the right name back.
9. **Close.** Close MCP tools. *Expect:* nothing on screen. (The app ends the session with the server; the server's owner can confirm
   the session was closed, if they log it.)
10. **The event log.** Connections and event log: *Expect:* only counts ("MCP request, N characters"), never your arguments or the answer.

## If something fails

| You see | It means | Next |
|---|---|---|
| "could not be reached, or stopped answering" | The PC could not reach the host, or the connection was cut | Ask IT whether the host is allowed and whether Zscaler inspects it |
| "The server's certificate was not accepted…" | The certificate is not valid, was revoked, or its revocation list could not be checked from this network (since round 41 the app checks it) | Ask IT whether the proxy lets the PC reach the certificate's revocation list (CRL or OCSP) for this host |
| "refused the request: it needs a token…" | 401 or 403 from the server | Check the variable, the header name and the word before the token with the server owner |
| "answered with an error (N)" | Any other HTTP error, N is the code | Give N to the server owner; 307 or 308 means the address redirects: use the final address |
| "speaks MCP version …, which this app does not" | The server's protocol version is not 2025-06-18 or 2025-03-26 | Tell the builder the version shown |
| "did not answer in time" | Over 30 seconds | Try once more; then tell the server owner |
| Anything else unexpected | — | Copy the sentence exactly; do not paste the token or any real data |

## Record

Date, time and time zone; the app's version line; the server's name (not its token); pass or fail for steps 1 to 10; each sentence
the app showed. Send the record to the builder. Do not include the token, the address of an internal server if it is not meant to be
shared, or any real data.
