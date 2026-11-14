using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaP1LivingLiquidScenarioTests;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// The strat's spots are the ones TeaP1LivingLiquidStage puts every seat on, cue by cue (its header has the timeline),
// each in the frame of the tornadoes standing then, and a spot hung off the Living Liquid from where it stands as the
// move is called. Bots walk there, so each cue is read once they have arrived. Where the strat differs from the stage:
//
//   - each tank fetches its own Drainage tether from 20.25 and walks it back to its spot (the main tank's 3.9 y
//     south-east of the middle, the off tank's at (-5.4, 5.4)): the east tornado's is the main tank's and the west
//     one's the off tank's, and the middle one's goes to the tank whose side has none;
//   - the main tank's fetch drags the Living Liquid off the middle, so at Cascade 2's 6.4 it steps onto the middle,
//     which the Liquid lets it do without moving, and at 7.2 walks on straight away from the Liquid, 4 y out: the
//     Liquid follows in a line and stops on the middle;
//   - the off tank walks to the west tornado from 25.2 with no wait on the way;
//   - both healers cleanse at 11.2, 13.7, 16.2 and 18.7, each itself first, then H1 down the party list and H2 up it.
public class TeaP1LivingLiquidLpduAiTests
{
    internal const int LpduStrat = 0;

    // The bots' 0.3 y jitter, and the frame's step the debug-driven player's seat can stop short by.
    private const float Tolerance = 0.45f;

    internal static readonly PartyRole[] Tanks = [MainTank, OffTank];
    internal static readonly PartyRole[] TheSix = [RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    // A run of the strat: both layouts pinned, the other rolls too where a test needs them, the rest left to the seed.
    internal sealed record Roll
    {
        public TeaP1LivingLiquidPart First { get; init; } = Protean1;
        public TeaP1LivingLiquidPart Last { get; init; } = Cascade2;
        public TeaP1OpenQuarter Open1 { get; init; } = TeaP1OpenQuarter.North;
        public TeaP1OpenQuarter Open2 { get; init; } = TeaP1OpenQuarter.North;
        public IReadOnlyList<TeaP1DrainageRoll>? Drainage { get; init; }
        public IReadOnlyList<PartyRole>? Throttled { get; init; }
        public IReadOnlyList<TeaP1EmbolusRoll>? Emboli { get; init; }
        public float? EmbolusSpeed { get; init; }

        // The debug driver still walks the player's seat, but the strat presses nothing for it.
        public bool PlayerPressesItsOwnActions { get; init; }

        // The player's job as its client has it; null leaves its seat's own.
        public JobId? PlayersJob { get; init; }

        public bool Plays(TeaP1LivingLiquidPart part) => part >= First && part <= Last;

        // A part's time on the run's clock.
        public float P1(float time) => time;
        public float C2(float time) => time + (First == Protean1 ? Stage.CascadeTwo : 0f);
        public float At(TeaP1LivingLiquidPart part, float time) => part == Protean1 ? P1(time) : C2(time);

        public TeaP1TornadoLayout Frame1 => new(Open1);
        public TeaP1TornadoLayout Frame2 => new(Open2);

        public Stage Stage => new() { First = First, Last = Last, Open1 = Open1, Open2 = Open2 };

        public void Pin(TeaP1LivingLiquidStateOverrides overrides)
        {
            overrides.Parts = (First, Last);
            overrides.Tornadoes1 = Open1;
            overrides.Tornadoes2 = Open2;
            overrides.Drainage = Drainage;
            overrides.Throttled = Throttled;
            overrides.Emboli = Emboli;
            overrides.EmbolusSpeed = EmbolusSpeed;
            overrides.PlayerPressesItsOwnActions = PlayerPressesItsOwnActions;
        }

        public override string ToString()
            => $"{First} to {Last}, tornadoes open {Open1} then {Open2}"
               + (Drainage is null ? "" : $", tethers {string.Join(" and ", Drainage.Select(roll => $"{roll.From} on {roll.FirstTarget}"))}");
    }

    internal static ScenarioRun Play(Roll roll, PartyRole player, int seed, Action<ScenarioProbe>? probe = null, float? stopAt = null)
        => ScenarioRun.Execute(typeof(TeaP1LivingLiquidScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            Configure = roll.PlayersJob is { } job ? _ => ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)job : null,
            Overrides = o => roll.Pin((TeaP1LivingLiquidStateOverrides)o),
            Probe = probe,
        });

