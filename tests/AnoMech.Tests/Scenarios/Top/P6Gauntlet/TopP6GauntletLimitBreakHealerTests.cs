using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Multiplayer;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6LimitBreakDuty;
using static AnoMech.Scenarios.Top.TopP6Constants;
using static AnoMech.Tests.TopP6GauntletHumanPress;

namespace AnoMech.Tests;

// The healers' limit breaks 3 with the human in either healer seat, playing each healer job beside the other seat's bot
// (H1 WHM, H2 SCH). Healers go by seat: H1 lifts Magic Number 1's mark, H2 Magic Number 2's, unless the human's healer row
// swaps them. On the Gauntlet's clock (G), each mark lands on everyone 1.3 s after its hit and kills whoever still carries
// it 6 s later (both UNVERIFIED); a healer's limit break 3, a 2 s cast, lifts it from all as it lands:
//   208.1 / 214.1  Magic Number 1's mark (the plan's healer presses at 208.8);
//   224.2 / 230.2  Magic Number 2's (at 224.9);
//   232.0 / 247.7  Run: ****mi*, which the enrage holder's melee limit break 3 ends; without it, its damage lands at 262.0.
// A press with Brilliant Dynamis gives the gauge back, one with Spark of Dynamis doesn't: a healer's wasted press leaves a
// later limit break waiting for a gauge that never fills, and that one's mechanic kills everyone, saying so.
public class TopP6GauntletLimitBreakHealerTests
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

    private static string TheMarkKills(string why = "") => $"Magic Number ({TopP6AlphaOmega.MagicNumberMarkRanOut}{why})";
    private static string MagicNumberKills(string why = "") => $"Magic Number ({TopP6AlphaOmega.MagicNumberNeedsATankLimitBreak3}{why})";
    private static string RunMiKills(string why = "") => $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded}{why})";

    private static float MarkRunsOut(TopP6LimitBreakDuty duty) => duty == MagicNumber1Healer ? 214.1f : 230.2f;

    private static TopP6GauntletHumanSeat FromMagicNumber(PartyRole seat, JobId job)
        => new TopP6GauntletHumanSeat(seat, job).From(TopP6GauntletPart.MagicNumber);

    [TestCase(RegenHealer, JobId.WhiteMage, MagicNumber1Healer)]
    [TestCase(RegenHealer, JobId.Scholar, MagicNumber1Healer)]
    [TestCase(RegenHealer, JobId.Astrologian, MagicNumber1Healer)]
    [TestCase(RegenHealer, JobId.Sage, MagicNumber1Healer)]
    [TestCase(ShieldHealer, JobId.WhiteMage, MagicNumber2Healer)]
    [TestCase(ShieldHealer, JobId.Scholar, MagicNumber2Healer)]
    [TestCase(ShieldHealer, JobId.Astrologian, MagicNumber2Healer)]
    [TestCase(ShieldHealer, JobId.Sage, MagicNumber2Healer)]
    public void EachHealerJobPressingItsLimitBreakClearsTheGauntlet(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
        => new TopP6GauntletHumanSeat(seat, job).Pressing([duty]).AssertEveryDutyPressedOnceByItsSeat([duty]);

    [TestCase(RegenHealer, JobId.Scholar, TopP6HealerLimitBreaks.MagicNumber2, MagicNumber2Healer)]
    [TestCase(ShieldHealer, JobId.Astrologian, TopP6HealerLimitBreaks.MagicNumber1, MagicNumber1Healer)]
    public void TheHealerRowsMagicNumberPressedClearsTheGauntlet(PartyRole seat, JobId job, TopP6HealerLimitBreaks row, TopP6LimitBreakDuty duty)
        => new TopP6GauntletHumanSeat(seat, job) { Healer = row }.Pressing([duty]).AssertEveryDutyPressedOnceByItsSeat([duty]);

    // Pressed with under 2 s of the mark left, it lands after the mark has killed.
    [TestCase(RegenHealer, JobId.Astrologian, MagicNumber1Healer, 212.5f)]
    [TestCase(ShieldHealer, JobId.Sage, MagicNumber2Healer, 228.5f)]
    public void AHealerLimitBreak3LandingAfterTheMarkRunsOutIsTooLate(PartyRole seat, JobId job, TopP6LimitBreakDuty duty, float pressAt)
        => FromMagicNumber(seat, job).Play(For(duty, pressAt)).AssertEveryoneDied(MarkRunsOut(duty), P6ActionId.MagicNumber, TheMarkKills());

    [TestCase(RegenHealer, JobId.Sage, MagicNumber1Healer)]
    [TestCase(ShieldHealer, JobId.Astrologian, MagicNumber2Healer)]
    public void AHealerWhoNeverPressesLetsTheMarkKillEveryone(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
        => FromMagicNumber(seat, job).Play().AssertEveryoneDied(MarkRunsOut(duty), P6ActionId.MagicNumber, TheMarkKills());

    // Landing before the mark does, it lifts nothing.
    [TestCase(RegenHealer, JobId.Scholar, MagicNumber1Healer, 205.5f)]
    [TestCase(ShieldHealer, JobId.WhiteMage, MagicNumber2Healer, 221.5f)]
    public void AHealerLimitBreak3LandingBeforeTheMarkLiftsNothing(PartyRole seat, JobId job, TopP6LimitBreakDuty duty, float pressAt)
        => FromMagicNumber(seat, job).Play(For(duty, pressAt)).AssertEveryoneDied(MarkRunsOut(duty), P6ActionId.MagicNumber, TheMarkKills());

    // A limit break 2 lifts nothing either.
    [TestCase(RegenHealer, JobId.Sage, MagicNumber1Healer)]
    [TestCase(ShieldHealer, JobId.WhiteMage, MagicNumber2Healer)]
    public void AHealerLimitBreak2LiftsNothing(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
        => FromMagicNumber(seat, job)
            .Play(new TopP6GauntletHumanPress(AimFor(duty), Level: 2))
            .AssertEveryoneDied(MarkRunsOut(duty), P6ActionId.MagicNumber, TheMarkKills());

    // Moving cancels the cast: unless the healer presses again in time, the mark kills everyone.
    [TestCase(RegenHealer, JobId.Scholar, MagicNumber1Healer)]
    [TestCase(ShieldHealer, JobId.WhiteMage, MagicNumber2Healer)]
    public void AnInterruptedHealerLimitBreak3LiftsNothing(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
        => FromMagicNumber(seat, job)
            .Play(For(duty, interruptedAfter: 1f))
            .AssertEveryoneDied(MarkRunsOut(duty), P6ActionId.MagicNumber, TheMarkKills());

    [Test]
    public void PressingAgainAfterAnInterruptionStillLifts()
        => FromMagicNumber(ShieldHealer, JobId.WhiteMage)
            .Play(For(MagicNumber2Healer, interruptedAfter: 1f), For(MagicNumber2Healer, 226.5f))
            .AssertClears();

    // The second press, with Spark of Dynamis, empties the gauge: Magic Number 2's tank waits for it, and the hit kills
    // everyone.
    [Test]
    public void PressingMagicNumber1TwiceLeavesTheNextTankWithoutAGauge()
        => FromMagicNumber(RegenHealer, JobId.WhiteMage)
            .Play(For(MagicNumber1Healer), For(MagicNumber1Healer, 211.0f))
            .AssertEveryoneDied(222.6f, P6ActionId.MagicNumber, MagicNumberKills(TopP6LimitBreaks.GaugeWasShort));

    // Pressing Magic Number 2 twice leaves the enrage holder's waiting: Run: ****mi* lands and kills everyone.
    [Test]
    public void PressingMagicNumber2TwiceLeavesTheEnrageWithoutAGauge()
        => FromMagicNumber(ShieldHealer, JobId.Scholar)
            .Play(For(MagicNumber2Healer), For(MagicNumber2Healer, 227.0f))
            .AssertEveryoneDied(262.0f, P6ActionId.RunMi, RunMiKills(TopP6LimitBreaks.GaugeWasShort));

    // The astrologian H2 lifts Magic Number 1's mark ahead of H1's, spending its Brilliant Dynamis there; its own Magic
    // Number 2 then empties the gauge, and the enrage holder's waits.
    [Test]
    public void PressingAtTheOtherHealersMarkLeavesItsOwnShort()
        => FromMagicNumber(ShieldHealer, JobId.Astrologian)
            .Play(For(MagicNumber1Healer, 208.5f), For(MagicNumber2Healer))
            .AssertEveryoneDied(262.0f, P6ActionId.RunMi, RunMiKills(TopP6LimitBreaks.GaugeWasShort));

    // The sage H1, its own limit break spent, lifts Magic Number 2's mark ahead of H2's: the gauge is empty for the enrage.
    [Test]
    public void TakingTheOtherHealersMarkWithSparkOfDynamisLeavesTheEnrageShort()
        => FromMagicNumber(RegenHealer, JobId.Sage)
            .Play(For(MagicNumber1Healer), For(MagicNumber2Healer, 224.5f))
            .AssertEveryoneDied(262.0f, P6ActionId.RunMi, RunMiKills(TopP6LimitBreaks.GaugeWasShort));

    // Pressed during Cosmo Memory's cast, before Brilliant Dynamis exists, it spends the gauge for good: the main tank's
    // limit break 3 waits for it, and Cosmo Memory kills everyone.
    [Test]
    public void PressingDuringCosmoMemoryLeavesItsTankWithoutAGauge()
        => new TopP6GauntletHumanSeat(RegenHealer, JobId.WhiteMage).From(TopP6GauntletPart.CosmoMemory)
            .Play(new TopP6GauntletHumanPress(3.0f))
            .AssertEveryoneDied(7.6f, TopConstants.ActionId.CosmoMemory,
                $"Cosmo Memory ({TopP6AlphaOmega.CosmoMemoryNeedsATankLimitBreak3}{TopP6LimitBreaks.GaugeWasShort})");

    // A dragoon in H2's seat can't press a healer's limit break: the run presses the scholar's for the seat.
    [Test]
    public void TheRunPressesForAHumanWhoseJobCantPressTheSeatsLimitBreak()
    {
        var run = FromMagicNumber(ShieldHealer, JobId.Dragoon).Play();

        run.AssertClears();
        Assert.Multiple(() =>
        {
            Assert.That(run.Plan.HumanFits, Is.False);
            Assert.That(run.Presses.Where(p => p.Seat == ShieldHealer).Select(p => (p.ByHuman, p.Job, p.ActionId)),
                        Is.EqualTo(new[] { (false, (uint)JobId.Scholar, LimitBreakHandler.ActionId((uint)JobId.Scholar, 3)) }));
        });
    }

    // Godmode deals no deaths: each player is reported once as Magic Number 1's mark runs out.
    [Test]
    public void GodmodeReportsEachPlayerOncePerHit()
    {
        var run = FromMagicNumber(RegenHealer, JobId.WhiteMage) with { GodMode = true };
        var result = run.Play();

        Assert.That(result.Run.Deaths, Is.Empty, result.Run.ToString);
        result.AssertReportedOncePerHit((214.1f, 8, TheMarkKills()));
    }
}
