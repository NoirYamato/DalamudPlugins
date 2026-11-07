using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6CosmoDive1;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// LPDU's spots (x east, z south; waymark 2 is SE at (9.64, 9.64)), written out here rather than read from the strat:
//
//    0.0   the autos: MT inside the hitbox north (0, -5), OT outside it east (14.5, 0) and the farthest, the rest
//          inside south (0, 6). 4.9: the MT's limit break 3, halfway through Cosmo Memory (cast 1.9, hit 7.6).
//   12.9 / 16.1   Flash Gale on the MT and the farthest.
//   16.3   Cosmo Arrow's cast: everyone stacks in front of waymark 2 at (7.5, 7.5). The first set's lines show at 18.3.
//   24.0   T: the first set lands, the cross |c| < 5 in first or the outer lines 10 < |c| < 20 out first; then a wave
//          every 2 s from 26.0 (the second set) and 26.3 (the first rolling lines) to 38.3 in first, 36.3 out first.
//            In first:  24.1 into the centre square (3, 3); 28.4 back to (7.5, 7.5); 32.4 side step south to
//                       (3, 12); 34.4 in to (3, 8) while the tanks leave for their dive spots; 36.4 the six back
//                       to (7.5, 7.5).
//            Out first: 24.1 out to the corner (12, 12); 28.4 back to (7.5, 7.5); 30.4 side step to (3, 12); 32.4 in
//                       to (3, 8), the tanks leave; 34.4 the six back to (7.5, 7.5).
//   39.4   Cosmo Dive: the MT at (-4.6, -4.6) toward waymark 4, the OT at (4.6, -4.6) toward waymark 1, both r 6.5 and
//          the two closest; the six at (7.5, 7.5), r 10.6, share the stack.
//   40.0   back to the autos' spots for 47.1 / 50.3.
public class TopP6CosmoDive1LpduAiTests
{
    internal const int LpduStrat = 0;
    private const float Tolerance = 0.45f;