    internal static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // -- The spots --

    // A stage cue, on its part's clock, and when the bots it sends have arrived; `Seats` when only some have by then.
    private sealed record Arrival(TeaP1LivingLiquidPart Part, float Cue, float By, PartyRole[]? Seats = null);

    private static readonly Arrival[] Arrivals =
    [
        new(Protean1, 0.05f, 1.4f),
        new(Protean1, 1.5f, 4.7f),
        new(Protean1, 5.3f, 9.8f),
        new(Protean1, 9.95f, 12.9f),
        new(Protean1, 13.05f, 19.9f),
        new(Cascade2, 0.9f, 5.8f),
        new(Cascade2, 9.5f, 11.9f),
        new(Cascade2, 12.0f, 18.1f),
        new(Cascade2, 18.7f, 21.2f, [MainTank, OffTank, MeleeDpsA, MeleeDpsB]),
        new(Cascade2, 18.7f, 23.2f, [MainTank, OffTank, MeleeDpsB, RegenHealer, ShieldHealer, PhysRangedDps, CasterDps]),
        new(Cascade2, 21.3f, 23.2f),
        new(Cascade2, 23.35f, 26.2f),
        new(Cascade2, 24.25f, 26.25f, [RegenHealer, ShieldHealer, PhysRangedDps, CasterDps]),
        new(Cascade2, 24.25f, 29.2f),
        new(Cascade2, 27.0f, 29.2f),
        new(Cascade2, 29.35f, 33.1f),
        new(Cascade2, 33.5f, 38.3f),
    ];

    [Test]
    public void StandsOnItsSpotsThroughProteanOne([Values] TeaP1OpenQuarter open)
        => AssertStands(new Roll { Last = Protean1, Open1 = open }, PerRole.All[(int)open]);

