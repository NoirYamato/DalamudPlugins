using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Each block, on its own clock, never keeps more helpers busy at once than the zone's pool will hold
// (P6Rules.HelperPool), so no cast is ever dropped. Godmode, everyone at the centre, so every block plays to its end.
// The busiest moment is Unlimited Wave Cannon's: two puddle sets of eight, the exaflares' 12 s casts and their later
// explosions.
public class TopP6HelperBudgetTests
{
    private static readonly float[] Steps = [1.1f, 1.0f];
    private static readonly int[] Points = Enumerable.Range(0, 8).ToArray();

    [Test]
    public void ExasquaresIntoCosmoDive([Values] bool inFirst)
        => AssertWithinThePool(alphaOmega =>
        {
            alphaOmega.CosmoMemory(1.9f);
            alphaOmega.Autos(12.0f);
            alphaOmega.CosmoArrow(new TopP6Exasquares(inFirst, 16.3f));
            alphaOmega.CosmoDive(31.4f);
            alphaOmega.Autos(46.2f);
        });

    // Wave Cannon's eight lasers come from helpers too, four at a time.
    [Test]
    public void ExaflaresIntoWaveCannon(
        [ValueSource(nameof(Points))] int first, [Values] bool clockwise, [ValueSource(nameof(Steps))] float step)
        => AssertWithinThePool(alphaOmega =>
        {
            alphaOmega.UnlimitedWaveCannon(new TopP6UnlimitedWaveCannon(first, clockwise, 1.9f, step));
            alphaOmega.WaveCannon(24.5f, Enum.GetValues<PartyRole>(), PartyRole.MainTank);
            alphaOmega.Autos(39.9f);
        });

    // Its baits, spreads, flares and stack all come from helpers.
    [Test]
    public void CosmoMeteor()
        => AssertWithinThePool(alphaOmega => alphaOmega.CosmoMeteor(1.9f, new TopP6CosmoMeteorRolls(
            [[PartyRole.MainTank, PartyRole.OffTank, PartyRole.RegenHealer, PartyRole.ShieldHealer], [PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps]],
            [PartyRole.CasterDps, PartyRole.MeleeDpsB, PartyRole.MainTank], PartyRole.ShieldHealer)));

    [Test]
    public void ExaflaresIntoCosmoDive(
        [ValueSource(nameof(Points))] int first, [Values] bool clockwise, [ValueSource(nameof(Steps))] float step)
        => AssertWithinThePool(alphaOmega =>
        {
            alphaOmega.UnlimitedWaveCannon(new TopP6UnlimitedWaveCannon(first, clockwise, 1.9f, step));
            alphaOmega.CosmoDive(19.6f);
            alphaOmega.Autos(34.4f);
        });

    private static void AssertWithinThePool(Action<TopP6AlphaOmega> block) => AssertWithinThePool((alphaOmega, _) => block(alphaOmega));

    private static void AssertWithinThePool(Action<TopP6AlphaOmega, SimWorld> block)
    {
        TopP6AlphaOmega? alphaOmega = null;
        var run = TimelineRun.Execute(world =>
        {
            alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            block(alphaOmega, world);
        }, new TimelineRunOptions { GodMode = true });
        TestContext.Out.WriteLine($"busiest: {alphaOmega!.BusyHelperPeak} helpers at once");

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Warnings, Has.None.Contains("dropped"));
        Assert.That(alphaOmega.BusyHelperPeak, Is.LessThan(P6Rules.HelperPool));
    }
}
