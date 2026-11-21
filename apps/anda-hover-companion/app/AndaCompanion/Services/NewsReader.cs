namespace AndaCompanion.Services;

/// <summary>
/// Round 42, hardened: the division's Word document is read away from the app's own thread, so a slow disk or a large file never
/// makes the app pause. What was read last is kept, keyed by the file's path, time and size; while a newer copy is read, the last read of
/// the same file is shown. When a read ends, <c>changed</c> is called once on the app's thread, with whether it worked. A read that failed
/// (a lock, an online-only copy) is tried again after a minute even if the file's time and size did not change, and not sooner, so a
/// window that paints again after a failure never starts a loop of reads.
/// </summary>
public sealed class NewsReader
{
    public const string SaidReading = "Reading the news document…";
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly Func<string, (IReadOnlyList<string>? Paragraphs, string? Problem)> _read;
    private readonly Action<Action> _background, _post;
    private readonly Func<DateTime> _now;
    private (string Path, DateTime Written, long Length)? _done, _reading;
    private ((string Path, DateTime Written, long Length) Stamp, DateTime At, string? Problem)? _failed;
    private IReadOnlyList<string>? _paragraphs;          // the last read that worked (of _done)
    private string? _problem;

    /// <param name="read">Reads the document (DivisionNews.ReadDocx).</param>
    /// <param name="background">Runs work away from the app's thread (Task.Run in the app).</param>
    /// <param name="post">Runs work on the app's thread (the dispatcher in the app).</param>
    /// <param name="now">The clock (UTC), for the minute before a failed read is tried again.</param>
    public NewsReader(Func<string, (IReadOnlyList<string>?, string?)> read, Action<Action> background, Action<Action> post, Func<DateTime>? now = null)
    {
        _read = read; _background = background; _post = post; _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// The paragraphs read for this path, time and size (Reading false); or the last read of the same path while a newer one is read
    /// (Reading true); or, after a recent failure for this copy, the failure (and the last good read of the same file, if any), not yet tried
    /// again (Reading false); or nothing yet (Reading true). A failure never throws away the last good read.
    /// </summary>
    public (IReadOnlyList<string>? Paragraphs, string? Problem, bool Reading) Get(string path, DateTime written, long length, Action<bool> changed)
    {
        var stamp = (path, written, length);
        if (_done == stamp) return (_paragraphs, _problem, false);
        if (_failed is { } f && f.Stamp == stamp && _now() - f.At < RetryAfter) return (Last(path), f.Problem, false);
        if (_reading != stamp)
        {
            _reading = stamp;
            _background(() =>
            {
                (IReadOnlyList<string>? Paragraphs, string? Problem) result;
                try { result = _read(path); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { result = (null, "The news document could not be read just now. It may be open elsewhere, not yet synced, or not a Word document."); }
                _post(() =>
                {
                    if (_reading != stamp) return;           // a newer read was asked for meanwhile; its own end will report
                    _reading = null;
                    bool ok = result.Paragraphs is not null;
                    if (ok) { _done = stamp; _failed = null; (_paragraphs, _problem) = result; }
                    else _failed = (stamp, _now(), result.Problem);
                    changed(ok);
                });
            });
        }
        return (Last(path), _failed is { } g && g.Stamp.Path == path ? g.Problem : null, true);
    }

    private IReadOnlyList<string>? Last(string path) => _done is { } d && d.Path == path ? _paragraphs : null;
}
