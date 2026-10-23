using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Intermission;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The conga runs R1 M1 MT H1 H2 OT M2 R2 west to east on the waymark 3-2 chord (z 9.6, x -9 to 9).
// Counted west to east within each debuff, spreads 1-4 take the walls W, SW, SE and E, stacks 1-2 NW
// and NE, and nothing k joins stack k. The marks follow the same count: Attack1-4, Bind1-2, Ignore1-2.
//
// The pinned roll (Pin): R1 MT OT R2 spread, M1 M2 stack, H1 H2 nothing. Spots are x east, z south,
// written out rather than computed; the south triangle's are the north triangle's mirrored in z, with
// the walls SW and NW (and SE and NE) trading places.
public class TopP3IntermissionLpduAiTests
{
    private const int LpduStrat = 0;
    private const float Tolerance = 0.75f;

    internal static readonly PartyRole[] PinnedDebuffOrder =
        [PhysRangedDps, MainTank, OffTank, CasterDps, MeleeDpsA, MeleeDpsB, RegenHealer, ShieldHealer];

    // M1 H2 M2 R2 spread, R1 OT stack, MT H1 nothing.
    private static readonly PartyRole[] OtherDebuffOrder =
        [MeleeDpsA, ShieldHealer, MeleeDpsB, CasterDps, PhysRangedDps, OffTank, MainTank, RegenHealer];

    private static readonly Dictionary<PartyRole, Vector2> Conga = new()
    {
        [PhysRangedDps] = new(-9f, 9.6f), [MeleeDpsA] = new(-6.43f, 9.6f), [MainTank] = new(-3.86f, 9.6f),
        [RegenHealer] = new(-1.29f, 9.6f), [ShieldHealer] = new(1.29f, 9.6f), [OffTank] = new(3.86f, 9.6f),
        [MeleeDpsB] = new(6.43f, 9.6f), [CasterDps] = new(9f, 9.6f),
    };

    private static readonly Dictionary<PartyRole, Vector2> ReadStep = new()
    {
        [PhysRangedDps] = new(-9f, 13.6f), [MeleeDpsA] = new(-6.43f, 5.6f), [MainTank] = new(-3.86f, 13.6f),
        [RegenHealer] = new(-1.29f, 9.6f), [ShieldHealer] = new(1.29f, 9.6f), [OffTank] = new(3.86f, 13.6f),
        [MeleeDpsB] = new(6.43f, 5.6f), [CasterDps] = new(9f, 13.6f),
    };

    // W, SW, SE, E, NW, NE.
    private static readonly Vector2[] NorthWait = [new(-18.40f, -3.51f), new(-6.16f, 17.69f), new(6.16f, 17.69f), new(18.40f, -3.51f), new(-12.24f, -14.18f), new(12.24f, -14.18f)];
    private static readonly Vector2[] NorthIn = [new(-16.50f, -3.00f), new(-5.65f, 15.79f), new(5.65f, 15.79f), new(16.50f, -3.00f), new(-10.85f, -12.79f), new(10.85f, -12.79f)];
    private static readonly Vector2[] NorthFinal = [new(-16.00f, 4.50f), new(-11.89f, 11.61f), new(11.89f, 11.61f), new(16.00f, 4.50f), new(-4.11f, -16.11f), new(4.11f, -16.11f)];
    private static readonly Vector2[] SouthWait = [new(-18.40f, 3.51f), new(-12.24f, 14.18f), new(12.24f, 14.18f), new(18.40f, 3.51f), new(-6.16f, -17.69f), new(6.16f, -17.69f)];
    private static readonly Vector2[] SouthIn = [new(-16.50f, 3.00f), new(-10.85f, 12.79f), new(10.85f, 12.79f), new(16.50f, 3.00f), new(-5.65f, -15.79f), new(5.65f, -15.79f)];
    private static readonly Vector2[] SouthFinal = [new(-16.00f, -4.50f), new(-4.11f, 16.11f), new(4.11f, 16.11f), new(16.00f, -4.50f), new(-11.89f, -11.61f), new(11.89f, -11.61f)];

    // Hello World's opening spread at r 8.5: MT N, R1 NE, H2 E, M2 SE, OT S, M1 SW, H1 W, R2 NW.
    private static readonly Dictionary<PartyRole, Vector2> LooseSpread = new()
    {
        [MainTank] = new(0f, -8.5f), [PhysRangedDps] = new(6.01f, -6.01f), [ShieldHealer] = new(8.5f, 0f),
        [MeleeDpsB] = new(6.01f, 6.01f), [OffTank] = new(0f, 8.5f), [MeleeDpsA] = new(-6.01f, 6.01f),
        [RegenHealer] = new(-8.5f, 0f), [CasterDps] = new(-6.01f, -6.01f),
    };

