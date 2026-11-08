using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6LimitBreakDuty;

namespace AnoMech.Tests;

// Who presses each limit break 3 of the phase. The bots always play MT WAR, OT PLD, H1 WHM, H2 SCH, M1 DRG, M2 MNK, R1 BRD,
// R2 BLM; the human takes one seat with their own job. Tanks rank WAR > DRK > PLD > GNB and melee SAM > DRG > MNK > VPR >
// RPR > NIN; one job on both seats of a pair goes to the human, else to the earlier seat.
public class TopP6GauntletLimitBreakPlanTests
{
    [SetUp]
    public void InstallGame() => FakeGame.Install();

    // Every job in the seat its job sits in when no seat is picked.
    [TestCase(JobId.Warrior, MainTank, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(JobId.DarkKnight, MainTank, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(JobId.Gunbreaker, MainTank, new[] { MagicNumber2Tank })]
    [TestCase(JobId.Paladin, OffTank, new[] { MagicNumber2Tank })]
    [TestCase(JobId.WhiteMage, RegenHealer, new[] { MagicNumber1Healer })]
    [TestCase(JobId.Astrologian, RegenHealer, new[] { MagicNumber1Healer })]
    [TestCase(JobId.Scholar, ShieldHealer, new[] { MagicNumber2Healer })]
    [TestCase(JobId.Sage, ShieldHealer, new[] { MagicNumber2Healer })]
    [TestCase(JobId.Monk, MeleeDpsA, new[] { FirstDiveMelee, Enrage })]
    [TestCase(JobId.Samurai, MeleeDpsB, new[] { FirstDiveMelee, Enrage })]
    [TestCase(JobId.Dragoon, MeleeDpsB, new[] { FirstDiveMelee, Enrage })]
    [TestCase(JobId.Viper, MeleeDpsB, new[] { SecondDiveMelee })]
    [TestCase(JobId.Reaper, MeleeDpsB, new[] { SecondDiveMelee })]
    [TestCase(JobId.Ninja, MeleeDpsB, new[] { SecondDiveMelee })]
    [TestCase(JobId.Bard, PhysRangedDps, new[] { Meteors })]
    [TestCase(JobId.Machinist, PhysRangedDps, new[] { Meteors })]
    [TestCase(JobId.Dancer, PhysRangedDps, new[] { Meteors })]
    [TestCase(JobId.BlackMage, CasterDps, new[] { Comets })]
    [TestCase(JobId.Summoner, CasterDps, new[] { Comets })]
    [TestCase(JobId.RedMage, CasterDps, new[] { Comets })]
    [TestCase(JobId.Pictomancer, CasterDps, new[] { Comets })]
    public void EachJobInItsOwnSeat(JobId job, PartyRole seat, TopP6LimitBreakDuty[] duties)
    {
        Assert.That(PartyPresets.SkipRoleForJob((uint)job), Is.EqualTo(seat), "the seat the job sits in");
        var plan = Plan(seat, job);

        Assert.That(plan.HumanFits, Is.True);
        Assert.That(plan.DutiesOf(seat), Is.EqualTo(duties));
        Assert.That(duties.All(plan.HumanPresses), Is.True);
    }

    // Each tank job in either tank seat, beside the bot of the other: MT WAR, OT PLD.
    [TestCase(MainTank, JobId.Warrior, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MainTank, JobId.DarkKnight, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MainTank, JobId.Paladin, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MainTank, JobId.Gunbreaker, new[] { MagicNumber2Tank })]
    [TestCase(OffTank, JobId.Warrior, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(OffTank, JobId.DarkKnight, new[] { MagicNumber2Tank })]
    [TestCase(OffTank, JobId.Paladin, new[] { MagicNumber2Tank })]
    [TestCase(OffTank, JobId.Gunbreaker, new[] { MagicNumber2Tank })]
    public void EachTankJobInEitherTankSeat(PartyRole seat, JobId job, TopP6LimitBreakDuty[] duties)
    {
        var plan = Plan(seat, job);
        TopP6LimitBreakDuty[] all = [CosmoMemory, MagicNumber1Tank, MagicNumber2Tank];

        Assert.That(plan.DutiesOf(seat), Is.EqualTo(duties));
        Assert.That(plan.DutiesOf(Other(seat)), Is.EqualTo(all.Except(duties)));
    }

    // Each melee job in either melee seat, beside the bot of the other: M1 DRG, M2 MNK.
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
    public void EachMeleeJobInEitherMeleeSeat(PartyRole seat, JobId job, TopP6LimitBreakDuty[] duties)
    {
        var plan = Plan(seat, job);
        TopP6LimitBreakDuty[] all = [FirstDiveMelee, SecondDiveMelee, Enrage];

        Assert.That(plan.DutiesOf(seat), Is.EqualTo(duties));
        Assert.That(plan.DutiesOf(Other(seat)), Is.EqualTo(all.Except(duties)));
    }

    [TestCase(RegenHealer, JobId.Scholar, MagicNumber1Healer)]
    [TestCase(RegenHealer, JobId.Sage, MagicNumber1Healer)]
    [TestCase(ShieldHealer, JobId.WhiteMage, MagicNumber2Healer)]
    [TestCase(ShieldHealer, JobId.Astrologian, MagicNumber2Healer)]
    public void HealersGoBySeatWhateverTheirJob(PartyRole seat, JobId job, TopP6LimitBreakDuty duty)
        => Assert.That(Plan(seat, job).DutiesOf(seat), Is.EqualTo(new[] { duty }));

    // With the human elsewhere, a job on both seats of a pair goes to the earlier seat.
    [Test]
    public void BetweenTwoBotsOfOneJobTheEarlierSeatWins()
    {
        var jobs = Bots();
        jobs[(int)OffTank] = (uint)JobId.Warrior;
        jobs[(int)MeleeDpsB] = (uint)JobId.Dragoon;
        var plan = new TopP6GauntletLimitBreakPlan(jobs, CasterDps);

        Assert.That(plan.SeatOf(CosmoMemory), Is.EqualTo(MainTank));
        Assert.That(plan.SeatOf(Enrage), Is.EqualTo(MeleeDpsA));
    }

    // The bots alone: MT WAR over OT PLD, M1 DRG over M2 MNK.
    [Test]
    public void WithTheHumanElsewhereTheBotsFollowTheJobs()
    {
        var plan = Plan(PhysRangedDps, JobId.Bard);

        Assert.Multiple(() =>
        {
            Assert.That(plan.DutiesOf(MainTank), Is.EqualTo(new[] { CosmoMemory, MagicNumber1Tank }));
            Assert.That(plan.DutiesOf(OffTank), Is.EqualTo(new[] { MagicNumber2Tank }));
            Assert.That(plan.DutiesOf(RegenHealer), Is.EqualTo(new[] { MagicNumber1Healer }));
            Assert.That(plan.DutiesOf(ShieldHealer), Is.EqualTo(new[] { MagicNumber2Healer }));
            Assert.That(plan.DutiesOf(MeleeDpsA), Is.EqualTo(new[] { FirstDiveMelee, Enrage }));
            Assert.That(plan.DutiesOf(MeleeDpsB), Is.EqualTo(new[] { SecondDiveMelee }));
            Assert.That(plan.DutiesOf(PhysRangedDps), Is.EqualTo(new[] { Meteors }));
            Assert.That(plan.DutiesOf(CasterDps), Is.EqualTo(new[] { Comets }));
        });
    }

    [TestCase(MainTank, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber1, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MainTank, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber2, new[] { CosmoMemory, MagicNumber2Tank })]
    [TestCase(MainTank, TopP6TankLimitBreaks.MagicNumber1, new[] { MagicNumber1Tank })]
    [TestCase(MainTank, TopP6TankLimitBreaks.MagicNumber2, new[] { MagicNumber2Tank })]
    [TestCase(OffTank, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber1, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(OffTank, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber2, new[] { CosmoMemory, MagicNumber2Tank })]
    [TestCase(OffTank, TopP6TankLimitBreaks.MagicNumber1, new[] { MagicNumber1Tank })]
    [TestCase(OffTank, TopP6TankLimitBreaks.MagicNumber2, new[] { MagicNumber2Tank })]
    public void TheTankRowForcesTheHumansShareAndTheOtherTankTakesTheRest(PartyRole seat, TopP6TankLimitBreaks row, TopP6LimitBreakDuty[] duties)
    {
        var plan = Plan(seat, seat == MainTank ? JobId.Gunbreaker : JobId.Paladin, tank: row);
        TopP6LimitBreakDuty[] all = [CosmoMemory, MagicNumber1Tank, MagicNumber2Tank];

        Assert.That(plan.DutiesOf(seat), Is.EqualTo(duties));
        Assert.That(plan.DutiesOf(Other(seat)), Is.EqualTo(all.Except(duties)));
    }

    [TestCase(MeleeDpsA, TopP6MeleeLimitBreaks.FirstAndTheEnrage, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsA, TopP6MeleeLimitBreaks.SecondAndTheEnrage, new[] { SecondDiveMelee, Enrage })]
    [TestCase(MeleeDpsA, TopP6MeleeLimitBreaks.First, new[] { FirstDiveMelee })]
    [TestCase(MeleeDpsA, TopP6MeleeLimitBreaks.Second, new[] { SecondDiveMelee })]
    [TestCase(MeleeDpsB, TopP6MeleeLimitBreaks.FirstAndTheEnrage, new[] { FirstDiveMelee, Enrage })]
    [TestCase(MeleeDpsB, TopP6MeleeLimitBreaks.SecondAndTheEnrage, new[] { SecondDiveMelee, Enrage })]
    [TestCase(MeleeDpsB, TopP6MeleeLimitBreaks.First, new[] { FirstDiveMelee })]
    [TestCase(MeleeDpsB, TopP6MeleeLimitBreaks.Second, new[] { SecondDiveMelee })]
    public void TheMeleeRowForcesTheHumansShareAndTheOtherMeleeTakesTheRest(PartyRole seat, TopP6MeleeLimitBreaks row, TopP6LimitBreakDuty[] duties)
    {
        var plan = Plan(seat, JobId.Ninja, melee: row);
        TopP6LimitBreakDuty[] all = [FirstDiveMelee, SecondDiveMelee, Enrage];

        Assert.That(plan.DutiesOf(seat), Is.EqualTo(duties));
        Assert.That(plan.DutiesOf(Other(seat)), Is.EqualTo(all.Except(duties)));
    }

    [TestCase(RegenHealer, TopP6HealerLimitBreaks.MagicNumber1, MagicNumber1Healer)]
    [TestCase(RegenHealer, TopP6HealerLimitBreaks.MagicNumber2, MagicNumber2Healer)]
    [TestCase(ShieldHealer, TopP6HealerLimitBreaks.MagicNumber1, MagicNumber1Healer)]
    [TestCase(ShieldHealer, TopP6HealerLimitBreaks.MagicNumber2, MagicNumber2Healer)]
    public void TheHealerRowForcesTheHumansMagicNumberAndTheOtherHealerTakesTheOther(PartyRole seat, TopP6HealerLimitBreaks row, TopP6LimitBreakDuty duty)
    {
        var plan = Plan(seat, JobId.Sage, healer: row);
        var other = duty == MagicNumber1Healer ? MagicNumber2Healer : MagicNumber1Healer;

        Assert.That(plan.DutiesOf(seat), Is.EqualTo(new[] { duty }));
        Assert.That(plan.DutiesOf(Other(seat)), Is.EqualTo(new[] { other }));
    }

    // A row only moves the human's own pair.
    [Test]
    public void ARowForAnotherPairChangesNothing()
    {
        var plan = Plan(PhysRangedDps, JobId.Bard, TopP6TankLimitBreaks.MagicNumber2, TopP6MeleeLimitBreaks.Second, TopP6HealerLimitBreaks.MagicNumber2);
        var unforced = Plan(PhysRangedDps, JobId.Bard);

        Assert.That(Enum.GetValues<TopP6LimitBreakDuty>().Select(plan.SeatOf), Is.EqualTo(Enum.GetValues<TopP6LimitBreakDuty>().Select(unforced.SeatOf)));
    }

    // A player's Brilliant Dynamis refills once, so the two dive melees, the two Magic Number tanks and the two Magic
    // Number healers are never one player, whatever is forced, and every duty has exactly one seat.
    [Test]
    public void EverySplitKeepsEachRefillToOnePlayer(
        [Values(MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB)] PartyRole seat,
        [Values(null, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber1, TopP6TankLimitBreaks.CosmoMemoryAndMagicNumber2,
            TopP6TankLimitBreaks.MagicNumber1, TopP6TankLimitBreaks.MagicNumber2)] TopP6TankLimitBreaks? tank,
        [Values(null, TopP6MeleeLimitBreaks.FirstAndTheEnrage, TopP6MeleeLimitBreaks.SecondAndTheEnrage,
            TopP6MeleeLimitBreaks.First, TopP6MeleeLimitBreaks.Second)] TopP6MeleeLimitBreaks? melee,
        [Values(null, TopP6HealerLimitBreaks.MagicNumber1, TopP6HealerLimitBreaks.MagicNumber2)] TopP6HealerLimitBreaks? healer)
    {
        var plan = Plan(seat, (JobId)PartyPresets.Standard[(int)seat].ClassJob, tank, melee, healer);

        Assert.Multiple(() =>
        {
            Assert.That(plan.SeatOf(FirstDiveMelee), Is.Not.EqualTo(plan.SeatOf(SecondDiveMelee)));
            Assert.That(plan.SeatOf(MagicNumber1Tank), Is.Not.EqualTo(plan.SeatOf(MagicNumber2Tank)));
            Assert.That(plan.SeatOf(MagicNumber1Healer), Is.Not.EqualTo(plan.SeatOf(MagicNumber2Healer)));
            Assert.That(PerRoleSeats().SelectMany(plan.DutiesOf), Is.EquivalentTo(Enum.GetValues<TopP6LimitBreakDuty>()));
        });
    }

    // The seat counts as its bot's job, the run presses its duties, and the human is held to none.
    [TestCase(MainTank, JobId.BlackMage, JobId.Warrior, new[] { CosmoMemory, MagicNumber1Tank })]
    [TestCase(MeleeDpsB, JobId.WhiteMage, JobId.Monk, new[] { SecondDiveMelee })]
    [TestCase(PhysRangedDps, JobId.Dragoon, JobId.Bard, new[] { Meteors })]
    [TestCase(CasterDps, JobId.BlueMage, JobId.BlackMage, new[] { Comets })]
    [TestCase(RegenHealer, JobId.Paladin, JobId.WhiteMage, new[] { MagicNumber1Healer })]
    public void ASeatTheHumansJobCantPressIsTheRuns(PartyRole seat, JobId job, JobId pressedAs, TopP6LimitBreakDuty[] duties)
    {
        var plan = Plan(seat, job);

        Assert.That(plan.HumanFits, Is.False);
        Assert.That(plan.JobOf(seat), Is.EqualTo((uint)pressedAs));
        Assert.That(plan.DutiesOf(seat), Is.EqualTo(duties));
        Assert.That(Enum.GetValues<TopP6LimitBreakDuty>().Any(plan.HumanPresses), Is.False);
    }

    [Test]
    public void ASeatsOwnJobIsWhatItPressesAs()
    {
        var plan = Plan(MeleeDpsB, JobId.Samurai);

        Assert.That(plan.JobOf(MeleeDpsB), Is.EqualTo((uint)JobId.Samurai));
        Assert.That(plan.JobOf(MeleeDpsA), Is.EqualTo((uint)JobId.Dragoon));
    }

    [TestCase(MainTank, JobId.Warrior, "Your limit breaks: tank LB3 on Cosmo Memory and Magic Number 1")]
    [TestCase(OffTank, JobId.Paladin, "Your limit breaks: tank LB3 on Magic Number 2")]
    [TestCase(ShieldHealer, JobId.Sage, "Your limit breaks: healer LB3 on Magic Number 2")]
    [TestCase(MeleeDpsB, JobId.Dragoon, "Your limit breaks: melee LB3 on Alpha Omega right after Cosmo Dive 2's cast bar and during Run: ****mi*'s cast")]
    [TestCase(MeleeDpsB, JobId.Reaper, "Your limit breaks: melee LB3 on Alpha Omega from the middle once the first one's refill fills the gauge")]
    [TestCase(PhysRangedDps, JobId.Dancer, "Your limit breaks: physical ranged LB3 through both Cosmo Meteors")]
    [TestCase(CasterDps, JobId.Pictomancer, "Your limit breaks: caster LB3 on the Cosmo Comets")]
    [TestCase(MainTank, JobId.BlackMage,
        "Your limit breaks: none, since your job can't press your seat's: the Gauntlet presses its tank LB3 on Cosmo Memory and Magic Number 1 for you")]
    public void TheChatLineNamesTheHumansDuties(PartyRole seat, JobId job, string line)
        => Assert.That(Plan(seat, job).ChatLine(), Is.EqualTo(line));

    // Only the duties a run plays: from Magic Number on, a warrior main tank has only Magic Number 1 left, a caster none.
    [TestCase(MainTank, JobId.Warrior, "Your limit breaks: tank LB3 on Magic Number 1")]
    [TestCase(CasterDps, JobId.BlackMage, "Your limit breaks: none in this run")]
    public void TheChatLineNamesOnlyTheDutiesTheRunPlays(PartyRole seat, JobId job, string line)
        => Assert.That(Plan(seat, job).ChatLine(duty => duty >= MagicNumber1Tank), Is.EqualTo(line));

    // A run starting later begins with Spark of Dynamis on whoever already pressed with Brilliant Dynamis.
    [TestCase(FirstDiveMelee, new PartyRole[0])]
    [TestCase(Comets, new[] { MeleeDpsA, MeleeDpsB })]
    [TestCase(MagicNumber1Tank, new[] { MeleeDpsA, MeleeDpsB, CasterDps, PhysRangedDps })]
    [TestCase(Enrage, new[] { MeleeDpsA, MeleeDpsB, CasterDps, PhysRangedDps, MainTank, RegenHealer, OffTank, ShieldHealer })]
    public void SparkedBeforeCountsEveryEarlierPressButCosmoMemorys(TopP6LimitBreakDuty next, PartyRole[] sparked)
        => Assert.That(Plan(PhysRangedDps, JobId.Bard).SparkedBefore(next), Is.EquivalentTo(sparked));

    // The bots' jobs and the human's as the client has it.
    [Test]
    public void ForReadsTheSeatsJobs()
    {
        TopP6GauntletLimitBreakPlan? plan = null;
        var run = TimelineRun.Execute(world => world.Events.Add(1f, () =>
        {
            ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)JobId.Samurai;
            plan = TopP6GauntletLimitBreakPlan.For(world.Party);
        }), new TimelineRunOptions { PlayerRole = MeleeDpsB });

        Assert.That(run.Passed, run.ToString);
        Assert.That(plan!.HumanSeat, Is.EqualTo(MeleeDpsB));
        Assert.That(plan.HumanJob, Is.EqualTo((uint)JobId.Samurai));
        Assert.That(PerRoleSeats().Where(s => s != MeleeDpsB).Select(plan.JobOf),
                    Is.EqualTo(PerRoleSeats().Where(s => s != MeleeDpsB).Select(s => (uint)PartyPresets.Standard[(int)s].ClassJob)));
        Assert.That(plan.SeatOf(Enrage), Is.EqualTo(MeleeDpsB));
    }

    private static TopP6GauntletLimitBreakPlan Plan(PartyRole seat, JobId job,
        TopP6TankLimitBreaks? tank = null, TopP6MeleeLimitBreaks? melee = null, TopP6HealerLimitBreaks? healer = null)
    {
        var jobs = Bots();
        jobs[(int)seat] = (uint)job;
        return new TopP6GauntletLimitBreakPlan(jobs, seat, tank, melee, healer);
    }

    private static uint[] Bots() => PartyPresets.Standard.Select(preset => (uint)preset.ClassJob).ToArray();

    private static PartyRole[] PerRoleSeats() => Enum.GetValues<PartyRole>();

    private static PartyRole Other(PartyRole seat) => seat switch
    {
        MainTank => OffTank,
        OffTank => MainTank,
        RegenHealer => ShieldHealer,
        ShieldHealer => RegenHealer,
        MeleeDpsA => MeleeDpsB,
        _ => MeleeDpsA,
    };
}
