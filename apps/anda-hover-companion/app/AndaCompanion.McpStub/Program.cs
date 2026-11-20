using System.Text.Json.Nodes;

// A stand-in MCP server over stdio, for tests only. One JSON line in, one JSON line out. It offers two tools:
// fictional_echo (says back the text it is given) and fictional_variable (says whether an environment variable is set, never its
// value), so a test can see which variables the app passed on. Fictional data only; no file is read, nothing is sent anywhere.
var output = Console.Out;
string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    if (JsonNode.Parse(line) is not JsonObject m || m["id"] is not { } id) continue;      // notifications get no answer
    string method = m["method"]?.GetValue<string>() ?? "";
    JsonObject result = method switch
    {
        "initialize" => new() { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(), ["serverInfo"] = new JsonObject { ["name"] = "Fictional stub", ["version"] = "1" } },
        "tools/list" => new()
        {
            ["tools"] = new JsonArray(
                new JsonObject { ["name"] = "fictional_echo", ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } } } },
                new JsonObject { ["name"] = "fictional_variable", ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["name"] = new JsonObject { ["type"] = "string" } } } }),
        },
        "tools/call" => Call(m["params"] as JsonObject),
        _ => new(),
    };
    output.WriteLine(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString());
    output.Flush();
}

static JsonObject Call(JsonObject? p)
{
    string tool = p?["name"]?.GetValue<string>() ?? "";
    var args = p?["arguments"] as JsonObject;
    string text = tool switch
    {
        "fictional_echo" => "Fictional echo: " + (args?["text"]?.GetValue<string>() ?? ""),
        "fictional_variable" => Environment.GetEnvironmentVariable(args?["name"]?.GetValue<string>() ?? "") is null ? "not set" : "set",
        _ => "unknown tool",
    };
    return new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) };
}
