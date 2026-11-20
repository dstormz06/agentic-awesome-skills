namespace AndaCompanion.Services;

/// <summary>
/// Recent and kept ANDAs (round 18 in the simulation, round 33 here). Only six-digit numbers, kept in this PC's settings and never sent anywhere.
/// The newest first and each number once, eight at most, nothing while it is switched off; five kept at most, and a sixth is refused, never pushed in.
/// </summary>
public static class Recents
{
    public const int RecentMax = 8, PinMax = 5;
    public const string SaidOff = "Recent ANDAs are off and were cleared.", SaidCleared = "Recent ANDAs cleared.", SaidPinsCleared = "My list cleared. The ANDAs themselves are not changed.";

    public static bool Six(string? v) => v is { Length: 6 } && v.All(c => c is >= '0' and <= '9');

    public static List<string> After(IEnumerable<string> recents, string? id, bool on)
    {
        var list = recents.ToList();
        if (!on || !Six(id)) return list;
        list.RemoveAll(x => x == id); list.Insert(0, id!);
        return list.Take(RecentMax).ToList();
    }

    /// <summary>One press of "Keep in my list" / "Remove from my list": the list after it, and what happened ("kept", "removed", "full", "refused").</summary>
    public static (List<string> Pins, string Result) PinAfter(IEnumerable<string> pins, string? id)
    {
        var list = pins.ToList();
        if (id is not null && list.Contains(id)) { list.RemoveAll(x => x == id); return (list, "removed"); }
        if (!Six(id)) return (list, "refused");
        if (list.Count >= PinMax) return (list, "full");
        list.Add(id!); return (list, "kept");
    }

    /// <summary>The numbers offered in the lookup window: the kept ones, then the recent ones that are not kept, and only numbers the data knows.</summary>
    public static List<string> Listed(IEnumerable<string> pins, IEnumerable<string> recents, Func<string, bool> known)
    {
        var p = pins.ToList();
        return p.Concat(recents.Where(x => !p.Contains(x))).Where(known).ToList();
    }

    /// <summary>The lists as they are kept: six-digit numbers only, each once, within their limits.</summary>
    public static List<string> Clean(IEnumerable<string>? list, int max) => (list ?? Array.Empty<string>()).Where(Six).Distinct().Take(max).ToList();

    public static string PinFull => $"Your list holds {PinMax} ANDAs. Remove one to keep another.";
    public static string PinLabel(bool kept) => kept ? "Remove from my list" : "Keep in my list";
    public static string Name(string id, bool kept) => $"ANDA {id}{(kept ? ", kept in my list" : "")}";
}
