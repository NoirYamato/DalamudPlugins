using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

/// <summary>
/// Builders and test doubles for the reminder tests. Everything is built in code: no data files and
/// no other core services (fight/action loading is implemented elsewhere).
/// </summary>
internal static class Kit
{
    public const double Tolerance = 1e-9;

    // Durations/kinds as at level 90; only ids, kinds, durations and cast times matter here.
    public static readonly ActionInfo Temperance = Act(16536, "Temperance", ActionKind.PartyMit, duration: 20, jobs: [Job.WHM]);        // −6 s
    public static readonly ActionInfo Tetragrammaton = Act(3570, "Tetragrammaton", ActionKind.Heal, jobs: [Job.WHM]);                  // +1 s
    public static readonly ActionInfo DivineBenison = Act(7432, "Divine Benison", ActionKind.Shield, duration: 15, jobs: [Job.WHM]);    // −4 s
    public static readonly ActionInfo Adloquium = Act(185, "Adloquium", ActionKind.Shield, duration: 30, castTime: 2, jobs: [Job.SCH]); // −4.5 s
    public static readonly ActionInfo MedicaII = Act(133, "Medica II", ActionKind.Regen, duration: 15, jobs: [Job.WHM]);                // −4.5 s
    public static readonly ActionInfo Rampart = Act(7531, "Rampart", ActionKind.TankMit, duration: 20, jobs: [Job.PLD, Job.WAR, Job.DRK, Job.GNB]); // −6 s
    public static readonly ActionInfo HallowedGround = Act(30, "Hallowed Ground", ActionKind.Invuln, duration: 10, jobs: [Job.PLD]);  // −2 s
    public static readonly ActionInfo Swiftcast = Act(7561, "Swiftcast", ActionKind.Buff, duration: 10, jobs: [Job.WHM, Job.SCH, Job.AST, Job.SGE]); // −3 s

    // Pre-placed heals (actions.yaml "when: before") and a healer LB ("when: after"), as in the real catalog.
    public static readonly ActionInfo Bell = Act(25862, "Liturgy of the Bell", ActionKind.Heal, duration: 20, jobs: [Job.WHM], when: UseTiming.Before);     // −6 s
    public static readonly ActionInfo Excogitation = Act(7434, "Excogitation", ActionKind.Heal, duration: 45, jobs: [Job.SCH], when: UseTiming.Before);  // −8 s
    public static readonly ActionInfo EarthlyStar = Act(                                                                                                  // −10 s (lead)
        7439,
        "Earthly Star",
        ActionKind.Heal,
        duration: 20,
        jobs: [Job.AST],
        when: UseTiming.Before,
        lead: 10,
        effects: [Effect("Earthly Dominance", 10), Effect("Giant Dominance (full potency)", 10)]);

    public static readonly ActionInfo Horoscope = Act(                                                                                                    // −3 s
        16557,
        "Horoscope",
        ActionKind.Heal,
        duration: 10,
        jobs: [Job.AST],
        when: UseTiming.Before,
        effects: [Effect("Horoscope", 10), Effect("Horoscope Helios (after Helios / Helios Conjunction)", 30)]);

    public static readonly ActionInfo PulseOfLife = Act(208, "Pulse of Life", ActionKind.LimitBreak, jobs: [Job.WHM], when: UseTiming.After);           // +2 s

    /// <summary>An action the test catalog does not know (timed like a note, still tracked as an action).</summary>
    public static readonly ActionInfo Unlisted = Act(999_999, "Unlisted", ActionKind.PartyMit, duration: 30);

    public static IReadOnlyList<ActionInfo> Catalogued { get; } =
        [Temperance, Tetragrammaton, DivineBenison, Adloquium, MedicaII, Rampart, HallowedGround, Swiftcast, Bell, Excogitation, EarthlyStar, Horoscope, PulseOfLife];

