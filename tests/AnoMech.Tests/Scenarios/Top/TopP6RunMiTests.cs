using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Run: ****mi* cast at 2.0: its 16 s cast ends at 18.0. A melee limit break 3 whose cast bar ends by then defeats Alpha
// Omega as it resolves; otherwise its damage lands 30 s after the cast starts, at 32.0, and kills everyone.
public class TopP6RunMiTests
{
    private const float CastAt = 2f;
    private const float MeleeLimitBreak3Cast = 4.5f;

    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    private static string RunMiKills => $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded})";

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void ItsCastIsShown()
    {
        var run = RunMi((world, alphaOmega, limitBreaks) => world.Events.Add(3f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy)));

        Assert.That(run.Sent("cast", P6ActionId.RunMi).Select(c => c.Time), Is.EqualTo(new[] { CastAt }).Within(TimelineRun.FrameLate));
    }

    // Pressed at 3.0, it resolves at 7.5: Alpha Omega falls then, and nobody dies.
    [TestCase(3f)]
    [TestCase(13.4f)]
    public void AMeleeLimitBreak3LandingInTheCastDefeatsAlphaOmega(float pressAt)
    {
        var fell = new Dictionary<string, bool>();
        var run = RunMi((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(pressAt, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
            world.Events.Add(pressAt + 4.4f, () => fell["before"] = alphaOmega.Enemy!.Dead);
            world.Events.Add(pressAt + 4.6f, () => fell["after"] = alphaOmega.Enemy!.Dead);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(fell, Is.EqualTo(new Dictionary<string, bool> { ["before"] = false, ["after"] = true }));
    }

    // Pressed at 14.0, it resolves at 18.5, after the cast's end.
    [Test]
    public void OneLandingAfterTheCastEndsIsTooLate()
    {
        var run = RunMi((world, alphaOmega, limitBreaks) => world.Events.Add(14f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy)));

        AssertEveryoneDiedToIt(run);
    }

    [Test]
    public void WithoutOneItKillsEveryone()
        => AssertEveryoneDiedToIt(RunMi((_, _, _) => { }));

    [Test]
    public void AnotherKindDoesntDefeatAlphaOmega()
        => AssertEveryoneDiedToIt(RunMi((world, alphaOmega, limitBreaks) => world.Events.Add(3f, () => limitBreaks.Press(CasterDps, Job(CasterDps), alphaOmega.Enemy))));

    // The melee's press waits for a gauge left at one bar, and the deaths say so.
    [Test]
    public void AShortGaugeSaysSo()
    {
        var run = RunMi((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(2.5f, () => limitBreaks.SetGauge(1f));
            world.Events.Add(3f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
        });

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause).Distinct(), Is.EqualTo(new[] { $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded}{TopP6LimitBreaks.GaugeWasShort})" }));
    }

    // A press that gives up as the cast ends, 14 s before the damage lands, still left the gauge short.
    [Test]
    public void AShortGaugeSaysSoThoughThePressGaveUpAsTheCastEnded()
    {
        var run = RunMi((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(2.5f, () => limitBreaks.SetGauge(1f));
            world.Events.Add(3f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy, TopP6AlphaOmega.RunMiFinishedBy(CastAt)));
        });

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause).Distinct(), Is.EqualTo(new[] { $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded}{TopP6LimitBreaks.GaugeWasShort})" }));
    }

    // The human's own press counts as a bot's.
    [Test]
    public void AHumansMeleeLimitBreak3DefeatsItToo()
    {
        var run = RunMi((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 3f, LimitBreakHandler.ActionId(Job(MeleeDpsA), 3), () => alphaOmega.Enemy),
            new TimelineRunOptions { PlayerRole = MeleeDpsA, HumanPlayer = true, UserActionsEnabled = true });

        Assert.That(run.Passed, run.ToString);
    }

    // The human's client hears its cast end only on a later frame: started 4.5 s before the cast ends, the human's lands
    // with it, its resolve reaching the run after the cast has ended, and still defeats Alpha Omega.
    [Test]
    public void AHumansMeleeLimitBreak3LandingAsTheCastEndsDefeatsItThoughHeardAfter()
    {
        const float HeardLate = 0.05f;
        var run = RunMi((world, alphaOmega, _) => world.Events.Add(TopP6AlphaOmega.RunMiFinishedBy(CastAt) - MeleeLimitBreak3Cast - 0.02f, () =>
        {
            var userActions = (FakeUserActions)Natives.UserActions;
            var limitBreak3 = LimitBreakHandler.ActionId(Job(MeleeDpsA), 3);
            var target = (ulong)alphaOmega.Enemy!.GameObjectId;
            userActions.Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, limitBreak3, target, MeleeLimitBreak3Cast));
            world.Events.Add(MeleeLimitBreak3Cast + HeardLate,
                () => userActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, limitBreak3, target)));
        }), new TimelineRunOptions { PlayerRole = MeleeDpsA, HumanPlayer = true, UserActionsEnabled = true });

        Assert.That(run.Passed, run.ToString);
    }

    private static void AssertEveryoneDiedToIt(TimelineRunResult run)
    {
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(Enum.GetValues<PartyRole>()), run.ToString);
        Assert.That(run.Deaths.Select(d => (d.ActionId, d.Cause)).Distinct(), Is.EqualTo(new[] { ((uint?)P6ActionId.RunMi, RunMiKills) }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Has.All.EqualTo(CastAt + P6Duration.RunMiLands).Within(TimelineRun.FrameLate), run.ToString);
    }

    private static TimelineRunResult RunMi(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            alphaOmega.RunMi(CastAt, limitBreaks);
            timeline(world, alphaOmega, limitBreaks);
        }, options ?? new TimelineRunOptions { PlayerRole = OffTank });
}
