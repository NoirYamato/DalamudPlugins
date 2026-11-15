using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
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

// The pull's margins, measured as the other parts' are. Its floors and ceilings come from the strat's spots and the
// fight's rules, each worked out beside it and then taken with every bot anywhere in its jitter (0.3 y, or 0.15 y on a
// bait), each boss anywhere its rule lets it rest behind its tank (the Living Liquid 6.0 to 7.0 y, the Liquid Hand 4.1
// to 5.1), and less a frame's walk (0.15 y), which the debug-driven player's seat can stop short by.
//
// Two of them hang off where the Living Liquid comes to rest behind a main tank who has run round the middle tornado's
// water to the rim, which no spot fixes: it trails 6 y behind the tank and cuts the corner the tank walks round. Those
// two are held to what the play needs of them, not to where the Liquid was seen to park.
public partial class TeaP1LivingLiquidLpduMarginTests
{
    private const string SplitWater = "The pull, as the tornadoes appear: anyone to a puddle's edge (y)";
    private const string PullPuddles = "The pull: anyone to a puddle's edge once it kills (y)";
    private const string PullRim = "The pull: anyone inside the rim (y)";
    private const string FirstGlowApart = "The first hand: the bosses apart as it glows less the palm's line (y)";
    private const string ProvokesReach = "The first hand: Provoke's reach less the Hand from the main tank (y)";
    private const string LiquidBehindItsTank = "The first hand: the Liquid at rest from the main tank (y)";
    private const string LiquidFromTheMiddleTornado = "The first hand: the Liquid at rest from the middle tornado's centre less the 5.8 y inside which no doll can be drawn through it (y)";
    private const string LiquidFromADoll = "The first hand: the Liquid at rest from the nearest doll's spot less the 5.2 y the bots ask for to hit it (y)";
    private const string FirstSlamTogether = "The first hand: a palm's 5.1 y less the bosses apart as it slams (y)";
    private const string FirstTelegraphPick = "The pull, the tornadoes' aim: next closest less the baiter (y)";
    private const string FirstBaiterToItsPuddle = "The pull: a tornado's baiter to its puddle's edge as it is picked (y)";
    private const string FirstTelegraphCones = "The pull, the tornadoes' telegraphed waves: everyone but the main tank outside every cone (deg)";
    private const string FirstUnseenPick = "The pull, the tornadoes' unseen waves: next closest less the baiter (y)";
    private const string FirstUnseenCones = "The pull, the tornadoes' unseen waves: everyone outside every cone but its own (deg)";
    private const string FlippedWave = "The pull: a ranged outside the middle tornado's unseen wave, were it aimed at the melee beside that tornado (deg)";
    private const string CleaveOthers = "The pull, every cleave: the nearest other seat outside it (deg)";
    private const string ExhaustOne = "Exhaust 1: a seat's second-nearest doll past its reach (y)";
    private const string ExhaustTwo = "Exhaust 2: a seat's second-nearest doll past its reach (y)";
    private const string HitDollFromABoss = "A hit doll with a quarter of its health or more: from either boss less the 4.6 y that feeds it (y)";
    private const string FirstEmboliSeats = "The pull's Emboli: the nearest seat's hitbox less their 1 y (y)";
    private const string FirstEmboliBosses = "The pull's Emboli: the nearest boss's hitbox less their 1 y (y)";

    private const string LiquidAtRestBy = "The first hand: the Liquid at rest behind the main tank by (s)";
    private const string HandAtRestBy = "The first hand: the Hand at rest by the Liquid by (s)";
    private const string LastDollHitBy = "The Jagd Dolls: the last of the four hit by (s)";
    private const string MeleeDollsFedBy = "The Jagd Dolls: the melee's fed by (s)";
    private const string RangedDollsFedBy = "The Jagd Dolls: the ranged's fed by (s)";
    private const string MainTankAcrossTheMiddleBy = "The pull: the main tank across the middle by (s)";
    private const string OffItsProteanOneSpot = "The handover: a seat off its spot for Protean 1 (y)";
    private const string LiquidOffAtTheHandover = "The handover: the Liquid off the middle (y)";

