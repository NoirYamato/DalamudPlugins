using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1DeathsPullLogsTests
{
    // An auto-attack lands first and leaves hit points; the cleave's own effect result shows 0.
    [Test]
    public void ADeathIsBlamedOnTheHitWhoseEffectResultLeftNoHitPoints()
    {
        var killed = Findings(new TeaP1DeathsPullLogs(), Of(
        [
            Hit(55.8, Liquid, TeaLogIds.LiquidAutoAttack, Player(1), sequence: 8), EffectResult(56.3, Player(1), 8, 9000, Centre),
            Hit(55.9, Liquid, P1ActionId.FluidSwing, Player(1), sequence: 9), EffectResult(56.4, Player(1), 9, 0, Centre),
            Hit(56.0, Hand, TeaLogIds.HandAutoAttack, Player(1), sequence: 10),
        ])).Named("What killed");

        Assert.Multiple(() =>
        {
            Assert.That(killed.Measured, Is.EqualTo("Action (18864) from the Living Liquid x1"));
            Assert.That(killed.Details.Single(), Does.StartWith("Network_test.log pull 1 at 56.40 s: Gunbreaker dies to Action (18864) from the Living Liquid"));
        });
    }

    [Test]
    public void ADeathWithNoHitOfTheFightsBeforeItSaysSo()
    {
        var killed = Findings(new TeaP1DeathsPullLogs(), Of([UpdateHp(138.1, Player(1), 0, Centre)])).Named("What killed");

        Assert.That(killed.Measured, Is.EqualTo("no hit of the fight's x1"));
    }
}
