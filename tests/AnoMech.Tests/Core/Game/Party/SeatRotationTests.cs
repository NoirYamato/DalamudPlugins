using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The Role pick Rotate: tanks alternate MT and OT and melee M1 and M2 from one local run to the next, from the seat Auto
// gives their job; healers and ranged keep their job's seat.
public class SeatRotationTests
{
    [TestCase(JobId.Warrior, MainTank, OffTank)]
    [TestCase(JobId.DarkKnight, MainTank, OffTank)]
    [TestCase(JobId.Gunbreaker, MainTank, OffTank)]
    [TestCase(JobId.Paladin, OffTank, MainTank)]
    [TestCase(JobId.Monk, MeleeDpsA, MeleeDpsB)]
    [TestCase(JobId.Dragoon, MeleeDpsB, MeleeDpsA)]
    [TestCase(JobId.Samurai, MeleeDpsB, MeleeDpsA)]
    [TestCase(JobId.Ninja, MeleeDpsB, MeleeDpsA)]
    [TestCase(JobId.Reaper, MeleeDpsB, MeleeDpsA)]
    [TestCase(JobId.Viper, MeleeDpsB, MeleeDpsA)]
    public void TanksAndMeleeAlternateFromTheSeatAutoGives(JobId job, PartyRole first, PartyRole second)
        => Assert.That(Seats(job, 4), Is.EqualTo(new[] { first, second, first, second }));

    [TestCase(JobId.WhiteMage, RegenHealer)]
    [TestCase(JobId.Astrologian, RegenHealer)]
    [TestCase(JobId.Scholar, ShieldHealer)]
    [TestCase(JobId.Sage, ShieldHealer)]
    [TestCase(JobId.Bard, PhysRangedDps)]
    [TestCase(JobId.Machinist, PhysRangedDps)]
    [TestCase(JobId.Dancer, PhysRangedDps)]
    [TestCase(JobId.BlackMage, CasterDps)]
    [TestCase(JobId.Pictomancer, CasterDps)]
    public void HealersAndRangedKeepTheirJobsSeat(JobId job, PartyRole seat)
        => Assert.That(Seats(job, 3), Is.All.EqualTo(seat));

    // A tank who changes job goes on alternating rather than starting over from the new job's seat (a PLD's is OT).
    [Test]
    public void AJobChangeGoesOnAlternating()
    {
        var rotation = new SeatRotation();
        rotation.Played(rotation.Next((uint)JobId.Warrior));
        rotation.Played(rotation.Next((uint)JobId.Warrior));

        Assert.That(rotation.Next((uint)JobId.Paladin), Is.EqualTo(MainTank));
    }

    // The seat SetupMemory keeps: the last run's, a tank's or a melee's; a healer or ranged run after it leaves it be.
    [Test]
    public void LastIsTheLastTankOrMeleeSeatGiven()
    {
        var rotation = new SeatRotation();
        var before = rotation.Last;
        rotation.Played(rotation.Next((uint)JobId.Warrior));
        rotation.Played(rotation.Next((uint)JobId.Dragoon));
        rotation.Played(rotation.Next((uint)JobId.WhiteMage));
        rotation.Played(rotation.Next((uint)JobId.Bard));

        Assert.That(before, Is.Null);
        Assert.That(rotation.Last, Is.EqualTo(MeleeDpsB));
    }

    // Each pair keeps its own turn: a melee run in between leaves the tanks' alone.
    [Test]
    public void EachPairKeepsItsOwnTurn()
    {
        var rotation = new SeatRotation();
        rotation.Played(rotation.Next((uint)JobId.Warrior));
        rotation.Played(rotation.Next((uint)JobId.Dragoon));

        Assert.That(rotation.Next((uint)JobId.Warrior), Is.EqualTo(OffTank));
        Assert.That(rotation.Next((uint)JobId.Dragoon), Is.EqualTo(MeleeDpsA));
    }

    [Test]
    public void AutoRestartedRunsTakeTheNextSeat()
    {
        using var run = new PracticeRun(PracticeRun.Clean, job: JobId.Dragoon);
        run.Practice.AutoRestart = true;

        run.StartInZone(new RunScenarioParams(run.Scenario, new RotatingPartyProvider(new SeatRotation(), false), null, 0));
        run.Until(() => run.Started.Count == 3);

        Assert.That(run.Started.Select(s => s.Seat), Is.EqualTo(new[] { MeleeDpsB, MeleeDpsA, MeleeDpsB }));
    }

    // A replayed roll is played in the seat it was played in, and the rotation goes on from where it was.
    [Test]
    public void ARetryKeepsItsSeatAndTheRotationGoesOn()
    {
        var rotation = new SeatRotation();
        using var run = new PracticeRun(PracticeRun.Clean, job: JobId.Warrior);
        var rotating = new RunScenarioParams(run.Scenario, new RotatingPartyProvider(rotation, false), null, 0);
        run.StartInZone(rotating);
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);
        run.Game.StartScenario(rotating);
        run.Until(() => run.Started.Count == 2 && run.Game.Status == SessionStatus.Succeeded);

        run.Game.StartScenario(run.Practice.Rolls.Replay());
        run.Until(() => run.Started.Count == 3 && run.Game.Status == SessionStatus.Succeeded);
        run.Game.StartScenario(rotating);
        run.Until(() => run.Started.Count == 4);

        Assert.That(run.Started.Select(s => s.Seat), Is.EqualTo(new[] { MainTank, OffTank, OffTank, MainTank }));
    }

    // The seats a player on `job` gets over `runs` runs, each started where Next said.
    private static IEnumerable<PartyRole> Seats(JobId job, int runs)
    {
        var rotation = new SeatRotation();
        for (var i = 0; i < runs; i++)
        {
            var seat = rotation.Next((uint)job);
            rotation.Played(seat);
            yield return seat;
        }
    }
}
