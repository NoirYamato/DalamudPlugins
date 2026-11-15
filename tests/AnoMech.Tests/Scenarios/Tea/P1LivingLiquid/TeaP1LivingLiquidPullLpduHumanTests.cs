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

// The pull with the player's seat a human from the first frame (TeaP1LivingLiquidHuman has how it plays), pressing its
// own buttons: the strat presses nothing for it. It starts where a run from the pull puts it, reads its north off the
// tornadoes as they appear with the Liquid Hand, and plays its seat from what it sees.
//
// On every tornado layout, at either pace and up to half a second late, nobody may die, every doll goes to the damage
// dealer whose quarter it stands in and is fed, and the bosses end on their own tanks. What a rule of the fight kills
// is each its own test, and says who dies of what; so is what a seat can get wrong and still lose nobody.
//
// Not played, since neither is a mistake here: a ranged that draws its doll to a boss 2 s after its tether (no boss
// stands within 20 y of it, so the doll is ready before it gets there), and H2 a step nearer M2's doll (its spot far
// out is 13 y or more from every second doll).
public class TeaP1LivingLiquidPullLpduHumanTests
{
    // Two rolls per layout; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private const int UsualSeeds = 2;
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", UsualSeeds)).Select(i => 17 * i);
    private static readonly float[] ReactionTimes = [0f, 0.25f, 0.5f];
    private static readonly TeaP1OpenQuarter[] Layouts = Enum.GetValues<TeaP1OpenQuarter>();
    private static readonly TeaP1HumanPace[] Paces = Enum.GetValues<TeaP1HumanPace>();
    private static readonly PartyRole[] Melee = [MeleeDpsA, MeleeDpsB];

    private const float TheSlam = 34.4f, TheUnseenWaves = 37.5f, TheFirstExhaust = 38.5f;

    private static string Secs(float seconds) => seconds.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool Cardinal(TeaP1OpenQuarter open) => (int)open % 2 == 0;

    // As the plugin has it: the seat a human from the first frame is all that tells the strat to press nothing for it.
    private static Roll ThePull(TeaP1OpenQuarter open) => new() { First = Pull, Last = Pull, Open1 = open };

    // The pull's one roll beside its layout: which two tornadoes send an Embolus, and which orb heads for which boss.
    private static IReadOnlyList<TeaP1EmbolusRoll> Emboli(int nth)
    {
        var rolls = TeaP1LivingLiquidLpduMarginTests.EmbolusRolls;
        return rolls[(nth % rolls.Length + rolls.Length) % rolls.Length].Select(orb => new TeaP1EmbolusRoll(orb.From, orb.AimsAt)).ToList();
    }

    private static bool FromTheMiddleTornado(IReadOnlyList<TeaP1EmbolusRoll> emboli) => emboli.Any(orb => orb.From == TeaP1Tornado.Middle);

    private static PartyRole TheOther(PartyRole seat) => seat switch
    {
        MainTank => OffTank,
        OffTank => MainTank,
        MeleeDpsA => MeleeDpsB,
        MeleeDpsB => MeleeDpsA,
        PhysRangedDps => CasterDps,
        CasterDps => PhysRangedDps,
        RegenHealer => ShieldHealer,
        _ => RegenHealer,
    };

    // What a run is read for beside its deaths: each doll by the seat whose quarter it stands in.
    private sealed class Watched
    {
        public Dictionary<PartyRole, PartyRole?> HeldBy { get; } = [];
        public Dictionary<PartyRole, float> FedAt { get; } = [];
        public PartyRole? TheHandIsOn { get; private set; }
        public PartyRole? TheLiquidIsOn { get; private set; }
        public List<(float At, PartyRole By, bool ByHuman)> Provokes { get; private set; } = [];

        public void Look(ScenarioProbe p, Roll roll)
        {
            var fight = Stage.Fight(p);
            foreach (var (seat, quarter) in Stage.DollOf)
            {
                if (fight.Dolls.In(roll.Frame1, quarter) is not { } doll) continue;
                HeldBy[seat] = Seat(doll.Holder);
                if (doll.FedTo is not null) FedAt.TryAdd(seat, p.Time);
            }
            if (fight.Hand is not null) TheHandIsOn = Seat(fight.Target(TeaP1Boss.LiquidHand));
            TheLiquidIsOn = Seat(fight.Target(TeaP1Boss.LivingLiquid));
            Provokes = Stage.Scenario(p).Presses!.Provokes.Select(provoke => (provoke.At, Seat(provoke.By)!.Value, provoke.ByHuman)).ToList();
        }
    }

    private static (ScenarioRun Run, Human Human, Watched Saw) Play(Roll roll, TeaP1LivingLiquidHumanPlay play, int seed, bool mayLose = false,
        Action<ScenarioProbe>? probe = null, bool? resolved = null)
    {
        var saw = new Watched();
        var (run, human) = Human.Play(roll, play, seed, mayLose, probe: p =>
        {
            saw.Look(p, roll);
            probe?.Invoke(p);
        }, resolved: resolved);
        return (run, human, saw);
    }

    // What every run is held to beside its deaths.
    private static IEnumerable<string> Faults(Roll roll, Human human, ScenarioRun run, int thrown = 0)
    {
        if (run.Elapsed > 20f && human.Open1Read != roll.Open1) yield return $"the human read the open quarter as {human.Open1Read}";
        if (run.Passed)
            foreach (var walk in human.WalksNeverSetOffOn) yield return $"the human never set off on its walk at {walk}";
        if (human.Slides != thrown) yield return $"the human was thrown {human.Slides} times";
    }

    // A pull played through: a palm, each doll with the damage dealer whose quarter it stands in and fed, and each boss
    // back on its own tank.
    private static IEnumerable<string> LeftUndone(ScenarioRun run, Human human, Watched saw, PartyRole? swapped = null)
    {
        if (!run.Passed) yield break;
        if (human.SawAPalmAtThePull != true) yield return "the human saw no palm";
        foreach (var seat in Stage.DollOf.Keys)
        {
            var holder = swapped == seat || swapped == TheOther(seat) ? TheOther(seat) : seat;
            if (saw.HeldBy.GetValueOrDefault(seat) != holder) yield return $"{seat}'s doll went to {saw.HeldBy.GetValueOrDefault(seat)}";
            if (!saw.FedAt.ContainsKey(seat)) yield return $"{seat}'s doll was never fed";
        }
        if (saw.TheLiquidIsOn != MainTank) yield return $"the Living Liquid ended on {saw.TheLiquidIsOn}";
        if (saw.TheHandIsOn != OffTank) yield return $"the Liquid Hand ended on {saw.TheHandIsOn}";
    }

    private static IEnumerable<string> Lost(ScenarioRun run) => run.Passed ? [] : [run.ToString()];

    // The strat presses nothing for a human that can press it itself.
    private static IEnumerable<string> PressedForTheHuman(PartyRole seat, Watched saw)
        => saw.Provokes.Where(provoke => provoke.By == seat && !provoke.ByHuman).Select(provoke => $"the run Provoked for the human at {Secs(provoke.At)}");

    private static IEnumerable<string> NotEveryoneDied(ScenarioRun run, float at, string of, float within = 0.15f)
    {
        if (run.Failure is not null) yield return run.ToString();
        if (run.Deaths.Count != 8) yield return $"{run.Deaths.Count} died: {run}";
        else if (run.Deaths.Any(death => MathF.Abs(death.Time - at) > within || !death.Cause.Contains(of))) yield return $"not everyone died at {Secs(at)} of that: {run}";
    }

    private static IEnumerable<TestCaseData> EverySeatEitherPaceAnyReaction()
        => from seat in PerRole.All
           from pace in Paces
           from late in ReactionTimes
           select new TestCaseData(seat, pace, late).SetName($"{{m}}({seat}, {pace}, {Secs(late)}s late)");

