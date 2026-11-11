using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Runtime;

/// <summary>
/// The calls shown dimmed next to the player's own when "Also show my co-healer's calls" is on (DESIGN D5: own calls
/// only, the co-healer's calls optional): the entries of the plan's other columns of the player's role, i.e. the
/// co-healer for a healer, the co-tank for a tank and the other DPS for a DPS. Columns for the player's own seat in
/// another team composition (LPDU's "H2 SGE" for the H2 Scholar, or the player's job in another tank pair) are not a
/// partner's, nor are the partner's columns for compositions without the player's job (LPDU's "DRK/GNB DRK" and
/// "DRK/PLD DRK" for a Warrior: their Dark Knight co-tank plays "WAR/DRK DRK"), and when the party is known, neither are
/// columns of jobs nobody in the party plays (LPDU's "H1 AST" when the co-healer is a White Mage).
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
    /// Entries that do not apply to <paramref name="me"/>, belong to the player's role and are not for the player's own
    /// seat: their job (or their column's job) is not the player's, and their slot (or their column's slot) is not the
    /// player's. An entry's role comes from its job, else its slot, else its column (the assignee's job or slot, or a key
    /// such as "H2" or "Scholar"); entries whose role cannot be told are left out. When a job has several columns and
    /// some of their keys name the player's job too (team compositions such as "WAR/DRK DRK"), only those are kept.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="me">The player.</param>
    /// <param name="partyJobs">The jobs in the player's party, or null/empty when unknown (solo, preview): entries for a
    /// job that is not in the party are then left out.</param>
    public static List<PlanEntry> For(MitPlan plan, PlayerContext me, IReadOnlyCollection<Job>? partyJobs = null)
    {
        var result = new List<PlanEntry>();
        var mine = GroupOf(Jobs.RoleOf(me.Job));
        if (mine == Group.None)
            return result;

        var mySlot = Slots.TryNormalize(me.Slot, out var s) ? s : null;
        var party = partyJobs is { Count: > 0 } ? partyJobs : null;
        var otherCompositions = CompositionsWithout(plan, me.Job);
        foreach (var entry in plan.Entries)
        {
            if (PlanSelection.Applies(entry, me) || GroupOf(RoleOf(plan, entry)) != mine)
                continue;

            var column = ColumnOf(plan, entry);
            var job = JobOf(entry, column);
            if (job == me.Job)
                continue; // the player's job in another column: their own seat in another composition
            if (mySlot != null && SlotOf(entry, column) == mySlot)
                continue; // the player's slot in another column (another job for it, or its job-less column)
            if (column != null && otherCompositions.Contains(column))
                continue; // the partner's job in a team composition without the player's job
            if (party != null && job != null && !party.Contains(job.Value))
                continue; // a job nobody in the party plays

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

        var assignee = ColumnOf(plan, entry);
        if (assignee?.Job is { } assigneeJob && assigneeJob != Job.None)
            return Jobs.RoleOf(assigneeJob);
        if (Slots.TryNormalize(assignee?.Slot, out slot) || Slots.TryNormalize(key, out slot))
            return Slots.RoleOf(slot);
        return Jobs.TryParse(key, out var keyJob) ? Jobs.RoleOf(keyJob) : Role.None;
    }

    /// <summary>
    /// Columns of another job for team compositions without <paramref name="myJob"/>: when a job has several columns and
    /// the keys of some of them name <paramref name="myJob"/> too (<see cref="LayoutDetector.JobsFromTabName"/>, which also
    /// reads the xlsx form "WARDRK DRK"), the others. For a Warrior on LPDU: "DRK/GNB DRK", "DRK/PLD DRK", "DRK/GNB GNB",
    /// "GNB/PLD GNB", "DRK/PLD PLD" and "GNB/PLD PLD". Columns of a job none of whose keys name the player's job (Carry the
    /// Clear's "Scholar", a column per phase) are all kept.
    /// </summary>
    private static HashSet<Assignee> CompositionsWithout(MitPlan plan, Job myJob)
    {
        var result = new HashSet<Assignee>();
        if (myJob == Job.None)
            return result;
        foreach (var group in plan.Assignees.Where(a => a?.Job is { } j && j != Job.None && j != myJob).GroupBy(a => a.Job))
        {
            var columns = group.ToList();
            if (columns.Count < 2)
                continue;
            var withMine = columns.Where(a => LayoutDetector.JobsFromTabName(a.Key).Contains(myJob)).ToList();
            if (withMine.Count == 0)
                continue;
            result.UnionWith(columns.Except(withMine));
        }

        return result;
    }

    private static Assignee? ColumnOf(MitPlan plan, PlanEntry entry) =>
        entry.Who.Assignee is { } key
            ? plan.Assignees.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>The entry's job, else its column's job, else a job named by the column key ("Scholar"); null when none.</summary>
    private static Job? JobOf(PlanEntry entry, Assignee? column)
    {
        if (entry.Who.Job is { } job && job != Job.None)
            return job;
        if (column?.Job is { } columnJob && columnJob != Job.None)
            return columnJob;
        return entry.Who.Assignee is { } key && Jobs.TryParse(key, out var keyJob) ? keyJob : null;
    }

    /// <summary>The entry's slot, else its column's slot, else a slot named by the column key ("H2"); null when none.</summary>
    private static string? SlotOf(PlanEntry entry, Assignee? column)
    {
        if (Slots.TryNormalize(entry.Who.Slot, out var slot))
            return slot;
        if (Slots.TryNormalize(column?.Slot, out slot))
            return slot;
        return entry.Who.Assignee is { } key && Slots.TryNormalize(key, out slot) ? slot : null;
    }

    private static Group GroupOf(Role role) => role switch
    {
        Role.Tank => Group.Tank,
        Role.Healer => Group.Healer,
        Role.Melee or Role.PhysicalRanged or Role.Caster => Group.Dps,
        _ => Group.None,
    };
}
