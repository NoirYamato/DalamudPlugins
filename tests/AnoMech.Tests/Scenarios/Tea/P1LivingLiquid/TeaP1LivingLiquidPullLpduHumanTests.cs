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

// The pull with the player's seat a human from the first frame (TeaP1LivingLiquidHuman has how it plays), pressing its
// own buttons: the strat presses nothing for it. It starts where a run from the pull puts it, reads its north off the
// tornadoes as they appear with the Liquid Hand, and plays its seat from what it sees.
//
// On every tornado layout, at either pace and up to half a second late, nobody may die, every doll goes to the damage
// dealer whose quarter it stands in and is fed, and the bosses end on their own tanks. What a rule of the fight kills
// is each its own test, and says who dies of what; so is what a seat can get wrong and still lose nobody.
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

    private static Roll ThePull(TeaP1OpenQuarter open) => new() { First = Pull, Last = Pull, Open1 = open, PlayerPressesItsOwnActions = true };

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
        foreach (var seed in Seeds)
        {
            var roll = ThePull(open);
            var (run, human, saw) = Play(roll, new TeaP1LivingLiquidHumanPlay(seat, pace, late), seed);
            runs.Add($"seed {seed}, {roll}", run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
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
            Drainage = Tethers(TetheringPairs[protean.Next(3)], StartsOn.TheStack, TheSix[first], TheSix[(first + 1 + protean.Next(5)) % 6]),
            PlayerPressesItsOwnActions = true,
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
            var faults = Faults(roll, human, run).Concat(LeftUndone(run, human, saw)).ToList();
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
    // actions; with those off no buster asks for a cooldown.
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

    // The feeders go to the bosses where they stand, and the tanks' holds hang off them.
    [Test]
    public void AHumanMainTankWhoLeadsTheLiquidOntoTheMiddleLateLosesNobody([Values(2f, 4f)] float leadsItInLate)
    {
        var runs = new TeaP1HumanRuns();
        foreach (var open in Layouts)
        foreach (var late in ReactionTimes)
        {
            var roll = ThePull(open);
            var play = new TeaP1LivingLiquidHumanPlay(MainTank, Paces[(int)open % 2], late) { LeadsTheLiquidInLate = leadsItInLate };
            var (run, human, saw) = Play(roll, play, 170);
            runs.Add($"{roll}, {Secs(late)}s late", run, Faults(roll, human, run).Concat(LeftUndone(run, human, saw)));
        }

        runs.AssertNoneLostBut();
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

    // -- The off tank --

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
    // Exhaust goes off, and explodes among the tanks.
    [TestCase(MeleeDpsA, true)]
    [TestCase(MeleeDpsB, false)]
    public void AMeleeWhoWalksItsDollToTheBossesTwoSecondsAfterItsTetherKillsEveryoneOrExplodesOnTheWay(PartyRole seat, bool reachesABoss)
    {
        var problems = new List<string>();
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
            else if (run.Deaths.All(death => death.Role != seat) || saw.FedAt.ContainsKey(seat)
                     || run.Deaths.Any(death => MathF.Abs(death.Time - TheFirstExhaust) > 0.1f || !death.Cause.Contains(TeaP1JagdDolls.LuminousAetheroplasmBurst)))
                problems.Add($"{roll}: the human did not explode at the first Exhaust, or someone died of something else: {run}");
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(12)));
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

    // -- A healer --

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
