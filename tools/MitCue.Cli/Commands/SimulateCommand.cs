using MitCue.Cli.Simulation;
using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;

namespace MitCue.Cli.Commands;

/// <summary><c>simulate &lt;plan.yaml&gt; --job &lt;job&gt;</c>: a generated pull played through the plugin's session logic.</summary>
internal static class SimulateCommand
{
    /// <summary>Simulated clock of the pull (as in the plugin's preview).</summary>
    public const double PullClock = 1000;

    /// <summary>Frame length: events are played and reminders updated every 0.1 s.</summary>
    public const double Tick = 0.1;

    /// <summary>Real seconds after the pull at which the --from phase starts (as in the plugin's preview).</summary>
    private const double PhaseLeadIn = 2;

    /// <summary>A pushed phase starts this long after the previous phase's last event (the transition).</summary>
    private const double PushGap = 3;

    private const double Epsilon = 1e-6;

    /// <summary>Event types the plugin produces in game (GameEventSource): everything else exists only in simulations.</summary>
    private static readonly HashSet<Type> GameEventTypes = [typeof(CombatStateChanged), typeof(CastStarted), typeof(AbilityResolved), typeof(DutyReset)];

    public static CommandSpec Spec { get; } = new()
    {
        Name = "simulate",
        Synopsis = "<plan.yaml> --job <job> [--fight <key>] [--slot <slot>] [--assignee <key>] [--push p2=110,p3=...] [--seed <n>] [--miss a,b] [--from <phase>]",
        Summary = "Simulate a pull: generated boss events through the sync engine and the reminder scheduler, logging every reminder state change.",
        Details = """
            Generates a pull with the fight simulator and plays it in 0.1 s frames through a timeline player and a reminder
            scheduler set up like the plugin's encounter session: the timeline does not stop when combat ends, timeline
            jumps (phase pushes) are forwarded to the scheduler, and a countdown runs pre-pull reminders. The player presses
            every action at its press time, except the --miss actions, with the button the job has at the sync level: the
            session gets the action the game reports (an upgrade for a replaced action, the base action for one not learned
            yet), so a plan naming the other one is not marked done, as in game ("press" lines in the log say so). By default
            only the events the plugin sees in game are played (combat start/end, cast starts, resolved boss abilities,
            wipes); --all-events plays every generated event, like /mitcue preview.

            The log lists each state change (Upcoming, Due, Done, Missed, Skipped; Passed for notes) with the real time
            from the pull and the timeline time, plus timeline starts, jumps and the pull end; then the pull summary and
            how many boss events the timeline was in sync with, per phase where it was not.

            --push p2=110 makes phase p2 start 110 s (real time) after the pull; the previous phase's events stop 3 s
            before. Times may be written as seconds or m:ss. Phase times are those of the pull as it plays: a phase entered
            through a timeline jump starts at the jump (DMU's P3 and later phases come 113.8 s before the timeline's spacing
            puts them) and is pushed by moving the jump, which works only within the jump's sync window; a push that cannot
            be simulated is an error. --from p3 starts at that phase like /mitcue preview (earlier phases are cut and their
            reminders skipped; the phase starts 2 s after the pull).
            """,
        Options =
        [
            .. PlayerSelection.Options,
            new("push", "p2=110,...", "Real start of phases after the pull (seconds or m:ss), e.g. p2=130,p3=5:10."),
            new("seed", "n", "Random seed of the jitter (default 1)."),
            new("jitter", "s", "Random timing error of each boss event, ± seconds (default 0.15)."),
            new("miss", "a,b", "Actions (names or ids) the player does not press."),
            new("from", "phase", "Start at this phase, like /mitcue preview <fight> <phase>."),
            new("wipe", "s", "Wipe at this real time after the pull (seconds or m:ss)."),
            new("countdown", "s", "Countdown before the pull, in seconds (default 10; 0 = none; not with --from)."),
            new("all-events", null, "Also play event types the plugin does not see in game (MapEffect, ActorControl, ...)."),
            new("syncs", null, "Also log every timeline sync."),
        ],
        MinArgs = 1,
        MaxArgs = 1,
        Examples =
        [
            "simulate ctc-whm.yaml --job WHM --slot H1",
            "simulate ctc-whm.yaml --job WHM --push p2=2:10,p3=5:20 --miss Temperance --seed 7",
            "simulate lpdu.yaml --job SCH --assignee \"H2 SCH\" --from p5",
        ],
        Run = Run,
    };

