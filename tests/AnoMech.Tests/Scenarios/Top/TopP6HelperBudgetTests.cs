using AnoMech.Core.Game.Party;
using AnoMech.Multiplayer;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Each part, on its own clock, never asks the zone's helper pool for more helpers at once than it holds, so no cast is
// ever dropped: a run fails on the pool's HelperPoolExhausted. Godmode, everyone at the centre, so every part plays to
// its end. The busiest moment is Unlimited Wave Cannon's: two puddle sets of eight, the exaflares' 12 s casts and their
// later explosions. The whole Gauntlet shares the one pool across its parts.
public class TopP6HelperBudgetTests
{
    private static readonly float[] Steps = [1.1f, 1.0f];
    private static readonly int[] Points = Enumerable.Range(0, 8).ToArray();

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    [Test]
    public void ExasquaresIntoCosmoDive([Values] bool inFirst)
        => AssertWithinThePool((alphaOmega, limitBreaks) =>
        {
            alphaOmega.CosmoMemory(1.9f, limitBreaks);
            alphaOmega.Autos(12.0f);
            alphaOmega.CosmoArrow(new TopP6Exasquares(inFirst, 16.3f));
            alphaOmega.CosmoDive(31.4f);
            alphaOmega.Autos(46.2f);
        });

    // Wave Cannon's eight lasers come from helpers too, four at a time.
    [Test]
    public void ExaflaresIntoWaveCannon(
        [ValueSource(nameof(Points))] int first, [Values] bool clockwise, [ValueSource(nameof(Steps))] float step)
        => AssertWithinThePool((alphaOmega, _) =>
        {
            alphaOmega.UnlimitedWaveCannon(new TopP6UnlimitedWaveCannon(first, clockwise, 1.9f, step));
            alphaOmega.WaveCannon(24.5f, Enum.GetValues<PartyRole>(), PartyRole.MainTank);
            alphaOmega.Autos(39.9f);
        });

    // Its baits, spreads, flares and stack all come from helpers.
    [Test]
    public void CosmoMeteor()
        => AssertWithinThePool((alphaOmega, limitBreaks) => alphaOmega.CosmoMeteor(1.9f, new TopP6CosmoMeteorRolls(
            [[PartyRole.MainTank, PartyRole.OffTank, PartyRole.RegenHealer, PartyRole.ShieldHealer], [PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps]],
            [PartyRole.CasterDps, PartyRole.MeleeDpsB, PartyRole.MainTank], PartyRole.ShieldHealer), limitBreaks));

    [Test]
    public void ExaflaresIntoCosmoDive(
        [ValueSource(nameof(Points))] int first, [Values] bool clockwise, [ValueSource(nameof(Steps))] float step)
        => AssertWithinThePool((alphaOmega, _) =>
        {
            alphaOmega.UnlimitedWaveCannon(new TopP6UnlimitedWaveCannon(first, clockwise, 1.9f, step));
            alphaOmega.CosmoDive(19.6f);
            alphaOmega.Autos(34.4f);
        });

    // The whole phase on the LPDU strat, in godmode so every part plays: both waves on each point, turning both ways, at
    // either step, and the two Cosmo Arrows in opposite orders, so each run plays both.
    [Test]
    public void TheWholeGauntlet([ValueSource(nameof(Points))] int first, [Values] bool clockwise, [ValueSource(nameof(Steps))] float step)
    {
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            GodMode = true,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                overrides.Exaflares1First = overrides.Exaflares2First = first;
                overrides.Exaflares1Clockwise = overrides.Exaflares2Clockwise = clockwise;
                overrides.Exaflares1Step = overrides.Exaflares2Step = step;
                overrides.Arrow1InFirst = clockwise;
                overrides.Arrow2InFirst = !clockwise;
            },
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Warnings, Has.None.Contains("dropped"));
    }

    // A cast the pool has no free helper for drops, and the run fails on it.
    [Test]
    public void ACastThePoolCannotHoldFailsTheRun()
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, _) => world.Events.Add(1f, () =>
        {
            for (var i = 0; i <= TopReplayData.DutyInitData.HelperPool.Count; i++)
                alphaOmega.CastFromHelper(TopP6Actions.FlashGale, world.Party.Get(PartyRole.MainTank)!);
        }), new TimelineRunOptions { GodMode = true });

        Assert.That(run.Failure, Does.StartWith("helper pool exhausted at t=1.0"), run.ToString);
        Assert.That(run.Warnings, Has.Some.Contains("dropped"));
    }

    private static void AssertWithinThePool(Action<TopP6AlphaOmega, TopP6LimitBreaks> part)
    {
        var run = TopP6LimitBreakRun.Execute((_, boss, limitBreaks) => part(boss, limitBreaks), new TimelineRunOptions { GodMode = true });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Warnings, Has.None.Contains("dropped"));
    }
}
