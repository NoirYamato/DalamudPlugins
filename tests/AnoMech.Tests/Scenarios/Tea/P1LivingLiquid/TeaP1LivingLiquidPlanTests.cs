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
        Assert.That(plan.ChatLine(_ => true), Does.Contain("none").And.Contain("the run cleanses for your seat"));
    }

    [Test]
    public void AHealerOutsideAHealersSeatCleansesNothing([Values(MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
        => Assert.That(Plan(seat, JobId.WhiteMage).HumanCleanses, Is.False);

    [Test]
    public void TheChatLineNamesADutyOnlyInThePartThatPlaysIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Plan(MainTank, JobId.Warrior).ChatLine(part => part == TeaP1LivingLiquidPart.Protean1), Does.StartWith("Your Drainage tether:"));
            Assert.That(Plan(MainTank, JobId.Warrior).ChatLine(part => part == TeaP1LivingLiquidPart.Cascade2), Is.Null);
            Assert.That(Plan(ShieldHealer, JobId.Scholar).ChatLine(part => part == TeaP1LivingLiquidPart.Cascade2), Does.StartWith("Your Throttle cleanses: yourself first, then R2, R1"));
            Assert.That(Plan(ShieldHealer, JobId.Scholar).ChatLine(part => part == TeaP1LivingLiquidPart.Protean1), Is.Null);
            Assert.That(Plan(CasterDps, JobId.BlackMage).ChatLine(_ => true), Is.Null);
        });
    }
}
