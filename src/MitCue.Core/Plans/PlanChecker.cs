using System.Globalization;
using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Reminders;

namespace MitCue.Core.Plans;

/// <summary>How serious a <see cref="PlanFinding"/> is.</summary>
public enum PlanFindingSeverity
{
    /// <summary>Worth knowing; nothing is wrong.</summary>
    Info,

    /// <summary>Probably wrong, or wrong only in some pulls (phase pushes, optional uses, assumptions).</summary>
    Warning,

    /// <summary>The plan cannot be followed as written.</summary>
    Error,
}

/// <summary>One problem <see cref="PlanChecker"/> found in a player's plan entries.</summary>
public sealed class PlanFinding
{
    public required PlanFindingSeverity Severity { get; init; }

    /// <summary>
    /// Stable machine code: <c>unknown-mechanic</c>, <c>hit-index</c>, <c>empty-entry</c>, <c>empty-item</c>,
    /// <c>unknown-action</c>, <c>name-mismatch</c>, <c>wrong-job</c>, <c>not-available-at-level</c>,
    /// <c>level-variant</c>, <c>recast-conflict</c>, <c>recast-tight</c>, <c>resource-shortage</c> or
    /// <c>follow-up-without-trigger</c>.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Human-readable explanation, with the times involved (phase-relative, e.g. "P2 1:39").</summary>
    public required string Message { get; init; }

    /// <summary>
    /// Timeline seconds of the planned press the finding is about (the later use of a conflict), on the
    /// timeline's nominal times; null when the entry cannot be timed (unknown mechanic).
    /// </summary>
    public double? Time { get; init; }

    /// <summary>The entry the finding is about (the later use of a conflict), if any.</summary>
    public PlanEntry? Entry { get; init; }

    /// <summary>Other entries involved, oldest first (the earlier uses of a recast conflict or resource shortage).</summary>
    public IReadOnlyList<PlanEntry> Related { get; init; } = [];

    /// <summary>The action concerned, when there is one.</summary>
    public uint? ActionId { get; init; }

    /// <summary>
    /// True for best-effort findings that rest on assumptions about the game (job gauges, follow-up windows); the
    /// message says which.
    /// </summary>
    public bool Heuristic { get; init; }

    public override string ToString() =>
        $"{this.Severity.ToString().ToUpperInvariant()} {this.Code}{(this.Heuristic ? " (heuristic)" : string.Empty)}: {this.Message}";
}

/// <summary>Settings for <see cref="PlanChecker"/>.</summary>
public sealed class PlanCheckOptions
{
    /// <summary>A use whose action (or charge) is ready less than this many seconds before it is "tight". Default 5.</summary>
    public double TightMargin { get; init; } = 5;

    /// <summary>
    /// Actions with a shorter recast are not checked for recast conflicts: GCDs and 1 s abilities are limited by the GCD or
    /// by resources, not by a cooldown (a double Succor is fine). Default 3.
    /// </summary>
    public double MinRecast { get; init; } = 3;

    /// <summary>Check job gauges (Lily, Addersgall, Aetherflow). Default true.</summary>
    public bool CheckResources { get; init; } = true;

    /// <summary>Check that follow-up actions (Consolation, Divine Caress, ...) have their trigger planned shortly before. Default true.</summary>
    public bool CheckFollowUps { get; init; } = true;
}

/// <summary>
/// Checks a player's plan entries against a fight and the action catalog: unknown mechanics and actions, actions the job
/// cannot use at the fight's sync level, recast conflicts, and (best effort) job gauges and follow-up actions.
/// Reusable by the plugin; pure logic.
/// </summary>
/// <remarks>
/// <para>
/// Press times are the reminder press times (<see cref="ReminderScheduler"/>: hit + offset, or the default offset of
/// docs/DESIGN.md §5.1) on the timeline's nominal times. Within a phase these are exact: the boss's script does not
/// change. Across a phase transition they are not: a phase that is pushed (killed) early makes everything after it
/// happen sooner than on the timeline (cactbot timelines place each phase start after the previous phase's enrage),
/// and a use planned late in a phase does not happen at all when the phase ends before it.
/// </para>
/// <para>
/// Recast rule: consecutive planned uses of an action, or of actions sharing a recast group
/// (<see cref="ActionInfo.SharesRecastWith"/>), are replayed with the action's recast and charges at the sync level
/// (charges recharge one at a time). A use that finds no charge is a <c>recast-conflict</c>: an error when every use
/// involved belongs to one phase (by its mechanic), otherwise a warning, since it depends on when the earlier phase
/// ends; also a warning when an optional or when-ready use is involved. A use whose charge was ready less than
/// <see cref="PlanCheckOptions.TightMargin"/> seconds before is <c>recast-tight</c> (warning). The conflicting use is
/// assumed to happen anyway, so each consecutive pair closer than the recast is reported. Either/or groups are left
/// out (the player picks whichever option is ready), as are actions with a recast under
/// <see cref="PlanCheckOptions.MinRecast"/>.
/// </para>
/// <para>
/// Gauges (warnings marked <see cref="PlanFinding.Heuristic"/>): Lily (WHM: 3 at most, +1 every 20 s in combat, full at
/// the pull since 7.4), Addersgall (SGE: 3 at most, +1 every 20 s, +1 from Rhizomata, assumed full at the pull) and
/// Aetherflow (SCH: 3 at most, refilled to 3 by Aetherflow, 60 s recast, in combat only, and by planned Dissipations;
/// empty at the pull; Recitation makes the next Indomitability or Excogitation free). Refills that are not planned are
/// assumed to be pressed as soon as they are ready and would not overfill the gauge, gauges regenerate on nominal times
/// and start as stated: the check is optimistic, so a shortage it reports is real unless the gauge was fuller at the pull.
/// </para>
/// </remarks>
public static class PlanChecker
{
    /// <summary>Follow-up triggers with a shorter recast (Eukrasia, Play II/III, Minor Arcana) are pressed right before their follow-up and never planned.</summary>
    private const double MinTriggerRecast = 30;

