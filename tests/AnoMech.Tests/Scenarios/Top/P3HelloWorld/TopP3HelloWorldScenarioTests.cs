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
//         (0,14) and (-14,0), R1 and R2 onto them for the blue rot; M2 and M1 take the red from
//         1.3 inside H2 and H1, at (10.43,-4.83) and (4.83,-10.43), 7.9 apart, short of the 9 at
//         which their tether breaks. 29.72: the tower debuffs land on the four soakers.
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

    // R2 shares MT's tower, so its blue rot comes only after the soak, at the pass, and goes off long
    // after its tower debuff runs out, 10.62s after the towers.
    [Test]
    public void TowerSoakedWithoutTheRotKillsTheSoakerWhenItsDebuffRunsOut()
    {
        var run = Run(CasterDps, stopAt: 40f,
                      [new Takeover(26.5f, new Vector2(-14f, 0)), new Takeover(34.2f, new Vector2(-6.81f, 2.89f))], _ => { });

        AssertFirstDeath(run, CasterDps, P3ActionId.BlueTowerStatusExpired, at: 29.1f + 0.62f + 10f);
    }

    // Ten seconds from 0.62s after the towers go off, as in the fight.
    [Test]
    public void TowerDebuffsLandJustAfterTheTowersAndRunTenSeconds()
    {
        var remaining = new Dictionary<float, float?>();
        var run = Run(MainTank, stopAt: 30f, [], p =>
        {
            foreach (var at in new[] { 29.6f, 29.8f })
                if (p.Crossed(at)) remaining[at] = p.Member(OffTank)!.FindStatus(P3StatusId.BlueTowerDebuff)?.RemainingTime;
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(remaining[29.6f], Is.Null, "OT's tower debuff before it lands");
            Assert.That(remaining[29.8f], Is.EqualTo(29.1f + 0.62f + 10f - 29.8f).Within(0.05f), "OT's tower debuff once it has");
        });
    }

    // The towers stand 14y out, the defamation colour's 45° either side of the layout and the stack colour's
    // 135°: a cardinal layout puts them on the intercardinals. Blue is the defamation colour here.
    [TestCase(225f, new[] { 180f, 270f }, new[] { 0f, 90f })]
    [TestCase(270f, new[] { 225f, 315f }, new[] { 45f, 135f })]
    [TestCase(0f, new[] { 45f, 315f }, new[] { 135f, 225f })]
    public void TowersStandOnTheRingRoundTheLayout(float layoutDegrees, float[] blue, float[] red)
    {
        var layout = new Direction(layoutDegrees * MathF.PI / 180f);
        var towers = new List<(uint ActionId, Vector3 At)>();
        var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 19.5f,
            Overrides = o =>
            {
                Pin((TopP3HelloWorldStateOverrides)o);
                ((TopP3HelloWorldStateOverrides)o).Layouts = [layout, layout, layout, layout];
            },
            Probe = p =>
            {
                if (!p.Crossed(19.3f)) return;
                towers.AddRange(p.World.Children.OfType<SimEnemy>()
                                 .Where(e => e.Casting.IsCasting && e.Casting.ActionId is P3ActionId.RedTower or P3ActionId.BlueTower)
                                 .Select(e => (e.Casting.ActionId, e.Position)));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(towers, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(towers.Select(t => new Vector2(t.At.X, t.At.Z).Length()), Is.All.EqualTo(14f).Within(0.01f));
            Assert.That(Bearings(towers, P3ActionId.BlueTower), Is.EquivalentTo(blue));
            Assert.That(Bearings(towers, P3ActionId.RedTower), Is.EquivalentTo(red));
        });
    }

    private static IEnumerable<float> Bearings(IEnumerable<(uint ActionId, Vector3 At)> towers, uint actionId)
        => towers.Where(t => t.ActionId == actionId)
                 .Select(t => (MathF.Round(MathF.Atan2(t.At.X, -t.At.Z) * 180f / MathF.PI) + 360f) % 360f);

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

    // Latent Defect lands with Hello World's other debuffs at 5s and runs 27s.
    [Test]
    public void LatentDefectKillsWhoeverNoDefamationHit()
    {
        float? remaining = null;
        var run = Run(PhysRangedDps, stopAt: 32.5f, [new Takeover(27f, new Vector2(8, -8))], p =>
        {
            if (p.Crossed(6f)) remaining = p.Member(PhysRangedDps)!.FindStatus(P3StatusId.LatentDefect)?.RemainingTime;
        });

        Assert.That(remaining, Is.EqualTo(26f).Within(0.05f), "R1's Latent Defect a second after it lands");
        AssertFirstDeath(run, PhysRangedDps, P3ActionId.LatentDefectExpired, at: 32f);
    }

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

    // M2 waits clear of every rot after taking its own, 7.4 from M1's pop spot, so the remote pair never parts.
    [Test]
    public void UnbrokenRemoteTetherWipesWhenItRunsOut()
        => HelloWorld(MeleeDpsB)
            .TeleportAt(31f, to: new(4, -4))
            .ShouldKill(ActionId.HwTetherFail, PerRole.All);

    // M1 and M2 have their rots and hold the remote tether until its pop at 31.4. M2 steps further out
    // from M1 first: past 9 y the tether breaks.
    [TestCase(8.5f, true)]
    [TestCase(9.5f, false)]
    public void TheRemoteTetherBreaksPastNineYalms(float apart, bool holds)
    {
        bool? held = null;
        var run = Run(MeleeDpsB, stopAt: 31.2f, [new Takeover(30.9f, null)], p =>
        {
            if (p.Crossed(30.95f))
            {
                var m1 = p.Member(MeleeDpsA)!.Position;
                var m2 = p.Member(MeleeDpsB)!;
                m2.SetPosition(m1 + Vector3.Normalize(m2.Position - m1) * apart);
            }
            if (p.Crossed(31.1f)) held = p.Member(MeleeDpsB)!.HasStatus(P3StatusId.RemoteRegression);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(held, Is.EqualTo(holds), $"M2 {apart} y from M1");
    }

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

    private static void AssertFirstDeath(ScenarioRun run, PartyRole role, uint actionId, float at)
    {
        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        var first = run.Deaths[0];
        Assert.Multiple(() =>
        {
            Assert.That(first.Role, Is.EqualTo(role), run.ToString);
            Assert.That(first.ActionId, Is.EqualTo(actionId), run.ToString);
            Assert.That(first.Time, Is.EqualTo(at).Within(0.05f), run.ToString);
        });
    }

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
