namespace MitCue.Game;

/// <summary>How an action use was detected.</summary>
internal enum OwnUseSource
{
    /// <summary>ActionEffect with the local player as caster.</summary>
    ActionEffect,

    /// <summary>ActionEffect of the local player's pet (fairy, Seraph), mapped to the player's command.</summary>
    Pet,

    /// <summary>The action's recast/charges changed (cooldown fallback).</summary>
    Recast,

    /// <summary>Another player's ActionEffect (only used for other assignees' reminders).</summary>
    Party,
}

/// <summary>One detected action use.</summary>
/// <param name="ActionId">Action id (pet actions already mapped to the player's command).</param>
/// <param name="Clock">Monotonic clock (seconds) of the detection.</param>
/// <param name="Source">Detection path.</param>
internal readonly record struct OwnUse(uint ActionId, double Clock, OwnUseSource Source);

/// <summary>
/// Merges detections of the same use that arrive through different paths (the player's ActionEffect, the pet's
/// ActionEffect, the recast/charge change). Each path reports a use at most once (ActionEffects are de-duplicated
/// by GlobalSequence, the recast path fires once per recast start or charge drop), so two detections from the
/// same path are always two uses: both charges of Consolation or Divine Benison double-weaved about 0.6 s apart
/// count twice. A detection is a duplicate only when an earlier use of the same action, not yet reported by this
/// path, lies within 1 s (5 s for pet detections, whose ActionEffect can lag the player's command); the oldest such
/// use is taken, so the paths pair up in order.
/// </summary>
internal sealed class OwnUseDeduper
{
    /// <summary>Seconds within which the player's own detections (ActionEffect, recast) of one use arrive.</summary>
    public const double Window = 1.0;

    /// <summary>Seconds a pet's ActionEffect may lag the player's command.</summary>
    public const double PetWindow = 5.0;

    private readonly Dictionary<uint, List<SeenUse>> recent = [];

    /// <summary>True when the use is new (and remembers it); false for another path's report of a known use.</summary>
    public bool Accept(in OwnUse use)
    {
        if (!this.recent.TryGetValue(use.ActionId, out var uses))
        {
            uses = new List<SeenUse>(2);
            this.recent[use.ActionId] = uses;
        }

        // Uses older than the longest window cannot pair with anything any more.
        for (var i = uses.Count - 1; i >= 0; i--)
        {
            if (use.Clock - uses[i].Clock > PetWindow)
                uses.RemoveAt(i);
        }

        var path = PathBit(use.Source);
        var window = use.Source == OwnUseSource.Pet ? PetWindow : Window;
        foreach (var seen in uses)
        {
            if ((seen.Paths & path) == 0 && Math.Abs(use.Clock - seen.Clock) < window)
            {
                seen.Paths |= path;
                return false;
            }
        }

        uses.Add(new SeenUse(use.Clock, path));
        return true;
    }

    /// <summary>Forgets everything (zone change).</summary>
    public void Clear() => this.recent.Clear();

    private static int PathBit(OwnUseSource source) => 1 << (int)source;

    /// <summary>A use already counted: when it was first seen and which paths have reported it.</summary>
    private sealed class SeenUse(double clock, int paths)
    {
        public double Clock { get; } = clock;

        public int Paths { get; set; } = paths;
    }
}
