using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P2PartySynergy;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The symbols of the LPDU slideshow's example (https://ff14.toolboxgaming.space/?id=502396712985961):
// G1 (MT, H1, M1, R1) holds Cross, Circle, Square, Circle and G2 (OT, H2, M2, R2) Square, Triangle,
// Triangle, Cross, so R1 and M2 flex. Both new norths are pinned to N, so expected spots are the AI's
// own coordinates: eye up for the spread, the three Omega-Ms up for the knockback.
public class TopP2PartySynergyLpduAiTests
{
    private const int LpduStrat = 0;
    private const float Tolerance = 0.75f;

    private static readonly Dictionary<PartyRole, PlaystationSymbol> SlideshowSymbols = new()
    {
        [MainTank] = PlaystationSymbol.Cross,
        [CasterDps] = PlaystationSymbol.Cross,
        [OffTank] = PlaystationSymbol.Square,
        [MeleeDpsA] = PlaystationSymbol.Square,
        [RegenHealer] = PlaystationSymbol.Circle,
        [PhysRangedDps] = PlaystationSymbol.Circle,
        [ShieldHealer] = PlaystationSymbol.Triangle,
        [MeleeDpsB] = PlaystationSymbol.Triangle,
    };

    // The 11s dodge sets off before the flexers arrive, so the flex is judged by where it sends them.
    [Test]
    public void SymbolFlexersTradeColumnSpots()
        => AssertSpots(GlitchType.Mid, [MeleeDpsA, CasterDps], at: 10.9f, new()
        {
            [MainTank] = new(-8, -6), [RegenHealer] = new(-8, -3), [MeleeDpsA] = new(-8, 0), [MeleeDpsB] = new(-8, 3),
            [OffTank] = new(8, -6), [ShieldHealer] = new(8, -3), [PhysRangedDps] = new(8, 0), [CasterDps] = new(8, 3),
        }, destinations: true);

    [Test]
    public void MidSpreadIsBpogOnBothSides()
        => AssertSpots(GlitchType.Mid, [MeleeDpsA, CasterDps], at: 21.6f, new()
        {
            [MainTank] = new(-11.13f, -15.89f), [MeleeDpsA] = new(-11.63f, -4.82f), [RegenHealer] = new(-11.63f, 4.82f), [MeleeDpsB] = new(-11.13f, 15.89f),
            [CasterDps] = new(11.13f, -15.89f), [OffTank] = new(11.63f, -4.82f), [PhysRangedDps] = new(11.63f, 4.82f), [ShieldHealer] = new(11.13f, 15.89f),
        });

    // The corners stand on the diagonal shurikens, the middle rows 3 notches either side of the side ones.
    [Test]
    public void RemoteSpreadReversesRightColumnToGopb()
        => AssertSpots(GlitchType.Far, [MeleeDpsA, CasterDps], at: 21.6f, new()
        {
            [MainTank] = new(-13.72f, -13.72f), [MeleeDpsA] = new(-18.74f, -5.02f), [RegenHealer] = new(-18.74f, 5.02f), [MeleeDpsB] = new(-13.72f, 13.72f),
            [ShieldHealer] = new(13.72f, -13.72f), [PhysRangedDps] = new(18.74f, -5.02f), [OffTank] = new(18.74f, 5.02f), [CasterDps] = new(13.72f, 13.72f),
        });

    [Test]
    public void StacksOnBothSidesNeedNoFlex()
        => AssertSpots(GlitchType.Mid, [MeleeDpsA, CasterDps], at: 33.1f, new()
        {
            [MainTank] = new(-15, 0), [MeleeDpsA] = new(-15, 0), [RegenHealer] = new(-15, 0), [MeleeDpsB] = new(-15, 0),
            [CasterDps] = new(0, 15), [OffTank] = new(0, 15), [PhysRangedDps] = new(0, 15), [ShieldHealer] = new(0, 15),
        });

    // Square and Triangle stacks both left: Triangle is further from the eye, so M2 trades with H2.
    [Test]
    public void MidStackFlexMovesTheStackFurthestFromTheEye()
        => AssertSpots(GlitchType.Mid, [MeleeDpsA, MeleeDpsB], at: 33.1f, new()
        {
            [MainTank] = new(-15, 0), [MeleeDpsA] = new(-15, 0), [RegenHealer] = new(-15, 0), [ShieldHealer] = new(-15, 0),
            [CasterDps] = new(0, 15), [OffTank] = new(0, 15), [PhysRangedDps] = new(0, 15), [MeleeDpsB] = new(0, 15),
        });

    // The reversed right column puts Cross furthest from the eye, so R2 trades with MT, not H2.
    [Test]
    public void RemoteStackFlexReadsTheReversedColumn()
        => AssertSpots(GlitchType.Far, [ShieldHealer, CasterDps], at: 33.1f, new()
        {
            [CasterDps] = new(-19, 0), [MeleeDpsA] = new(-19, 0), [RegenHealer] = new(-19, 0), [MeleeDpsB] = new(-19, 0),
            [MainTank] = new(19, 0), [OffTank] = new(19, 0), [PhysRangedDps] = new(19, 0), [ShieldHealer] = new(19, 0),
        });

    private static void AssertSpots(GlitchType glitch, PartyRole[] stacks, float at, Dictionary<PartyRole, Vector2> expected,
                                    bool destinations = false)
    {
        var seen = new Dictionary<PartyRole, Vector2>();
        var run = ScenarioRun.Execute(typeof(TopP2PartySynergyScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = at + 0.1f,
            Overrides = o => Pin((TopP2PartySynergyStateOverrides)o, glitch, stacks),
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
                            $"{role} at {seen[role]}, expected {spot} (seed {run.Seed})");
        });
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    private static void Pin(TopP2PartySynergyStateOverrides o, GlitchType glitch, PartyRole[] stacks)
    {
        o.NewNorthA = Direction.N;
        o.NewNorthB = Direction.N;
        o.Glitch = glitch;
        foreach (var (role, symbol) in SlideshowSymbols)
        {
            o.Symbol[role] = symbol;
            o.Stack[role] = stacks.Contains(role);
        }
    }
}
