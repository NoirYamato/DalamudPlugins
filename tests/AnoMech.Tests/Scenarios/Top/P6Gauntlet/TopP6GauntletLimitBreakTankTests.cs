using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Multiplayer;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6LimitBreakDuty;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;
using static AnoMech.Tests.TopP6GauntletHumanPress;

namespace AnoMech.Tests;

// The tanks' limit breaks 3 with the human in either tank seat, playing each tank job beside the other seat's bot (MT WAR,
// OT PLD). The tank higher on WAR > DRK > PLD > GNB takes Cosmo Memory and Magic Number 1, the other Magic Number 2; a tie
// goes to the human, and the human's tank row forces its share. On the Gauntlet's clock (G):
//     1.9 / 7.6    Cosmo Memory's cast, the gauge full; its hit decides: everyone needs a tank's limit break 3 up for a
//                  second by then (UNVERIFIED), so 6.6 is the last press that holds;
//   201.8 / 206.5  Magic Number 1, likewise; its mark on everyone from 208.1 to 214.1, which a healer's limit break 3 lifts;
//   217.9 / 222.6  Magic Number 2; its mark from 224.2 to 230.2.
// A press with Brilliant Dynamis gives the gauge back, one with Spark of Dynamis doesn't: a tank's wasted press leaves a
// later limit break waiting for a gauge that never fills, and that one's mechanic kills everyone, saying so.
public class TopP6GauntletLimitBreakTankTests
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

    private static string CosmoMemoryKills(string why = "") => $"Cosmo Memory ({TopP6AlphaOmega.CosmoMemoryNeedsATankLimitBreak3}{why})";
    private static string MagicNumberKills(string why = "") => $"Magic Number ({TopP6AlphaOmega.MagicNumberNeedsATankLimitBreak3}{why})";
    private static string TheMarkKills(string why = "") => $"Magic Number ({TopP6AlphaOmega.MagicNumberMarkRanOut}{why})";
    private static readonly string TooLate = string.Format(TopP6LimitBreaks.TakesHoldTooLate, P6Duration.TankLimitBreak3TakesHold);

    private static (TopP6GauntletPart Part, float Decides, uint ActionId, Func<string, string> Kills) Hit(TopP6LimitBreakDuty duty) => duty switch
    {
        CosmoMemory => (TopP6GauntletPart.CosmoMemory, 7.6f, ActionId.CosmoMemory, CosmoMemoryKills),
        MagicNumber1Tank => (TopP6GauntletPart.MagicNumber, 206.5f, P6ActionId.MagicNumber, MagicNumberKills),
        _ => (TopP6GauntletPart.MagicNumber, 222.6f, P6ActionId.MagicNumber, MagicNumberKills),
    };

    [TestCase(MainTank, JobId.Warrior, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MainTank, JobId.DarkKnight, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MainTank, JobId.Paladin, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MainTank, JobId.Gunbreaker, new[] { MagicNumber2Tank })]
    [TestCase(OffTank, JobId.Warrior, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(OffTank, JobId.DarkKnight, new[] { MagicNumber2Tank })]
    [TestCase(OffTank, JobId.Paladin, new[] { MagicNumber2Tank })]
    [TestCase(OffTank, JobId.Gunbreaker, new[] { MagicNumber2Tank })]
    public void EachTankJobPressingItsLimitBreaksClearsTheGauntlet(PartyRole seat, JobId job, TopP6LimitBreakDuty[] duties)
        => new TopP6GauntletHumanSeat(seat, job).Pressing(duties).AssertEveryDutyPressedOnceByItsSeat(duties);

    [TestCase(MainTank, JobId.Warrior, TopP6TankLimitBreaks.MagicNumber2, new[] { MagicNumber2Tank })]
    [TestCase(MainTank, JobId.Gunbreaker, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber1, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(OffTank, JobId.Paladin, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber2, new[] { CosmoMemory, MagicNumber2Tank })]
    [TestCase(OffTank, JobId.DarkKnight, TopP6TankLimitBreaks.MagicNumber1, new[] { MagicNumber1Tank })]
    public void TheTankRowsSharePressedClearsTheGauntlet(PartyRole seat, JobId job, TopP6TankLimitBreaks row, TopP6LimitBreakDuty[] duties)
        => new TopP6GauntletHumanSeat(seat, job) { Tank = row }.Pressing(duties).AssertEveryDutyPressedOnceByItsSeat(duties);

    [TestCase(MainTank, JobId.Warrior, CosmoMemory, 7.0f)]
    [TestCase(MainTank, JobId.DarkKnight, MagicNumber1Tank, 206.0f)]
    [TestCase(OffTank, JobId.Gunbreaker, MagicNumber2Tank, 222.0f)]
    public void ATankLimitBreak3InTheHitsLastSecondTakesHoldTooLate(PartyRole seat, JobId job, TopP6LimitBreakDuty duty, float pressAt)
    {
        var (part, decides, actionId, kills) = Hit(duty);

        new TopP6GauntletHumanSeat(seat, job).From(part).Play(For(duty, pressAt)).AssertEveryoneDied(decides, actionId, kills(TooLate));
    }

    // The paladin main tank and the warrior off-tank tie with the other seat's bot: the duty is the human's.
    [TestCase(MainTank, JobId.Paladin, CosmoMemory)]
    [TestCase(OffTank, JobId.Warrior, MagicNumber1Tank)]
    [TestCase(MainTank, JobId.Gunbreaker, MagicNumber2Tank)]
    public void ATankWhoNeverPressesLetsTheHitKillEveryone(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
    {
        var (part, decides, actionId, kills) = Hit(duty);

        new TopP6GauntletHumanSeat(seat, job).From(part).Play().AssertEveryoneDied(decides, actionId, kills(""));
    }

    // A limit break 2 covers nothing that Cosmo Memory counts.
    [Test]
    public void ATankLimitBreak2AtCosmoMemoryLetsItKillEveryone()
        => new TopP6GauntletHumanSeat(MainTank, JobId.Warrior).From(TopP6GauntletPart.CosmoMemory)
            .Play(new TopP6GauntletHumanPress(4.9f, Level: 2))
            .AssertEveryoneDied(7.6f, ActionId.CosmoMemory, CosmoMemoryKills());

    // The second press of Magic Number 1, with Spark of Dynamis, empties the gauge: the first healer's waits for it, and
    // the mark kills everyone.
    [Test]
    public void PressingMagicNumber1TwiceLeavesItsHealerWithoutAGauge()
        => new TopP6GauntletHumanSeat(MainTank, JobId.Warrior).From(TopP6GauntletPart.MagicNumber)
            .Play(For(MagicNumber1Tank), For(MagicNumber1Tank, 203.5f))
            .AssertEveryoneDied(214.1f, P6ActionId.MagicNumber, TheMarkKills(TopP6LimitBreaks.GaugeWasShort));

    [Test]
    public void PressingMagicNumber2TwiceLeavesItsHealerWithoutAGauge()
        => new TopP6GauntletHumanSeat(OffTank, JobId.Paladin).From(TopP6GauntletPart.MagicNumber)
            .Play(For(MagicNumber2Tank), For(MagicNumber2Tank, 219.6f))
            .AssertEveryoneDied(230.2f, P6ActionId.MagicNumber, TheMarkKills(TopP6LimitBreaks.GaugeWasShort));

    // The dark knight main tank also takes the off-tank's Magic Number 2: its own limit break 3 covers the hit, but with
    // Spark of Dynamis it empties the gauge, so the off-tank's and the second healer's wait, and the mark kills everyone.
    [Test]
    public void PressingTheOtherTanksMagicNumberLeavesTheHealerWithoutAGauge()
        => new TopP6GauntletHumanSeat(MainTank, JobId.DarkKnight).From(TopP6GauntletPart.MagicNumber)
            .Play(For(MagicNumber1Tank), For(MagicNumber2Tank, 218.0f))
            .AssertEveryoneDied(230.2f, P6ActionId.MagicNumber, TheMarkKills(TopP6LimitBreaks.GaugeWasShort));

    // The gunbreaker off-tank also presses at Magic Number 1, after the warrior's, spending its Brilliant Dynamis there.
    [Test]
    public void PressingAtTheOtherTanksMagicNumberLeavesItsOwnShort()
        => new TopP6GauntletHumanSeat(OffTank, JobId.Gunbreaker).From(TopP6GauntletPart.MagicNumber)
            .Play(For(MagicNumber1Tank, 202.9f), For(MagicNumber2Tank))
            .AssertEveryoneDied(230.2f, P6ActionId.MagicNumber, TheMarkKills(TopP6LimitBreaks.GaugeWasShort));

    // A limit break 1 wasted between the Magic Numbers spends the paladin's Brilliant Dynamis.
    [Test]
    public void AWastedLimitBreak1LeavesTheNextOneShort()
        => new TopP6GauntletHumanSeat(OffTank, JobId.Paladin).From(TopP6GauntletPart.MagicNumber)
            .Play(new TopP6GauntletHumanPress(212.0f, Level: 1), For(MagicNumber2Tank))
            .AssertEveryoneDied(230.2f, P6ActionId.MagicNumber, TheMarkKills(TopP6LimitBreaks.GaugeWasShort));

    // A white mage in the off-tank's seat can't press a tank's limit break: the run presses the paladin's for the seat, and
    // nothing holds the human to it.
    [Test]
    public void TheRunPressesForAHumanWhoseJobCantPressTheSeatsLimitBreak()
    {
        var run = new TopP6GauntletHumanSeat(OffTank, JobId.WhiteMage).From(TopP6GauntletPart.MagicNumber).Play();

        run.AssertClears();
        Assert.Multiple(() =>
        {
            Assert.That(run.Plan.HumanFits, Is.False);
            Assert.That(run.Presses.Where(p => p.Seat == OffTank).Select(p => (p.ByHuman, p.Job, p.ActionId)),
                        Is.EqualTo(new[] { (false, (uint)JobId.Paladin, LimitBreakHandler.ActionId((uint)JobId.Paladin, 3)) }));
            Assert.That(run.Chat.Select(c => c.Line), Has.Some.EndsWith(
                "Your limit breaks: none, since your job can't press your seat's: the Gauntlet presses its tank LB3 on Magic Number 2 for you"));
        });
    }

    // Godmode deals no deaths: each player is reported once for each hit the human's missing limit breaks let through,
    // Cosmo Memory's and Magic Number 1's.
    [Test]
    public void GodmodeReportsEachPlayerOncePerHit()
    {
        var run = new TopP6GauntletHumanSeat(MainTank, JobId.Warrior) { GodMode = true }.Play();

        Assert.That(run.Run.Deaths, Is.Empty, run.Run.ToString);
        run.AssertReportedOncePerHit((7.6f, 8, CosmoMemoryKills()), (206.5f, 8, MagicNumberKills()));
    }
}