    public static ActionInfo Act(
        uint id,
        string name,
        ActionKind kind,
        double? duration = null,
        double castTime = 0,
        Job[]? jobs = null,
        UseTiming when = UseTiming.Default,
        double? lead = null,
        ActionEffect[]? effects = null) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        CastTime = castTime,
        Jobs = jobs ?? [],
        DurationByLevel = duration is { } d ? new Dictionary<int, double> { [1] = d } : new Dictionary<int, double>(),
        When = when,
        Lead = lead,
        Effects = effects ?? [],
    };

    public static ActionEffect Effect(string name, double duration) => new(name, new Dictionary<int, double> { [1] = duration });

    public static Mechanic Mech(string id, double hit, params double[] hits) => new()
    {
        Id = id,
        Name = id,
        Phase = id.Split('.')[0],
        Hit = hit,
        Hits = hits,
    };

    /// <summary>A single-hit mechanic of the given kind (e.g. a phase's enrage).</summary>
    public static Mechanic MechOfKind(string id, double hit, MechanicKind kind) => new()
    {
        Id = id,
        Name = id,
        Phase = id.Split('.')[0],
        Hit = hit,
        Hits = [hit],
        Kind = kind,
    };

    public static FightDefinition Fight(int syncLevel, params Mechanic[] mechanics) => new()
    {
        Key = "test",
        Name = "Test Fight",
        Territory = 1,
        SyncLevel = syncLevel,
        TimelineFile = "test.txt",
        Mechanics = mechanics,
    };

    /// <summary>A fight with phases (id, start) and mechanics, at sync level 90.</summary>
    public static FightDefinition PhasedFight((string Id, double Start)[] phases, params Mechanic[] mechanics) => new()
    {
        Key = "test",
        Name = "Test Fight",
        Territory = 1,
        SyncLevel = 90,
        TimelineFile = "test.txt",
        Phases = phases.Select(p => new Phase { Id = p.Id, Name = p.Id.ToUpperInvariant(), Start = p.Start }).ToList(),
        Mechanics = mechanics,
    };

    /// <summary>Raidwide at 100, tank buster at 130, triple hit at 160/163/166, phase 2 raidwide at 300 (sync 90).</summary>
    public static FightDefinition TestFight() => Fight(
        90,
        Mech("p1.raidwide", 100),
        Mech("p1.buster", 130),
        Mech("p1.triple", 160, 160, 163, 166),
        Mech("p2.raidwide", 300));

    public static PlanItem Do(ActionInfo action) => PlanItem.ForAction(action.Id, action.Name);

    public static PlanItem Either(params ActionInfo[] options) => PlanItem.AnyOf(options.Select(Do));

    public static PlanItem Note(string text) => PlanItem.ForNote(text);

    public static PlanEntry Entry(string mechanic, double? offset, params PlanItem[] items) => new()
    {
        Id = $"{mechanic}@{offset}",
        Mechanic = mechanic,
        Offset = offset,
        Do = [.. items],
    };

    public static PlanEntry AsOptional(this PlanEntry entry)
    {
        entry.Optional = true;
        return entry;
    }

    public static PlanEntry AsWhenReady(this PlanEntry entry)
    {
        entry.WhenReady = true;
        return entry;
    }

    public static PlanEntry AtHit(this PlanEntry entry, int? hitIndex)
    {
        entry.HitIndex = hitIndex;
        return entry;
    }

    public static PlanEntry AsPhaseEnd(this PlanEntry entry)
    {
        entry.AtPhaseEnd = true;
        return entry;
    }

    /// <summary>
    /// Phases p1 (0), p2 (200), p3 (400). P1: raidwide 100, late raidwide 140, enrage 150. P2: start marker 200, raidwide
    /// 250, enrage 350. P3: raidwide 450.
    /// </summary>
    public static FightDefinition PhasedTestFight() => PhasedFight(
        [("p1", 0), ("p2", 200), ("p3", 400)],
        Mech("p1.raidwide", 100),
        Mech("p1.late", 140),
        MechOfKind("p1.enrage", 150, MechanicKind.Enrage),
        MechOfKind("p2.start", 200, MechanicKind.Marker),
        Mech("p2.raidwide", 250),
        MechOfKind("p2.enrage", 350, MechanicKind.Enrage),
        Mech("p3.raidwide", 450));

    /// <summary>A scheduler for <see cref="TestFight"/> with default options and the test catalog.</summary>
    public static ReminderScheduler Scheduler(params PlanEntry[] entries) => Scheduler(TestFight(), new ReminderOptions(), entries);

    public static ReminderScheduler Scheduler(ReminderOptions options, params PlanEntry[] entries) => Scheduler(TestFight(), options, entries);

    public static ReminderScheduler Scheduler(FightDefinition fight, ReminderOptions options, params PlanEntry[] entries) =>
        new(fight, entries, new FakeCatalog(Catalogued), options);

    /// <summary>Collects every BecameDue notification, in order.</summary>
    public static List<Reminder> RecordDue(ReminderScheduler scheduler)
    {
        var due = new List<Reminder>();
        scheduler.BecameDue += due.Add;
        return due;
    }

    public static void Close(double expected, double? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected, actual.Value, Tolerance);
    }
}

/// <summary>In-memory <see cref="IActionCatalog"/>.</summary>
internal sealed class FakeCatalog(IEnumerable<ActionInfo> actions) : IActionCatalog
{
    private readonly Dictionary<uint, ActionInfo> byId = actions.ToDictionary(a => a.Id);

    public IEnumerable<ActionInfo> All => this.byId.Values;

    public ActionInfo? Get(uint id) => this.byId.GetValueOrDefault(id);

    public ActionInfo? FindByName(string name, Job? job = null) =>
        this.byId.Values.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) && (job == null || a.UsableBy(job.Value)));

    public IEnumerable<ActionInfo> ForJob(Job job, int syncLevel) => this.byId.Values.Where(a => a.UsableBy(job) && a.AvailableAt(syncLevel));
}

/// <summary>Scriptable <see cref="ICooldownProvider"/>: unknown (null) unless set; counts queries.</summary>
internal sealed class FakeCooldowns : ICooldownProvider
{
    public Dictionary<uint, double?> Seconds { get; } = [];

    public Dictionary<uint, bool?> Resources { get; } = [];

    public int SecondsQueries { get; private set; }

    public int ResourceQueries { get; private set; }

    public FakeCooldowns Set(ActionInfo action, double? secondsUntilReady)
    {
        this.Seconds[action.Id] = secondsUntilReady;
        return this;
    }

    public double? SecondsUntilReady(uint actionId)
    {
        this.SecondsQueries++;
        return this.Seconds.TryGetValue(actionId, out var s) ? s : null;
    }

    public bool? HasResources(uint actionId)
    {
        this.ResourceQueries++;
        return this.Resources.TryGetValue(actionId, out var r) ? r : null;
    }
}
