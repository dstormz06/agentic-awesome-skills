namespace AndaCompanion.Services;

/// <summary>
/// What happens at the first start: the guide opens by itself, once. That it was shown is saved before it opens, so that a crash, a closed
/// window or a second start can never bring it up by itself again. Where the PC does not let the settings be saved, the guide still opens
/// and says that it will open again. A diagnostic run never opens it.
/// </summary>
public static class FirstStart
{
    /// <param name="save">Saves the settings and says whether that worked.</param>
    /// <param name="open">Opens the guide; its argument is a sentence the guide shows at its first step, or null.</param>
    /// <returns>Whether the guide was opened.</returns>
    public static bool Run(Settings settings, Func<Settings, bool> save, Action<string?> open, bool diagnosticRun)
    {
        if (diagnosticRun || !Guide.OpensAtStart(settings.FirstRunDone)) return false;
        settings.FirstRunDone = true;
        bool saved; try { saved = save(settings); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { saved = false; }
        open(saved ? null : Guide.SaidNotRemembered);
        return true;
    }
}
