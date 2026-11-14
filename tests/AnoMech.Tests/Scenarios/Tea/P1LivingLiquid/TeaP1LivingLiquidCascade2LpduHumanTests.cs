using System.Globalization;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaP1LivingLiquidLpduAiTests;
using static AnoMech.Tests.TeaP1LivingLiquidScenarioTests;
using Human = AnoMech.Tests.TeaP1LivingLiquidHuman;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// Cascade 2 to the kill with the player's seat a human from the first frame (TeaP1LivingLiquidHuman has how it plays).
// It starts inside every new puddle's reach, where a run from Cascade 2 puts it, reads its north off the tornadoes as
// they appear and walks from each cue: to the stack once Throttle is out, out of the telegraphs as their bars show,
// in or out as each set lands and each tornado aims, apart after the tornadoes' last waves and in as the palm shows.
// A healer presses its Esunas; the main tank brings the Living Liquid back onto the middle and turns it north.
//
// On every tornado layout, at either pace and up to half a second late, nobody may die, with one exception, in the
// run in: a healer or a ranged is aimed at by its tornado at 24.2 and has to be one of the four nearest the Living
// Liquid at 26.3. R1 has the longest way, round the east tornado's water, and walking it half a second late is still
// farther out than the main tank. The cases a rule of the fight kills are each their own test.
//
// The last tests play both parts in one run, with the walk from Drainage into the middle between them.
public class TeaP1LivingLiquidCascade2LpduHumanTests
{
    // Three rolls per layout; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private const int UsualSeeds = 3;
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", UsualSeeds)).Select(i => 17 * i);
    private static readonly float[] ReactionTimes = [0f, 0.25f, 0.5f];
    private static readonly TeaP1OpenQuarter[] Layouts = Enum.GetValues<TeaP1OpenQuarter>();
    private static readonly PartyRole[] Healers = [RegenHealer, ShieldHealer];

    private static string Secs(float seconds) => seconds.ToString("0.##", CultureInfo.InvariantCulture);

    // The first tornadoes, which a run from here never shows, only turn where everyone starts.
    private static Roll CascadeTwo(TeaP1OpenQuarter open2, int seed)
        => new() { First = Cascade2, Open1 = Layouts[((int)open2 + 3 + seed) % 8], Open2 = open2 };

    private static IEnumerable<string> Faults(Roll roll, Human human, ScenarioRun run)
    {
        if (roll.Plays(Protean1) && human.Open1Read != roll.Open1) yield return $"the human read the first open quarter as {human.Open1Read}";
        if (run.Elapsed > roll.C2(6f) && human.Open2Read != roll.Open2) yield return $"the human read the open quarter as {human.Open2Read}";
        if (run.Passed)
            foreach (var walk in human.WalksNeverSetOffOn) yield return $"the human never set off on its walk at {walk}";
        if (human.Slides != 0) yield return $"the human was thrown {human.Slides} times";
        if (run.Passed && human.SawAPalm != true) yield return "the human saw no palm";
    }

    private static bool WalksTheRunInTooLate(PartyRole seat, TeaP1HumanPace pace, float late)
        => (seat, pace, late) is (PhysRangedDps, TeaP1HumanPace.Walking, 0.5f);

    private static IEnumerable<TestCaseData> EverySeatEitherPaceAnyReaction()
        => from seat in PerRole.All
           from pace in Enum.GetValues<TeaP1HumanPace>()
           from late in ReactionTimes
           where !WalksTheRunInTooLate(seat, pace, late)
           select new TestCaseData(seat, pace, late).SetName($"{{m}}({seat}, {pace}, {Secs(late)}s late)");

