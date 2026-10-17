using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// Kits pinned to MT+OT defamation (blue rot), H1+H2 stack (red rot), M1+M2 remote first and R1+R2
// local first, so every pair already stands on its own spread spot's axis when it re-spreads by
// debuff at 5s: MT N, OT S, H1 W, H2 E, the rest under the boss. Spots are written as degrees
// clockwise from the set's layout and yalms from the centre, as the slideshow gives them.
//
// With the defamation towers at SW, MT (north) is nearer W and takes the right-hand spots, OT the
// left; H2 (east) takes the left stack spot, H1 the right. The pairs under the boss stand on the
// same point, so the higher priority takes the left: M1 and R1.
public class TopP3HelloWorldLpduAiTests
{
    private const int LpduStrat = 0;
    private const float Tolerance = 0.75f;

    private static readonly PartyRole[] KitOrder =
        [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    [Test]
    public void SplitsIntoTheCardinalCrossByDebuff()
        => AssertSpots(Direction.SW, at: 7.9f, new()
        {
            [MainTank] = new(0, -10), [OffTank] = new(0, 10), [RegenHealer] = new(-10, 0), [ShieldHealer] = new(10, 0),
            [MeleeDpsA] = Vector2.Zero, [MeleeDpsB] = Vector2.Zero, [PhysRangedDps] = Vector2.Zero, [CasterDps] = Vector2.Zero,
        });

    [Test]
    public void TakesTheTowersInSetOne()
        => AssertPolarSpots(Direction.SW, at: 27.9f, new()
        {
            [OffTank] = (-45, 18.1f), [MainTank] = (45, 18.1f),
            [ShieldHealer] = (-154, 12f), [RegenHealer] = (154, 12f),
            [MeleeDpsA] = (-166, 11.4f), [MeleeDpsB] = (166, 11.4f),
            [PhysRangedDps] = (-74, 14.2f), [CasterDps] = (74, 14.2f),
        });

    [Test]
    public void PassesTheRotsInSetOne()
        => AssertPolarSpots(Direction.SW, at: 30.6f, new()
        {
            [OffTank] = (-45, 13.7f), [MainTank] = (45, 13.7f),
            [PhysRangedDps] = (-45, 13.7f), [CasterDps] = (45, 13.7f),
            [ShieldHealer] = (-154, 12f), [RegenHealer] = (154, 12f),
            [MeleeDpsA] = (-154, 12f), [MeleeDpsB] = (154, 12f),
        });

    [Test]
    public void PopsTheRemoteTetherInSetOne()
        => AssertPolarSpots(Direction.SW, at: 34f, new()
        {
            [OffTank] = (-45, 13.7f), [MainTank] = (45, 13.7f),
            [MeleeDpsA] = (-141, 17f), [MeleeDpsB] = (149, 11.6f),
            [ShieldHealer] = (-120, 10f), [RegenHealer] = (112, 10f),
            [PhysRangedDps] = (-42, 7.7f), [CasterDps] = (42, 7.7f),
        });

    [Test]
    public void PopsTheLocalTetherClearOfTheRotsInSetOne()
        => AssertPolarSpots(Direction.SW, at: 35.6f, new()
        {
            [OffTank] = (-45, 13.7f), [MainTank] = (45, 13.7f),
            [ShieldHealer] = (-120, 10f), [RegenHealer] = (112, 10f),
            [MeleeDpsA] = (-158, 12f), [MeleeDpsB] = (165, 12f),
            [PhysRangedDps] = (-22, 7.4f), [CasterDps] = (22, 7.4f),
        });

    // The next defamation pair (R1, R2) takes one cardinal axis and the next stack pair (M1, M2) the
    // other; the two whose rots just went off wait under the boss.
    [Test]
    public void ReformsTheCardinalCrossBetweenSets()
        => AssertPolarSpots(Direction.SW, at: 40.9f, new()
        {
            [PhysRangedDps] = (-45, 10f), [CasterDps] = (135, 10f),
            [MeleeDpsA] = (-135, 10f), [MeleeDpsB] = (45, 10f),
            [MainTank] = (0, 0f), [OffTank] = (0, 0f), [RegenHealer] = (0, 0f), [ShieldHealer] = (0, 0f),
        });

    // Towers at NE instead: MT (north) is now nearer N and OT nearer E, and H1 (west) nearer the
    // left stack spot, so both pairs swap sides.
    [Test]
    public void EachPairTakesTheCloserSplit()
        => AssertPolarSpots(Direction.NE, at: 27.9f, new()
        {
            [MainTank] = (-45, 18.1f), [OffTank] = (45, 18.1f),
            [RegenHealer] = (-154, 12f), [ShieldHealer] = (154, 12f),
            [MeleeDpsA] = (-166, 11.4f), [MeleeDpsB] = (166, 11.4f),
            [PhysRangedDps] = (-74, 14.2f), [CasterDps] = (74, 14.2f),
        });

    // Set 4 roles: M1+M2 defamation, R1+R2 stack, H1+H2 remote, MT+OT local, which stands on the
    // remote spot so its tether pops the moment it goes live.
    [Test]
    public void TakesTheTowersInTheLastSet()
        => AssertPolarSpots(Direction.SW, at: 91.3f, new()
        {
            [MeleeDpsA] = (-45, 18.1f), [MeleeDpsB] = (45, 18.1f),
            [PhysRangedDps] = (-154, 12f), [CasterDps] = (154, 12f),
            [RegenHealer] = (-166, 11.4f), [ShieldHealer] = (166, 11.4f),
            [MainTank] = (-165, 10.5f), [OffTank] = (165, 11.2f),
        });

    [Test]
    public void SpreadsTheLastRotsWithoutPassingThem()
        => AssertPolarSpots(Direction.SW, at: 93.8f, new()
        {
            [MeleeDpsA] = (-45, 13.7f), [MeleeDpsB] = (45, 13.7f),
            [PhysRangedDps] = (-137, 8.4f), [CasterDps] = (127, 9.8f),
            [RegenHealer] = (-166, 11.4f), [ShieldHealer] = (166, 11.4f),
            [MainTank] = (-165, 10.5f), [OffTank] = (165, 11.2f),
        });

    [Test]
    public void PopsTheLastRemoteTether()
        => AssertPolarSpots(Direction.SW, at: 97.4f, new()
        {
            [RegenHealer] = (-141, 16f), [ShieldHealer] = (146, 13.1f),
        });

    [Test]
    public void WaitsForTheLastRotsToGoOff()
        => AssertPolarSpots(Direction.SW, at: 99.2f, new()
        {
            [MeleeDpsA] = (-45, 13.7f), [MeleeDpsB] = (45, 13.7f),
            [PhysRangedDps] = (-126, 14.2f), [CasterDps] = (113, 14.3f),
            [RegenHealer] = (-154, 14.6f), [ShieldHealer] = (153, 11.9f),
            [MainTank] = (-167, 12.9f), [OffTank] = (175, 12.3f),
        });

    [Test]
    public void LinesUpWestForMonitors()
    {
        PartyRole[] northToSouth = [RegenHealer, ShieldHealer, MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];
        AssertPolarSpots(Direction.N, at: 104.5f,
            northToSouth.Select((role, i) => (role, i)).ToDictionary(t => t.role, t => (320f - 15.1f * t.i, 13.15f)));
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

    private static void Pin(TopP3HelloWorldStateOverrides o, Direction layout)
    {
        o.KitOrder = KitOrder;
        o.DefamationColour = RotColour.Blue;
        o.Layouts = [layout, layout, layout, layout];
    }
}