    private static readonly Sign[] PinnedSigns =
        [Sign.Attack1, Sign.Attack2, Sign.Attack3, Sign.Attack4, Sign.Bind1, Sign.Bind2, Sign.Ignore1, Sign.Ignore2];

    [TestCase(MainTank, Automarkers.On)]
    [TestCase(RegenHealer, Automarkers.Off)]
    [TestCase(CasterDps, Automarkers.On)]
    public void LinesUpInCongaOrderThenStepsByDebuff(PartyRole player, Automarkers markers)
    {
        AssertSpots(PinnedDebuffOrder, FirstArms.NorthTriangle, markers, player, at: 9f, Conga);
        AssertSpots(PinnedDebuffOrder, FirstArms.NorthTriangle, markers, player, at: 10.5f, ReadStep);
    }

    [TestCase(FirstArms.NorthTriangle, Automarkers.On)]
    [TestCase(FirstArms.NorthTriangle, Automarkers.Off)]
    [TestCase(FirstArms.SouthTriangle, Automarkers.On)]
    [TestCase(FirstArms.SouthTriangle, Automarkers.Off)]
    public void DodgesFromTheWallIntoTheFirstArmsSpentCircle(FirstArms arms, Automarkers markers)
    {
        var north = arms == FirstArms.NorthTriangle;
        AssertSpots(PinnedDebuffOrder, arms, markers, MainTank, at: 18f, ByGroup(PinnedGroups, north ? NorthWait : SouthWait));
        AssertSpots(PinnedDebuffOrder, arms, markers, MainTank, at: 20f, ByGroup(PinnedGroups, north ? NorthIn : SouthIn));
        AssertSpots(PinnedDebuffOrder, arms, markers, MainTank, at: 26f, ByGroup(PinnedGroups, north ? NorthWait : SouthWait));
        AssertSpots(PinnedDebuffOrder, arms, markers, MainTank, at: 28.2f, ByGroup(PinnedGroups, north ? NorthFinal : SouthFinal));
    }

    // M1 is the westernmost spread, so it takes W although it stands second in the conga.
    [TestCase(FirstArms.NorthTriangle)]
    [TestCase(FirstArms.SouthTriangle)]
    public void CountsEachDebuffWestToEastAlongTheConga(FirstArms arms)
    {
        PartyRole[][] groups = [[MeleeDpsA], [ShieldHealer], [MeleeDpsB], [CasterDps], [PhysRangedDps, MainTank], [OffTank, RegenHealer]];
        AssertSpots(OtherDebuffOrder, arms, Automarkers.On, OffTank, at: 28.2f,
                    ByGroup(groups, arms == FirstArms.NorthTriangle ? NorthFinal : SouthFinal));
    }

    // They come straight in to r 8.5 first and cross the middle only once the void zone has gone.
    [Test]
    public void ComesInToHelloWorldsOpeningSpreadAfterTheSecondArms()
        => AssertSpots(PinnedDebuffOrder, FirstArms.SouthTriangle, Automarkers.On, ShieldHealer, at: 31f, LooseSpread, destinations: true);

    [TestCase(MainTank)]
    [TestCase(ShieldHealer)]
    public void AutomarkersSignEveryoneInCongaOrder(PartyRole player)
    {
        var marks = Marks(PinnedDebuffOrder, Automarkers.On, player, Random.Shared.Next());

        var expected = PinnedDebuffOrder.Select((role, i) => (role, PinnedSigns[i])).ToDictionary(t => t.role, t => (t.Item2, 9.7f));
        Assert.That(marks.Keys, Is.EquivalentTo(PerRole.All));
        Assert.Multiple(() =>
        {
            foreach (var (role, (sign, at)) in expected)
            {
                Assert.That(marks[role].Sign, Is.EqualTo(sign), $"{role}'s sign");
                Assert.That(marks[role].At, Is.EqualTo(at).Within(0.05f), $"{role}'s mark time");
            }
        });
    }

    [Test]
    public void AutomarkersOffPlacesNoSign()
        => Assert.That(Marks(PinnedDebuffOrder, Automarkers.Off, MainTank, Random.Shared.Next()), Is.Empty);

