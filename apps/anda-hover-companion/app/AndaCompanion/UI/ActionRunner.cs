using AndaCompanion.Data;
using AndaCompanion.Models;
using AndaCompanion.Services;

namespace AndaCompanion.UI;

/// <summary>What starting an action found: something to work on, a need to ask for an ANDA number, or a number that is not in the data.</summary>
public abstract record ActionStart;
public sealed record StartReady(AndaRecord? Record, string? Number, string Selection) : ActionStart;
public sealed record StartAskForAnda : ActionStart;
public sealed record StartNotFound(string Number) : ActionStart;

/// <summary>What running an action produced: a short notice to show, or a window to open.</summary>
public abstract record ActionOutcome;
public sealed record ActionNotice(string Message) : ActionOutcome;
public sealed record ActionShow(ActionWindowModel Model) : ActionOutcome;

/// <summary>
/// Works out what each kind of custom action does. It holds no windows, so it can be tested on its own. It never sends anything itself:
/// an AI or an agent is asked only from the Send function it puts in the window model, which runs when the person clicks Send
/// (or at once, if they chose that for this one action). A reply is text; it is never run or passed on.
/// </summary>
public sealed class ActionRunner(
    Func<DateOnly> today, Func<BusinessCalendar> calendar, TodoService todo, AgentService agents,
    Func<AndaRecord, ReferenceInfo?> reference, Func<IChatCompleter?> completer, Func<string> connectionLabel)
{
    /// <summary>
    /// Decides what an action works on. A selected ANDA number is used if it is in the data. With nothing useful selected the person is asked
    /// for a number, so a press is never a dead end. An agent always needs an ANDA from the data.
    /// </summary>
    public ActionStart Start(CustomAction action, string? selection, Func<string, AndaRecord?> find)
    {
        string clean = ActionFill.CleanSelection(selection);
        string? number = AndaValidator.Extract(clean);
        var record = number is null ? null : find(number);
        bool needsRecord = action.Kind == ActionKind.RunAgent;
        if (record is null && (clean.Length == 0 || needsRecord))
            return number is not null ? new StartNotFound(number) : new StartAskForAnda();
        return new StartReady(record, number, clean);
    }

    public ActionOutcome Run(CustomAction action, AndaRecord? record, string? number, string selection)
    {
        var day = today();
        var ref0 = record is null ? null : reference(record);
        string subject = record is null ? "Your selected text" : $"ANDA {record.Number} · {record.Drug}";
        string? ownerNumber = record?.Number ?? number;

        switch (action.Kind)
        {
            case ActionKind.AddToTodo:
            {
                string? title = ActionFill.Title(action.Template, record, selection, day);
                if (title is null) return new ActionNotice("Nothing to add yet. Please select an ANDA number or some text, then try again.");
                var due = MyActionRules.DueDate(day, action.DueBusinessDays, calendar());
                var result = todo.Add(title, ownerNumber, null, due, action.Name, day);
                return new ActionNotice(result.Reason ?? (result.Added ? "Added to your to-do list: " + Shorten(title) : "That is already on your to-do list."));
            }
            case ActionKind.FillPrompt:
            {
                string prompt = ActionFill.Prompt(action.Template, record, selection, day, ref0, fenceSelection: false);
                return Show(new ActionWindowModel(action.Name, subject, "YOUR PROMPT, FILLED IN", prompt,
                    "Nothing was sent anywhere. Please copy the text into the tool you use.", "", null, "", false, null));
            }
            case ActionKind.AskAi:
                return AskAi(action, subject, ownerNumber, ActionFill.Prompt(action.Template, record, selection, day, ref0, fenceSelection: true));
            default:
                return RunAgent(action, record, selection, subject, day, ref0);
        }
    }

    private ActionOutcome AskAi(CustomAction action, string subject, string? ownerNumber, string prompt)
    {
        var probe = completer();
        (probe as IDisposable)?.Dispose();
        if (probe is null)
            return Show(new ActionWindowModel(action.Name, subject, "YOUR PROMPT, FILLED IN", prompt,
                "No AI connection is ready, so nothing can be sent. You can set one up in Settings, or copy this text into the tool you use. Nothing was sent.",
                "", null, "", false, null));
        return Show(new ActionWindowModel(action.Name, subject, "THIS IS EXACTLY WHAT WILL BE SENT", prompt,
            $"Nothing has been sent yet. Pressing Send sends only the text above, with a short fixed instruction that treats selected text as data, to your AI connection ({connectionLabel()}).",
            "THE REPLY, AS RECEIVED", ct => AskAiAsync(prompt, ct), "Send", action.SendWithoutPreview,
            lines => AddLines(lines, ownerNumber, action.Name)));
    }

    private async Task<ActionReply> AskAiAsync(string prompt, CancellationToken ct)
    {
        var c = completer();
        if (c is null) return new(false, "", "No AI connection is ready. Nothing was sent.", Array.Empty<string>());
        try
        {
            string text = AgentService.Clean(await c.CompleteAsync(MyActionRules.AskSystemPrompt, prompt, 800, ct));
            return string.IsNullOrWhiteSpace(text)
                ? new(false, "", "Your AI returned nothing.", Array.Empty<string>())
                : new(true, text, "From your AI. Verify before acting.", TodoSuggestions.Parse(text));
        }
        catch (ElsaException ex) { return new(false, "", ex.Message, Array.Empty<string>()); }
        finally { (c as IDisposable)?.Dispose(); }
    }

    private ActionOutcome RunAgent(CustomAction action, AndaRecord? record, string selection, string subject, DateOnly day, ReferenceInfo? ref0)
    {
        if (record is null) return new ActionNotice("An agent needs an ANDA from your data. Please select a number and try again.");
        var agent = agents.Find(action.AgentId);
        string prompt = ActionFill.Prompt(action.Template, record, selection, day, ref0, fenceSelection: true);
        string? problem = agent is null ? "That agent is not in your list any more. Please choose another in Settings."
            : !agent.Enabled ? $"{agent.Name} is switched off. Please switch it on in Settings."
            : agents.SetupProblem(agent);
        if (agent is null || problem is not null)
            return Show(new ActionWindowModel(action.Name, subject, "YOUR PROMPT, FILLED IN", prompt, (problem ?? "The agent is not set up.") + " Nothing was sent.",
                "", null, "", false, null));
        string host = Uri.TryCreate(agent.Api.Endpoint, UriKind.Absolute, out var uri) ? uri.Host : "the address you set";
        return Show(new ActionWindowModel(action.Name, subject, "THIS IS WHAT WILL BE SENT TO " + agent.Name.ToUpperInvariant(), prompt,
            $"Nothing has been sent yet. Pressing Send asks {agent.Name} at {host}. It sends this text and only the fields named in the request body you set up in Settings.",
            $"{agent.Name.ToUpperInvariant()} REPLIED", async ct =>
            {
                var finding = await agents.RunAsync(agent, record, day, ref0, selection, force: true, ct, promptOverride: action.Template);
                return new ActionReply(finding.IsDone, finding.Text, finding.Message, finding.Items);
            }, "Send", action.SendWithoutPreview, lines => AddLines(lines, record.Number, agent.Name)));
    }

    /// <summary>Adds the lines a person ticked. Returns how many were new.</summary>
    public int AddLines(IReadOnlyList<string> lines, string? number, string source)
    {
        var day = today();
        int added = 0;
        foreach (var line in lines) if (todo.Add(line, number, null, null, source, day).Added) added++;
        return added;
    }

    private static ActionShow Show(ActionWindowModel model) => new(model);

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57].TrimEnd() + "…";
}
