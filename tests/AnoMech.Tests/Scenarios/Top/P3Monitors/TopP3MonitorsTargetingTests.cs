using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Monitors;
using static AnoMech.Core.Game.Party.PartyRole;
using P5Side = AnoMech.Scenarios.Top.P5Delta.Side;

namespace AnoMech.Tests;

// Who a monitor's beam takes: two players at random from the half-plane on its side of the line its
// owner faces along, never the owner, and random others when fewer than two stand there. The runs
// pin the slideshow's example (TopP3MonitorsScenarioTests), where every beam holds exactly its two
// LPDU targets, then crowd or empty one beam just before the hits.
public class TopP3MonitorsTargetingTests
{
    private const float FacingSouth = 0f;
    private const float FacingEast = MathF.PI / 2;
    private const float FacingNorth = MathF.PI;

    private static readonly IReadOnlyList<int> Seeds = Enumerable.Range(0, 30).ToList();

    private static readonly PartyRole[] SlideshowMonitors = [OffTank, MeleeDpsA, PhysRangedDps];
    private static readonly PartyRole[] OmegasLpduTargets = [MeleeDpsA, PhysRangedDps];
    private static readonly PartyRole[] OffTanksLpduTargets = [MainTank, ShieldHealer];
    private static readonly PartyRole[] MeleeDpsAsLpduTargets = [OffTank, RegenHealer];
    private static readonly PartyRole[] PhysRangedDpsLpduTargets = [MeleeDpsB, CasterDps];

    private sealed record Spot(Vector3 Position, float Rotation = 0f) : IPositioned
    {
        public Spot(float x, float z) : this(new Vector3(x, 0, z)) { }
    }

    // Facing north the right hand is east; facing south it is west.
    [TestCase(FacingNorth, MonitorSide.Right, 5f, 0f)]
    [TestCase(FacingNorth, MonitorSide.Left, -5f, 0f)]
    [TestCase(FacingSouth, MonitorSide.Right, -5f, 0f)]
    [TestCase(FacingSouth, MonitorSide.Left, 5f, 0f)]
    [TestCase(FacingEast, MonitorSide.Right, 0f, 5f)]
    [TestCase(FacingEast, MonitorSide.Left, 0f, -5f)]
    public void BeamsToTheOwnersHand(float facing, MonitorSide side, float x, float z)
    {
        var hand = new Spot(x, z);
        var find = new CharacterFind<Spot>(new List<Spot> { new(-x, -z), hand });

        Assert.That(find.OnSideN(new Rng(1), new Placement(Vector3.Zero, facing), side.Mul(), count: 1), Is.EqualTo(new[] { hand }));
    }

