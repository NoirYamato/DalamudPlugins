using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;
using AnoMech.Multiplayer;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6LimitBreakDuty;
using static AnoMech.Scenarios.Top.TopP6Constants;
using static AnoMech.Tests.TopP6GauntletHumanPress;

namespace AnoMech.Tests;

// Cosmo Meteor's limit breaks 3 with the human in R2, playing each caster job, or in R1, each physical ranged job. On the
// Gauntlet's clock (G), the adds show at 174.9 (UNVERIFIED): six Cosmo Comets and two Cosmo Meteors, north and south.
//   178.3 / 187.0  R2's caster limit break 3, put down on Alpha Omega, kills every comet its circle holds; the comets
//                  enrage at 187.0 and kill everyone if one lives;
//   188.8 / 197.1  R1's physical ranged limit break 3 from A, aimed at Alpha Omega, kills each meteor its line holds; the
//                  meteors enrage at 197.1.
// A press with Brilliant Dynamis gives the gauge back, one with Spark of Dynamis doesn't, and nothing else refills it
// after Cosmo Dive 2: a wasted press leaves the next limit break waiting, and that one's mechanic kills everyone, saying so.
public class TopP6GauntletLimitBreakRangedTests
{
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

    private static string CometsEnraged(string why = "") => $"Cosmo Meteor ({TopP6CosmoMeteor.CometsEnraged}{why})";
    private static string MeteorsEnraged(string why = "") => $"Cosmo Meteor ({TopP6CosmoMeteor.MeteorsEnraged}{why})";

    private static TopP6GauntletHumanSeat FromCosmoMeteor(PartyRole seat, JobId job)
        => new TopP6GauntletHumanSeat(seat, job).From(TopP6GauntletPart.CosmoMeteor);

    [TestCase(PhysRangedDps, JobId.Bard, Meteors)]
    [TestCase(PhysRangedDps, JobId.Machinist, Meteors)]
    [TestCase(PhysRangedDps, JobId.Dancer, Meteors)]
    [TestCase(CasterDps, JobId.BlackMage, Comets)]
    [TestCase(CasterDps, JobId.Summoner, Comets)]
    [TestCase(CasterDps, JobId.RedMage, Comets)]
    [TestCase(CasterDps, JobId.Pictomancer, Comets)]
    public void EachRangedJobPressingItsLimitBreakClearsTheGauntlet(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
        => new TopP6GauntletHumanSeat(seat, job).Pressing([duty]).AssertEveryDutyPressedOnceByItsSeat([duty]);

    // ---- Late, never, cut off or too weak

    [Test]
    public void ACasterLimitBreak3LandingAfterTheCometsEnrageIsTooLate()
        => FromCosmoMeteor(CasterDps, JobId.Pictomancer).Play(For(Comets, 183.0f)).AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged());

    [Test]
    public void APhysRangedLimitBreak3LandingAfterTheMeteorsEnrageIsTooLate()
        => FromCosmoMeteor(PhysRangedDps, JobId.Dancer).Play(For(Meteors, 193.0f)).AssertEveryoneDied(197.1f, P6ActionId.CosmoMeteorEnrage, MeteorsEnraged());