    private static readonly (string Check, float AtLeast)[] ThePullsFloors =
    [
        // Everyone waits within 6 y of the middle, the main tank farthest, and a tornado's water reaches in to 7.4.
        (SplitWater, 1.0f),
        (PullPuddles, 0.1f),
        // The feeders keep within 19.3 y of the middle and the main tank baits 18.7 y out.
        (PullRim, 0.4f),
        // The Hand rests behind an off tank 11.5 y out, 24.9 y or more from the main tank's bait, and the Liquid within
        // its 7.0 y of that bait.
        (FirstGlowApart, 1.8f),
        // Provoke reaches 25 y between hitboxes, 27.1 between a tank and the Hand.
        (ProvokesReach, 0.7f),
        // Its rule lets it rest up to 7.0 y behind the tank, but it is still walking after the tank as the tank arrives,
        // so it stops on its 6.0 y. If that ever fails, the two floors after this one are the ones to read.
        (LiquidBehindItsTank, 5.9f),
        (LiquidFromTheMiddleTornado, 0.7f),
        (LiquidFromADoll, 0.1f),
        // The Hand walks straight to 4.1 y from the main tank, down a line that leaves the tank 30 degrees east of
        // north; the Liquid rests within its 7.0 y of the tank, between north and that line: 4.0 y from the Hand at the
        // most.
        (FirstSlamTogether, 1.0f),
        // M2 waits 10.3 y from the middle tornado, which the main tank baits from 8.4.
        (FirstTelegraphPick, 1.3f),
        // Each bait is 8.4 y from its tornado, whose water reaches 7.6.
        (FirstBaiterToItsPuddle, 0.4f),
        // A ranged steps 32.6 degrees off the line from its tornado to its bait, 17.6 outside the cone.
        (FirstTelegraphCones, 13.5f),
        // A melee half a yalm off its doll, away from the tornado beside it, is 9.44 y from that tornado.
        (FirstUnseenPick, 0.6f),
        // The off tank on the Liquid's flank is 44.3 degrees off the middle tornado's line to the main tank, 29.3
        // outside the cone; its jitter turns that by 1.5 degrees, and the tank's and a frame's walk by 1 each.
        (FirstUnseenCones, 25.5f),
        // The melee beside the middle tornado is 17.8 degrees off that tornado's line to the nearer ranged's bait.
        (FlippedWave, 0.6f),
        // Seen from a boss within a yalm of the middle, the two tanks' holds are 65 degrees apart or more; at the first
        // busters the off tank is 90 degrees and more off both. Held at 60.
        (CleaveOthers, 15.0f),
        // The dolls' spots are 9.9 y apart, and a melee half a yalm off its own is 9.4 y from the nearer of the next
        // two.
        (ExhaustOne, 0.3f),
        // Each ranged's doll rests 3.3 y short of its tornado's bait, 13 y east or west of the middle.
        (ExhaustTwo, 3.2f),
        // The bots hit a doll only with both bosses 5.2 y from it, 0.6 y past the line, and neither boss walks until
        // the melee's are fed.
        (HitDollFromABoss, 0.55f),
        // The orb from the east tornado passes the main tank's hold, 3.9 y south-east of the Liquid, 2.8 y off at the
        // least, and the Liquid stands up to half a yalm off the middle.
        (FirstEmboliSeats, 0.7f),
        // It passes 6.5 y and more from the middle, where the Liquid then stands.
        (FirstEmboliBosses, 1.5f),
    ];

    private static readonly (string Check, float AtMost)[] ThePullsCeilings =
    [
        (LiquidBehindItsTank, 6.2f),
        // The tank sprints 26 y at the most, round the middle tornado's water; the Hand glows at 29.3.
        (LiquidAtRestBy, 28.0f),
        // Provoked at 30.0, it sets off at 30.67 and walks 22 y.
        (HandAtRestBy, 33.3f),
        // M2 walks 10 y to its doll from 33.5 and reaches it from 3.5 y.
        (LastDollHitBy, 35.1f),
        // Under a quarter 4.6 s after its hit, and the bosses leave the rim at 42.5.
        (MeleeDollsFedBy, 42.4f),
        // From 49.3, a 13.5 y walk to the middle.
        (RangedDollsFedBy, 52.0f),
        // 25 y from its bait, round the middle tornado's water.
        (MainTankAcrossTheMiddleBy, 46.6f),
        (OffItsProteanOneSpot, 0.5f),
        (LiquidOffAtTheHandover, 0.5f),
    ];

    private static readonly string[] ThePullsChecks = ThePullsFloors.Select(floor => floor.Check).Concat(ThePullsCeilings.Select(ceiling => ceiling.Check)).Distinct().ToArray();

