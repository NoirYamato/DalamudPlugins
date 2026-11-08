using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// The gauge P6's limit breaks share: three bars; a limit break spends its level's bars as it resolves, and gives them
// back at once if its user holds Brilliant Dynamis, which then becomes Spark of Dynamis and gives nothing back again.
// The party's own generation runs only where Rebuild says. The human (the regen healer, a WHM, unless a test says
// otherwise) presses through their client.
public class TopP6LimitBreakEconomyTests
{
    private static uint LimitBreak(PartyRole seat, int level) => LimitBreakHandler.ActionId(PartyPresets.Standard[(int)seat].ClassJob, level);

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void AFirstPressWithBrilliantDynamisGivesItsBarsBackAndBecomesSpark()
    {
        var after = (Gauge: -1f, Brilliant: true, Spark: false, Hud: -1);
        var run = TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                Healer(world).AddStatus(StatusId.BrilliantDynamis);
            });
            TopP6HumanLimitBreak.Press(world, 1f, LimitBreak(RegenHealer, 3));
            world.Events.Add(3.5f, () => after = (limitBreaks.Gauge, Healer(world).HasStatus(StatusId.BrilliantDynamis),
                                                  Healer(world).HasStatus(P6StatusId.SparkOfDynamis), world.Party.LimitBreak.FilledBars));
        }, Human);

        Assert.That(run.Passed, run.ToString);
        Assert.That(after, Is.EqualTo((3f, false, true, 3)));
    }

    [Test]
    public void ASecondPressGivesNothingBack()
    {
        var gauge = new List<float>();
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                Healer(world).AddStatus(StatusId.BrilliantDynamis);
            });
            TopP6HumanLimitBreak.Press(world, 1f, LimitBreak(RegenHealer, 3));
            world.Events.Add(3.5f, () => gauge.Add(limitBreaks.Gauge));
            TopP6HumanLimitBreak.Press(world, 4f, LimitBreak(RegenHealer, 3));
            world.Events.Add(6.5f, () => gauge.Add(limitBreaks.Gauge));
        }, Human);

        Assert.That(gauge, Is.EqualTo(new[] { 3f, 0f }));
    }

    [Test]
    public void WithoutBrilliantDynamisAPressSpendsItsBars()
    {
        var gauge = -1f;
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            TopP6HumanLimitBreak.Press(world, 1f, LimitBreak(RegenHealer, 3));
            world.Events.Add(3.5f, () => gauge = limitBreaks.Gauge);
        }, Human);

        Assert.That(gauge, Is.EqualTo(0f));
    }

    // Healing Wind and Breath of the Earth spend one bar and two.
    [TestCase(1, false, 2f)]
    [TestCase(2, false, 1f)]
    [TestCase(1, true, 3f)]
    [TestCase(2, true, 3f)]
    public void ALowerLevelSpendsItsOwnBars(int level, bool brilliant, float left)
    {
        var after = (Gauge: -1f, Spark: false);
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                if (brilliant) Healer(world).AddStatus(StatusId.BrilliantDynamis);
            });
            TopP6HumanLimitBreak.Press(world, 1f, LimitBreak(RegenHealer, level));
            world.Events.Add(4.5f, () => after = (limitBreaks.Gauge, Healer(world).HasStatus(P6StatusId.SparkOfDynamis)));
        }, Human);

        Assert.That(after, Is.EqualTo((left, brilliant)));
    }

    [Test]
    public void DeathTakesBrilliantAndSparkOfDynamis()
    {
        var held = new List<bool>();
        TopP6LimitBreakRun.Execute((world, _, _) =>
        {
            world.Events.Add(0.5f, () =>
            {
                world.Party.Get(MainTank)!.AddStatus(StatusId.BrilliantDynamis);
                world.Party.Get(OffTank)!.AddStatus(P6StatusId.SparkOfDynamis);
            });
            world.Events.Add(1f, () =>
            {
                world.Party.Get(MainTank)!.Die(SimCharacterDeathExtensions.Environment, "test");
                world.Party.Get(OffTank)!.Die(SimCharacterDeathExtensions.Environment, "test");
            });
            world.Events.Add(1.5f, () => held.AddRange([
                world.Party.Get(MainTank)!.HasStatus(StatusId.BrilliantDynamis),
                world.Party.Get(OffTank)!.HasStatus(P6StatusId.SparkOfDynamis)]));
        });

        Assert.That(held, Is.EqualTo(new[] { false, false }));
    }

    // The human's second press spends the bars its Brilliant Dynamis already gave back once, so the main tank's bot
    // waits for a gauge that never fills, and Magic Number kills everyone, saying why.
    [Test]
    public void AWrongPressLeavesTheNextDutyWithoutAGauge()
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                alphaOmega.ApplyCosmoMemoryStatuses();
            });
            TopP6HumanLimitBreak.Press(world, 1f, LimitBreak(RegenHealer, 3));
            TopP6HumanLimitBreak.Press(world, 4f, LimitBreak(RegenHealer, 3));
            alphaOmega.MagicNumber(7f, limitBreaks);
            world.Events.Add(7.7f, () => limitBreaks.Press(MainTank, PartyPresets.Standard[(int)MainTank].ClassJob));
        }, Human);

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.ActionId), Has.All.EqualTo(P6ActionId.MagicNumber), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(TopP6LimitBreaks.GaugeWasShort.TrimStart(';', ' ')), run.ToString);
    }

    // Evenly from 2 to 12, then nothing: half the gauge by 7, all of it by 12, and none back after a press at 13.
    [Test]
    public void TheGaugeRebuildsEvenlyThenStops()
    {
        var gauge = new List<float>();
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(0f);
                limitBreaks.Rebuild(2f, 12f);
            });
            foreach (var at in new[] { 1.5f, 7f, 12f })
                world.Events.Add(at, () => gauge.Add(limitBreaks.Gauge));
            TopP6HumanLimitBreak.Press(world, 13f, LimitBreak(RegenHealer, 3));
            world.Events.Add(20f, () => gauge.Add(limitBreaks.Gauge));
        }, Human);

        Assert.That(gauge, Is.EqualTo(new[] { 0f, 1.5f, 3f, 0f }).Within(0.01f));
    }

    // A bar spent while the gauge rebuilds is a bar short once it stops.
    [Test]
    public void ASpendWhileItRebuildsLeavesItShort()
    {
        var gauge = -1f;
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(0f);
                limitBreaks.Rebuild(1f, 11f);
            });
            TopP6HumanLimitBreak.Press(world, 6f, LimitBreak(RegenHealer, 1));
            world.Events.Add(15f, () => gauge = limitBreaks.Gauge);
        }, Human);

        Assert.That(gauge, Is.EqualTo(2f).Within(0.01f));
    }

    // A run starting 41 s into a 52.2 s rebuild begins with what it has made by then, and is exactly full when it ends:
    // a bot waiting for a full gauge presses then.
    [Test]
    public void ALaterStartBeginsWithWhatTheRebuildHasMade()
    {
        const float from = -41f, fullBy = 11.2f;
        var pressedAt = -1f;
        var start = TopP6LimitBreaks.RebuiltBars(0f, from, fullBy);
        TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(TopP6LimitBreaks.RebuiltBars(0.5f, from, fullBy));
                limitBreaks.Rebuild(from, fullBy);
                limitBreaks.Press(MeleeDpsA, PartyPresets.Standard[(int)MeleeDpsA].ClassJob, alphaOmega.Enemy);
            });
            limitBreaks.Resolved += press => pressedAt = press.CastStartedAt;
            world.Events.Add(18f, () => { });
        });

        Assert.That(start, Is.EqualTo(3f * 41f / 52.2f).Within(0.001f));
        Assert.That(pressedAt, Is.EqualTo(fullBy).Within(2 * ScenarioRun.FrameSeconds));
    }

    [Test]
    public void TheHudShowsTheGauge()
    {
        var bars = new List<int>();
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(0f);
                limitBreaks.Rebuild(1f, 4f);
            });
            foreach (var at in new[] { 1.5f, 2.5f, 3.5f, 4.5f })
                world.Events.Add(at, () => bars.Add(world.Party.LimitBreak.FilledBars));
        });

        Assert.That(bars, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    private static readonly TimelineRunOptions Human = new() { PlayerRole = RegenHealer, HumanPlayer = true, UserActionsEnabled = true };

    private static SimCharacter Healer(SimWorld world) => world.Party.Get(RegenHealer)!;
}
