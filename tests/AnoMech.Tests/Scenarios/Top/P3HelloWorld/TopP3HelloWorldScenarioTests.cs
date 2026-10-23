using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.NegativeRun;

namespace AnoMech.Tests;

// Every roll is pinned: MT+OT defamation with the blue rot, H1+H2 stack with the red rot, M1+M2
// remote first, R1+R2 local first, and the blue towers at S and W in every set. Set 1 as the AI
// plays it (x east, z south):
//
//   0.5   loose spread at r 8.5, held until the towers: MT N, R1 NE, H2 E, M2 SE, OT S, M1 SW,
//         H1 W, R2 NW. 8.0: the M1-M2 remote and R1-R2 local tethers show.
//   27.9  towers: OT (0,18.1) in the S blue tower, MT (-18.1,0) in the W one, H2 (11.35,-3.91) in
//         the E red tower, H1 (3.91,-11.35) in the N one; M2 (9.77,-5.87) and M1 (5.87,-9.77) in
//         the stacks; R1 (6.88,12.42) and R2 (-12.42,-6.88) in OT's and MT's defamations.
//   28.0  regressions: M1-M2 remote, R1-R2 local.
//   29.1  towers, stacks and defamations go off. 29.4: OT and MT step to the tower centres
//         (0,13.7) and (-13.7,0), R1 and R2 onto them for the blue rot, M2 and M1 onto H2 and H1
//         for the red.
//   31.4  M2 (16.91,-1.78) and M1 (2.81,-11.26) pop the remote tether; H2 (9.66,2.59) and H1
//         (-3.91,-9.21) spread; R1 (-0.40,7.69) and R2 (-7.69,0.40) wait.
//   34.1  R1 (-2.89,6.81) and R2 (-6.81,2.89) pop the local tether. 35.0: the first rots go off.
public class TopP3HelloWorldScenarioTests
{
    private static readonly PartyRole[] KitOrder =
        [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static NegativeRun<TopP3HelloWorldScenario> HelloWorld(PartyRole player)
        => Negative<TopP3HelloWorldScenario>(player).Overrides<TopP3HelloWorldStateOverrides>(Pin);

    private static void Pin(TopP3HelloWorldStateOverrides o)
    {
        o.KitOrder = KitOrder;
        o.DefamationColour = RotColour.Blue;
        o.Layouts = [Direction.SW, Direction.SW, Direction.SW, Direction.SW];
    }

    [Test]
    public void UnsoakedTowerWipes()
        => HelloWorld(OffTank)
            .TeleportAt(27.5f, to: new(0, 2))
            .ShouldKill(P3ActionId.BlueTowerUnsoaked, PerRole.All);

    // R2 shares MT's tower, so its blue rot comes only after the soak, at the pass, and goes off
    // long after the tower debuff runs out.
    [Test]
    public void TowerSoakedWithoutTheRotKillsTheSoakerWhenItsDebuffRunsOut()
        => HelloWorld(CasterDps)
            .TeleportAt(26.5f, to: new(-13.7f, 0))
            .TeleportAt(34.2f, to: new(-6.81f, 2.89f))
            .ShouldKill(P3ActionId.BlueTowerStatusExpired, CasterDps);

    [Test]
    public void StackTakenAloneKillsTheHolder()
        => HelloWorld(MeleeDpsA)
            .TeleportAt(26.5f, to: new(15, -10))
            .ShouldKill(P3ActionId.Stack, RegenHealer);

    [Test]
    public void TwoDefamationsKillWhoeverBothHit()
        => HelloWorld(PhysRangedDps)
            .TeleportAt(27f, to: new(-9, 9))
            .ShouldKill(P3ActionId.Defamation, PhysRangedDps);

    [Test]
    public void LatentDefectKillsWhoeverNoDefamationHit()
        => HelloWorld(PhysRangedDps)
            .TeleportAt(27f, to: new(8, -8))
            .ShouldKill(P3ActionId.LatentDefectExpired, PhysRangedDps);

    // R1 stays on OT after taking the blue rot from it.
    [Test]
    public void RotExplosionKillsWhoeverStandsInIt()
        => HelloWorld(PhysRangedDps)
            .FreezeAt(31f)
            .ShouldKill(P3ActionId.BlueRotExplosion, PhysRangedDps);

    [Test]
    public void UnbrokenLocalTetherWipesWhenItRunsOut()
        => HelloWorld(PhysRangedDps)
            .TeleportAt(33f, to: new(-8, 16))
            .ShouldKill(ActionId.HwTetherFail, PerRole.All);

    // M2 stays on H2 after taking its rot, so M1's pop leaves the remote pair 11.3 apart.
    [Test]
    public void UnbrokenRemoteTetherWipesWhenItRunsOut()
        => HelloWorld(MeleeDpsB)
            .FreezeAt(31f)
            .ShouldKill(ActionId.HwTetherFail, PerRole.All);

    // H2 edges toward H1 inside its tower and M1 stands between them, 4.7 from each.
    [Test]
    public void TwoStacksKillWhoeverBothHit()
        => HelloWorld(MeleeDpsA)
            .TeleportAt(28.5f, to: new(7.39f, -8.21f))
            .MoveBotAt(28.6f, ShieldHealer, to: new(10.88f, -5.07f))
            .ShouldKill(P3ActionId.Stack, MeleeDpsA);

    // R1, set 3's left remote, waits clear of every stack, defamation and tower, so OT's stack goes
    // off alone and nothing cleanses R1.
    [Test]
    public void LatentSynchronizationBugKillsWhoeverNoStackHit()
        => HelloWorld(PhysRangedDps)
            .TeleportAt(70.5f, to: new(3.54f, -3.54f))
            .ShouldKill(P3ActionId.Stack, OffTank)
            .ShouldKill(P3ActionId.LatentSynchronizationBugExpired, PhysRangedDps);

    // H2, set 2's left local, sidesteps the defamations and comes back for its rot, so it never
    // carries a defamation of its own; neither does M2, whom H2's set-3 defamation should have hit.
    [Test]
    public void CriticalErrorKillsWhoeverNeverResolvedADefamation()
        => HelloWorld(ShieldHealer)
            .MoveBotAt(49.95f, ShieldHealer, to: new(3.54f, -3.54f))
            .MoveBotAt(50.2f, ShieldHealer, to: new(6.88f, 12.42f))
            .ShouldKill(P3ActionId.CriticalError, ShieldHealer, MeleeDpsB);

    // R1 meets R2 as the remote tether pops: the second Patch lands on the first one's vuln.
    [Test]
    public void TwoTetherBreaksWithinASecondKillEveryone()
        => HelloWorld(PhysRangedDps)
            .TeleportAt(31.4f, to: new(-11, 0))
            .ShouldKill(ActionId.HwTetherBreak, PerRole.All);

    // M1 touches OT on its loose-spread spot, after the rots go live and long before the passes.
    [Test]
    public void TouchingARotHolderCopiesTheirRot()
    {
        var seen = new Dictionary<PartyRole, bool>();
        var run = Run(MeleeDpsA, stopAt: 12.3f, [new Takeover(12f, new Vector2(0, 7.5f))], p =>
        {
            if (!p.Crossed(12.2f)) return;
            seen[MeleeDpsA] = p.Member(MeleeDpsA)!.HasStatus(P3StatusId.BlueRot);
            seen[OffTank] = p.Member(OffTank)!.HasStatus(P3StatusId.BlueRot);
            seen[MeleeDpsB] = p.Member(MeleeDpsB)!.HasStatus(P3StatusId.BlueRot);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(seen[MeleeDpsA], Is.True, "M1 should have caught OT's blue rot");
            Assert.That(seen[OffTank], Is.True, "OT should have kept its blue rot");
            Assert.That(seen[MeleeDpsB], Is.False, "M2, on its own spot, should not have a rot");
        });
    }

    // MT's blue rot went off at 35.0, so the blue rot R1 picked up at the pass can't reach it again.
    [Test]
    public void DebuggerStopsTheRot()
    {
        var caught = true;
        var run = Run(MainTank, stopAt: 35.8f, [new Takeover(35.2f, new Vector2(-2.89f, 7.6f))], p =>
        {
            if (p.Crossed(35.6f)) caught = p.Member(MainTank)!.HasStatus(P3StatusId.BlueRot);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(caught, Is.False, "MT has the Performance Debugger and should not catch the blue rot");
    }

    [Test]
    public void CleanRunEndsWithEveryDebuggerAndNothingElse()
    {
        ushort[] debuggers =
        [
            P3StatusId.SynchronizationDebugger, P3StatusId.OverflowDebugger,
            P3StatusId.UnderflowDebugger, P3StatusId.PerformanceDebugger,
        ];
        ushort[] leftovers =
        [
            P3StatusId.CriticalSynchronizationBug, P3StatusId.CriticalOverflowBug, P3StatusId.RedRot, P3StatusId.BlueRot,
            P3StatusId.RedTowerDebuff, P3StatusId.BlueTowerDebuff, P3StatusId.LatentSynchronizationBug, P3StatusId.LatentDefect,
            P3StatusId.LocalRegression, P3StatusId.RemoteRegression,
        ];
        var held = new Dictionary<PartyRole, HashSet<ushort>>();
        var run = Run(MainTank, stopAt: 100.2f, [], p =>
        {
            if (!p.Crossed(100f)) return;
            foreach (var role in PerRole.All)
                held[role] = p.Member(role)!.ActiveStatuses.Select(s => s.StatusId).ToHashSet();
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var role in PerRole.All)
            {
                Assert.That(held[role], Is.SupersetOf(debuggers), $"{role}'s debuggers");
                Assert.That(held[role].Intersect(leftovers), Is.Empty, $"{role}'s leftovers");
            }
        });
    }

    // Each set's pairs show their tethers 20s before their code smells run out, long before the towers.
    [Test]
    public void TethersShowTwentySecondsBeforeTheirRegressions()
    {
        float[] times = [7.9f, 8.1f, 19.0f, 27.9f, 28.1f, 28.9f, 29.2f, 48.9f, 50.2f, 71.2f];
        var shown = new Dictionary<float, string[]>();
        var run = Run(MainTank, stopAt: 71.3f, [], p =>
        {
            foreach (var at in times)
                if (p.Crossed(at)) shown[at] = TethersBeforeRegression(p);
        });

        string[] setOne = ["local PhysRangedDps+CasterDps", "remote MeleeDpsA+MeleeDpsB"];
        string[] setTwo = ["local RegenHealer+ShieldHealer", "remote MainTank+OffTank"];
        Assert.That(run.Passed, run.ToString);
        Assert.That(shown, Is.EqualTo(new Dictionary<float, string[]>
        {
            [7.9f] = [],
            [8.1f] = setOne,
            [19.0f] = setOne,
            [27.9f] = setOne,
            [28.1f] = [],
            [28.9f] = [],
            [29.2f] = setTwo,
            [48.9f] = setTwo,
            [50.2f] = ["local MeleeDpsA+MeleeDpsB", "remote PhysRangedDps+CasterDps"],
            [71.2f] = ["local MainTank+OffTank", "remote RegenHealer+ShieldHealer"],
        }));
    }

    private static string[] TethersBeforeRegression(ScenarioProbe p)
        => p.World.Children.OfType<SimTether>()
            .Where(t => t.IsActive && t.TetherId is TetherId.HWPrepLocal or TetherId.HWPrepRemote)
            .Select(t => (t.TetherId == TetherId.HWPrepLocal ? "local " : "remote ")
                         + string.Join("+", Enum.GetValues<PartyRole>().Where(r => p.Member(r) is { } m && (m == t.A || m == t.B))))
            .Order()
            .ToArray();

    private static ScenarioRun Run(PartyRole player, float stopAt, IReadOnlyList<Takeover> takeovers, Action<ScenarioProbe> probe)
        => ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            Overrides = o => Pin((TopP3HelloWorldStateOverrides)o),
            Takeovers = takeovers,
            Probe = probe,
        });
}
