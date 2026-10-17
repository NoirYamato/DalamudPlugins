using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// Kits pinned to MT+OT defamation (blue rot), H1+H2 stack (red rot), M1+M2 remote first and R1+R2
// local first. Spots are written as degrees clockwise from the set's layout and yalms from the
// centre, as the slideshow gives them.
//
// From the loose spread, with the defamation towers at SW: MT (north) is nearer W and takes the
// right-hand spots, OT the left; H2 (east) takes the left stack spot, H1 the right; M2 (SE) the
// left remote spot, M1 the right; R1 (NE) the left local spot, R2 the right.
public class TopP3HelloWorldLpduAiTests
{
    private const int LpduStrat = 0;
    private const float Tolerance = 0.75f;

    private static readonly PartyRole[] KitOrder =
        [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static readonly Dictionary<PartyRole, float> LooseSpreadBearings = new()
    {
        [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
    };

    [TestCase(7.9f)]
    [TestCase(19.9f)]
    public void HoldsTheLooseSpreadUntilTheTowers(float at)
        => AssertSpots(Direction.SW, at, LooseSpreadBearings.ToDictionary(kv => kv.Key, kv => OnLayout(Direction.N, kv.Value, 8.5f)));

    // The soakers are still walking; the tethered pairs wait where they stood, but for R2, which MT's
    // straight walk from N to its W tower passes.
    [Test]
    public void TetheredPairsWaitForTheSoakersInSetOne()
        => AssertSpots(Direction.SW, at: 20.6f, new[] { MeleeDpsA, MeleeDpsB, PhysRangedDps }
            .ToDictionary(role => role, role => OnLayout(Direction.N, LooseSpreadBearings[role], 8.5f)));

    // MT, the player's seat walked as a human would, goes straight from N to its W tower over R2's
    // NW spot while it holds the blue rot; R2 steps aside out of reach and comes back.
    [Test]
    public void ABotInTheHumansWayStepsAside()
    {
        var closest = float.MaxValue;
        var aside = 0f;
        var home = OnLayout(Direction.N, LooseSpreadBearings[CasterDps], 8.5f);
        var caught = false;
        var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 24f,
            Overrides = o => Pin((TopP3HelloWorldStateOverrides)o, Direction.SW),
            Probe = p =>
            {
                if (p.Time < 20.1f) return;
                var mt = Flat(p.Member(MainTank)!.Position);
                var r2 = Flat(p.Member(CasterDps)!.Position);
                closest = Math.Min(closest, Vector2.Distance(mt, r2));
                aside = Math.Max(aside, Vector2.Distance(r2, home));
                caught |= p.Member(CasterDps)!.ActiveStatuses.Any(s => s.StatusId is Scenarios.Top.TopP3Constants.P3StatusId.BlueRot);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(aside, Is.GreaterThan(2f), $"R2 should have stepped off MT's line (seed {run.Seed})");
            Assert.That(closest, Is.GreaterThan(2f), $"closest MT came to R2 (seed {run.Seed})");
            Assert.That(caught, Is.False, "R2 caught MT's blue rot");
        });
    }

    [Test]
    public void TakesTheTowersInSetOne()
        => AssertPolarSpots(Direction.SW, at: 27.9f, new()
        {
            [OffTank] = (-45, 18.1f), [MainTank] = (45, 18.1f),
            [ShieldHealer] = (-154, 12f), [RegenHealer] = (154, 12f),
            [MeleeDpsB] = (-166, 11.4f), [MeleeDpsA] = (166, 11.4f),
            [PhysRangedDps] = (-74, 14.2f), [CasterDps] = (74, 14.2f),
        });

    [Test]
    public void PassesTheRotsInSetOne()
        => AssertPolarSpots(Direction.SW, at: 30.6f, new()
        {
            [OffTank] = (-45, 13.7f), [MainTank] = (45, 13.7f),
            [PhysRangedDps] = (-45, 13.7f), [CasterDps] = (45, 13.7f),
            [ShieldHealer] = (-154, 12f), [RegenHealer] = (154, 12f),
            [MeleeDpsB] = (-154, 12f), [MeleeDpsA] = (154, 12f),
        });

    [Test]
    public void PopsTheRemoteTetherInSetOne()
        => AssertPolarSpots(Direction.SW, at: 34f, new()
        {
            [OffTank] = (-45, 13.7f), [MainTank] = (45, 13.7f),
            [MeleeDpsB] = (-141, 17f), [MeleeDpsA] = (149, 11.6f),
            [ShieldHealer] = (-120, 10f), [RegenHealer] = (112, 10f),
            [PhysRangedDps] = (-42, 7.7f), [CasterDps] = (42, 7.7f),
        });

    [Test]
    public void PopsTheLocalTetherClearOfTheRotsInSetOne()
        => AssertPolarSpots(Direction.SW, at: 35.6f, new()
        {
            [OffTank] = (-45, 13.7f), [MainTank] = (45, 13.7f),
            [ShieldHealer] = (-120, 10f), [RegenHealer] = (112, 10f),
            [MeleeDpsB] = (-158, 12f), [MeleeDpsA] = (165, 12f),
            [PhysRangedDps] = (-22, 7.4f), [CasterDps] = (22, 7.4f),
        });

    // Nobody moves between the last rots going off and the next towers.
    [Test]
    public void HoldsItsSpotsUntilTheNextTowers()
        => AssertPolarSpots(Direction.SW, at: 41f, new()
        {
            [OffTank] = (-45, 13.7f), [MainTank] = (45, 13.7f),
            [ShieldHealer] = (-120, 10f), [RegenHealer] = (112, 10f),
            [MeleeDpsB] = (-158, 12f), [MeleeDpsA] = (165, 12f),
            [PhysRangedDps] = (-22, 7.4f), [CasterDps] = (22, 7.4f),
        });

    // Towers at NE instead: MT (north) is now nearer N, H1 (west) nearer the left stack spot, M1
    // (SW) nearer the left remote spot, and R2 (NW) nearer the left local spot.
    [Test]
    public void EachPairTakesTheCloserSplit()
        => AssertPolarSpots(Direction.NE, at: 27.9f, new()
        {
            [MainTank] = (-45, 18.1f), [OffTank] = (45, 18.1f),
            [RegenHealer] = (-154, 12f), [ShieldHealer] = (154, 12f),
            [MeleeDpsA] = (-166, 11.4f), [MeleeDpsB] = (166, 11.4f),
            [CasterDps] = (-74, 14.2f), [PhysRangedDps] = (74, 14.2f),
        });

    // Set 4 roles: M1+M2 defamation, R1+R2 stack, H1+H2 remote, MT+OT local, which stands on the
    // remote spot so its tether pops the moment it goes live.
    [Test]
    public void TakesTheTowersInTheLastSet()
        => AssertPolarSpots(Direction.SW, at: 91.3f, new()
        {
            [MeleeDpsB] = (-45, 18.1f), [MeleeDpsA] = (45, 18.1f),
            [PhysRangedDps] = (-154, 12f), [CasterDps] = (154, 12f),
            [ShieldHealer] = (-166, 11.4f), [RegenHealer] = (166, 11.4f),
            [OffTank] = (-165, 10.5f), [MainTank] = (165, 11.2f),
        });

    [Test]
    public void SpreadsTheLastRotsWithoutPassingThem()
        => AssertPolarSpots(Direction.SW, at: 93.8f, new()
        {
            [MeleeDpsB] = (-45, 13.7f), [MeleeDpsA] = (45, 13.7f),
            [PhysRangedDps] = (-137, 8.4f), [CasterDps] = (127, 9.8f),
            [ShieldHealer] = (-166, 11.4f), [RegenHealer] = (166, 11.4f),
            [OffTank] = (-165, 10.5f), [MainTank] = (165, 11.2f),
        });

    [Test]
    public void PopsTheLastRemoteTether()
        => AssertPolarSpots(Direction.SW, at: 97.4f, new()
        {
            [ShieldHealer] = (-141, 16f), [RegenHealer] = (146, 13.1f),
        });

    [Test]
    public void WaitsForTheLastRotsToGoOff()
        => AssertPolarSpots(Direction.SW, at: 99.2f, new()
        {
            [MeleeDpsB] = (-45, 13.7f), [MeleeDpsA] = (45, 13.7f),
            [PhysRangedDps] = (-126, 14.2f), [CasterDps] = (113, 14.3f),
            [ShieldHealer] = (-154, 14.6f), [RegenHealer] = (153, 11.9f),
            [OffTank] = (-167, 12.9f), [MainTank] = (175, 12.3f),
        });

    [Test]
    public void LinesUpWestForMonitors()
    {
        PartyRole[] northToSouth = [RegenHealer, ShieldHealer, MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];
        AssertSpots(Direction.SW, at: 104.5f,
            northToSouth.Select((role, i) => (role, i)).ToDictionary(t => t.role, t => OnLayout(Direction.N, 320f - 15.1f * t.i, 13.15f)));
    }

    private static void AssertPolarSpots(Direction layout, float at, Dictionary<PartyRole, (float Degrees, float Radius)> expected)
        => AssertSpots(layout, at, expected.ToDictionary(kv => kv.Key, kv => OnLayout(layout, kv.Value.Degrees, kv.Value.Radius)));

    private static void AssertSpots(Direction layout, float at, Dictionary<PartyRole, Vector2> expected)
    {
        var seen = new Dictionary<PartyRole, Vector2>();
        var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = at + 0.1f,
            Overrides = o => Pin((TopP3HelloWorldStateOverrides)o, layout),
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                foreach (var role in PerRole.All)
                    if (p.Member(role) is { } member)
                        seen[role] = new(member.Position.X, member.Position.Z);
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

    private static Vector2 OnLayout(Direction layout, float degrees, float radius)
    {
        var bearing = layout.RadiansFromNorth + degrees * MathF.PI / 180f;
        return new(radius * MathF.Sin(bearing), -radius * MathF.Cos(bearing));
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    private static void Pin(TopP3HelloWorldStateOverrides o, Direction layout)
    {
        o.KitOrder = KitOrder;
        o.DefamationColour = RotColour.Blue;
        o.Layouts = [layout, layout, layout, layout];
    }
}
