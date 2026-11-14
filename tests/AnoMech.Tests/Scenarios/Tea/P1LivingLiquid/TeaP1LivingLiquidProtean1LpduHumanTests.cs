using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
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

// Protean 1 to Drainage with the player's seat a human from the first frame (TeaP1LivingLiquidHuman has how it plays).
// It starts where a run from Protean 1 puts it and walks from each cue: to the stack as the run starts, out of the
// telegraphs as their bars show, onto its bait or out to its Sluice, across as the first set lands and to its Drainage
// spot as the second does. A tank then walks into its own tether as it shows and takes it to its spot.
//
// On every tornado layout, at either pace and up to half a second late, nobody may die. The cases a rule of the fight
// kills are each their own test and say who dies of what.
public class TeaP1LivingLiquidProtean1LpduHumanTests
{
    // Three rolls per layout; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private const int UsualSeeds = 3;
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", UsualSeeds)).Select(i => 17 * i);
    private static readonly float[] ReactionTimes = [0f, 0.25f, 0.5f];
    private static readonly TeaP1OpenQuarter[] Layouts = Enum.GetValues<TeaP1OpenQuarter>();
    private static readonly TeaP1HumanPace[] Paces = Enum.GetValues<TeaP1HumanPace>();

    private static string Secs(float seconds) => seconds.ToString("0.##", CultureInfo.InvariantCulture);

    // Both tethers start in the stack, on two of the six and from two of the three tornadoes, by the seed.
    private static Roll ProteanOne(TeaP1OpenQuarter open, int seed)
    {
        var rng = new Random(seed * 8 + (int)open);
        var pair = TetheringPairs[rng.Next(3)];
        var first = rng.Next(6);
        return new Roll { Last = Protean1, Open1 = open, Drainage = Tethers(pair, StartsOn.TheStack, TheSix[first], TheSix[(first + 1 + rng.Next(5)) % 6]) };
    }

    private static (TeaP1Tornado First, TeaP1Tornado Second) PairOf(Roll roll) => (roll.Drainage![0].From, roll.Drainage[1].From);

    // What every run is held to beside its deaths.
    private static IEnumerable<string> Faults(Roll roll, Human human, bool thrown = false)
    {
        if (human.Open1Read != roll.Open1) yield return $"the human read the open quarter as {human.Open1Read}";
        foreach (var walk in human.WalksNeverSetOffOn) yield return $"the human never set off on its walk at {walk}";
        if (human.Slides != (thrown ? 1 : 0)) yield return $"the human was thrown {human.Slides} times";
        if (human.HeldTwoAtOnce) yield return "the human held two tethers at once";
    }

    private static IEnumerable<TestCaseData> EverySeatEitherPaceAnyReaction()
        => from seat in PerRole.All
           from pace in Paces
           from late in ReactionTimes
           from stance in Enum.GetValues<TeaP1HumanStance>()
           where stance == TeaP1HumanStance.Exact || (pace == TeaP1HumanPace.Walking && late != 0.25f)
           select new TestCaseData(seat, pace, late, stance).SetName($"{{m}}({seat}, {pace}, {Secs(late)}s late, {stance})");

