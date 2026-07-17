# Security posture — ANDA Hover Companion

Defense-in-depth for a tool that will eventually sit next to nonpublic, commercially
confidential application data. "Bullet-proof" is not a claim any honest engineer makes;
this is the concrete control set, what each control defends against, and what remains
the deployment's responsibility. Current v0 ships with **fictional data only**.

## Threat model & controls

| Threat | Control (v0 = implemented now, v1 = required before real data) |
|---|---|
| ANDA data leaving the authorized boundary | v0: no real data exists in the app. v1: data layer reads only sanctioned extracts; Claude calls go only through an agency-approved channel (e.g. Claude on AWS GovCloud / Bedrock, FedRAMP-authorized); **minimal-necessary-fields** prompt policy — the model receives the structured card record, never documents or correspondence bodies. |
| API key theft | The app **never stores or writes the key** — it reads `ANTHROPIC_API_KEY` from the process environment at call time and never logs it. v1: Windows Credential Manager (DPAPI, per-user) instead of an env var. |
| Prompt injection via record contents | Record JSON is fenced in `<record>` tags; the system prompt's highest-priority rule declares fenced content **data, never instructions**. The forecast number, dates, and statuses are rendered from the data layer, so a poisoned note can at worst color the brief's prose — never the facts, never an action (the app takes none). |
| Prompt injection via the Prompt Library | Lenses are user-authored and stored locally in plain JSON the user can inspect; they enter the prompt in a `<lens>` block explicitly subordinated to the grounding rules. A lens can shift emphasis; it cannot suppress grounding, invent facts, or exfiltrate the system prompt. |
| Clipboard exposure (Select & Peek) | The captured selection is used only for the 6-digit extraction, never persisted or logged; the user's original clipboard is restored immediately. |
| Over-collection / telemetry | None. The app writes exactly two local files (`settings.json`, `prompts.json`, both user-readable JSON in `%AppData%\AndaCompanion`), makes no network calls except the optional Anthropic endpoint, and requires no admin rights. |
| Supply chain | Dependencies pinned by exact version (Avalonia 11.3.18, official Anthropic SDK 12.35.1); no post-install scripts; single-file publish. v1: signed builds + SBOM in CI, IT-managed deployment (MSIX/Intune). |
| Model output as a decision | Every generated brief is labeled "generated — verify before acting," claims carry source refs, and the deterministic facts stand apart from AI text by design. The app is **read-only**: it can display, never modify, agency records. |
| Availability / fail-open behavior | Any AI-path failure (missing key, network, rate limit, refusal) silently degrades to the deterministic offline brief — the tool never blocks the user's work and never retries aggressively. |

## Rules of the road (enforced by code review on this repo)

1. No secret ever appears in settings, logs, source, or commit history.
2. No new outbound endpoint without updating this document.
3. Anything the model wrote is visually labeled as generated, wherever it is shown.
4. The app remains read-only against systems of record until a formal review says otherwise.
