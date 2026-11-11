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
/// Merges detections of the same use from different paths: the same action within 1 s counts once
/// (5 s for pet-derived detections, whose ActionEffect can lag the player's command).
/// </summary>
internal sealed class OwnUseDeduper
{
    private readonly Dictionary<uint, double> last = [];

    /// <summary>True when the use is new (and remembers it); false for a duplicate.</summary>
    public bool Accept(in OwnUse use)
    {
        var window = use.Source == OwnUseSource.Pet ? 5.0 : 1.0;
        if (this.last.TryGetValue(use.ActionId, out var previous) && Math.Abs(use.Clock - previous) < window)
            return false;
        this.last[use.ActionId] = use.Clock;
        return true;
    }

    /// <summary>Forgets everything (zone change).</summary>
    public void Clear() => this.last.Clear();
}