    [Test]
    public void StandsOnItsSpotsFromCascadeTwo([Values] TeaP1OpenQuarter open2,
        [Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast, TeaP1OpenQuarter.West)] TeaP1OpenQuarter open1)
        => AssertStands(new Roll { First = Cascade2, Open1 = open1, Open2 = open2 }, PerRole.All[((int)open2 + (int)open1) % 8]);

    [Test]
    public void StandsOnItsSpotsThroughTheWholeFight([Values] TeaP1OpenQuarter open1, [Values(1, 4, 6)] int turnsOn)
        => AssertStands(new Roll { Open1 = open1, Open2 = (TeaP1OpenQuarter)(((int)open1 + turnsOn) % 8) }, PerRole.All[((int)open1 + turnsOn) % 8]);

    private static void AssertStands(Roll roll, PartyRole player)
    {
        var seed = Random.Shared.Next();
        var stage = roll.Stage;
        var cues = stage.Cues().ToList();
        var expected = new Dictionary<Stage.Cue, Dictionary<PartyRole, Vector2>>();
        var problems = new List<string>();
        var read = 0;
        var run = Play(roll, player, seed, p =>
        {
            foreach (var cue in cues)
                if (p.Crossed(cue.At)) expected[cue] = stage.ArenaSpots(p, cue);
            foreach (var arrival in Arrivals.Where(arrival => roll.Plays(arrival.Part) && p.Crossed(roll.At(arrival.Part, arrival.By))))
            {
                var cueAt = arrival is { Part: Cascade2, Cue: 0.9f } && roll.First == Cascade2 ? 0.05f : roll.At(arrival.Part, arrival.Cue);
                var cue = cues.Single(cue => MathF.Abs(cue.At - cueAt) < 0.001f);
                foreach (var (seat, spot) in expected[cue])
                {
                    if (arrival.Seats is { } seats && !seats.Contains(seat)) continue;
                    read++;
                    var stands = Flat(p.Member(seat)!.Position);
                    if (Vector2.Distance(stands, spot) >= Tolerance)
                        problems.Add($"{seat} at {stands} at {arrival.Part} {F(arrival.By)}, {F(Vector2.Distance(stands, spot))} y from its spot {spot} of {F(arrival.Cue)}");
                }
            }
        });
        var context = $"{roll}, player {player}, seed {seed}";

        Assert.That(run.Passed, () => $"{run}{Environment.NewLine}{context}");
        Assert.That(problems, Is.Empty, context);
        Assert.That(read, Is.EqualTo((roll.Plays(Protean1) ? 40 : 0) + (roll.Plays(Cascade2) ? 54 : 0)), "every cue was read");
    }

    // -- The bosses --

    private const float OnTheMiddleWithin = 0.4f;
    private const float FacesItsTanksWithinDegrees = 5f;
    private const float TheLiquidStaysWithinReachOfItsTank = 4.3f;
    private const float InsideEveryNewPuddlesReach = 6.2f;

    // Protean 1's telegraph is aimed at 4.8 with the Liquid on the middle facing the tanks south of it, and Cascade
    // 2's at 18.2 with it facing the main tank north of it. While it follows, the main tank stands inside its reach
    // (4.0 y), so it only turns; the tank lets it walk three times: fetching its tether (until the walks of Cascade 2's
    // 6.4 and 7.2 have brought it back, by 9.0), backing out north-east at 23.35 and for the last hand.
    [Test]
    public void TheMainTankHoldsTheLiquidOnTheMiddleFacingTheStacks([Values] TeaP1OpenQuarter open1, [Values(0, 3, 5)] int turnsOn,
        [Values(Protean1, Cascade2)] TeaP1LivingLiquidPart first)
    {
        var roll = new Roll { First = first, Open1 = open1, Open2 = (TeaP1OpenQuarter)(((int)open1 + turnsOn) % 8) };
        var problems = new List<string>();
        var farthestFromItsTank = 0f;
        for (var i = 0; i < 4; i++)
        {
            var seed = Random.Shared.Next();
            var player = PerRole.All[((int)open1 + turnsOn + 3 * i) % 8];
            var context = $"{roll}, player {player}, seed {seed}";
            var read = 0;
            var run = Play(roll, player, seed, p =>
            {
                var fight = Stage.Fight(p);
                var liquid = Flat(fight.Position(TeaP1Boss.LivingLiquid));
                if (roll.Plays(Protean1) && p.Crossed(roll.P1(4.85f)))
                {
                    read++;
                    if (liquid.Length() > OnTheMiddleWithin) problems.Add($"the Liquid {F(liquid.Length())} y off the middle at Protean 1's aim ({context})");
                    var off = MathF.Abs(Turn(fight.Liquid!.Rotation, roll.Frame1.Facing(180f))) * 180f / MathF.PI;
                    if (off > FacesItsTanksWithinDegrees) problems.Add($"the Liquid {F(off)} degrees off south at Protean 1's aim ({context})");
                    if (!fight.FacingLocked) problems.Add($"the facing is not locked at Protean 1's aim ({context})");
                }
                if (p.Crossed(roll.C2(5.9f)))
                {
                    read++;
                    foreach (var seat in PerRole.All)
                        if (Flat(p.Member(seat)!.Position).Length() > InsideEveryNewPuddlesReach)
                            problems.Add($"{seat} {F(Flat(p.Member(seat)!.Position).Length())} y from the middle as the new tornadoes appear ({context})");
                }
                if (p.Crossed(roll.C2(9.5f)))
                {
                    read++;
                    if (liquid.Length() > OnTheMiddleWithin) problems.Add($"the Liquid {F(liquid.Length())} y off the middle at 9.5 ({context})");
                }
                if (p.Crossed(roll.C2(18.25f)))
                {
                    read++;
                    if (liquid.Length() > OnTheMiddleWithin) problems.Add($"the Liquid {F(liquid.Length())} y off the middle at Protean 2's aim ({context})");
                    var off = MathF.Abs(Turn(fight.Liquid!.Rotation, roll.Frame2.Facing(0f))) * 180f / MathF.PI;
                    if (off > FacesItsTanksWithinDegrees) problems.Add($"the Liquid {F(off)} degrees off north at Protean 2's aim ({context})");
                }
                if (fight.FacingLocked || !HoldsItStill(roll, p.Time)) return;
                var apart = Vector2.Distance(liquid, Flat(p.Member(MainTank)!.Position));
                farthestFromItsTank = MathF.Max(farthestFromItsTank, apart);
                if (apart > TheLiquidStaysWithinReachOfItsTank)
                    problems.Add($"the main tank {F(apart)} y from the Liquid at {F(p.Time)} ({context})");
            });
            Assert.That(run.Passed, () => $"{run}{Environment.NewLine}{context}");
            Assert.That(read, Is.EqualTo(roll.Plays(Protean1) ? 4 : 3), context);
        }

        TestContext.Out.WriteLine($"The main tank's farthest from a Liquid it holds still: {F(farthestFromItsTank)} y");
        Assert.That(problems.Distinct().Take(12), Is.Empty);
    }

    private static bool HoldsItStill(Roll roll, float time)
    {
        if (roll.Plays(Protean1) && time >= roll.P1(0.6f) && time <= roll.P1(20.2f)) return true;
        return time >= roll.C2(roll.Plays(Protean1) ? 9.0f : 0.6f) && time <= roll.C2(23.3f);
    }

    // The tanks stand 12 y or more apart at the glow, so the Hand shows a palm, and each then walks through the middle
    // and stops beyond it, its boss's stopping distance out.
    [Test]
    public void TheTanksShowAPalmAndBringBothBossesOntoTheMiddle([Values] TeaP1OpenQuarter open2)
    {
        var roll = new Roll { First = Cascade2, Open1 = TeaP1OpenQuarter.Northeast, Open2 = open2 };
        bool? palm = null;
        float apartAtTheGlow = 0f, liquidOff = float.NaN, handOff = float.NaN;
        var run = Play(roll, PerRole.All[(int)open2], Random.Shared.Next(), p =>
        {
            var fight = Stage.Fight(p);
            var (liquid, hand) = (Flat(fight.Position(TeaP1Boss.LivingLiquid)), Flat(fight.Position(TeaP1Boss.LiquidHand)));
            if (p.Crossed(33.25f)) (palm, apartAtTheGlow) = (fight.HandShowsAPalm, Vector2.Distance(liquid, hand));
            if (p.Crossed(38.35f)) (liquidOff, handOff) = (liquid.Length(), hand.Length());
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(palm, Is.True);
            Assert.That(apartAtTheGlow, Is.GreaterThan(P1Rules.HandShowsAPalmBeyond + 5f));
            Assert.That(liquidOff, Is.LessThan(1f), "the Living Liquid from the middle as the Hand slams");
            Assert.That(handOff, Is.LessThan(1f), "the Liquid Hand from the middle as it slams");
        });
    }

    // -- Drainage --

    internal static readonly (TeaP1Tornado First, TeaP1Tornado Second)[] TetheringPairs =
        [(TeaP1Tornado.West, TeaP1Tornado.Middle), (TeaP1Tornado.West, TeaP1Tornado.East), (TeaP1Tornado.Middle, TeaP1Tornado.East)];

    // Whom the pair's first and second tether start on.
    public enum StartsOn { TheStack, TheMainTankAndTheStack, TheStackAndTheMainTank, TheOffTankAndTheStack, TheStackAndTheOffTank, TheTanksUncrossed, TheTanksCrossed }

    // The strat's rule, written out: the tank's own side's tornado if it tethers, else the middle one.
    internal static TeaP1Tornado Own(PartyRole tank, (TeaP1Tornado First, TeaP1Tornado Second) pair)
    {
        var itsSide = tank == MainTank ? TeaP1Tornado.East : TeaP1Tornado.West;
        return pair.First == itsSide || pair.Second == itsSide ? itsSide : TeaP1Tornado.Middle;
    }

    internal static TeaP1DrainageRoll[] Tethers((TeaP1Tornado First, TeaP1Tornado Second) pair, StartsOn startsOn, PartyRole stackA, PartyRole stackB)
    {
        var ownerOfTheFirst = Own(MainTank, pair) == pair.First ? MainTank : OffTank;
        var ownerOfTheSecond = ownerOfTheFirst == MainTank ? OffTank : MainTank;
        var (first, second) = startsOn switch
        {
            StartsOn.TheMainTankAndTheStack => (MainTank, stackA),
            StartsOn.TheStackAndTheMainTank => (stackA, MainTank),
            StartsOn.TheOffTankAndTheStack => (OffTank, stackA),
            StartsOn.TheStackAndTheOffTank => (stackA, OffTank),
            StartsOn.TheTanksUncrossed => (ownerOfTheFirst, ownerOfTheSecond),
            StartsOn.TheTanksCrossed => (ownerOfTheSecond, ownerOfTheFirst),
            _ => (stackA, stackB),
        };
        return [new(pair.First, first), new(pair.Second, second)];
    }

    // The one start a tank is not back from by Drainage: the main tank takes the middle tether off the off tank first,
    // and only then is the off tank free to fetch the west one, the far side of its own spot.
    internal static bool TheOffTankFetchesLate((TeaP1Tornado First, TeaP1Tornado Second) pair, StartsOn startsOn)
        => pair == (TeaP1Tornado.West, TeaP1Tornado.Middle) && startsOn == StartsOn.TheStackAndTheOffTank;

    private const float TanksApartAtLeast = 7.5f;
    private const float ATankFromTheSixAtLeast = 8.5f;
    private const float OnItsDrainageSpotWithin = 0.5f;
    private const float TheLateOffTankFromItsSpotAtMost = 4.5f;

    internal static Vector2 DrainageSpot(Roll roll, PartyRole tank)
        => roll.Frame1.ToArena(tank == MainTank ? Stage.Off(Vector2.Zero, 3.9f, 135f) : new Vector2(-5.4f, 5.4f));

    [Test]
    public void EachTankEndsWithItsOwnTetherOnItsSpotWhereverTheTethersStart([Values(0, 1, 2)] int pair, [Values] StartsOn startsOn)
    {
        var tethering = TetheringPairs[pair];
        var problems = new List<string>();
        foreach (var open in new[] { TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southwest })
        for (var i = 0; i < 6; i++)
        {
            var seed = Random.Shared.Next();
            var (stackA, stackB) = (TheSix[(i + pair) % 6], TheSix[(i + pair + 1 + i / 2) % 6]);
            var roll = new Roll { Last = Protean1, Open1 = open, Drainage = Tethers(tethering, startsOn, stackA, stackB) };
            var player = PerRole.All[(i * 3 + pair + (int)startsOn) % 8];
            var context = $"{roll}, player {player}, seed {seed}";
            var read = 0;
            var run = Play(roll, player, seed, p =>
            {
                if (p.Time < 20f || p.Time > 24.59f) return;
                var fight = Stage.Fight(p);
                var holders = fight.DrainageTethersOut.Select(tether => (tether.From, Holder: Seat(fight.Holder(tether.Tether)))).ToList();
                foreach (var tank in Tanks)
                    if (holders.Count(holder => holder.Holder == tank) > 1) problems.Add($"{tank} holds two tethers at {F(p.Time)} ({context})");
                if (p.Crossed(24.4f) && !TheOffTankFetchesLate(tethering, startsOn))
                {
                    read++;
                    foreach (var tank in Tanks)
                    {
                        var from = Vector2.Distance(Flat(p.Member(tank)!.Position), DrainageSpot(roll, tank));
                        if (from > OnItsDrainageSpotWithin) problems.Add($"{tank} {F(from)} y from its spot at 24.4 ({context})");
                    }
                }
                if (!p.Crossed(24.55f)) return;
                read++;
                foreach (var (from, holder) in holders)
                {
                    if (holder is not { } seat || !seat.IsTank()) problems.Add($"the {from} tether on {holder} at Drainage ({context})");
                    else if (startsOn != StartsOn.TheTanksCrossed && Own(seat, tethering) != from) problems.Add($"{seat} holds the {from} tether at Drainage ({context})");
                }
                var (mainTank, offTank) = (Flat(p.Member(MainTank)!.Position), Flat(p.Member(OffTank)!.Position));
                if (Vector2.Distance(mainTank, offTank) < TanksApartAtLeast) problems.Add($"the tanks {F(Vector2.Distance(mainTank, offTank))} y apart at Drainage ({context})");
                foreach (var tank in Tanks)
                foreach (var seat in TheSix)
                {
                    var apart = Vector2.Distance(Flat(p.Member(tank)!.Position), Flat(p.Member(seat)!.Position));
                    if (apart < ATankFromTheSixAtLeast) problems.Add($"{tank} {F(apart)} y from {seat} at Drainage ({context})");
                }
                if (TheOffTankFetchesLate(tethering, startsOn))
                {
                    var from = Vector2.Distance(offTank, DrainageSpot(roll, OffTank));
                    if (from > TheLateOffTankFromItsSpotAtMost) problems.Add($"the off tank {F(from)} y from its spot at Drainage ({context})");
                }
            });
            if (!run.Passed) problems.Add($"{run} ({context})");
            if (read != (TheOffTankFetchesLate(tethering, startsOn) ? 1 : 2)) problems.Add($"Drainage was not read ({context})");
        }

        Assert.That(problems, Is.Empty);
    }

    // -- Throttle --

    private sealed record Cleansed(PartyRole By, PartyRole Target, float At, bool ByHuman);

    private static List<Cleansed> CleansesOf(ScenarioProbe p)
        => Stage.Scenario(p).Presses!.Cleanses.Select(cleanse => new Cleansed(Seat(cleanse.By)!.Value, Seat(cleanse.Target)!.Value, cleanse.At, cleanse.ByHuman)).ToList();

    private static readonly PartyRole[] BothHealersThrottled = [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, PhysRangedDps];
    private static readonly PartyRole[] NeitherHealerThrottled = [MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static IEnumerable<TestCaseData> CleanseOrders()
    {
        yield return new TestCaseData(BothHealersThrottled, new[]
        {
            (RegenHealer, RegenHealer, 11.2f), (ShieldHealer, ShieldHealer, 11.2f),
            (RegenHealer, MainTank, 13.7f), (ShieldHealer, PhysRangedDps, 13.7f),
            (RegenHealer, OffTank, 16.2f), (ShieldHealer, MeleeDpsA, 16.2f),
        }).SetArgDisplayNames("both healers have it");
        yield return new TestCaseData(NeitherHealerThrottled, new[]
        {
            (RegenHealer, MainTank, 11.2f), (ShieldHealer, CasterDps, 11.2f),
            (RegenHealer, OffTank, 13.7f), (ShieldHealer, PhysRangedDps, 13.7f),
            (RegenHealer, MeleeDpsA, 16.2f), (ShieldHealer, MeleeDpsB, 16.2f),
        }).SetArgDisplayNames("neither healer has it");
        yield return new TestCaseData(new[] { RegenHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps, OffTank }, new[]
        {
            (RegenHealer, RegenHealer, 11.2f), (ShieldHealer, CasterDps, 11.2f),
            (RegenHealer, OffTank, 13.7f), (ShieldHealer, PhysRangedDps, 13.7f),
            (RegenHealer, MeleeDpsA, 16.2f), (ShieldHealer, MeleeDpsB, 16.2f),
        }).SetArgDisplayNames("one healer has it");
    }

    // The player's seat, driven as a bot that presses nothing, is cleansed for like any bot's.
    [TestCaseSource(nameof(CleanseOrders))]
    public void TheHealersCleanseInLpdusOrderAndAreDoneBySixteenPointTwo(PartyRole[] throttled, (PartyRole By, PartyRole Target, float At)[] order)
    {
        foreach (var player in new[] { MeleeDpsB, RegenHealer, ShieldHealer })
        {
            var roll = new Roll { First = Cascade2, Throttled = throttled };
            List<Cleansed> cleanses = [];
            PartyRole[]? left = null;
            var run = Play(roll, player, Random.Shared.Next(), p =>
            {
                if (p.Crossed(16.3f)) left = Throttled(p);
                if (p.Crossed(19.6f)) cleanses = CleansesOf(p);
            });

            Assert.That(run.Passed, () => $"{run}, player {player}");
            Assert.That(left, Is.Empty, $"all six are cleansed by 16.2 (player {player})");
            Assert.That(cleanses.Select(cleanse => (cleanse.By, cleanse.Target)), Is.EqualTo(order.Select(step => (step.By, step.Target))), $"player {player}");
            Assert.That(cleanses.Select(cleanse => cleanse.At), Is.EqualTo(order.Select(step => step.At)).Within(0.02f), $"player {player}");
            Assert.That(cleanses.Select(cleanse => cleanse.ByHuman), Is.All.False);
        }
    }

    // The other healer, alone, gets to four by 18.7: itself, then up the list to H1. The tanks, H1's to cleanse, die.
    [Test]
    public void TheStratPressesNothingForAPlayerWhoPressesItsOwn()
    {
        var roll = new Roll { First = Cascade2, Throttled = BothHealersThrottled, PlayerPressesItsOwnActions = true };
        List<Cleansed> cleanses = [];
        var run = Play(roll, RegenHealer, Random.Shared.Next(), p =>
        {
            if (p.Crossed(19.4f)) cleanses = CleansesOf(p);
        }, stopAt: 21f);

        Assert.That(cleanses.Select(cleanse => (cleanse.By, cleanse.Target)), Is.EqualTo(new[]
        {
            (ShieldHealer, ShieldHealer), (ShieldHealer, PhysRangedDps), (ShieldHealer, MeleeDpsA), (ShieldHealer, RegenHealer),
        }));
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EquivalentTo(new[] { (MainTank, (uint?)P1ActionId.LiquidGaol), (OffTank, (uint?)P1ActionId.LiquidGaol) }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1LivingLiquid.ThrottleRanOut));
    }

    // A healer's seat on a job without Esuna is held to no cleanse: the run makes the seat's, on the bots' times.
    [Test]
    public void TheRunCleansesForAHealersSeatOnAJobWithoutEsuna([Values(RegenHealer, ShieldHealer)] PartyRole seat,
        [Values(JobId.Monk, JobId.Warrior, JobId.Bard)] JobId job)
    {
        var roll = new Roll { First = Cascade2, Throttled = BothHealersThrottled, PlayerPressesItsOwnActions = true, PlayersJob = job };
        List<Cleansed> cleanses = [];
        var run = Play(roll, seat, Random.Shared.Next(), p =>
        {
            if (p.Crossed(19.4f)) cleanses = CleansesOf(p);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(cleanses.Where(cleanse => cleanse.By == seat).Select(cleanse => cleanse.At), Is.EqualTo(new[] { 11.2f, 13.7f, 16.2f }).Within(0.02f));
        Assert.That(cleanses, Has.Count.EqualTo(6));
    }
}
