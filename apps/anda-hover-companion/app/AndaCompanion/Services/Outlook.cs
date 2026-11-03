using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndaCompanion.Services;

/// <summary>Which Microsoft 365 cloud the person's organization uses. The addresses are fixed; nothing here is typed in.</summary>
public enum OutlookCloud { Commercial, UsGovernment }

/// <summary>
/// The optional, read-only Outlook connection. Nothing in it is a secret: the app never stores a token. The client ID is the
/// "Application (client) ID" of an app the person's organization registered for them in Microsoft Entra.
/// </summary>
public sealed class OutlookSettings
{
    public bool Enabled { get; set; }
    public OutlookCloud Cloud { get; set; } = OutlookCloud.Commercial;
    public string ClientId { get; set; } = "";

    /// <summary>"organizations" (any work account), "common", a directory ID, or the organization's domain name.</summary>
    public string TenantId { get; set; } = "organizations";

    /// <summary>How many recent messages a card shows (3, 5 or 10).</summary>
    public int MaxItems { get; set; } = 5;
    public static readonly IReadOnlyList<int> MaxItemChoices = new[] { 3, 5, 10 };

    /// <summary>Repairs anything out of range or missing, so a damaged settings file or backup can never break the app.</summary>
    public static OutlookSettings Normalize(OutlookSettings? s)
    {
        s ??= new OutlookSettings();
        s.ClientId = (s.ClientId ?? "").Trim();
        s.TenantId = string.IsNullOrWhiteSpace(s.TenantId) ? "organizations" : s.TenantId.Trim();
        if (!Enum.IsDefined(s.Cloud)) s.Cloud = OutlookCloud.Commercial;
        if (!MaxItemChoices.Contains(s.MaxItems)) s.MaxItems = 5;
        return s;
    }
}

public enum OutlookState { Off, NotSetUp, SignedOut, SigningIn, SignedIn, Error }

public sealed record OutlookSnapshot(OutlookState State, string Message, string? UserCode = null, string? VerificationUrl = null);

public sealed record MailItem(string Subject, string From, DateTimeOffset? Received, string Preview);

public sealed record MailResult(bool Ok, IReadOnlyList<MailItem> Items, string Message)
{
    public static MailResult Fail(string message) => new(false, Array.Empty<MailItem>(), message);
}

/// <summary>
/// Signs in to Outlook with Microsoft's device sign-in (the person types a short code at microsoft.com; this app never sees their
/// password) and reads, only for the ANDA number on the open card, the most recent messages that mention it: sender, subject, date
/// and a short preview. Read-only (Mail.Read). The sign-in token lives in memory only and ends with the app or with Sign out. Mail is
/// shown on the person's own screen and is never stored and never sent to any AI service.
/// Written from Microsoft's public documentation and tested against a fake server only: it has never been run against Microsoft.
/// </summary>
public sealed class OutlookService : IDisposable
{
    private static readonly Regex AndaNumber = new(@"^[0-9]{6}\z", RegexOptions.Compiled);
    private static readonly Regex Tenant = new(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,98}\z", RegexOptions.Compiled);

    private readonly Func<OutlookSettings> _settings;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private string? _token;
    private DateTimeOffset _expires;
    private CancellationTokenSource? _signIn;
    private OutlookSnapshot _snapshot = new(OutlookState.Off, "Outlook is off.");

    public OutlookService(Func<OutlookSettings> settings, HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? now = null)
    {
        _settings = settings;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(30) };
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public event Action? Changed;

    public OutlookSnapshot Snapshot { get { lock (_gate) return Describe(); } }

    // ---- fixed addresses ----
    private static string Login(OutlookCloud c) => c == OutlookCloud.UsGovernment ? "https://login.microsoftonline.us" : "https://login.microsoftonline.com";
    private static string Graph(OutlookCloud c) => c == OutlookCloud.UsGovernment ? "https://graph.microsoft.us" : "https://graph.microsoft.com";

