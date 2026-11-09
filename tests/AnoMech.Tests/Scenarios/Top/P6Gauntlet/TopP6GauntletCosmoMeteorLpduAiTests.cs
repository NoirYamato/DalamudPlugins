using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// LPDU's spots in the Gauntlet's Cosmo Meteor part on its own (x east, z south). A run starting here puts everyone on
// the spots for the autos before it: the main tank inside the hitbox north (0, -5), the off tank outside it east
// (14.5, 0), the rest inside south (0, 6). Everyone baits under Alpha Omega, then takes the meteor clock at r 13: R1 A,
// R2 1, H2 B, M2 2, OT C, M1 3, H1 D, MT 4; the shield healer goes into the middle after the first eight spreads. On the
// flare markers R1 holds A; the five without a flare stack on R1, or at C when R1 has one; the flares stand 18.5 y out on
// the cardinals the stack leaves, in their clock order. Once the flares have landed everyone goes back to the autos'
// spots for Magic Number.
//
//   1.9  Cosmo Meteor (5 s). 6.9 the baits drop, decide at 10.6. 12.0 / 13.0 and 18.0 / 19.0 the spreads.
//   13.5 the caster's limit break 3 (lands 18.0); 21.9 the flare markers; 24.0 R1's limit break 3 (lands 28.5); 30.0 the
//   flares and the stack; 31.0 back to the autos' spots.
public class TopP6GauntletCosmoMeteorLpduAiTests
{
    internal const int LpduStrat = 0;
    private const float Tolerance = 0.45f;

    // The meteor clock, clockwise from A.
    internal static readonly PartyRole[] Clockwise = [PhysRangedDps, CasterDps, ShieldHealer, MeleeDpsB, OffTank, MeleeDpsA, RegenHealer, MainTank];

    internal static readonly Dictionary<PartyRole, Vector2> Clock = new()
    {
        [PhysRangedDps] = new(0f, -13f), [CasterDps] = new(9.19f, -9.19f), [ShieldHealer] = new(13f, 0f),
        [MeleeDpsB] = new(9.19f, 9.19f), [OffTank] = new(0f, 13f), [MeleeDpsA] = new(-9.19f, 9.19f),
        [RegenHealer] = new(-13f, 0f), [MainTank] = new(-9.19f, -9.19f),
    };

    internal static readonly Dictionary<PartyRole, Vector2> AutoSpots = PerRole.All.ToDictionary(r => r, r => r switch
    {
        MainTank => new Vector2(0f, -5f),
        OffTank => new Vector2(14.5f, 0f),
        _ => new Vector2(0f, 6f),
    });

    internal static readonly Vector2 NorthStack = new(0f, -13f);
    internal static readonly Vector2 SouthStack = new(0f, 13f);

    internal static readonly Dictionary<string, Vector2> Cardinals = new()
    {
        ["N"] = new(0f, -18.5f), ["E"] = new(18.5f, 0f), ["S"] = new(0f, 18.5f), ["W"] = new(-18.5f, 0f),
    };

    // Every three of the eight, in clock order within each.
    internal static readonly PartyRole[][] FlareCombinations =
        (from a in Enumerable.Range(0, 8)
         from b in Enumerable.Range(a + 1, 7 - a)
         from c in Enumerable.Range(b + 1, 7 - b)
         select new[] { Clockwise[a], Clockwise[b], Clockwise[c] }).ToArray();

    private static IEnumerable<TestCaseData> EveryFlareCombination()
        => FlareCombinations.Select(flares => new TestCaseData((object)flares).SetName($"{{m}}({Describe(flares)})"));

    [Test]
    public void ThereAreFiftySixFlareCombinations()
        => Assert.That(FlareCombinations.Select(Describe).Distinct().Count(), Is.EqualTo(56));

    [Test]
    public void TheClockSpotsAreTheRaidplansMeteorSpread()
    {
        Assert.Multiple(() =>
        {
            foreach (var (role, spot) in Clock)
                Assert.That(Vector2.Distance(TopP6GauntletLpduAi.ClockSpot(role), spot), Is.LessThan(0.01f), $"{role}");
        });
    }

