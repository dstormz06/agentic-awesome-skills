namespace AndaCompanion.Services;

/// <summary>
/// Round 42, hardened: the division's Word document is read away from the app's own thread, so a slow disk or a large file never
/// makes the app pause. What was read last is kept, keyed by the file's path, time and size; while a newer copy is read, the last read of
/// the same file is shown. When a read ends, <c>changed</c> is called once on the app's thread (to check the notices and repaint).
/// </summary>
public sealed class NewsReader
{
    public const string SaidReading = "Reading the news document…";

    private readonly Func<string, (IReadOnlyList<string>? Paragraphs, string? Problem)> _read;
    private readonly Action<Action> _background, _post;
    private (string Path, DateTime Written, long Length)? _done, _reading;
    private IReadOnlyList<string>? _paragraphs;
    private string? _problem;

    /// <param name="read">Reads the document (DivisionNews.ReadDocx).</param>
    /// <param name="background">Runs work away from the app's thread (Task.Run in the app).</param>
    /// <param name="post">Runs work on the app's thread (the dispatcher in the app).</param>
    public NewsReader(Func<string, (IReadOnlyList<string>?, string?)> read, Action<Action> background, Action<Action> post)
    {
        _read = read; _background = background; _post = post;
    }

    /// <summary>
    /// The paragraphs read for this path, time and size, or the last read of the same path while a newer one is read (Reading true), or
    /// nothing yet (Reading true). A read is started only when none is already under way for the same stamp.
    /// </summary>
    public (IReadOnlyList<string>? Paragraphs, string? Problem, bool Reading) Get(string path, DateTime written, long length, Action changed)
    {
        var stamp = (path, written, length);
        if (_done == stamp) return (_paragraphs, _problem, false);
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
                    _reading = null; _done = stamp; (_paragraphs, _problem) = result;
                    changed();
                });
            });
        }
        return _done is { } d && d.Path == path ? (_paragraphs, _problem, true) : (null, null, true);
    }
}
