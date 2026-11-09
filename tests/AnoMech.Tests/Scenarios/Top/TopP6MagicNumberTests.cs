using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Magic Number cast at 2.0: it decides at 6.7 and lands at 7.0, the main tank's limit break 3 (2.7) carrying everyone.
// Its mark lands on all eight 1.3 s after the hit, at 8.3, and kills whoever still carries it as it runs out 6 s later,
// at 14.3, unless a healer's limit break 3 resolves while it is on. Everyone holds Brilliant Dynamis, so each press
// gives the gauge back.
public class TopP6MagicNumberTests
{
    private const float CastAt = 2f;
    private const float MarkRunsOut = 14.3f;

    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    private static string MarkKills => $"Magic Number ({TopP6AlphaOmega.MagicNumberMarkRanOut})";

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void AHealerLimitBreak3WhileTheMarkIsOnLiftsItFromEveryone()
    {
        var marked = new Dictionary<float, int>();
        var run = MagicNumber((world, limitBreaks) =>
        {
            world.Events.Add(8.5f, () => limitBreaks.Press(RegenHealer, Job(RegenHealer)));
            foreach (var at in new[] { 8.2f, 8.4f, 10.6f })
                world.Events.Add(at, () => marked[at] = Marked(world));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(marked, Is.EqualTo(new Dictionary<float, int> { [8.2f] = 0, [8.4f] = 8, [10.6f] = 0 }));
    }

    // Pressed at 5.5, it resolves at 7.5, before the mark lands.
    [Test]
    public void OneResolvingBeforeTheMarkLandsLeavesItToKill()
    {
        var run = MagicNumber((world, limitBreaks) => world.Events.Add(5.5f, () => limitBreaks.Press(RegenHealer, Job(RegenHealer))));

        AssertTheMarkKilledEveryone(run);
    }

    [Test]
    public void WithoutOneTheMarkKillsEveryoneAsItRunsOut()
    {
        var run = MagicNumber((_, _) => { });

        AssertTheMarkKilledEveryone(run);
    }

    // The human regen healer's own press, through their client.
    [TestCase(9f, true)]
    [TestCase(5f, false)]
    public void AHumanHealersPressCountsTheSame(float pressAt, bool lives)
    {
        var run = MagicNumber((world, _) => TopP6HumanLimitBreak.Press(world, pressAt, LimitBreakHandler.ActionId(Job(RegenHealer), 3)),
            new TimelineRunOptions { PlayerRole = RegenHealer, HumanPlayer = true, UserActionsEnabled = true });

        if (lives) Assert.That(run.Passed, run.ToString);
        else AssertTheMarkKilledEveryone(run);
    }

    // The human's client is heard between the sim's frames. A resolve heard in the frame the mark runs out, before the
    // mark's own event, landed while the mark was on: it lifts it. One heard in the frame the mark lands, before the
    // mark's event, landed before the mark: it lifts nothing.
    [TestCase(true)]
    [TestCase(false)]
    public void AHumanHealersResolveHeardInTheMarksOwnFrame(bool asItRunsOut)
    {
        var markLands = CastAt + TopP6Actions.MagicNumber.ReleasesAfter() + P6Duration.MagicNumberMarkAfterTheHit;
        var resolvesAt = asItRunsOut ? markLands + P6Duration.MagicNumberMark : markLands;
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                alphaOmega.ApplyCosmoMemoryStatuses();
            });
            var limitBreak3 = LimitBreakHandler.ActionId(Job(RegenHealer), 3);
            var castSeconds = Natives.Data.Action(limitBreak3)!.CastSeconds;
            world.Events.Add(resolvesAt - castSeconds, () => Act(PlayerActionStep.CastStarted, limitBreak3, castSeconds));
            world.Events.Add(resolvesAt, () => Act(PlayerActionStep.Resolved, limitBreak3, 0f));
            alphaOmega.MagicNumber(CastAt, limitBreaks);
            world.Events.Add(2.7f, () => limitBreaks.Press(MainTank, Job(MainTank)));
        }, new TimelineRunOptions { PlayerRole = RegenHealer, HumanPlayer = true, UserActionsEnabled = true });

        if (asItRunsOut) Assert.That(run.Passed, run.ToString);
        else AssertTheMarkKilledEveryone(run);
    }

    // Only a healer's limit break 3 lifts it.
    [Test]
    public void AnotherLimitBreakLeavesIt()
    {
        var run = MagicNumber((world, limitBreaks) => world.Events.Add(8.5f, () => limitBreaks.Press(CasterDps, Job(CasterDps), world.Party.Get(MainTank))));

        AssertTheMarkKilledEveryone(run);
    }

    // Without Brilliant Dynamis the main tank's press isn't given back: the healer's waits for a gauge that never
    // fills, and the deaths say so.
    [Test]
    public void AShortGaugeSaysSo()
    {
        var run = MagicNumber((world, limitBreaks) => world.Events.Add(8.5f, () => limitBreaks.Press(RegenHealer, Job(RegenHealer))),
            mainTankWithoutBrilliantDynamis: true);

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause).Distinct(), Is.EqualTo(new[] { $"Magic Number ({TopP6AlphaOmega.MagicNumberMarkRanOut}{TopP6LimitBreaks.GaugeWasShort})" }));
    }

    private static void AssertTheMarkKilledEveryone(TimelineRunResult run)
    {
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(Enum.GetValues<PartyRole>()), run.ToString);
        Assert.That(run.Deaths.Select(d => (d.ActionId, d.Cause)).Distinct(), Is.EqualTo(new[] { ((uint?)P6ActionId.MagicNumber, MarkKills) }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Has.All.EqualTo(MarkRunsOut).Within(TimelineRun.FrameLate), run.ToString);
    }

    private static int Marked(SimWorld world) => Enum.GetValues<PartyRole>().Count(r => world.Party.Get(r)!.HasStatus(P6StatusId.MagicNumber));

    private static void Act(PlayerActionStep step, uint actionId, float castSeconds)
        => ((FakeUserActions)Natives.UserActions).Act(new PlayerAction(step, ActionType.Action, actionId, 0, castSeconds));

    private static TimelineRunResult MagicNumber(Action<SimWorld, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null,
        bool mainTankWithoutBrilliantDynamis = false)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                alphaOmega.ApplyCosmoMemoryStatuses();
                if (mainTankWithoutBrilliantDynamis) world.Party.Get(MainTank)!.RemoveStatus(StatusId.BrilliantDynamis);
            });
            alphaOmega.MagicNumber(2f, limitBreaks);
            world.Events.Add(2.7f, () => limitBreaks.Press(MainTank, Job(MainTank)));
            timeline(world, limitBreaks);
        }, options ?? new TimelineRunOptions { PlayerRole = OffTank });
}
