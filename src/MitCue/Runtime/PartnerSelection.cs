using MitCue.Core;
using MitCue.Core.Plans;

namespace MitCue.Runtime;

/// <summary>
/// The calls shown dimmed next to the player's own when "Also show my co-healer's calls" is on (DESIGN D5: own calls
/// only, the co-healer's calls optional): the entries of the plan's other columns of the player's role, i.e. the
/// co-healer for a healer, the co-tank for a tank and the other DPS for a DPS.
/// </summary>
internal static class PartnerSelection
{
    private enum Group
    {
        None,
        Tank,
        Healer,
        Dps,
    }

    /// <summary>
    /// Entries that do not apply to <paramref name="me"/> and belong to the player's role. An entry's role comes from
    /// its job, else its slot, else its column (the assignee's job or slot, or a key such as "H2" or "Scholar");
    /// entries whose role cannot be told are left out.
    /// </summary>
    public static List<PlanEntry> For(MitPlan plan, PlayerContext me)
    {
        var result = new List<PlanEntry>();
        var mine = GroupOf(Jobs.RoleOf(me.Job));
        if (mine == Group.None)
            return result;

        foreach (var entry in plan.Entries)
        {
            if (!PlanSelection.Applies(entry, me) && GroupOf(RoleOf(plan, entry)) == mine)
                result.Add(entry);
        }

        return result;
    }

    /// <summary>The role an entry is meant for, or <see cref="Role.None"/> when unknown.</summary>
    public static Role RoleOf(MitPlan plan, PlanEntry entry)
    {
        var who = entry.Who;
        if (who.Job is { } job && job != Job.None)
            return Jobs.RoleOf(job);
        if (Slots.TryNormalize(who.Slot, out var slot))
            return Slots.RoleOf(slot);
        if (who.Assignee is not { } key)
            return Role.None;

        var assignee = plan.Assignees.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
        if (assignee?.Job is { } assigneeJob && assigneeJob != Job.None)
            return Jobs.RoleOf(assigneeJob);
        if (Slots.TryNormalize(assignee?.Slot, out slot) || Slots.TryNormalize(key, out slot))
            return Slots.RoleOf(slot);
        return Jobs.TryParse(key, out var keyJob) ? Jobs.RoleOf(keyJob) : Role.None;
    }

    private static Group GroupOf(Role role) => role switch
    {
        Role.Tank => Group.Tank,
        Role.Healer => Group.Healer,
        Role.Melee or Role.PhysicalRanged or Role.Caster => Group.Dps,
        _ => Group.None,
    };
}
