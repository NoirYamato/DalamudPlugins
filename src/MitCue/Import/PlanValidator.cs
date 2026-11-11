using System.Globalization;
using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Import;

/// <summary>A problem shown in the review: from the importer (<see cref="FromImporter"/>) or from <see cref="PlanValidator"/>.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Code">Stable code ("unknown-mechanic", "not-usable-by-job", ...).</param>
/// <param name="Message">User-facing text.</param>
/// <param name="Src">Source cell, if any.</param>
/// <param name="FromImporter">True for diagnostics of the sheet importer.</param>
internal sealed record ReviewIssue(DiagnosticSeverity Severity, string Code, string Message, string? Src = null, bool FromImporter = false)
{
    /// <summary>Wraps an importer diagnostic.</summary>
    public static ReviewIssue From(ImportDiagnostic d) => new(d.Severity, d.Code, d.Message, d.Src, true);
}

/// <summary>
/// Checks a plan against the fight and the action catalog, for plans from the AI route (whose ids nobody has checked)
/// and after review edits: unknown mechanic ids, unknown action ids, actions the entry's job cannot use, actions not
/// available at the fight's sync level, missing who, odd offsets. Never throws for a well-formed plan object.
/// </summary>
internal static class PlanValidator
{
    /// <summary>Offsets beyond this many seconds are reported as suspicious.</summary>
    public const double MaxPlausibleOffset = 60;

    /// <summary>Problems of one entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="plan">The plan (for its assignees).</param>
    /// <param name="fight">The fight.</param>
    /// <param name="actions">The action catalog.</param>
    /// <param name="actionsLoaded">False when the catalog is a stand-in: action checks are skipped.</param>
    public static List<ReviewIssue> CheckEntry(PlanEntry entry, MitPlan plan, FightDefinition fight, IActionCatalog actions, bool actionsLoaded)
    {
        var issues = new List<ReviewIssue>();
        var ci = CultureInfo.InvariantCulture;
        var src = entry.Src;

        var mechanic = string.IsNullOrWhiteSpace(entry.Mechanic) ? null : fight.GetMechanic(entry.Mechanic);
        if (mechanic == null)
        {
            var message = string.IsNullOrWhiteSpace(entry.Mechanic)
                ? "No mechanic: choose the mechanic this call is for."
                : $"Unknown mechanic id \"{entry.Mechanic}\" for {fight.Key.ToUpperInvariant()}: choose the mechanic in the Mechanic column.";
            issues.Add(new ReviewIssue(DiagnosticSeverity.Error, "unknown-mechanic", message, src));
        }
        else if (entry.HitIndex is { } hitIndex && (hitIndex < 0 || (mechanic.Hits.Count > 0 && hitIndex >= mechanic.Hits.Count) || (mechanic.Hits.Count == 0 && hitIndex > 0)))
        {
            var hits = Math.Max(1, mechanic.Hits.Count);
            issues.Add(new ReviewIssue(
                DiagnosticSeverity.Warning,
                "hit-index",
                $"Hit #{(hitIndex + 1).ToString(ci)} does not exist: {mechanic.Name} has {hits.ToString(ci)} hit{(hits == 1 ? string.Empty : "s")}; the nearest one is used.",
                src));
        }

        if (entry.Offset is { } offset && (!double.IsFinite(offset) || Math.Abs(offset) > MaxPlausibleOffset))
        {
            issues.Add(new ReviewIssue(
                DiagnosticSeverity.Warning,
                "offset-range",
                $"An offset of {offset.ToString("+0.0;-0.0", ci)} s from the hit is unusually large: check the time.",
                src));
        }

        if (entry.Do is not { Count: > 0 })
            issues.Add(new ReviewIssue(DiagnosticSeverity.Warning, "no-items", "Nothing to press or read in this entry.", src));

        var who = entry.Who ?? new WhoSpec();
        var assignee = who.Assignee == null
            ? null
            : plan.Assignees.FirstOrDefault(a => string.Equals(a.Key, who.Assignee, StringComparison.OrdinalIgnoreCase));
        if (who.Job == null && string.IsNullOrWhiteSpace(who.Slot) && string.IsNullOrWhiteSpace(who.Assignee))
            issues.Add(new ReviewIssue(DiagnosticSeverity.Warning, "no-assignee", "No job, slot or column: this call is shown to everyone who uses the plan.", src));
        else if (who.Assignee != null && assignee == null && plan.Assignees.Count > 0)
            issues.Add(new ReviewIssue(DiagnosticSeverity.Info, "unknown-assignee", $"Column \"{who.Assignee}\" is not in the plan's column list.", src));

        var job = who.Job ?? assignee?.Job;
        if (job == Job.None)
            job = null;

        if (actionsLoaded && entry.Do != null)
        {
            foreach (var item in entry.Do)
            {
                if (item != null)
                    CheckItem(item, job, fight.SyncLevel, actions, src, issues);
            }
        }

        return issues;
    }