    [TestCaseSource(nameof(EverySeatEitherPaceAnyReaction))]
    public void AHumanPlayingTheStratIsNeverCaught(PartyRole seat, TeaP1HumanPace pace, float late)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var seed in Seeds)
        {
            var roll = CascadeTwo(open, seed);
            var (run, human) = Human.Play(roll, new TeaP1LivingLiquidHumanPlay(seat, pace, late), seed);
            runs.Add($"seed {seed}, {roll}", run, Faults(roll, human, run));
        }

        runs.AssertNoneLostBut();
    }

    // The second set then takes the main tank for its fourth, 8.5 y out and still carrying the first set's Water
    // Resistance Down. At a quarter second late the same walk is in time on every roll.
    [Test]
    public void ARangedWhoWalksTheLongestRunInHalfASecondLateLosesTheMainTankToTheSecondSet()
    {
        var problems = new List<string>();
        var (nearest, farthest) = (float.MaxValue, 0f);
        foreach (var open in Layouts)
        foreach (var seed in Seeds)
        {
            var roll = CascadeTwo(open, seed);
            var behindTheMainTankBy = float.NaN;
            var (run, human) = Human.Play(roll, new TeaP1LivingLiquidHumanPlay(PhysRangedDps, TeaP1HumanPace.Walking, 0.5f), seed, mayLose: true, probe: p =>
            {
                if (!p.Crossed(26.28f)) return;
                var liquid = Stage.Fight(p).Position(TeaP1Boss.LivingLiquid);
                behindTheMainTankBy = TeaP1LivingLiquid.Apart(p.Member(PhysRangedDps)!.Position, liquid) - TeaP1LivingLiquid.Apart(p.Member(MainTank)!.Position, liquid);
            });
            (nearest, farthest) = (MathF.Min(nearest, behindTheMainTankBy), MathF.Max(farthest, behindTheMainTankBy));
            problems.AddRange(Faults(roll, human, run).Select(fault => $"seed {seed}, {roll}: {fault}"));
            if (run.Failure is not null || run.Deaths.Count == 0
                || run.Deaths[0] is not { Role: MainTank, ActionId: P1ActionId.ProteanWaveAtTheClosest, Time: > 27.2f and < 27.4f })
                problems.Add($"seed {seed}, {roll}: the main tank did not die first, a second after the second set: {run}");
        }

        TestContext.Out.WriteLine($"The human stood {Secs(nearest)} to {Secs(farthest)} y farther from the Living Liquid than the main tank as the second set went off");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // -- Throttle --

    // Its three Esunas, 2.5 s apart from 0.7 s after Throttle lands, or one or two of them later: the other healer's
    // bot gets to four by itself. In its own order or the other healer's, with Throttle on itself or not.
    [Test]
    public void AHumanHealersEsunasAtItsGcdsPaceSaveEveryone([ValueSource(nameof(Healers))] PartyRole seat,
        [Values(TeaP1HumanCleanses.InItsOwnOrder, TeaP1HumanCleanses.InTheOtherHealersOrder)] TeaP1HumanCleanses order,
        [Values(0, 1, 2)] int gcdsLate, [Values] bool hasItItself)
    {
        var runs = new TeaP1HumanRuns();
        for (var i = 0; i < 8; i++)
        {
            var roll = CascadeTwo(Layouts[i], 17 * (i + gcdsLate));
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, i % 2 == 0 ? 0f : 0.5f) { Cleanses = order, GcdsLate = gcdsLate };
            var seed = 17 * (i + 1);
            var (run, human) = Human.Play(roll, play, seed, pin: overrides => overrides.Throttle[seat] = hasItItself);
            var faults = Faults(roll, human, run).ToList();
            if (human.Cleansed.Count < (gcdsLate == 2 ? 2 : 3 - gcdsLate)) faults.Add($"the human only cleansed {string.Join(", ", human.Cleansed)}");
            if (human.Cleansed.Distinct().Count() != human.Cleansed.Count) faults.Add($"the human cleansed someone twice: {string.Join(", ", human.Cleansed)}");
            runs.Add($"seed {seed}, {roll}, {Secs(play.Late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // The bot's four and the human's one make five of the six.
    [Test]
    public void AHumanHealerThreeGcdsLateLosesOneToThrottle([ValueSource(nameof(Healers))] PartyRole seat, [Values] bool hasItItself)
    {
        var roll = CascadeTwo(Layouts[(int)seat], 17);
        var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, 0.25f) { GcdsLate = 3 };
        var (run, human) = Human.Play(roll, play, 17, mayLose: true, pin: overrides => overrides.Throttle[seat] = hasItItself, stopAt: 21f);

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(human.Cleansed, Has.Count.EqualTo(1));
        AssertLostToThrottle(run, 1);
    }

    // The other healer's bot cleanses itself and then up the party list, four in all.
    [Test]
    public void AHumanHealerWhoNeverCleansesLosesTwoToThrottle([ValueSource(nameof(Healers))] PartyRole seat, [Values] bool hasItItself)
    {
        var roll = CascadeTwo(Layouts[(int)seat + 2], 17);
        var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, 0.25f) { Cleanses = TeaP1HumanCleanses.Never };
        var (run, human) = Human.Play(roll, play, 17, mayLose: true, pin: overrides => overrides.Throttle[seat] = hasItItself, stopAt: 21f);

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(human.Cleansed, Is.Empty);
        AssertLostToThrottle(run, 2);
    }

    private static void AssertLostToThrottle(ScenarioRun run, int deaths)
        => Assert.Multiple(() =>
        {
            Assert.That(run.Deaths, Has.Count.EqualTo(deaths), run.ToString);
            Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo((uint?)P1ActionId.LiquidGaol), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(19.5f).Within(0.1f), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1LivingLiquid.ThrottleRanOut), run.ToString);
        });

    // Held to no cleanse, it presses none: the run makes its seat's, on the bots' times.
    [Test]
    public void AHumanOnAJobWithoutEsunaInAHealersSeatPlaysItsSpotsAndTheRunCleanses([ValueSource(nameof(Healers))] PartyRole seat,
        [Values(JobId.Monk, JobId.Paladin, JobId.BlackMage)] JobId job)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = CascadeTwo(open, 34);
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Sprinting, 0.25f) { Job = job };
            var (run, human) = Human.Play(roll, play, 34);
            var faults = Faults(roll, human, run).ToList();
            if (human.Cleansed.Count > 0) faults.Add("the human pressed Esuna");
            runs.Add(roll.ToString(), run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // -- The last hand --

    // The palm shows at 33.2 and slams at 38.4; the off tank has 19.5 y to walk with the Hand 2.1 y behind it.
    [Test]
    public void AHumanOffTankBringsTheHandInUpToTwoSecondsLate([Values(0f, 1f, 2f)] float setsOffLate, [Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = CascadeTwo(open, 51);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, pace, 0f) { BringsTheHandIn = setsOffLate };
            var (run, human) = Human.Play(roll, play, 51);
            runs.Add(roll.ToString(), run, Faults(roll, human, run));
        }

        runs.AssertNoneLostBut();
    }

    [Test]
    public void AHumanOffTankWhoNeverBringsTheHandInLosesEveryoneToThePalm([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast)] TeaP1OpenQuarter open)
    {
        var roll = CascadeTwo(open, 68);
        var play = new TeaP1LivingLiquidHumanPlay(OffTank, TeaP1HumanPace.Walking, 0.25f) { BringsTheHandIn = null };
        var (run, _) = Human.Play(roll, play, 68, mayLose: true);

        AssertEveryoneDied(run, P1ActionId.HandOfPrayer, 38.4f, TeaP1LivingLiquid.PalmFoundThemApart);
    }

    // -- The main tank at the edge of its reach --

    // It stacks on (-0.8, -7.4), where the guides draw it: the Living Liquid walks 3.4 y north to it, and everyone
    // else's spots, hung off the Liquid, go north with it.
    [Test]
    public void AHumanMainTankOnTheGuidesStackSpotDragsTheLiquidNorthAndNobodyIsCaught([Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = CascadeTwo(open, 85);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { MaxMelee = true };
            float? draggedNorth = null;
            var (run, human) = Human.Play(roll, play, 85, probe: p =>
            {
                if (p.Crossed(18.25f)) draggedNorth = -roll.Frame2.ToRelative(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid)).Y;
            });
            var faults = Faults(roll, human, run).ToList();
            if (draggedNorth is not > 3f) faults.Add($"the Living Liquid stood {draggedNorth} y north of the middle at Protean 2's aim");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // -- A new puddle --

    // It waits out the Cascade where it stood for Drainage: the six 7 to 9.4 y north of the middle in the first
    // tornadoes' frame, the off tank 7.6 y south-west. A new puddle reaches in to 6.9 y on its tornado's bearing and
    // kills from a second after it appears.
    [Test]
    public void AHumanWhoWaitsWhereItStoodForDrainageStepsOutOfANewPuddleInTime(
        [Values(OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
    {
        var runs = new TeaP1HumanRuns();
        var wet = 0;
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = new Roll { First = Cascade2, Open1 = TeaP1OpenQuarter.North, Open2 = open };
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Sprinting, late) { StaysOutForTheCascade = true };
            var (run, human) = Human.Play(roll, play, 102, probe: p =>
            {
                if (!p.Crossed(5.95f)) return;
                var at = p.Member(seat)!.Position;
                if (Enum.GetValues<TeaP1Tornado>().Any(tornado => TeaP1LivingLiquid.Apart(at, roll.Frame2.Centre(tornado)) < P1Geometry.PuddleRadius)) wet++;
            });
            runs.Add($"{roll}, {Secs(late)}s late", run, Faults(roll, human, run));
        }

        TestContext.Out.WriteLine($"{wet} of them with the human in a new puddle as it appeared");
        Assert.That(wet, Is.GreaterThanOrEqualTo(3), "runs with the human in a new puddle as it appeared");
        runs.AssertNoneLostBut();
    }

    // -- Both parts --

    private static Roll BothParts(int i, int seed)
    {
        var protean = new Random(seed + i);
        var first = protean.Next(6);
        return new Roll
        {
            Open1 = Layouts[i],
            Open2 = Layouts[(i + 1 + 2 * (seed % 3)) % 8],
            Drainage = Tethers(TetheringPairs[protean.Next(3)], StartsOn.TheStack, TheSix[first], TheSix[(first + 1 + protean.Next(5)) % 6]),
        };
    }

    [Test]
    public void AHumanPlaysBothPartsThrough([Values] PartyRole seat, [Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        for (var i = 0; i < 8; i++)
        {
            var seed = 17 * (1 + (int)seat);
            var roll = BothParts(i, seed);
            var late = ReactionTimes[i % 3];
            if (WalksTheRunInTooLate(seat, pace, late)) late = 0.25f;
            var (run, human) = Human.Play(roll, new TeaP1LivingLiquidHumanPlay(seat, pace, late), seed);
            runs.Add($"seed {seed}, {roll}, {Secs(late)}s late", run, Faults(roll, human, run));
        }

        runs.AssertNoneLostBut();
    }

    // From Protean 1's second set it stands 7.4 y south-east of the middle and takes its tether from there, so the
    // Living Liquid is well off the middle as Cascade 2 lands: the tank walks it back and turns it north by eye.
    [Test]
    public void AHumanMainTankAtTheEdgeOfItsReachPlaysBothPartsThrough([Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        for (var i = 0; i < 8; i++)
        {
            var roll = BothParts(i, 119);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, ReactionTimes[i % 3]) { MaxMelee = true };
            var (run, human) = Human.Play(roll, play, 119);
            runs.Add($"{roll}, {Secs(play.Late)}s late", run, Faults(roll, human, run));
        }

        runs.AssertNoneLostBut();
    }
}
