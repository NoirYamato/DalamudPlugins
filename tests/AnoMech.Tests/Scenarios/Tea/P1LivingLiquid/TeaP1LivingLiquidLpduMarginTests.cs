using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaP1LivingLiquidLpduAiTests;
using static AnoMech.Tests.TeaP1LivingLiquidScenarioTests;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These runs measure by how much: for every pick the fight makes by
// distance, how much nearer the seat the strat means stands than the next one; for every wave, how many degrees outside
// its cone everyone else stands as it decides; how far everyone is from each Sluice and each puddle; how far apart the
// tanks and the bosses are when a rule reads it. The strat plays on every pair of tornado layouts, the tethers on
// every pair of tornadoes and every start, and the Emboli from every pair of tornadoes at either boss; the player's
// seat moves round, driven as a bot.
//
// The floors come from the strat's spots, not from what these runs measure: each is the least the spots give with
// every bot anywhere in its jitter (0.3 y, or 0.15 y on a bait) and the Living Liquid up to half a yalm off the middle; less a
// frame's walk (0.15 y, or the 1.5 degrees that is from a bait), which the debug-driven player's seat can stop short
// by. A run that falls under one has a bot off its spot or late to it.
public class TeaP1LivingLiquidLpduMarginTests
{
    private static int Seeds => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 2));

    private const float WaveHalfAngle = 15f;
    private const float SluiceRadius = 5f;

    // A cast with a bar decides whom it hits this long after it starts, 0.3 s before its effect shows.
    private const float WaveDecidesAfter = 2.7f;

    private const string P1Telegraph = "Protean 1, the telegraphed waves: everyone outside every cone (deg)";
    private const string P1Set1Pick = "Protean 1, set 1 and its Sluices: fifth closest less fourth (y)";
    private const string P1Set1Cones = "Protean 1, set 1: everyone outside every cone but its own (deg)";
    private const string P1Sluices = "Protean 1: everyone outside every Sluice (y)";
    private const string P1Set2Pick = "Protean 1, set 2: fifth closest less fourth (y)";
    private const string P1Set2Cones = "Protean 1, set 2: everyone outside every cone but its own (deg)";
    private const string P1Puddles = "Protean 1: anyone to a puddle's edge (y)";
    private const string TanksApart = "Drainage: the tanks apart less its 6 y (y)";
    private const string TankToTheSix = "Drainage: a tank to the nearest of the six less its 6 y (y)";
    private const string P2Telegraph = "Protean 2, the telegraphed waves: everyone outside every cone (deg)";
    private const string P2Set1Pick = "Protean 2, set 1 and its Sluices: fifth closest less fourth (y)";
    private const string P2Set1Cones = "Protean 2, set 1: everyone outside every cone but its own (deg)";
    private const string P2Sluices = "Protean 2: everyone outside every Sluice (y)";
    private const string TornadoAim = "Protean 2, the tornadoes' aim: next closest less the baiter (y)";
    private const string P2Set2Pick = "Protean 2, set 2: fifth closest less fourth (y)";
    private const string P2Set2Cones = "Protean 2, set 2: everyone outside every cone but its own (deg)";
    private const string TornadoTelegraphs = "Protean 2, the tornadoes' telegraphed waves: everyone outside every cone (deg)";
    private const string TornadoWavesPick = "Protean 2, the tornadoes' unseen waves: next closest less the baiter (y)";
    private const string TornadoWavesCones = "Protean 2, the tornadoes' unseen waves: everyone outside every cone but its own (deg)";
    private const string BaiterToItsPuddle = "Protean 2: a tornado's baiter to its puddle's edge as it is picked (y)";
    private const string C2Puddles = "Cascade 2: anyone to a puddle's edge (y)";
    private const string BetweenWaves = "A seat's two waves apart less Water Resistance Down (s)";
    private const string ApartAtTheGlow = "The last hand: the bosses apart as it glows less a palm's 12 y (y)";
    private const string TogetherAtTheSlam = "The last hand: a palm's 5.1 y less the bosses apart as it slams (y)";
    private const string EmbolusSpeed = "An Embolus: the speed from which it would touch a bot or a boss (y/s)";
    private const string EmbolusClear = "An Embolus: the nearest bot or boss as played, less its 1 y (y)";

    private const string FacingOffSouth = "Protean 1: the locked facing off south (deg)";
    private const string FacingOffNorth = "Protean 2: the locked facing off north (deg)";
    private const string LiquidOffAtProteanOne = "Protean 1's aim: the Liquid off the middle (y)";
    private const string LiquidOffAtDrainage = "Drainage: the Liquid off the middle (y)";
    private const string LiquidOffAtTheNewTornadoes = "Cascade 2, the new tornadoes: the Liquid off the middle (y)";
    private const string LiquidOffAtNinePointFive = "Cascade 2, 9.5: the Liquid off the middle (y)";
    private const string LiquidOffAtProteanTwo = "Protean 2's aim: the Liquid off the middle (y)";
    private const string TankOffItsSpot = "Drainage: a tank off its spot (y)";
    private const string LateOffTankOffItsSpot = "Drainage: the off tank off its spot after the one late fetch (y)";
    private const string TethersOnTheTanksBy = "Drainage: both tethers on their tanks by (s)";
    private const string RunInArrivesBy = "Protean 2, the run in: the last of the four on its bait by (s)";

    private static readonly (string Check, float AtLeast)[] Floors =
    [
        (P1Telegraph, 8.2f),
        (P1Set1Pick, 6.45f),
        (P1Set1Cones, 18.5f),
        // H1's bait is 8.2 y from where it left its own.
        (P1Sluices, 2.5f),
        (P1Set2Pick, 1.35f),
        (P1Set2Cones, 14.2f),
        // A walker keeps outside the 7.8 y the bots give a tornado, 0.2 y off its water.
        (P1Puddles, 0.1f),
        (P2Telegraph, 6.5f),
        (P2Set1Pick, 9.55f),
        (P2Set1Cones, 9.3f),
        // M1, anywhere within 8.7 y of the middle tornado on its way round it, passes 7.8 y from R1's.
        (P2Sluices, 2.6f),
        (TornadoAim, 0.75f),
        (P2Set2Pick, 2.95f),
        (P2Set2Cones, 9.0f),
        // The off tank, 1.7 s into its walk round the west tornado's water, is 15.5 degrees outside the middle
        // tornado's wave at H2 if it hugs the 7.8 y. H2's jitter turns the wave 2 degrees, and a walker steers a little
        // wider than it must. On its spot the off tank would be 2.9 outside.
        (TornadoTelegraphs, 11.0f),
        (TornadoWavesPick, 1.0f),
        (TornadoWavesCones, 35.4f),
        (BaiterToItsPuddle, 0.35f),
        (C2Puddles, 0.1f),
        // Set 1 at 23.3, the tornadoes' unseen waves at 29.3.
        (BetweenWaves, 0.95f),
        (ApartAtTheGlow, 10.3f),
        (TogetherAtTheSlam, 4.0f),
    ];

    // The facing: a tank's 0.15 y jitter and a frame's walk, seen from the Liquid 3.3 y (south) or 3.9 y (north) off.
    // The main tank leaves the Liquid where it stands when within 0.3 y of the middle at Cascade 2's 6.4.
    private static readonly (string Check, float AtMost)[] Ceilings =
    [
        (FacingOffSouth, 4.5f),
        (FacingOffNorth, 3.8f),
        (LiquidOffAtProteanOne, 0.5f),
        (LiquidOffAtNinePointFive, 0.35f),
        (LiquidOffAtProteanTwo, 0.4f),
        (RunInArrivesBy, 26.3f),
    ];

    private sealed class Margins
    {
        public readonly Dictionary<string, (float Value, string Where)> Least = [];
        public readonly Dictionary<string, (float Value, string Where)> Most = [];
        public readonly List<string> Problems = [];

        public void AtLeast(string check, float value, string where)
        {
            if (!Least.TryGetValue(check, out var least) || value < least.Value) Least[check] = (value, where);
        }

        public void AtMost(string check, float value, string where)
        {
            if (!Most.TryGetValue(check, out var most) || value > most.Value) Most[check] = (value, where);
        }

        public void Print(string title)
        {
            TestContext.Out.WriteLine(title);
            foreach (var (check, (value, where)) in Least.OrderBy(m => m.Key)) TestContext.Out.WriteLine($"  least  {check}: {F(value)}   [{where}]");
            foreach (var (check, (value, where)) in Most.OrderBy(m => m.Key)) TestContext.Out.WriteLine($"  most   {check}: {F(value)}   [{where}]");
        }

        public void AssertHeld(params string[] alsoMeasured)
        {
            Assert.Multiple(() =>
            {
                Assert.That(Problems.Distinct().Take(15), Is.Empty);
                foreach (var (check, atLeast) in Floors)
                    if (Least.TryGetValue(check, out var least)) Assert.That(least.Value, Is.GreaterThanOrEqualTo(atLeast), $"{check} [{least.Where}]");
                foreach (var (check, atMost) in Ceilings)
                    if (Most.TryGetValue(check, out var most)) Assert.That(most.Value, Is.LessThanOrEqualTo(atMost), $"{check} [{most.Where}]");
                foreach (var check in alsoMeasured)
                    Assert.That(Least.ContainsKey(check) || Most.ContainsKey(check), $"{check} was measured");
            });
        }
    }

    private sealed record Cone(Vector2 From, Vector2 Along, PartyRole? Baiter);

    // How many degrees outside a wave `at` stands.
    private static float Outside(Vector2 at, Cone cone)
    {
        var toward = at - cone.From;
        if (toward.Length() < 0.001f) return -WaveHalfAngle;
        return MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(toward), cone.Along), -1f, 1f)) * 180f / MathF.PI - WaveHalfAngle;
    }

    private static Vector2 Looking(float rotation) => new(MathF.Sin(rotation), MathF.Cos(rotation));

    private static readonly PartyRole[] TanksAndMelee = [MainTank, OffTank, MeleeDpsA, MeleeDpsB];
    private static readonly PartyRole[] HealersAndRanged = [RegenHealer, ShieldHealer, PhysRangedDps, CasterDps];

    private static readonly (TeaP1Tornado Tornado, PartyRole Baiter)[] AimedAt =
        [(TeaP1Tornado.West, RegenHealer), (TeaP1Tornado.Middle, ShieldHealer), (TeaP1Tornado.East, PhysRangedDps)];
    private static readonly (TeaP1Tornado Tornado, PartyRole Baiter)[] UnseenAt =
        [(TeaP1Tornado.West, OffTank), (TeaP1Tornado.Middle, MeleeDpsA), (TeaP1Tornado.East, MeleeDpsB)];

    // One run's measuring, called every frame.
    private sealed class Measure(Roll roll, Margins margins, string where)
    {
        private readonly Watch watch = new();
        private readonly Dictionary<string, List<Cone>> cones = [];
        private readonly Dictionary<string, List<Vector2>> sluices = [];
        private readonly Dictionary<PartyRole, float> ranIn = [];

        public void See(ScenarioProbe p)
        {
            watch.See(p);
            var fight = Stage.Fight(p);
            if (roll.Plays(Protean1))
            {
                Protean(p, fight, 4.8f, 9.9f, 13.0f, P1Telegraph, P1Set1Pick, P1Set1Cones, P1Sluices, P1Set2Pick, P1Set2Cones);
                if (p.Crossed(roll.P1(4.85f)))
                {
                    margins.AtMost(FacingOffSouth, DegreesOff(fight, roll.Frame1.Facing(180f)), where);
                    margins.AtMost(LiquidOffAtProteanOne, Flat(fight.Position(TeaP1Boss.LivingLiquid)).Length(), where);
                }
                if (p.Time <= roll.P1(24.55f)) Puddles(p, fight, P1Puddles);
                if (p.Crossed(roll.P1(24.55f))) Drainage(p, fight);
            }
            if (!roll.Plays(Cascade2)) return;

            ProteanTwo(p, fight);
            if (p.Time >= roll.C2(5.9f)) Puddles(p, fight, C2Puddles);
            var liquid = Flat(fight.Position(TeaP1Boss.LivingLiquid));
            if (p.Crossed(roll.C2(5.9f))) margins.AtMost(LiquidOffAtTheNewTornadoes, liquid.Length(), where);
            if (p.Crossed(roll.C2(9.5f))) margins.AtMost(LiquidOffAtNinePointFive, liquid.Length(), where);
            if (p.Crossed(roll.C2(18.25f)))
            {
                margins.AtMost(FacingOffNorth, DegreesOff(fight, roll.Frame2.Facing(0f)), where);
                margins.AtMost(LiquidOffAtProteanTwo, liquid.Length(), where);
            }
            var apart = Vector2.Distance(liquid, Flat(fight.Position(TeaP1Boss.LiquidHand)));
            if (p.Crossed(roll.C2(33.2f))) margins.AtLeast(ApartAtTheGlow, apart - P1Rules.HandShowsAPalmBeyond, where);
            if (p.Crossed(roll.C2(38.4f))) margins.AtLeast(TogetherAtTheSlam, P1Rules.PalmNeedsThemWithin - apart, where);
        }

        private static float DegreesOff(TeaP1LivingLiquid fight, float facing) => MathF.Abs(Turn(fight.Liquid!.Rotation, facing)) * 180f / MathF.PI;

        private List<(PartyRole Seat, Vector2 At)> Living(ScenarioProbe p)
            => PerRole.All.Where(seat => p.Member(seat).IsAlive()).Select(seat => (seat, Flat(p.Member(seat)!.Position))).ToList();

        private bool Within(ScenarioProbe p, float from, float to) => p.Time >= from && p.Time <= to;

        private void ProteanTwo(ScenarioProbe p, TeaP1LivingLiquid fight)
        {
            Protean(p, fight, roll.C2(18.2f), roll.C2(23.3f), roll.C2(26.3f), P2Telegraph, P2Set1Pick, P2Set1Cones, P2Sluices, P2Set2Pick, P2Set2Cones);

            if (p.Crossed(roll.C2(24.2f))) cones[TornadoTelegraphs] = Tornadoes(p, AimedAt, TornadoAim);
            if (p.Crossed(roll.C2(24.2f + WaveDecidesAfter)) && cones.TryGetValue(TornadoTelegraphs, out var telegraphed))
                foreach (var (seat, at) in Living(p))
                foreach (var cone in telegraphed)
                    margins.AtLeast(TornadoTelegraphs, Outside(at, cone), $"{seat}, {where}");
            if (p.Crossed(roll.C2(29.3f)) && Tornadoes(p, UnseenAt, TornadoWavesPick) is var unseen)
                foreach (var (seat, at) in Living(p))
                foreach (var cone in unseen.Where(cone => cone.Baiter != seat))
                    margins.AtLeast(TornadoWavesCones, Outside(at, cone), $"{seat} in the wave at {cone.Baiter}, {where}");

            foreach (var seat in HealersAndRanged)
                if (Within(p, roll.C2(24.4f), roll.C2(28f)) && !ranIn.ContainsKey(seat) && p.Member(seat) is { MoveDestination: null })
                    ranIn[seat] = p.Time - roll.C2(0f);
            if (p.Crossed(roll.C2(28f)))
                margins.AtMost(RunInArrivesBy, HealersAndRanged.Max(seat => ranIn.GetValueOrDefault(seat, float.PositiveInfinity)), where);
        }

        // The telegraphed cast is aimed as it starts and decides later; each set is aimed and decided at once. A probe
        // reads a frame after the fight did.
        private void Protean(ScenarioProbe p, TeaP1LivingLiquid fight, float aim, float set1, float set2,
                             string telegraph, string set1Pick, string set1Cones, string sluiceCheck, string set2Pick, string set2Cones)
        {
            if (fight.Liquid is not { } boss) return;
            var liquid = Flat(fight.Position(TeaP1Boss.LivingLiquid));
            var ahead = new Cone(liquid, Looking(boss.Rotation), null);
            if (p.Crossed(aim))
                cones[telegraph] = [ahead, .. Living(p).Select(seat => new Cone(liquid, Vector2.Normalize(seat.At - liquid), seat.Seat))];
            if (p.Crossed(aim + WaveDecidesAfter) && cones.TryGetValue(telegraph, out var telegraphed))
                foreach (var (seat, at) in Living(p))
                foreach (var cone in telegraphed)
                    margins.AtLeast(telegraph, Outside(at, cone), $"{seat} in the wave at {cone.Baiter?.ToString() ?? "the Liquid's front"}, {where}");
            if (p.Crossed(set1)) sluices[sluiceCheck] = Set(p, liquid, ahead, TanksAndMelee, set1Pick, set1Cones);
            if (p.Crossed(set1 + WaveDecidesAfter) && sluices.TryGetValue(sluiceCheck, out var laid))
                foreach (var (seat, at) in Living(p))
                foreach (var sluice in laid)
                    margins.AtLeast(sluiceCheck, Vector2.Distance(at, sluice) - SluiceRadius, $"{seat}, {where}");
            if (p.Crossed(set2)) Set(p, liquid, ahead, HealersAndRanged, set2Pick, set2Cones);
        }

        // Returns where the four farthest stand, which is where a set's Sluices are laid.
        private List<Vector2> Set(ScenarioProbe p, Vector2 liquid, Cone ahead, PartyRole[] meant, string pick, string coneCheck)
        {
            var byDistance = Living(p).OrderBy(seat => Vector2.Distance(seat.At, liquid)).ToList();
            var closest = byDistance.Take(4).ToList();
            if (closest.Select(seat => seat.Seat).Except(meant).Any())
                margins.Problems.Add($"{pick}: the four closest are {string.Join(" ", closest.Select(seat => seat.Seat))} ({where})");
            if (byDistance.Count > 4)
                margins.AtLeast(pick, Vector2.Distance(byDistance[4].At, liquid) - Vector2.Distance(byDistance[3].At, liquid), $"{byDistance[4].Seat} against {byDistance[3].Seat}, {where}");
            List<Cone> fired = [ahead, .. closest.Select(seat => new Cone(liquid, Vector2.Normalize(seat.At - liquid), seat.Seat))];
            foreach (var (seat, at) in byDistance)
            foreach (var cone in fired.Where(cone => cone.Baiter != seat))
                margins.AtLeast(coneCheck, Outside(at, cone), $"{seat} in the wave at {cone.Baiter?.ToString() ?? "the Liquid's front"}, {where}");
            return byDistance.Skip(4).Select(seat => seat.At).ToList();
        }

        private List<Cone> Tornadoes(ScenarioProbe p, (TeaP1Tornado Tornado, PartyRole Baiter)[] meant, string pick)
        {
            var fired = new List<Cone>();
            foreach (var (tornado, baiter) in meant)
            {
                var centre = Flat(roll.Frame2.Centre(tornado));
                var byDistance = Living(p).OrderBy(seat => Vector2.Distance(seat.At, centre)).ToList();
                if (byDistance[0].Seat != baiter) margins.Problems.Add($"{pick}: the {tornado} tornado's closest is {byDistance[0].Seat} ({where})");
                margins.AtLeast(pick, Vector2.Distance(byDistance[1].At, centre) - Vector2.Distance(byDistance[0].At, centre), $"{tornado}: {byDistance[1].Seat} against {byDistance[0].Seat}, {where}");
                margins.AtLeast(BaiterToItsPuddle, Vector2.Distance(byDistance[0].At, centre) - P1Geometry.PuddleRadius, $"{byDistance[0].Seat}, {where}");
                fired.Add(new Cone(centre, Vector2.Normalize(byDistance[0].At - centre), byDistance[0].Seat));
            }
            return fired;
        }

        private void Puddles(ScenarioProbe p, TeaP1LivingLiquid fight, string check)
        {
            if (fight.Tornadoes.Standing is not { } standing) return;
            foreach (var tornado in Enum.GetValues<TeaP1Tornado>())
            foreach (var (seat, at) in Living(p))
                margins.AtLeast(check, Vector2.Distance(at, Flat(standing.Centre(tornado))) - P1Geometry.PuddleRadius, $"{seat} at {F(p.Time)}, {where}");
        }

        private void Drainage(ScenarioProbe p, TeaP1LivingLiquid fight)
        {
            var (mainTank, offTank) = (Flat(p.Member(MainTank)!.Position), Flat(p.Member(OffTank)!.Position));
            margins.AtLeast(TanksApart, Vector2.Distance(mainTank, offTank) - P1Geometry.DrainageRadius, where);
            foreach (var tank in Tanks)
            foreach (var seat in TheSix)
                margins.AtLeast(TankToTheSix, Vector2.Distance(Flat(p.Member(tank)!.Position), Flat(p.Member(seat)!.Position)) - P1Geometry.DrainageRadius, $"{tank} to {seat}, {where}");
            margins.AtMost(LiquidOffAtDrainage, Flat(fight.Position(TeaP1Boss.LivingLiquid)).Length(), where);
        }

        // Once the run has ended: who each wave hit, and how long after that seat's wave before.
        public void Waves()
        {
            var hits = watch.Sent("ability", P1ActionId.ProteanWaveFromTheLiquid, P1ActionId.ProteanWaveAtAPlayer, P1ActionId.ProteanWaveStraightAhead,
                                  P1ActionId.ProteanWaveAtTheClosest, P1ActionId.TornadoWave, P1ActionId.TornadoWaveUnseen)
                .SelectMany(wave => wave.Hits.Select(seat => (Seat: seat, wave.At, wave.ActionId))).ToList();
            foreach (var seat in PerRole.All)
            {
                var taken = hits.Where(hit => hit.Seat == seat).OrderBy(hit => hit.At).ToList();
                var meant = (roll.Plays(Protean1) ? 1 : 0) + (roll.Plays(Cascade2) ? seat is OffTank or MeleeDpsA or MeleeDpsB ? 2 : 1 : 0);
                if (taken.Count != meant)
                    margins.Problems.Add($"{seat} took {taken.Count} waves, not {meant}: {string.Join(", ", taken.Select(hit => $"{hit.ActionId} at {F(hit.At)}"))} ({where})");
                foreach (var (before, after) in taken.Zip(taken.Skip(1)))
                    margins.AtLeast(BetweenWaves, after.At - before.At - P1Duration.WaterResistanceDown, $"{seat}, {where}");
            }
        }
    }

    private static void Play(Roll roll, PartyRole player, Margins margins, Action<ScenarioProbe, string>? also = null, float? stopAt = null)
    {
        var seed = Random.Shared.Next();
        var where = $"{roll}, player {player}, seed {seed}";
        var measure = new Measure(roll, margins, where);
        var run = TeaP1LivingLiquidLpduAiTests.Play(roll, player, seed, p =>
        {
            measure.See(p);
            also?.Invoke(p, where);
        }, stopAt);
        if (!run.Passed) margins.Problems.Add($"{run} ({where})");
        else if (stopAt is null) measure.Waves();
    }

    [Test]
    public void KeepsItsMarginsOnEveryPairOfLayouts()
    {
        var margins = new Margins();
        var run = 0;
        foreach (var open1 in Enum.GetValues<TeaP1OpenQuarter>())
        foreach (var open2 in Enum.GetValues<TeaP1OpenQuarter>())
        for (var i = 0; i < Seeds; i++, run++)
            Play(new Roll { Open1 = open1, Open2 = open2 }, PerRole.All[(run + i) % 8], margins);

        margins.Print($"Every pair of layouts, {Seeds} runs each:");
        margins.AssertHeld(Floors.Select(floor => floor.Check).Concat(Ceilings.Select(ceiling => ceiling.Check)).ToArray());
    }

    [Test]
    public void KeepsItsMarginsInEachPartOnItsOwn()
    {
        var (proteanOne, cascadeTwo) = (new Margins(), new Margins());
        var run = 0;
        foreach (var open in Enum.GetValues<TeaP1OpenQuarter>())
        for (var i = 0; i < Seeds * 2; i++, run++)
        {
            Play(new Roll { Last = Protean1, Open1 = open }, PerRole.All[run % 8], proteanOne);
            Play(new Roll { First = Cascade2, Open1 = (TeaP1OpenQuarter)((run * 3) % 8), Open2 = open }, PerRole.All[(run + 4) % 8], cascadeTwo);
        }

        proteanOne.Print("Protean 1 alone, every layout:");
        cascadeTwo.Print("Cascade 2 alone, every layout:");
        proteanOne.AssertHeld(P1Telegraph, P1Set2Pick, TanksApart);
        cascadeTwo.AssertHeld(P2Telegraph, TornadoAim, TornadoWavesPick, TogetherAtTheSlam);
    }

    // Every start of the two tethers on every pair of tornadoes, played on into Cascade 2 until Protean 2 is aimed:
    // the main tank's fetch drags the Living Liquid, and its walk at Cascade 2's 6.4 has to bring it back.
    [Test]
    public void KeepsItsMarginsThroughEveryFetch()
    {
        var margins = new Margins();
        var run = 0;
        foreach (var tethering in TetheringPairs)
        foreach (var startsOn in Enum.GetValues<StartsOn>())
        foreach (var open1 in new[] { TeaP1OpenQuarter.Northeast, TeaP1OpenQuarter.West })
        for (var i = 0; i < Seeds * 2; i++, run++)
        {
            var roll = new Roll
            {
                Open1 = open1, Open2 = (TeaP1OpenQuarter)((run * 3) % 8),
                Drainage = Tethers(tethering, startsOn, TheSix[run % 6], TheSix[(run + 1 + i % 5) % 6]),
            };
            var late = TheOffTankFetchesLate(tethering, startsOn);
            float? onTheTanks = null;
            Play(roll, PerRole.All[(run + 3 * i) % 8], margins, (p, where) =>
            {
                if (p.Time < 20f || p.Time > 24.59f) return;
                var fight = Stage.Fight(p);
                var held = fight.DrainageTethersOut.All(tether => Seat(fight.Holder(tether.Tether)) is { } holder
                                                                 && (startsOn == StartsOn.TheTanksCrossed ? holder.IsTank() : holder.IsTank() && Own(holder, tethering) == tether.From));
                if (!held) onTheTanks = null;
                else onTheTanks ??= p.Time;
                if (!p.Crossed(24.55f)) return;
                margins.AtMost(TethersOnTheTanksBy, onTheTanks ?? float.PositiveInfinity, where);
                foreach (var tank in Tanks)
                    margins.AtMost(late && tank == OffTank ? LateOffTankOffItsSpot : TankOffItsSpot,
                                   Vector2.Distance(Flat(p.Member(tank)!.Position), DrainageSpot(roll, tank)), $"{tank}, {where}");
            }, stopAt: roll.C2(18.4f));
        }

        margins.Print($"Every start of the tethers, {Seeds * 4} runs each:");
        margins.AssertHeld(TanksApart, TankToTheSix, TethersOnTheTanksBy, TankOffItsSpot, LateOffTankOffItsSpot, LiquidOffAtNinePointFive, LiquidOffAtProteanTwo);
        Assert.Multiple(() =>
        {
            Assert.That(margins.Least[TanksApart].Value, Is.GreaterThanOrEqualTo(2.0f), TanksApart);
            Assert.That(margins.Least[TankToTheSix].Value, Is.GreaterThanOrEqualTo(3.0f), TankToTheSix);
            Assert.That(margins.Most[TethersOnTheTanksBy].Value, Is.LessThanOrEqualTo(24.5f), TethersOnTheTanksBy);
            Assert.That(margins.Most[TankOffItsSpot].Value, Is.LessThanOrEqualTo(0.5f), TankOffItsSpot);
            Assert.That(margins.Most[LateOffTankOffItsSpot].Value, Is.LessThanOrEqualTo(4.5f), LateOffTankOffItsSpot);
            Assert.That(margins.Most[LiquidOffAtDrainage].Value, Is.LessThanOrEqualTo(5.0f), LiquidOffAtDrainage);
        });
    }

    private static readonly (TeaP1Tornado From, TeaP1Boss AimsAt)[][] EmbolusRolls =
        (from pair in TetheringPairs
         from firstAtTheLiquid in new[] { true, false }
         select new[]
         {
             (pair.First, firstAtTheLiquid ? TeaP1Boss.LivingLiquid : TeaP1Boss.LiquidHand),
             (pair.Second, firstAtTheLiquid ? TeaP1Boss.LiquidHand : TeaP1Boss.LivingLiquid),
         }).ToArray();

    private const float EmbolusSpawnsAt = 33.3f + P1Duration.EmbolusAfterPressurize;
    private const float TheLiquidFallsAt = 39.7f;

    // Each Embolus drifts from its tornado toward where its boss stood at 33.3, so the speed nobody has measured
    // decides how far it gets before the Liquid falls at 39.7. From where everyone walks meanwhile, this works out the
    // lowest speed at which each orb would have come within its 1 y of a bot or a boss.
    [Test]
    public void TheEmboliReachNobodyUntilWellAboveTheirSpeed()
    {
        var margins = new Margins();
        var perRoll = new Dictionary<string, float>();
        var run = 0;
        foreach (var emboli in EmbolusRolls)
        foreach (var open2 in Enum.GetValues<TeaP1OpenQuarter>())
        for (var i = 0; i < Seeds; i++, run++)
        {
            var roll = new Roll { First = Cascade2, Open1 = (TeaP1OpenQuarter)(run % 8), Open2 = open2, Emboli = emboli.Select(e => new TeaP1EmbolusRoll(e.From, e.AimsAt)).ToList() };
            (Vector2 From, Vector2 To)[]? orbs = null;
            Play(roll, PerRole.All[(run + i) % 8], margins, (p, where) =>
            {
                var fight = Stage.Fight(p);
                if (orbs is null && fight.Emboli.Count == emboli.Length)
                    orbs = fight.Emboli.Select((orb, k) => (Flat(roll.Frame2.Centre(emboli[k].From)), Flat(orb.MoveDestination ?? orb.Position))).ToArray();
                if (orbs is null || p.Time <= EmbolusSpawnsAt + 0.05f || p.Time > TheLiquidFallsAt) return;
                var others = PerRole.All.Where(seat => p.Member(seat).IsAlive()).Select(seat => (Who: seat.ToString(), At: Flat(p.Member(seat)!.Position)))
                    .Append(("the Living Liquid", Flat(fight.Position(TeaP1Boss.LivingLiquid))))
                    .Append(("the Liquid Hand", Flat(fight.Position(TeaP1Boss.LiquidHand)))).ToList();
                for (var k = 0; k < orbs.Length; k++)
                {
                    var label = $"from the {emboli[k].From} tornado at the {emboli[k].AimsAt}'s spot";
                    foreach (var (who, at) in others)
                    {
                        margins.AtLeast(EmbolusClear, Vector2.Distance(Flat(fight.Emboli[k].Position), at) - P1Geometry.EmbolusContact, $"{who}, {label}, {where}");
                        if (SpeedToTouch(orbs[k].From, orbs[k].To, at, p.Time - EmbolusSpawnsAt) is not { } speed) continue;
                        margins.AtLeast(EmbolusSpeed, speed, $"{who} at {F(p.Time)}, {label}, {where}");
                        perRoll[label] = MathF.Min(perRoll.GetValueOrDefault(label, float.MaxValue), speed);
                    }
                }
            });
        }

        margins.Print($"The Emboli, {Seeds} runs on each layout for each pair of tornadoes and either aim:");
        TestContext.Out.WriteLine("The speed from which each would touch a bot or a boss (y/s):");
        foreach (var (label, speed) in perRoll.OrderBy(m => m.Value)) TestContext.Out.WriteLine($"  {label}: {F(speed)}");
        margins.AssertHeld(EmbolusClear, EmbolusSpeed);
        Assert.Multiple(() =>
        {
            Assert.That(margins.Least[EmbolusSpeed].Value, Is.GreaterThanOrEqualTo(2.0f), $"{EmbolusSpeed} [{margins.Least[EmbolusSpeed].Where}]");
            Assert.That(margins.Least[EmbolusSpeed].Value, Is.GreaterThan(P1Rules.EmbolusSpeed), "above the speed the scenario plays");
            Assert.That(margins.Least[EmbolusClear].Value, Is.GreaterThanOrEqualTo(3.0f), $"{EmbolusClear} [{margins.Least[EmbolusClear].Where}]");
        });
    }

    // The lowest speed at which an orb that left `from` for `to` `seconds` ago, and stops there, is within its contact
    // distance of `at`; null when it never is.
    private static float? SpeedToTouch(Vector2 from, Vector2 to, Vector2 at, float seconds)
    {
        var path = to - from;
        var length = path.Length();
        if (length < 0.001f || seconds <= 0f) return null;
        var along = Vector2.Dot(at - from, path / length);
        var across = MathF.Abs((at.X - from.X) * path.Y - (at.Y - from.Y) * path.X) / length;
        if (across > P1Geometry.EmbolusContact) return null;
        var half = MathF.Sqrt(P1Geometry.EmbolusContact * P1Geometry.EmbolusContact - across * across);
        if (along + half < 0f || along - half > length) return null;
        return MathF.Max(along - half, 0f) / seconds;
    }
}
