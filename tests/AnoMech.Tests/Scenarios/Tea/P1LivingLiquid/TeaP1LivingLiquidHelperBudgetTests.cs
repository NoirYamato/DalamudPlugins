using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// Every wave and Sluice comes from one pool of invisible helpers, and a cast that finds none free is dropped with
// only a warning. The pull holds three at once, for the tornadoes' telegraphs. The telegraphed waves hold seven at once, and the first set with its Sluices eight. Cascade 2 holds the most: its Sluices land in the moment
// its second set fires, while the tornadoes' three telegraphs are still casting.
public class TeaP1LivingLiquidHelperBudgetTests
{
    [TestCase(Protean1, Protean1, 8)]
    [TestCase(Cascade2, Cascade2, 11)]
    [TestCase(Protean1, Cascade2, 11)]
    [TestCase(Pull, Pull, 3)]
    [TestCase(Pull, Cascade2, 11)]
    public void TheHelperPoolIsNeverShort(TeaP1LivingLiquidPart first, TeaP1LivingLiquidPart last, int expectedPeak)
    {
        var stage = new Stage { First = first, Last = last };
        var peak = -1;
        var run = stage.Run(MainTank, probe: p => peak = Stage.Fight(p).BusyHelperPeak);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Warnings, Has.None.Contain("helpers busy"), run.ToString);
        Assert.That(peak, Is.EqualTo(expectedPeak));
        Assert.That(peak, Is.LessThan(P1Rules.LivingLiquidHelpers));
    }

    [Test]
    public void TheMostAreBusyAsCascadeTwosSluicesLandWithItsSecondSet()
    {
        var peaks = new List<int>();
        var run = new Stage { First = Cascade2 }.Run(MainTank, probe: p =>
        {
            foreach (var at in new[] { 18.05f, 18.25f, 23.4f, 24.3f, 26.2f, 26.4f })
                if (p.Crossed(at)) peaks.Add(Stage.Fight(p).BusyHelperPeak);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(peaks, Is.EqualTo(new[] { 0, 7, 8, 8, 8, 11 }));
    }
}
