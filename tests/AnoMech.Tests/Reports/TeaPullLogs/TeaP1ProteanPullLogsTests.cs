using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1ProteanPullLogsTests
{
    private static readonly Vector2 East = new(110f, 100f), West = new(90f, 100f);

    // Two players, the Liquid turned to the one in the east: its own cone goes east and one helper's goes west.
    [Test]
    public void TheTelegraphedCastHasOneConeForEachLivingPlayer()
    {
        var findings = Findings(new TeaP1ProteanPullLogs(), Of(
        [
            StatusList(57, Player(1), 40000, East), StatusList(57, Player(2), 40000, West),
            Hit(58, Liquid, TeaLogIds.LiquidAutoAttack, Player(1)),
            Cast(60, Liquid, P1ActionId.ProteanWaveFromTheLiquid, 2.7f), CastAt(60, Liquid, P1ActionId.ProteanWaveFromTheLiquid, Centre, MathF.PI / 2),
            Cast(60, Helper, P1ActionId.ProteanWaveAtAPlayer, 2.7f), CastAt(60, Helper, P1ActionId.ProteanWaveAtAPlayer, Centre, -MathF.PI / 2),
        ], [LiquidStands(x: 100f, y: 100f), HelperStands()], players: 2));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Named("The Liquid's telegraphed cast: how many cones").Measured, Does.StartWith("1 of 1 casts have as many cones as living players"));
            Assert.That(findings.Named("The Liquid's telegraphed cast: how many cones").Verdict, Is.EqualTo(Verdict.Matches));
            Assert.That(findings.Named("The Liquid's telegraphed cast: whom").Measured, Does.StartWith("in 1 of 1 casts the one player no helper's cone fits"));
        });
    }

    // The first seat takes a cleave and then a tornado's wave; the second takes that wave alone.
    [Test]
    public void AWaveAfterACleaveIsALaterWave()
    {
        var waves = Findings(new TeaP1ProteanPullLogs(), Of(
        [
            StatusList(30, Player(1), 40000, East), StatusList(30, Player(2), 30000, West),
            Hit(36.8, Liquid, P1ActionId.FluidSwing, Player(1), sequence: 1), EffectResult(37.3, Player(1), 1, 20000, East),
            Hit(37.9, Helper, P1ActionId.TornadoWaveUnseen, Player(1), sequence: 2), EffectResult(38.4, Player(1), 2, 0, East),
            Hit(37.9, OtherHelper, P1ActionId.TornadoWaveUnseen, Player(2), sequence: 3), EffectResult(38.4, Player(2), 3, 15000, West),
        ], players: 2)).Named("A wave on a player who took a wave or a cleave");

        Assert.Multiple(() =>
        {
            Assert.That(waves.Measured, Does.StartWith("1 of 1 later waves killed; 0 of 1 first waves did"));
            Assert.That(waves.Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    [Test]
    public void TheDebuffIsTheStatusAWavesOwnEffectsName()
    {
        var findings = Findings(new TeaP1ProteanPullLogs(), Of(
        [
            Hit(37.9, Helper, P1ActionId.TornadoWaveUnseen, Player(1), applies: 1025), Gains(37.9, 1025, 3.96f, Helper, Player(1)),
            Gains(38.0, TeaLogIds.LuminousAetheroplasm, 4.96f, Helper, Player(1)),
        ], [LiquidStands(), HelperStands()]));

        Assert.Multiple(() =>
        {
            Assert.That((findings.Named("The status a wave leaves").Measured, findings.Named("The status a wave leaves").Verdict),
                        Is.EqualTo(("1025, which 1 wave hits name among their own effects", Verdict.Matches)));
            Assert.That(findings.Named("The wave debuff's seconds").Measured, Is.EqualTo("given for 3.96 s (n=1)"));
        });
    }

    // Each of the two cones takes the player nearest its axis, and one player takes one cone.
    [Test]
    public void ConesAreMatchedToThePlayersNearestTheirAxes()
    {
        var assigned = TeaP1ProteanPullLogs.Assign([1.5, 1.6], new Dictionary<uint, double> { [Player(1)] = 1.58, [Player(2)] = 1.45, [Player(3)] = -1.5 });

        Assert.That(assigned.Select(a => a.Player), Is.EquivalentTo(new[] { Player(1), Player(2) }));
    }
}