    private const float ProvokeBetweenHitboxes = 25f;
    private const float NoDollDrawnThroughWithin = 5.8f;
    private const float TheBotsHitADollBeyond = 5.2f;
    private const float CleaveHalfAngle = 45f;
    private const float CleaveReachesAtMost = 12.6f;

    private static readonly (TeaP1Tornado Tornado, PartyRole Baiter)[] BaitedAtThePull =
        [(TeaP1Tornado.West, CasterDps), (TeaP1Tornado.Middle, MainTank), (TeaP1Tornado.East, PhysRangedDps)];

    private static readonly (float At, TeaP1Boss Boss)[] Cleaves =
        [(36.4f, TeaP1Boss.LivingLiquid), (36.5f, TeaP1Boss.LiquidHand), (55.55f, TeaP1Boss.LivingLiquid), (55.8f, TeaP1Boss.LiquidHand)];

    private static readonly Dictionary<PartyRole, Vector2> WhereProteanOneStarts = new()
    {
        [OffTank] = new(-3.0f, 3.0f), [RegenHealer] = new(-1.1f, -4.2f), [ShieldHealer] = new(1.1f, -5.2f), [MeleeDpsA] = new(-0.3f, -5.6f),
        [MeleeDpsB] = new(0.4f, -4.3f), [PhysRangedDps] = new(11.0f, -7.8f), [CasterDps] = new(-10.3f, -8.1f),
    };

    private sealed partial class Measure
    {
        private readonly HashSet<TeaP1JagdDoll> fed = [];
        private float? mainTankOnItsBaitAt, liquidAtRestAt, handAtRestAt, mainTankAcrossAt;
        private bool mainTankHeldStill;

        private void ThePull(ScenarioProbe p, TeaP1LivingLiquid fight)
        {
            if (p.Time > roll.P1(0f) + 0.05f) return;
            var frame = roll.Frame1;
            var (liquid, hand) = (Flat(fight.Position(TeaP1Boss.LivingLiquid)), Flat(fight.Position(TeaP1Boss.LiquidHand)));
            var mainTank = p.Member(MainTank)!;
            var apart = Vector2.Distance(liquid, hand);

            if (p.Crossed(19.65f))
                foreach (var tornado in Enum.GetValues<TeaP1Tornado>())
                foreach (var (seat, at) in Living(p))
                    margins.AtLeast(SplitWater, Vector2.Distance(at, Flat(frame.Centre(tornado))) - P1Geometry.PuddleRadius, $"{seat}, {where}");
            if (p.Time >= 19.6f + P1Duration.PuddleKillsAfter && p.Time < roll.P1(0f))
            {
                Puddles(p, fight, PullPuddles);
                foreach (var (seat, at) in Living(p)) margins.AtLeast(PullRim, P1Geometry.ArenaRadius - at.Length(), $"{seat} at {F(p.Time)}, {where}");
            }

            TheFirstHand(p, fight, liquid, hand, apart);
            TheTornadoesWaves(p, frame);
            foreach (var (at, boss) in Cleaves)
                if (p.Crossed(at)) ACleave(p, fight, boss, boss == TeaP1Boss.LivingLiquid ? liquid : hand);
            TheDolls(p, fight, liquid, hand);
            TheFirstEmboli(p, fight, liquid, hand);

            if (p.Time > 42.6f && mainTankAcrossAt is null && mainTank.MoveDestination is null) mainTankAcrossAt = p.Time;
            if (mainTankAcrossAt is not null && p.Time <= 49.1f)
            {
                var sentOn = mainTankHeldStill && mainTank.MoveDestination is not null;
                if (sentOn && fight.Liquid is { MoveDestination: not null })
                    margins.Problems.Add($"the main tank left for its hold at {F(p.Time)} with the Liquid still walking ({where})");
                mainTankHeldStill = mainTank.MoveDestination is null;
            }
            if (p.Crossed(49.1f)) margins.AtMost(MainTankAcrossTheMiddleBy, mainTankAcrossAt ?? float.PositiveInfinity, where);

            if (!p.Crossed(roll.P1(0f))) return;
            margins.AtMost(LiquidOffAtTheHandover, liquid.Length(), where);
            foreach (var (seat, at) in Living(p))
            {
                var spot = seat == MainTank ? Stage.Off(frame.ToRelative(liquid), 3.9f, 135f) : WhereProteanOneStarts[seat];
                margins.AtMost(OffItsProteanOneSpot, Vector2.Distance(at, frame.ToArena(spot)), $"{seat}, {where}");
            }
            if (fight.Dolls.All.Any(doll => doll.IsUp)) margins.Problems.Add($"a doll is still up as the pull ends ({where})");
        }

