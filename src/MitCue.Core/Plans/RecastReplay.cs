using MitCue.Core.Actions;

namespace MitCue.Core.Plans;

/// <summary>One planned press of an action in a <see cref="RecastReplay"/>.</summary>
/// <param name="Time">The press time (timeline seconds).</param>
/// <param name="Action">The action as the player presses it at the sync level (<see cref="RecastReplay.Tracked"/>).</param>
public readonly record struct RecastUse(double Time, ActionInfo Action);

/// <summary>What one press of a <see cref="RecastReplay"/> finds.</summary>
/// <param name="HasCharge">A charge is ready at the press.</param>
/// <param name="ReadyAt">
/// With a charge: since when one has been ready (−∞ when the charges were never short); without: when the next one is
/// ready.
/// </param>
/// <param name="SinceFull">
/// The index of the first press since the charges were last full: the presses from there on (to this one) spent them.
/// </param>
public readonly record struct RecastState(bool HasCharge, double ReadyAt, int SinceFull);

/// <summary>
/// The recast model of <c>check</c> (<see cref="PlanChecker"/>: <c>recast-conflict</c>, <c>recast-tight</c>). Planned
/// presses of an action, or of actions that share a recast (<see cref="ActionInfo.SharesRecastWith"/>), form a chain
/// (<see cref="Chains{T}"/>), replayed in time order with the action's recast and charges at the sync level
/// (<see cref="Replay"/>). Pure logic.
/// </summary>
public static class RecastReplay
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// The action <paramref name="item"/> presses, as a recast replay counts it: one action (not an either/or choice, which
    /// the player picks by what is ready, nor a note) that <paramref name="job"/> can use at <paramref name="level"/>
    /// (<see cref="Usable"/>), with a recast of at least <paramref name="minRecast"/> seconds there
    /// (<see cref="PlanCheckOptions.MinRecast"/>: GCDs are limited by the GCD and resources, not by a cooldown); else null.
    /// </summary>
    /// <param name="item">The plan item.</param>
    /// <param name="actions">The action catalog.</param>
    /// <param name="job">The player's job; <see cref="Job.None"/> for any.</param>
    /// <param name="level">The sync level.</param>
    /// <param name="minRecast">Actions with a shorter recast are left out.</param>
    public static ActionInfo? Tracked(PlanItem item, IActionCatalog actions, Job job, int level, double minRecast)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Action is not { } id || item.Any != null || Usable(actions, id, job, level) is not { } action)
            return null;
        return action.RecastAt(level) >= minRecast ? action : null;
    }

    /// <summary>
    /// The catalog action for <paramref name="id"/> as pressed at <paramref name="level"/>
    /// (<see cref="ActionCatalogExtensions.ResolveAt"/>), when it is available there and <paramref name="job"/> can use it
    /// (any job for <see cref="Job.None"/>); else null.
    /// </summary>
    public static ActionInfo? Usable(IActionCatalog actions, uint id, Job job, int level)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Get(id) is not { } action)
            return null;
        var resolved = actions.ResolveAt(action, level);
        if (!resolved.AvailableAt(level) || (job != Job.None && !resolved.UsableBy(job)))
            return null;
        return resolved;
    }

    /// <summary>
    /// The presses grouped into chains of actions that share a recast: the same action, or actions of one recast group of
    /// a job. Each action joins the chain of the first action before it (in order of first use) that it shares a recast
    /// with. Chains come in order of their first press, each in the order of <paramref name="uses"/>.
    /// </summary>
    /// <param name="uses">The presses.</param>
    /// <param name="actionOf">A press's action.</param>
    public static IEnumerable<List<T>> Chains<T>(IEnumerable<T> uses, Func<T, ActionInfo> actionOf)
    {
        ArgumentNullException.ThrowIfNull(uses);
        ArgumentNullException.ThrowIfNull(actionOf);
        var list = uses.ToList();
        var distinct = list.Select(actionOf).DistinctBy(a => a.Id).ToList();
        var chainOf = new Dictionary<uint, int>();
        for (var i = 0; i < distinct.Count; i++)
        {
            var chain = i;
            for (var j = 0; j < i; j++)
            {
                if (distinct[i].SharesRecastWith(distinct[j]))
                {
                    chain = chainOf[distinct[j].Id];
                    break;
                }
            }

            chainOf[distinct[i].Id] = chain;
        }

        return list.GroupBy(u => chainOf[actionOf(u).Id]).OrderBy(g => g.Key).Select(g => g.ToList());
    }

    /// <summary>
    /// Replays the presses of one chain, in time order: they start with every charge, charges recharge one at a time,
    /// and a press while the timer is idle starts it with that press's recast. A press that finds no charge is assumed to
    /// happen anyway: it restarts the timer (<c>check</c> reports each press closer than the recast).
    /// </summary>
    /// <param name="uses">The chain's presses, in time order.</param>
    /// <param name="level">The sync level (recasts and charges).</param>
    /// <param name="maxCharges">The charges; null for the most any press's action has at the level (at least 1).</param>
    /// <returns>What each press finds, at its index.</returns>
    public static RecastState[] Replay(IReadOnlyList<RecastUse> uses, int level, int? maxCharges = null)
    {
        ArgumentNullException.ThrowIfNull(uses);
        var states = new RecastState[uses.Count];
        if (uses.Count == 0)
            return states;

        var max = maxCharges ?? Math.Max(1, uses.Max(u => u.Action.ChargesAt(level)));
        var charges = max;
        double? timerEnd = null;
        double timerRecast = 0;
        double? availableSince = double.NegativeInfinity; // charges ≥ 1 since then (null: none left)
        var sinceFull = 0;
        for (var i = 0; i < uses.Count; i++)
        {
            var use = uses[i];
            while (timerEnd is { } end && end <= use.Time + Epsilon)
            {
                charges++;
                if (charges == 1)
                    availableSince = end;
                if (charges >= max)
                {
                    charges = max;
                    timerEnd = null;
                    sinceFull = i;
                }
                else
                {
                    timerEnd = end + timerRecast;
                }
            }

            var hasCharge = charges >= 1;
            states[i] = new RecastState(hasCharge, hasCharge ? availableSince ?? use.Time : timerEnd ?? use.Time, sinceFull);
            var recast = use.Action.RecastAt(level);
            if (hasCharge)
            {
                charges--;
                if (timerEnd == null)
                {
                    timerEnd = use.Time + recast;
                    timerRecast = recast;
                }

                if (charges == 0)
                    availableSince = null;
            }
            else
            {
                timerEnd = use.Time + recast;
                timerRecast = recast;
                availableSince = null;
            }
        }

        return states;
    }
}