    private const double Epsilon = 1e-6;

    /// <summary>Lily, Addersgall and Aetherflow (docs/research/actions.md §10; data/actions/actions.yaml notes).</summary>
    private static readonly GaugeModel[] Gauges =
    [
        new()
        {
            Resource = "Lily",
            Job = Job.WHM,
            Start = 3,
            RegenEvery = 20,
            Basis = "3 at most, +1 every 20 s in combat, full at the pull",
        },
        new()
        {
            Resource = "Addersgall",
            Job = Job.SGE,
            Start = 3,
            RegenEvery = 20,
            Refills = [new GaugeRefill("Rhizomata", 1, Auto: true)],
            Basis = "3 at most, +1 every 20 s in combat and +1 per Rhizomata, assumed full at the pull",
        },
        new()
        {
            Resource = "Aetherflow",
            Job = Job.SCH,
            Start = 0,
            Refills = [new GaugeRefill("Aetherflow", 3, Auto: true), new GaugeRefill("Dissipation", 3, Auto: false)],
            FreeTrigger = "Recitation",
            FreeConsumers = ["Indomitability", "Excogitation", "Adloquium", "Succor", "Concitation"],
            Basis = "3 at most, refilled by Aetherflow (in combat, as soon as it is ready) and by planned Dissipations, empty at the pull",
        },
    ];

    /// <summary>Checks the entries of <paramref name="plan"/> that apply to <paramref name="me"/>, at <see cref="PlayerContext.Level"/>.</summary>
    public static IReadOnlyList<PlanFinding> Check(FightDefinition fight, IActionCatalog actions, MitPlan plan, PlayerContext me, PlanCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(me);
        return Check(fight, actions, PlanSelection.For(plan, me), me.Job, me.Level, options);
    }

    /// <summary>
    /// Checks a player's entries (already selected, e.g. with <see cref="PlanSelection.For"/>) for <paramref name="job"/>
    /// (<see cref="Job.None"/> skips the job checks) at <paramref name="syncLevel"/>.
    /// </summary>
    /// <returns>The findings in time order (untimed ones first), errors before warnings at equal times.</returns>
    public static IReadOnlyList<PlanFinding> Check(FightDefinition fight, IActionCatalog actions, IEnumerable<PlanEntry> entries, Job job, int syncLevel, PlanCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(entries);

        var run = new Run(fight, actions, entries.Where(e => e != null).ToList(), job, syncLevel, options ?? new PlanCheckOptions());
        run.CheckMechanics();
        run.CheckItems();
        run.CheckRecasts();
        if (run.Options.CheckResources)
            run.CheckGauges();
        if (run.Options.CheckFollowUps)
            run.CheckFollowUps();
        return run.Sorted();
    }

    /// <summary>
    /// A timeline time as the phase id and the time from the phase start, e.g. "P2 1:39" or "P1 -0:05" (whole seconds).
    /// </summary>
    /// <param name="fight">The fight (for its phases).</param>
    /// <param name="time">Timeline seconds.</param>
    /// <param name="phaseId">The phase to count from; null = the phase containing <paramref name="time"/>.</param>
    public static string FormatTime(FightDefinition fight, double time, string? phaseId = null)
    {
        ArgumentNullException.ThrowIfNull(fight);
        var phase = (phaseId != null ? fight.GetPhase(phaseId) : null) ?? fight.PhaseAt(time) ?? fight.Phases.OrderBy(p => p.Start).FirstOrDefault();
        return phase == null ? Clock(time) : phase.Id.ToUpperInvariant() + " " + Clock(time - phase.Start);
    }