    [Test]
    public void AutomarkersOnAndOffPutEveryoneOnTheSameSpots()
    {
        foreach (var seed in Enumerable.Range(0, 20).Select(_ => Random.Shared.Next()))
        {
            var on = FinalSpots(seed, Automarkers.On);
            var off = FinalSpots(seed, Automarkers.Off);
            foreach (var role in PerRole.All)
                Assert.That(Vector2.Distance(on[role], off[role]), Is.LessThan(0.01f),
                            $"{role}: {on[role]} with automarkers, {off[role]} without (seed {seed})");
        }
    }

    // The catalog's soak runs only the default setting (On).
    [Test]
    public void AutomarkersOffClearsEverySeed()
    {
        var seeds = Math.Max(25, TestContext.Parameters.Get("SunnySeeds", 0));
        var failures = Enumerable.Range(0, seeds)
            .Select(_ => ScenarioRun.Execute(typeof(TopP3IntermissionScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
            {
                Overrides = o => ((TopP3IntermissionStateOverrides)o).Automarkers = Automarkers.Off,
            }))
            .Where(run => !run.Passed)
            .ToList();

        Assert.That(failures, Is.Empty, () => $"{failures.Count}/{seeds} seeds failed:{Environment.NewLine}"
                                               + string.Join(Environment.NewLine, failures));
    }

    private static readonly PartyRole[][] PinnedGroups =
        [[PhysRangedDps], [MainTank], [OffTank], [CasterDps], [MeleeDpsA, RegenHealer], [MeleeDpsB, ShieldHealer]];

    private static Dictionary<PartyRole, Vector2> ByGroup(PartyRole[][] groups, Vector2[] spots)
        => groups.SelectMany((roles, i) => roles.Select(role => (role, spot: spots[i]))).ToDictionary(t => t.role, t => t.spot);

    private static void AssertSpots(PartyRole[] debuffOrder, FirstArms arms, Automarkers markers, PartyRole player, float at,
                                    Dictionary<PartyRole, Vector2> expected, bool destinations = false)
    {
        var seen = new Dictionary<PartyRole, Vector2>();
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = at + 0.1f,
            Overrides = o => Pin((TopP3IntermissionStateOverrides)o, arms, markers, debuffOrder),
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                foreach (var role in PerRole.All)
                    if (p.Member(role) is { } member)
                        seen[role] = Flat(destinations ? member.MoveDestination ?? member.Position : member.Position);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (role, spot) in expected)
                Assert.That(Vector2.Distance(seen[role], spot), Is.LessThan(Tolerance),
                            $"{role} at {seen[role]} at t={at}, expected {spot} (seed {run.Seed})");
        });
    }

    // The sign each member was marked with, and when; rows with no target are the clears.
    private static Dictionary<PartyRole, (Sign Sign, float At)> Marks(PartyRole[] debuffOrder, Automarkers markers, PartyRole player, int seed)
    {
        var marks = new Dictionary<PartyRole, (Sign, float)>();
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = player,
            Overrides = o => Pin((TopP3IntermissionStateOverrides)o, FirstArms.NorthTriangle, markers, debuffOrder),
            Probe = p =>
            {
                if (!p.Crossed(30f)) return;
                var roleById = PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
                foreach (var row in p.Fake.Timeline.Rows.Where(r => r["kind"] == "ac_target" && r["id"] == "0x1F6" && r["target"] != ""))
                {
                    var sign = (Sign)Convert.ToUInt32(row["p"].Split(',')[0], 16);
                    marks[roleById[row["target"]]] = (sign, float.Parse(row["t"], System.Globalization.CultureInfo.InvariantCulture));
                }
            },
        });

        Assert.That(run.Passed, run.ToString);
        return marks;
    }

    private static Dictionary<PartyRole, Vector2> FinalSpots(int seed, Automarkers markers)
    {
        var seen = new Dictionary<PartyRole, Vector2>();
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 28.3f,
            Overrides = o => ((TopP3IntermissionStateOverrides)o).Automarkers = markers,
            Probe = p =>
            {
                if (!p.Crossed(28.2f)) return;
                foreach (var role in PerRole.All)
                    seen[role] = Flat(p.Member(role)!.Position);
            },
        });

        Assert.That(run.Passed, run.ToString);
        return seen;
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    internal static void Pin(TopP3IntermissionStateOverrides o, FirstArms arms, Automarkers markers = Automarkers.On,
                             PartyRole[]? debuffOrder = null)
    {
        o.DebuffOrder = debuffOrder ?? PinnedDebuffOrder;
        o.FirstArms = arms;
        o.Automarkers = markers;
    }
}