    // Written from the rule rather than the strat's code: of every way to give the flares the cardinals the stack
    // leaves, keep the one that keeps their clockwise order around the arena, R1 holding north when it has a flare and,
    // otherwise, the first flare after north taking the first free cardinal after north.
    [TestCaseSource(nameof(EveryFlareCombination))]
    public void TheFlaresKeepTheirClockOrderOnTheCardinalsTheStackLeaves(PartyRole[] flares)
    {
        var expected = ExpectedFlareCardinals(flares);
        var actual = TopP6GauntletLpduAi.FlareCardinalsKeepingTheirClockOrderFromNorth(flares);

        Assert.That(actual, Is.EquivalentTo(expected), Describe(flares));
    }

    // The three cases LPDU's guides walk through, the last with R1 holding a flare and the stack south.
    [TestCase(new[] { CasterDps, MeleeDpsB, MainTank }, new[] { "E", "S", "W" })]
    [TestCase(new[] { MeleeDpsB, RegenHealer, MainTank }, new[] { "E", "S", "W" })]
    [TestCase(new[] { PhysRangedDps, RegenHealer, MeleeDpsB }, new[] { "N", "W", "E" })]
    public void TheGuidesExamples(PartyRole[] flares, string[] cardinals)
    {
        var spots = TopP6GauntletLpduAi.FlareCardinalsKeepingTheirClockOrderFromNorth(flares);

        Assert.That(flares.Select(f => CardinalName(spots[f])), Is.EqualTo(cardinals));
    }

    [Test]
    public void TheStackGoesNorthOnThePhysRangedUnlessItHasAFlare()
    {
        Assert.Multiple(() =>
        {
            foreach (var flares in FlareCombinations)
            {
                var stack = flares.Contains(PhysRangedDps) ? SouthStack : NorthStack;
                foreach (var role in PerRole.All.Where(r => r != PhysRangedDps && !flares.Contains(r)))
                    Assert.That(Vector2.Distance(TopP6GauntletLpduAi.FinalSpot(role, flares), stack), Is.LessThan(0.01f), $"{role}, {Describe(flares)}");
                Assert.That(Vector2.Distance(TopP6GauntletLpduAi.FinalSpot(PhysRangedDps, flares), Clock[PhysRangedDps]), Is.LessThan(0.01f),
                            $"R1 holds A, {Describe(flares)}");
            }
        });
    }

    public enum Stage { Autos, UnderAlphaOmega, MeteorClock, ShieldHealerInTheMiddle, BeforeTheMarkers }

    [TestCase(2.55f, Stage.Autos)]
    [TestCase(6.85f, Stage.UnderAlphaOmega, Description = "the cast ends")]
    [TestCase(10.55f, Stage.MeteorClock, Description = "the baits decide")]
    [TestCase(12.95f, Stage.MeteorClock, Description = "the second wave of set 1")]
    [TestCase(17.95f, Stage.ShieldHealerInTheMiddle, Description = "set 2")]
    [TestCase(21.85f, Stage.BeforeTheMarkers)]
    public void StandsOnTheStratsSpots(float at, Stage stage)
    {
        var flares = new[] { CasterDps, MeleeDpsB, MainTank };
        var expected = PerRole.All.ToDictionary(r => r, r => stage switch
        {
            Stage.Autos => AutoSpots[r],
            Stage.UnderAlphaOmega => Vector2.Zero,
            Stage.MeteorClock => Clock[r],
            _ => r == ShieldHealer ? Vector2.Zero : Clock[r],
        });
        var seen = Probe(flares, OffTank, [at], out var run, player: MeleeDpsA);

        Assert.That(run.Passed, run.ToString);
        AssertSpots(seen[at], expected, $"{stage} at {at}s (seed {run.Seed})");
    }

    // On the meteor clock just before the flare markers (21.9), then on the flare and stack spots as they land.
    [TestCaseSource(nameof(EveryFlareCombination))]
    public void TakesTheFlareAndStackSpotsForEveryCombination(PartyRole[] flares)
    {
        var stack = PerRole.All.First(r => !flares.Contains(r));
        var seen = Probe(flares, stack, [21.85f, 29.9f], out var run);

        Assert.That(run.Passed, run.ToString);
        AssertSpots(seen[21.85f], PerRole.All.ToDictionary(r => r, r => r == ShieldHealer ? Vector2.Zero : Clock[r]),
                    $"before the markers, {Describe(flares)} (seed {run.Seed})");
        AssertSpots(seen[29.9f], ExpectedFinalSpots(flares), $"as the flares land, {Describe(flares)} (seed {run.Seed})");
    }