    private static int Run(CliContext ctx, ParsedArgs args)
    {
        var plan = ctx.ReadPlan(args.Positionals[0]);
        var player = PlayerSelection.Select(ctx, args, plan);
        var fight = player.Fight;
        var timeline = ctx.Fights.LoadTimeline(fight);
        var actions = ctx.Actions;
        var miss = MissSet.Parse(args, actions, player.Me.Job);

        var from = args.Get("from") is { } fromId
            ? fight.GetPhase(fromId) ?? throw new UsageException($"simulate: {fight.Key} has no phase '{fromId}'. Phases: {string.Join(", ", fight.Phases.Select(p => p.Id))}.")
            : null;
        var countdown = args.GetSeconds("countdown") ?? (from == null ? 10 : 0);
        if (countdown < 0)
            throw new UsageException("simulate: --countdown cannot be negative.");
        if (from != null && countdown > 0)
            throw new UsageException("simulate: --countdown applies to a whole pull, not with --from.");

        var sim = new SimulationOptions
        {
            PullClock = PullClock,
            Seed = args.GetInt("seed") ?? 1,
            Jitter = args.GetSeconds("jitter") ?? 0.15,
            WipeAt = args.GetSeconds("wipe"),
        };
        if (sim.Jitter < 0)
            throw new UsageException("simulate: --jitter cannot be negative.");
        var pushNotes = ConfigurePhases(sim, timeline, fight, ParsePushes(args, fight), from);

        var generated = FightSimulator.Simulate(timeline, fight, sim);
        var allEvents = args.Has("all-events");
        var events = generated.Where(e => allEvents || GameEventTypes.Contains(e.Event.GetType())).OrderBy(e => e.Event.Time).ToList();

        var run = new PullRun(fight, timeline, player.Entries, actions, player.Me.Job, miss, args.Has("syncs"), (2 * sim.Jitter) + 0.5);
        run.Play(events, countdown, from);

        WriteHeader(ctx, player, run, sim, countdown, from, pushNotes, miss, events.Count, generated.Count, allEvents);
        run.WriteLog(ctx.Out);
        run.WriteSummary(ctx.Out);
        return ExitCodes.Ok;
    }

    private static void WriteHeader(CliContext ctx, Player player, PullRun run, SimulationOptions sim, double countdown, Phase? from, List<string> pushNotes, MissSet miss, int played, int generated, bool allEvents)
    {
        var all = run.Session.Scheduler.All;
        var fight = player.Fight;
        ctx.Out.WriteLine(
            $"Simulating \"{player.PlanName}\" ({fight.Key}, level {fight.SyncLevel}) as {player.Description}: " +
            $"{Text.Count(player.Entries.Count, "entry", "entries")}, {Text.Count(all.Count, "reminder")} ({Text.Count(all.Count(r => r.IsAction), "action")}, {Text.Count(all.Count(r => !r.IsAction), "note")})");
        var start = from != null ? $"from {from.Id} ({from.Name}) at {Text.Clock(PhaseLeadIn)}" : countdown > 0 ? $"countdown {Text.Num(countdown)} s" : "no countdown";
        var events = allEvents ? $"all {generated} generated events" : $"{played} of {generated} generated events (the types the plugin sees in game)";
        ctx.Out.WriteLine($"Pull: seed {sim.Seed}, jitter ±{Text.Num(sim.Jitter)} s, {start}{(sim.WipeAt is { } w ? $", wipe at {Text.Clock(w, 1)}" : string.Empty)}; {events}");
        foreach (var note in pushNotes)
            ctx.Out.WriteLine("Push: " + note);
        if (!miss.IsEmpty)
            ctx.Out.WriteLine("Not pressed: " + miss.Description);
        foreach (var problem in run.Session.Scheduler.Problems)
            ctx.Out.WriteLine("Not scheduled: " + problem);
        ctx.Out.WriteLine();
    }

