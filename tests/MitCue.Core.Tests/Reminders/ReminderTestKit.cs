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

    /// <summary>An action the test catalog does not know (timed like a note, still tracked as an action).</summary>
    public static readonly ActionInfo Unlisted = Act(999_999, "Unlisted", ActionKind.PartyMit, duration: 30);

    public static IReadOnlyList<ActionInfo> Catalogued { get; } =
        [Temperance, Tetragrammaton, DivineBenison, Adloquium, MedicaII, Rampart, HallowedGround, Swiftcast];

    public static ActionInfo Act(uint id, string name, ActionKind kind, double? duration = null, double castTime = 0, Job[]? jobs = null) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        CastTime = castTime,
        Jobs = jobs ?? [],
        DurationByLevel = duration is { } d ? new Dictionary<int, double> { [1] = d } : new Dictionary<int, double>(),
    };

    public static Mechanic Mech(string id, double hit, params double[] hits) => new()
    {
        Id = id,
        Name = id,
        Phase = id.Split('.')[0],
        Hit = hit,
        Hits = hits,
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
