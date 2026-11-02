using Avalonia.Threading;

namespace AndaCompanion.UI;

/// <summary>
/// The auto-hide rule, pure so it can be tested without a clock or a window. A card or compact box hides only when
/// it has been idle for the whole period AND the pointer is away AND it is neither pinned nor expanded. Hover,
/// pinning and the expanded drawer are all "I am using this" signals; a rule that ignored them would take a card
/// away mid-sentence.
/// </summary>
public static class AutoHidePolicy
{
    /// <summary>Seconds offered in Settings; 0 = never hide on its own.</summary>
    public static readonly IReadOnlyList<int> Choices = new[] { 0, 10, 20, 40 };

    public const int Default = 20;

    /// <summary>A saved value that is not one of the choices (hand-edited, or from a later build) falls back to the default.</summary>
    public static int Normalize(int seconds) => Choices.Contains(seconds) ? seconds : Default;

    public static bool ShouldHide(double idleSeconds, double limitSeconds, bool pointerInside, bool pinned, bool expanded) =>
        limitSeconds > 0 && !pointerInside && !pinned && !expanded && idleSeconds >= limitSeconds;

    public static string Label(int seconds) => seconds == 0 ? "Off" : $"{seconds} s";
}

/// <summary>Drives <see cref="AutoHidePolicy"/> for one window: a 250 ms timer, a "touched" stamp, and the pointer flag.</summary>
public sealed class AutoHideController
{
    private readonly Func<bool> _pinned;
    private readonly Func<bool> _expanded;
    private readonly Action _hide;
    private readonly Func<double> _clock;
    private readonly DispatcherTimer _timer;
    private double _lastTouch;
    private double _limit;

    public AutoHideController(Func<bool> pinned, Func<bool> expanded, Action hide, Func<double>? clock = null)
    {
        _pinned = pinned;
        _expanded = expanded;
        _hide = hide;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _clock = clock ?? (() => sw.Elapsed.TotalSeconds);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
    }

    public bool PointerInside { get; set; }

    /// <summary>The user did something (moved, pressed a key): restart the idle period.</summary>
    public void Touch() => _lastTouch = _clock();

    public void Start(double limitSeconds)
    {
        _limit = limitSeconds;
        if (limitSeconds <= 0) return;
        Touch();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>One evaluation; public so tests can drive it with a fake clock.</summary>
    public void Tick()
    {
        if (!AutoHidePolicy.ShouldHide(_clock() - _lastTouch, _limit, PointerInside, _pinned(), _expanded())) return;
        _timer.Stop();
        _hide();
    }
}
