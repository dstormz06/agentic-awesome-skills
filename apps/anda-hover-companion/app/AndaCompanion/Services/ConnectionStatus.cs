namespace AndaCompanion.Services;

/// <summary>Whether the app is actually talking to an AI service. Never "connected" on the strength of a setting alone.</summary>
public enum ConnectionState
{
    /// <summary>No AI service is set up. Briefs are assembled from the record. Nothing leaves the PC.</summary>
    Offline,
    /// <summary>A service is set up but no request has succeeded yet, so nobody knows it works.</summary>
    SetUp,
    /// <summary>The last real request (a Test, a brief or a writing action) succeeded.</summary>
    Connected,
    /// <summary>The last request failed. The card fell back to the offline brief.</summary>
    Failed,
}

public sealed record ConnectionSnapshot(ConnectionState State, string Provider, string Detail, DateTimeOffset? At)
{
    public static readonly ConnectionSnapshot OfflineNow = new(ConnectionState.Offline, "", "", null);

    /// <summary>The short text shown on the card and in the tray: one line, always says whether an API is connected.</summary>
    public string Label => State switch
    {
        ConnectionState.Offline => "OFFLINE · no API connected",
        ConnectionState.SetUp => $"{Provider.ToUpperInvariant()} · set up, not tested",
        ConnectionState.Connected => $"CONNECTED · {Provider}" + (At is { } t ? $" · checked {t.ToLocalTime():h:mm tt}" : ""),
        _ => $"NOT REACHABLE · {Provider} · showing the offline brief",
    };

    /// <summary>One plain sentence for the Settings panel.</summary>
    public string Sentence => State switch
    {
        ConnectionState.Offline => "Not connected to any API. Briefs are assembled from the record on this PC and nothing is sent anywhere.",
        ConnectionState.SetUp => $"{Provider} is set up, but no request has been made yet, so it is not known to work. Press Test.",
        ConnectionState.Connected => $"Connected to {Provider}. The last request succeeded" + (At is { } t ? $" at {t.ToLocalTime():h:mm tt}." : "."),
        _ => $"{Provider} did not answer{(Detail.Length > 0 ? ": " + Detail : "")}. Briefs fall back to the offline version.",
    };
}

/// <summary>
/// The single source of truth for "is this app connected to an API right now". It is changed only by what really happened:
/// a Test, a generated brief or a writing action that succeeded (Connected) or failed (Failed). Having a key or an endpoint
/// configured is only "set up, not tested".
/// </summary>
public static class ConnectionStatus
{
    private static readonly object Gate = new();
    private static ConnectionSnapshot _current = ConnectionSnapshot.OfflineNow;

    public static ConnectionSnapshot Current { get { lock (Gate) return _current; } }

    /// <summary>Raised on whatever thread made the change; UI code must marshal to its own thread.</summary>
    public static event Action? Changed;

    public static void Reset(string? configuredProvider = null)
    {
        Set(string.IsNullOrEmpty(configuredProvider)
            ? ConnectionSnapshot.OfflineNow
            : new ConnectionSnapshot(ConnectionState.SetUp, configuredProvider, "", null));
    }

    public static void Success(string provider, DateTimeOffset? now = null) =>
        Set(new ConnectionSnapshot(ConnectionState.Connected, provider, "", now ?? DateTimeOffset.Now));

    public static void Failure(string provider, string detail, DateTimeOffset? now = null) =>
        Set(new ConnectionSnapshot(ConnectionState.Failed, provider, detail, now ?? DateTimeOffset.Now));

    private static void Set(ConnectionSnapshot next)
    {
        lock (Gate) _current = next;
        Changed?.Invoke();
    }

    /// <summary>The status a start-up or a settings change should show: set up (not tested) if a completer exists, else offline.</summary>
    public static void Configure(IChatCompleter? completer) => Reset(completer?.ProviderName);
}

/// <summary>Wraps a completer so every real request updates <see cref="ConnectionStatus"/>. Adds no behaviour of its own.</summary>
public sealed class ReportingChatCompleter(IChatCompleter inner) : IChatCompleter
{
    public string ProviderName => inner.ProviderName;

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        try
        {
            string text = await inner.CompleteAsync(system, user, maxTokens, ct);
            ConnectionStatus.Success(inner.ProviderName);
            return text;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ConnectionStatus.Failure(inner.ProviderName, ex is ElsaException ? ex.Message : ex.GetType().Name);
            throw;
        }
    }
}
