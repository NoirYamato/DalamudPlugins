using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// What LPDU asks of each seat where Living Liquid leaves a choice, written from the strat's rule: each tank takes the
// Drainage tether from the tornado on its own side (the main tank the east one's, the off tank the west one's) and
// the middle one's when its side has none; each healer cleanses itself, then H1 down the party list and H2 up it.
public class TeaP1LivingLiquidPlanTests
{
    private static readonly JobId[] HealerJobs = [JobId.WhiteMage, JobId.Scholar, JobId.Astrologian, JobId.Sage];

    private static TeaP1LivingLiquidPlan Plan(PartyRole seat = MeleeDpsA, JobId job = JobId.Dragoon) => new(seat, (uint)job);

    [Test]
    public void EachHealerCleansesItselfThenH1DownThePartyListAndH2UpIt()
    {
        var plan = Plan();

        Assert.That(plan.CleanseOrder(RegenHealer), Is.EqualTo(new[]
            { RegenHealer, MainTank, OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps }));
        Assert.That(plan.CleanseOrder(ShieldHealer), Is.EqualTo(new[]
            { ShieldHealer, CasterDps, PhysRangedDps, MeleeDpsB, MeleeDpsA, RegenHealer, OffTank, MainTank }));
    }

    [TestCase(TeaP1Tornado.West, TeaP1Tornado.East, TeaP1Tornado.East, TeaP1Tornado.West)]
    [TestCase(TeaP1Tornado.West, TeaP1Tornado.Middle, TeaP1Tornado.Middle, TeaP1Tornado.West)]
    [TestCase(TeaP1Tornado.Middle, TeaP1Tornado.East, TeaP1Tornado.East, TeaP1Tornado.Middle)]
    public void EachTankTakesItsOwnSidesTetherOrTheMiddleOne(TeaP1Tornado first, TeaP1Tornado second, TeaP1Tornado mainTanks, TeaP1Tornado offTanks)
    {
        var plan = Plan();

        Assert.That(plan.TetherOf(MainTank, [first, second]), Is.EqualTo(mainTanks));
        Assert.That(plan.TetherOf(OffTank, [first, second]), Is.EqualTo(offTanks));
        Assert.That(plan.TetherOf(MainTank, [second, first]), Is.EqualTo(mainTanks), "whatever order they are named in");
    }

    [Test]
    public void NobodyButATankHasATether([Values(RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
        => Assert.That(Plan().TetherOf(seat, [TeaP1Tornado.West, TeaP1Tornado.East]), Is.Null);

    [Test]
    public void ATankWhoseTornadoesDoNotTetherHasNone()
    {
        Assert.That(Plan().TetherOf(MainTank, [TeaP1Tornado.West]), Is.Null);
        Assert.That(Plan().TetherOf(OffTank, []), Is.Null);
    }

    [Test]
    public void AHumanCleansesInAHealersSeatOnAHealersJob([Values(RegenHealer, ShieldHealer)] PartyRole seat, [ValueSource(nameof(HealerJobs))] JobId job)
        => Assert.That(Plan(seat, job).HumanCleanses, Is.True);

    [Test]
    public void AHumanOnAJobWithoutEsunaInAHealersSeatCleansesNothing([Values(RegenHealer, ShieldHealer)] PartyRole seat,
        [Values(JobId.Warrior, JobId.Monk, JobId.Bard, JobId.BlackMage)] JobId job)
    {
        var plan = Plan(seat, job);

        Assert.That(plan.HumanCleanses, Is.False);
        Assert.That(plan.ChatLines(_ => true).Single(), Does.Contain("none").And.Contain("the run cleanses for your seat"));
    }

    [Test]
    public void AHealerOutsideAHealersSeatCleansesNothing([Values(MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
        => Assert.That(Plan(seat, JobId.WhiteMage).HumanCleanses, Is.False);

    [Test]
    public void TheChatLinesNameADutyOnlyInThePartThatPlaysIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Plan(MainTank, JobId.Warrior).ChatLines(ProteanOne).Single(), Does.StartWith("Your Drainage tether: from the tornado right of the open quarter"));
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(ProteanOne).Single(), Does.StartWith("Your Drainage tether: from the tornado left of the open quarter"));
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(CascadeTwo), Is.Empty);
            Assert.That(Plan(ShieldHealer, JobId.Scholar).ChatLines(CascadeTwo).Single(), Does.StartWith("Your Throttle cleanses: yourself first, then R2, R1"));
            Assert.That(Plan(ShieldHealer, JobId.Scholar).ChatLines(ProteanOne), Is.Empty);
            Assert.That(Plan(CasterDps, JobId.BlackMage).ChatLines(_ => true), Is.Empty);
        });
    }

    // Two tanks that start with each other's tether can't swap: a tether's holder is passed over by the other one.
    [Test]
    public void ATankIsToldToKeepTheOtherTanksTetherWhenEachStartsWithTheOthers([Values(MainTank, OffTank)] PartyRole seat)
        => Assert.That(Plan(seat, JobId.Warrior).ChatLines(ProteanOne).Single(),
            Does.Contain("If each of you starts with the other's, keep it on your own spot: a tether's holder can't take the second"));

    // No move of the strat's reaches a human, and the Living Liquid follows its tank from 4 y.
    [Test]
    public void TheMainTankAloneIsToldToHaveTheLiquidOnTheMiddleForProteanTwo()
    {
        var whole = Plan(MainTank, JobId.Warrior).ChatLines(_ => true);
        var fromCascadeTwo = Plan(MainTank, JobId.Warrior).ChatLines(CascadeTwo);

        Assert.Multiple(() =>
        {
            Assert.That(whole, Has.Count.EqualTo(2));
            Assert.That(whole[1], Does.StartWith("Your Living Liquid: it walks after you whenever you are more than 4 y from it, so fetching your tether drags it off the middle. After Drainage walk it back onto the middle, and turn it to the open quarter before Protean 2"));
            Assert.That(fromCascadeTwo.Single(), Does.StartWith("Your Living Liquid: it walks after you whenever you are more than 4 y from it. Keep it on the middle, and turn it to the open quarter before Protean 2"));
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(_ => true).Single(), Does.StartWith("Your Drainage tether:"));
        });
    }

    private static bool ProteanOne(TeaP1LivingLiquidPart part) => part == TeaP1LivingLiquidPart.Protean1;

    private static bool CascadeTwo(TeaP1LivingLiquidPart part) => part == TeaP1LivingLiquidPart.Cascade2;
}
