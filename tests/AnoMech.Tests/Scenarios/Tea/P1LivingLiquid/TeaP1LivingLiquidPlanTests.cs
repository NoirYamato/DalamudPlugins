using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// What LPDU asks of each seat where Living Liquid leaves a choice, written from the strat's rule: each tank takes the
// Drainage tether from the tornado on its own side (the main tank the east one's, the off tank the west one's) and
// the middle one's when its side has none; each healer cleanses itself, then H1 down the party list and H2 up it. At
// the pull the main tank holds both bosses but for the first hand, and each damage dealer takes the Jagd Doll of its
// own quarter: R1 north-east, M2 south-east, M1 south-west, R2 north-west.
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
        Assert.That(plan.ChatLines(AfterThePull).Single(), Does.Contain("none").And.Contain("the run cleanses for your seat"));
    }

    [Test]
    public void AHealerOutsideAHealersSeatCleansesNothing([Values(MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
        => Assert.That(Plan(seat, JobId.WhiteMage).HumanCleanses, Is.False);

    [Test]
    public void TheChatLinesNameADutyOnlyInThePartThatPlaysIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Plan(MainTank, JobId.Warrior).ChatLines(ProteanOne)[0], Does.StartWith("Your Drainage tether: from the tornado right of the open quarter"));
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(ProteanOne).Single(), Does.StartWith("Your Drainage tether: from the tornado left of the open quarter"));
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(CascadeTwo).Single(), Does.StartWith("Your Liquid Hand:"));
            Assert.That(Plan(ShieldHealer, JobId.Scholar).ChatLines(CascadeTwo).Single(), Does.StartWith("Your Throttle cleanses: yourself first, then R2, R1"));
            Assert.That(Plan(ShieldHealer, JobId.Scholar).ChatLines(ProteanOne), Is.Empty);
            Assert.That(Plan(CasterDps, JobId.BlackMage).ChatLines(AfterThePull), Is.Empty);
        });
    }

    // Two tanks that start with each other's tether can't swap: a tether's holder is passed over by the other one.
    [Test]
    public void ATankIsToldToKeepTheOtherTanksTetherWhenEachStartsWithTheOthers([Values(MainTank, OffTank)] PartyRole seat)
        => Assert.That(Plan(seat, JobId.Warrior).ChatLines(ProteanOne)[0],
            Does.Contain("If each of you starts with the other's, keep it on your own spot: a tether's holder can't take the second"));

    // No move of the strat's reaches a human, and the Living Liquid walks after a tank more than 7 y off.
    [Test]
    public void TheMainTankAloneIsToldToHaveTheLiquidOnTheMiddleForProteanTwo()
    {
        var whole = Plan(MainTank, JobId.Warrior).ChatLines(AfterThePull);
        var fromCascadeTwo = Plan(MainTank, JobId.Warrior).ChatLines(CascadeTwo);

        Assert.Multiple(() =>
        {
            Assert.That(whole, Has.Count.EqualTo(4));
            Assert.That(whole[2], Does.StartWith("Your Living Liquid: it stays put while you are within 7 y of it, and beyond that walks after you until it is 6 y from you, so fetching your tether can drag it off the middle. After Drainage lead it back: stand 6 y past the middle on the far side from it, and turn it to the open quarter 2 s or more before Protean 2's bar shows"));
            Assert.That(fromCascadeTwo, Has.Count.EqualTo(2));
            Assert.That(fromCascadeTwo[0], Does.StartWith("Your Living Liquid: it stays put while you are within 7 y of it, and beyond that walks after you until it is 6 y from you. Keep it on the middle, and turn it to the open quarter 2 s or more before Protean 2's bar shows"));
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(_ => true).Where(line => line.StartsWith("Your Living Liquid")), Is.Empty);
        });
    }

    // It faces its tank as the telegraphed cast starts, and its own wave goes down that line.
    [Test]
    public void TheMainTankAloneIsToldToTurnTheLiquidAwayFromTheOpenQuarterForProteanOne()
    {
        var mainTanks = Plan(MainTank, JobId.Warrior).ChatLines(ProteanOne);

        Assert.Multiple(() =>
        {
            Assert.That(mainTanks, Has.Count.EqualTo(2));
            Assert.That(mainTanks[1], Does.StartWith("Your Living Liquid in Protean 1: it faces you until its Protean Wave's bar shows")
                .And.Contain("Stand on its side away from the open quarter before the bar"));
            Assert.That(Plan(MainTank, JobId.Warrior).ChatLines(CascadeTwo).Where(line => line.Contains("Protean 1:")), Is.Empty);
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(_ => true).Where(line => line.Contains("Protean 1:")), Is.Empty);
        });
    }

    // The Living Liquid stops 6 y behind its tank and the Liquid Hand 4 y: two tanks that stop on the middle leave
    // them 6.1 y apart, and a palm needs 5.1.
    [Test]
    public void EachTankIsToldToWalkOnThroughTheMiddleForTheLastHand()
    {
        var mainTanks = Plan(MainTank, JobId.Warrior).ChatLines(CascadeTwo);
        var offTanks = Plan(OffTank, JobId.Paladin).ChatLines(AfterThePull);

        Assert.Multiple(() =>
        {
            Assert.That(mainTanks[1], Does.StartWith("Your last hand: hold the Living Liquid more than 17 y from the Liquid Hand until the Hand shows its palm")
                .And.Contain("walk on through the middle until the Living Liquid stands on the Hand: it stops 6 y behind you"));
            Assert.That(offTanks, Has.Count.EqualTo(2));
            Assert.That(offTanks[0], Does.StartWith("Your Drainage tether:"));
            Assert.That(offTanks[1], Does.StartWith("Your Liquid Hand: it stays put while you are within 5 y of it, and beyond that walks after you until it is 4 y from you")
                .And.Contain("hold it more than 17 y from the Living Liquid until it shows its palm")
                .And.Contain("walk on through the middle until it stands on the Living Liquid"));
            Assert.That(Plan(MainTank, JobId.Warrior).ChatLines(ProteanOne).Where(line => line.Contains("palm")), Is.Empty);
            Assert.That(Plan(OffTank, JobId.Paladin).ChatLines(ProteanOne).Where(line => line.Contains("palm")), Is.Empty);
            Assert.That(Plan(MeleeDpsA, JobId.Dragoon).ChatLines(AfterThePull), Is.Empty);
        });
    }

    // -- The pull --

    [Test]
    public void AHumanTanksInATanksSeatOnATanksJob([Values(MainTank, OffTank)] PartyRole seat,
        [Values(JobId.Warrior, JobId.Paladin, JobId.DarkKnight, JobId.Gunbreaker)] JobId job)
        => Assert.That(Plan(seat, job).HumanTanks, Is.True);

    // Each boss walks after its seat whatever the job in it, so the seat's spots are still the human's: it is told
    // them, and when the run presses for it.
    [Test]
    public void AHumanOnAnotherJobInATanksSeatIsHeldToNoneOfATanksPressesAndToldItsSpots([Values(MainTank, OffTank)] PartyRole seat,
        [Values(JobId.Monk, JobId.WhiteMage, JobId.Bard, JobId.BlackMage)] JobId job)
    {
        var plan = Plan(seat, job);
        var lines = plan.ChatLines(ThePull);
        var all = string.Join(" | ", lines);

        Assert.That(plan.HumanTanks, Is.False);
        Assert.That(all, Does.Not.Contain("Provoke the Liquid Hand as").And.Not.Contain("Your invulnerability").And.Not.Contain("Provoke it back after").And.Not.Contain("Arm's Length"));
        if (seat == MainTank)
        {
            Assert.That(lines, Has.Count.EqualTo(4));
            Assert.That(lines[0], Does.StartWith("Your pull: the Living Liquid comes to 6 y from you"));
            Assert.That(lines[1], Does.StartWith("Your first hand: take the Living Liquid to the rim beside the middle tornado")
                .And.Contain("step out of it and back, or hold your ground with your anti-knockback, and be its closest player again by 37 s")
                .And.EndWith(TheMainTankLeavesTheDolls));
            Assert.That(lines[2], Does.StartWith("Your Living Liquid from 42 s: set off for the middle between 42 and 44 s, once the melee have fed their dolls")
                .And.Contain("Walk through the middle to 6 y past it"));
            Assert.That(lines[3], Is.EqualTo("Your Provoke and invulnerability: none, since your job has none: the run presses them for your seat. "
                                              + "It Provokes the Liquid Hand at 30 s if it shows a palm, which it does with the Living Liquid at the rim, "
                                              + "or as soon after as the Hand stands within Provoke's 25 y of you, and turns you invulnerable at 34 s"));
            return;
        }
        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0], Does.StartWith("Your Liquid Hand at the pull: as it splits off, take it out on the right-hand side of the open quarter until it shows its palm")
            .And.EndWith(TheOffTankLeavesTheDolls));
        Assert.That(lines[1], Does.StartWith("Your Liquid Hand from 42 s: the run Provokes it back for your seat at 38 s, since your job has no Provoke; set off for the middle between 42 and 47 s"));
    }

    [Test]
    public void ATankOutsideATanksSeatTanksNothing([Values(RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
        => Assert.That(Plan(seat, JobId.Warrior).HumanTanks, Is.False);

    // With the open quarter north, as the dolls stand whatever the layout: one in each quarter.
    [Test]
    public void EachDamageDealerTakesTheDollOfItsOwnQuarter()
    {
        var plan = Plan();

        Assert.That(new[] { PhysRangedDps, MeleeDpsB, MeleeDpsA, CasterDps }.Select(plan.DollOf),
            Is.EqualTo(new TeaP1Quarter?[] { TeaP1Quarter.Northeast, TeaP1Quarter.Southeast, TeaP1Quarter.Southwest, TeaP1Quarter.Northwest }));
        Assert.That(new[] { MainTank, OffTank, RegenHealer, ShieldHealer }.Select(plan.DollOf), Is.All.Null);
    }

    // Any seat's hit tethers a doll, a tank's Provoke on one too, and nothing in a tank's or a healer's part feeds one.
    private const string TheMainTankLeavesTheDolls =
        "Leave the Jagd Dolls' first hits to the damage dealers, a Provoke included: a doll walks after whoever hits it first, "
        + "and one on its way to you at the rim is fed to the Living Liquid whole, which kills everyone";

    private const string TheOffTankLeavesTheDolls =
        "Leave the Jagd Dolls' first hits to the damage dealers, a Provoke included: a doll walks after whoever hits it first, "
        + "and one that comes to you there is fed to the Living Liquid on its way, whole, or is a second Exhaust on you";

    // The main tank holds both bosses but for the first hand, and no move of the strat's reaches a human.
    [Test]
    public void TheMainTankIsToldItsPullItsFirstHandAndWhereToHoldTheLiquidAfterIt()
    {
        var lines = Plan(MainTank, JobId.Warrior).ChatLines(ThePull);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(3));
            Assert.That(lines[0], Does.StartWith("Your pull: the Living Liquid comes to 6 y from you and cleaves toward you at 10 s, so turn it away from the party")
                .And.Contain("Be within 6 y of the middle when Cascade lands at 18 s"));
            Assert.That(lines[1], Does.StartWith("Your first hand: take the Living Liquid to the rim beside the middle tornado, on the side of the tornado left of the open quarter")
                .And.Contain("Provoke the Liquid Hand as it shows its palm, at about 29 to 31 s")
                .And.Contain("step out of it and back, or hold your ground with Arm's Length, and be its closest player again by 37 s")
                .And.Contain("Your invulnerability between 28 and 37 s: both busters at 36 s leave Water Resistance Down, and the tornado's unseen wave on it kills at 37.5 s")
                .And.EndWith(TheMainTankLeavesTheDolls));
            Assert.That(lines[2], Does.StartWith("Your Living Liquid from 42 s: set off for the middle between 42 and 44 s, once the melee have fed their dolls. "
                                                 + "Sooner, the way in is inside two dolls' reach until the Jagd Dolls' first Exhaust at 38 s, a doll the Living Liquid passes is fed to it, "
                                                 + "and at 40 s an Embolus sets its course for where the Living Liquid stands. "
                                                 + "Later, an Embolus from the middle tornado finds the Living Liquid still at the rim at 47 s: it follows you only once you are 7 y past it. ")
                .And.Contain("Walk through the middle to 6 y past it, so that it stops on the middle, then stand within 4 y of it, between the middle tornado and the one right of the open quarter")
                .And.Contain("an Embolus from the tornado right of the open quarter passes 7 y from the middle on that side at about 52 s"));
        });
    }

    // Without the Hand from the main tank's Provoke until it leads it in, it has to be told where to wait: the middle
    // is inside two dolls' reach, and a Hand led in before the melee have fed walks over a doll that is not ready.
    [Test]
    public void TheOffTankIsToldToTakeTheHandAwayWhereToWaitAndWhenToLeadItIn()
    {
        var lines = Plan(OffTank, JobId.Paladin).ChatLines(ThePull);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0], Does.StartWith("Your Liquid Hand at the pull: as it splits off, take it out on the right-hand side of the open quarter until it shows its palm. "
                                                 + "Stand 9 to 12 y from the middle yourself: the Hand stops 4 y short of you, more than 17 y from the Living Liquid, "
                                                 + "and the main tank Provokes it then from the rim beside the middle tornado, with Provoke's 25 y: with you farther out, the Hand is out of its reach. ")
                .And.Contain("Once it has left you, wait beside the Living Liquid on the side of the tornado left of the open quarter, not near the middle")
                .And.Contain("until the Jagd Dolls' first Exhaust at 38 s the middle is inside two dolls' reach")
                .And.EndWith(TheOffTankLeavesTheDolls));
            Assert.That(lines[1], Does.StartWith("Your Liquid Hand from 42 s: Provoke it back after the busters at 36 s and before the next at 55 s, and set off for the middle between 42 and 47 s, "
                                                 + "once the melee have fed their dolls and before an Embolus from the middle tornado finds the Hand still at the rim at 48 s: ")
                .And.Contain("walk through the middle to 4 y past it, so that the Hand stops on the middle")
                .And.Contain("then stand between the middle tornado and the one left of the open quarter"));
        });
    }

    // The bots' tanks lead the bosses to the middle past the south-west doll's spot on every layout, and past no other.
    [TestCase(PhysRangedDps, JobId.Bard, "north-east", "Your tornado: the one right of the open quarter. Be its closest player for both of its waves, from the open quarter's side of it, at the rim, "
                                                       + "so that both run out of the arena along the rim: baited from its inner side they cross the arena, and whoever both hit dies. "
                                                       + "Step out of the telegraphed one and back")]
    [TestCase(CasterDps, JobId.BlackMage, "north-west", "Your tornado: the one left of the open quarter. Be its closest player for both of its waves, from the open quarter's side of it, at the rim, "
                                                        + "so that both run out of the arena along the rim: baited from its inner side they cross the arena, and whoever both hit dies. "
                                                        + "Step out of the telegraphed one and back")]
    [TestCase(MeleeDpsA, JobId.Dragoon, "south-west", "Your spot beside it: on your doll or a step away from the tornado beside it, not toward it")]
    [TestCase(MeleeDpsB, JobId.Monk, "south-east", "Your spot beside it: on your doll or a step away from the tornado beside it, not toward it")]
    public void ADamageDealerIsToldItsDollAndWhereToStand(PartyRole seat, JobId job, string quarter, string stands)
    {
        var lines = Plan(seat, job).ChatLines(ThePull);
        const string deadline = "Hit it by 38 s: from 42 s the tanks lead the bosses past it to the middle, which feeds it";

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0], Does.StartWith($"Your Jagd Doll: the one {quarter} of the middle with the open quarter north. Hit it first: a doll goes to whoever's attack "
                                                 + "lands on it first, an auto-attack too while Resolve your own actions is on, so keep the other dolls untargeted")
                .And.Contain("each reaches 8.8 y, and two at once kill")
                .And.Contain("Keep it more than 5 y from both bosses until it is under a quarter of its health: above a quarter, feeding it kills everyone")
                .And.Contain("stand on the boss's far side from it, clear of the tornadoes' water, or feed it once the bosses are on the middle"));
            Assert.That(lines[0], seat == MeleeDpsA ? Does.Contain(deadline) : Does.Not.Contain(deadline));
            Assert.That(lines[1], Does.StartWith(stands));
        });
    }

    // The middle is inside both melee dolls' reach while they stand where they appeared.
    [TestCase(RegenHealer, JobId.WhiteMage, "stay far out, in the open quarter, until their first Exhaust at 38 s")]
    [TestCase(ShieldHealer, JobId.Scholar, "stay far out, between the middle tornado and the one right of the open quarter, until their first Exhaust at 38 s")]
    public void AHealerIsToldToStayOutOfTheMiddleUntilTheFirstExhaust(PartyRole seat, JobId job, string waits)
    {
        var line = Plan(seat, job).ChatLines(ThePull).Single();

        Assert.That(line, Does.StartWith("Your wait for the Jagd Dolls: " + waits)
            .And.Contain("until then the middle is inside two dolls' reach, and two Exhausts at once kill")
            .And.Contain("Leave the dolls' first hits to the damage dealers: a doll walks after whoever hits it first, and one that comes to you makes it two where another's Exhaust reaches you")
            .And.EndWith("Then come in on the open quarter's side of the middle: the bosses stand on it from 46 s, "
                         + "and their busters at 55 s cleave toward the tanks, on the middle tornado's side"));
    }

    [Test]
    public void ThePullsLinesComeFirstAndOnlyInARunThatPlaysIt([Values] PartyRole seat)
    {
        var job = (JobId)PartyPresets.Standard[(int)seat].ClassJob;
        var (thePulls, later, whole) = (Plan(seat, job).ChatLines(ThePull), Plan(seat, job).ChatLines(AfterThePull), Plan(seat, job).ChatLines(_ => true));

        Assert.Multiple(() =>
        {
            Assert.That(thePulls, Is.Not.Empty);
            Assert.That(whole, Is.EqualTo(thePulls.Concat(later)));
            Assert.That(later.Intersect(thePulls), Is.Empty);
        });
    }

    private static bool ThePull(TeaP1LivingLiquidPart part) => part == TeaP1LivingLiquidPart.Pull;

    private static bool AfterThePull(TeaP1LivingLiquidPart part) => part != TeaP1LivingLiquidPart.Pull;

    private static bool ProteanOne(TeaP1LivingLiquidPart part) => part == TeaP1LivingLiquidPart.Protean1;

    private static bool CascadeTwo(TeaP1LivingLiquidPart part) => part == TeaP1LivingLiquidPart.Cascade2;
}