    /// <summary>"m:ss" rounded to whole seconds, with a minus sign before the pull/phase start.</summary>
    private static string Clock(double seconds)
    {
        var total = (long)Math.Round(Math.Abs(seconds), MidpointRounding.AwayFromZero);
        var sign = seconds < 0 && total > 0 ? "-" : string.Empty;
        return sign + (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Seconds with at most one decimal ("100", "99.5").</summary>
    private static string Num(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Ordinal(int n) => n + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    private static string JobList(ActionInfo action) => action.Jobs.Count == 0 ? "no job's" : string.Join("/", action.Jobs);

    /// <summary>The entries in order, each once (by reference).</summary>
    private static List<PlanEntry> DistinctEntries(IEnumerable<PlanEntry> entries)
    {
        var seen = new HashSet<PlanEntry>(ReferenceEqualityComparer.Instance);
        return entries.Where(seen.Add).ToList();
    }

    /// <summary>Levenshtein distance (small strings only).</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>A gauge MitCue knows how to replay.</summary>
    private sealed class GaugeModel
    {
        /// <summary>The <see cref="ResourceCost.Resource"/> its spenders name.</summary>
        public required string Resource { get; init; }

        public required Job Job { get; init; }

        public int Capacity { get; init; } = 3;

        /// <summary>Amount at the pull.</summary>
        public required int Start { get; init; }

        /// <summary>Seconds per +1 in combat (from the pull); 0 = no regeneration.</summary>
        public double RegenEvery { get; init; }

        public IReadOnlyList<GaugeRefill> Refills { get; init; } = [];

        /// <summary>An action that makes the next consumer free (Recitation), by name.</summary>
        public string? FreeTrigger { get; init; }

        /// <summary>Actions that use up <see cref="FreeTrigger"/>, by name (spenders among them are then free).</summary>
        public IReadOnlyList<string> FreeConsumers { get; init; } = [];

        /// <summary>The assumptions, for messages.</summary>
        public required string Basis { get; init; }
    }

    /// <summary>An action that adds to a gauge; <paramref name="Auto"/>: assumed pressed whenever useful when the plan does not say.</summary>
    private sealed record GaugeRefill(string Action, int Amount, bool Auto);

    /// <summary>One planned press of one action (an item of an entry), for the recast and gauge replays.</summary>
    private sealed record Use(Reminder Reminder, ActionInfo Action, int Order)
    {
        public double T => this.Reminder.PressAt;

        public PlanEntry Entry => this.Reminder.Entry;

        public string Phase => this.Reminder.Mechanic.Phase;
    }

    private sealed class Run
    {
        private readonly FightDefinition fight;
        private readonly IActionCatalog actions;
        private readonly List<PlanEntry> entries;
        private readonly Dictionary<PlanEntry, int> entryIndex = new(ReferenceEqualityComparer.Instance);
        private readonly Job job;
        private readonly int level;
        private readonly List<(PlanFinding Finding, int Order)> findings = [];
        private readonly IReadOnlyList<Reminder> reminders;
        private readonly Dictionary<PlanEntry, double> pressTimes = new(ReferenceEqualityComparer.Instance);

        public Run(FightDefinition fight, IActionCatalog actions, List<PlanEntry> entries, Job job, int level, PlanCheckOptions options)
        {
            this.fight = fight;
            this.actions = actions;
            this.entries = entries;
            this.job = job;
            this.level = level;
            this.Options = options;
            for (var i = 0; i < entries.Count; i++)
                this.entryIndex.TryAdd(entries[i], i);

            // The reminder press times: exactly what the overlay will use.
            this.reminders = new ReminderScheduler(fight, entries, actions, new ReminderOptions()).All;
            foreach (var r in this.reminders)
                this.pressTimes.TryAdd(r.Entry, r.PressAt);
        }

        public PlanCheckOptions Options { get; }

        public void CheckMechanics()
        {
            foreach (var entry in this.entries)
            {
                var mechanic = this.fight.GetMechanic(entry.Mechanic ?? string.Empty);
                if (mechanic == null)
                {
                    this.Add(PlanFindingSeverity.Error, "unknown-mechanic",
                        $"{this.Describe(entry)}: unknown mechanic '{entry.Mechanic}' in fight '{this.fight.Key}'{this.Suggest(entry.Mechanic)}; it is never reminded.",
                        entry, null);
                    continue;
                }

                var hits = Math.Max(1, mechanic.Hits.Count);
                if (entry.HitIndex is { } index && (index < 0 || index >= hits))
                {
                    var used = index < 0 ? "first" : "last";
                    this.Add(PlanFindingSeverity.Warning, "hit-index",
                        $"{this.Describe(entry)}: the {Ordinal(index + 1)} hit of {mechanic.Name} is planned, but it has {hits} hit{(hits == 1 ? string.Empty : "s")}; the {used} hit is used.",
                        entry, this.pressTimes.TryGetValue(entry, out var t) ? t : null);
                }
            }
        }

        public void CheckItems()
        {
            foreach (var entry in this.entries)
            {
                double? time = this.pressTimes.TryGetValue(entry, out var t) ? t : null;
                var items = entry.Do ?? [];
                if (items.Count == 0)
                {
                    this.Add(PlanFindingSeverity.Info, "empty-entry", $"{this.Describe(entry)} has nothing to press or read.", entry, time);
                    continue;
                }

                foreach (var item in items)
                {
                    if (item == null)
                        continue;
                    if (item.Any != null)
                        this.CheckGroup(entry, item, time);
                    else if (item.Action is { } id)
                        this.CheckAction(entry, id, item.Name, time);
                    else if (item.Note == null)
                        this.Add(PlanFindingSeverity.Warning, "empty-item", $"{this.Describe(entry)} has an item with no action, choice or note.", entry, time);
                }
            }
        }

        public void CheckRecasts()
        {
            var uses = new List<Use>();
            foreach (var r in this.reminders)
            {
                if (r.Item.Action is not { } id || r.Item.Any != null || this.Usable(id) is not { } action)
                    continue;
                if (action.RecastAt(this.level) >= this.Options.MinRecast)
                    uses.Add(new Use(r, action, uses.Count));
            }

            // Chains of actions that share a recast (the same action, or one recast group of the job).
            var distinct = uses.Select(u => u.Action).DistinctBy(a => a.Id).ToList();
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

            foreach (var chain in uses.GroupBy(u => chainOf[u.Action.Id]).OrderBy(g => g.Key))
                this.ReplayRecast(chain.OrderBy(u => u.T).ThenBy(u => u.Order).ToList());
        }

        public void CheckGauges()
        {
            foreach (var model in Gauges)
            {
                if (this.job != Job.None && this.job != model.Job)
                    continue;
                this.ReplayGauge(model);
            }
        }

        public void CheckFollowUps()
        {
            foreach (var r in this.reminders)
            {
                if (r.Item.Action is not { } id || r.Item.Any != null || this.Usable(id) is not { } action)
                    continue;
                if (action.FollowUpOf is not { } triggerId || this.actions.Get(triggerId) is not { } trigger)
                    continue;
                if (trigger.RecastAt(this.level) < MinTriggerRecast)
                    continue;

                var window = trigger.Effects.Select(e => e.DurationAt(this.level) ?? 0).Append(trigger.DurationAt(this.level) ?? 0).Max();
                if (window <= 0)
                    continue;

                var before = this.reminders
                    .Where(x => x.PressAt <= r.PressAt + Epsilon && this.ResolvedIds(x.Item).Contains(trigger.Id))
                    .Select(x => x.PressAt)
                    .ToList();
                if (before.Any(t => r.PressAt - t <= window + Epsilon))
                    continue;

                var last = before.Count > 0 ? before.Max() : (double?)null;
                var tail = last is { } l
                    ? $"; the last one before it is planned at {this.At(l, r.Mechanic.Phase)} ({Num(r.PressAt - l)} s before)"
                    : "; the plan has none before it";
                this.Add(PlanFindingSeverity.Warning, "follow-up-without-trigger",
                    $"{action.Name} at {this.At(r)} can only be used within {Num(window)} s after {trigger.Name}{tail}.",
                    r.Entry, r.PressAt, actionId: action.Id, heuristic: true);
            }
        }

        public IReadOnlyList<PlanFinding> Sorted() =>
            this.findings
                .OrderBy(f => f.Finding.Time ?? double.NegativeInfinity)
                .ThenByDescending(f => f.Finding.Severity)
                .ThenBy(f => f.Finding.Entry is { } e && this.entryIndex.TryGetValue(e, out var i) ? i : -1)
                .ThenBy(f => f.Order)
                .Select(f => f.Finding)
                .ToList()
                .AsReadOnly();

        /// <summary>
        /// Replays the uses of one recast chain in time order: charges recharge one at a time, each use restarts an idle
        /// timer with its own recast; a use without a charge is reported and assumed to happen anyway (restarting the timer).
        /// </summary>
        private void ReplayRecast(List<Use> uses)
        {
            var maxCharges = Math.Max(1, uses.Max(u => u.Action.ChargesAt(this.level)));
            var charges = maxCharges;
            double? timerEnd = null;
            double timerRecast = 0;
            double? availableSince = double.NegativeInfinity; // charges ≥ 1 since then (null: none left)
            var sinceFull = new List<Use>();
            Use? previous = null;

            foreach (var use in uses)
            {
                var recast = use.Action.RecastAt(this.level);
                while (timerEnd is { } end && end <= use.T + Epsilon)
                {
                    charges++;
                    if (charges == 1)
                        availableSince = end;
                    if (charges >= maxCharges)
                    {
                        charges = maxCharges;
                        timerEnd = null;
                        sinceFull.Clear();
                    }
                    else
                    {
                        timerEnd = end + timerRecast;
                    }
                }

                List<Use> involved;
                if (maxCharges > 1)
                    involved = [.. sinceFull, use];
                else if (previous != null)
                    involved = [previous, use];
                else
                    involved = [use];
                if (charges >= 1)
                {
                    var margin = use.T - (availableSince ?? use.T);
                    if (margin < this.Options.TightMargin && previous != null && involved.Count > 1)
                        this.ReportRecast(maxCharges > 1 ? involved[Math.Max(0, involved.Count - maxCharges - 1)..] : involved, maxCharges, recastConflict: false, margin);
                    charges--;
                    if (timerEnd == null)
                    {
                        timerEnd = use.T + recast;
                        timerRecast = recast;
                    }

                    if (charges == 0)
                        availableSince = null;
                }
                else
                {
                    this.ReportRecast(maxCharges > 1 ? this.ShortestInfeasibleTail(involved, maxCharges) : involved, maxCharges, recastConflict: true, (timerEnd ?? use.T) - use.T);
                    timerEnd = use.T + recast;
                    timerRecast = recast;
                    availableSince = null;
                }

                sinceFull.Add(use);
                previous = use;
            }
        }

        /// <summary>
        /// The shortest tail of <paramref name="uses"/> (at least the last <paramref name="maxCharges"/> + 1) that cannot all be
        /// made starting with full charges: the uses that explain a conflict. All of them when no tail is infeasible.
        /// </summary>
        private List<Use> ShortestInfeasibleTail(List<Use> uses, int maxCharges)
        {
            for (var start = Math.Max(0, uses.Count - maxCharges - 1); start >= 0; start--)
            {
                var tail = uses.GetRange(start, uses.Count - start);
                if (!this.FeasibleFromFull(tail, maxCharges))
                    return tail;
            }

            return uses;
        }

        /// <summary>True when every use finds a charge, starting with full charges (one-at-a-time recharge).</summary>
        private bool FeasibleFromFull(List<Use> uses, int maxCharges)
        {
            var charges = maxCharges;
            double? timerEnd = null;
            double timerRecast = 0;
            foreach (var use in uses)
            {
                while (timerEnd is { } end && end <= use.T + Epsilon)
                {
                    charges++;
                    if (charges >= maxCharges)
                    {
                        charges = maxCharges;
                        timerEnd = null;
                    }
                    else
                    {
                        timerEnd = end + timerRecast;
                    }
                }

                if (charges == 0)
                    return false;
                charges--;
                if (timerEnd == null)
                {
                    timerRecast = use.Action.RecastAt(this.level);
                    timerEnd = use.T + timerRecast;
                }
            }

            return true;
        }

        /// <param name="involved">The uses that explain the finding (the previous use for single-charge actions), ending with the use reported.</param>
        /// <param name="charges">Max charges at the level.</param>
        /// <param name="recastConflict">True: no charge was ready; false: the use is tight.</param>
        /// <param name="seconds">How early the use is (conflict), or how long its charge was ready before it (tight).</param>
        private void ReportRecast(List<Use> involved, int charges, bool recastConflict, double seconds)
        {
            var use = involved[^1];
            var first = involved[0];
            var recast = use.Action.RecastAt(this.level);
            var sameAction = involved.All(u => u.Action.Id == use.Action.Id);

            string what;
            if (charges == 1)
            {
                var gap = use.T - first.T;
                what = sameAction
                    ? $"{use.Action.Name} planned at {this.At(first)} and {this.At(use)} ({Num(gap)} s apart), recast {Num(recast)} s"
                    : $"{first.Action.Name} at {this.At(first)} and {use.Action.Name} at {this.At(use)} share a recast ({Num(gap)} s apart), recast {Num(recast)} s";
            }
            else
            {
                var name = sameAction ? use.Action.Name : string.Join("/", involved.Select(u => u.Action.Name).Distinct());
                what = $"{name} planned {involved.Count} times between {this.At(first)} and {this.At(use)} ({Num(use.T - first.T)} s), {charges} charges, recast {Num(recast)} s";
            }

            var phases = involved.Select(u => u.Phase).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var crossPhase = phases.Count > 1;
            var optional = involved.Any(u => u.Entry.Optional);
            var whenReady = use.Entry.WhenReady;

            string message;
            PlanFindingSeverity severity;
            string code;
            if (recastConflict)
            {
                code = "recast-conflict";
                message = what + (charges == 1 ? string.Empty : $": the use at {this.At(use)} is {Num(seconds)} s early");
                severity = crossPhase || optional || whenReady ? PlanFindingSeverity.Warning : PlanFindingSeverity.Error;
                if (whenReady)
                    message += $" (planned when ready: it would be pressed {Num(seconds)} s late)";
            }
            else
            {
                code = "recast-tight";
                message = what + (charges == 1
                    ? $": only {Num(seconds)} s to spare"
                    : $": the charge for the use at {this.At(use)} is ready only {Num(seconds)} s before");
                severity = PlanFindingSeverity.Warning;
            }

            if (optional)
                message += " (an optional use is involved)";
            if (crossPhase)
            {
                var from = phases[0].ToUpperInvariant();
                message += $"; it spans the {string.Join(" → ", phases.Select(p => p.ToUpperInvariant()))} transition, so the real gap depends on when {from} ends";
            }

            this.Add(severity, code, message + ".", use.Entry, use.T, actionId: use.Action.Id,
                related: DistinctEntries(involved.Take(involved.Count - 1).Select(u => u.Entry)));
        }

        /// <summary>Replays one gauge over the planned spenders and refills (see the class remarks for the assumptions).</summary>
        private void ReplayGauge(GaugeModel model)
        {
            var refills = model.Refills
                .Select(r => (Rule: r, Action: this.FindAction(r.Action)))
                .Where(r => r.Action != null && (this.job == Job.None || r.Action.UsableBy(this.job)) && r.Action.AvailableAt(this.level))
                .Select(r => (r.Rule, Action: r.Action!))
                .ToList();
            var freeTrigger = model.FreeTrigger != null ? this.FindAction(model.FreeTrigger) : null;
            if (freeTrigger != null && !freeTrigger.AvailableAt(this.level))
                freeTrigger = null;
            var consumers = model.FreeConsumers.Select(this.FindAction).Where(a => a != null).Select(a => a!.Id).ToHashSet();

            // Planned events, in reminder order (press time, entry order, item index).
            var events = new List<(Reminder R, int Spend, int Refill, bool Trigger, bool Consumer, ActionInfo? Action)>();
            foreach (var r in this.reminders)
            {
                var spend = this.SpendOf(r.Item, model.Resource, out var spender);
                var single = r.Item.Action is { } id && r.Item.Any == null ? this.Usable(id) : null;
                var refill = single != null ? refills.FindIndex(x => x.Action.Id == single.Id) : -1;
                var trigger = single != null && freeTrigger != null && single.Id == freeTrigger.Id;
                var consumer = single != null && consumers.Contains(single.Id);
                if (spend > 0 || refill >= 0 || trigger || consumer)
                    events.Add((r, spend, refill, trigger, consumer, spender ?? single));
            }

            if (!events.Any(e => e.Spend > 0))
                return;

            var gauge = model.Start;
            var ticks = 0;
            var readyAt = refills.Select(_ => 0.0).ToArray();
            double? freeUntil = null;
            double? windowStart = null; // first spend since the gauge was last full
            var windowSpends = new List<Reminder>();
            var now = Math.Min(0, events[0].R.PressAt);

            void MarkFull()
            {
                if (gauge >= model.Capacity)
                {
                    windowStart = null;
                    windowSpends.Clear();
                }
            }

            MarkFull();
            foreach (var e in events)
            {
                var t = e.R.PressAt;

                // Regeneration ticks and automatic refills up to t, earliest first.
                for (var guard = 0; guard < 100_000; guard++)
                {
                    var next = double.PositiveInfinity;
                    var kind = -2; // -1 = regen tick, i ≥ 0 = refill i
                    if (model.RegenEvery > 0 && gauge < model.Capacity)
                    {
                        var tick = (ticks + 1) * model.RegenEvery;
                        if (tick < next)
                            (next, kind) = (tick, -1);
                    }

                    for (var i = 0; i < refills.Count; i++)
                    {
                        var (rule, _) = refills[i];
                        var useful = rule.Amount >= model.Capacity ? gauge == 0 : gauge + rule.Amount <= model.Capacity;
                        if (!rule.Auto || !useful)
                            continue;
                        var at = Math.Max(Math.Max(readyAt[i], 0), now);
                        if (at < next)
                            (next, kind) = (at, i);
                    }

                    if (kind == -2 || next > t + Epsilon)
                        break;

                    now = Math.Max(now, next);
                    if (kind == -1)
                    {
                        ticks++;
                        gauge = Math.Min(model.Capacity, gauge + 1);
                    }
                    else
                    {
                        gauge = Math.Min(model.Capacity, gauge + refills[kind].Rule.Amount);
                        readyAt[kind] = now + refills[kind].Action.RecastAt(this.level);
                    }

                    MarkFull();
                }

                // Ticks that came while the gauge was full are lost (no tick is scheduled then).
                if (model.RegenEvery > 0 && t >= 0 && gauge >= model.Capacity)
                    ticks = Math.Max(ticks, (int)Math.Floor((t + Epsilon) / model.RegenEvery));
                now = Math.Max(now, t);

                if (e.Refill >= 0)
                {
                    gauge = Math.Min(model.Capacity, gauge + refills[e.Refill].Rule.Amount);
                    readyAt[e.Refill] = t + refills[e.Refill].Action.RecastAt(this.level);
                    MarkFull();
                }

                if (e.Trigger)
                {
                    freeUntil = t + (freeTrigger!.DurationAt(this.level) ?? 0);
                    continue;
                }

                var free = false;
                if (e.Consumer && freeUntil is { } until && t <= until + Epsilon)
                {
                    freeUntil = null;
                    free = true;
                }

                if (e.Spend <= 0 || free)
                    continue;

                windowStart ??= t;
                windowSpends.Add(e.R);
                if (gauge >= e.Spend)
                {
                    gauge -= e.Spend;
                    continue;
                }

                var name = e.Action?.Name ?? e.R.Item.ToString();
                var count = windowSpends.Count;
                var left = gauge == 0 ? "none is left" : $"only {gauge} is left";
                var since = count > 1 ? $": {count} {model.Resource} spenders since {this.At(windowStart.Value, e.R.Mechanic.Phase)}" : string.Empty;
                this.Add(PlanFindingSeverity.Warning, "resource-shortage",
                    $"{name} at {this.At(e.R)} needs {e.Spend} {model.Resource}, but {left}{since} ({model.Resource}: {model.Basis}).",
                    e.R.Entry, t, actionId: e.Action?.Id, heuristic: true,
                    related: DistinctEntries(windowSpends.Take(windowSpends.Count - 1).Select(x => x.Entry)));
            }
        }

        /// <summary>
        /// The amount of <paramref name="resource"/> an item spends: its action's cost, or for an either/or group the
        /// smallest cost when every option the player can use spends it (0 when they can pick a free option).
        /// </summary>
        private int SpendOf(PlanItem item, string resource, out ActionInfo? spender)
        {
            spender = null;
            var amount = int.MaxValue;
            foreach (var id in item.ActionIds())
            {
                if (this.Usable(id) is not { } action)
                    continue; // another job's option (or unknown): not the player's to press
                if (action.Cost is not { } cost || !string.Equals(cost.Resource, resource, StringComparison.OrdinalIgnoreCase) || cost.Amount <= 0)
                    return 0;
                if (cost.Amount < amount)
                {
                    amount = cost.Amount;
                    spender = action;
                }
            }

            return spender == null ? 0 : amount;
        }

        private void CheckAction(PlanEntry entry, uint id, string? name, double? time)
        {
            var action = this.actions.Get(id);
            var at = time is { } t ? " at " + this.At(t, this.PhaseOf(entry)) : string.Empty;
            if (action == null)
            {
                var named = string.IsNullOrWhiteSpace(name) ? string.Empty : $" ({name})";
                this.Add(PlanFindingSeverity.Error, "unknown-action", $"Action {id}{named}{at} is not in the action catalog.", entry, time, actionId: id);
                return;
            }

            if (!string.IsNullOrWhiteSpace(name) && AliasDictionary.Normalize(name) != AliasDictionary.Normalize(action.Name))
            {
                this.Add(PlanFindingSeverity.Warning, "name-mismatch",
                    $"The item{at} is named \"{name}\" but action {id} is {action.Name}: the id decides what is reminded.", entry, time, actionId: id);
            }

            if (this.job != Job.None && !action.UsableBy(this.job))
            {
                this.Add(PlanFindingSeverity.Error, "wrong-job", $"{action.Name}{at} is a {JobList(action)} action, not {this.job}.", entry, time, actionId: id);
                return;
            }

            this.CheckLevel(entry, action, at, time);
        }

        private void CheckGroup(PlanEntry entry, PlanItem item, double? time)
        {
            var at = time is { } t ? " at " + this.At(t, this.PhaseOf(entry)) : string.Empty;
            var ids = item.ActionIds().Distinct().ToList();
            if (ids.Count == 0)
            {
                // A choice between notes is odd but readable; a choice of nothing is not.
                if (!(item.Any?.Any(o => o?.Note != null) ?? false))
                    this.Add(PlanFindingSeverity.Warning, "empty-item", $"An either/or choice{at} has no action to press.", entry, time);
                return;
            }

            var known = new List<ActionInfo>();
            foreach (var id in ids)
            {
                if (this.actions.Get(id) is { } action)
                    known.Add(action);
                else
                    this.Add(PlanFindingSeverity.Error, "unknown-action", $"Action {id} in an either/or choice{at} is not in the action catalog.", entry, time, actionId: id);
            }

            if (known.Count == 0)
                return;
            var usable = known.Where(a => this.job == Job.None || a.UsableBy(this.job)).ToList();
            var names = string.Join(" / ", known.Select(a => a.Name));
            if (usable.Count == 0)
            {
                this.Add(PlanFindingSeverity.Error, "wrong-job", $"No option of {names}{at} is a {this.job} action.", entry, time, actionId: known[0].Id);
                return;
            }

            if (!usable.Any(a => this.actions.ResolveAt(a, this.level).AvailableAt(this.level)))
            {
                this.Add(PlanFindingSeverity.Error, "not-available-at-level",
                    $"No option of {names}{at} can be used at level {this.level}, the synced level of {this.FightName}.", entry, time, actionId: usable[0].Id);
                return;
            }

            foreach (var action in usable)
                this.CheckVariant(entry, action, at, time);
        }

        private void CheckLevel(PlanEntry entry, ActionInfo action, string at, double? time)
        {
            var resolved = this.actions.ResolveAt(action, this.level);
            if (!resolved.AvailableAt(this.level))
            {
                var why = resolved.Level > this.level
                    ? $"is learned at level {resolved.Level}"
                    : $"is replaced at level {resolved.ReplacedAtLevel}";
                var shown = resolved.Id == action.Id ? action.Name : $"{action.Name} (here {resolved.Name})";
                this.Add(PlanFindingSeverity.Error, "not-available-at-level",
                    $"{shown}{at} {why}; {this.FightName} is synced to level {this.level}.", entry, time, actionId: action.Id);
                return;
            }

            this.CheckVariant(entry, action, at, time);
        }

        /// <summary>The plan names an action whose button is another action at the level (an upgrade, or the base action of one not learned yet).</summary>
        private void CheckVariant(PlanEntry entry, ActionInfo action, string at, double? time)
        {
            var resolved = this.actions.ResolveAt(action, this.level);
            if (resolved.Id == action.Id || !resolved.AvailableAt(this.level))
                return;
            var how = action.Level <= this.level
                ? $"is replaced by {resolved.Name} ({resolved.Id}) at level {this.level}"
                : $"is not learned at level {this.level}: the button is {resolved.Name} ({resolved.Id})";
            this.Add(PlanFindingSeverity.Warning, "level-variant",
                $"{action.Name}{at} {how}; the game reports {resolved.Name}, so plan that action or the use is not tracked.", entry, time, actionId: action.Id);
        }

        /// <summary>The catalog action for <paramref name="id"/> as pressed at the level, when the job can use it there; else null.</summary>
        private ActionInfo? Usable(uint id)
        {
            if (this.actions.Get(id) is not { } action)
                return null;
            var resolved = this.actions.ResolveAt(action, this.level);
            if (!resolved.AvailableAt(this.level) || (this.job != Job.None && !resolved.UsableBy(this.job)))
                return null;
            return resolved;
        }

        /// <summary>The ids an item is satisfied by, resolved at the level (unknown ids as they are).</summary>
        private HashSet<uint> ResolvedIds(PlanItem item)
        {
            var set = new HashSet<uint>();
            foreach (var id in item.ActionIds())
            {
                set.Add(id);
                if (this.actions.Get(id) is { } action)
                    set.Add(this.actions.ResolveAt(action, this.level).Id);
            }

            return set;
        }

        private ActionInfo? FindAction(string name)
        {
            var action = this.actions.FindByName(name, this.job == Job.None ? null : this.job);
            return action == null ? null : this.actions.ResolveAt(action, this.level);
        }

        private string FightName => this.fight.Key.ToUpperInvariant();

        private string? PhaseOf(PlanEntry entry) => this.fight.GetMechanic(entry.Mechanic ?? string.Empty)?.Phase;

        private string At(Use use) => this.At(use.Reminder);

        private string At(Reminder r) => FormatTime(this.fight, r.PressAt, r.Mechanic.Phase);

        private string At(double time, string? phase) => FormatTime(this.fight, time, phase);

        private string Describe(PlanEntry entry)
        {
            var name = !string.IsNullOrWhiteSpace(entry.Id)
                ? "Entry " + entry.Id
                : "Entry #" + ((this.entryIndex.TryGetValue(entry, out var i) ? i : 0) + 1).ToString(CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(entry.Src) ? name : $"{name} ({entry.Src})";
        }

        /// <summary>" (did you mean p2.cosmo_memory?)" for a near miss, else empty.</summary>
        private string Suggest(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || this.fight.Mechanics.Count == 0)
                return string.Empty;
            var text = id.Trim().ToLowerInvariant();
            var best = this.fight.Mechanics
                .Select(m => (m.Id, Score: m.Id.EndsWith("." + text, StringComparison.OrdinalIgnoreCase) ? 0 : Distance(text, m.Id.ToLowerInvariant())))
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .First();
            return best.Score <= Math.Max(2, text.Length / 4) ? $" (did you mean {best.Id}?)" : string.Empty;
        }

        private void Add(PlanFindingSeverity severity, string code, string message, PlanEntry? entry, double? time, uint? actionId = null, bool heuristic = false, IReadOnlyList<PlanEntry>? related = null) =>
            this.findings.Add((new PlanFinding
            {
                Severity = severity,
                Code = code,
                Message = message,
                Entry = entry,
                Time = time,
                ActionId = actionId,
                Heuristic = heuristic,
                Related = related ?? [],
            }, this.findings.Count));
    }
}
