using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Core.Import;

/// <summary>
/// Checks a plan against a fight and the action catalog (imported, hand-written or LLM-written plans, docs/DESIGN.md §6):
/// every entry anchors to a mechanic of the fight with a valid hit index, every action exists and can be used at the
/// fight's sync level by the entry's job (or, for an entry without a job, by some job its slot or assignee allows), ids are
/// unique and assignee keys are declared.
/// </summary>
public static class PlanValidator
{
    /// <summary>The problems found (errors and warnings); empty for a valid plan.</summary>
    public static IReadOnlyList<ImportDiagnostic> Validate(MitPlan plan, FightDefinition fight, IActionCatalog actions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(actions);

        var problems = new List<ImportDiagnostic>();
        void Add(DiagnosticSeverity severity, string code, string message, string? src) => problems.Add(new ImportDiagnostic(severity, code, message, src));

        if (!string.Equals(plan.Fight, fight.Key, StringComparison.OrdinalIgnoreCase))
            Add(DiagnosticSeverity.Error, "wrong-fight", $"The plan is for fight '{plan.Fight}', not '{fight.Key}'.", null);
        if (string.IsNullOrWhiteSpace(plan.Id))
            Add(DiagnosticSeverity.Error, "missing-id", "The plan has no id.", null);

        var assignees = new Dictionary<string, Assignee>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in plan.Assignees)
        {
            if (string.IsNullOrWhiteSpace(a.Key))
                Add(DiagnosticSeverity.Error, "assignee-key", "An assignee has no key.", null);
            else if (!assignees.TryAdd(a.Key, a))
                Add(DiagnosticSeverity.Error, "duplicate-assignee", $"Two assignees are keyed '{a.Key}'.", null);
            if (a.Slot is { } slot && !Slots.Standard.Contains(slot))
                Add(DiagnosticSeverity.Warning, "assignee-slot", $"Assignee '{a.Key}' has the unknown slot '{slot}'.", null);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in plan.Entries)
        {
            var where = $"Entry '{e.Id}'";
            if (string.IsNullOrWhiteSpace(e.Id))
                Add(DiagnosticSeverity.Error, "missing-id", "An entry has no id.", e.Src);
            else if (!ids.Add(e.Id))
                Add(DiagnosticSeverity.Error, "duplicate-id", $"{where}: the id is used twice.", e.Src);

            var mechanic = fight.GetMechanic(e.Mechanic);
            if (mechanic is null)
                Add(DiagnosticSeverity.Error, "unknown-mechanic", $"{where}: {fight.Key} has no mechanic '{e.Mechanic}'.", e.Src);
            else if (e.HitIndex is { } k && (k < 0 || k >= Math.Max(1, mechanic.Hits.Count)))
                Add(DiagnosticSeverity.Error, "hit-index", $"{where}: {mechanic.Id} has {mechanic.Hits.Count} hit(s); hit index {k} does not exist.", e.Src);

            if (e.Offset is { } offset && (double.IsNaN(offset) || double.IsInfinity(offset) || Math.Abs(offset) > 600))
                Add(DiagnosticSeverity.Error, "offset", $"{where}: the offset {offset} is not a time near its mechanic.", e.Src);

            if (e.Who.Assignee is { } key && !assignees.ContainsKey(key))
                Add(DiagnosticSeverity.Warning, "unknown-assignee", $"{where}: assignee '{key}' is not one of the plan's assignees.", e.Src);
            if (e.Who.Slot is { } entrySlot && !Slots.Standard.Contains(entrySlot))
                Add(DiagnosticSeverity.Warning, "unknown-slot", $"{where}: unknown slot '{entrySlot}'.", e.Src);

            if (e.Do.Count == 0)
                Add(DiagnosticSeverity.Warning, "empty-entry", $"{where}: nothing to do.", e.Src);

            var jobs = JobsFor(e, assignees);
            foreach (var item in e.Do)
                CheckItem(item, e, where, jobs, fight.SyncLevel, actions, Add);
        }

        return problems;
    }

    /// <summary>The jobs an entry is for: its job, else its assignee's job, else the jobs of its slot, else every job.</summary>
    private static IReadOnlyList<Job> JobsFor(PlanEntry e, Dictionary<string, Assignee> assignees)
    {
        if (e.Who.Job is { } job && job != Job.None)
            return [job];
        if (e.Who.Assignee is { } key && assignees.TryGetValue(key, out var a) && a.Job is { } assigneeJob && assigneeJob != Job.None)
            return [assigneeJob];
        var slot = e.Who.Slot ?? (e.Who.Assignee is { } k && assignees.TryGetValue(k, out var b) ? b.Slot : null);
        Role[] roles = slot switch
        {
            "MT" or "OT" => [Role.Tank],
            "H1" or "H2" => [Role.Healer],
            "M1" or "M2" => [Role.Melee],
            "R1" or "R2" => [Role.PhysicalRanged, Role.Caster],
            _ => [],
        };
        return roles.Length == 0 ? Jobs.All : Jobs.All.Where(j => roles.Contains(Jobs.RoleOf(j))).ToList();
    }

    private static void CheckItem(PlanItem item, PlanEntry e, string where, IReadOnlyList<Job> jobs, int level, IActionCatalog actions, Action<DiagnosticSeverity, string, string, string?> add)
    {
        var kinds = (item.Action is not null ? 1 : 0) + (item.Any is not null ? 1 : 0) + (item.Note is not null ? 1 : 0);
        if (kinds != 1)
            add(DiagnosticSeverity.Error, "item-shape", $"{where}: an item must be exactly one of an action, an either/or group or a note ({item}).", e.Src);

        if (item.Note is { } note && string.IsNullOrWhiteSpace(note))
            add(DiagnosticSeverity.Warning, "empty-note", $"{where}: an empty note.", e.Src);

        if (item.Any is { } any)
        {
            if (any.Count == 0)
                add(DiagnosticSeverity.Error, "empty-any", $"{where}: an empty either/or group.", e.Src);
            foreach (var option in any)
                CheckItem(option, e, where, jobs, level, actions, add);
        }

        if (item.Action is not { } id)
            return;
        if (actions.Get(id) is not { } action)
        {
            add(DiagnosticSeverity.Error, "unknown-action", $"{where}: no action has the id {id}{(item.Name is null ? string.Empty : $" ('{item.Name}')")}.", e.Src);
            return;
        }

        if (!jobs.Any(action.UsableBy))
            add(DiagnosticSeverity.Error, "wrong-job", $"{where}: {action.Name} is not an action of {string.Join("/", jobs)}.", e.Src);
        else if (!action.AvailableAt(level))
            add(DiagnosticSeverity.Error, "not-available-at-level", $"{where}: {action.Name} is not available at level {level}.", e.Src);
        if (item.Name is { } name && !string.Equals(name, action.Name, StringComparison.Ordinal))
            add(DiagnosticSeverity.Warning, "action-name", $"{where}: action {id} is {action.Name}, not '{name}'.", e.Src);
    }
}