    [TestCaseSource(nameof(EverySeatEitherPaceAnyReaction))]
    public void AHumanPlayingTheStratIsNeverCaught(PartyRole seat, TeaP1HumanPace pace, float late)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var (seed, nth) in Seeds.Select((seed, nth) => (seed, nth)))
        {
            var roll = ThePull(open) with { FirstEmboli = Emboli((int)open + nth) };
            var (run, human, saw) = Play(roll, new TeaP1LivingLiquidHumanPlay(seat, pace, late), seed);
            runs.Add($"seed {seed}, {roll}", run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)).Concat(PressedForTheHuman(seat, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // The seeds alone would give two of the six Emboli rolls: each is pinned, so that every seat meets all six, in the
    // sweep and in the whole fight.
    [Test]
    public void TheSweepPlaysEveryEmboliRoll()
    {
        var inTheSweep = (from open in Layouts from nth in Enumerable.Range(0, UsualSeeds) select string.Join(" ", Emboli((int)open + nth))).Distinct().Count();
        var inTheWholeFight = PerRole.All.Select(seat => Enumerable.Range(0, 8).Select(i => string.Join(" ", TheWholeFight(i, 17 * (1 + (int)seat)).FirstEmboli!)).Distinct().Count());

        Assert.That(inTheSweep, Is.EqualTo(6));
        Assert.That(inTheWholeFight, Is.All.EqualTo(6));
    }

    // -- The whole fight --

    private static Roll TheWholeFight(int i, int seed)
    {
        var protean = new Random(seed + i);
        var first = protean.Next(6);
        return new Roll
        {
            First = Pull,
            Open1 = Layouts[i],
            Open2 = Layouts[(i + 1 + 2 * (seed % 3)) % 8],
            FirstEmboli = Emboli(i + seed),
            Drainage = Tethers(TetheringPairs[protean.Next(3)], StartsOn.TheStack, TheSix[first], TheSix[(first + 1 + protean.Next(5)) % 6]),
        };
    }

    // The pull and on through Protean 1 and Cascade 2 to the kill, as Start plays it. R1's walk in for Protean 2's
    // second set is too long at a walk half a second late (TeaP1LivingLiquidCascade2LpduHumanTests): it reacts a
    // quarter second late there.
    [Test]
    public void AHumanPlaysTheWholeFightFromThePull([Values] PartyRole seat, [Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        for (var i = 0; i < 8; i++)
        {
            var seed = 17 * (1 + (int)seat);
            var roll = TheWholeFight(i, seed);
            var late = ReactionTimes[i % 3];
            if (seat == PhysRangedDps && pace == TeaP1HumanPace.Walking && late == 0.5f) late = 0.25f;
            var (run, human, saw) = Play(roll, new TeaP1LivingLiquidHumanPlay(seat, pace, late), seed);
            var faults = Faults(roll, human, run).Concat(LeftUndone(run, human, saw)).Concat(PressedForTheHuman(seat, saw)).ToList();
            if (run.Passed && human.Open2Read != roll.Open2) faults.Add($"the human read the second open quarter as {human.Open2Read}");
            if (run.Passed && human.SawAPalm != true) faults.Add("the human saw no palm for the last hand");
            runs.Add($"seed {seed}, {roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // -- The main tank: its Provoke --

    // The Hand sets off two thirds of a second after the press and has some 20 y to walk. Pressed at 28.4 it has gone
    // 2 y as it glows, at 29.3, and still shows a palm; at 27.8 it has come within 15.6 y and shows a fist, which the
    // slam at 34.4 then finds together. Pressed at 31.6 it is there in time; at 32.4 it is not, and neither is one
    // never called.
    [TestCase(28.4f, true, null)]
    [TestCase(27.8f, false, TeaP1LivingLiquid.FistFoundThemTogether)]
    [TestCase(31.6f, true, null)]
    [TestCase(32.4f, true, TeaP1LivingLiquid.PalmFoundThemApart)]
    [TestCase(null, true, TeaP1LivingLiquid.PalmFoundThemApart)]
    public void AHumanMainTanksProvokeHasToComeBetweenTheGlowAndTheSlam(float? pressedAt, bool palm, string? everyoneDiesOf)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in new[] { TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southwest, TeaP1OpenQuarter.East, TeaP1OpenQuarter.Northwest })
        foreach (var pace in Paces)
        foreach (var late in new[] { 0f, 0.5f })
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { ProvokesAt = pressedAt, NeverProvokes = pressedAt is null };
            var (run, human, saw) = Play(roll, play, 17, mayLose: everyoneDiesOf is not null);
            var faults = Faults(roll, human, run).ToList();
            if (human.SawAPalmAtThePull != palm) faults.Add(palm ? "the human saw no palm" : "the human saw no fist");
            if (everyoneDiesOf is null) faults.AddRange(LeftUndone(run, human, saw));
            else
            {
                faults.AddRange(NotEveryoneDied(run, TheSlam, everyoneDiesOf));
                if (saw.FedAt.Count > 0) faults.Add($"a doll was fed: {string.Join(", ", saw.FedAt.Keys)}");
            }
            runs.Add($"{roll}, {pace}, {Secs(late)}s late", run, faults, mayLose: everyoneDiesOf is not null);
        }

        runs.AssertNoneLostBut(everyoneDiesOf is null ? 0 : runs.Runs);
    }

    // -- The main tank: its invulnerability --

    // Both busters leave Water Resistance Down on it, and its tornado's unseen wave follows a second later. Its
    // invulnerability lasts 10 s: pressed any time from 27.5 until that wave, it takes the tank through.
    [Test]
    public void AHumanMainTankWhoTurnsInvulnerableAnyTimeInTheTenSecondsBeforeTheUnseenWaveLosesNobody([Values(28f, 36.3f, 37.3f)] float pressedAt)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { TurnsInvulnerableAt = pressedAt };
            var (run, human, saw) = Play(roll, play, 17);
            runs.Add(roll.ToString(), run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // Pressed at 27 it has run out by the wave; pressed after the wave, or never, it does not help either: the tank
    // dies alone, a second after the wave.
    [Test]
    public void AHumanMainTankWithoutItsInvulnerabilityInTimeDiesAloneOfItsTornadosUnseenWave([Values(27f, 37.6f, null)] float? pressedAt, [Values] bool resolved)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3])
            {
                TurnsInvulnerableAt = pressedAt, NeverTurnsInvulnerable = pressedAt is null,
            };
            var (run, human, _) = Play(roll, play, 34, mayLose: true, resolved: resolved);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null || run.Deaths.Count != 1
                || run.Deaths[0] is not { Role: MainTank, ActionId: P1ActionId.TornadoWaveUnseen, Time: > TheUnseenWaves + 0.9f and < TheUnseenWaves + 1.1f }
                || !run.Deaths[0].Cause.Contains(TeaP1Actions.SecondWave))
                problems.Add($"{roll}: the main tank did not die alone, of the unseen wave: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // The status is what every rule reads, and the press puts it on whether or not the sim resolves the player's
    // actions.
    [Test]
    public void AHumanTankPlaysThePullWithTheSimNotResolvingItsActions([Values(MainTank, OffTank)] PartyRole seat)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]);
            var (run, human, saw) = Play(roll, play, 51, resolved: false);
            runs.Add(roll.ToString(), run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // A press goes out aimed at the client's target whatever the button is, and a tank tabbing for the Hand has a
    // doll targeted about when it presses these. None of them is a hit on the doll.
    [Test]
    public void AHumanTankWhoPressesItsCooldownsWithADollTargetedTethersNone([Values(MainTank, OffTank)] PartyRole seat)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { PressesWithADollTargeted = true };
            var (run, human, saw) = Play(roll, play, 68);
            runs.Add(roll.ToString(), run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // It has neither Provoke nor an invulnerability, so it presses neither and plays the seat's spots: the run presses
    // both for its seat, on the bots' beats.
    [Test]
    public void AHumanOnAJobWithoutATanksButtonsInATanksSeatPlaysItsSpotsAndTheRunPresses([Values(MainTank, OffTank)] PartyRole seat,
        [Values(JobId.Monk, JobId.Bard, JobId.BlackMage)] JobId job)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Sprinting, 0.25f) { Job = job };
            var (run, human, saw) = Play(roll, play, 85);
            var faults = Faults(roll, human, run).Concat(LeftUndone(run, human, saw)).ToList();
            if (human.ProvokedAt is not null || human.TurnedInvulnerableAt is not null) faults.Add("the human pressed a tank's button");
            if (run.Passed && !saw.Provokes.Select(provoke => (provoke.By, provoke.ByHuman)).SequenceEqual([(MainTank, false), (OffTank, false)]))
                faults.Add($"the Provokes were {string.Join(", ", saw.Provokes)}");
            runs.Add(roll.ToString(), run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // -- The main tank: its tornado's telegraph --

    // It walks 8 y along the rim, out of the wave by 9 degrees, and back once the wave has gone off: the Living Liquid
    // comes a little way after it, and it is the tornado's closest again before the unseen wave.
    [Test]
    public void AHumanMainTankWhoStepsOutOfItsTornadosTelegraphAndBackLosesNobody([Values] TeaP1HumanPace pace)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { Telegraph = TeaP1HumanTelegraph.StepsOutAndBack };
            var carried = true;
            var (run, human, saw) = Play(roll, play, 102, probe: p =>
            {
                if (p.Crossed(36.3f)) carried = p.Member(MainTank)!.HasStatus(P1StatusId.WaterResistanceDown);
            });
            var faults = Faults(roll, human, run).Concat(LeftUndone(run, human, saw)).ToList();
            if (carried) faults.Add("the telegraphed wave hit the main tank");
            runs.Add($"{roll}, {Secs(late)}s late", run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // The wave throws it 10 y on, past the rim.
    [Test]
    public void AHumanMainTankWhoStaysInItsTornadosTelegraphWithoutArmsLengthIsThrownPastTheRim()
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { Telegraph = TeaP1HumanTelegraph.Stays };
            var (run, human, _) = Play(roll, play, 119, mayLose: true);
            problems.AddRange(Faults(roll, human, run, thrown: 1).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null || run.Deaths.Count != 1 || run.Deaths[0].Role != MainTank || !run.Deaths[0].Cause.Contains(TeaZone.WentPastTheRim))
                problems.Add($"{roll}: the main tank alone did not die past the rim: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // Having stepped out, it stops 2 y short of its spot: the melee bot beside that tornado is nearer it than the tank
    // as the unseen wave goes off, and takes it. That is its first wave, and the ranged are clear of its line.
    [Test]
    public void AHumanMainTankNotQuiteBackFromItsStepOutLeavesTheUnseenWaveToAMeleeAndNobodyDies()
    {
        var runs = new TeaP1HumanRuns();
        var wentToAMelee = 0;
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, TeaP1HumanPace.Walking, late)
            {
                Telegraph = TeaP1HumanTelegraph.StepsOutAndBack, ComesBackShortBy = 2f,
            };
            var hit = new List<PartyRole>();
            var (run, human, saw) = Play(roll, play, 136, probe: p =>
            {
                if (p.Crossed(TheUnseenWaves + 0.4f))
                    hit.AddRange(PerRole.All.Where(seat => seat is MainTank or MeleeDpsA or MeleeDpsB && p.Member(seat)!.HasStatus(P1StatusId.WaterResistanceDown)));
            });
            if (hit.Any(Melee.Contains)) wentToAMelee++;
            runs.Add($"{roll}, {Secs(late)}s late, hit {string.Join(" ", hit)}", run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        TestContext.Out.WriteLine($"the unseen wave went to a melee in {wentToAMelee} runs");
        Assert.That(wentToAMelee, Is.GreaterThanOrEqualTo(12), "runs in which the unseen wave went to a melee");
        runs.AssertNoneLostBut();
    }

    // -- The main tank: where it takes the Living Liquid --

    // A yalm along the rim it is 9.2 y from the middle tornado, still nearer than the melee beside it.
    [Test]
    public void AHumanMainTankWhoParksTheLiquidAYalmAlongTheRimFromTheStratsSpotLosesNobody()
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], late) { ParksAlongTheRim = 1f };
            var (run, human, saw) = Play(roll, play, 153);
            runs.Add($"{roll}, {Secs(late)}s late", run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // Three yalms along the rim it is 10.8 y from the middle tornado, and its tornado's waves are no longer its own:
    // M2, on its split spot, or H2, on its own, is nearer as the telegraph is aimed. Aimed at H2 it hits H2 alone and
    // nobody dies. Aimed at M2's spot it runs on over M1 by its doll and over R2 where that has stepped out of its own
    // tornado's telegraph: R2 then takes its own tornado's unseen wave carrying this one's debuff, and M1, on the
    // layouts where its doll stands beside the middle tornado, takes that one's. The start's chat line asks the main
    // tank to be the middle tornado's closest player. No more of the 24 runs may lose than do today.
    [Test]
    public void AHumanMainTankThreeYalmsAlongTheRimLeavesItsTornadosWavesToWhoeverIsNearer()
    {
        var problems = new List<string>();
        var (runs, lost) = (0, 0);
        foreach (var open in Layouts)
        for (var i = 0; i < 3; i++)
        {
            var (seed, late) = (153 + 17 * i, ReactionTimes[i]);
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], late) { ParksAlongTheRim = 3f };
            var (fromTheTornado, aimedAt) = (float.NaN, new List<PartyRole>());
            var (run, human, _) = Play(roll, play, seed, mayLose: true, probe: p =>
            {
                if (p.Crossed(32.35f)) fromTheTornado = TeaP1LivingLiquid.Apart(p.Member(MainTank)!.Position, roll.Frame1.Centre(TeaP1Tornado.Middle));
                if (p.Crossed(35.45f))
                    aimedAt.AddRange(new[] { ShieldHealer, MeleeDpsA, MeleeDpsB, CasterDps }.Where(seat => p.Member(seat)!.HasStatus(P1StatusId.WaterResistanceDown)));
            });
            var context = $"seed {seed}, {roll}, {Secs(late)}s late";
            runs++;
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{context}: {fault}"));
            if (run.Failure is not null) problems.Add($"{context}: {run}");
            if (fromTheTornado is < 10.5f or > 11.1f) problems.Add($"{context}: the main tank stood {Secs(fromTheTornado)} y from the middle tornado");
            TestContext.Out.WriteLine($"{context}: the telegraph hit {string.Join(" ", aimedAt)}; died: {string.Join(" ", run.Deaths.Select(death => death.Role))}");
            if (run.Passed) continue;
            lost++;
            if (run.Deaths.Any(death => death is not { Role: CasterDps or MeleeDpsA, ActionId: P1ActionId.TornadoWaveUnseen, Time: > TheUnseenWaves + 0.9f and < TheUnseenWaves + 1.1f }
                                        || !death.Cause.Contains(TeaP1Actions.SecondWave)))
                problems.Add($"{context}: someone else died, or of something else: {run}");
        }

        TestContext.Out.WriteLine($"{runs} runs, {lost} with a death");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That(lost, Is.InRange(1, 19), "runs with a death");
    }

    // The Emboli of 40.4 set off at 45.95 for where the bosses stood then, at the rim, and one from the middle tornado
    // has only a few yalms to go to them there, whichever boss it heads for: a boss still at the rim as it comes by
    // bursts it, and everyone dies. The orbs from the two side tornadoes pass clear.
    //
    // The Living Liquid does not leave with its tank: the tank's way to the middle runs through it, and it sets off
    // only once the tank is 7 y past it. Setting off up to 2 s after the bots' beat, the main tank has it away in time
    // on every roll; the feeders go to the bosses where they stand, and the tanks' holds hang off them. 4 s late, an
    // orb from the middle tornado bursts on it at about 47.5 s. The Liquid Hand sets off as its tank does, 5 y behind
    // it: the off tank has until 5.5 s after the beat, and 6.5 s late an orb bursts on the Hand at about 48.6 s. The
    // start's chat lines have the main tank set off between 42 and 44 s and the off tank between 42 and 47 s.
    [TestCase(MainTank, 1.5f, new[] { 0f, 0.25f, 0.5f }, false, 0f)]
    [TestCase(MainTank, 2f, new[] { 0f }, false, 0f)]
    [TestCase(MainTank, 4f, new[] { 0f, 0.5f }, true, 47.55f)]
    [TestCase(OffTank, 5f, new[] { 0f, 0.25f, 0.5f }, false, 0f)]
    [TestCase(OffTank, 6.5f, new[] { 0f, 0.5f }, true, 48.6f)]
    public void AHumanTankWhoLeadsItsBossInLateLosesNobodyUntilAnEmbolusFromTheMiddleTornadoFindsItAtTheRim(PartyRole seat, float leadsItInLate, float[] reactions,
        bool anOrbFromTheMiddleTornadoFindsIt, float burstsAt)
    {
        var problems = new List<string>();
        var (runs, lost) = (0, 0);
        for (var nth = 0; nth < TeaP1LivingLiquidLpduMarginTests.EmbolusRolls.Length; nth++)
        foreach (var open in Layouts)
        foreach (var late in reactions)
        {
            var roll = ThePull(open) with { FirstEmboli = Emboli(nth) };
            var play = seat == MainTank
                ? new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], late) { LeadsTheLiquidInLate = leadsItInLate }
                : new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], late) { LeadsTheHandInLate = leadsItInLate };
            var bursts = anOrbFromTheMiddleTornadoFindsIt && FromTheMiddleTornado(roll.FirstEmboli);
            var (run, human, saw) = Play(roll, play, seat == MainTank ? 170 : 731, mayLose: bursts);
            var faults = Faults(roll, human, run).ToList();
            if (bursts) faults.AddRange(NotEveryoneDied(run, burstsAt, TeaP1LivingLiquid.EmbolusTouched, within: 0.55f));
            else faults.AddRange(LeftUndone(run, human, saw).Concat(Lost(run)));
            problems.AddRange(faults.Select(fault => $"{roll}, {Secs(late)}s late: {fault}"));
            runs++;
            if (!run.Passed) lost++;
        }

        TestContext.Out.WriteLine($"{runs} runs, {lost} with a death");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That(lost, Is.EqualTo(anOrbFromTheMiddleTornadoFindsIt ? runs * 4 / 6 : 0), "the four rolls with an orb from the middle tornado");
    }

    // The Living Liquid lets its tank stand up to 7 y from it. The Embolus that heads for where the Liquid stood at the
    // rim, when it comes from the tornado right of the open quarter, crosses 7 y from the middle on the tank's side at
    // about 51 s: 6.5 y from the Liquid the tank is on its line, and touching it kills everyone. The one from that
    // tornado for the Hand's spot passes clear. The start's chat line says to stand within 4 y.
    [TestCase(TeaP1Boss.LivingLiquid, true)]
    [TestCase(TeaP1Boss.LiquidHand, false)]
    public void AHumanMainTankSixAndAHalfYalmsFromTheLiquidOnTheMiddleIsOnTheLineOfTheEmbolusThatHeadsForIt(TeaP1Boss theEastTornadosOrbHeadsFor, bool touches)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open) with
            {
                FirstEmboli =
                [
                    new(TeaP1Tornado.East, theEastTornadosOrbHeadsFor),
                    new(TeaP1Tornado.West, theEastTornadosOrbHeadsFor == TeaP1Boss.LivingLiquid ? TeaP1Boss.LiquidHand : TeaP1Boss.LivingLiquid),
                ],
            };
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HoldsThisFarFromTheLiquid = 6.5f };
            var (run, human, saw) = Play(roll, play, 187, mayLose: touches);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{roll}: {fault}"));
            if (!touches) problems.AddRange(LeftUndone(run, human, saw).Concat(Lost(run)).Select(fault => $"{roll}: {fault}"));
            else if (run.Failure is not null || run.Deaths.Count != 8
                     || run.Deaths.Any(death => death.Time is < 50.5f or > 52.5f || !death.Cause.Contains(TeaP1LivingLiquid.EmbolusTouched)))
                problems.Add($"{roll}: not everyone died of a touched Embolus at about 51 s: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // Some guides draw the main tank a little nearer the middle tornado, or a little farther along the rim, than the
    // strat's bots stand: here 8.0 y from that tornado where the bots stand 8.4, and on (-9.1, 15.7). The Living Liquid
    // then rests nearer the south-west doll's spot, and within 5.2 y of it M1's bot would leave that doll alone until
    // the bosses have gone. From either spot it rests just beyond that on every layout, 5.24 y at the nearest: M1 hits
    // its doll on its beat and every doll is fed, with the Liquid led in on time and 4 s late. The orbs are pinned to
    // the two side tornadoes: 4 s late, one from the middle tornado would find the Liquid still at the rim (above).
    [Test]
    public void AHumanMainTankWhoBaitsFromWhereSomeGuidesDrawItLosesNobody([Values(0f, 4f)] float leadsItInLate)
    {
        var runs = new TeaP1HumanRuns();
        var nearest = float.MaxValue;
        foreach (var baitsFrom in new[] { Stage.Off(Stage.South, 8.0f, 258f), new Vector2(-9.1f, 15.7f) })
        foreach (var open in Layouts)
        {
            var roll = ThePull(open) with { FirstEmboli = [new(TeaP1Tornado.West, TeaP1Boss.LiquidHand), new(TeaP1Tornado.East, TeaP1Boss.LivingLiquid)] };
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { BaitsFrom = baitsFrom, LeadsTheLiquidInLate = leadsItInLate };
            var liquidFromTheDoll = float.NaN;
            float? hitAt = null;
            var (run, human, saw) = Play(roll, play, 442, probe: p =>
            {
                if (p.Time < 34f || Stage.Fight(p).Dolls.In(roll.Frame1, TeaP1Quarter.Southwest) is not { } doll) return;
                if (p.Crossed(34.2f)) liquidFromTheDoll = TeaP1LivingLiquid.Apart(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid), doll.Spot);
                hitAt = doll.HitAt;
            });
            nearest = MathF.Min(nearest, liquidFromTheDoll);
            var faults = Faults(roll, human, run).Concat(LeftUndone(run, human, saw)).ToList();
            if (run.Passed && hitAt is not <= 34.3f) faults.Add($"M1 hit its doll at {hitAt}");
            runs.Add($"{roll}, from {baitsFrom}, the Liquid {Secs(liquidFromTheDoll)} y from the south-west doll", run, faults);
        }

        TestContext.Out.WriteLine($"the Liquid rested {Secs(nearest)} y from the south-west doll's spot at the nearest");
        runs.AssertNoneLostBut();
    }

    // -- The main tank: when it leads the Living Liquid in --

    // It sets off for the middle at 36.6, as soon as both busters have landed, with the melee still by their dolls,
    // and inside its invulnerability, pressed at 34. Where the two melee dolls stand either side of its way, both reach
    // it as they first Exhaust: it explodes. Its invulnerability takes it through its own explosion, but both melee,
    // within 8.5 y of it, die, and at a walk the off tank on the Liquid's flank with them; where a sprint has by then
    // drawn the Living Liquid on past M1's doll, the rest die of that doll a moment later. Where the dolls do not both
    // reach it, the Living Liquid follows it past M1's doll, which is fed with more than a quarter of its health left:
    // everyone dies. The start's chat line has it lead the Liquid in only from 42 s.
    [Test]
    public void AHumanMainTankWhoLeadsTheLiquidInAsSoonAsTheBustersHaveLandedExplodesOrFeedsADollTooSoon([Values] TeaP1HumanPace pace)
    {
        var problems = new List<string>();
        var (exploded, fedTooSoon) = (0, 0);
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, 0f) { LeadsTheLiquidInAt = 36.6f };
            var (run, human, _) = Play(roll, play, 510, mayLose: true);
            var context = roll.ToString();
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{context}: {fault}"));
            if (human.TurnedInvulnerableAt is not < 34.6f) problems.Add($"{context}: the main tank turned invulnerable at {human.TurnedInvulnerableAt}");
            TestContext.Out.WriteLine($"{context}: {string.Join(", ", run.Deaths.Select(death => $"{death.Role} at {Secs(death.Time)}"))}");
            var ofItsExplosion = run.Deaths.Where(death => MathF.Abs(death.Time - TheFirstExhaust) <= 0.1f && death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst)).ToList();
            var later = run.Deaths.Except(ofItsExplosion).ToList();
            if (run.Failure is not null) problems.Add($"{context}: {run}");
            else if (ofItsExplosion.Count > 0)
            {
                exploded++;
                var died = ofItsExplosion.Select(death => death.Role).Order().ToList();
                var expected = pace == TeaP1HumanPace.Walking ? new[] { OffTank, MeleeDpsA, MeleeDpsB } : [MeleeDpsA, MeleeDpsB];
                if (!died.SequenceEqual(expected.Order())) problems.Add($"{context}: its explosion killed {string.Join(" ", died)}: {run}");
                if (later.Count > 0 && (later.Count != 8 - ofItsExplosion.Count
                                        || later.Any(death => death.Time is < 39f or > 40.3f || !death.Cause.Contains(TeaP1JagdDolls.ADollWasFedTooSoon))))
                    problems.Add($"{context}: after the explosion not everyone left died of a doll fed too soon: {run}");
            }
            else if (NotEveryoneDied(run, 39.4f, TeaP1JagdDolls.ADollWasFedTooSoon, within: 0.4f).Any())
                problems.Add($"{context}: the main tank did not explode at the first Exhaust, nor did everyone die of a doll fed too soon: {run}");
            else fedTooSoon++;
        }

        TestContext.Out.WriteLine($"exploded in {exploded} runs, a doll fed too soon in {fedTooSoon}");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That((exploded, fedTooSoon), Is.EqualTo((4, 4)), "the explosion where the open quarter is on a cardinal, the feed where it is on a diagonal");
    }

    // Setting off at 37.6 it is past the dolls after their first Exhaust, and the melee feed theirs as the Living
    // Liquid comes by, under a quarter. But Pressurize, at 40.4, sends an Embolus for where the Liquid stands then: no
    // longer at the rim. At a sprint the Liquid is on the middle by then, and the orb bursts on the bosses there at
    // about 51.6 s, on every layout. At a walk it is still on its way in, and the orb crosses where the bosses stand
    // only later: on two of the eight layouts before the pull is over, at about 59 s. No more may lose than do today.
    [TestCase(TeaP1HumanPace.Sprinting, 51.65f, 8)]
    [TestCase(TeaP1HumanPace.Walking, 58.9f, 2)]
    public void AHumanMainTankWhoLeadsTheLiquidInBeforePressurizeDrawsItsEmbolusOntoTheBosses(TeaP1HumanPace pace, float burstsAt, int mayLose)
    {
        var problems = new List<string>();
        var lost = 0;
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, 0f) { LeadsTheLiquidInAt = 37.6f };
            var (run, human, saw) = Play(roll, play, 510, mayLose: true);
            var context = roll.ToString();
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{context}: {fault}"));
            TestContext.Out.WriteLine($"{context}: {string.Join(", ", run.Deaths.Select(death => $"{death.Role} at {Secs(death.Time)}"))}");
            if (!Melee.All(saw.FedAt.ContainsKey)) problems.Add($"{context}: a melee's doll was not fed");
            if (run.Passed) continue;
            lost++;
            problems.AddRange(NotEveryoneDied(run, burstsAt, TeaP1LivingLiquid.EmbolusTouched, within: 0.5f).Select(fault => $"{context}: {fault}"));
        }

        TestContext.Out.WriteLine($"{lost} of 8 runs lost everyone to an Embolus");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That(lost, Is.InRange(1, mayLose), "runs with a death");
        if (pace == TeaP1HumanPace.Sprinting) Assert.That(lost, Is.EqualTo(8));
    }

    // It waits where the start put it, 2 y south of the middle, and the Living Liquid comes to 6 y north of it and
    // cleaves south, over the knot behind the tank: every non-tank in it dies. The start's chat line asks the main tank
    // to turn the Liquid away from the party.
    [Test]
    public void AHumanMainTankWhoNeverTurnsTheLiquidAtThePullLosesTheSixBehindItToTheFirstFluidSwing([Values] TeaP1HumanPace pace)
    {
        var problems = new List<string>();
        foreach (var open in new[] { TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southwest })
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, pace, late) { NeverTurnsTheLiquidAtThePull = true };
            var (run, human, _) = Play(roll, play, 527, mayLose: true);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null || !run.Deaths.Select(death => death.Role).Order().SequenceEqual(PerRole.All.Where(seat => !seat.IsTank()).Order())
                || run.Deaths.Any(death => death.ActionId != P1ActionId.FluidSwing || MathF.Abs(death.Time - 11.1f) > 0.1f || !death.Cause.Contains("Caught in Fluid Swing")))
                problems.Add($"{roll}: the six behind the tank did not die of the first Fluid Swing, and they alone: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // -- The off tank --

    // Provoke reaches 25 y past the two hitboxes, 27.1 y between a tank's centre and the Hand's, and the main tank
    // presses it from the rim beside the middle tornado. The bound is the off tank's own to keep, since the Hand rests
    // 4.1 y short of it. With the off tank 9 or 12 y from the middle the Hand is 4.9 or 7.9 y out and 23.4 or 26.4 y
    // from the main tank, in reach. With it 13.5 y out, or 14.1 where some guides draw it, the Hand is 9.4 or 10 y out
    // and 27.8 y or more away, and a Hand that is itself 12 y out has its tank at 16.1: the main tank's bot then has no
    // Provoke to press, and the palm's slam finds the bosses apart.
    [TestCase(9f, 39f, true)]
    [TestCase(12f, 39f, true)]
    [TestCase(13.5f, 39f, false)]
    [TestCase(14.1f, 34f, false)]
    [TestCase(16.1f, 39f, false)]
    public void AHumanOffTankHasToStandWithinTwelveYalmsOfTheMiddleForTheMainTanksProvokeToReachTheHand(float fromTheMiddle, float bearing, bool inReach)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { SplitsTo = (fromTheMiddle, bearing) };
            var (apart, theHandOut) = (float.NaN, float.NaN);
            var (run, human, saw) = Play(roll, play, 578, mayLose: !inReach, probe: p =>
            {
                if (!p.Crossed(29.95f)) return;
                apart = TeaP1LivingLiquid.Apart(p.Member(MainTank)!.Position, Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
                theHandOut = Flat(Stage.Fight(p).Position(TeaP1Boss.LiquidHand)).Length();
            });
            var faults = Faults(roll, human, run).ToList();
            if (human.SawAPalmAtThePull != true) faults.Add("the human saw no palm");
            if (MathF.Abs(theHandOut - (fromTheMiddle - Stage.HandStopsFromItsTank)) > 0.2f) faults.Add($"the Hand stood {Secs(theHandOut)} y from the middle");
            if (inReach)
            {
                faults.AddRange(LeftUndone(run, human, saw).Concat(Lost(run)));
                if (apart is < 23f or > 27.1f) faults.Add($"the Hand stood {Secs(apart)} y from the main tank");
            }
            else
            {
                faults.AddRange(NotEveryoneDied(run, TheSlam, TeaP1LivingLiquid.PalmFoundThemApart));
                if (apart < 27.3f) faults.Add($"the Hand stood {Secs(apart)} y from the main tank");
                if (saw.Provokes.Count > 0) faults.Add("someone Provoked");
            }
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // The main tank's bot looks for its Provoke every quarter second from 30.0 until 34.25. Here the off tank has the
    // Hand 10 y out as the palm shows, out of reach, and then walks in through it to 3.9 y from the middle: the Hand
    // follows to 8 y out, into reach. Setting off at 29.4 or 30.0, the bot Provokes on a later look, at about 31.3 or
    // 31.8, and the Hand is with the Living Liquid for the slam. Setting off at 30.3, the Provoke comes at about 32.3,
    // too late: the palm finds them apart.
    [TestCase(29.4f, 31.3f, true)]
    [TestCase(30.0f, 31.8f, true)]
    [TestCase(30.3f, 32.3f, false)]
    public void TheMainTanksBotProvokesOnALaterLookOnceAHumanOffTankHasDrawnTheHandIntoItsReach(float drawsItInAt, float provokedAt, bool inTime)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, Paces[(int)open % 2], 0f) { SplitsTo = (14.1f, 34f), DrawsTheHandInAt = drawsItInAt };
            var apart = float.NaN;
            var (run, human, saw) = Play(roll, play, 629, mayLose: !inTime, probe: p =>
            {
                if (p.Crossed(29.95f)) apart = TeaP1LivingLiquid.Apart(p.Member(MainTank)!.Position, Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
            });
            var faults = Faults(roll, human, run).ToList();
            if (human.SawAPalmAtThePull != true) faults.Add("the human saw no palm");
            if (apart < 27.3f) faults.Add($"the Hand stood {Secs(apart)} y from the main tank at the first look");
            var byTheMainTank = saw.Provokes.Where(provoke => provoke.By == MainTank).ToList();
            if (byTheMainTank.Count != 1 || byTheMainTank[0].ByHuman || MathF.Abs(byTheMainTank[0].At - provokedAt) > 0.1f)
                faults.Add($"the main tank's Provokes were {string.Join(", ", byTheMainTank)}");
            faults.AddRange(inTime ? LeftUndone(run, human, saw).Concat(Lost(run)) : NotEveryoneDied(run, TheSlam, TeaP1LivingLiquid.PalmFoundThemApart));
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // On a job without Provoke the run presses the off tank's Provoke back for its seat, looking every quarter second
    // from 38.0 until the next busters. Waiting 19 y out in the open quarter once the Hand has left it, it is 33 y from
    // the Hand at 38.0, out of reach: the run Provokes on a later look, at about 44 s, as it comes in for the middle,
    // and the Hand ends on it.
    [Test]
    public void TheRunProvokesBackOnALaterLookOnceAHumanOffTankOnAnotherJobComesIntoItsReach()
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3])
            {
                Job = JobId.Monk, WaitsHereOnceTheHandHasLeft = Stage.Off(Vector2.Zero, 19f, 20f),
            };
            var apart = float.NaN;
            var (run, human, saw) = Play(roll, play, 646, probe: p =>
            {
                if (p.Crossed(37.95f)) apart = TeaP1LivingLiquid.Apart(p.Member(OffTank)!.Position, Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
            });
            var faults = Faults(roll, human, run).Concat(LeftUndone(run, human, saw)).ToList();
            if (apart < 27.3f) faults.Add($"the Hand stood {Secs(apart)} y from the off tank at the first look");
            if (run.Passed && (saw.Provokes.Count != 2 || saw.Provokes[0] is not { By: MainTank, ByHuman: false, At: < 30.1f }
                               || saw.Provokes[1] is not { By: OffTank, ByHuman: false, At: > 43f and < 44.6f }))
                faults.Add($"the Provokes were {string.Join(", ", saw.Provokes)}");
            runs.Add(roll.ToString(), run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // From the main tank's Provoke until it leads the Hand in, it has no boss. On the middle it stands inside both
    // melee dolls' reach as they first Exhaust: it explodes and takes both melee along. The start's chat line has it
    // wait beside the Living Liquid.
    [Test]
    public void AHumanOffTankWhoWaitsOnTheMiddleOnceTheHandHasLeftExplodesWithBothMeleeAtTheFirstExhaust()
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { WaitsOnTheMiddleOnceTheHandHasLeft = true };
            var (run, human, _) = Play(roll, play, 459, mayLose: true);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null || !run.Deaths.Select(death => death.Role).Order().SequenceEqual(new[] { OffTank, MeleeDpsA, MeleeDpsB }.Order())
                || run.Deaths.Any(death => MathF.Abs(death.Time - TheFirstExhaust) > 0.1f || !death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst)))
                problems.Add($"{roll}: the off tank and both melee did not die of its explosion at the first Exhaust: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // It Provokes the Hand back at 36.8, as soon as the busters have landed, and sets off for the middle at 37.5, with
    // the melee still by their dolls. Where the two melee dolls stand either side of its way, both reach it as they
    // first Exhaust: it explodes, and whoever stands within 8.5 y dies with it. Where they do not, the Hand follows it
    // past M1's doll, which is fed with more than a quarter of its health left: everyone dies. Setting off a quarter
    // second later it loses nobody: Hand of Pain's cast, from 38.6, holds the Hand where it is until the melee have
    // fed. The start's chat line has it lead the Hand in from 42 s, once the melee have fed their dolls.
    [Test]
    public void AHumanOffTankWhoLeadsTheHandInBeforeTheMeleeHaveFedExplodesOrFeedsADollTooSoon()
    {
        var problems = new List<string>();
        var (exploded, fedTooSoon) = (0, 0);
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, TeaP1HumanPace.Walking, 0f) { ProvokesBackAt = 36.8f, LeadsTheHandInAt = 37.5f };
            var (run, human, _) = Play(roll, play, 476, mayLose: true);
            var context = roll.ToString();
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{context}: {fault}"));
            TestContext.Out.WriteLine($"{context}: {string.Join(", ", run.Deaths.Select(death => $"{death.Role} at {Secs(death.Time)}"))}");
            if (run.Failure is not null) problems.Add($"{context}: {run}");
            else if (run.Deaths.Count > 0 && run.Deaths.All(death => MathF.Abs(death.Time - TheFirstExhaust) <= 0.1f && death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst))
                     && run.Deaths.Any(death => death.Role == OffTank))
                exploded++;
            else if (NotEveryoneDied(run, 39.4f, TeaP1JagdDolls.ADollWasFedTooSoon, within: 0.6f).Any())
                problems.Add($"{context}: the off tank did not explode at the first Exhaust, nor did everyone die of a doll fed too soon: {run}");
            else fedTooSoon++;
        }

        TestContext.Out.WriteLine($"exploded in {exploded} runs, a doll fed too soon in {fedTooSoon}");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        Assert.That((exploded, fedTooSoon), Is.EqualTo((4, 4)), "the explosion where the open quarter is on a cardinal, the feed where it is on a diagonal");
    }

    // Any time after the first busters and before the next: here just ahead of them.
    [Test]
    public void AHumanOffTankWhoProvokesTheHandBackLateLosesNobody([Values(45f, 50f, 53f)] float pressedAt)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { ProvokesBackAt = pressedAt };
            var (run, human, saw) = Play(roll, play, 204);
            runs.Add(roll.ToString(), run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // Both of the next busters then go to the main tank, which lives: nothing kills in the pull. The Hand stays with
    // the main tank for the rest of the fight, beside the Living Liquid, so at the last hand it shows a fist and its
    // slam finds them together.
    [Test]
    public void AHumanOffTankWhoNeverProvokesTheHandBackLosesNobodyUntilTheLastHand([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast)] TeaP1OpenQuarter open,
        [Values] TeaP1HumanPace pace)
    {
        var roll = TheWholeFight((int)open, 221);
        var play = new TeaP1LivingLiquidHumanPlay(OffTank, pace, 0.25f) { NeverProvokesBack = true };
        var (run, human, saw) = Play(roll, play, 221, mayLose: true);

        Assert.That(Faults(roll, human, run), Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(saw.TheHandIsOn, Is.EqualTo(MainTank));
            Assert.That(human.SawAPalmAtThePull, Is.True);
            Assert.That(human.SawAPalm, Is.False, "the last hand shows a fist");
            Assert.That(NotEveryoneDied(run, roll.C2(38.25f), TeaP1LivingLiquid.FistFoundThemTogether), Is.Empty);
        });
    }

    // It takes the Hand only 5 y out as it splits off, 14 y from where the Living Liquid goes: a fist, which the main
    // tank's bot leaves alone. Walked to the Liquid's flank as on a palm, the slam finds them together. Kept where it
    // is, they are 12 y apart as it slams and nobody dies of that; but the Hand has then stood a yalm off the middle
    // through Pressurize, the Embolus sent for it heads there, and the bosses are brought onto the middle next: it
    // bursts on them at about 51.5 s.
    [Test]
    public void AHumanOffTankWhoHoldsTheHandNearTheLiquidGetsAFistWhoseSlamNeedsThemApart([Values] bool walksIn)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HoldsTheHandNearTheLiquid = true, WalksInOnAFist = walksIn };
            var apart = float.NaN;
            var (run, human, saw) = Play(roll, play, 238, mayLose: true, probe: p =>
            {
                if (p.Crossed(29.25f)) apart = TeaP1LivingLiquid.Apart(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid), Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
            });
            var faults = Faults(roll, human, run).ToList();
            if (human.SawAPalmAtThePull != false) faults.Add($"the human saw no fist, the bosses {Secs(apart)} y apart");
            if (saw.Provokes.Count > 0) faults.Add("someone Provoked");
            if (saw.TheHandIsOn != OffTank) faults.Add($"the Liquid Hand ended on {saw.TheHandIsOn}");
            faults.AddRange(walksIn
                ? NotEveryoneDied(run, TheSlam, TeaP1LivingLiquid.FistFoundThemTogether)
                : NotEveryoneDied(run, 51.5f, TeaP1LivingLiquid.EmbolusTouched, within: 1f));
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // -- A tank or a healer: a doll --

    private static readonly Dictionary<PartyRole, Vector2> WhereProteanOneStarts = new()
    {
        [MeleeDpsA] = new(-0.3f, -5.6f), [MeleeDpsB] = new(0.4f, -4.3f), [PhysRangedDps] = new(11.0f, -7.8f), [CasterDps] = new(-10.3f, -8.1f),
    };

    private static IEnumerable<string> NotTheOnesWhoDied(ScenarioRun run, PartyRole[] die, float at, string of, float within = 0.1f)
    {
        if (run.Failure is not null) yield return run.ToString();
        else if (!run.Deaths.Select(death => death.Role).Order().SequenceEqual(die.Order())
                 || run.Deaths.Any(death => MathF.Abs(death.Time - at) > within || !death.Cause.Contains(of)))
            yield return $"{string.Join(" and ", die)} did not die at {Secs(at)} of that, and they alone: {run}";
    }

    // A doll tethers whoever hits it first, whatever the seat, and walks after it; nothing in a tank's or a healer's
    // part feeds one. Each hits one 0.2 s after the dolls turn targetable, ahead of the bots' first look. The start's
    // chat lines tell the tanks and the healers to leave the dolls' first hits to the damage dealers.
    //
    // H2 waits 8.2 to 8.7 y from the south-east doll's spot, inside its reach. With any other doll come to stand
    // beside it, two Exhausts reach it at 38.5: it explodes, and M2, by its own doll, dies with it.
    [Test]
    public void TheHealerFarSouthEastWhoHitsADollExplodesAtTheFirstExhaustWithTheMeleeBesideIt([Values(MeleeDpsA, PhysRangedDps, CasterDps)] PartyRole whoseDoll)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(ShieldHealer, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsTheDollOf = whoseDoll };
            var (run, human, saw) = Play(roll, play, 663, mayLose: true);
            var faults = Faults(roll, human, run).ToList();
            if (saw.HeldBy.GetValueOrDefault(whoseDoll) != ShieldHealer) faults.Add($"{whoseDoll}'s doll went to {saw.HeldBy.GetValueOrDefault(whoseDoll)}");
            faults.AddRange(NotTheOnesWhoDied(run, [ShieldHealer, MeleeDpsB], TheFirstExhaust, TeaP1JagdDolls.LuminousAetheroplasmBurst));
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // The doll it waits beside reaches it anyway, and no other does: hitting that one, H2 loses nobody, and the doll
    // is fed in passing, under a quarter, as H2 comes in past the bosses.
    [Test]
    public void TheHealerFarSouthEastWhoHitsTheDollItWaitsBesideLosesNobody()
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(ShieldHealer, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsTheDollOf = MeleeDpsB };
            var (run, human, saw) = Play(roll, play, 663);
            var faults = Faults(roll, human, run).ToList();
            if (saw.HeldBy.GetValueOrDefault(MeleeDpsB) != ShieldHealer) faults.Add($"M2's doll went to {saw.HeldBy.GetValueOrDefault(MeleeDpsB)}");
            if (run.Passed && saw.FedAt.Count != 4) faults.Add($"fed: {string.Join(", ", saw.FedAt.Keys)}");
            runs.Add(roll.ToString(), run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // H1 waits far out in the open quarter, 13 y or more from every doll's spot: with a doll come to stand beside it,
    // only that one reaches it, then and at each later Exhaust. Nobody dies. The doll is never fed, and the bot it was
    // taken from takes no other: it ends on its Protean 1 spot, a melee going north without a doll after the first
    // Exhaust.
    [Test]
    public void TheHealerInTheOpenQuarterWhoHitsADollLosesNobodyAndTheBotItWasTakenFromTakesNoOther(
        [Values(MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole whoseDoll)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(RegenHealer, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsTheDollOf = whoseDoll };
            var itsBotFromItsSpot = float.NaN;
            var (run, human, saw) = Play(roll, play, 680, probe: p =>
            {
                if (p.Time > 59f) itsBotFromItsSpot = Vector2.Distance(Flat(p.Member(whoseDoll)!.Position), roll.Frame1.ToArena(WhereProteanOneStarts[whoseDoll]));
            });
            var faults = Faults(roll, human, run).ToList();
            foreach (var seat in Stage.DollOf.Keys)
            {
                if (saw.HeldBy.GetValueOrDefault(seat) != (seat == whoseDoll ? RegenHealer : seat)) faults.Add($"{seat}'s doll went to {saw.HeldBy.GetValueOrDefault(seat)}");
                if (saw.FedAt.ContainsKey(seat) == (seat == whoseDoll)) faults.Add(seat == whoseDoll ? "the healer's doll was fed" : $"{seat}'s doll was never fed");
            }
            if (!(itsBotFromItsSpot <= 0.5f)) faults.Add($"{whoseDoll}'s bot ended {Secs(itsBotFromItsSpot)} y from its Protean 1 spot");
            runs.Add(roll.ToString(), run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // The Living Liquid rests on the main tank's inner side, 6 y from it. A doll that walks to the tank at the rim
    // comes within the Liquid's 4.6 y on its way, a second or two after the hit, whole: it is fed, and everyone dies.
    [Test]
    public void AMainTankWhoHitsADollFromTheRimHasItFedToTheLiquidOnItsWayAndEveryoneDies([Values(MeleeDpsA, MeleeDpsB, CasterDps)] PartyRole whoseDoll)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsTheDollOf = whoseDoll };
            var (run, human, saw) = Play(roll, play, 697, mayLose: true);
            var faults = Faults(roll, human, run).ToList();
            if (saw.HeldBy.GetValueOrDefault(whoseDoll) != MainTank) faults.Add($"{whoseDoll}'s doll went to {saw.HeldBy.GetValueOrDefault(whoseDoll)}");
            var fedAt = saw.FedAt.TryGetValue(whoseDoll, out var fed) ? fed : (float?)null;
            if (fedAt is not { } at || human.HitADollAt is not { } hit || at - hit is < 0.6f or > 3f) faults.Add($"its doll was hit at {human.HitADollAt} and fed at {fedAt}");
            else faults.AddRange(NotEveryoneDied(run, at + P1Duration.ReducibleComplexityAfterTheFeed, TeaP1JagdDolls.ADollWasFedTooSoon));
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // Once the Hand has left it the off tank stands on the Living Liquid's flank. M2's doll walks to it there past the
    // Liquid and is fed whole: everyone dies. R2's comes to it clear of both bosses and stops beside it, inside M1's
    // doll's reach as well as its own: it explodes at the first Exhaust and M1, by its doll, dies with it.
    [TestCase(MeleeDpsB, true)]
    [TestCase(CasterDps, false)]
    public void AnOffTankWhoHitsADollHasItFedToTheLiquidOnItsWayOrExplodesBesideIt(PartyRole whoseDoll, bool itPassesTheLiquid)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(OffTank, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsTheDollOf = whoseDoll };
            var (run, human, saw) = Play(roll, play, 714, mayLose: true);
            var faults = Faults(roll, human, run).ToList();
            if (saw.HeldBy.GetValueOrDefault(whoseDoll) != OffTank) faults.Add($"{whoseDoll}'s doll went to {saw.HeldBy.GetValueOrDefault(whoseDoll)}");
            if (!itPassesTheLiquid) faults.AddRange(NotTheOnesWhoDied(run, [OffTank, MeleeDpsA], TheFirstExhaust, TeaP1JagdDolls.LuminousAetheroplasmBurst));
            else if (!saw.FedAt.TryGetValue(whoseDoll, out var fed) || fed > 36.2f) faults.Add("its doll was not fed by 36.2");
            else faults.AddRange(NotEveryoneDied(run, fed + P1Duration.ReducibleComplexityAfterTheFeed, TeaP1JagdDolls.ADollWasFedTooSoon));
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // -- A damage dealer: its hit --

    // Its doll is under a quarter 4 s after its tether shows. A melee that hits 2 or 3 s late, as one who opens on its
    // doll with auto-attacks does (no auto-attack tethers one here), still has it ready before the bosses leave the
    // rim; a ranged's is not wanted before the second Exhaust.
    [Test]
    public void ADamageDealerWhoHitsItsDollLateStillFeedsIt([Values(MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat, [Values(2f, 3f)] float hitsLate)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsLate = hitsLate };
            var (run, human, saw) = Play(roll, play, 255);
            runs.Add(roll.ToString(), run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // Hit 5 s late, a doll still has more than a quarter of its health as the bosses leave the rim for the middle, at
    // 42.5. On every layout they pass the doll M1 takes, and it is fed to one with a third of its health left: everyone
    // dies. They pass M2's on none, and it is fed once it is ready. A doll nobody has hit is never fed, so the same
    // melee hitting none at all loses nobody (below).
    [TestCase(MeleeDpsA, true)]
    [TestCase(MeleeDpsB, false)]
    public void AMeleeWhoHitsItsDollFiveSecondsLateLosesEveryoneWhereTheBossesPassItOnTheirWayToTheMiddle(PartyRole seat, bool theyPassIt)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsLate = 5f };
            var (run, human, saw) = Play(roll, play, 272, mayLose: theyPassIt);
            var faults = Faults(roll, human, run).ToList();
            if (theyPassIt) faults.AddRange(NotEveryoneDied(run, 44.7f, TeaP1JagdDolls.ADollWasFedTooSoon, within: 0.5f));
            else faults.AddRange(LeftUndone(run, human, saw).Concat(Lost(run)));
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // The bot leaves the doll someone else hit alone and, from 36.0, takes that one's: a melee walks to it first.
    [Test]
    public void ADamageDealerWhoHitsTheOthersDollPlaysThatOneAndTheBotTakesItsOwn([Values(MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { Hits = TeaP1HumanHits.TheOthersDoll, CastsFor = 0f };
            var (run, human, saw) = Play(roll, play, 289);
            runs.Add(roll.ToString(), run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw, swapped: seat)));
        }

        runs.AssertNoneLostBut();
    }

    // A ranged hits a melee's doll before that melee's bot does, from where it stands out of its tornado's telegraph,
    // and the doll walks out to it, into reach of whoever stands by the ranged's own. The melee's bot waits out the
    // first Exhaust by its own doll's spot, then takes the ranged's own and feeds it once the bosses are on the middle.
    [Test]
    public void ARangedWhoHitsAMeleesDollFirstLosesNobody([Values(PhysRangedDps, CasterDps)] PartyRole seat, [Values(MeleeDpsA, MeleeDpsB)] PartyRole whoseDoll)
    {
        var runs = new TeaP1HumanRuns();
        var theMeleeTookTheRangeds = 0;
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { HitsTheDollOf = whoseDoll, CastsFor = 0f };
            var (run, human, saw) = Play(roll, play, 493);
            var faults = Faults(roll, human, run).ToList();
            if (run.Passed && saw.FedAt.Count != 4) faults.Add($"fed: {string.Join(", ", saw.FedAt.Keys)}");
            if (saw.HeldBy.GetValueOrDefault(whoseDoll) != seat) faults.Add($"{whoseDoll}'s doll went to {saw.HeldBy.GetValueOrDefault(whoseDoll)}");
            if (saw.HeldBy.GetValueOrDefault(seat) == whoseDoll) theMeleeTookTheRangeds++;
            else if (saw.HeldBy.GetValueOrDefault(seat) is { } other) faults.Add($"{seat}'s own doll went to {other}");
            runs.Add($"{roll}, {seat}'s own doll with {saw.HeldBy.GetValueOrDefault(seat)}", run, faults);
        }

        Assert.That(theMeleeTookTheRangeds, Is.EqualTo(runs.Runs), "runs in which the melee took the ranged's own doll");
        runs.AssertNoneLostBut();
    }

    // A doll nobody has hit is never fed, and one doll's Exhaust harms nobody: it stands where it appeared to the end.
    [Test]
    public void ADamageDealerWhoNeverHitsItsDollLosesNobody([Values(MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { Hits = TeaP1HumanHits.NoDoll };
            var (run, human, saw) = Play(roll, play, 306);
            var faults = Faults(roll, human, run).ToList();
            if (saw.HeldBy.GetValueOrDefault(seat) is not null || saw.FedAt.ContainsKey(seat)) faults.Add("its doll was hit or fed");
            if (saw.FedAt.Count != 3) faults.Add($"fed: {string.Join(", ", saw.FedAt.Keys)}");
            runs.Add(roll.ToString(), run, faults);
        }

        runs.AssertNoneLostBut();
    }

    // A caster's hit lands as its cast ends, and it casts standing still: out of its tornado's telegraph, or back on
    // its bait when it starts later.
    [Test]
    public void ARangedWhoseHitIsALongCastStillTakesItsDoll([Values(PhysRangedDps, CasterDps)] PartyRole seat, [Values(0f, 2f)] float startsLate)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, TeaP1HumanPace.Walking, late) { CastsFor = 2.8f, HitsLate = startsLate };
            var (run, human, saw) = Play(roll, play, 323);
            runs.Add($"{roll}, {Secs(late)}s late, hit at {human.HitADollAt}", run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // It hits the other melee's doll from range before that one's bot can, and then its own: both tether it, the
    // second walks to it, and both reach it as they first Exhaust. It explodes. The other melee's bot, left without a
    // doll, stays by its own doll's spot through that Exhaust, out of the explosion's reach, and then goes north.
    [Test]
    public void AMeleeWhoHitsBothMeleeDollsExplodesAtTheFirstExhaustAndTheOtherMeleeGoesNorthWithoutOne([Values(MeleeDpsA, MeleeDpsB)] PartyRole seat)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { Hits = TeaP1HumanHits.TheOthersDollThenItsOwn };
            var theOthersSpotNorth = roll.Frame1.ToArena(TheOther(seat) == MeleeDpsA ? new Vector2(-0.3f, -5.6f) : new Vector2(0.4f, -4.3f));
            var theOtherFromItsSpotNorth = float.NaN;
            var (run, human, saw) = Play(roll, play, 544, mayLose: true, probe: p =>
            {
                theOtherFromItsSpotNorth = Vector2.Distance(Flat(p.Member(TheOther(seat))!.Position), theOthersSpotNorth);
            });
            var faults = Faults(roll, human, run).ToList();
            if (saw.HeldBy.GetValueOrDefault(seat) != seat || saw.HeldBy.GetValueOrDefault(TheOther(seat)) != seat) faults.Add("the human does not hold both melee dolls");
            if (run.Failure is not null || run.Deaths.All(death => death.Role != seat) || run.Deaths.Any(death => death.Role == TheOther(seat))
                || run.Deaths.Any(death => MathF.Abs(death.Time - TheFirstExhaust) > 0.1f || !death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst)))
                faults.Add($"the human did not explode at the first Exhaust, the other melee died, or someone died of something else: {run}");
            if (theOtherFromItsSpotNorth > 0.5f) faults.Add($"the other melee ended {Secs(theOtherFromItsSpotNorth)} y from its spot north of the middle");
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // -- A melee: where it stands by its doll --

    [Test]
    public void AMeleeOnItsDollOrHalfAYalmTowardTheTornadoBesideItLosesNobody([Values(MeleeDpsA, MeleeDpsB)] PartyRole seat,
        [Values(TeaP1HumanByItsDoll.OnIt, TeaP1HumanByItsDoll.HalfAYalmTowardTheTornado)] TeaP1HumanByItsDoll stands)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], late) { ByItsDoll = stands };
            var (run, human, saw) = Play(roll, play, 340);
            runs.Add($"{roll}, {Secs(late)}s late", run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // The two melee dolls appear 9.9 y apart and an Exhaust reaches 8.8 y: 2 y toward the other, both reach it.
    [Test]
    public void AMeleeTwoYalmsTowardTheOtherMeleesDollExplodesAtTheFirstExhaust([Values(MeleeDpsA, MeleeDpsB)] PartyRole seat)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { ByItsDoll = TeaP1HumanByItsDoll.TwoYalmsTowardTheOtherMeleesDoll };
            var (run, human, _) = Play(roll, play, 357, mayLose: true);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null || run.Deaths.All(death => death.Role != seat)
                || run.Deaths.Any(death => MathF.Abs(death.Time - TheFirstExhaust) > 0.1f || !death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst)))
                problems.Add($"{roll}: the human did not explode at the first Exhaust, or someone died of something else: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // -- A damage dealer: when it feeds its doll --

    // Fed with a quarter of its health or more, a doll kills everyone. M1 has the bosses a few yalms from its doll at
    // the rim: the doll is fed some 3 s after its tether, with over a third of its health left. On its way M1 comes
    // nearer the middle tornado than the main tank and takes that tornado's unseen wave in its stead; where R2 stands
    // in that wave's line, beside its own tornado, it is hit by two at once and dies a moment before the rest. M2's
    // doll has twice as far to go: M2 is on its way, inside its own doll's reach and the other melee's, as the first
    // Exhaust goes off, and explodes among the tanks; on the one layout where its doll comes within the Living Liquid's
    // 4.6 y just as that Exhaust goes off, the doll is fed there and everyone dies of it.
    [TestCase(MeleeDpsA, true)]
    [TestCase(MeleeDpsB, false)]
    public void AMeleeWhoWalksItsDollToTheBossesTwoSecondsAfterItsTetherKillsEveryoneOrExplodesOnTheWay(PartyRole seat, bool reachesABoss)
    {
        var problems = new List<string>();
        var explodedOnTheWay = 0;
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { Feeds = TeaP1HumanFeeds.TwoSecondsAfterItsTether };
            var (run, human, saw) = Play(roll, play, 374, mayLose: true);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null) problems.Add($"{roll}: {run}");
            else if (reachesABoss)
            {
                var ofTheDoll = run.Deaths.Where(death => death.Cause.Contains(TeaP1JagdDolls.ADollWasFedTooSoon)).ToList();
                var others = run.Deaths.Except(ofTheDoll).ToList();
                if (run.Deaths.Count != 8 || ofTheDoll.Count < 7 || !saw.FedAt.TryGetValue(seat, out var fed) || fed > 39.5f
                    || ofTheDoll.Any(death => MathF.Abs(death.Time - fed - P1Duration.ReducibleComplexityAfterTheFeed) > 0.1f)
                    || others.Any(death => death.Role != CasterDps || !death.Cause.Contains(TeaP1Actions.SecondWave)))
                    problems.Add($"{roll}: not everyone died of a doll fed too soon, or R2 alone of two waves before: {run}");
            }
            else if (saw.FedAt.TryGetValue(seat, out var fedAt))
            {
                if (MathF.Abs(fedAt - TheFirstExhaust) > 0.1f || NotEveryoneDied(run, fedAt + P1Duration.ReducibleComplexityAfterTheFeed, TeaP1JagdDolls.ADollWasFedTooSoon).Any())
                    problems.Add($"{roll}: its doll was fed at {Secs(fedAt)}, and not everyone died of that: {run}");
            }
            else if (run.Deaths.All(death => death.Role != seat)
                     || run.Deaths.Any(death => MathF.Abs(death.Time - TheFirstExhaust) > 0.1f || !death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst)))
                problems.Add($"{roll}: the human did not explode at the first Exhaust, or someone died of something else: {run}");
            else explodedOnTheWay++;
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
        if (!reachesABoss) Assert.That(explodedOnTheWay, Is.EqualTo(7), "layouts on which M2 explodes on its way");
    }

    // A melee that waits by its doll until the bosses rest on the middle, and a ranged that goes in as soon as they do,
    // ahead of the second Exhaust.
    [Test]
    public void ADamageDealerWhoFeedsItsDollOnceTheBossesRestOnTheMiddleLosesNobody([Values(MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps)] PartyRole seat)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], late) { Feeds = TeaP1HumanFeeds.OnceABossRestsOnTheMiddle };
            var (run, human, saw) = Play(roll, play, 391);
            runs.Add($"{roll}, {Secs(late)}s late, fed at {(saw.FedAt.TryGetValue(seat, out var fed) ? Secs(fed) : "never")}", run,
                Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
    }

    // -- A ranged: its tornado's telegraph --

    [Test]
    public void ARangedWhoStaysInItsTornadosTelegraphIsThrownPastTheRim([Values(PhysRangedDps, CasterDps)] PartyRole seat)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { StaysInItsTelegraph = true };
            var (run, human, _) = Play(roll, play, 408, mayLose: true);
            problems.AddRange(Faults(roll, human, run, thrown: 1).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null || run.Deaths.Count != 1 || run.Deaths[0].Role != seat || !run.Deaths[0].Cause.Contains(TeaZone.WentPastTheRim))
                problems.Add($"{roll}: the human alone did not die past the rim: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // The strat's ranged bait from the open quarter's side of their tornadoes, at the rim, so both of a tornado's waves
    // run out of the arena. This one baits from its tornado's inner side, 8.4 y from it toward the open quarter's far
    // rim, steps out of the telegraph and back, and goes out to the rim once the unseen wave has gone off. Both waves
    // cross the arena over H1 on its spot far out in the open quarter: the telegraphed one leaves Water Resistance
    // Down and the unseen one, 2.4 s later, kills it. The start's chat line names the side to bait from.
    [Test]
    public void ARangedWhoBaitsItsTornadoFromItsInnerSideSendsBothWavesAcrossTheArenaOverAHealer([Values(PhysRangedDps, CasterDps)] PartyRole seat)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        foreach (var late in new[] { 0f, 0.25f })
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], late) { BaitsItsTornadoFromBearing = 310f };
            var (run, human, _) = Play(roll, play, 595, mayLose: true);
            var context = $"{roll}, {Secs(late)}s late";
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{context}: {fault}"));
            if (run.Failure is not null || run.Deaths.Count != 1
                || run.Deaths[0] is not { Role: RegenHealer, ActionId: P1ActionId.TornadoWaveUnseen, Time: > TheUnseenWaves + 0.9f and < TheUnseenWaves + 1.1f }
                || !run.Deaths[0].Cause.Contains(TeaP1Actions.SecondWave))
                problems.Add($"{context}: H1 did not die alone, of the tornado's unseen wave: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }

    // -- A healer --

    // Once the dolls have Exhausted it comes in on the tanks' side of the middle, 3 y toward the middle tornado, and
    // stays: the bosses are led onto the middle and cleave toward their tanks at 55 s. The start's chat line has it
    // come in on the open quarter's side.
    [Test]
    public void AHealerWhoComesInOnTheTanksSideOfTheMiddleDiesAloneOfTheSecondBusters([Values(RegenHealer, ShieldHealer)] PartyRole seat)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { ComesInTo = new Vector2(1.5f, 3.0f) };
            var (run, human, _) = Play(roll, play, 561, mayLose: true);
            problems.AddRange(Faults(roll, human, run).Select(fault => $"{roll}: {fault}"));
            if (run.Failure is not null || run.Deaths.Count != 1
                || run.Deaths[0] is not { ActionId: P1ActionId.FluidSwing or P1ActionId.FluidStrike, Time: > 56.4f and < 57f } || run.Deaths[0].Role != seat
                || !run.Deaths[0].Cause.Contains("Caught in Fluid"))
                problems.Add($"{roll}: the healer did not die alone of a cleave at 55 s: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }


    // Some guides draw both healers in the middle while the dolls are up. Here the melee's dolls stay where they
    // appear, 7 y out, and an Exhaust reaches 8.8 y: H2's spot is inside both on every layout, and H1's, 2 y north of
    // the middle, on the four where they stand south-west and east-south-east of it. The explosion takes both melee
    // along. Where the dolls stand west-south-west and south-east, H1 is 9.1 y from the second and nobody dies.
    [Test]
    public void AHealerInTheMiddleFromTheMomentTheDollsAppearExplodesAtTheFirstExhaustWhereBothMeleeDollsReachIt([Values(RegenHealer, ShieldHealer)] PartyRole seat)
    {
        var problems = new List<string>();
        foreach (var open in Layouts)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(seat, Paces[(int)open % 2], ReactionTimes[(int)open % 3]) { Waits = TeaP1HumanHealerWaits.InTheMiddle };
            var (run, human, saw) = Play(roll, play, 425, mayLose: true);
            var faults = Faults(roll, human, run).ToList();
            if (seat == RegenHealer && Cardinal(open)) faults.AddRange(LeftUndone(run, human, saw).Concat(Lost(run)));
            else if (run.Failure is not null || !run.Deaths.Select(death => death.Role).Order().SequenceEqual(new[] { seat, MeleeDpsA, MeleeDpsB }.Order())
                     || run.Deaths.Any(death => MathF.Abs(death.Time - TheFirstExhaust) > 0.1f || !death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst)))
                faults.Add($"the healer and both melee did not die of its explosion at the first Exhaust: {run}");
            problems.AddRange(faults.Select(fault => $"{roll}: {fault}"));
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
    }
}
