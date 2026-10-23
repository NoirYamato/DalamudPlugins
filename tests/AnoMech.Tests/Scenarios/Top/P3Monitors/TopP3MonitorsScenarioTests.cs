using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P3Monitors;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.NegativeRun;

namespace AnoMech.Tests;

// The slideshow's example is pinned. With Omega beaming west the AI puts the monitor players on
// spots M1-M3: OffTank (10.05,-12.18) aiming east, MeleeDpsA (-9.84,-8.35) north, PhysRangedDps
// (-9.92,9.24) south. The rest take N1-N5: RegenHealer (1.98,-16.6), ShieldHealer (13.54,-4.44),
// MainTank (13.71,4.99), MeleeDpsB (6.14,11.2), CasterDps (2.23,18.34). Omega's monitor takes
// MeleeDpsA and PhysRangedDps, OffTank's ShieldHealer and MainTank, MeleeDpsA's RegenHealer and
// OffTank, PhysRangedDps's MeleeDpsB and CasterDps. East mirrors x.
//
//   0.1   Omega casts Critical Error (8s).
//   8.1   Critical Error hits everyone.
//   8.6   the bots line up on the west arc.
//   19.4  Omega casts Oversampled Wave Cannon; the three get their monitors.
//   27.4  the monitor players turn to aim.
//   29.5  eight 7y circles, one on each player.
public class TopP3MonitorsScenarioTests
{
    [Test]
    public void CriticalErrorHitsAtEightSecondsAndKillsNobody()
    {
        var criticalError = $"{TopP3Constants.P3ActionId.CriticalError}";
        var rows = new List<(string Kind, float Time)>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 9f,
            Probe = p =>
            {
                if (!p.Crossed(8.8f)) return;
                rows.AddRange(p.Fake.Timeline.Rows.Where(row => row["id"] == criticalError && row["kind"] is "cast" or "ability")
                                   .Select(row => (row["kind"], float.Parse(row["t"], System.Globalization.CultureInfo.InvariantCulture))));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(rows.Select(r => r.Kind), Is.EqualTo(new[] { "cast", "ability" }), string.Join("; ", rows));
        Assert.That(rows[0].Time, Is.EqualTo(0.1f).Within(0.05f));
        Assert.That(rows[1].Time, Is.EqualTo(8.1f).Within(0.1f));
    }

    private const uint Hit = ActionId.OversampledWaveCannonAoe;
    private const float FacingNorth = MathF.PI;

    private static readonly PartyRole[] Monitors = [OffTank, MeleeDpsA, PhysRangedDps];

    private static NegativeRun<TopP3MonitorsScenario> Slideshow(PartyRole player)
        => Negative<TopP3MonitorsScenario>(player)
            .Overrides<TopP3MonitorsStateOverrides>(o => TopP3MonitorsLpduAiTests.Pin(o, OmegaMonitor.West, Monitors,
                                                                                      [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left]));

    // Omega's cast only draws its monitor; the circles follow the state's side. The two must agree,
    // or players read the monitor and stand in the wrong half.
    [TestCase(OmegaMonitor.West, TopP3Constants.P3ActionId.OversampledWaveCannonWest)]
    [TestCase(OmegaMonitor.East, TopP3Constants.P3ActionId.OversampledWaveCannonEast)]
    public void EveryMonitorHitsItsTwoLpduTargets(OmegaMonitor omega, uint omegaCast)
    {
        var picks = new List<PartyRole>();
        var omegaCasts = new List<string>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => TopP3MonitorsLpduAiTests.Pin((TopP3MonitorsStateOverrides)o, omega, Monitors,
                                                          [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left]),
            Probe = p =>
            {
                if (!p.Crossed(29.9f)) return;
                picks.AddRange(HitTargets(p));
                var omegaId = p.World.Children.OfType<SimEnemy>().Single(e => e.BNpcBaseId == TopP3Constants.OmegaP3).EntityId.ToString("X8");
                omegaCasts.AddRange(p.Fake.Timeline.Rows.Where(row => row["kind"] == "cast" && row["actor"] == omegaId).Select(row => row["id"]));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(omegaCasts, Is.EqualTo(new[] { $"{TopP3Constants.P3ActionId.CriticalError}", $"{omegaCast}" }), "Omega's casts");
        Assert.That(picks, Has.Count.EqualTo(8));
        var pairs = picks.Chunk(2).Select(pair => pair.Order().ToArray()).ToList();
        Assert.That(pairs[0], Is.EqualTo(new[] { MeleeDpsA, PhysRangedDps }), "Omega's monitor");
        Assert.That(pairs.Skip(1), Is.EquivalentTo(new[]
        {
            new[] { MainTank, ShieldHealer },
            new[] { OffTank, RegenHealer },
            new[] { MeleeDpsB, CasterDps },
        }), "the players' monitors");
    }

    // A left monitor is Oversampled Wave Cannon Loading 3453, a right one 3452, as in P5.
    [Test]
    public void MonitorsShowFromTheCastUntilTheHits()
    {
        var atCast = new Dictionary<PartyRole, ushort[]>();
        var afterHits = new List<ushort>();
        ushort[] loading = [StatusId.PlayerMonitorLeft, StatusId.PlayerMonitorRight];
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = OffTank,
            Overrides = o => TopP3MonitorsLpduAiTests.Pin((TopP3MonitorsStateOverrides)o, OmegaMonitor.West, Monitors,
                                                          [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left]),
            Probe = p =>
            {
                if (p.Crossed(19.5f))
                    foreach (var role in PerRole.All)
                        atCast[role] = loading.Where(id => p.Member(role)!.HasStatus(id)).ToArray();
                if (p.Crossed(29.6f))
                    foreach (var role in PerRole.All)
                        afterHits.AddRange(loading.Where(id => p.Member(role)!.HasStatus(id)));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(atCast[OffTank], Is.EqualTo(new[] { StatusId.PlayerMonitorLeft }));
            Assert.That(atCast[MeleeDpsA], Is.EqualTo(new[] { StatusId.PlayerMonitorRight }));
            Assert.That(atCast[PhysRangedDps], Is.EqualTo(new[] { StatusId.PlayerMonitorLeft }));
            foreach (var role in PerRole.All.Except(Monitors))
                Assert.That(atCast[role], Is.Empty, $"{role} has a monitor");
            Assert.That(afterHits, Is.Empty);
        });
    }

    // MeleeDpsB steps next to CasterDps, still in PhysRangedDps's beam: both circles land on both.
    [Test]
    public void TwoPlayersInsideOneCircleBothDie()
        => Slideshow(MeleeDpsB)
            .TeleportAt(28.9f, to: new(4, 15))
            .ShouldKill(Hit, MeleeDpsB, CasterDps);

    // Facing north, OffTank's monitor aims toward Omega's beam instead of away from it. It misses
    // ShieldHealer and MainTank and takes two at random of the five on that side, each of whom
    // another monitor takes too, so those two die.
    [TestCase(OmegaMonitor.West, MonitorSide.Left)]
    [TestCase(OmegaMonitor.East, MonitorSide.Right)]
    public void MonitorAimedTheWrongWayHitsTwoPlayersTwice(OmegaMonitor omega, MonitorSide side)
    {
        var x = omega == OmegaMonitor.East ? -1f : 1f;
        PartyRole[] onTheWrongSide = [RegenHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];
        var killed = Enumerable.Range(0, 20).Select(seed =>
        {
            var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), 0, seed, new ScenarioRunOptions
            {
                PlayerRole = OffTank,
                WriteArtifactsOnFailure = false,
                Overrides = o => TopP3MonitorsLpduAiTests.Pin((TopP3MonitorsStateOverrides)o, omega, Monitors,
                                                              [side, MonitorSide.Right, MonitorSide.Left]),
                Takeovers = [new Takeover(28.9f, new Vector2(10.05f * x, -12.18f), FacingNorth)],
            });
            var dead = run.Deaths.Select(d => d.Role).Order().ToArray();
            Assert.That(run.Failure, Is.Null, run.ToString);
            Assert.That(run.Deaths.Select(d => d.ActionId), Has.All.EqualTo(Hit), run.ToString);
            Assert.That(dead, Has.Length.EqualTo(2), run.ToString);
            Assert.That(dead, Is.SubsetOf(onTheWrongSide), run.ToString);
            return string.Join(" ", dead);
        }).ToList();

        Assert.That(killed.Distinct().Count(), Is.GreaterThan(1), string.Join("; ", killed));
    }

    // ShieldHealer and MainTank crowd OffTank and stay in its beam: both its circles land on all
    // three of them.
    [Test]
    public void StackingOnAMonitorPlayerKillsEveryoneInTheCircle()
        => Slideshow(ShieldHealer)
            .TeleportAt(28.9f, to: new(11, -10))
            .MoveBotAt(28.9f, MainTank, to: new(12, -11))
            .ShouldKill(Hit, OffTank, ShieldHealer, MainTank);

    // Who each Oversampled Wave Cannon circle has landed on so far, in the order they fired.
    internal static List<PartyRole> HitTargets(ScenarioProbe p)
    {
        var roleById = PerRole.All.Where(r => p.Member(r) is not null)
                              .ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
        return p.Fake.Timeline.Rows.Where(row => row["kind"] == "ability" && row["id"] == $"{Hit}")
                .Select(row => roleById[row["target"]])
                .ToList();
    }
}
