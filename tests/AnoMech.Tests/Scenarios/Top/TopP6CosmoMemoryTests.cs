using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Cosmo Memory cast at 1.9: its ActionEffect at 7.9 gives everyone Brilliant Dynamis and Alpha Omega Code: ****mi*. A run
// starting after it gets the same statuses, but for the seats whose limit break has spent Brilliant Dynamis by then: they
// carry Spark of Dynamis instead.
public class TopP6CosmoMemoryTests
{
    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void ItsHitLeavesBrilliantDynamisOnEveryoneAndCodeMiOnAlphaOmega()
    {
        var before = new List<bool>();
        var after = new List<bool>();
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            alphaOmega.CosmoMemory(1.9f, limitBreaks);
            world.Events.Add(4.9f, () => limitBreaks.Press(MainTank, PartyPresets.Standard[(int)MainTank].ClassJob));
            world.Events.Add(7.5f, () => before.AddRange(Statuses(world, alphaOmega)));
            world.Events.Add(8.5f, () => after.AddRange(Statuses(world, alphaOmega)));
        }, new TimelineRunOptions { PlayerRole = OffTank });

        Assert.That(run.Passed, run.ToString);
        Assert.That(before, Has.Count.EqualTo(9).And.All.False);
        Assert.That(after, Has.Count.EqualTo(9).And.All.True);
    }

    // The gauge: full from the cast, in the run and on the HUD, spent by the main tank's press, then rebuilding from the
    // hit.
    [Test]
    public void ItFillsTheGaugeAndStartsItsRebuild()
    {
        const float CastAt = 1.9f, Decides = 7.6f;
        var gauge = new Dictionary<float, float>();
        var hudBars = -1;
        var (from, fullBy) = TopP6AlphaOmega.GaugeRebuildAfterCosmoMemory(CastAt);
        TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            alphaOmega.CosmoMemory(CastAt, limitBreaks);
            world.Events.Add(1.0f, () => limitBreaks.SetGauge(0f));
            world.Events.Add(4.9f, () => limitBreaks.Press(MainTank, PartyPresets.Standard[(int)MainTank].ClassJob));
            world.Events.Add(2.0f, () => hudBars = world.Party.LimitBreak.FilledBars);
            foreach (var at in new[] { 2.0f, 5.0f, 7.5f, 20f })
                world.Events.Add(at, () => gauge[at] = limitBreaks.Gauge);
        }, new TimelineRunOptions { PlayerRole = OffTank });

        Assert.That(from, Is.EqualTo(Decides).Within(0.001f));
        Assert.That(fullBy, Is.EqualTo(Decides + P6Duration.GaugeRebuild).Within(0.001f));
        Assert.That(gauge[2.0f], Is.EqualTo(3f));
        Assert.That(hudBars, Is.EqualTo(3), "the HUD's filled bars");
        Assert.That(gauge[5.0f], Is.EqualTo(0f));
        Assert.That(gauge[7.5f], Is.EqualTo(0f));
        Assert.That(gauge[20f], Is.EqualTo(TopP6LimitBreaks.RebuiltBars(20f, from, fullBy)).Within(0.01f));
    }

    [Test]
    public void ARunStartingAfterItGetsTheSameStatuses()
    {
        var statuses = new List<bool>();
        TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(1f, () => alphaOmega.ApplyCosmoMemoryStatuses());
            world.Events.Add(1.5f, () => statuses.AddRange(Statuses(world, alphaOmega)));
        });

        Assert.That(statuses, Has.Count.EqualTo(9).And.All.True);
    }

    [Test]
    public void TheSeatsWhoseLimitBreakSpentItCarrySparkOfDynamisInstead()
    {
        PartyRole[] sparked = [MeleeDpsA, MeleeDpsB, CasterDps];
        var brilliant = new List<PartyRole>();
        var spark = new List<PartyRole>();
        var bossMarked = false;
        TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(1f, () => alphaOmega.ApplyCosmoMemoryStatuses(sparked));
            world.Events.Add(1.5f, () =>
            {
                brilliant.AddRange(Enum.GetValues<PartyRole>().Where(r => world.Party.Get(r)!.HasStatus(StatusId.BrilliantDynamis)));
                spark.AddRange(Enum.GetValues<PartyRole>().Where(r => world.Party.Get(r)!.HasStatus(P6StatusId.SparkOfDynamis)));
                bossMarked = alphaOmega.Enemy!.HasStatus(StatusId.CodeMi);
            });
        });

        Assert.That(spark, Is.EquivalentTo(sparked));
        Assert.That(brilliant, Is.EquivalentTo(Enum.GetValues<PartyRole>().Except(sparked)));
        Assert.That(bossMarked, Is.True);
    }

    // Everyone's Brilliant Dynamis, then Alpha Omega's Code: ****mi*.
    private static IEnumerable<bool> Statuses(SimWorld world, TopP6AlphaOmega alphaOmega)
        => Enum.GetValues<PartyRole>().Select(role => world.Party.Get(role)!.HasStatus(StatusId.BrilliantDynamis))
               .Append(alphaOmega.Enemy!.HasStatus(StatusId.CodeMi));
}
