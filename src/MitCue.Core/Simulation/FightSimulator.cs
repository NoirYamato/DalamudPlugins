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
    /// Missing phases get no push variance: they start where the timeline, played on from the previous
    /// phase, reaches their start (at their timeline start when nothing before them was pushed or jumped).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Precisely: the phase's start (<see cref="Phase.Start"/>, its sync point) happens at this real time
    /// and the phase's events keep their timeline spacing from it. A missing phase gets no push of its
    /// own: it follows the previous phase with the timeline's spacing (keeping that phase's push), so with
    /// no pushes and no jumps every event happens at its timeline time. It is not put back at its own
    /// timeline time after an earlier push: by then the engine would have run past its phase sync (a
    /// <c>window N,0</c> sync closes at the phase start) and the rest of the pull could not be followed.
    /// </para>
    /// <para>
    /// A phase entered through a jump (DMU's success jumps) starts when the jump happens unless it is
    /// listed here. When it is listed, the jump's event is moved so that the phase starts on time, but only
    /// as far as the engine, running on from the previous event, can still sync it: inside the jump
    /// entry's window and before the next forcejump (DMU's 486.2 forcejump bounds the 483.2 success cast).
    /// A listed start outside that range is not kept: the jump happens at its own time and the phase
    /// starts from it. Applies to the first time a phase is entered only.
    /// </para>
    /// </remarks>
    public Dictionary<string, double> PhaseRealStart { get; set; } = [];

    /// <summary>
    /// Timeline time after which a phase's remaining events are dropped (the boss died early),
    /// phase id → timeline seconds. Missing = keep all events until the next phase.
    /// </summary>
    /// <remarks>
    /// Everything after the cutoff is dropped, including a jump out of the phase, so a phase cut at its
    /// start produces no events at all (the plugin's preview skips phases this way). Set
    /// <see cref="CutPhasesLeaveByJump"/> to keep that jump.
    /// </remarks>
    public Dictionary<string, double> PhaseCutoff { get; set; } = [];

    /// <summary>
    /// When true, a phase cut by <see cref="PhaseCutoff"/> still leaves through the jump the pull takes out
    /// of it (DMU's success jumps, cast when the boss reaches its HP threshold). Default false: the jump is
    /// dropped with the rest of the phase.
    /// </summary>
    /// <remarks>
    /// The jump's event happens at the cutoff (or once its sync window opens, if that is later), or when
    /// the next phase's <see cref="PhaseRealStart"/> asks for it, within the range described there.
    /// </remarks>
    public bool CutPhasesLeaveByJump { get; set; }

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

    /// <summary>
    /// Seconds kept between a moved jump event and the edges of the range in which the engine can sync it,
    /// on top of twice the jitter (the event's own and the engine's offset from the previous event).
    /// </summary>
    private const double SyncMargin = 0.1;

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
        private readonly TimelineEntry[] forceJumps;
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
            this.forceJumps = this.sorted.Where(e => e.ForceJump && e.JumpTime != null).ToArray();
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
                    this.TakeJump(e, e.Time + this.shift, e.Time, emit: Emittable(e) && this.Choice(e) != false);
                return;
            }

            if (!Emittable(e))
                return;

            if (e.JumpTime != null)
            {
                if (!this.RepeatsExhausted(e) && this.Decide(e))
                    this.TakeJump(e, e.Time + this.shift, e.Time, emit: true);
                return;
            }

            if (this.Emit(e, e.Time + this.shift, e.Time) is { } ev)
            {
                // A later entry at the same time whose sync this event also matches stands for the same game line (a fight
                // file's timeline_additions line that widens a cactbot line's window): one event, as in game.
                for (var i = this.pos; i < this.sorted.Length && this.sorted[i].Time == e.Time; i++)
                {
                    var same = this.sorted[i];
                    if (same.JumpTime == null && same.Sync?.Matches(ev) == true)
                        this.skip.Add(same);
                }
            }
        }

        /// <summary>
        /// The boss died at <paramref name="cut"/>: skip to the next phase, or with
        /// <see cref="SimulationOptions.CutPhasesLeaveByJump"/> leave the phase by its exit jump.
        /// </summary>
        private void LeaveCutPhase(double cut)
        {
            var rangeEnd = this.phase + 1 < this.phases.Length ? Math.Min(this.phases[this.phase + 1].Start, this.mainEnd) : this.mainEnd;
            for (var i = this.pos; this.options.CutPhasesLeaveByJump && i < this.sorted.Length && this.sorted[i].Time < rangeEnd; i++)
            {
                var x = this.sorted[i];
                if (x.JumpTime is not { } target || target <= x.Time || target >= this.mainEnd || !Emittable(x) || this.RepeatsExhausted(x))
                    continue;
                if (x.ForceJump ? this.Choice(x) == false : !this.Decide(x))
                    continue;

                // At the cutoff, or once the jump's window opens (DMU's C3FD: window 90,5); never where it cannot sync.
                var (lo, hi) = this.SyncableRange(x, cut);
                var at = Math.Max(cut + this.shift, lo);
                if (at > hi)
                    continue;
                this.pos = i + 1;
                if (this.TakeJump(x, at, cut, emit: true))
                    return;
            }

            this.pos = this.FirstIndexAtOrAfter(rangeEnd);
        }

        /// <summary>Follows a jump; returns false when its event could not be generated (then the jump does not happen).</summary>
        /// <param name="e">The jump entry.</param>
        /// <param name="defaultReal">Real time of the jump's event unless a listed phase start moves it.</param>
        /// <param name="from">Timeline time the walk is at (the entry's time, or the cutoff of a cut phase).</param>
        /// <param name="emit">Whether the jump's event happens (a refused forcejump jumps without it).</param>
        private bool TakeJump(TimelineEntry e, double defaultReal, double from, bool emit)
        {
            var target = e.JumpTime!.Value;
            var real = defaultReal;
            var startsFromJump = -1; // a listed phase whose start cannot be kept
            if (target > e.Time && target < this.mainEnd)
            {
                double? moved = null;
                var listed = -1;
                var q = this.PhaseIndexAt(target);
                if (q > this.phase && !this.entered[q] && this.RealStart(q) is { } rq)
                {
                    moved = rq + (target - this.phases[q].Start);
                    listed = q;
                }
                else if (q == this.phase)
                {
                    // Landing just before the next phase's sync point (DMU: 597 label, 600.0 phase sync).
                    var n = this.phase + 1;
                    if (n < this.phases.Length && !this.entered[n] && this.RealStart(n) is { } rn && !this.HasSyncedEntries(target, this.phases[n].Start))
                    {
                        moved = rn - (this.phases[n].Start - target);
                        listed = n;
                    }
                }

                if (moved is { } m)
                {
                    // Only as far as the engine, running on from the previous event, can still sync the jump.
                    // Otherwise the listed start cannot happen: the phase starts from the jump at its own time.
                    var (lo, hi) = this.SyncableRange(e, from);
                    if (m >= lo && m <= hi)
                        real = m;
                    else
                        startsFromJump = listed;
                }
            }

            GameEvent? ev = null;
            if (emit)
            {
                ev = this.Emit(e, real, target);
                if (ev == null && !e.ForceJump)
                    return false;
            }

            if (startsFromJump >= 0)
                this.entered[startsFromJump] = true;
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

        /// <summary>
        /// Real times at which the jump entry's sync still fires when the engine runs on with the current shift
        /// from timeline time <paramref name="from"/>: inside the entry's window and before the next forcejump
        /// (which would take the engine away first), and for a forcejump entry before its own time (a later event
        /// no longer acts as the jump). Kept clear of the edges by twice the jitter plus <see cref="SyncMargin"/>.
        /// </summary>
        private (double Lo, double Hi) SyncableRange(TimelineEntry e, double from)
        {
            var end = e.SyncEnd;
            if (e.ForceJump)
                end = Math.Min(end, e.Time);
            foreach (var f in this.forceJumps)
            {
                if (!ReferenceEquals(f, e) && f.Time > from)
                {
                    end = Math.Min(end, f.Time);
                    break;
                }
            }

            var margin = (2 * Math.Max(0, this.options.Jitter)) + SyncMargin;
            return (e.SyncStart + this.shift + margin, end + this.shift - margin);
        }

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