    /// <summary>True only for a Microsoft sign-in address. The app opens nothing else from a reply, whatever a server sends.</summary>
    public static bool IsMicrosoftAddress(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        string host = uri.Host.ToLowerInvariant();
        foreach (string root in new[] { "microsoft.com", "microsoftonline.com", "microsoftonline.us", "microsoft.us" })
            if (host == root || host.EndsWith("." + root, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>A plain sentence about what is missing, or null when the settings are complete.</summary>
    public static string? ValidateSetup(OutlookSettings s)
    {
        if (!s.Enabled) return "Outlook is off.";
        if (!Guid.TryParseExact((s.ClientId ?? "").Trim(), "D", out _)) return "Please enter the Application (client) ID your organization gave you. It looks like 12345678-1234-1234-1234-123456789abc.";
        if (!Tenant.IsMatch((s.TenantId ?? "").Trim())) return "The directory (tenant) must be \"organizations\", a directory ID or your organization's domain name.";
        return null;
    }

    private OutlookSnapshot Describe()
    {
        var s = _settings();
        if (ValidateSetup(s) is { } problem) return new(s.Enabled ? OutlookState.NotSetUp : OutlookState.Off, problem);
        if (_snapshot.State is OutlookState.SigningIn or OutlookState.Error) return _snapshot;
        if (_token is not null && _now() < _expires) return new(OutlookState.SignedIn, "Signed in to Outlook (read-only). The sign-in ends when the app closes.");
        return new(OutlookState.SignedOut, _snapshot.State == OutlookState.SignedOut && _snapshot.Message.Length > 0 ? _snapshot.Message : "Not signed in. Please press Sign in.");
    }

    private void Set(OutlookSnapshot next) { lock (_gate) _snapshot = next; Changed?.Invoke(); }

    public void SignOut()
    {
        lock (_gate) { _signIn?.Cancel(); _token = null; _expires = default; _snapshot = new(OutlookState.SignedOut, "Signed out. Nothing from Outlook is kept."); }
        Changed?.Invoke();
    }

    // ---------------- device sign-in ----------------

    public async Task SignInAsync(CancellationToken ct = default)
    {
        var s = _settings();
        if (ValidateSetup(s) is { } problem) { Set(new(OutlookState.NotSetUp, problem)); return; }
        CancellationTokenSource cts;
        lock (_gate) { _signIn?.Cancel(); cts = _signIn = CancellationTokenSource.CreateLinkedTokenSource(ct); }
        string tenant = s.TenantId.Trim(), client = s.ClientId.Trim();
        try
        {
            using var begin = await _http.PostAsync($"{Login(s.Cloud)}/{tenant}/oauth2/v2.0/devicecode",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = client, ["scope"] = $"{Graph(s.Cloud)}/Mail.Read" }), cts.Token);
            if (!begin.IsSuccessStatusCode)
            {
                Set(new(OutlookState.Error, begin.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "Microsoft did not accept this Application ID or directory. Please check both with whoever registered the app."
                    : $"Microsoft's sign-in service answered {(int)begin.StatusCode}."));
                return;
            }
            using var doc = JsonDocument.Parse(await begin.Content.ReadAsStringAsync(cts.Token));
            var r = doc.RootElement;
            string device = r.GetProperty("device_code").GetString() ?? "", code = r.GetProperty("user_code").GetString() ?? "", uri = r.GetProperty("verification_uri").GetString() ?? "";
            int interval = r.TryGetProperty("interval", out var iv) && iv.TryGetInt32(out int i) ? Math.Clamp(i, 1, 30) : 5;
            int lifetime = r.TryGetProperty("expires_in", out var ex) && ex.TryGetInt32(out int e) ? Math.Clamp(e, 30, 1800) : 900;
            Set(new(OutlookState.SigningIn, $"Please open {uri} in your browser and enter the code {code}, then wait here.", code, uri));

            var until = _now().AddSeconds(lifetime);
            while (_now() < until)
            {
                await _delay(TimeSpan.FromSeconds(interval), cts.Token);
                using var poll = await _http.PostAsync($"{Login(s.Cloud)}/{tenant}/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["client_id"] = client, ["device_code"] = device,
                }), cts.Token);
                using var body = JsonDocument.Parse(await poll.Content.ReadAsStringAsync(cts.Token));
                var t = body.RootElement;
                if (poll.IsSuccessStatusCode && t.TryGetProperty("access_token", out var at) && at.GetString() is { Length: > 0 } token)
                {
                    int seconds = t.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out int n) ? Math.Clamp(n, 60, 86400) : 3600;
                    lock (_gate) { _token = token; _expires = _now().AddSeconds(seconds); _snapshot = new(OutlookState.SignedIn, ""); }
                    Changed?.Invoke();
                    return;
                }
                string error = t.TryGetProperty("error", out var er) ? er.GetString() ?? "" : "";
                switch (error)
                {
                    case "authorization_pending": continue;
                    case "slow_down": interval = Math.Min(interval + 5, 60); continue;
                    case "authorization_declined": Set(new(OutlookState.SignedOut, "The sign-in was declined, so nothing was connected.")); return;
                    case "expired_token": Set(new(OutlookState.SignedOut, "The code expired before it was used. Please press Sign in again.")); return;
                    default: Set(new(OutlookState.Error, "Microsoft would not complete the sign-in. Your organization may need to approve this app first.")); return;
                }
            }
            Set(new(OutlookState.SignedOut, "The code expired before it was used. Please press Sign in again."));
        }
        catch (OperationCanceledException)
        {
            // Only the sign-in that is still current reports it; Sign out and a newer Sign in say their own piece.
            bool current; lock (_gate) current = ReferenceEquals(_signIn, cts) && _snapshot.State == OutlookState.SigningIn;
            if (current) Set(new(OutlookState.SignedOut, "Sign-in cancelled."));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Set(new(OutlookState.Error, "Microsoft's sign-in service could not be reached. Please check your connection and try again."));
        }
    }

    // ---------------- reading mail about one ANDA ----------------

    public async Task<MailResult> SearchAsync(string number, CancellationToken ct = default)
    {
        var s = _settings();
        if (!s.Enabled) return MailResult.Fail("Outlook is off.");
        if (!AndaNumber.IsMatch(number ?? "")) return MailResult.Fail("Outlook is only searched for a six-digit ANDA number.");
        string? token; lock (_gate) token = _token is not null && _now() < _expires ? _token : null;
        if (token is null) return MailResult.Fail("Not signed in. Please sign in to Outlook in Settings.");
        int max = OutlookSettings.MaxItemChoices.Contains(s.MaxItems) ? s.MaxItems : 5;
        string url = $"{Graph(s.Cloud)}/v1.0/me/messages?$search={Uri.EscapeDataString("\"" + number + "\"")}&$top={max}&$select=subject,from,receivedDateTime,bodyPreview";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 401) { lock (_gate) { _token = null; } Set(new(OutlookState.SignedOut, "Your Outlook sign-in ended. Please sign in again.")); return MailResult.Fail("Your Outlook sign-in ended. Please sign in again."); }
                return MailResult.Fail((int)response.StatusCode switch
                {
                    403 => "Your organization has not allowed this app to read mail. An administrator may need to approve it.",
                    429 => "Outlook asked for a pause. Please try again in a minute.",
                    var code => $"Outlook answered {code}.",
                });
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var items = new List<MailItem>();
            if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                foreach (var m in value.EnumerateArray().Take(max))
                {
                    string subject = Clean(Str(m, "subject"), 120), preview = Clean(Str(m, "bodyPreview"), 160);
                    string from = "";
                    if (m.TryGetProperty("from", out var f) && f.TryGetProperty("emailAddress", out var ea))
                        from = Clean(Str(ea, "name").Length > 0 ? Str(ea, "name") : Str(ea, "address"), 80);
                    DateTimeOffset? received = DateTimeOffset.TryParse(Str(m, "receivedDateTime"), out var d) ? d : null;
                    items.Add(new MailItem(subject.Length > 0 ? subject : "(no subject)", from, received, preview));
                }
            return new MailResult(true, items, items.Count == 0 ? $"No messages mention {number}." : "");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return MailResult.Fail("Outlook could not be reached. Please check your connection.");
        }
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>One line of plain text: control characters and runs of white space become single spaces, trimmed to a length.</summary>
    internal static string Clean(string text, int max)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.Length <= max ? sb.ToString() : sb.ToString(0, max - 1).TrimEnd() + "…";
    }

    public void Dispose() { lock (_gate) { _signIn?.Cancel(); _token = null; } _http.Dispose(); }
}