    [Test]
    public void ACasterWhoNeverPressesLetsTheCometsEnrage()
        => FromCosmoMeteor(CasterDps, JobId.Summoner).Play().AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged());

    [Test]
    public void APhysRangedWhoNeverPressesLetsTheMeteorsEnrage()
        => FromCosmoMeteor(PhysRangedDps, JobId.Machinist).Play().AssertEveryoneDied(197.1f, P6ActionId.CosmoMeteorEnrage, MeteorsEnraged());

    [Test]
    public void ACasterLimitBreak3CutOffKillsNothing()
        => FromCosmoMeteor(CasterDps, JobId.RedMage).Play(For(Comets, interruptedAfter: 2f))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged());

    [Test]
    public void APhysRangedLimitBreak3CutOffKillsNothing()
        => FromCosmoMeteor(PhysRangedDps, JobId.Bard).Play(For(Meteors, interruptedAfter: 1f))
            .AssertEveryoneDied(197.1f, P6ActionId.CosmoMeteorEnrage, MeteorsEnraged());

    // Landing at 174.5, before the comets show, it finds nothing to kill.
    [Test]
    public void ACasterLimitBreak3BeforeTheCometsShowKillsNothing()
        => FromCosmoMeteor(CasterDps, JobId.BlackMage).Play(For(Comets, 170.0f))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged());

    [Test]
    public void ACasterLimitBreak2KillsNoComet()
        => FromCosmoMeteor(CasterDps, JobId.RedMage).Play(new TopP6GauntletHumanPress(178.3f, Level: 2, OnAlphaOmega: true))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged());

    // From its clock spot, R2 puts its circle 8 y north of Alpha Omega: the comets at 135 and 225 live and enrage.
    [Test]
    public void ACasterLimitBreak3PutDownAwayFromAlphaOmegaLetsTheCometsItMissesEnrage()
        => FromCosmoMeteor(CasterDps, JobId.BlackMage).Play(For(Comets) with { GroundPoint = new Vector3(0f, 0f, -8f) })
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged(string.Format(TopP6CosmoMeteor.YoursCaught, 4)));

    [Test]
    public void APhysRangedLimitBreak2KillsNoMeteor()
        => FromCosmoMeteor(PhysRangedDps, JobId.Bard).Play(new TopP6GauntletHumanPress(188.8f, Level: 2, OnAlphaOmega: true))
            .AssertEveryoneDied(197.1f, P6ActionId.CosmoMeteorEnrage, MeteorsEnraged());

    // ---- A wasted press leaves the next one without a gauge

    // The black mage presses before the comets show, spending its Brilliant Dynamis, then again on them: the comets die,
    // but the gauge is empty for R1's.
    [Test]
    public void ACasterPressingTwiceLeavesThePhysRangedWithoutAGauge()
        => FromCosmoMeteor(CasterDps, JobId.BlackMage).Play(For(Comets, 170.0f), For(Comets))
            .AssertEveryoneDied(197.1f, P6ActionId.CosmoMeteorEnrage, MeteorsEnraged(TopP6LimitBreaks.GaugeWasShort));

    // A limit break 1 the red mage wastes as the adds show spends its Brilliant Dynamis: its limit break 3 on the comets
    // then empties the gauge.
    [Test]
    public void AWastedLimitBreak1LeavesThePhysRangedWithoutAGauge()
        => FromCosmoMeteor(CasterDps, JobId.RedMage).Play(new TopP6GauntletHumanPress(175.0f, Level: 1, OnAlphaOmega: true), For(Comets))
            .AssertEveryoneDied(197.1f, P6ActionId.CosmoMeteorEnrage, MeteorsEnraged(TopP6LimitBreaks.GaugeWasShort));

    // The summoner presses again as R1's turn comes, ahead of it: a caster's limit break kills no meteor, and R1's waits.
    [Test]
    public void ACasterPressingThePhysRangedsTurnLeavesItWithoutAGauge()
        => FromCosmoMeteor(CasterDps, JobId.Summoner).Play(For(Comets), For(Comets, 188.0f))
            .AssertEveryoneDied(197.1f, P6ActionId.CosmoMeteorEnrage, MeteorsEnraged(TopP6LimitBreaks.GaugeWasShort));

    // The bard presses as R2's turn comes (178.3), just ahead of it: its line kills the meteors, but R2's waits for it to
    // land and then lands itself after the comets have enraged.
    [Test]
    public void APhysRangedPressingTheCastersTurnMakesItLandTooLate()
        => FromCosmoMeteor(PhysRangedDps, JobId.Bard).Play(For(Meteors, 178.2f))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged());

    // The bard presses through the meteors twice: Magic Number 1's tank finds the gauge empty, and the hit kills everyone.
    [Test]
    public void APhysRangedPressingTwiceLeavesMagicNumber1WithoutAGauge()
        => new TopP6GauntletHumanSeat(PhysRangedDps, JobId.Bard).From(TopP6GauntletPart.CosmoMeteor, TopP6GauntletPart.MagicNumber)
            .Play(For(Meteors), For(Meteors, 193.5f))
            .AssertEveryoneDied(206.5f, P6ActionId.MagicNumber,
                $"Magic Number ({TopP6AlphaOmega.MagicNumberNeedsATankLimitBreak3}{TopP6LimitBreaks.GaugeWasShort})");

    // A machinist pressing as soon as the adds show, from its clock spot north, already lines up both meteors through Alpha
    // Omega: they die early, and the run plays on.
    [Test]
    public void APhysRangedLimitBreak3AsTheAddsShowKillsTheMeteorsEarly()
        => FromCosmoMeteor(PhysRangedDps, JobId.Machinist).Play(For(Meteors, 175.5f)).AssertClears();

    // ---- A job that can't press the seat's

    // A summoner in R1 and a machinist in R2: the run presses the seat's bot's, and the adds die to it.
    [TestCase(PhysRangedDps, JobId.Summoner, JobId.Bard)]
    [TestCase(CasterDps, JobId.Machinist, JobId.BlackMage)]
    public void TheRunPressesForAHumanWhoseJobCantPressTheSeatsLimitBreak(PartyRole seat, JobId job, JobId pressedAs)
    {
        var run = FromCosmoMeteor(seat, job).Play();

        run.AssertClears();
        Assert.Multiple(() =>
        {
            Assert.That(run.Plan.HumanFits, Is.False);
            Assert.That(run.Presses.Where(p => p.Seat == seat).Select(p => (p.ByHuman, p.Job, p.ActionId)),
                        Is.EqualTo(new[] { (false, (uint)pressedAs, LimitBreakHandler.ActionId((uint)pressedAs, 3)) }));
        });
    }

    // The run's press for a summoner in R1 reaches both meteors wherever the summoner stands: here east of Alpha Omega
    // while it casts and lands (G 188.8 to 193.3), where a line aimed at Alpha Omega would pass between them. Then back to
    // A for the flares.
    [Test]
    public void TheRunsPressForAHumanWhoseJobCantPressItReachesBothMeteorsFromAnywhere()
    {
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = PhysRangedDps,
            WriteArtifactsOnFailure = false,
            Overrides = o =>
            {
                ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)JobId.Summoner;
                var overrides = (TopP6GauntletStateOverrides)o;
                TopP6GauntletRun.Only(overrides, TopP6GauntletPart.CosmoMeteor);
                overrides.Flares = [CasterDps, MeleeDpsB, MainTank];
                overrides.StackTarget = ShieldHealer;
            },
            Probe = p =>
            {
                if (p.Crossed(23.0f)) TimelineRun.Place(p.World, PhysRangedDps, 13f, 0f);
                if (p.Crossed(29.0f)) TimelineRun.Place(p.World, PhysRangedDps, 0f, -13f);
            },
        });

        Assert.That(run.Passed, run.ToString);
    }

    // Godmode deals no deaths: each player is reported once as the comets enrage.
    [Test]
    public void GodmodeReportsEachPlayerOncePerHit()
    {
        var run = (FromCosmoMeteor(CasterDps, JobId.BlackMage) with { GodMode = true }).Play();

        Assert.That(run.Run.Deaths, Is.Empty, run.Run.ToString);
        run.AssertReportedOncePerHit((187.0f, 8, CometsEnraged()));
    }
}
