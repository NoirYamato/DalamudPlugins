using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Data;
using MitCue.Import;

namespace MitCue.Plans;

/// <summary>
/// Checks plans that reach the store without the import window's review (share codes and plan files in the plans
/// folder) with the review's own rules (<see cref="ReviewValidator"/>), and reports only what makes a call impossible
/// in game: a mechanic the fight does not have (the call is never shown), an unknown action, and an action (or every
/// option of an either/or) that the entry's job (or slot) cannot use, or not at the fight's sync level (DESIGN §1.3).
/// What the runtime does not mind is not a problem: an action name spelled another way, a hit index past the last hit
/// (the nearest hit is used), an unusual offset. <see cref="PlanStore.Check"/> uses it.
/// </summary>
internal static class PlanChecks
{
    /// <summary>The review codes of the problems that make a call impossible.</summary>
    private static readonly HashSet<string> Impossible = new(StringComparer.Ordinal)
    {
        "unknown-mechanic",
        "unknown-action",
        "not-usable-by-job",
        "not-available-at-level",
    };

    /// <summary>The problems found, as messages; empty when there are none or the fight data and actions did not load.</summary>
    public static IReadOnlyList<string> Find(MitPlan plan, CoreData data)
    {
        if (data.Fights == null || !data.ActionsLoaded)
            return [];
        if (data.GetFight(plan.Fight) is not { } fight)
            return [$"This MitCue has no fight \"{plan.Fight}\" (it has {string.Join(", ", data.FightList.Select(f => f.Key))}): the plan is never used."];

        var problems = new List<string>();
        foreach (var entry in plan.Entries)
        {
            if (entry == null)
                continue;
            foreach (var issue in ReviewValidator.CheckEntry(entry, plan, fight, data.Actions, actionsLoaded: true))
            {
                if (Impossible.Contains(issue.Code))
                    problems.Add(Describe(entry, issue, fight));
            }
        }

        return problems;
    }

    /// <summary>"2 problems: A; B" (at most <paramref name="max"/> messages, then "and N more").</summary>
    public static string Summary(IReadOnlyList<string> problems, int max = 3)
    {
        var shown = string.Join("; ", problems.Take(max).Select(p => p.TrimEnd('.')));
        var more = problems.Count > max ? $"; and {problems.Count - max} more" : string.Empty;
        return $"{problems.Count} problem{(problems.Count == 1 ? string.Empty : "s")}: {shown}{more}.";
    }

    private static string Describe(PlanEntry entry, ReviewIssue issue, FightDefinition fight)
    {
        var where = $"Entry '{entry.Id}'";
        return issue.Code switch
        {
            // The review's message tells the player to pick the mechanic in its table, which a share code never shows.
            "unknown-mechanic" => string.IsNullOrWhiteSpace(entry.Mechanic)
                ? $"{where} has no mechanic, so the call is never shown."
                : $"{where}: {fight.Key.ToUpperInvariant()} has no mechanic \"{entry.Mechanic}\", so the call is never shown.",
            "unknown-action" => $"{where}: {issue.Message}",
            _ => $"{where}: {issue.Message.TrimEnd('.')}, so the call can never be done.",
        };
    }
}
