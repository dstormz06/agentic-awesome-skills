# Plugging in Elsa

**Status: wired as a placeholder. There are two ways in, and the first needs no code.**

**Agents use the same form.** Settings, My agents (round 15) takes the same fields for each agent, such as Alert Hub or Triaging Agent, whose addresses and formats are not known here. That is separate from Elsa: an agent is asked only for the card's facts, on a click or by the person's choice.

**Fastest: describe Elsa's API in Settings.** Open **Settings, Connection, My API** and fill in the address, the header that carries the
token, the *name* of the environment variable that holds the token, the model or agent name, the request body and where the reply text is.
Press **Test**. Nothing is rebuilt and no code is written. The same form works for any other service, so a change of approved platform is a
change of settings. It has been tested against a fake server only, never against a real service. The banner at the top of Settings says
Connected only after a request has really succeeded.

**Or write the two translations in code** (the five-step checklist below). Choose this if Elsa needs something a form cannot express, such as
integrated Windows authentication or a client certificate.

The app already knows how to talk to "a chat model behind a provider seam". Claude is one provider.
Elsa is the second, and everything around it is built and tested: choosing it, reading its settings,
refusing an unsafe endpoint, timing out, turning an error into one plain sentence, falling back to the
offline brief, and telling you in Settings exactly what is missing. What is **not** built is the two
small translations that depend on Elsa's actual API. They are not guessed, because a guessed
integration fails in confusing ways and looks like a finished one.

## The checklist

| # | Step | Who | Where |
|---|---|---|---|
| 1 | Get API access and the facts you need: the endpoint URL, how requests are authenticated, which model or agent to use, the request and reply shape, the token allowance. Ask the data owner whether ANDA records may be sent to Elsa, because the prototype does not make that decision. | You | Elsa's team |
| 2 | Set the environment variables (below). | You | Windows |
| 3 | Write the two methods in a class that implements `IElsaMapping`. | You, or ask for it | `app/AndaCompanion/Services/ElsaMapping.cs` |
| 4 | Change one line: `new UnfilledElsaMapping()` → `new YourElsaMapping()`. | You | same file |
| 5 | Open Settings → **Test**. It says "Connected · Elsa" or tells you what is wrong. | You | the app |

### Step 2 — environment variables

| Variable | Needed | Meaning |
|---|---|---|
| `ANDA_COMPANION_PROVIDER` | yes | `elsa` |
| `ANDA_ELSA_ENDPOINT` | yes | The URL. Must start with `https://` (plain `http://` is accepted only for `localhost`). |
| `ANDA_ELSA_TOKEN` | if Elsa uses a token | Read at call time, never stored or logged, redacted if an object is ever printed. |
| `ANDA_ELSA_MODEL` | optional | Passed to your mapping as `settings.Model`. |
| `ANDA_ELSA_AGENT` | optional | Passed to your mapping as `settings.Agent`. |

PowerShell, for the current user. `setx` stores the value in the user's registry, so for a real token
use whatever secret store your agency approves instead of typing it into a shell:

```powershell
setx ANDA_COMPANION_PROVIDER elsa
setx ANDA_ELSA_ENDPOINT "https://<the endpoint Elsa's team gives you>"
```

### Step 3 — the two methods

`IElsaMapping` has two required members and one optional one:

```csharp
HttpRequestMessage BuildRequest(ElsaSettings settings, string system, string user, int maxTokens);
string ReadReply(string responseBody);
bool IsFilledIn { get; }                       // return true once the two methods above are real
HttpMessageHandler CreateHandler() => ...;     // only if Elsa needs integrated Windows auth, a client certificate or a proxy
```

`system` is the grounding prompt, `user` is the fenced record plus the lens, `maxTokens` is 400.
Everything else (timeout, status handling, fallback) is done for you.

**This skeleton is illustrative only.** Every field name in it is a stand-in, not Elsa's. Replace them
with the names in Elsa's own documentation, and delete anything Elsa does not use.

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class FdaElsaMapping : IElsaMapping
{
    public bool IsFilledIn => true;

    public HttpRequestMessage BuildRequest(ElsaSettings s, string system, string user, int maxTokens)
    {
        string body = JsonSerializer.Serialize(new
        {
            model = s.Model,                                   // stand-in
            agent = s.Agent,                                   // stand-in
            max_tokens = maxTokens,                            // stand-in
            messages = new[]                                   // stand-in
            {
                new { role = "system", content = system },
                new { role = "user",   content = user },
            },
        });
        var request = new HttpRequestMessage(HttpMethod.Post, s.Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (s.Token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);   // or whatever Elsa uses
        return request;
    }

    public string ReadReply(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("reply").GetString() ?? "";   // stand-in: use Elsa's documented path
    }
}
```

Then in `ElsaMapping.cs`:

```csharp
public static IElsaMapping Current { get; } = new FdaElsaMapping();    // was: new UnfilledElsaMapping()
```

## What Settings says, and what it means

| Message | Meaning | Fix |
|---|---|---|
| "Elsa is selected but ANDA_ELSA_ENDPOINT is not set" | Provider is `elsa`, no endpoint. | Step 2. |
| "must be an https:// URL … Nothing was sent." | The endpoint is plain `http://` (or not a URL). The app refuses so a token never travels in clear text. | Use the `https://` URL. |
| "Elsa endpoint set (host), but the request mapping is still the placeholder." | Steps 3 and 4 are not done. Briefs stay offline. | Steps 3 and 4. |
| "Connected · Elsa … response: OK" | End to end works. | Nothing. |
| "Connection failed: ElsaException — Elsa answered 401 …" | Elsa refused. Only the status is shown, never the body. | Check the token and who it belongs to. |
| "Connection failed: HttpRequestException …" | The network path is blocked or the host is wrong. | Check the URL, the proxy and the VPN. |

Until the mapping exists the app behaves exactly as it does with no AI at all: the deterministic offline
brief, clearly labeled, with every fact still coming from the record.

## What the tests already protect

`dotnet test app/AndaCompanion.Tests --filter ElsaTests` (and the self-test's `elsa-placeholder` check)
prove, with a fake HTTP handler and no network: every configuration state resolves to a plain message;
plain `http://` is refused; the token never appears in any message or in a printed settings object; an
error status becomes a short exception without the response body; a failing Elsa degrades to the offline
brief; a working one produces a brief labeled as generated; the Test button explains each state. When
you fill in the mapping these tests keep guarding everything around it. Add one test of your own that
feeds `ReadReply` a sample of Elsa's real reply.

## Cost, so the request for access can be specific

A brief is generated only when the **full card** opens. A peek, an invite and every compact box make no
AI call. Measured on the mock records the prompt is about 700 to 900 input tokens plus about 150 output
tokens; the planning figure in `PROPOSAL.md` is 3,000 tokens per brief to leave room for real records.
The limit and the price that apply to Elsa are Elsa's, not this document's.