    // The flares are what the players see at 21.9; before then nobody moves differently for them. On one seed, every seat
    // stands where it does under the other rolls, frame by frame, whoever holds the flares and the stack.
    [Test]
    public void NothingDependsOnTheFlaresBeforeTheMarkers()
    {
        const float Markers = 21.9f;
        var seed = Random.Shared.Next();
        (PartyRole[] Flares, PartyRole Stack)[] rolls =
        [
            ([CasterDps, MeleeDpsB, MainTank], ShieldHealer),
            ([PhysRangedDps, RegenHealer, MeleeDpsB], MainTank),
            ([ShieldHealer, OffTank, MeleeDpsA], PhysRangedDps),
        ];
        var runs = rolls.Select(roll =>
        {
            var seen = new List<Vector2>();
            var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = MainTank,
                StopAt = Markers,
                Overrides = o => Pin((TopP6GauntletStateOverrides)o, roll.Flares, roll.Stack),
                Probe = p =>
                {
                    if (p.Time < Markers) seen.AddRange(PerRole.All.Select(r => Flat(p.Member(r)!.Position)));
                },
            });
            Assert.That(run.Passed, run.ToString);
            return seen;
        }).ToList();

        Assert.That(runs[0], Is.Not.Empty);
        Assert.That(runs[1], Is.EqualTo(runs[0]).Using<Vector2>(Near), $"seed {seed}: {Describe(rolls[1].Flares)} against {Describe(rolls[0].Flares)}");
        Assert.That(runs[2], Is.EqualTo(runs[0]).Using<Vector2>(Near), $"seed {seed}: {Describe(rolls[2].Flares)} against {Describe(rolls[0].Flares)}");
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.01f;

    // The caster's limit break 3 starts on arriving at the meteor clock (13.5) and the physical ranged's once the
    // caster's has refilled the gauge (24.0), each cast for 4.5 s; R1 turns to face Alpha Omega first, so its line runs
    // from A through both meteors. Each side's adds die as its limit break lands, whatever "Require tank mitigation" says.
    [TestCase(true)]
    [TestCase(false)]
    public void TheCasterAndThePhysRangedFireTheirLimitBreaksAsTheAddsDie(bool tankMitigationRequired)
    {
        var fired = new List<(float At, PartyRole Role, float Rotation)>();
        var casts = new List<(float At, PartyRole Role)>();
        var alive = new Dictionary<float, (int Meteors, int Comets)>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            UserActionsEnabled = tankMitigationRequired,
            Configure = c => c.EnableTankMitigation = tankMitigationRequired,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, [CasterDps, MeleeDpsB, MainTank], OffTank),
            Probe = p =>
            {
                foreach (var role in new[] { CasterDps, PhysRangedDps })
                {
                    var limitBreak = LimitBreakHandler.ActionId(PartyPresets.Standard[(int)role].ClassJob, 3).ToString();
                    var actor = p.Member(role)!.EntityId.ToString("X8");
                    if (p.Fake.Timeline.Rows.Any(r => r["kind"] == "cast" && r["id"] == limitBreak && r["actor"] == actor)
                        && casts.All(c => c.Role != role))
                        casts.Add((p.Time, role));
                    if (p.Fake.Timeline.Rows.Any(r => r["kind"] == "ability" && r["id"] == limitBreak && r["actor"] == actor)
                        && fired.All(f => f.Role != role))
                        fired.Add((p.Time, role, p.Member(role)!.Rotation));
                }
                foreach (var at in new[] { 17.9f, 18.1f, 28.4f, 28.6f })
                    if (p.Crossed(at))
                    {
                        var adds = p.World.Children.OfType<SimEnemy>().Where(e => e.IsAlive()).ToList();
                        alive[at] = (adds.Count(a => a.BNpcBaseId == P6BNpcBaseId.CosmoMeteor), adds.Count(a => a.BNpcBaseId == P6BNpcBaseId.CosmoComet));
                    }
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(casts.Select(c => c.Role), Is.EqualTo(new[] { CasterDps, PhysRangedDps }));
        Assert.That(casts.Select(c => c.At), Is.EqualTo(new[] { 13.5f, 24.0f }).Within(0.05f), "the casts start");
        Assert.That(fired.Select(f => f.Role), Is.EqualTo(new[] { CasterDps, PhysRangedDps }));
        Assert.That(fired.Select(f => f.At), Is.EqualTo(new[] { 18.0f, 28.5f }).Within(0.05f), "they land");
        Assert.That(fired[1].Rotation, Is.EqualTo(0f).Within(0.05f), "R1 faces south, through Alpha Omega and both meteors");
        Assert.That(alive[17.9f], Is.EqualTo((2, 6)), "before the caster's lands");
        Assert.That(alive[18.1f], Is.EqualTo((2, 0)), "the comets die as it lands");
        Assert.That(alive[28.4f], Is.EqualTo((2, 0)), "before the physical ranged's lands");
        Assert.That(alive[28.6f], Is.EqualTo((0, 0)), "the meteors die as it lands");
    }

    internal static Dictionary<PartyRole, Vector2> ExpectedFinalSpots(PartyRole[] flares)
    {
        var stack = flares.Contains(PhysRangedDps) ? SouthStack : NorthStack;
        var cardinals = ExpectedFlareCardinals(flares);
        return PerRole.All.ToDictionary(r => r, r =>
            r == PhysRangedDps ? Clock[PhysRangedDps]
            : flares.Contains(r) ? Cardinals[CardinalName(cardinals[r])]
            : stack);
    }

    internal static Dictionary<PartyRole, float> ExpectedFlareCardinals(PartyRole[] flares)
    {
        var physRangedFlare = flares.Contains(PhysRangedDps);
        float[] free = physRangedFlare ? [0f, 90f, 270f] : [90f, 180f, 270f];
        var byClock = flares.OrderBy(f => Array.IndexOf(Clockwise, f)).ToArray();
        var keeping = Permutations(free)
            .Where(cardinals => IsARotationOf(cardinals, free))
            .Where(cardinals => physRangedFlare ? cardinals[0] == 0f : cardinals[0] == free[0])
            .ToList();
        Assert.That(keeping, Has.Count.EqualTo(1), $"one way keeps the clock order for {Describe(flares)}");
        return byClock.Zip(keeping[0]).ToDictionary(t => t.First, t => t.Second);
    }

    private static IEnumerable<float[]> Permutations(float[] values)
        => values.Length <= 1 ? [values]
            : values.SelectMany((v, i) => Permutations([.. values[..i], .. values[(i + 1)..]]).Select(rest => (float[])[v, .. rest]));

    // In clock order the flares' cardinals run on clockwise, starting anywhere.
    private static bool IsARotationOf(float[] cardinals, float[] sorted)
        => Enumerable.Range(0, sorted.Length).Any(start => cardinals.Select((_, i) => sorted[(start + i) % sorted.Length]).SequenceEqual(cardinals));

    internal static string CardinalName(float bearing) => bearing switch { 0f => "N", 90f => "E", 180f => "S", _ => "W" };

    internal static string Describe(IEnumerable<PartyRole> roles) => string.Join("+", roles);

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    internal static void Pin(TopP6GauntletStateOverrides o, PartyRole[] flares, PartyRole stack, PartyRole[][]? firstSpreads = null)
    {
        TopP6GauntletRun.Only(o, TopP6GauntletPart.CosmoMeteor);
        o.Flares = flares;
        o.StackTarget = stack;
        o.FirstSpreads = firstSpreads;
    }

    private static void AssertSpots(Dictionary<PartyRole, Vector2> seen, Dictionary<PartyRole, Vector2> expected, string why)
    {
        Assert.Multiple(() =>
        {
            foreach (var (role, spot) in expected)
                Assert.That(Vector2.Distance(seen[role], spot), Is.LessThan(Tolerance), $"{role} at {seen[role]}, expected {spot}: {why}");
        });
    }

    private static Dictionary<float, Dictionary<PartyRole, Vector2>> Probe(PartyRole[] flares, PartyRole stack, float[] times, out ScenarioRun run,
        PartyRole? player = null)
    {
        var seen = new Dictionary<float, Dictionary<PartyRole, Vector2>>();
        run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = times.Max() + 0.1f,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, flares, stack),
            Probe = p =>
            {
                foreach (var at in times)
                    if (p.Crossed(at))
                        seen[at] = PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position));
            },
        });
        return seen;
    }
}