        private void TheFirstHand(ScenarioProbe p, TeaP1LivingLiquid fight, Vector2 liquid, Vector2 hand, float apart)
        {
            var mainTank = p.Member(MainTank)!;
            if (p.Time > 24.1f && mainTankOnItsBaitAt is null && mainTank.MoveDestination is null) mainTankOnItsBaitAt = p.Time;
            if (mainTankOnItsBaitAt is not null && liquidAtRestAt is null && fight.Liquid is { MoveDestination: null }) liquidAtRestAt = p.Time;
            if (p.Time > 30.7f && handAtRestAt is null && fight.Hand is { MoveDestination: null } && ReferenceEquals(fight.Target(TeaP1Boss.LiquidHand), mainTank))
                handAtRestAt = p.Time;

            if (p.Crossed(29.35f))
            {
                if (fight.HandShowsAPalm != true) margins.Problems.Add($"the Hand shows {(fight.HandShowsAPalm == false ? "a fist" : "nothing")} at its first glow ({where})");
                var fromItsTank = Vector2.Distance(liquid, Flat(mainTank.Position));
                margins.AtMost(LiquidAtRestBy, liquidAtRestAt ?? float.PositiveInfinity, where);
                margins.AtLeast(LiquidBehindItsTank, fromItsTank, where);
                margins.AtMost(LiquidBehindItsTank, fromItsTank, where);
                margins.AtLeast(LiquidFromTheMiddleTornado, Vector2.Distance(liquid, Flat(roll.Frame1.Centre(TeaP1Tornado.Middle))) - NoDollDrawnThroughWithin, where);
                margins.AtLeast(LiquidFromADoll, DollSpots().Min(spot => Vector2.Distance(spot, liquid)) - TheBotsHitADollBeyond, where);
                margins.AtLeast(FirstGlowApart, apart - P1Rules.HandShowsAPalmBeyond, where);
            }
            if (p.Crossed(30.0f))
                margins.AtLeast(ProvokesReach, ProvokeBetweenHitboxes + fight.Hand!.HitboxRadius + mainTank.HitboxRadius - Vector2.Distance(hand, Flat(mainTank.Position)), where);
            if (!p.Crossed(34.4f)) return;
            margins.AtMost(HandAtRestBy, handAtRestAt ?? float.PositiveInfinity, where);
            margins.AtLeast(FirstSlamTogether, P1Rules.PalmNeedsThemWithin - apart, where);
        }

        private static IEnumerable<Vector2> DollSpots()
            => P1Geometry.JagdDollBearings.Select(bearing => Stage.Off(Vector2.Zero, P1Geometry.JagdDollRing, bearing));

        private void TheTornadoesWaves(ScenarioProbe p, TeaP1TornadoLayout frame)
        {
            if (p.Crossed(32.4f)) cones[FirstTelegraphCones] = Tornadoes(p, frame, BaitedAtThePull, FirstTelegraphPick, FirstBaiterToItsPuddle);
            if (p.Crossed(32.4f + WaveDecidesAfter) && cones.TryGetValue(FirstTelegraphCones, out var telegraphed))
                foreach (var (seat, at) in Living(p))
                foreach (var cone in telegraphed)
                {
                    if (seat != MainTank) margins.AtLeast(FirstTelegraphCones, Outside(at, cone), $"{seat} in the wave at {cone.Baiter}, {where}");
                    else if (cone.Baiter == MainTank && Outside(at, cone) > 0f) margins.Problems.Add($"the main tank has left its tornado's telegraph ({where})");
                }
            if (!p.Crossed(37.5f)) return;

            var middle = Flat(frame.Centre(TeaP1Tornado.Middle));
            var unseen = Tornadoes(p, frame, BaitedAtThePull, FirstUnseenPick, FirstBaiterToItsPuddle);
            foreach (var (seat, at) in Living(p))
            foreach (var cone in unseen.Where(cone => cone.Baiter != seat))
                margins.AtLeast(FirstUnseenCones, Outside(at, cone), $"{seat} in the wave at {cone.Baiter}, {where}");
            var besideIt = new[] { MeleeDpsA, MeleeDpsB }.Select(seat => Flat(p.Member(seat)!.Position)).MinBy(at => Vector2.Distance(at, middle));
            var flipped = new Cone(middle, Vector2.Normalize(besideIt - middle), null);
            foreach (var ranged in new[] { PhysRangedDps, CasterDps })
                margins.AtLeast(FlippedWave, Outside(Flat(p.Member(ranged)!.Position), flipped), $"{ranged}, {where}");
        }

