using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1ActorsPullLogsTests
{
    [Test]
    public void TornadoesFifteenYalmsOutDifferFromARingOfFourteenAndAHalf()
    {
        var findings = Findings(new TeaP1ActorsPullLogs(), Of(Tornadoes(19.6)));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Named("The tornado ring's radius").Measured, Does.StartWith("15.00 y (n=3"));
            Assert.That(findings.Named("The tornado ring's radius").Verdict, Is.EqualTo(Verdict.Differs));
            Assert.That(findings.Named("The tornadoes' layout").Measured, Does.Contain("1 of 1 sets leave one quarter open: open east"));
        });
    }

    [Test]
    public void TheWaterIsTheObjectAddedOnATornadosSpot()
    {
        var water = Findings(new TeaP1ActorsPullLogs(), Of(
        [
            .. Tornadoes(19.6),
            Memory(19.6, "Add", 0x40000051, ("BNpcID", "1E9998"), ("PosX", "100.0000"), ("PosY", "85.0000")),
            Memory(19.6, "Add", 0x40000052, ("BNpcID", "1E9998"), ("PosX", "85.0000"), ("PosY", "100.0000")),
        ])).Named("A Liquid Rage's water");

        Assert.That((water.Measured, water.Verdict),
                    Is.EqualTo(("2 of 3 Liquid Rages have object 2005400 added on their spot as they appear", Verdict.Differs)));
    }

    [Test]
    public void TheLiquidsSpotBeforeThePullIsReadFromAPullLoggedFromItsStart()
    {
        var spot = Findings(new TeaP1ActorsPullLogs(), Of([]), Of([], [LiquidStands(LiquidMaxHp - 20000, 99.8f, 96f)])).Named("The Living Liquid before the pull");

        Assert.That(spot.Measured, Does.StartWith("(0.00, -15.00), facing"));
    }
}
