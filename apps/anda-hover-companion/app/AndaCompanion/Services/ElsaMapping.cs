namespace AndaCompanion.Services;

/// <summary>
/// ┌─────────────────────────────────────────────────────────────────────────────────────────────┐
/// │  THE ONE PLACE TO EDIT WHEN ELSA'S API IS AVAILABLE  (checklist: ELSA-INTEGRATION.md)        │
/// └─────────────────────────────────────────────────────────────────────────────────────────────┘
/// 1. Write a class that implements <see cref="IElsaMapping"/> (two methods: BuildRequest, ReadReply) — an
///    illustrative skeleton is in ELSA-INTEGRATION.md. It is NOT filled in here because Elsa's request and response
///    shapes are not known to this project, and a guessed integration would fail in confusing ways.
/// 2. Change the one line below from <c>new UnfilledElsaMapping()</c> to <c>new YourElsaMapping()</c>.
/// 3. Set ANDA_COMPANION_PROVIDER=elsa and ANDA_ELSA_ENDPOINT, press Test in Settings.
/// Nothing else in the app changes: the card, the offline fallback, the prompt library and the grounding rules are
/// provider-agnostic by construction.
/// </summary>
public static class ElsaMapping
{
    public static IElsaMapping Current { get; } = new UnfilledElsaMapping();
}
