using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.PluginTests;

/// <summary>A small in-memory action catalog (the real one is loaded by MitCue.Core and may not exist in every build).</summary>
internal sealed class FakeCatalog(IEnumerable<ActionInfo> actions) : IActionCatalog
{
    private readonly List<ActionInfo> actions = actions.ToList();

    public IEnumerable<ActionInfo> All => this.actions;

    public ActionInfo? Get(uint id) => this.actions.FirstOrDefault(a => a.Id == id);

    public ActionInfo? FindByName(string name, Job? job = null) =>
        this.actions
            .Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => job is { } j && a.UsableBy(j))
            .FirstOrDefault();

    public IEnumerable<ActionInfo> ForJob(Job job, int syncLevel) => this.actions.Where(a => a.UsableBy(job) && a.AvailableAt(syncLevel));
}

/// <summary>Fixtures shared by the tests: a cut-down TOP and a few healer actions.</summary>
internal static class TestData
{
    public const uint Temperance = 16536;
    public const uint Tetragrammaton = 3570;
    public const uint DivineBenison = 7432;
    public const uint LiturgyOfTheBell = 25862;
    public const uint DivineCaress = 37011;
    public const uint SacredSoil = 188;
    public const uint Adloquium = 185;
    public const uint Swiftcast = 7561;
    public const uint Succor = 186;
    public const uint Concitation = 37013;

    public static FightDefinition Top() => new()
    {
        Key = "top",
        Name = "The Omega Protocol (Ultimate)",
        Territory = 1122,
        SyncLevel = 90,
        TimelineFile = "the_omega_protocol.txt",
        Phases =
        [
            new Phase { Id = "p1", Name = "P1: Omega", Start = 0 },
            new Phase { Id = "p2", Name = "P2: Omega-M/F", Start = 200 },
        ],
        Mechanics =
        [
            new Mechanic { Id = "p1.pull", Name = "Pull", Phase = "p1", Hit = 0, Hits = [0], Kind = MechanicKind.Marker },
            new Mechanic
            {
                Id = "p1.program_loop",
                Name = "Program Loop",
                Phase = "p1",
                Hit = 28.2,
                Hits = [28.2, 37.2, 46.2, 55.2],
                Entry = "Storage Violation",
                Kind = MechanicKind.Mixed,
                Aliases = ["Program Loop: towers (36k) + blasters (HP to 1%) ★", "Loop"],
            },
            new Mechanic
            {
                Id = "p1.pantokrator",
                Name = "Pantokrator",
                Phase = "p1",
                Hit = 81.2,
                Hits = [81.2, 87.2, 93.2, 99.2],
                Kind = MechanicKind.Multihit,
                Aliases = ["Pantokrator", "Panto"],
            },
            new Mechanic { Id = "p2.solar_ray", Name = "Solar Ray", Phase = "p2", Hit = 240, Hits = [240], Kind = MechanicKind.Tankbuster },
        ],
        Calibration = new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["carry_the_clear"] = new Dictionary<string, double> { ["p1"] = 0.9, ["p2"] = 201.8 },
        },
    };

    public static FakeCatalog Catalog() => new(
    [
        new ActionInfo { Id = Temperance, Name = "Temperance", Jobs = [Job.WHM], Level = 80, Kind = ActionKind.PartyMit, DurationByLevel = new Dictionary<int, double> { [80] = 20 } },
        new ActionInfo { Id = Tetragrammaton, Name = "Tetragrammaton", Jobs = [Job.WHM], Level = 60, Kind = ActionKind.Heal },
        new ActionInfo { Id = DivineBenison, Name = "Divine Benison", Jobs = [Job.WHM], Level = 66, Kind = ActionKind.Shield },
        new ActionInfo { Id = LiturgyOfTheBell, Name = "Liturgy of the Bell", Jobs = [Job.WHM], Level = 90, Kind = ActionKind.Heal },
        new ActionInfo { Id = DivineCaress, Name = "Divine Caress", Jobs = [Job.WHM], Level = 100, Kind = ActionKind.Shield },
        new ActionInfo { Id = SacredSoil, Name = "Sacred Soil", Jobs = [Job.SCH], Level = 50, Kind = ActionKind.PartyMit, DurationByLevel = new Dictionary<int, double> { [50] = 15 } },
        new ActionInfo { Id = Adloquium, Name = "Adloquium", Jobs = [Job.SCH], Level = 30, Kind = ActionKind.Shield, CastTime = 2, IsGcd = true },
        new ActionInfo { Id = Succor, Name = "Succor", Jobs = [Job.SCH], Level = 35, Kind = ActionKind.Shield, CastTime = 2, IsGcd = true, ReplacedBy = Concitation, ReplacedAtLevel = 96 },
        new ActionInfo { Id = Concitation, Name = "Concitation", Jobs = [Job.SCH], Level = 96, Kind = ActionKind.Shield, CastTime = 2, IsGcd = true },
        new ActionInfo { Id = Swiftcast, Name = "Swiftcast", Jobs = [Job.WHM, Job.SCH, Job.AST, Job.SGE], Level = 18, Kind = ActionKind.Buff },
    ]);

    public static PlanItem Action(uint id, string name) => PlanItem.ForAction(id, name);

    public static PlanEntry Entry(string id, string mechanic, Job? job, params PlanItem[] items) => new()
    {
        Id = id,
        Mechanic = mechanic,
        Who = new WhoSpec { Job = job },
        Do = items.ToList(),
    };

    /// <summary>A tab whose rows are the given cell texts (null = empty cell).</summary>
    public static SheetTab Tab(string name, params string?[][] rows) => new()
    {
        Name = name,
        Rows = rows.Select(r => (IReadOnlyList<SheetCell>)r.Select(t => t == null ? SheetCell.Empty : new SheetCell { Text = t }).ToList()).ToList(),
    };
}