    [TestCaseSource(nameof(EverySeatEitherPaceAnyReaction))]
    public void AHumanPlayingTheStratIsNeverCaught(PartyRole seat, TeaP1HumanPace pace, float late, TeaP1HumanStance stance)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var seed in Seeds)
        {
            var roll = ProteanOne(open, seed);
            var play = new TeaP1LivingLiquidHumanPlay(seat, pace, late) { Stance = stance };
            var (run, human) = Human.Play(roll, play, seed);
            var faults = Faults(roll, human).ToList();
            if (seat.IsTank() && run.Passed && human.HeldAsDrainageWentOff != Own(seat, PairOf(roll)))
                faults.Add($"the human tank held the {human.HeldAsDrainageWentOff} tether as Drainage went off");
            runs.Add($"seed {seed}, {roll}", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // The Living Liquid locks its facing on the main tank as the telegraphed cast starts, so its own wave, and both
    // unseen sets' wave straight ahead, go that many degrees off south.
    [Test]
    public void AHumanMainTankWhoStacksOffSouthTurnsTheLiquidsOwnWavesAndNobodyIsCaught([Values(-20f, -10f, 10f, 20f)] float degrees, [Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ProteanOne(open, 17);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { StacksOffSouth = degrees };
            float? lockedOff = null;
            var (run, human) = Human.Play(roll, play, 17, probe: p =>
            {
                if (p.Crossed(4.9f)) lockedOff = Turn(Stage.Fight(p).Liquid!.Rotation, roll.Frame1.Facing(180f)) * 180f / MathF.PI;
            });
            var faults = Faults(roll, human).ToList();
            if (lockedOff is not { } off || MathF.Abs(MathF.Abs(off) - MathF.Abs(degrees)) > 4f)
                faults.Add($"the Living Liquid locked its facing {lockedOff} degrees off south");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // It waits for the bar where the start put it, south-east of the Living Liquid, which faces it there: the
    // Liquid's own wave and the one aimed at the tank go down one line, and its first-set bait, 3 degrees off that
    // line, is inside both. The start's chat line asks the main tank to stand opposite the open quarter.
    [Test]
    public void AHumanMainTankWhoNeverTurnsTheLiquidSouthIsCaughtByItsOwnTwoWaves([Values] TeaP1HumanPace pace)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ProteanOne(open, 102);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { NeverTurnsTheLiquid = true };
            float? faces = null;
            var (run, human) = Human.Play(roll, play, 102, mayLose: true, probe: p =>
            {
                if (p.Crossed(4.9f)) faces = Turn(Stage.Fight(p).Liquid!.Rotation, roll.Frame1.Facing(135f)) * 180f / MathF.PI;
            });
            var context = $"{roll}, {Secs(late)}s late";
            if (human.Open1Read != roll.Open1) problems.Add($"{context}: the human read the open quarter as {human.Open1Read}");
            if (faces is not { } off || MathF.Abs(off) > 4f) problems.Add($"{context}: the Living Liquid faced {faces} degrees off south-east");
            if (run.Failure is not null || run.Deaths.Count != 1
                || run.Deaths[0] is not { Role: MainTank, ActionId: P1ActionId.ProteanWaveFromTheLiquid or P1ActionId.ProteanWaveAtAPlayer, Time: > 8.4f and < 8.6f }
                || !run.Deaths[0].Cause.Contains(TeaP1Actions.SecondWave))
                problems.Add($"{context}: the main tank alone did not die of a second telegraphed wave: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // From the second set on it stands 7.4 y south-east of the middle, as the guides draw it: the Living Liquid, free
    // again at 14.0, walks 3.4 y toward it, and the tank takes its tether from there.
    [Test]
    public void AHumanMainTankAtTheEdgeOfItsReachDragsTheLiquidAndNobodyIsCaught([Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ProteanOne(open, 34);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { MaxMelee = true };
            var (run, human) = Human.Play(roll, play, 34);
            var faults = Faults(roll, human).ToList();
            if (human.FarthestTheLiquidStoodOffTheMiddle < 3f) faults.Add($"the Living Liquid only went {human.FarthestTheLiquidStoodOffTheMiddle} y off the middle");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // -- Drainage, a tank --

    // Wherever the two tethers start: on two of the six, on the human, on the bot tank or on both tanks, either way
    // round. A tether on the wrong tank is taken off it by the tank whose own it is, the bot's off the human as much
    // as the human's off the bot; only both tanks crossed from the start stay crossed, since a tank that holds one
    // cannot take the other. The human walks into its own at once, half a second late and a second late.
    [Test]
    public void AHumanTankTakesItsOwnTetherWhereverTheTethersStart([Values(MainTank, OffTank)] PartyRole seat, [Values(0, 1, 2)] int pair, [Values] StartsOn startsOn)
    {
        var tethering = TetheringPairs[pair];
        var runs = new TeaP1HumanRuns();
        var layouts = new[] { TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southwest };
        for (var i = 0; i < 12; i++)
        {
            var (pace, fetchesLate, open) = (Paces[i % 2], new[] { 0f, 0.5f, 1f }[i / 2 % 3], layouts[i / 6]);
            var roll = new Roll { Last = Protean1, Open1 = open, Drainage = Tethers(tethering, startsOn, TheSix[(i + pair) % 6], TheSix[(i + pair + 2) % 6]) };
            var play = new TeaP1LivingLiquidHumanPlay(seat, pace, 0f) { FetchesLate = fetchesLate };
            var seed = 17 * (i + 1);
            var (run, human) = Human.Play(roll, play, seed);
            var faults = Faults(roll, human).ToList();
            var held = startsOn == StartsOn.TheTanksCrossed ? Own(seat == MainTank ? OffTank : MainTank, tethering) : Own(seat, tethering);
            if (run.Passed && human.HeldAsDrainageWentOff != held) faults.Add($"the human held the {human.HeldAsDrainageWentOff} tether as Drainage went off, not the {held} one");
            runs.Add($"seed {seed}, {roll}, {pace}, into its tether {Secs(fetchesLate)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // No bot covers for it: a tank that holds its own tether cannot take a second. The human's stays on one of the six,
    // either from the start or once the bot tank has taken its own off the human, and Drainage goes off in the stack.
    [Test]
    public void AHumanTankWhoNeverTakesItsTetherLosesTheSix([Values(MainTank, OffTank)] PartyRole seat, [Values(0, 1, 2)] int pair, [Values] bool startsWithTheBotTanks)
    {
        var tethering = TetheringPairs[pair];
        var startsOn = !startsWithTheBotTanks ? StartsOn.TheStack : seat == MainTank ? StartsOn.TheMainTankAndTheStack : StartsOn.TheStackAndTheOffTank;
        var roll = new Roll { Last = Protean1, Open1 = Layouts[pair * 3 + (int)seat], Drainage = Tethers(tethering, startsOn, ShieldHealer, CasterDps) };
        var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, 0.25f) { NeverFetches = true };
        var (run, human) = Human.Play(roll, play, 17, mayLose: true);

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(Faults(roll, human), Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(TheSix), run.ToString);
            Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo((uint?)P1ActionId.Drainage), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(24.6f).Within(0.1f), run.ToString);
            Assert.That(run.Deaths.Count(d => d.Cause.Contains(TeaP1LivingLiquid.NonTankHoldsATether)), Is.EqualTo(1), run.ToString);
            Assert.That(run.Deaths.Count(d => d.Cause.Contains(TeaP1LivingLiquid.InSomeoneElsesDrainage)), Is.EqualTo(5), run.ToString);
        });
    }

    // Each tank starts with its own. The human walks into the other tether's line, a yalm and a half ahead of the bot
    // tank that holds it, and back to its spot.
    [Test]
    public void AHumanTankWithItsOwnTetherWalksThroughTheOtherAndEachKeepsItsOne([Values(MainTank, OffTank)] PartyRole seat, [Values(0, 1, 2)] int pair)
    {
        var tethering = TetheringPairs[pair];
        var runs = new TeaP1HumanRuns();
        foreach (var open in new[] { TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast, TeaP1OpenQuarter.West })
        foreach (var late in ReactionTimes)
        {
            var roll = new Roll { Last = Protean1, Open1 = open, Drainage = Tethers(tethering, StartsOn.TheTanksUncrossed, ShieldHealer, CasterDps) };
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, late) { WalksThroughTheOtherTether = true };
            var changedHands = false;
            var nearestTheOtherLine = float.MaxValue;
            var (run, human) = Human.Play(roll, play, 51, probe: p =>
            {
                var fight = Stage.Fight(p);
                foreach (var (from, tether) in fight.DrainageTethersOut)
                {
                    var holder = Seat(fight.Holder(tether));
                    if (holder != (Own(MainTank, tethering) == from ? MainTank : OffTank)) changedHands = true;
                    if (holder == seat || fight.Holder(tether) is not { } itsHolder) continue;
                    nearestTheOtherLine = MathF.Min(nearestTheOtherLine,
                        FromTheLine(Flat(p.Member(seat)!.Position), Flat(itsHolder.Position), Flat(fight.Tornadoes.Centre(from)!.Value)));
                }
            });
            var faults = Faults(roll, human).ToList();
            if (changedHands) faults.Add("a tether changed hands");
            if (nearestTheOtherLine > 0.3f) faults.Add($"the human came no nearer the other tether's line than {nearestTheOtherLine} y");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // How far `at` is from the stretch of a tether ahead of its holder.
    private static float FromTheLine(Vector2 at, Vector2 holder, Vector2 tornado)
    {
        var along = Vector2.Normalize(tornado - holder);
        var ahead = Vector2.Dot(at - holder, along);
        return ahead < 0.1f ? float.MaxValue : MathF.Abs(along.X * (at.Y - holder.Y) - along.Y * (at.X - holder.X));
    }

    // -- Drainage, one of the six --

    // The human starts with one tether and the other tank with its own. In the stack a tether passes at once to whoever
    // stands in its line, so the human keeps the one it starts with only when nobody of the six stands between it and
    // that tornado: in about a quarter of these runs. Then the tank whose tether it is walks into it wherever the human
    // takes it, and once it has gone the human walks back to the stack.
    [Test]
    public void AHumanInTheStackWithATetherIsRelievedOfIt([Values] TeaP1HumanHolder does)
    {
        var runs = new TeaP1HumanRuns();
        var walkedOff = 0;
        foreach (var seat in TheSix)
        for (var i = 0; i < 12; i++)
        {
            var (tethering, itsTank, late) = (TetheringPairs[i % 3], Tanks[i / 3 % 2], i < 6 ? 0f : 0.5f);
            var otherTank = itsTank == MainTank ? OffTank : MainTank;
            var roll = new Roll
            {
                Last = Protean1,
                Open1 = Layouts[(i + (int)seat) % 8],
                Drainage = [new(Own(itsTank, tethering), seat), new(Own(otherTank, tethering), otherTank)],
            };
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, late) { Holder = does };
            var aTankHeldTwo = false;
            Vector2? stood = null;
            var strayed = 0f;
            var (run, human) = Human.Play(roll, play, 68, probe: p =>
            {
                var fight = Stage.Fight(p);
                if (p.Crossed(20f)) stood = Flat(p.Member(seat)!.Position);
                if (stood is { } there && p.Time < 24.5f) strayed = MathF.Max(strayed, Vector2.Distance(there, Flat(p.Member(seat)!.Position)));
                if (Tanks.Any(tank => fight.DrainageTethersOut.Count(tether => Seat(fight.Holder(tether.Tether)) == tank) > 1)) aTankHeldTwo = true;
            });
            var faults = Faults(roll, human).ToList();
            if (aTankHeldTwo) faults.Add("a tank held two tethers");
            if (strayed >= 2f) walkedOff++;
            if (run.Passed && human.HeldAsDrainageWentOff is not null) faults.Add("the human still held its tether as Drainage went off");
            runs.Add($"{seat}, {roll}, {Secs(late)}s late", run, faults);
        }

        TestContext.Out.WriteLine($"{walkedOff} of them with the human 2 y or more from where it stood");
        if (does != TeaP1HumanHolder.Stays) Assert.That(walkedOff, Is.GreaterThanOrEqualTo(6), "runs with the human 2 y or more from where it stood");
        else Assert.That(walkedOff, Is.Zero);
        runs.AssertNoneLostBut();
    }

    // -- The telegraphed waves' knockback --

    // A healer or a ranged goes straight out to where its Sluice will drop and stands there through the telegraph: the
    // one wave aimed at it goes down its pair's line. Without its anti-knockback it is thrown to the rim and walks
    // back; either way its next wave is the second set's, 5.2 s on, and so is its pair's bot, caught in the same wave.
    [Test]
    public void AHealerOrARangedAloneInItsOwnTelegraphedWaveIsThrownUnlessItPresses(
        [Values(RegenHealer, ShieldHealer, PhysRangedDps, CasterDps)] PartyRole seat, [Values] bool presses, [Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in new[] { 0f, 0.5f })
        {
            var roll = ProteanOne(open, 85);
            var play = new TeaP1LivingLiquidHumanPlay(seat, pace, late) { TakesItsOwnTelegraphedWave = true, PressesAntiKnockback = presses };
            var pair = seat switch { RegenHealer => CasterDps, CasterDps => RegenHealer, ShieldHealer => PhysRangedDps, _ => ShieldHealer };
            var (carried, itsPairCarried) = (false, false);
            var (run, human) = Human.Play(roll, play, 85, probe: p =>
            {
                if (p.Crossed(9f)) (carried, itsPairCarried) = (p.Member(seat)!.HasStatus(P1StatusId.WaterResistanceDown), p.Member(pair)!.HasStatus(P1StatusId.WaterResistanceDown));
            });
            var faults = Faults(roll, human, thrown: !presses).ToList();
            if (!carried) faults.Add("the human was not hit by its wave");
            if (!itsPairCarried) faults.Add($"{pair} was not caught in the human's wave");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }
}