    [Test]
    public void SidesAreP5DeltasSides()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MonitorSide.Left.Mul(), Is.EqualTo(P5Side.Left.Mul));
            Assert.That(MonitorSide.Right.Mul(), Is.EqualTo(P5Side.Right.Mul));
            Assert.That(MonitorSide.Left.StatusId(), Is.EqualTo(P5Side.Left.MonitorDebuffId));
            Assert.That(MonitorSide.Right.StatusId(), Is.EqualTo(P5Side.Right.MonitorDebuffId));
        });
    }

    // RegenHealer strays from N1 across Omega's line, so Omega's beam holds three. It takes two of
    // them at random, and RegenHealer, MeleeDpsA's target as well, dies whenever it is one of them.
    [Test]
    public void ACrowdedBeamTakesTwoOfItsPlayersAtRandom()
    {
        var omegasPairs = Seeds.Select(seed =>
        {
            var (pairs, run) = Fire(seed, RegenHealer, new Takeover(10.5f, new Vector2(-3, -17)));
            var omegas = PairLeftOver(pairs, OffTanksLpduTargets, MeleeDpsAsLpduTargets, PhysRangedDpsLpduTargets);
            Assert.That(omegas, Is.SubsetOf(new[] { MeleeDpsA, PhysRangedDps, RegenHealer }), $"seed {seed}");
            Assert.That(run.Deaths.Select(d => d.Role),
                        Is.EqualTo(omegas.Contains(RegenHealer) ? new[] { RegenHealer } : []), $"seed {seed}");
            return Describe(omegas);
        }).ToList();

        Assert.That(omegasPairs.Distinct().Count(), Is.EqualTo(3), string.Join("; ", omegasPairs));
    }

    // ShieldHealer steps out of OffTank's beam to a spot no beam covers, leaving MainTank alone in it.
    [Test]
    public void AShortBeamFillsUpWithOthersAtRandomButNeverItsOwner()
    {
        var seconds = Seeds.Select(seed =>
        {
            var (pairs, _) = Fire(seed, ShieldHealer, new Takeover(10.5f, new Vector2(6, 0)));
            var offTanks = PairLeftOver(pairs, OmegasLpduTargets, MeleeDpsAsLpduTargets, PhysRangedDpsLpduTargets);
            Assert.That(offTanks, Does.Contain(MainTank), $"seed {seed}");
            Assert.That(offTanks, Does.Not.Contain(OffTank), $"seed {seed}");
            return offTanks.Single(role => role != MainTank);
        }).ToList();

        Assert.That(seconds.Distinct().Count(), Is.GreaterThan(2), string.Join(", ", seconds));
    }

    // ShieldHealer and MainTank both step out of OffTank's beam to spots no beam covers.
    [Test]
    public void AnEmptyBeamTakesTwoOthersAtRandomButNeverItsOwner()
    {
        var offTanksPairs = Seeds.Select(seed =>
        {
            var (pairs, _) = Fire(seed, ShieldHealer, new Takeover(10.5f, new Vector2(6, 0)),
                                  new Takeover(10.5f, new Vector2(6, -3), Bot: MainTank));
            var offTanks = PairLeftOver(pairs, OmegasLpduTargets, MeleeDpsAsLpduTargets, PhysRangedDpsLpduTargets);
            Assert.That(offTanks, Is.Unique, $"seed {seed}");
            Assert.That(offTanks, Does.Not.Contain(OffTank), $"seed {seed}");
            return Describe(offTanks);
        }).ToList();

        Assert.That(offTanksPairs.Distinct().Count(), Is.GreaterThan(2), string.Join("; ", offTanksPairs));
    }

    // Each monitor's two targets, Omega's first, each pair in role order.
    private static (List<PartyRole[]> Pairs, ScenarioRun Run) Fire(int seed, PartyRole player, params Takeover[] takeovers)
    {
        var hits = new List<PartyRole>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = player,
            WriteArtifactsOnFailure = false,
            Takeovers = takeovers,
            Overrides = o => TopP3MonitorsLpduAiTests.Pin((TopP3MonitorsStateOverrides)o, OmegaMonitor.West, SlideshowMonitors,
                                                          [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left]),
            Probe = p =>
            {
                if (p.Crossed(11.5f)) hits.AddRange(TopP3MonitorsScenarioTests.HitTargets(p));
            },
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(hits, Has.Count.EqualTo(8), $"seed {seed}");
        return (hits.Chunk(2).Select(pair => pair.Order().ToArray()).ToList(), run);
    }

    // The one pair left once the monitors whose beams still hold exactly their two LPDU targets are
    // taken out.
    private static PartyRole[] PairLeftOver(List<PartyRole[]> pairs, params PartyRole[][] unchanged)
    {
        var left = pairs.ToList();
        foreach (var pair in unchanged)
        {
            var index = left.FindIndex(p => p.SequenceEqual(pair.Order()));
            Assert.That(index, Is.Not.EqualTo(-1), $"no monitor took {Describe(pair)}: {string.Join("; ", pairs.Select(Describe))}");
            left.RemoveAt(index);
        }
        return left.Single();
    }

    private static string Describe(IEnumerable<PartyRole> pair) => string.Join(" ", pair);
}
