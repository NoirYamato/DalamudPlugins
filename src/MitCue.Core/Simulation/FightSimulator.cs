using System.Globalization;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Timelines;

namespace MitCue.Core.Simulation;

public sealed class SimulationOptions
{
    /// <summary>Clock value (seconds) at the pull.</summary>
    public double PullClock { get; set; } = 1000;

    /// <summary>
    /// Real seconds from the pull at which each phase's first event happens (phase id → seconds).
    /// Missing phases start at their timeline start (no push variance).
    /// </summary>
    /// <remarks>
    /// Precisely: the phase's start (<see cref="Phase.Start"/>, its sync point) happens at this real time
    /// and the phase's events keep their timeline spacing from it. A missing phase gets no push of its
    /// own: it follows the previous phase with the timeline's spacing, so with no pushes and no jumps
    /// every event happens at its timeline time. A phase entered through a jump (DMU's success jumps)
    /// starts when the jump happens unless it is listed here. Applies to the first time a phase is
    /// entered only.
    /// </remarks>
    public Dictionary<string, double> PhaseRealStart { get; set; } = [];

    /// <summary>
    /// Timeline time after which a phase's remaining events are dropped (the boss died early),
    /// phase id → timeline seconds. Missing = keep all events until the next phase.
    /// </summary>
    /// <remarks>
    /// When the rest of the phase holds a jump the pull takes out of the phase (DMU's "Kefka &lt;20% HP"
    /// success jumps), that jump's event still happens, right at the cutoff (or at the next phase's
    /// <see cref="PhaseRealStart"/> when given): it is how the timeline leaves the phase.
    /// </remarks>
    public Dictionary<string, double> PhaseCutoff { get; set; } = [];

    /// <summary>Uniform random jitter (± seconds) added to each event time.</summary>
    public double Jitter { get; set; } = 0.15;

    public int Seed { get; set; } = 1;

    /// <summary>Emit a CombatStateChanged(false) and a DutyReset(Wipe) at this real time after the pull, if set.</summary>
    public double? WipeAt { get; set; }

    /// <summary>Boss source name to use for events whose sync has no source.</summary>
    public string DefaultSource { get; set; } = "Boss";