        private void ACleave(ScenarioProbe p, TeaP1LivingLiquid fight, TeaP1Boss boss, Vector2 from)
        {
            if (fight.Target(boss) is not { } target) return;
            var along = Vector2.Normalize(Flat(target.Position) - from);
            foreach (var (seat, at) in Living(p))
            {
                if (ReferenceEquals(p.Member(seat), target) || Vector2.Distance(at, from) > CleaveReachesAtMost) continue;
                var off = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(at - from), along), -1f, 1f)) * 180f / MathF.PI;
                margins.AtLeast(CleaveOthers, off - CleaveHalfAngle, $"{seat} at {F(p.Time)}, {where}");
            }
        }

        private void TheDolls(ScenarioProbe p, TeaP1LivingLiquid fight, Vector2 liquid, Vector2 hand)
        {
            foreach (var (at, check, up) in new[] { (38.5f, ExhaustOne, 4), (49.14f, ExhaustTwo, 2) })
            {
                if (!p.Crossed(at)) continue;
                var exhausting = fight.Dolls.All.Where(doll => doll.IsUp).ToList();
                if (exhausting.Count != up) margins.Problems.Add($"{exhausting.Count} dolls are up at {F(at)}, not {up} ({where})");
                foreach (var (seat, stands) in Living(p))
                {
                    var past = exhausting.Select(doll => Vector2.Distance(Flat(doll.Enemy.Position), stands) - ExhaustReach(doll)).OrderBy(left => left).ToList();
                    if (past.Count > 1) margins.AtLeast(check, past[1], $"{seat}, {where}");
                }
            }

            foreach (var doll in fight.Dolls.All)
            {
                var quarter = doll.QuarterIn(roll.Frame1);
                var melees = quarter is TeaP1Quarter.Southwest or TeaP1Quarter.Southeast;
                if (doll.IsUp && doll.HasBeenHit && doll.Health(p.Time) >= P1Rules.DollIsReadyUnder)
                {
                    var at = Flat(doll.Enemy.Position);
                    margins.AtLeast(HitDollFromABoss, MathF.Min(Vector2.Distance(at, liquid), Vector2.Distance(at, hand)) - P1Geometry.DollIsFedWithin, $"{quarter} at {F(p.Time)}, {where}");
                }
                if (doll.FedTo is null || !fed.Add(doll)) continue;
                if (doll.Health(p.Time) >= P1Rules.DollIsReadyUnder) margins.Problems.Add($"the {quarter} doll was fed at {F(p.Time)} with {F(doll.Health(p.Time))} of its health ({where})");
                if (Stage.DollOf.Single(its => its.Value == quarter).Key != Seat(doll.Holder)) margins.Problems.Add($"the {quarter} doll went to {Seat(doll.Holder)} ({where})");
                margins.AtMost(melees ? MeleeDollsFedBy : RangedDollsFedBy, p.Time, $"{quarter}, {where}");
            }
            if (p.Crossed(38.4f))
                margins.AtMost(LastDollHitBy, fight.Dolls.All.Max(doll => doll.HitAt ?? float.PositiveInfinity), where);
        }

        private static float ExhaustReach(TeaP1JagdDoll doll) => (Natives.Data.Action(P1ActionId.Exhaust)?.EffectRange ?? 0) + doll.Enemy.HitboxRadius;

        private void TheFirstEmboli(ScenarioProbe p, TeaP1LivingLiquid fight, Vector2 liquid, Vector2 hand)
        {
            if (p.Time <= 40.4f + P1Duration.EmbolusAfterPressurize + 0.05f) return;
            foreach (var orb in fight.Emboli.Where(orb => orb.IsActive))
            {
                var at = Flat(orb.Position);
                if (at.Length() > P1Geometry.EmbolusLeavesBeyond - 0.1f) continue;
                foreach (var (seat, stands) in Living(p))
                    margins.AtLeast(FirstEmboliSeats, Vector2.Distance(at, stands) - PlayerHitbox - P1Geometry.EmbolusRadius, $"{seat} at {F(p.Time)}, {where}");
                margins.AtLeast(FirstEmboliBosses, Vector2.Distance(at, liquid) - (fight.Liquid?.HitboxRadius ?? 0f) - P1Geometry.EmbolusRadius, $"the Living Liquid at {F(p.Time)}, {where}");
                margins.AtLeast(FirstEmboliBosses, Vector2.Distance(at, hand) - (fight.Hand?.HitboxRadius ?? 0f) - P1Geometry.EmbolusRadius, $"the Liquid Hand at {F(p.Time)}, {where}");
            }
        }

        // The main tank takes its tornado's two waves under its invulnerability, each ranged its tornado's unseen one,
        // and nobody else any; each cleave finds its target alone.
        private void ThePullsHits(IReadOnlyList<PartyRole> waves)
        {
            foreach (var seat in PerRole.All)
            {
                var meant = seat switch { MainTank => 2, PhysRangedDps or CasterDps => 1, _ => 0 };
                if (waves.Count(hit => hit == seat) != meant) margins.Problems.Add($"{seat} took {waves.Count(hit => hit == seat)} waves in the pull, not {meant} ({where})");
            }
            var cleaves = watch.Sent("ability", P1ActionId.FluidSwing, P1ActionId.FluidStrike).Where(cleave => cleave.At < roll.P1(0f)).ToList();
            var alone = cleaves.Select(cleave => cleave.Hits.Count == 1 ? cleave.Hits[0] : (PartyRole?)null).ToList();
            if (!alone.SequenceEqual(new PartyRole?[] { MainTank, MainTank, MainTank, MainTank, OffTank }))
                margins.Problems.Add($"the cleaves hit {string.Join("; ", cleaves.Select(cleave => $"{F(cleave.At)}: {string.Join(" ", cleave.Hits)}"))} ({where})");
        }
    }

    [Test]
    public void KeepsItsMarginsThroughThePullOnEveryLayout()
    {
        var margins = new Margins();
        var run = 0;
        foreach (var open in Enum.GetValues<TeaP1OpenQuarter>())
        for (var i = 0; i < Seeds * 4; i++, run++)
            Play(new Roll { First = Pull, Last = Pull, Open1 = open }, PerRole.All[(run + i) % 8], margins);

        margins.Print($"The pull alone, {Seeds * 4} runs on each layout:");
        margins.AssertHeld(ThePullsChecks);
    }

    // The released parts' floors hold from where the pull leaves everyone, on every layout of the first tornadoes.
    [Test]
    public void KeepsItsMarginsFromThePullToTheKill()
    {
        var margins = new Margins();
        var run = 0;
        foreach (var open1 in Enum.GetValues<TeaP1OpenQuarter>())
        for (var i = 0; i < Seeds; i++, run++)
            Play(new Roll { First = Pull, Open1 = open1, Open2 = (TeaP1OpenQuarter)((run * 3 + 1) % 8) }, PerRole.All[(run * 3 + i) % 8], margins);

        margins.Print($"From the pull to the kill, {Seeds} runs on each layout of the first tornadoes:");
        margins.AssertHeld(ThePullsChecks.Concat(Floors.Select(floor => floor.Check)).Concat(Ceilings.Select(ceiling => ceiling.Check)).ToArray());
    }

    // Each pair of tornadoes at either boss, on every layout: the orbs set off at 45.95 for where the bosses stood at
    // the rim, with the bosses on their way to the middle since 42.5.
    [Test]
    public void TheFirstEmboliPassClearOfEveryoneOnEveryRoll()
    {
        var margins = new Margins();
        var run = 0;
        foreach (var emboli in EmbolusRolls)
        foreach (var open in Enum.GetValues<TeaP1OpenQuarter>())
        for (var i = 0; i < Seeds; i++, run++)
        {
            var roll = new Roll { First = Pull, Last = Pull, Open1 = open, FirstEmboli = emboli.Select(e => new TeaP1EmbolusRoll(e.From, e.AimsAt)).ToList() };
            Play(roll, PerRole.All[(run + i) % 8], margins);
        }

        margins.Print($"The pull's Emboli, {Seeds} runs on each layout for each pair of tornadoes and either aim:");
        margins.AssertHeld(FirstEmboliSeats, FirstEmboliBosses);
    }
}