    internal static readonly PartyRole[] TheSix = [RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    internal static readonly Vector2 MainTankAutoSpot = new(0f, -5f);
    internal static readonly Vector2 OffTankAutoSpot = new(14.5f, 0f);
    internal static readonly Vector2 PartyAutoSpot = new(0f, 6f);
    internal static readonly Vector2 WaymarkTwoStack = new(7.5f, 7.5f);
    internal static readonly Vector2 CentreSquare = new(3f, 3f);
    internal static readonly Vector2 Corner = new(12f, 12f);
    internal static readonly Vector2 SideStep = new(3f, 12f);
    internal static readonly Vector2 InStep = new(3f, 8f);
    internal static readonly Vector2 MainTankDiveSpot = new(-4.6f, -4.6f);
    internal static readonly Vector2 OffTankDiveSpot = new(4.6f, -4.6f);

    internal static Vector2 AutoSpot(PartyRole role) => role switch
    {
        MainTank => MainTankAutoSpot,
        OffTank => OffTankAutoSpot,
        _ => PartyAutoSpot,
    };

    internal static Vector2 DiveSpot(PartyRole role) => role switch
    {
        MainTank => MainTankDiveSpot,
        OffTank => OffTankDiveSpot,
        _ => WaymarkTwoStack,
    };

    public enum Spot { Autos, WaymarkTwo, FirstDodge, SideStep, InStep, Dive }

    // Just before each hit or move the spot is held for, once every bot has settled.
    [TestCase(true, 12.8f, Spot.Autos, Description = "the first autos")]
    [TestCase(false, 16.0f, Spot.Autos, Description = "the second autos")]
    [TestCase(true, 23.9f, Spot.WaymarkTwo, Description = "T")]
    [TestCase(false, 23.9f, Spot.WaymarkTwo, Description = "T")]
    [TestCase(true, 26.2f, Spot.FirstDodge, Description = "T+2")]
    [TestCase(false, 26.2f, Spot.FirstDodge, Description = "T+2")]
    [TestCase(true, 30.2f, Spot.WaymarkTwo, Description = "T+6")]
    [TestCase(false, 30.2f, Spot.WaymarkTwo, Description = "T+6")]
    [TestCase(true, 34.2f, Spot.SideStep, Description = "T+10")]
    [TestCase(false, 32.2f, Spot.SideStep, Description = "T+8")]
    [TestCase(true, 36.2f, Spot.InStep, Description = "T+12")]
    [TestCase(false, 34.2f, Spot.InStep, Description = "T+10")]
    [TestCase(true, 39.3f, Spot.Dive, Description = "the dive")]
    [TestCase(false, 39.3f, Spot.Dive, Description = "the dive")]
    [TestCase(true, 47.0f, Spot.Autos, Description = "the autos after the dive")]
    [TestCase(false, 47.0f, Spot.Autos, Description = "the autos after the dive")]
    public void StandsOnLpdusSpots(bool inFirst, float at, Spot spot)
    {
        var seen = Probe(inFirst, [at], Random.Shared.Next(), out var run);

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (role, expected) in ExpectedSpots(spot, inFirst))
                Assert.That(Vector2.Distance(seen[at][role], expected), Is.LessThan(Tolerance),
                            $"{role} at {seen[at][role]} at {at}s, expected {expected} ({(inFirst ? "in" : "out")} first, seed {run.Seed})");
        });
    }

    // The first move a pattern changes goes after the first set lands, 6 s after its lines show.
    [Test]
    public void NothingDependsOnThePatternBeforeTheFirstSetLands()
    {
        var seed = Random.Shared.Next();
        var times = Enumerable.Range(0, 241).Select(i => i * 0.1f).ToArray();
        var inFirst = Probe(true, times, seed, out var inRun);
        var outFirst = Probe(false, times, seed, out var outRun);

        Assert.That(inRun.Failure ?? outRun.Failure, Is.Null);
        Assert.Multiple(() =>
        {
            foreach (var at in times)
                foreach (var role in PerRole.All.Where(r => r != inRun.PlayerRole))
                    Assert.That(Vector2.Distance(inFirst[at][role], outFirst[at][role]), Is.LessThan(1e-3f), $"{role} at {at:F1}s");
        });
    }

    [Test]
    public void TheStratsSpotsAreLpdus()
        => Assert.Multiple(() =>
        {
            Assert.That(TopP6LpduAi.MainTankInsideNorthForTheAutos, Is.EqualTo(MainTankAutoSpot));
            Assert.That(TopP6LpduAi.OffTankOutsideEastAndFarthestForTheAutos, Is.EqualTo(OffTankAutoSpot));
            Assert.That(TopP6LpduAi.TheRestInsideSouthForTheAutos, Is.EqualTo(PartyAutoSpot));
            Assert.That(TopP6CosmoDive1LpduAi.InFrontOfWaymarkTwo, Is.EqualTo(WaymarkTwoStack));
            Assert.That(TopP6CosmoDive1LpduAi.InTheCentreSquare, Is.EqualTo(CentreSquare));
            Assert.That(TopP6CosmoDive1LpduAi.OutOnTheCorner, Is.EqualTo(Corner));
            Assert.That(TopP6CosmoDive1LpduAi.SideStepSouth, Is.EqualTo(SideStep));
            Assert.That(TopP6CosmoDive1LpduAi.InFromTheSideStep, Is.EqualTo(InStep));
            Assert.That(TopP6CosmoDive1LpduAi.MainTankDiveSpotTowardWaymarkFour, Is.EqualTo(MainTankDiveSpot));
            Assert.That(TopP6CosmoDive1LpduAi.OffTankDiveSpotTowardWaymarkOne, Is.EqualTo(OffTankDiveSpot));
            Assert.That(TopP6CosmoDive1LpduAi.HalfwayThroughCosmoMemory, Is.EqualTo(1.9f + 6f / 2f).Within(1e-4f));
        });

    // Land Waker (863), the warrior main tank's limit break 3: 80% for 8 s on everyone within 50 y.
    private const ushort LandWaker = 863;

    // A bot presses only while tank mitigation is required; the player is seated away from the main tank.
    [TestCase(true)]
    [TestCase(false)]
    public void TheMainTanksLimitBreakGoesOffHalfwayThroughCosmoMemory(bool tankMitigationRequired)
    {
        var before = new List<bool>();
        var after = new List<bool>();
        var run = ScenarioRun.Execute(typeof(TopP6CosmoDive1Scenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = RegenHealer,
            StopAt = 8f,
            UserActionsEnabled = tankMitigationRequired,
            Probe = p =>
            {
                if (p.Crossed(4.85f)) before.AddRange(PerRole.All.Select(r => p.Member(r)!.HasStatus(LandWaker)));
                if (p.Crossed(4.95f)) after.AddRange(PerRole.All.Select(r => p.Member(r)!.HasStatus(LandWaker)));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(before, Has.Count.EqualTo(8).And.All.False);
        Assert.That(after, Has.Count.EqualTo(8).And.All.EqualTo(tankMitigationRequired));
    }

    internal static Dictionary<PartyRole, Vector2> ExpectedSpots(Spot spot, bool inFirst) => spot switch
    {
        Spot.Autos => PerRole.All.ToDictionary(r => r, AutoSpot),
        Spot.WaymarkTwo => Everyone(WaymarkTwoStack),
        Spot.FirstDodge => Everyone(inFirst ? CentreSquare : Corner),
        Spot.SideStep => Everyone(SideStep),
        Spot.InStep => TheSix.ToDictionary(r => r, _ => InStep),
        _ => PerRole.All.ToDictionary(r => r, DiveSpot),
    };

    private static Dictionary<PartyRole, Vector2> Everyone(Vector2 spot) => PerRole.All.ToDictionary(r => r, _ => spot);

    // Every seat's spot at each time, the player's under the bots' own AI.
    internal static Dictionary<float, Dictionary<PartyRole, Vector2>> Probe(bool inFirst, float[] times, int seed, out ScenarioRun run)
    {
        var seen = new Dictionary<float, Dictionary<PartyRole, Vector2>>();
        run = ScenarioRun.Execute(typeof(TopP6CosmoDive1Scenario), LpduStrat, seed, new ScenarioRunOptions
        {
            StopAt = times.Max() + 0.1f,
            Overrides = o => Pin((TopP6CosmoDive1StateOverrides)o, inFirst),
            Probe = p =>
            {
                foreach (var at in times)
                    if (p.Crossed(at))
                        seen[at] = PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position));
            },
        });
        return seen;
    }

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    internal static void Pin(TopP6CosmoDive1StateOverrides o, bool inFirst) => o.InFirst = inFirst;
}
