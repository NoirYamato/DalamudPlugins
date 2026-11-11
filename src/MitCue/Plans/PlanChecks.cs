using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Data;

namespace MitCue.Plans;

/// <summary>
/// Checks plans that reach the store without the import window's review (share codes and plan files in the plans
/// folder) with the core <see cref="PlanValidator"/>: mechanics and hit indexes of the fight, action ids, and actions
/// the entry's job (or slot) can use at the fight's sync level (DESIGN §1.3). <see cref="PlanStore.Check"/> uses it.
/// </summary>
internal static class PlanChecks
{
    /// <summary>The problems found, as messages; empty when there are none or the fight data and actions did not load.</summary>
    public static IReadOnlyList<string> Find(MitPlan plan, CoreData data)
    {
        if (data.Fights == null || !data.ActionsLoaded)
            return [];
        if (data.GetFight(plan.Fight) is not { } fight)
            return [$"This MitCue has no fight \"{plan.Fight}\" (it has {string.Join(", ", data.FightList.Select(f => f.Key))}): the plan is never used."];
        return PlanValidator.Validate(plan, fight, data.Actions).Select(d => d.Message).ToList();
    }

    /// <summary>"2 problems: A; B" (at most <paramref name="max"/> messages, then "and N more").</summary>
    public static string Summary(IReadOnlyList<string> problems, int max = 3)
    {
        var shown = string.Join("; ", problems.Take(max).Select(p => p.TrimEnd('.')));
        var more = problems.Count > max ? $"; and {problems.Count - max} more" : string.Empty;
        return $"{problems.Count} problem{(problems.Count == 1 ? string.Empty : "s")}: {shown}{more}.";
    }
}
