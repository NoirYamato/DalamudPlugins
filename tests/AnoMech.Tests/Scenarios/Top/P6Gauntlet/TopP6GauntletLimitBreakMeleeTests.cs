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
//   232.0 / 257.5  the enrage: landing from Run: ****mi*'s cast until before its damage at 262.0, as the presses that
//                  defeat Alpha Omega do; the latest press.
// Each must start inside its window (the enrage's: land in it), on Alpha Omega, and land: otherwise the human dies as it
// closes, or as the cast is cut off if that is later, and at the enrage Run: ****mi*'s damage then kills the rest. A
// wasted press with Spark of Dynamis empties the gauge, and with nothing generating it after Cosmo Dive 2, the caster's
// limit break 3 then waits for it: the Cosmo Comets enrage at 187.0 and kill everyone, saying so.
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

    private static TopP6GauntletHumanSeat From(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
        => duty == Enrage ? FromMagicNumber(seat, job) : FromTheDive(seat, job);

    [TestCaseSource(typeof(TopP6GauntletLimitBreakPlanTests), nameof(TopP6GauntletLimitBreakPlanTests.MeleeJobsInEitherSeat))]
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

    // Started at 257.6, it would land at 262.1, after Run: ****mi*'s damage at 262.0.
    [TestCase(MeleeDpsA, JobId.Monk)]
    [TestCase(MeleeDpsB, JobId.Samurai)]
    public void TheEnragesStartedAfterItsWindowKillsTheHuman(PartyRole seat, JobId job)
        => FromMagicNumber(seat, job).Play(For(Enrage, 257.6f)).AssertTheHumanDiedThenRunMiKilledTheRest(257.5f, TheEnrage);

    [TestCase(MeleeDpsB, JobId.Dragoon, 161.0f, First)]
    [TestCase(MeleeDpsB, JobId.Viper, 165.5f, Second)]
    public void AMeleeWhoNeverPressesDiesAsItsWindowCloses(PartyRole seat, JobId job, float closes, string rule)
        => FromTheDive(seat, job).Play().AssertOnlyTheHumanDied(closes, rule);

    [TestCase(MeleeDpsA, JobId.Samurai)]
    [TestCase(MeleeDpsB, JobId.Samurai)]
    public void AnEnrageHolderWhoNeverPressesDiesAsItsWindowCloses(PartyRole seat, JobId job)
        => FromMagicNumber(seat, job).Play().AssertTheHumanDiedThenRunMiKilledTheRest(257.5f, TheEnrage);

    // The samurai in M2 is the first melee, and its window runs to 161.0: pressing late in it, it still goes first, since
    // the second melee, the dragoon bot in M1, waits for its limit break to land (or, past 161.0, for any still casting)
    // before pressing its own.
    [TestCase(158.0f)]
    [TestCase(160.9f)]
    public void TheSecondMeleeWaitsForAFirstPressedLateInItsWindow(float pressAt)
    {
        var run = FromTheDive(MeleeDpsB, JobId.Samurai).Play(For(FirstDiveMelee, pressAt));

        run.AssertClears();
        Assert.That(run.Presses.Select(p => (p.Seat, p.ByHuman)), Is.EqualTo(new[] { (MeleeDpsB, true), (MeleeDpsA, false) }),
                    () => TopP6GauntletHumanSeatRun.Describe(run.Presses));
        Assert.That(run.G(run.Presses[1].CastStartedAt), Is.EqualTo(pressAt + 4.5f).Within(0.05f), "the second goes as the first lands");
    }

    // Godmode: the dragoon in M2, the first melee by the tie, never presses and is reported as its window closes at 161.0;
    // the dragoon bot in M1 presses the second then, without waiting any longer for a first that never lands.
    [Test]
    public void TheSecondMeleePressesAsTheFirstsWindowClosesWithoutIt()
    {
        var run = (FromTheDive(MeleeDpsB, JobId.Dragoon) with { GodMode = true }).Play();

        Assert.That(run.Run.Deaths, Is.Empty, run.Run.ToString);
        run.AssertReportedOncePerHit((161.0f, 1, First));
        Assert.That(run.Presses.Select(p => (p.Seat, p.ByHuman, p.ResolvedAt is not null)), Is.EqualTo(new[] { (MeleeDpsA, false, true) }),
                    () => TopP6GauntletHumanSeatRun.Describe(run.Presses));
        Assert.That(run.G(run.Presses[0].CastStartedAt), Is.EqualTo(161.0f).Within(0.05f));
    }

    // ---- Cut off, or not what the window asks for

    [Test]
    public void TheFirstCutOffEarlyKillsTheHumanAsItsWindowCloses()
        => FromTheDive(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee, interruptedAfter: 1f))
            .AssertOnlyTheHumanDied(161.0f, First + TopP6LimitBreaks.YoursWasCutOff);

    // Started at 160.0 and cut off at 162.0, after the window has closed.
    [Test]
    public void TheFirstCutOffAfterItsWindowClosedKillsTheHumanThere()
        => FromTheDive(MeleeDpsB, JobId.Samurai).Play(For(FirstDiveMelee, 160.0f, interruptedAfter: 2f))
            .AssertOnlyTheHumanDied(162.0f, First + TopP6LimitBreaks.YoursWasCutOff);

    [Test]
    public void TheSecondCutOffKillsTheHumanAsItsWindowCloses()
        => FromTheDive(MeleeDpsA, JobId.Reaper).Play(For(SecondDiveMelee, interruptedAfter: 3.5f))
            .AssertOnlyTheHumanDied(165.5f, Second + TopP6LimitBreaks.YoursWasCutOff);

    // Started at 255.0 and cut off at 259.0, after the window has closed.
    [TestCase(MeleeDpsA, JobId.Dragoon)]
    [TestCase(MeleeDpsB, JobId.Samurai)]
    public void TheEnragesCutOffAfterItsWindowClosedKillsTheHumanThere(PartyRole seat, JobId job)
        => FromMagicNumber(seat, job).Play(For(Enrage, 255.0f, interruptedAfter: 4f))
            .AssertTheHumanDiedThenRunMiKilledTheRest(259.0f, TheEnrage + TopP6LimitBreaks.YoursWasCutOff);

    [Test]
    public void PressingAgainAfterBeingCutOffFillsTheWindow()
        => FromTheDive(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee, interruptedAfter: 0.5f), For(FirstDiveMelee, 151.5f)).AssertClears();

    // The first window (the dragoon in M1) and the second (the monk in M2, after the dragoon bot's first); then the
    // enrage's, after which Run: ****mi*'s damage kills the rest.
    [TestCase(MeleeDpsA, JobId.Dragoon, FirstDiveMelee, 161.0f, First)]
    [TestCase(MeleeDpsB, JobId.Monk, SecondDiveMelee, 165.5f, Second)]
    public void ALimitBreak2DoesntFillTheWindow(PartyRole seat, JobId job, TopP6LimitBreakDuty duty, float closes, string rule)
        => From(seat, job, duty).Play(new TopP6GauntletHumanPress(AimFor(duty), Level: 2, OnAlphaOmega: true))
            .AssertOnlyTheHumanDied(closes, rule + string.Format(TopP6LimitBreaks.YoursWasNotALimitBreak3, 2));

    [Test]
    public void ALimitBreak2DoesntFillTheEnragesWindow()
        => FromMagicNumber(MeleeDpsA, JobId.Dragoon).Play(new TopP6GauntletHumanPress(AimFor(Enrage), Level: 2, OnAlphaOmega: true))
            .AssertTheHumanDiedThenRunMiKilledTheRest(257.5f, TheEnrage + string.Format(TopP6LimitBreaks.YoursWasNotALimitBreak3, 2));

    // The samurai in M2 first, the reaper in M1 second, after the monk bot's first; then the samurai at the enrage.
    [TestCase(MeleeDpsB, JobId.Samurai, FirstDiveMelee, 161.0f, First)]
    [TestCase(MeleeDpsA, JobId.Reaper, SecondDiveMelee, 165.5f, Second)]
    public void OneAimedAtNothingDoesntFillTheWindow(PartyRole seat, JobId job, TopP6LimitBreakDuty duty, float closes, string rule)
        => From(seat, job, duty).Play(new TopP6GauntletHumanPress(AimFor(duty)))
            .AssertOnlyTheHumanDied(closes, rule + TopP6LimitBreaks.YoursMissedAlphaOmega);

    [Test]
    public void OneAimedAtNothingDoesntFillTheEnragesWindow()
        => FromMagicNumber(MeleeDpsB, JobId.Samurai).Play(new TopP6GauntletHumanPress(AimFor(Enrage)))
            .AssertTheHumanDiedThenRunMiKilledTheRest(257.5f, TheEnrage + TopP6LimitBreaks.YoursMissedAlphaOmega);

    // The reaper in M1 goes second, but presses as the dive's bar ends, ahead of the monk's first (150.2), which then waits
    // for it and lands at 159.0: the reaper's started before its window opened.
    [Test]
    public void TheSecondStartedBeforeTheFirstHasLandedDoesntFillTheWindow()
        => FromTheDive(MeleeDpsA, JobId.Reaper).Play(For(SecondDiveMelee, 150.0f))
            .AssertOnlyTheHumanDied(165.5f, Second + TopP6LimitBreaks.YoursStartedTooEarly);

    // Started at 230.0, before Run: ****mi*'s cast, it lands in the cast at 234.5: Alpha Omega falls, and the press fills the
    // window, which counts what defeats Alpha Omega. Started at 227.6, it lands 0.1 s into the cast, and Alpha Omega's body
    // is gone by the time the window closes.
    [TestCase(230.0f)]
    [TestCase(227.6f)]
    public void TheEnragesStartedBeforeRunMisCastFillsTheWindowLandingInIt(float pressAt)
    {
        var run = FromMagicNumber(MeleeDpsA, JobId.Dragoon).Play(For(Enrage, pressAt));

        run.AssertClears();
        Assert.That(run.G(run.Run.Elapsed), Is.LessThan(250f), "the run ends with Alpha Omega fallen, before Run: ****mi* lands");
    }

    // Started at 243.4, it lands at 247.9, after Run: ****mi*'s bar has gone at 247.7; at 257.4, it lands at 261.9, just
    // before the damage. Either fills the window and defeats Alpha Omega.
    [TestCase(243.4f)]
    [TestCase(250.0f)]
    [TestCase(257.4f)]
    public void TheEnragesLandingAfterTheBarButBeforeTheDamageFillsTheWindow(float pressAt)
    {
        var run = FromMagicNumber(MeleeDpsB, JobId.Samurai).Play(For(Enrage, pressAt));

        run.AssertClears();
        Assert.That(run.TheHumans.Select(p => run.G(p.LandsAt)), Is.EqualTo(new[] { pressAt + 4.5f }).Within(0.05f),
                    () => TopP6GauntletHumanSeatRun.Describe(run.Presses));
    }

    // Started at 257.6, it would land at 262.1, after the damage: godmode deals no deaths, and its cast doesn't save anyone
    // from the damage, which lands while it still casts.
    [Test]
    public void TheEnragesLandingAfterTheDamageIsTooLate()
    {
        var run = (FromMagicNumber(MeleeDpsA, JobId.Dragoon) with { GodMode = true }).Play(For(Enrage, 257.6f));

        Assert.That(run.Run.Deaths, Is.Empty, run.Run.ToString);
        run.AssertReportedOncePerHit((257.5f, 1, TheEnrage), (262.0f, 8, $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded})"));
    }

    // Started at 227.0, as soon as the gauge has refilled after the second healer's, it lands at 231.5, before Run: ****mi*'s
    // bar goes up: it neither fills the window nor defeats Alpha Omega, and with Spark of Dynamis it leaves no gauge for
    // another. Godmode deals no deaths: the human is reported as the window closes, and everyone as Run: ****mi* lands.
    [Test]
    public void TheEnragesLandingBeforeRunMisCastIsWasted()
    {
        var run = (FromMagicNumber(MeleeDpsA, JobId.Dragoon) with { GodMode = true }).Play(For(Enrage, 227.0f));

        Assert.That(run.Run.Deaths, Is.Empty, run.Run.ToString);
        run.AssertReportedOncePerHit((257.5f, 1, TheEnrage + TopP6LimitBreaks.YoursLandedBeforeTheBar),
            (262.0f, 8, $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded})"));
    }

    // ---- A wasted press leaves the gauge empty for the caster

    // The enrage holder presses the first twice, the second with Spark of Dynamis.
    [Test]
    public void PressingTheFirstTwiceLeavesTheCasterWithoutAGauge()
        => IntoCosmoMeteor(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee), For(FirstDiveMelee, 155.0f))
            .AssertEveryoneDied(187.0f, P6ActionId.CosmoMeteorEnrage, CometsEnraged);

    // The enrage holder also presses the second melee's, ahead of it (156.9), its own Brilliant Dynamis spent on the first.
    [Test]
    public void PressingTheSecondMeleesTooLeavesTheCasterWithoutAGauge()
        => IntoCosmoMeteor(MeleeDpsA, JobId.Dragoon).Play(For(FirstDiveMelee), For(SecondDiveMelee, 156.0f))
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
        run.AssertReportedOncePerHit((257.5f, 1, TheEnrage), (262.0f, 8, $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded})"));
    }
}