    /// <summary>Plan-level problems (no entries, actions not checked, ...).</summary>
    public static List<ReviewIssue> CheckPlan(MitPlan plan, FightDefinition fight, bool actionsLoaded)
    {
        var issues = new List<ReviewIssue>();
        if (plan.Entries.Count == 0)
            issues.Add(new ReviewIssue(DiagnosticSeverity.Warning, "no-entries", "The plan has no entries."));
        if (!string.IsNullOrWhiteSpace(plan.Fight) && !string.Equals(plan.Fight, fight.Key, StringComparison.OrdinalIgnoreCase))
            issues.Add(new ReviewIssue(DiagnosticSeverity.Warning, "fight-mismatch", $"The plan says fight \"{plan.Fight}\"; it is checked against {fight.Key} and will be saved for {fight.Key}."));
        if (!actionsLoaded)
            issues.Add(new ReviewIssue(DiagnosticSeverity.Info, "actions-not-checked", "The action list did not load, so action ids were not checked."));
        return issues;
    }

    /// <summary>Gives every entry a unique, non-empty id (e001, e002, ...). Returns how many ids changed.</summary>
    public static int NormalizeIds(MitPlan plan)
    {
        var taken = new HashSet<string>(
            plan.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)).Select(e => e.Id),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        var next = 1;
        foreach (var entry in plan.Entries)
        {
            // The first entry with an id keeps it; later duplicates and empty ids get fresh ones.
            if (!string.IsNullOrWhiteSpace(entry.Id) && seen.Add(entry.Id))
                continue;

            string id;
            do
            {
                id = "e" + next.ToString("000", CultureInfo.InvariantCulture);
                next++;
            }
            while (taken.Contains(id));

            taken.Add(id);
            seen.Add(id);
            entry.Id = id;
            changed++;
        }

        return changed;
    }

    /// <summary>Repairs null collections of a plan read from text (who, do, assignees, entries).</summary>
    public static void Repair(MitPlan plan)
    {
        plan.Assignees ??= [];
        plan.Entries ??= [];
        plan.Assignees.RemoveAll(a => a == null);
        plan.Entries.RemoveAll(e => e == null);
        foreach (var e in plan.Entries)
        {
            e.Who ??= new WhoSpec();
            e.Do ??= [];
            e.Do.RemoveAll(i => i == null);
            e.Mechanic ??= string.Empty;
        }
    }

    private static void CheckItem(PlanItem item, Job? job, int level, IActionCatalog actions, string? src, List<ReviewIssue> issues)
    {
        var ci = CultureInfo.InvariantCulture;
        if (item.Action is { } id)
        {
            var info = actions.Get(id);
            if (info == null)
            {
                var named = string.IsNullOrWhiteSpace(item.Name) ? string.Empty : $" (\"{item.Name}\")";
                issues.Add(new ReviewIssue(DiagnosticSeverity.Error, "unknown-action", $"Unknown action id {id.ToString(ci)}{named}: it is shown but never tracked.", src));
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.Name) && Simplify(item.Name) != Simplify(info.Name))
                issues.Add(new ReviewIssue(DiagnosticSeverity.Info, "action-name-mismatch", $"Action id {id.ToString(ci)} is {info.Name}, but the plan calls it \"{item.Name}\".", src));

            if (job is { } j && !info.UsableBy(j))
            {
                issues.Add(new ReviewIssue(DiagnosticSeverity.Error, "not-usable-by-job", $"{info.Name} is not a {j} action.", src));
                return;
            }

            if (!info.AvailableAt(level))
                issues.Add(new ReviewIssue(DiagnosticSeverity.Warning, "not-available-at-level", NotAvailableMessage(info, level), src));
            return;
        }

        if (item.Any is not { Count: > 0 } options)
            return;

        var infos = new List<ActionInfo>();
        foreach (var option in options)
        {
            if (option == null)
                continue;
            foreach (var optionId in option.ActionIds())
            {
                var info = actions.Get(optionId);
                if (info == null)
                    issues.Add(new ReviewIssue(DiagnosticSeverity.Error, "unknown-action", $"Unknown action id {optionId.ToString(ci)} in an either-or group: it is shown but never tracked.", src));
                else
                    infos.Add(info);
            }
        }

        if (infos.Count == 0)
            return;

        var usable = job is { } jj ? infos.Where(i => i.UsableBy(jj)).ToList() : infos;
        if (usable.Count == 0)
        {
            issues.Add(new ReviewIssue(DiagnosticSeverity.Error, "not-usable-by-job", $"None of {string.Join(" / ", infos.Select(i => i.Name))} is a {job} action.", src));
            return;
        }

        if (!usable.Any(i => i.AvailableAt(level)))
            issues.Add(new ReviewIssue(DiagnosticSeverity.Warning, "not-available-at-level", NotAvailableMessage(usable[0], level), src));
    }

    private static string NotAvailableMessage(ActionInfo info, int level)
    {
        var ci = CultureInfo.InvariantCulture;
        if (info.Level > level)
            return $"{info.Name} is learned at level {info.Level.ToString(ci)}, above this fight's level {level.ToString(ci)}.";
        if (info.ReplacedAtLevel is { } at && at <= level)
            return $"{info.Name} is replaced by its upgrade at level {at.ToString(ci)}; at level {level.ToString(ci)} the upgrade is pressed instead.";
        return $"{info.Name} is not available at level {level.ToString(ci)}.";
    }

    private static string Simplify(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
