using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
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
//   1.0   Omega casts Oversampled Wave Cannon; the three get their monitors.
//   9.0   the monitor players turn to aim.
//   11.1  eight 7y circles, one on each player.
public class TopP3MonitorsScenarioTests
{
    private const uint Hit = ActionId.OversampledWaveCannonAoe;
    private const float FacingNorth = MathF.PI;

    private static readonly PartyRole[] Monitors = [OffTank, MeleeDpsA, PhysRangedDps];

    private static NegativeRun<TopP3MonitorsScenario> Slideshow(PartyRole player, OmegaMonitor omega = OmegaMonitor.West,
                                                               MonitorSide offTankSide = MonitorSide.Left)
        => Negative<TopP3MonitorsScenario>(player)
            .Overrides<TopP3MonitorsStateOverrides>(o =>
                TopP3MonitorsLpduAiTests.Pin(o, omega, Monitors, [offTankSide, MonitorSide.Right, MonitorSide.Left]));

    [TestCase(OmegaMonitor.West)]
    [TestCase(OmegaMonitor.East)]
    public void EveryMonitorHitsItsTwoLpduTargets(OmegaMonitor omega)
    {
        var picks = new List<PartyRole>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => TopP3MonitorsLpduAiTests.Pin((TopP3MonitorsStateOverrides)o, omega, Monitors,
                                                          [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left]),
            Probe = p =>
            {
                if (!p.Crossed(11.5f)) return;
                var roleById = PerRole.All.Where(r => p.Member(r) is not null)
                                      .ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
                picks.AddRange(p.Fake.Timeline.Rows.Where(row => row["kind"] == "ability" && row["id"] == $"{Hit}")
                                .Select(row => roleById[row["target"]]));
            },
        });

        Assert.That(run.Passed, run.ToString);
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
                if (p.Crossed(1.1f))
                    foreach (var role in PerRole.All)
                        atCast[role] = loading.Where(id => p.Member(role)!.HasStatus(id)).ToArray();
                if (p.Crossed(11.2f))
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
            .TeleportAt(10.5f, to: new(4, 15))
            .ShouldKill(Hit, MeleeDpsB, CasterDps);

    // Facing north, OffTank's monitor aims toward Omega's beam instead of away from it, missing
    // ShieldHealer and MainTank and taking the two nearest on that side: RegenHealer, whom
    // MeleeDpsA's monitor also takes, and MeleeDpsA, whom Omega's does.
    [TestCase(OmegaMonitor.West, MonitorSide.Left)]
    [TestCase(OmegaMonitor.East, MonitorSide.Right)]
    public void MonitorAimedTheWrongWayHitsSomeoneTwice(OmegaMonitor omega, MonitorSide side)
    {
        var x = omega == OmegaMonitor.East ? -1f : 1f;
        Slideshow(OffTank, omega, side)
            .TeleportAt(10.5f, to: new(10.05f * x, -12.18f), facing: FacingNorth)
            .ShouldKill(Hit, RegenHealer, MeleeDpsA);
    }

    // ShieldHealer and MainTank crowd OffTank, who is MeleeDpsA's target and aims at them: each of
    // the three is inside all three circles.
    [Test]
    public void StackingOnAMonitorPlayerKillsEveryoneInTheCircle()
        => Slideshow(ShieldHealer)
            .TeleportAt(10.5f, to: new(11, -10))
            .MoveBotAt(10.5f, MainTank, to: new(12, -11))
            .ShouldKill(Hit, OffTank, ShieldHealer, MainTank);
}