    /// <summary>
    /// Branch choices for conditional jumps (entries whose sync has a <c>jump</c>): true = the sync's event
    /// happens and the timeline jumps, false = it does not happen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys: the jump's label (e.g. "p3-chaos-enrage"), its target time in invariant format (e.g. "550"),
    /// or "L" + the entry's line number to pick one entry (e.g. "L197"); the line key wins. Unlisted jumps
    /// take the default: a forward jump within the main timeline is taken (a phase success such as DMU's
    /// "p2-success"); a jump back (loops, retries, the <c>--Reset--</c> jump 0) or into a block that only
    /// jumps reach (a label more than <see cref="FightSimulator.DetachedGap"/> seconds after everything
    /// before it, such as DMU's enrage sequences at 10000+) is not.
    /// </para>
    /// <para>
    /// A <c>forcejump</c> is always followed (the timeline jumps when it reaches it); false only suppresses
    /// its sync's event. After a block that only jumps reach, the simulation continues with the next phase
    /// when that phase has a <see cref="PhaseRealStart"/> (e.g. DMU P4 after a P3 single-boss enrage), and
    /// ends otherwise. Each jump is followed at most <see cref="FightSimulator.MaxJumpRepeats"/> times.
    /// </para>
    /// </remarks>
    public Dictionary<string, bool> Jumps { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One generated event and what it stands for.</summary>
/// <param name="Event">The game event.</param>
/// <param name="Entry">
/// The timeline entry the event was generated for; null for the combat end and wipe events, and for the
/// combat start when the timeline has no InCombat sync.
/// </param>
/// <param name="SyncTime">
/// Timeline time the event should set: the entry's time, or its jump target. Null when the event is not
/// meant to sync.
/// </param>
public sealed record SimulatedEvent(GameEvent Event, TimelineEntry? Entry, double? SyncTime);

/// <summary>
/// Generates the game events a pull would produce for a timeline: combat start, then one
/// StartsUsing/Ability/... event per synced entry at its (phase-shifted, jittered) time.
/// Used by tests and by the plugin's preview mode.
/// </summary>
/// <remarks>
/// The pull follows one path through the timeline, in time order: an entry with a sync produces one event
/// of its sync type whose fields match the sync (a value from each array, a matching string for regex
/// values; the source falls back to <see cref="SimulationOptions.DefaultSource"/>). Entries without a
/// usable sync produce nothing, and the InCombat sync is the combat start itself. At a conditional jump the
/// path branches as <see cref="SimulationOptions.Jumps"/> decides; a taken jump continues at its target
/// (the event happens at the jump's time and the target is "now"). Phases come from the fight
/// definition (none when it is null: every event happens at its timeline time, apart from jumps).
/// The result is ordered by time; equal times keep generation order.
/// </remarks>
public static class FightSimulator
{
    /// <summary>A label this many seconds after everything before it starts a block that only jumps reach.</summary>
    public const double DetachedGap = 300;

    /// <summary>How many times one jump entry is followed at most (bounds loops).</summary>
    public const int MaxJumpRepeats = 10;

    public static IReadOnlyList<GameEvent> Generate(Timeline timeline, FightDefinition? fight, SimulationOptions options) =>
        Simulate(timeline, fight, options).Select(e => e.Event).ToList();

    /// <summary>Like <see cref="Generate"/>, with the entry each event stands for and the time it should sync to.</summary>
    public static IReadOnlyList<SimulatedEvent> Simulate(Timeline timeline, FightDefinition? fight, SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(options);
        return new Walk(timeline, fight, options).Run();
    }

    /// <summary>The path through the timeline, one entry at a time.</summary>
    private sealed class Walk
    {
        private readonly SimulationOptions options;
        private readonly TimelineEntry[] sorted;
        private readonly Phase[] phases;
        private readonly double[] farLabels;
        private readonly double mainEnd;
        private readonly Random rng;
        private readonly List<SimulatedEvent> events = [];
        private readonly bool[] entered;
        private readonly Dictionary<TimelineEntry, int> jumpCounts = [];
        private readonly HashSet<TimelineEntry> skip = [];

        private int pos;
        private int phase = -1;
        private double shift;
        private bool detached;
        private double detachedEnd = double.PositiveInfinity;
        private int resumePhase = -1;
        private bool finished;

        public Walk(Timeline timeline, FightDefinition? fight, SimulationOptions options)
        {
            this.options = options;
            this.sorted = timeline.ByTime.ToArray();
            this.phases = (fight?.Phases ?? []).OrderBy(p => p.Start).ToArray();
            this.entered = new bool[this.phases.Length];
            this.rng = new Random(options.Seed);
            this.farLabels = FarLabels(timeline, this.sorted);
            this.mainEnd = this.farLabels.Length > 0 ? this.farLabels[0] : double.PositiveInfinity;
        }

        public IReadOnlyList<SimulatedEvent> Run()
        {
            var pull = this.options.PullClock;
            var start = new CombatStateChanged(pull, true);
            var inCombat = this.sorted.FirstOrDefault(e => e.CanSync && e.Sync!.Type == "InCombat" && e.Sync.Matches(start));
            this.events.Add(new SimulatedEvent(start, inCombat, inCombat == null ? null : inCombat.JumpTime ?? inCombat.Time));

            var first = this.PhaseIndexAt(0);
            if (first >= 0)
                this.Enter(first);

            var guard = 0;
            while (!this.finished && guard++ < 1_000_000)
                this.Step();

            IEnumerable<SimulatedEvent> result = this.events;
            if (this.options.WipeAt is { } wipe)
            {
                var at = pull + wipe;
                result = result.Where(e => e.Event.Time <= at)
                    .Append(new SimulatedEvent(new CombatStateChanged(at, false), null, null))
                    .Append(new SimulatedEvent(new DutyReset(at, DutyResetKind.Wipe), null, null));
            }

            return result.OrderBy(e => e.Event.Time).ToList().AsReadOnly();
        }

        /// <summary>Labels that start blocks reached only by jumps: more than <see cref="DetachedGap"/> after everything before them.</summary>
        private static double[] FarLabels(Timeline timeline, TimelineEntry[] sorted)
        {
            var points = sorted.Select(e => e.Time).Concat(timeline.Labels.Values).Order().ToArray();
            var far = new List<double>();
            foreach (var label in timeline.Labels.Values.Distinct().Order())
            {
                var before = Array.FindLast(points, p => p < label);
                var hasBefore = points.Any(p => p < label);
                if (hasBefore && label - before > DetachedGap)
                    far.Add(label);
            }

            return far.ToArray();
        }

        private static bool Emittable(TimelineEntry e) => e.CanSync && e.Sync!.Type != "InCombat";

        private void Step()
        {
            if (this.pos >= this.sorted.Length)
            {
                if (!(this.detached && this.Resume()))
                    this.finished = true;
                return;
            }

            var e = this.sorted[this.pos];
            if (this.detached)
            {
                if (e.Time >= this.detachedEnd)
                {
                    if (!this.Resume())
                        this.finished = true;
                    return;
                }
            }
            else
            {
                if (e.Time >= this.mainEnd)
                {
                    this.finished = true;
                    return;
                }

                while (this.phase + 1 < this.phases.Length && e.Time >= this.phases[this.phase + 1].Start)
                    this.Enter(this.phase + 1);

                if (this.phase >= 0 && this.Cutoff(this.phase) is { } cut && e.Time > cut)
                {
                    this.LeaveCutPhase(cut);
                    return;
                }
            }

            this.pos++;
            if (this.skip.Remove(e))
                return;

            if (e.ForceJump && e.JumpTime != null)
            {
                // The timeline jumps when it reaches a forcejump, whether or not its event happens.
                if (!this.RepeatsExhausted(e))
                    this.TakeJump(e, e.Time + this.shift, emit: Emittable(e) && this.Choice(e) != false);
                return;
            }

            if (!Emittable(e))
                return;

            if (e.JumpTime != null)
            {
                if (!this.RepeatsExhausted(e) && this.Decide(e))
                    this.TakeJump(e, e.Time + this.shift, emit: true);
                return;
            }

            this.Emit(e, e.Time + this.shift, e.Time);
        }

        /// <summary>The boss died at <paramref name="cut"/>: leave the phase by its exit jump, or skip to the next phase.</summary>
        private void LeaveCutPhase(double cut)
        {
            var rangeEnd = this.phase + 1 < this.phases.Length ? Math.Min(this.phases[this.phase + 1].Start, this.mainEnd) : this.mainEnd;
            for (var i = this.pos; i < this.sorted.Length && this.sorted[i].Time < rangeEnd; i++)
            {
                var x = this.sorted[i];
                if (x.JumpTime is not { } target || target <= x.Time || target >= this.mainEnd || !Emittable(x) || this.RepeatsExhausted(x))
                    continue;
                if (x.ForceJump ? this.Choice(x) == false : !this.Decide(x))
                    continue;
                this.pos = i + 1;
                if (this.TakeJump(x, cut + this.shift, emit: true))
                    return;
            }

            this.pos = this.FirstIndexAtOrAfter(rangeEnd);
        }

        /// <summary>Follows a jump; returns false when its event could not be generated (then the jump does not happen).</summary>
        private bool TakeJump(TimelineEntry e, double defaultReal, bool emit)
        {
            var target = e.JumpTime!.Value;
            var real = defaultReal;
            if (target > e.Time && target < this.mainEnd)
            {
                var q = this.PhaseIndexAt(target);
                if (q > this.phase && !this.entered[q] && this.RealStart(q) is { } rq)
                {
                    real = rq + (target - this.phases[q].Start);
                }
                else if (q == this.phase)
                {
                    // Landing just before the next phase's sync point (DMU: 597 label, 600.0 phase sync).
                    var n = this.phase + 1;
                    if (n < this.phases.Length && !this.entered[n] && this.RealStart(n) is { } rn && !this.HasSyncedEntries(target, this.phases[n].Start))
                        real = rn - (this.phases[n].Start - target);
                }
            }

            GameEvent? ev = null;
            if (emit)
            {
                ev = this.Emit(e, real, target);
                if (ev == null && !e.ForceJump)
                    return false;
            }

            this.jumpCounts[e] = this.jumpCounts.GetValueOrDefault(e) + 1;
            if (target == 0)
            {
                this.finished = true; // a reset: the pull is over
                return true;
            }

            this.shift = real - target;
            if (target >= this.mainEnd)
            {
                this.detached = true;
                this.resumePhase = this.phase + 1;
                this.detachedEnd = double.PositiveInfinity;
                foreach (var label in this.farLabels)
                {
                    if (label > target)
                    {
                        this.detachedEnd = label;
                        break;
                    }
                }
            }
            else
            {
                this.detached = false;
                var q = this.PhaseIndexAt(target);
                if (q >= 0)
                {
                    this.phase = q;
                    this.entered[q] = true;
                }
            }

            this.pos = this.FirstIndexAtOrAfter(target);
            if (ev != null)
            {
                // The jump's event is the event of a same-sync entry at the target (DSR's loop to 550.0).
                for (var i = this.pos; i < this.sorted.Length && this.sorted[i].Time == target; i++)
                {
                    if (this.sorted[i].Sync?.Matches(ev) == true)
                        this.skip.Add(this.sorted[i]);
                }
            }

            return true;
        }

        /// <summary>After a block only jumps reach: continue with the next phase if it has a real start.</summary>
        private bool Resume()
        {
            var r = this.resumePhase;
            if (r < 0 || r >= this.phases.Length || this.entered[r] || this.RealStart(r) is not { } start)
                return false;
            this.detached = false;
            this.phase = r;
            this.entered[r] = true;
            this.shift = start - this.phases[r].Start;
            this.pos = this.FirstIndexAtOrAfter(this.phases[r].Start);
            return true;
        }

        private void Enter(int i)
        {
            this.phase = i;
            if (this.entered[i])
                return;
            this.entered[i] = true;
            if (this.RealStart(i) is { } r)
                this.shift = r - this.phases[i].Start;
        }

        private GameEvent? Emit(TimelineEntry e, double real, double syncTime)
        {
            if (this.options.Jitter > 0)
                real += ((this.rng.NextDouble() * 2) - 1) * this.options.Jitter;
            var clock = this.options.PullClock + Math.Max(0, real);
            var ev = SimulatedEvents.Create(e, clock, this.rng, this.options.DefaultSource, this.CastTime(e));
            if (ev != null)
                this.events.Add(new SimulatedEvent(ev, e, syncTime));
            return ev;
        }

        /// <summary>For a cast start: seconds until the next Ability entry with the same id (within 30 s), else 0.</summary>
        private float CastTime(TimelineEntry e)
        {
            if (e.Sync is not { Type: "StartsUsing" } sync || !sync.Fields.TryGetValue("id", out var ids))
                return 0;
            var i = Array.IndexOf(this.sorted, e);
            for (var j = i + 1; j < this.sorted.Length && this.sorted[j].Time <= e.Time + 30; j++)
            {
                var next = this.sorted[j];
                if (next.Sync is { Type: "Ability" } a && a.Fields.TryGetValue("id", out var abilityIds) && abilityIds.Values.Intersect(ids.Values, StringComparer.OrdinalIgnoreCase).Any())
                    return (float)Math.Round(next.Time - e.Time, 1);
            }

            return 0;
        }

        private bool Decide(TimelineEntry e)
        {
            if (this.Choice(e) is { } chosen)
                return chosen;
            var target = e.JumpTime!.Value;
            return target > e.Time && target < this.mainEnd;
        }

        /// <summary>The explicit choice for a jump entry in <see cref="SimulationOptions.Jumps"/>, if any.</summary>
        private bool? Choice(TimelineEntry e)
        {
            var jumps = this.options.Jumps;
            if (jumps.Count == 0)
                return null;
            if (jumps.TryGetValue("L" + e.Line.ToString(CultureInfo.InvariantCulture), out var byLine))
                return byLine;
            if (e.JumpLabel != null && jumps.TryGetValue(e.JumpLabel, out var byLabel))
                return byLabel;
            if (e.JumpTime is { } t && jumps.TryGetValue(t.ToString(CultureInfo.InvariantCulture), out var byTime))
                return byTime;
            return null;
        }

        private bool RepeatsExhausted(TimelineEntry e) => this.jumpCounts.GetValueOrDefault(e) >= MaxJumpRepeats;

        private bool HasSyncedEntries(double from, double to)
        {
            foreach (var e in this.sorted)
            {
                if (e.Time >= from && e.Time < to && Emittable(e))
                    return true;
            }

            return false;
        }

        /// <summary>The last phase starting at or before <paramref name="t"/> (none in blocks only jumps reach).</summary>
        private int PhaseIndexAt(double t)
        {
            if (t >= this.mainEnd)
                return -1;
            var index = -1;
            for (var i = 0; i < this.phases.Length && this.phases[i].Start <= t; i++)
                index = i;
            return index;
        }

        private int FirstIndexAtOrAfter(double t)
        {
            var i = 0;
            while (i < this.sorted.Length && this.sorted[i].Time < t)
                i++;
            return i;
        }

        private double? RealStart(int i) => Lookup(this.options.PhaseRealStart, this.phases[i].Id);

        private double? Cutoff(int i) => Lookup(this.options.PhaseCutoff, this.phases[i].Id);

        private static double? Lookup(Dictionary<string, double> map, string id)
        {
            if (map.TryGetValue(id, out var exact))
                return exact;
            foreach (var (key, value) in map)
            {
                if (string.Equals(key, id, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            return null;
        }
    }
}
