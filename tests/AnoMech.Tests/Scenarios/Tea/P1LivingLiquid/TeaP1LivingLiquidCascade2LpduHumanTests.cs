using System.Globalization;
using System.Numerics;
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
// On every tornado layout, at either pace and up to half a second late, on its spots or a yalm wide of each one it
// baits from, nobody may die, with one exception, in the run in: a healer or a ranged is aimed at by its tornado at
// 24.2 and has to be one of the four nearest the Living Liquid at 26.3. R1 has the longest way, round the east
// tornado's water, and walking it half a second late is still farther out than the main tank; so is H2 from a yalm
// farther out, and R2 from a yalm aside is still on its way on a few rolls. The cases a rule of the fight kills are
// each their own test, and so are the tanks' that no move of the strat's covers: a main tank that never leads the
// Living Liquid back onto the middle or never turns it, and either tank short of the middle for the last hand.
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

    private static IEnumerable<string> Faults(Roll roll, Human human, ScenarioRun run, bool palm = true)
    {
        if (roll.Plays(Protean1) && human.Open1Read != roll.Open1) yield return $"the human read the first open quarter as {human.Open1Read}";
        if (run.Elapsed > roll.C2(6f) && human.Open2Read != roll.Open2) yield return $"the human read the open quarter as {human.Open2Read}";
        if (run.Passed)
            foreach (var walk in human.WalksNeverSetOffOn) yield return $"the human never set off on its walk at {walk}";
        if (human.Slides != 0) yield return $"the human was thrown {human.Slides} times";
        if (run.Passed && human.SawAPalm != palm) yield return palm ? "the human saw no palm" : "the human saw no fist";
    }

    // Walking, half a second late, these do not make the run in: R1 from its spot or a yalm farther out, and from a
    // yalm aside on most rolls, H2 from a yalm farther out, and R2 from a yalm aside on some rolls. Each has its own
    // test below.
    private static bool WalksTheRunInTooLate(PartyRole seat, TeaP1HumanPace pace, float late, TeaP1HumanStance stance = TeaP1HumanStance.Exact)
        => (pace, late) is (TeaP1HumanPace.Walking, 0.5f)
           && (seat, stance) is (PhysRangedDps, _) or (ShieldHealer, TeaP1HumanStance.AYalmOut) or (CasterDps, TeaP1HumanStance.AYalmAside);

    // A stance moves every spot the seat baits from: round the Living Liquid, beside its tornado and on the run in.
    private static IEnumerable<TestCaseData> EverySeatEitherPaceAnyReaction()
        => from seat in PerRole.All
           from pace in Enum.GetValues<TeaP1HumanPace>()
           from late in ReactionTimes
           from stance in Enum.GetValues<TeaP1HumanStance>()
           where !WalksTheRunInTooLate(seat, pace, late, stance)
           where stance == TeaP1HumanStance.Exact || (pace == TeaP1HumanPace.Walking && late != 0.25f)
           select new TestCaseData(seat, pace, late, stance).SetName($"{{m}}({seat}, {pace}, {Secs(late)}s late, {stance})");

    [TestCaseSource(nameof(EverySeatEitherPaceAnyReaction))]
    public void AHumanPlayingTheStratIsNeverCaught(PartyRole seat, TeaP1HumanPace pace, float late, TeaP1HumanStance stance)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var seed in Seeds)
        {
            var roll = CascadeTwo(open, seed);
            var (run, human) = Human.Play(roll, new TeaP1LivingLiquidHumanPlay(seat, pace, late) { Stance = stance }, seed);
            runs.Add($"seed {seed}, {roll}", run, Faults(roll, human, run));
        }

        runs.AssertNoneLostBut();
    }

    // The second set then takes the main tank for its fourth, 8.5 y out and still carrying the first set's Water
    // Resistance Down. At a quarter second late the same walk is in time on every roll.
    [TestCase(PhysRangedDps, TeaP1HumanStance.Exact, TestName = "{m}(R1, the longest way in)")]
    [TestCase(PhysRangedDps, TeaP1HumanStance.AYalmOut, TestName = "{m}(R1, from a yalm farther out)")]
    [TestCase(ShieldHealer, TeaP1HumanStance.AYalmOut, TestName = "{m}(H2, from a yalm farther out)")]
    public void AHumanWhoWalksTheRunInHalfASecondLateLosesTheMainTankToTheSecondSet(PartyRole seat, TeaP1HumanStance stance)
    {
        var problems = new List<string>();
        var (nearest, farthest) = (float.MaxValue, 0f);
        foreach (var open in Layouts)
        foreach (var seed in Seeds)
        {
            var roll = CascadeTwo(open, seed);
            var behindTheMainTankBy = float.NaN;
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, 0.5f) { Stance = stance };
            var (run, human) = Human.Play(roll, play, seed, mayLose: true, probe: p =>
            {
                if (!p.Crossed(26.28f)) return;
                var liquid = Stage.Fight(p).Position(TeaP1Boss.LivingLiquid);
                behindTheMainTankBy = TeaP1LivingLiquid.Apart(p.Member(seat)!.Position, liquid) - TeaP1LivingLiquid.Apart(p.Member(MainTank)!.Position, liquid);
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

    // From a yalm aside of its tornado spot R1's way in is a little shorter: on some rolls it is nearer the Living
    // Liquid than the main tank as the second set goes off, and nobody dies; on the rest the main tank does, as
    // above. No more of the usual 24 runs may lose than do today.
    [Test]
    public void ARangedAYalmAsideOfTheEastTornadoWhoWalksTheRunInHalfASecondLateLosesTheMainTankOnMostRolls()
    {
        var problems = new List<string>();
        var (runs, lost, lostOnTheUsualRolls) = (0, 0, 0);
        foreach (var open in Layouts)
        foreach (var seed in Seeds)
        {
            var roll = CascadeTwo(open, seed);
            var play = new TeaP1LivingLiquidHumanPlay(PhysRangedDps, TeaP1HumanPace.Walking, 0.5f) { Stance = TeaP1HumanStance.AYalmAside };
            var (run, human) = Human.Play(roll, play, seed, mayLose: true);
            runs++;
            problems.AddRange(Faults(roll, human, run).Select(fault => $"seed {seed}, {roll}: {fault}"));
            if (run.Failure is not null) problems.Add($"seed {seed}, {roll}: {run}");
            if (run.Passed) continue;
            lost++;
            if (seed <= 17 * UsualSeeds) lostOnTheUsualRolls++;
            if (run.Deaths[0] is not { Role: MainTank, ActionId: P1ActionId.ProteanWaveAtTheClosest, Time: > 27.2f and < 27.4f })
                problems.Add($"seed {seed}, {roll}: the main tank did not die first, a second after the second set: {run}");
        }

        TestContext.Out.WriteLine($"{runs} runs, {lost} with a death, {lostOnTheUsualRolls} of them on the usual rolls");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That(lostOnTheUsualRolls, Is.InRange(1, 18), "runs with a death on the usual rolls");
    }

    // R2's way in from a yalm aside of its tornado spot runs along H1's wave. On a few rolls it is still on it as the
    // second set goes off, and the two die of each other's waves. No more of the usual 24 runs may lose than do today.
    [Test]
    public void ARangedAYalmAsideWhoWalksTheRunInHalfASecondLateIsCaughtWithItsHealerOnAFewRolls()
    {
        var problems = new List<string>();
        var (runs, lost, lostOnTheUsualRolls) = (0, 0, 0);
        foreach (var open in Layouts)
        foreach (var seed in Seeds)
        {
            var roll = CascadeTwo(open, seed);
            var play = new TeaP1LivingLiquidHumanPlay(CasterDps, TeaP1HumanPace.Walking, 0.5f) { Stance = TeaP1HumanStance.AYalmAside };
            var (run, human) = Human.Play(roll, play, seed, mayLose: true);
            runs++;
            problems.AddRange(Faults(roll, human, run).Select(fault => $"seed {seed}, {roll}: {fault}"));
            if (run.Failure is not null) problems.Add($"seed {seed}, {roll}: {run}");
            if (run.Passed) continue;
            lost++;
            if (seed <= 17 * UsualSeeds) lostOnTheUsualRolls++;
            if (run.Deaths.Any(d => d is not { Role: RegenHealer or CasterDps, ActionId: P1ActionId.ProteanWaveAtTheClosest, Time: > 27.2f and < 27.4f }))
                problems.Add($"seed {seed}, {roll}: someone else died, or of something else: {run}");
        }

        TestContext.Out.WriteLine($"{runs} runs, {lost} with a death, {lostOnTheUsualRolls} of them on the usual rolls");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That(lostOnTheUsualRolls, Is.LessThanOrEqualTo(2), "runs with a death on the usual rolls");
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
        var (run, human) = Human.Play(roll, play, 17, mayLose: true, pin: overrides => overrides.Throttle[seat] = hasItItself, stopAt: 22f);

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
        var (run, human) = Human.Play(roll, play, 17, mayLose: true, pin: overrides => overrides.Throttle[seat] = hasItItself, stopAt: 22f);

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(human.Cleansed, Is.Empty);
        AssertLostToThrottle(run, 2);
    }

    private static void AssertLostToThrottle(ScenarioRun run, int deaths)
        => Assert.Multiple(() =>
        {
            Assert.That(run.Deaths, Has.Count.EqualTo(deaths), run.ToString);
            Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo((uint?)P1ActionId.LiquidGaol), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(20.5f).Within(0.1f), run.ToString);
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

    // The palm shows at 33.15 and slams at 38.25; the off tank walks through the middle with the Hand 4.1 y behind it.
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

        AssertEveryoneDied(run, P1ActionId.HandOfParting, 38.25f, TeaP1LivingLiquid.PalmFoundThemApart);
    }

    // The bot off tank has the Hand on the middle; the Living Liquid stays where its tank held it.
    [Test]
    public void AHumanMainTankWhoNeverBringsTheLiquidInLosesEveryoneToThePalm([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast)] TeaP1OpenQuarter open)
    {
        var roll = CascadeTwo(open, 68);
        var play = new TeaP1LivingLiquidHumanPlay(MainTank, TeaP1HumanPace.Walking, 0.25f) { BringsItsBoss = TeaP1HumanBringsItsBoss.Never };
        var (run, _) = Human.Play(roll, play, 68, mayLose: true);

        AssertEveryoneDied(run, P1ActionId.HandOfParting, 38.25f, TeaP1LivingLiquid.PalmFoundThemApart);
    }

    // The Living Liquid stops 6 y behind its tank and the Liquid Hand 4.1 y, and a palm needs their centres within
    // 5.1 y. The main tank's bot has the Liquid on the middle, so an off tank that stops on the middle has the Hand
    // within reach, by a yalm.
    [Test]
    public void AHumanOffTankWhoStopsOnTheMiddleHasTheHandWithinThePalmsReach([Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        var widest = 0f;
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = CascadeTwo(open, 238);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, pace, late) { BringsItsBoss = TeaP1HumanBringsItsBoss.OntoTheMiddle };
            var apart = float.NaN;
            var (run, human) = Human.Play(roll, play, 238, probe: p =>
            {
                if (p.Crossed(38.2f)) apart = TeaP1LivingLiquid.Apart(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid), Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
            });
            widest = MathF.Max(widest, apart);
            runs.Add($"{roll}, {Secs(late)}s late, the bosses {Secs(apart)} y apart", run, Faults(roll, human, run));
        }

        TestContext.Out.WriteLine($"The bosses stood up to {Secs(widest)} y apart as the palm landed");
        runs.AssertNoneLostBut();
    }

    // A main tank that stops on the middle leaves the Living Liquid 6 y short of it, and of the Hand the off tank's
    // bot has there. The start's chat line tells it to walk on through until the Liquid stands on the Hand. Everyone
    // dies by the sim's 5.1 y: the fight's own line is only known to lie between 2.9 and 7.1 y.
    [Test]
    public void AHumanMainTankWhoStopsOnTheMiddleLeavesTheLiquidShortAndLosesEveryoneToThePalm(
        [Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast, TeaP1OpenQuarter.West)] TeaP1OpenQuarter open, [Values] TeaP1HumanPace pace)
    {
        var roll = CascadeTwo(open, 238);
        var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, 0.25f) { BringsItsBoss = TeaP1HumanBringsItsBoss.OntoTheMiddle };
        var apart = float.NaN;
        var (run, _) = Human.Play(roll, play, 238, mayLose: true, probe: p =>
        {
            if (p.Crossed(38.2f)) apart = TeaP1LivingLiquid.Apart(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid), Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
        });

        TestContext.Out.WriteLine($"The bosses stood {Secs(apart)} y apart as the palm landed");
        Assert.That(apart, Is.InRange(P1Rules.PalmNeedsThemWithin, 7f));
        AssertEveryoneDied(run, P1ActionId.HandOfParting, 38.25f, TeaP1LivingLiquid.PalmFoundThemApart);
    }

    // The other tank's bot stands its boss's stopping distance past the middle, on the side this tank comes in from.
    // A tank that stops a yalm beside it is short of the middle, and its boss stops behind it, out of the other's
    // reach. The start's chat line tells each tank to walk on through.
    [Test]
    public void AHumanTankWhoStopsBesideTheOtherTankLosesEveryoneToThePalm([Values(MainTank, OffTank)] PartyRole seat,
        [Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast, TeaP1OpenQuarter.West)] TeaP1OpenQuarter open)
    {
        var roll = CascadeTwo(open, 255);
        var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, 0.25f) { BringsItsBoss = TeaP1HumanBringsItsBoss.BesideTheOtherTank };
        var apart = float.NaN;
        var (run, _) = Human.Play(roll, play, 255, mayLose: true, probe: p =>
        {
            if (p.Crossed(38.2f)) apart = TeaP1LivingLiquid.Apart(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid), Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
        });

        TestContext.Out.WriteLine($"The bosses stood {Secs(apart)} y apart as the palm landed");
        AssertEveryoneDied(run, P1ActionId.HandOfParting, 38.25f, TeaP1LivingLiquid.PalmFoundThemApart);
    }

    // A fist. The off tank waits for the glow on the middle, the Hand 4.1 y behind it and less than 15.6 y from the
    // Living Liquid, which the main tank's bot holds north-east: under a fist that bot stays where it is. The bosses
    // are still more than the 12 y a fist asks for apart, so an off tank that stays loses nobody.
    [Test]
    public void AHumanOffTankWhoWaitsInTheMiddleIsShownAFistAndStayingLosesNobody([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast)] TeaP1OpenQuarter open)
    {
        var roll = CascadeTwo(open, 272);
        var play = new TeaP1LivingLiquidHumanPlay(OffTank, TeaP1HumanPace.Walking, 0.25f) { WaitsForTheGlowInTheMiddle = true };
        var apart = float.NaN;
        var (run, human) = Human.Play(roll, play, 272, probe: p =>
        {
            if (p.Crossed(38.2f)) apart = TeaP1LivingLiquid.Apart(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid), Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
        });

        TestContext.Out.WriteLine($"The bosses stood {Secs(apart)} y apart as the fist landed");
        Assert.That(human.SawAPalm, Is.False);
        Assert.That(apart, Is.InRange(P1Rules.FistNeedsThemBeyond, P1Rules.HandShowsAPalmBeyond));
        Assert.That(run.Passed, Is.True, run.ToString);
    }

    // Shown the fist, it walks on toward the Living Liquid as it would under a palm, and the Hand comes after it,
    // inside the 12 y.
    [Test]
    public void AHumanOffTankShownAFistWhoWalksOnTowardTheLiquidLosesEveryone([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast)] TeaP1OpenQuarter open)
    {
        var roll = CascadeTwo(open, 272);
        var play = new TeaP1LivingLiquidHumanPlay(OffTank, TeaP1HumanPace.Walking, 0.25f) { WaitsForTheGlowInTheMiddle = true, StepsAwayOnAFist = -6f };
        var (run, human) = Human.Play(roll, play, 272, mayLose: true);

        Assert.That(human.SawAPalm, Is.False);
        AssertEveryoneDied(run, P1ActionId.HandOfPrayer, 38.25f, TeaP1LivingLiquid.FistFoundThemTogether);
    }

    // It steps straight away from the Living Liquid, and the Hand follows 4.1 y behind it.
    [Test]
    public void AHumanOffTankShownAFistWhoStepsAwaySavesEveryone([Values(8f, 10f)] float steps, [Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        var (nearest, liquidMoved) = (float.MaxValue, 0f);
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = CascadeTwo(open, 289);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, pace, late) { WaitsForTheGlowInTheMiddle = true, StepsAwayOnAFist = steps };
            var apart = float.NaN;
            Vector3 liquidAtTheGlow = default;
            var (run, human) = Human.Play(roll, play, 289, probe: p =>
            {
                var fight = Stage.Fight(p);
                if (p.Crossed(33.2f)) liquidAtTheGlow = fight.Position(TeaP1Boss.LivingLiquid);
                if (!p.Crossed(38.2f)) return;
                apart = TeaP1LivingLiquid.Apart(fight.Position(TeaP1Boss.LivingLiquid), fight.Position(TeaP1Boss.LiquidHand));
                liquidMoved = MathF.Max(liquidMoved, TeaP1LivingLiquid.Apart(fight.Position(TeaP1Boss.LivingLiquid), liquidAtTheGlow));
            });
            nearest = MathF.Min(nearest, apart);
            runs.Add($"{roll}, {Secs(late)}s late, the bosses {Secs(apart)} y apart", run, Faults(roll, human, run, palm: false));
        }

        TestContext.Out.WriteLine($"The bosses stood at least {Secs(nearest)} y apart as the fist landed; the Living Liquid moved {Secs(liquidMoved)} y at most after the glow");
        Assert.That(liquidMoved, Is.LessThan(0.5f), "the main tank's bot holds the Living Liquid under a fist");
        runs.AssertNoneLostBut();
    }

    // -- The main tank at the edge of its reach --

    // It stacks on (-0.8, -7.4), where the guides draw it, just past the 7 y the Living Liquid reaches: the Liquid
    // walks 1.4 y north to it, and everyone else's spots, hung off the Liquid, go north with it.
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
                if (p.Crossed(18.2f)) draggedNorth = -roll.Frame2.ToRelative(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid)).Y;
            });
            var faults = Faults(roll, human, run).ToList();
            if (draggedNorth is not > 1.2f) faults.Add($"the Living Liquid stood {draggedNorth} y north of the middle at Protean 2's aim");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // The Living Liquid locks its facing on the main tank as the telegraphed cast starts, so its own wave, and both
    // unseen sets' wave straight ahead, go that many degrees off north.
    [Test]
    public void AHumanMainTankWhoTurnsTheLiquidOffNorthTurnsItsOwnWavesAndNobodyIsCaught([Values(-20f, -10f, 10f, 20f)] float degrees, [Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = CascadeTwo(open, 136);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { TurnsItOffNorth = degrees };
            float? lockedOff = null;
            var (run, human) = Human.Play(roll, play, 136, probe: p =>
            {
                if (p.Crossed(18.2f)) lockedOff = Turn(Stage.Fight(p).Liquid!.Rotation, roll.Frame2.Facing(0f)) * 180f / MathF.PI;
            });
            var faults = Faults(roll, human, run).ToList();
            if (lockedOff is not { } off || MathF.Abs(MathF.Abs(off) - MathF.Abs(degrees)) > 4f)
                faults.Add($"the Living Liquid locked its facing {lockedOff} degrees off north");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // The Living Liquid's own wave, and both unseen sets' wave straight ahead, go the way it faced as the bar
    // showed. This main tank never turns it: it waits for the bar where the start put it, south-east of the Liquid
    // by the first tornadoes, so the Liquid faces wherever that is among the second, `turned` eighths clockwise of
    // their open quarter, and then plays its spots.
    //
    // An unseen set's wave straight ahead kills whoever it hits, so whoever's spot is in the Liquid's face as either
    // set goes off dies of it. Whoever dies leaves a bait untaken, and the cone or the tornado's wave it would have
    // drawn goes to someone who still carries the last one: those are the second waves among each case's deaths.
    // At 135 nobody stands in its face for the first set, and M1 walks through it on the way to its tornado as the
    // second goes off. At 225 M1, aside for the telegraph, takes the Liquid's own telegraphed wave and then its cone
    // of the first set, and H2, by its tornado, is on the edge of that set's wave straight ahead.
    [TestCase(0, 0, TestName = "{m}(at the open quarter: nobody)")]
    [TestCase(1, 16, MainTank, MeleeDpsB, TestName = "{m}(45: itself on its own bait, then M2)")]
    [TestCase(2, 16, MeleeDpsB, PhysRangedDps, TestName = "{m}(90: M2, then R1)")]
    [TestCase(3, 16, MeleeDpsA, RegenHealer, ShieldHealer, PhysRangedDps, CasterDps, TestName = "{m}(135: M1, and its tornado's wave on the four in the middle)")]
    [TestCase(4, 16, MeleeDpsA, ShieldHealer, TestName = "{m}(180: M1 and H2)")]
    [TestCase(5, 16, MainTank, MeleeDpsA, ShieldHealer, TestName = "{m}(225: M1, H2 on its edge, then the main tank)")]
    [TestCase(6, 16, OffTank, CasterDps, TestName = "{m}(270: the off tank, then R2)")]
    [TestCase(7, 16, MainTank, OffTank, RegenHealer, CasterDps, TestName = "{m}(315: H1 and R2, then both tanks)")]
    public void AHumanMainTankWhoNeverTurnsTheLiquidForProteanTwoLosesWhoeverStandsInItsFace(int turned, int lost, params PartyRole[] inItsFace)
    {
        var lostRuns = 0;
        var problems = new List<string>();
        var died = new SortedSet<string>();
        var whoDied = new HashSet<PartyRole>();
        foreach (var open2 in Layouts)
        foreach (var late in new[] { 0f, 0.5f })
        {
            var open1 = Layouts[((int)open2 + 5 + turned) % 8];
            var roll = new Roll { First = Cascade2, Open1 = open1, Open2 = open2 };
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, TeaP1HumanPace.Walking, late) { NeverTurnsTheLiquid = true };
            float? faces = null;
            var (run, human) = Human.Play(roll, play, 306, mayLose: true, probe: p =>
            {
                if (p.Crossed(18.2f)) faces = Turn(Stage.Fight(p).Liquid!.Rotation, roll.Frame2.Facing(45f * turned)) * 180f / MathF.PI;
            });
            var context = $"{roll}, {Secs(late)}s late";
            if (human.Open2Read != roll.Open2) problems.Add($"{context}: the human read the open quarter as {human.Open2Read}");
            if (run.Failure is not null) problems.Add($"{context}: {run}");
            if (faces is not { } off || MathF.Abs(off) > 4f) problems.Add($"{context}: the Living Liquid faced {faces} degrees off where the start left it");
            if (run.Passed) continue;
            lostRuns++;
            foreach (var death in run.Deaths) died.Add($"{death.Role} of {death.ActionId} at {Secs(death.Time)}");
            whoDied.UnionWith(run.Deaths.Select(death => death.Role));
            if (run.Deaths.Any(death => !inItsFace.Contains(death.Role))) problems.Add($"{context}: someone else died: {run}");
            if (run.Deaths[0] is not { ActionId: P1ActionId.ProteanWaveFromTheLiquid or P1ActionId.ProteanWaveAtAPlayer or P1ActionId.ProteanWaveStraightAhead or P1ActionId.ProteanWaveAtTheClosest })
                problems.Add($"{context}: the first death was not one of the Living Liquid's waves: {run}");

        }

        TestContext.Out.WriteLine($"Turned {45 * turned} degrees off the open quarter: {lostRuns} of 16 runs with a death");
        foreach (var death in died) TestContext.Out.WriteLine(death);
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That(whoDied, Is.EquivalentTo(inItsFace));
        Assert.That(lostRuns, Is.EqualTo(lost), "runs with a death");
    }

    // -- A new puddle --

    // It waits out the Cascade where it stood for Drainage: the six 7 to 9.4 y north of the middle in the first
    // tornadoes' frame, the off tank 7.6 y south-west. A new puddle reaches in to 7.4 y on its tornado's bearing and
    // kills from a second after it appears. M1, 7 y out, is inside every new puddle's reach.
    [Test]
    public void AHumanWhoWaitsWhereItStoodForDrainageStepsOutOfANewPuddleInTime(
        [Values(OffTank, ShieldHealer, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
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
                if (!p.Crossed(7.35f)) return;
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

    // Its fetch has dragged the Living Liquid off the middle, by up to a yalm and a half, and where that is more than
    // it lets pass it leads it back and turns it north this long after the strat's beat: from 4.5 s late the Liquid
    // is still on its way as the two stacks are first taken, at 12.0. The stacks follow it until half a second before
    // Protean 2's bar, so the telegraphs, the spots round the Liquid and the ones beside the tornadoes still fit.
    [Test]
    public void AHumanMainTankWhoLeadsTheLiquidBackLateStillHasTheStacksOnItsLine([Values(2f, 4f, 4.5f, 6f, 7f)] float leadsItBackLate)
    {
        var runs = new TeaP1HumanRuns();
        var (movedAfterTheStacks, farthestOff) = (0, 0f);
        foreach (var seed in new[] { 153, 170, 187, 204, 221 })
        for (var i = 0; i < 8; i++)
        {
            var roll = BothParts(i, seed);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, TeaP1HumanPace.Walking, ReactionTimes[i % 3]) { LeadsItBackLate = leadsItBackLate };
            Vector3 asTheStacksWereTaken = default, atTheAim = default;
            var (run, human) = Human.Play(roll, play, seed, probe: p =>
            {
                if (p.Crossed(roll.C2(12.0f))) asTheStacksWereTaken = Stage.Fight(p).Position(TeaP1Boss.LivingLiquid);
                if (p.Crossed(roll.C2(18.2f))) atTheAim = Stage.Fight(p).Position(TeaP1Boss.LivingLiquid);
            });
            if (Vector3.Distance(asTheStacksWereTaken, atTheAim) > LedBackFromBeyond) movedAfterTheStacks++;
            farthestOff = MathF.Max(farthestOff, atTheAim.Length());
            runs.Add($"seed {seed}, {roll}, the Living Liquid at {asTheStacksWereTaken} as the stacks were taken and {atTheAim} at the aim", run, Faults(roll, human, run));
        }

        TestContext.Out.WriteLine($"the Living Liquid moved more than {Secs(LedBackFromBeyond)} y after 12.0 in {movedAfterTheStacks} runs and stood up to {Secs(farthestOff)} y off the middle at the aim");
        if (leadsItBackLate >= 4.5f) Assert.That(movedAfterTheStacks, Is.GreaterThanOrEqualTo(8), "runs in which the Living Liquid was still being led back after 12.0");
        Assert.That(farthestOff, Is.LessThan(LedBackFromBeyond + 0.1f), "the tank had it back on the middle by the aim, or had let it pass");
        runs.AssertNoneLostBut();
    }

    // The human leads back a Living Liquid it sees more than this off the middle, as the strat's own main tank does.
    private const float LedBackFromBeyond = 0.7f;

    // How the main tank's own Drainage tether comes to it in a run of both parts: with both tethers starting on the
    // stack; once the off tank has taken the west one, which started on the main tank itself, off it; or off the off
    // tank, on whom its own, the middle one, started.
    public enum TheMainTanksTether { FromTheStack, OnceItIsRidOfTheOther, OffTheOffTank }

    private static Roll WithItsTether(Roll roll, TheMainTanksTether comes, PartyRole onTheStack) => comes switch
    {
        TheMainTanksTether.OnceItIsRidOfTheOther
            => roll with { Drainage = Tethers((TeaP1Tornado.West, TeaP1Tornado.East), StartsOn.TheMainTankAndTheStack, onTheStack, onTheStack) },
        TheMainTanksTether.OffTheOffTank
            => roll with { Drainage = Tethers((TeaP1Tornado.West, TeaP1Tornado.Middle), StartsOn.TheStackAndTheOffTank, onTheStack, onTheStack) },
        _ => roll,
    };

    // No move of the strat's reaches a human, and the Living Liquid walks after its tank from 7 y. This main tank
    // fetches its tether where its line is nearest, takes it to its spot and later turns the Liquid north where it
    // stands, never leading it back: it is left up to a yalm and a half off the middle for Protean 2, whose spots
    // round it and beside the tornadoes are spaced for a Liquid on the middle. That close to the middle nobody dies,
    // however its tether comes to it. The start's chat line still asks the main tank to lead it back.
    [Test]
    public void AHumanMainTankWhoLeavesTheLiquidWhereItsFetchPutItLosesNobody([Values] TheMainTanksTether comes)
    {
        var left = LeavingTheLiquidWhereItsFetchPutIt(comes, byItsTornado: false);

        Assert.That(left.Problems, Is.Empty, () => string.Join(Environment.NewLine, left.Problems.Take(12)));
        Assert.That(left.FarthestOff, Is.InRange(1f, 2f), "the fetch drags the Living Liquid");
        Assert.That(left.Lost, Is.Zero, "runs with a death");
    }

    // This one takes its tether as far out along it as the tornado's water lets it, 8 y from the middle, and the
    // Living Liquid comes after it, to 6 y from it: left there, some 2 y off the middle, it still costs nobody. The
    // bots' own fetch can drag it farther, to 2.7 y; no human here leaves it that far off.
    [Test]
    public void AHumanMainTankWhoTakesItsTetherByItsTornadoAndLeavesTheLiquidThereLosesNobody([Values] TheMainTanksTether comes)
    {
        var left = LeavingTheLiquidWhereItsFetchPutIt(comes, byItsTornado: true);

        Assert.That(left.Problems, Is.Empty, () => string.Join(Environment.NewLine, left.Problems.Take(12)));
        Assert.That(left.FarthestOff, Is.InRange(1.7f, 3f), "the fetch drags the Living Liquid");
        Assert.That(left.Lost, Is.Zero, "runs with a death");
    }

    // Forty runs of both parts.
    private static (List<string> Problems, int Lost, float FarthestOff) LeavingTheLiquidWhereItsFetchPutIt(TheMainTanksTether comes, bool byItsTornado)
    {
        var problems = new List<string>();
        var (lost, farthestOff) = (0, 0f);
        foreach (var seed in new[] { 153, 170, 187, 204, 221 })
        for (var i = 0; i < 8; i++)
        {
            var roll = WithItsTether(BothParts(i, seed), comes, (i + seed) % 2 == 0 ? PhysRangedDps : CasterDps);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, TeaP1HumanPace.Walking, ReactionTimes[i % 3])
            {
                LeavesTheLiquidWhereItStands = true, TakesItsTetherByItsTornado = byItsTornado,
            };
            var offTheMiddle = float.NaN;
            var (run, human) = Human.Play(roll, play, seed, mayLose: true, probe: p =>
            {
                if (p.Crossed(roll.C2(18.2f))) offTheMiddle = Stage.Fight(p).Position(TeaP1Boss.LivingLiquid).Length();
            });
            var context = $"seed {seed}, {roll}, the Living Liquid {Secs(offTheMiddle)} y off the middle";
            farthestOff = MathF.Max(farthestOff, offTheMiddle);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{context}: {fault}"));
            if (run.Failure is not null) problems.Add($"{context}: {run}");
            if (run.Passed) continue;
            lost++;
            TestContext.Out.WriteLine($"{context}: {string.Join(", ", run.Deaths.Select(d => $"{d.Role} of {d.ActionId} at {Secs(d.Time - Stage.CascadeTwo)}"))}");
            problems.Add($"{context}: {run}");
        }

        TestContext.Out.WriteLine($"{lost} of 40 runs with a death; the Living Liquid stood up to {Secs(farthestOff)} y off the middle at Protean 2's aim");
        return (problems, lost, farthestOff);
    }
}
