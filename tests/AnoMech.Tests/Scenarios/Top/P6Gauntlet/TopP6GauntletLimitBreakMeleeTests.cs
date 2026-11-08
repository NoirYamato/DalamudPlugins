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

// The melee limit breaks 3 with the human in either melee seat, playing each melee job beside the other seat's bot (M1 DRG,
// M2 MNK). The melee higher on SAM > DRG > MNK > VPR > RPR > NIN is the enrage holder: it goes first after Cosmo Dive 2
// and takes the enrage, the other goes second, from the middle; a tie goes to the human, and the human's melee row forces
// its share. On the Gauntlet's clock (G), the Gauntlet holds the human, and only the human, to its own windows:
//   149.8 / 161.0  the first: from Cosmo Dive 2's bar end; the latest press;
//   (the first's landing) / 165.5  the second;
//   232.0 / 243.5  the enrage: from Run: ****mi*'s cast; the latest press, landing as the bar ends (248.0).
// Each must start inside its window, on Alpha Omega, and land: otherwise the human dies as it closes, or as the cast is
// cut off if that is later. A wasted press with Spark of Dynamis empties the gauge, and with nothing generating it after
// Cosmo Dive 2, the caster's limit break 3 then waits for it: the Cosmo Comets enrage at 187.0 and kill everyone, saying so.
public class TopP6GauntletLimitBreakMeleeTests
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

    private const string First = TopP6LimitBreaks.FirstDiveMeleeRule;
    private const string Second = TopP6LimitBreaks.SecondDiveMeleeRule;
    private const string TheEnrage = TopP6LimitBreaks.EnrageMeleeRule;
    private static readonly string CometsEnraged = $"Cosmo Meteor ({TopP6CosmoMeteor.CometsEnraged}{TopP6LimitBreaks.GaugeWasShort})";

    private static TopP6GauntletHumanSeat FromTheDive(PartyRole seat, JobId job)
        => new TopP6GauntletHumanSeat(seat, job).From(TopP6GauntletPart.Exaflares2);

    private static TopP6GauntletHumanSeat FromMagicNumber(PartyRole seat, JobId job)
        => new TopP6GauntletHumanSeat(seat, job).From(TopP6GauntletPart.MagicNumber);

    private static TopP6GauntletHumanSeat IntoCosmoMeteor(PartyRole seat, JobId job)
        => new TopP6GauntletHumanSeat(seat, job).From(TopP6GauntletPart.Exaflares2, TopP6GauntletPart.CosmoMeteor);

    // The monk in M1 and the dragoon in M2 tie with the other seat's bot: the enrage is the human's.
    [TestCase(MeleeDpsA, JobId.Samurai, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsA, JobId.Dragoon, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsA, JobId.Monk, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsA, JobId.Viper, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsA, JobId.Reaper, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsA, JobId.Ninja, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsB, JobId.Samurai, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsB, JobId.Dragoon, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsB, JobId.Monk, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsB, JobId.Viper, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsB, JobId.Reaper, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsB, JobId.Ninja, new[] { SecondDiveMelee })]
    public void EachMeleeJobPressingItsLimitBreaksClearsTheGauntlet(PartyRole seat, JobId job, TopP6LimitBreakDuty[] duties)
        => new TopP6GauntletHumanSeat(seat, job).Pressing(duties).AssertEveryDutyPressedOnceByItsSeat(duties);

    [TestCase(MeleeDpsA, JobId.Dragoon, TopP6MeleeLimitBreaks.Second, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsA, JobId.Dragoon, TopP6MeleeLimitBreaks.SecondAndTheEnrage, new[] { SecondDiveMelee, Enrage })]
    [TestCase(MeleeDpsB, JobId.Monk, TopP6MeleeLimitBreaks.FirstAndTheEnrage, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsB, JobId.Ninja, TopP6MeleeLimitBreaks.First, new[] { FirstDiveMelee })]
    public void TheMeleeRowsSharePressedClearsTheGauntlet(PartyRole seat, JobId job, TopP6MeleeLimitBreaks row, TopP6LimitBreakDuty[] duties)
        => new TopP6GauntletHumanSeat(seat, job) { Melee = row }.Pressing(duties).AssertEveryDutyPressedOnceByItsSeat(duties);

    // ---- Late or never

    [Test]
    public void TheFirstStartedAfterItsWindowKillsTheHuman()
        => FromTheDive(MeleeDpsB, JobId.Samurai).Play(For(FirstDiveMelee, 161.5f)).AssertOnlyTheHumanDied(161.0f, First);

    [Test]
    public void TheSecondStartedAfterItsWindowKillsTheHuman()
        => FromTheDive(MeleeDpsA, JobId.Ninja).Play(For(SecondDiveMelee, 166.0f)).AssertOnlyTheHumanDied(165.5f, Second);

    [Test]
    public void TheEnragesStartedAfterItsWindowKillsTheHuman()
        => FromMagicNumber(MeleeDpsA, JobId.Monk).Play(For(Enrage, 244.0f)).AssertOnlyTheHumanDied(243.5f, TheEnrage);

    [TestCase(MeleeDpsB, JobId.Dragoon, 161.0f, First)]
    [TestCase(MeleeDpsB, JobId.Viper, 165.5f, Second)]
    public void AMeleeWhoNeverPressesDiesAsItsWindowCloses(PartyRole seat, JobId job, float closes, string rule)
        => FromTheDive(seat, job).Play().AssertOnlyTheHumanDied(closes, rule);

    [Test]
    public void AnEnrageHolderWhoNeverPressesDiesAsItsWindowCloses()
        => FromMagicNumber(MeleeDpsA, JobId.Samurai).Play().AssertOnlyTheHumanDied(243.5f, TheEnrage);

    // ---- Cut off, or not what the window asks for

    [Test]
    public void TheFirstCutOffEarlyKillsTheHumanAsItsWindowCloses()
        => FromTheDive(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee, interruptedAfter: 1f))
            .AssertOnlyTheHumanDied(161.0f, First + TopP6LimitBreaks.MovingCancelledYours);

    // Started at 160.0 and cut off at 162.0, after the window has closed.
    [Test]
    public void TheFirstCutOffAfterItsWindowClosedKillsTheHumanThere()
        => FromTheDive(MeleeDpsB, JobId.Samurai).Play(For(FirstDiveMelee, 160.0f, interruptedAfter: 2f))
            .AssertOnlyTheHumanDied(162.0f, First + TopP6LimitBreaks.MovingCancelledYours);

    [Test]
    public void TheSecondCutOffKillsTheHumanAsItsWindowCloses()
        => FromTheDive(MeleeDpsA, JobId.Reaper).Play(For(SecondDiveMelee, interruptedAfter: 3.5f))
            .AssertOnlyTheHumanDied(165.5f, Second + TopP6LimitBreaks.MovingCancelledYours);

    [Test]
    public void TheEnragesCutOffAfterItsWindowClosedKillsTheHumanThere()
        => FromMagicNumber(MeleeDpsA, JobId.Dragoon).Play(For(Enrage, 240.0f, interruptedAfter: 4f))
            .AssertOnlyTheHumanDied(244.0f, TheEnrage + TopP6LimitBreaks.MovingCancelledYours);

    [Test]
    public void PressingAgainAfterBeingCutOffFillsTheWindow()
        => FromTheDive(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee, interruptedAfter: 0.5f), For(FirstDiveMelee, 151.5f)).AssertClears();

    [Test]
    public void ALimitBreak2DoesntFillTheWindow()
        => FromTheDive(MeleeDpsA, JobId.Dragoon).Play(new TopP6GauntletHumanPress(150.2f, Level: 2, OnAlphaOmega: true))
            .AssertOnlyTheHumanDied(161.0f, First + string.Format(TopP6LimitBreaks.YoursWasNotALimitBreak3, 2));

    [Test]
    public void OneAimedAtNothingDoesntFillTheWindow()
        => FromTheDive(MeleeDpsB, JobId.Samurai).Play(new TopP6GauntletHumanPress(150.2f))
            .AssertOnlyTheHumanDied(161.0f, First + TopP6LimitBreaks.YoursMissedAlphaOmega);

    // The reaper in M1 goes second, but presses before the monk's first one has landed.
    [Test]
    public void TheSecondStartedBeforeTheFirstHasLandedDoesntFillTheWindow()
        => FromTheDive(MeleeDpsA, JobId.Reaper).Play(For(SecondDiveMelee, 151.0f))
            .AssertOnlyTheHumanDied(165.5f, Second + TopP6LimitBreaks.YoursStartedTooEarly);

    // Started at 230.0, before Run: ****mi*'s cast, it lands in the cast and Alpha Omega falls; but the Gauntlet's window
    // opens with the cast, so the press doesn't fill it.
    [Test]
    public void TheEnragesStartedBeforeRunMisCastDoesntFillTheWindow()
        => FromMagicNumber(MeleeDpsA, JobId.Dragoon).Play(For(Enrage, 230.0f))
            .AssertOnlyTheHumanDied(243.5f, TheEnrage + TopP6LimitBreaks.YoursStartedTooEarly);

    // ---- A wasted press leaves the gauge empty for the caster

    // The enrage holder presses the first twice, the second with Spark of Dynamis.
    [Test]
    public void PressingTheFirstTwiceLeavesTheCasterWithoutAGauge()
        => IntoCosmoMeteor(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee), For(FirstDiveMelee, 155.0f))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged);

    // The enrage holder also presses beside the second melee's, its own Brilliant Dynamis spent on the first.
    [Test]
    public void PressingTheSecondMeleesTooLeavesTheCasterWithoutAGauge()
        => IntoCosmoMeteor(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee), For(SecondDiveMelee, 157.0f))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged);

    [Test]
    public void PressingTheSecondTwiceLeavesTheCasterWithoutAGauge()
        => IntoCosmoMeteor(MeleeDpsB, JobId.Monk).Play(For(SecondDiveMelee), For(SecondDiveMelee, 161.5f))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged);

    // ---- A job that can't press the seat's

    // A white mage in M1 and an astrologian in M2 can't press a melee limit break: the run presses the seat's bot's, and
    // no window holds the human.
    [TestCase(MeleeDpsA, JobId.WhiteMage, JobId.Dragoon, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsB, JobId.Astrologian, JobId.Monk, new[] { SecondDiveMelee })]
    public void TheRunPressesForAHumanWhoseJobCantPressTheSeatsLimitBreak(PartyRole seat, JobId job, JobId pressedAs, TopP6LimitBreakDuty[] duties)
    {
        var run = new TopP6GauntletHumanSeat(seat, job).From(TopP6GauntletPart.Exaflares2, TopP6GauntletPart.MagicNumber).Play();

        run.AssertClears();
        Assert.Multiple(() =>
        {
            Assert.That(run.Plan.HumanFits, Is.False);
            Assert.That(run.Presses.Where(p => p.Seat == seat).Select(p => (p.ByHuman, p.Job, p.ActionId)),
                        Is.EqualTo(duties.Select(_ => (false, (uint)pressedAs, LimitBreakHandler.ActionId((uint)pressedAs, 3)))));
        });
    }

    // Godmode deals no deaths: the enrage holder who never presses is reported once as its window closes, and everyone,
    // the human too, once as Run: ****mi* lands.
    [Test]
    public void GodmodeReportsEachPlayerOncePerHit()
    {
        var run = (FromMagicNumber(MeleeDpsA, JobId.Dragoon) with { GodMode = true }).Play();

        Assert.That(run.Run.Deaths, Is.Empty, run.Run.ToString);
        run.AssertReportedOncePerHit((243.5f, 1, TheEnrage), (262.0f, 8, $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded})"));
    }
}