    /// <summary>Parses --push "p2=110,p3=5:10" into phase id → real seconds after the pull.</summary>
    private static Dictionary<string, double> ParsePushes(ParsedArgs args, FightDefinition fight)
    {
        var pushes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in args.GetList("push") ?? [])
        {
            var eq = item.IndexOf('=');
            if (eq <= 0)
                throw new UsageException($"simulate: --push needs phase=time pairs like p2=130, not '{item}'.");
            var id = item[..eq].Trim();
            var phase = fight.GetPhase(id) ?? throw new UsageException($"simulate: --push: {fight.Key} has no phase '{id}'. Phases: {string.Join(", ", fight.Phases.Select(p => p.Id))}.");
            if (!Text.TryParseSeconds(item[(eq + 1)..], out var real) || real <= 0)
                throw new UsageException($"simulate: --push {id} needs a time after the pull (seconds or m:ss), not '{item[(eq + 1)..]}'.");
            if (!pushes.TryAdd(phase.Id, real))
                throw new UsageException($"simulate: --push names {phase.Id} twice.");
        }

        return pushes;
    }

    /// <summary>
    /// Sets the simulator's phase starts and cutoffs: --from cuts the earlier phases (like the plugin's preview); a push
    /// starts its phase at the given real time and cuts the previous phase <see cref="PushGap"/> seconds before.
    /// </summary>
    /// <remarks>
    /// Phase times are read from the pull as it plays with the pushes so far, not from the timeline's spacing: a phase
    /// entered through a timeline jump starts at the jump (DMU's P2 success jump brings P3 and every later phase 113.8 s
    /// sooner). A phase cut by a push leaves through its exit jump, if it has one, moved to the pushed start: the
    /// simulator moves a jump only within the range in which the engine can still sync it, so a push it cannot apply is a
    /// usage error rather than a header that announces a push that does not happen.
    /// </remarks>
    private static List<string> ConfigurePhases(SimulationOptions o, Timeline timeline, FightDefinition fight, Dictionary<string, double> pushes, Phase? from)
    {
        var notes = new List<string>();
        var phases = fight.Phases.OrderBy(p => p.Start).ToList();
        var first = 0;
        if (from != null)
        {
            first = phases.FindIndex(p => ReferenceEquals(p, from));
            for (var i = 0; i < first; i++)
                o.PhaseCutoff[phases[i].Id] = i == 0 ? phases[i].Start + 0.5 : phases[i].Start - 0.001;
            o.PhaseRealStart[from.Id] = PhaseLeadIn;
        }

        foreach (var id in pushes.Keys)
        {
            var index = phases.FindIndex(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index <= first)
            {
                throw new UsageException(index == 0
                    ? $"simulate: --push {id}: the first phase starts at the pull."
                    : $"simulate: --push {id}: the simulation starts at {from!.Id}, after {id}.");
            }
        }

        if (pushes.Count == 0)
            return notes;

        // A phase a push cuts short ends as in game when its boss reaches the HP threshold: through the jump the pull takes
        // out of it (DMU's success casts), moved to the pushed start. The phases --from skips are not left that way: their
        // jumps are refused, so that they still produce no events (as in the plugin's preview).
        o.CutPhasesLeaveByJump = true;
        if (from != null)
        {
            foreach (var e in timeline.Entries.Where(e => e.JumpTime != null && e.Time < from.Start))
                o.Jumps["L" + e.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)] = false;
        }

        for (var i = first + 1; i < phases.Count; i++)
        {
            var phase = phases[i];
            if (!pushes.TryGetValue(phase.Id, out var real))
                continue;

            var pull = PhaseTimes.Simulate(timeline, fight, o, jitter: 0);
            var previous = phases[i - 1];
            if (pull.Start(previous) is not { } previousStart)
                throw new UsageException($"simulate: --push {phase.Id}: {previous.Id} is not reached in this pull.");
            if (real <= previousStart)
                throw new UsageException($"simulate: --push {phase.Id}={Text.Clock(real, 1)} is not after {previous.Id} starts ({Text.Clock(previousStart, 1)}).");

            // The previous phase's events stop PushGap seconds before, at its own offset from the timeline. Nothing to cut
            // when they end before that anyway (they end where the phase would start).
            var natural = pull.Start(phase);
            var end = real - PushGap;
            if (natural is not { } n || end < n - Epsilon)
            {
                o.PhaseCutoff[previous.Id] = Math.Max(end - pull.OffsetAt(previous, end), previous.Start + 0.001);
                notes.Add($"{phase.Id} starts at {Text.Clock(real, 1)} (timeline {Text.Sec(phase.Start)}); {previous.Id} ends at timeline {Text.Sec(o.PhaseCutoff[previous.Id])}");
            }
            else
            {
                notes.Add($"{phase.Id} starts at {Text.Clock(real, 1)}, later than the timeline brings it ({Text.Clock(n, 1)}): the sync engine may not follow");
            }

            o.PhaseRealStart[phase.Id] = real;

            // The phase must start there in the pull itself (with its jitter): the simulator moves a jump into a phase only
            // within the jump's sync window.
            var check = PhaseTimes.Simulate(timeline, fight, o, jitter: null);
            if (check.Start(phase) is not { } start || Math.Abs(start - real) > o.Jitter + 0.001)
                throw new UsageException(CannotPush(phase, real, previousStart, pull, check.Start(phase), timeline));
        }

        return notes;
    }

    /// <summary>Why a push cannot be simulated: the phase's entry jump and its sync window in this pull, when it has one.</summary>
    private static string CannotPush(Phase phase, double real, double previousStart, PhaseTimes pull, double? starts, Timeline timeline)
    {
        var what = $"simulate: --push {phase.Id}={Text.Clock(real, 1)} cannot be simulated";
        if (pull.First(phase) is { } first && first.Sim.Entry is { JumpTime: { } target } jump && jump.Time < target)
        {
            // Real times of the phase start when the jump's event happens at the edges of its window, which ends early at
            // a forcejump after it (DMU's 486.2 forcejump for the 483.2 success cast).
            var end = jump.ForceJump ? Math.Min(jump.SyncEnd, jump.Time) : jump.SyncEnd;
            if (timeline.ByTime.FirstOrDefault(e => e.ForceJump && e.JumpTime != null && e.Time > jump.Time && !ReferenceEquals(e, jump)) is { } force)
                end = Math.Min(end, force.Time);
            var offset = first.Real - jump.Time + (phase.Start - target);
            var earliest = Math.Max(jump.SyncStart + offset, previousStart);
            return $"{what}: {phase.Id} is entered through the timeline jump at {Text.Sec(jump.Time)} (line {jump.Line}), which starts it at " +
                   $"{Text.Clock(first.Real + phase.Start - target, 1)} in this pull and can be moved only within its sync window: " +
                   $"{phase.Id} can start from about {Text.Clock(earliest, 1)} to {Text.Clock(end + offset, 1)}.";
        }

        return starts is { } s
            ? $"{what}: {phase.Id} starts at {Text.Clock(s, 1)} in this pull."
            : $"{what}: {phase.Id} is not reached in this pull.";
    }

    /// <summary>
    /// When each phase starts in a generated pull and the offset of its real times from the timeline (real − timeline),
    /// read from the events: each event stands for a timeline time (<see cref="SimulatedEvent.SyncTime"/>, the target of a
    /// jump), so a phase entered through a jump starts at the jump, not where the timeline's spacing puts it.
    /// </summary>
    private sealed class PhaseTimes
    {
        private readonly Dictionary<string, List<(double Real, double Offset, SimulatedEvent Sim)>> byPhase = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Generates the pull <paramref name="o"/> sets up, without its wipe and with <paramref name="jitter"/> if given.</summary>
        /// <remarks>Without jitter the offsets are exact (the simulator adds the jitter after placing each event).</remarks>
        public static PhaseTimes Simulate(Timeline timeline, FightDefinition fight, SimulationOptions o, double? jitter)
        {
            var copy = new SimulationOptions
            {
                PullClock = o.PullClock,
                Seed = o.Seed,
                Jitter = jitter ?? o.Jitter,
                DefaultSource = o.DefaultSource,
                CutPhasesLeaveByJump = o.CutPhasesLeaveByJump,
                PhaseRealStart = new(o.PhaseRealStart),
                PhaseCutoff = new(o.PhaseCutoff),
                Jumps = new(o.Jumps, StringComparer.Ordinal),
            };

            var times = new PhaseTimes();
            foreach (var sim in FightSimulator.Simulate(timeline, fight, copy))
            {
                if (sim.SyncTime is not { } t || fight.PhaseAt(t) is not { } phase)
                    continue;
                var real = sim.Event.Time - o.PullClock;
                if (!times.byPhase.TryGetValue(phase.Id, out var list))
                    times.byPhase[phase.Id] = list = [];
                list.Add((real, Math.Round(real - t, 6), sim));
            }

            return times;
        }

        /// <summary>The phase's first event in the pull: its real time and the event (null when the phase is not reached).</summary>
        public (double Real, SimulatedEvent Sim)? First(Phase phase) =>
            this.byPhase.TryGetValue(phase.Id, out var list) ? (list[0].Real, list[0].Sim) : null;

        /// <summary>Real seconds after the pull at which the phase starts (null when the pull does not reach it).</summary>
        public double? Start(Phase phase) =>
            this.byPhase.TryGetValue(phase.Id, out var list) ? Math.Round(phase.Start + list[0].Offset, 6) : null;

        /// <summary>The phase's real − timeline offset at real time <paramref name="real"/>: that of its last event by then (else its first).</summary>
        public double OffsetAt(Phase phase, double real)
        {
            var list = this.byPhase[phase.Id];
            var at = list.FindLastIndex(x => x.Real <= real + Epsilon);
            return list[Math.Max(0, at)].Offset;
        }
    }

    /// <summary>The actions the simulated player does not press (--miss), by id and by normalized name.</summary>
    private sealed class MissSet
    {
        private readonly HashSet<uint> ids = [];
        private readonly HashSet<string> names = new(StringComparer.Ordinal);
        private readonly List<string> shown = [];

        public bool IsEmpty => this.shown.Count == 0;

        public string Description => string.Join(", ", this.shown);

        public static MissSet Parse(ParsedArgs args, IActionCatalog actions, Job job)
        {
            var set = new MissSet();
            foreach (var item in args.GetList("miss") ?? [])
            {
                ActionInfo? action = uint.TryParse(item, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id)
                    ? actions.Get(id)
                    : actions.FindByName(item, job);
                if (action == null && !uint.TryParse(item, out _))
                    throw new UsageException($"simulate: --miss: unknown action '{item}'.");
                if (action != null)
                {
                    set.ids.Add(action.Id);
                    set.names.Add(AliasDictionary.Normalize(action.Name));
                    set.shown.Add($"{action.Name} ({action.Id})");
                }
                else
                {
                    set.ids.Add(id);
                    set.shown.Add($"#{id}");
                }
            }

            return set;
        }

        public bool Contains(PlanItem option) =>
            (option.Action is { } id && this.ids.Contains(id))
            || (option.Name != null && this.names.Contains(AliasDictionary.Normalize(option.Name)));

        public bool Contains(ActionInfo action) => this.ids.Contains(action.Id) || this.names.Contains(AliasDictionary.Normalize(action.Name));
    }

    /// <summary>One simulated pull: the frame loop, the player's presses and the log.</summary>
    private sealed class PullRun
    {
        private readonly FightDefinition fight;
        private readonly IActionCatalog actions;
        private readonly Job job;
        private readonly MissSet miss;
        private readonly bool logSyncs;
        private readonly ReminderState[] states;
        private readonly HashSet<Reminder> pressed = new(ReferenceEqualityComparer.Instance);
        private readonly List<(double Clock, string[] Cells)> log = [];
        private readonly Dictionary<string, (int Expected, int InSync)> syncsByPhase = new(StringComparer.Ordinal);
        private readonly double syncTolerance;
        private PullSummary? summary;
        private string? endReason;
        private double endClock;

        public PullRun(FightDefinition fight, Timeline timeline, IReadOnlyList<PlanEntry> entries, IActionCatalog actions, Job job, MissSet miss, bool logSyncs, double syncTolerance)
        {
            this.syncTolerance = syncTolerance;
            this.fight = fight;
            this.actions = actions;
            this.job = job;
            this.miss = miss;
            this.logSyncs = logSyncs;
            this.Session = new PullSession(fight, timeline, entries, actions, new ReminderOptions());
            this.states = this.Session.Scheduler.All.Select(r => r.State).ToArray();
            this.Session.Synced += this.OnSynced;
            this.Session.Jumped += (clock, from, to) => this.Event(clock, to, "jump", $"{Text.Sec(from)} -> {Text.Sec(to)}", this.PhaseNote(to));
            this.Session.PullEnded += (s, reason, clock) =>
            {
                this.summary = s;
                this.endReason = reason;
                this.endClock = clock;
                this.Event(clock, this.Session.Player.TimeAt(clock), "end", "pull ended: " + reason, string.Empty);
            };
        }

        public PullSession Session { get; }

        public void Play(IReadOnlyList<SimulatedEvent> events, double countdown, Phase? from)
        {
            var simStart = PullClock - (countdown > 0 ? countdown : PhaseLeadIn);
            var lastEventTime = events.Count > 0 ? events[^1].Event.Time : PullClock;
            var next = 0;
            var inCombat = false;
            if (countdown > 0)
                this.Event(simStart, -countdown, "countdown", $"countdown {Text.Num(countdown)} s", string.Empty);

            // As the plugin's preview: starting at a phase skips the earlier phases' reminders (never shown, not missed).
            if (from != null)
            {
                this.Session.SkipBefore(from.Start);
                this.Diff(simStart);
            }

            for (long frame = 0; this.endReason == null; frame++)
            {
                var now = simStart + (frame * Tick);
                while (next < events.Count && events[next].Event.Time <= now && this.endReason == null)
                {
                    var sim = events[next++];
                    var e = sim.Event;
                    if (e is CombatStateChanged combat)
                        inCombat = combat.InCombat;
                    this.Session.Process(e);
                    if (this.endReason == null)
                        this.CountSync(sim);
                    this.Diff(e.Time);
                }

                if (this.endReason != null)
                    break;

                this.Press(now);
                float? remaining = countdown > 0 && now < PullClock ? (float)(PullClock - now) : null;
                this.Session.Update(now, remaining, inCombat);
                this.Diff(now);
                if (this.endReason != null)
                    break;

                var finished = next >= events.Count
                               && now > lastEventTime + 5
                               && (!this.Session.Player.IsRunning || now > lastEventTime + 60);
                if (finished || now > lastEventTime + 3600)
                {
                    this.Session.EndPull("simulation finished", now);
                    this.endReason ??= "simulation finished";
                    this.endClock = now;
                }
            }

            this.summary ??= this.Session.Scheduler.Summarize();
        }

        public void WriteLog(TextWriter output)
        {
            // In clock order; within a frame events, presses and updates are handled in turn, so a press can be logged
            // after an event that came a little later in the same frame.
            var table = new Table(">real", ">timeline", "event", "item", "mechanic", "details");
            foreach (var (_, cells) in this.log.OrderBy(x => x.Clock))
                table.Add(cells);
            table.Write(output);
        }

        public void WriteSummary(TextWriter output)
        {
            var s = this.summary!;
            var all = this.Session.Scheduler.All;
            output.WriteLine();
            output.WriteLine($"Pull ended at {Text.Clock(this.endClock - PullClock, 1)} (timeline {Text.Sec(this.Session.Player.TimeAt(this.endClock))}): {this.endReason}.");
            var open = all.Count(r => r.IsAction && !r.Entry.Optional && r.State is ReminderState.Pending or ReminderState.Upcoming or ReminderState.Due);
            output.WriteLine(
                $"Summary: {s.Done} done, {s.Missed} missed (of {s.Total}), {s.Skipped} skipped" +
                (open > 0 ? $", {open} not reached" : string.Empty) +
                $"; counted: actions of non-optional entries ({all.Count(r => r.IsAction && !r.Entry.Optional)}).");

            var expected = this.syncsByPhase.Values.Sum(v => v.Expected);
            var inSync = this.syncsByPhase.Values.Sum(v => v.InSync);
            var lost = this.syncsByPhase.Where(kv => kv.Value.InSync < kv.Value.Expected).Select(kv => $"{kv.Key} {kv.Value.Expected - kv.Value.InSync} of {kv.Value.Expected}").ToList();
            output.WriteLine(
                $"Sync: the timeline was within {Text.Num(this.syncTolerance)} s of {inSync} of {expected} boss events" +
                (lost.Count > 0
                    ? $"; off: {string.Join(", ", lost)} (the sync engine did not follow there; a phase start may need event types the plugin does not see, see --all-events)."
                    : "."));

            this.WriteList(output, "Missed", s.MissedReminders);
            this.WriteList(output, "Skipped", all.Where(r => r.IsAction && !r.Entry.Optional && r.State == ReminderState.Skipped).ToList());
        }

        private void WriteList(TextWriter output, string title, IReadOnlyList<Reminder> reminders)
        {
            if (reminders.Count == 0)
                return;
            output.WriteLine($"{title} ({reminders.Count}):");
            var table = new Table("time", "item", "mechanic", "entry");
            foreach (var r in reminders)
                table.Add(Text.PhaseTime(this.fight, r.PressAt, r.Mechanic.Phase, 1), PlanCommand.ItemName(r.Item, this.actions), $"{r.Mechanic.Name} ({r.Mechanic.Id})", r.Entry.Id);
            table.Write(output);
        }

        /// <summary>
        /// The player presses every unfinished action reminder once its press time is reached: the first option not missed
        /// that the job has a button for at the sync level. The session gets the id the game reports for that button, as
        /// in game: an upgrade replaces its base action (Raw Intuition is Bloodwhetting at 90) and a base action stands in
        /// for an upgrade not learned yet (Damnation is Vengeance), so a reminder whose item does not name that action is
        /// not marked done; an option the job cannot use at the level (or an unknown action) is not pressed at all.
        /// </summary>
        private void Press(double now)
        {
            if (!this.Session.TryTimelineTimeAt(now, out var t))
                return;
            foreach (var r in this.Session.Scheduler.All)
            {
                if (!r.IsAction || r.PressAt > t + 1e-9 || this.pressed.Contains(r) || IsFinished(r.State))
                    continue;
                this.pressed.Add(r);

                // Pressed exactly at the press time when the timeline ran into it during this frame.
                var lag = t - r.PressAt;
                var (clock, at) = lag <= Tick + 1e-6 ? (now - lag, r.PressAt) : (now, t);

                ActionInfo? button = null;
                var missed = false;
                foreach (var option in r.Item.Any is { } any ? Flatten(any) : [r.Item])
                {
                    if (option.Action is not { } id)
                        continue;
                    if (this.miss.Contains(option))
                    {
                        missed = true;
                        continue;
                    }

                    if (this.Button(id) is not { } b)
                        continue;
                    if (this.miss.Contains(b))
                    {
                        missed = true;
                        continue;
                    }

                    button = b;
                    break;
                }

                if (button == null)
                {
                    if (!missed)
                        this.Note(clock, at, r, $"not pressed: no {this.job} action for it at level {this.fight.SyncLevel}");
                    continue;
                }

                if (!r.ActionIds.Contains(button.Id))
                    this.Note(clock, at, r, $"pressed {button.Name} ({button.Id}), the action the game reports at level {this.fight.SyncLevel}: not the plan's, so the use is not tracked");
                this.Session.OnOwnUse(button.Id, clock);
                this.Diff(clock);
            }
        }

        /// <summary>The action the game reports when the player presses <paramref name="id"/> at the sync level, if the job can.</summary>
        private ActionInfo? Button(uint id)
        {
            if (this.actions.Get(id) is not { } action)
                return null;
            var level = this.fight.SyncLevel;
            var resolved = this.actions.ResolveAt(action, level);
            return resolved.AvailableAt(level) && (this.job == Job.None || resolved.UsableBy(this.job)) ? resolved : null;
        }

        private void Note(double clock, double timeline, Reminder r, string details) =>
            this.log.Add((clock, [Text.Clock(clock - PullClock, 1), Text.Sec(timeline), "press", PlanCommand.ItemName(r.Item, this.actions), Text.Clip(r.Mechanic.Name, 40), details]));

        /// <summary>
        /// Counts, per phase, the events meant to sync the timeline and those after which the engine's time matched the
        /// entry (or jump target) they stand for, as the end-to-end sync tests do. An event need not fire its own sync to be
        /// in sync: a phase sync at the exclusive end of its window does not fire in an unpushed pull, and that is fine.
        /// </summary>
        private void CountSync(SimulatedEvent sim)
        {
            if (sim.SyncTime is not { } expected || sim.Entry == null)
                return;
            var phase = this.fight.PhaseAt(sim.Entry.Time)?.Id ?? "-";
            var ok = this.Session.Player.IsRunning && Math.Abs(this.Session.Player.TimeAt(sim.Event.Time) - expected) <= this.syncTolerance;
            var (count, inSync) = this.syncsByPhase.GetValueOrDefault(phase);
            this.syncsByPhase[phase] = (count + 1, inSync + (ok ? 1 : 0));
        }

        private static List<PlanItem> Flatten(List<PlanItem> options) =>
            options.SelectMany(o => o.Any is { } nested ? Flatten(nested) : [o]).ToList();

        private static bool IsFinished(ReminderState s) =>
            s is ReminderState.Done or ReminderState.Missed or ReminderState.Skipped or ReminderState.Passed;

        private void Diff(double clock)
        {
            var all = this.Session.Scheduler.All;
            for (var i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r.State == this.states[i])
                    continue;
                this.states[i] = r.State;
                if (r.State == ReminderState.Pending)
                    continue;

                var (timeline, details) = r.State switch
                {
                    ReminderState.Done => (r.UsedAt ?? r.FinishedAt ?? 0, $"used at {Text.Sec(r.UsedAt ?? 0)}{(r.UsedEarly ? " (early)" : string.Empty)}{(r.UsedActionId is { } u && r.ActionIds.Count > 1 ? " as " + (this.actions.Get(u)?.Name ?? "#" + u) : string.Empty)}"),
                    ReminderState.Missed => (r.FinishedAt ?? 0, $"press {Text.Sec(r.PressAt)}, latest {Text.Sec(r.Latest)}"),
                    ReminderState.Skipped => (r.FinishedAt ?? 0, $"press {Text.Sec(r.PressAt)}, hit {Text.Sec(r.Hit)}"),
                    ReminderState.Passed => (r.FinishedAt ?? 0, string.Empty),
                    _ => (this.Session.TimelineTime, $"press {Text.Sec(r.PressAt)}, hit {Text.Sec(r.Hit)}"),
                };
                this.log.Add((clock, [
                    Text.Clock(clock - PullClock, 1),
                    Text.Sec(timeline),
                    r.State.ToString(),
                    PlanCommand.ItemName(r.Item, this.actions),
                    Text.Clip(r.Mechanic.Name, 40),
                    details,
                ]));
            }
        }

        private void OnSynced(SyncResult result)
        {
            if (result.Started)
                this.Event(result.Clock, result.ToTime, "start", $"timeline started at {Text.Sec(result.ToTime)}", $"\"{result.Entry.Name}\" (line {result.Entry.Line})");
            else if (result.Stopped)
                this.Event(result.Clock, 0, "stop", "timeline reset (jump 0)", $"\"{result.Entry.Name}\" (line {result.Entry.Line})");
            else if (this.logSyncs && !result.IsJump)
                this.Event(result.Clock, result.ToTime, "sync", $"{Text.Sec(result.FromTime)} -> {Text.Sec(result.ToTime)}", $"\"{result.Entry.Name}\" (line {result.Entry.Line})");
        }

        private string PhaseNote(double time) => this.fight.PhaseAt(time) is { } p ? $"{p.Name} ({Text.PhaseTime(this.fight, time, p.Id, 1)})" : string.Empty;

        private void Event(double clock, double timeline, string kind, string what, string details) =>
            this.log.Add((clock, [Text.Clock(clock - PullClock, 1), Text.Sec(timeline), kind, what, string.Empty, details]));
    }
}
