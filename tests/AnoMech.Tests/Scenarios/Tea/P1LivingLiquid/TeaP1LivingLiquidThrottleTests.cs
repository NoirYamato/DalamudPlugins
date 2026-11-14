using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Tea;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// Throttle with a human pressing its own cleanses, as its client reports them. Cascade 2 from its start on the stage:
// Throttle lands at 10.5 on the main tank, the off tank, both healers, M1 and R1, and runs out at 19.5. The stage's
// healers cleanse at 11.2, 13.7, 16.2 and 18.7, each itself first, then H1 down the party list (the main tank, the off
// tank) and H2 up it (R1, M1, then H1 and the tanks); the human's healer seat is left to the test.
public class TeaP1LivingLiquidThrottleTests
{
    private static Stage HumanHealer => new() { First = Cascade2, HumanUntil = 60f, PlayerCleansesItself = true };

    // H1's three in LPDU's order, and in another: the order is nobody's rule.
    [TestCase(RegenHealer, MainTank, OffTank, TestName = "{m}(itself, then down the list)")]
    [TestCase(OffTank, MainTank, RegenHealer, TestName = "{m}(the other way)")]
    public void AHumanHealersEsunasTakeThrottleOffInAnyOrder(PartyRole first, PartyRole second, PartyRole third)
    {
        var run = Play(HumanHealer, RegenHealer, (11.2f, first), (13.7f, second), (16.2f, third));

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void TheModuleNeedNotBeOnForACleanseToCount([Values] bool resolveYourOwnActions)
    {
        var stage = new Stage { First = Cascade2, HumanUntil = 60f, PlayerCleansesItself = true, UserActionsEnabled = resolveYourOwnActions };
        var run = Play(stage, RegenHealer, (11.2f, RegenHealer), (13.7f, MainTank), (16.2f, OffTank));

        Assert.That(run.Passed, run.ToString);
    }

    // The press at 12.0 is settled by the run's next tick.
    [Test]
    public void AnEsunaOnACarrierTakesItOffAsTheRunNextLooks()
    {
        var seen = new List<(bool Counted, bool Status)>();
        var run = Play(HumanHealer, RegenHealer, [(12f, MainTank), (14f, RegenHealer), (16f, OffTank)], stopAt: 21f, probe: p =>
        {
            var mainTank = p.Member(MainTank)!;
            foreach (var at in new[] { 11.9f, 12.1f })
                if (p.Crossed(at)) seen.Add((Stage.Fight(p).IsThrottled(mainTank), mainTank.HasStatus(P1StatusId.Throttle)));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(seen, Is.EqualTo(new[] { (true, true), (false, false) }));
    }

    // H2, alone, gets to four: itself, R1, M1 and H1. The tanks, H1's to cleanse, are left.
    [Test]
    public void AHumanHealerWhoPressesNothingLeavesItsShareToDie()
    {
        var run = Play(HumanHealer, RegenHealer, [], stopAt: 21f);

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EquivalentTo(new[] { (MainTank, (uint?)P1ActionId.LiquidGaol), (OffTank, (uint?)P1ActionId.LiquidGaol) }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(19.5f).Within(0.1f));
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1LivingLiquid.ThrottleRanOut));
    }

    // Three Esunas on itself cleanse one seat.
    [Test]
    public void EsunaOnACleanSeatCleansesNobodyElse()
    {
        var run = Play(HumanHealer, RegenHealer, [(11.2f, RegenHealer), (13.7f, RegenHealer), (16.2f, RegenHealer)], stopAt: 21f);

        Assert.That(run.Deaths.Select(d => d.Role), Is.EqualTo(new[] { MainTank }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(TeaP1LivingLiquid.ThrottleRanOut));
    }

    // H2 leaves the off tank to the human.
    [TestCase(19.3f, true)]
    [TestCase(19.7f, false)]
    public void TheLastCleanseMustLandBeforeThrottleRunsOut(float lastAt, bool saves)
    {
        var stage = new Stage { First = Cascade2, HumanUntil = 60f, PlayerCleansesItself = true, NotCleansed = [OffTank] };
        var run = Play(stage, RegenHealer, [(11.2f, RegenHealer), (13.7f, MainTank), (lastAt, OffTank)], stopAt: saves ? 21f : null);

        if (saves) Assert.That(run.Passed, run.ToString);
        else TeaP1LimitCutScenarioTests.AssertFirstDeath(run, OffTank, P1ActionId.LiquidGaol, 19.5f, TeaP1LivingLiquid.ThrottleRanOut);
    }

    // The client turns a cleanse aimed at an enemy, or at nothing, onto its caster. H1's own Throttle goes; the stage
    // leaves the tanks' to it.
    [TestCase(true, TestName = "{m}(at the Living Liquid)")]
    [TestCase(false, TestName = "{m}(at nothing)")]
    public void AnEsunaAimedAtNoPartyMemberCleansesTheHealerItself(bool atTheLiquid)
    {
        bool? counted = null;
        var run = HumanHealer.Run(RegenHealer, stopAt: 13f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(12f))
                p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, atTheLiquid ? (ulong)fight.Liquid!.GameObjectId : 0ul));
            if (p.Crossed(12.5f)) counted = fight.IsThrottled(p.Member(RegenHealer)!);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(counted, Is.False);
    }

    // R1 is a bard; the stage's healers leave its Throttle on.
    [TestCase(true)]
    [TestCase(false)]
    public void ABardsWardensPaeanTakesItsOwnThrottleOff(bool presses)
    {
        var stage = new Stage { First = Cascade2, HumanUntil = 60f, NotCleansed = [PhysRangedDps] };
        var run = stage.Run(PhysRangedDps, stopAt: 21f, probe: p =>
        {
            if (presses && p.Crossed(12f))
                p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.WardensPaean, (ulong)p.Member(PhysRangedDps)!.GameObjectId));
        });

        if (presses) Assert.That(run.Passed, run.ToString);
        else TeaP1LimitCutScenarioTests.AssertFirstDeath(run, PhysRangedDps, P1ActionId.LiquidGaol, 19.5f, TeaP1LivingLiquid.ThrottleRanOut);
    }

    // M2 has no Throttle: nothing kills it, and an Esuna on it is wasted.
    [Test]
    public void ASeatWithoutThrottleNeedsNoCleanse()
    {
        var run = Play(HumanHealer, RegenHealer, (11.2f, MeleeDpsB), (13.7f, RegenHealer), (16.2f, MainTank), (18f, OffTank));

        Assert.That(run.Passed, run.ToString);
    }

    private static ScenarioRun Play(Stage stage, PartyRole healer, params (float At, PartyRole On)[] esunas)
        => Play(stage, healer, esunas, stopAt: null);

    // The human in `healer`'s seat presses Esuna on each seat at its time.
    private static ScenarioRun Play(Stage stage, PartyRole healer, (float At, PartyRole On)[] esunas, float? stopAt, Action<ScenarioProbe>? probe = null)
        => stage.Run(healer, stopAt, probe: p =>
        {
            foreach (var (at, on) in esunas)
                if (p.Crossed(at) && p.Member(on) is { } target)
                    p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, (ulong)target.GameObjectId));
            probe?.Invoke(p);
        });
}
