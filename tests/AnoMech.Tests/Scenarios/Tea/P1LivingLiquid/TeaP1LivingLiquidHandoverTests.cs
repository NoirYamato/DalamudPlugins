using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaP1LivingLiquidLpduAiTests;
using static AnoMech.Tests.TeaP1LivingLiquidScenarioTests;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// A run from the pull hands over to Protean 1 at 60.9 as a run that starts there begins: the same rolls for the same
// seed, everyone on the spots that start teleports them to, both bosses in the middle on their tanks, the first
// tornadoes standing, and nothing left of the pull but, on some rolls, one Embolus on its way out past the rim. From
// there on the two runs play the same: every cast of the fight at the same time on the same seats, to the Living
// Liquid's fall.
//
// The bots stand within their jitter of a spot they walked to, where a start puts them on it, and the bosses stand
// where their tanks led them: the Living Liquid within half a yalm of the middle, the Liquid Hand within a yalm of
// it, where a start puts it 2.1 y out.
public class TeaP1LivingLiquidHandoverTests
{
    private const float ReadAt = 0.6f;
    private const float SeatsWithin = 1.0f;
    private const float LiquidWithin = 1.0f;
    private const float LiquidFacesWithinDegrees = 20f;
    private const float HandWithin = 3.0f;
    private const float AnOrbLeftIsBeyond = 16f;
    private const float TheOrbIsGoneBy = 3.0f;
    private const float TheLiquidFallsAfter = Stage.CascadeTwo + 39.7f;

    private static readonly ushort[] LeftByThePull =
        [P1StatusId.WaterResistanceDown, P1StatusId.LuminousAetheroplasm, .. P1StatusId.Invulnerabilities];

    private static readonly uint[] TheFightsCasts =
    [
        P1ActionId.ProteanWaveFromTheLiquid, P1ActionId.ProteanWaveStraightAhead, P1ActionId.ProteanWaveAtAPlayer, P1ActionId.ProteanWaveAtTheClosest,
        P1ActionId.TornadoWave, P1ActionId.TornadoWaveUnseen, P1ActionId.Sluice, P1ActionId.Splash, P1ActionId.Drainage, P1ActionId.Cascade,
        P1ActionId.HandOfPain, P1ActionId.HandOfParting, P1ActionId.HandOfPrayer, P1ActionId.Outburst, P1ActionId.FluidSwing, P1ActionId.FluidStrike,
        P1ActionId.Exhaust, P1ActionId.ExhaustFromAPlayer, P1ActionId.ReducibleComplexity,
    ];

    private sealed record Rolled(TeaP1OpenQuarter Tornadoes1, TeaP1OpenQuarter Tornadoes2, string Drainage, string Throttled, string Emboli)
    {
        public static Rolled Of(TeaP1LivingLiquidState state)
            => new(state.Tornadoes1.Open, state.Tornadoes2.Open,
                string.Join(", ", state.Drainage.Select(roll => $"{roll.From} on {roll.FirstTarget}")),
                string.Join(" ", state.Throttled.List),
                string.Join(", ", state.Emboli.Select(roll => $"{roll.From} to the {roll.AimsAt}")));
    }

    // What stands as Protean 1 begins, ReadAt into it.
    private sealed record Standing(
        Dictionary<PartyRole, Vector2> Seats, Vector2 Liquid, float LiquidFaces, Vector2 Hand, PartyRole? LiquidIsOn, PartyRole? HandIsOn,
        bool FacingLocked, bool? Palm, TeaP1OpenQuarter? Tornadoes, int Dolls, List<string> Statuses, List<(TeaP1Tornado? From, float FromTheMiddle)> Orbs);

    private sealed class Played
    {
        public required ScenarioRun Run { get; init; }
        public Rolled? Rolled { get; set; }
        public Standing? AsProteanOneBegins { get; set; }
        public int? OrbsLater { get; set; }
        public float? TheLiquidFell { get; set; }
        public List<(uint ActionId, string Hits, float At)> Casts { get; set; } = [];
    }

    private static List<(TeaP1Tornado? From, float FromTheMiddle)> OrbsInTheArena(TeaP1LivingLiquid fight, TeaP1LivingLiquidState state)
        => fight.Emboli
            .Select((orb, k) => (Orb: orb, From: k < state.FirstEmboli.Count && state.Parts.Plays(Pull) ? state.FirstEmboli[k].From : (TeaP1Tornado?)null))
            .Where(orb => orb.Orb.IsActive && Flat(orb.Orb.Position).Length() < P1Geometry.EmbolusLeavesBeyond - 0.1f)
            .Select(orb => (orb.From, Flat(orb.Orb.Position).Length()))
            .ToList();

    private static Played Play(Roll roll, PartyRole player, int seed)
    {
        var start = roll.P1(0f);
        var watch = new Watch();
        Standing? standing = null;
        Rolled? rolled = null;
        int? orbsLater = null;
        float? fell = null;
        var run = TeaP1LivingLiquidLpduAiTests.Play(roll, player, seed, p =>
        {
            watch.See(p);
            var (fight, state) = (Stage.Fight(p), Stage.Scenario(p).State!);
            rolled ??= Rolled.Of(state);
            if (p.Crossed(start + ReadAt))
                standing = new Standing(
                    PerRole.All.ToDictionary(seat => seat, seat => Flat(p.Member(seat)!.Position)),
                    Flat(fight.Position(TeaP1Boss.LivingLiquid)), fight.Liquid!.Rotation, Flat(fight.Position(TeaP1Boss.LiquidHand)),
                    Seat(fight.Target(TeaP1Boss.LivingLiquid)), Seat(fight.Target(TeaP1Boss.LiquidHand)),
                    fight.FacingLocked, fight.HandShowsAPalm, fight.Tornadoes.Standing?.Open, fight.Dolls.All.Count(doll => doll.Enemy.IsActive),
                    PerRole.All.SelectMany(seat => LeftByThePull.Where(p.Member(seat)!.HasStatus).Select(status => $"{seat} {status}")).ToList(),
                    OrbsInTheArena(fight, state));
            if (p.Crossed(start + TheOrbIsGoneBy)) orbsLater = OrbsInTheArena(fight, state).Count;
            if (fell is null && p.Time > start && fight.Liquid is { } liquid && !liquid.IsAlive()) fell = p.Time - start;
        });
        return new Played
        {
            Run = run, Rolled = rolled, AsProteanOneBegins = standing, OrbsLater = orbsLater, TheLiquidFell = fell,
            Casts = run.Failure is null
                ? watch.Sent("ability", TheFightsCasts).Where(cast => cast.At >= start + ReadAt)
                    .Select(cast => (cast.ActionId, string.Join(" ", cast.Hits.Order()), cast.At - start))
                    .OrderBy(cast => cast.ActionId).ThenBy(cast => cast.Item2).ThenBy(cast => cast.Item3).ToList()
                : [],
        };
    }

    [Test]
    public void ARunFromThePullHandsOverWhatAStartAtProteanOneBeginsWithAndPlaysOnAsItDoes([Values] TeaP1OpenQuarter open)
    {
        for (var i = 0; i < 3; i++)
        {
            var seed = Random.Shared.Next();
            var player = PerRole.All[((int)open * 3 + i * 5) % 8];
            var open2 = (TeaP1OpenQuarter)(((int)open + 3 + i) % 8);
            var context = $"tornadoes open {open} then {open2}, player {player}, seed {seed}";
            var fromThePull = Play(new Roll { First = Pull, Open1 = open, Open2 = open2 }, player, seed);
            var fromProteanOne = Play(new Roll { First = Protean1, Open1 = open, Open2 = open2 }, player, seed);

            Assert.That(fromThePull.Run.Passed, () => $"{fromThePull.Run}{Environment.NewLine}{context}");
            Assert.That(fromProteanOne.Run.Passed, () => $"{fromProteanOne.Run}{Environment.NewLine}{context}");
            var (handed, begun) = (fromThePull.AsProteanOneBegins!, fromProteanOne.AsProteanOneBegins!);
            Assert.Multiple(() =>
            {
                Assert.That(fromThePull.Rolled, Is.EqualTo(fromProteanOne.Rolled), $"the rolls ({context})");

                foreach (var seat in PerRole.All)
                    Assert.That(Vector2.Distance(handed.Seats[seat], begun.Seats[seat]), Is.LessThan(SeatsWithin), $"{seat} at {handed.Seats[seat]}, begun at {begun.Seats[seat]} ({context})");
                Assert.That(Vector2.Distance(handed.Liquid, begun.Liquid), Is.LessThan(LiquidWithin), $"the Living Liquid at {handed.Liquid} ({context})");
                Assert.That(MathF.Abs(Turn(handed.LiquidFaces, begun.LiquidFaces)) * 180f / MathF.PI, Is.LessThan(LiquidFacesWithinDegrees), $"the Living Liquid's facing ({context})");
                Assert.That(Vector2.Distance(handed.Hand, begun.Hand), Is.LessThan(HandWithin), $"the Liquid Hand at {handed.Hand}, begun at {begun.Hand} ({context})");
                Assert.That((handed.LiquidIsOn, handed.HandIsOn), Is.EqualTo(((PartyRole?)MainTank, (PartyRole?)OffTank)), $"the bosses' targets ({context})");
                Assert.That((begun.LiquidIsOn, begun.HandIsOn), Is.EqualTo((handed.LiquidIsOn, handed.HandIsOn)), $"the bosses' targets ({context})");
                Assert.That((handed.FacingLocked, handed.Palm, handed.Tornadoes), Is.EqualTo((false, (bool?)null, (TeaP1OpenQuarter?)open)), context);
                Assert.That((begun.FacingLocked, begun.Palm, begun.Tornadoes), Is.EqualTo((handed.FacingLocked, handed.Palm, handed.Tornadoes)), context);
                Assert.That(handed.Statuses.Concat(begun.Statuses), Is.Empty, $"what the pull's waves, busters and Exhausts leave ({context})");
                Assert.That((handed.Dolls, begun.Dolls), Is.EqualTo((0, 0)), $"Jagd Dolls ({context})");

                Assert.That(begun.Orbs, Is.Empty, context);
                Assert.That(handed.Orbs, Has.Count.LessThanOrEqualTo(1), $"Emboli still in the arena ({context})");
                foreach (var (from, fromTheMiddle) in handed.Orbs)
                {
                    Assert.That(from, Is.EqualTo(TeaP1Tornado.East), $"the one Embolus left ({context})");
                    Assert.That(fromTheMiddle, Is.GreaterThan(AnOrbLeftIsBeyond), $"the one Embolus left ({context})");
                }
                Assert.That((fromThePull.OrbsLater, fromProteanOne.OrbsLater), Is.EqualTo(((int?)0, (int?)0)), $"Emboli {F(TheOrbIsGoneBy)} s into Protean 1 ({context})");

                Assert.That(fromProteanOne.TheLiquidFell, Is.EqualTo(TheLiquidFallsAfter).Within(0.03f), $"the Living Liquid's fall ({context})");
                Assert.That(fromThePull.TheLiquidFell, Is.EqualTo(TheLiquidFallsAfter).Within(0.03f), $"the Living Liquid's fall after the pull ({context})");
                Assert.That(fromThePull.Casts.Select(cast => (cast.ActionId, cast.Hits)), Is.EqualTo(fromProteanOne.Casts.Select(cast => (cast.ActionId, cast.Hits))), $"what each cast hit ({context})");
                Assert.That(fromThePull.Casts.Select(cast => cast.At), Is.EqualTo(fromProteanOne.Casts.Select(cast => cast.At)).Within(0.03f), $"when each cast went off ({context})");
                Assert.That(fromProteanOne.Casts, Has.Count.GreaterThan(40), context);
            });
        }
    }

    // As the settings ask for it: Start from left alone plays the pull, and each later start what it played before the
    // pull was built.
    private static ScenarioRun Start(TeaP1LivingLiquidPart? from, int seed, Action<ScenarioProbe>? probe = null, float? stopAt = null)
        => ScenarioRun.Execute(typeof(TeaP1LivingLiquidScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            StopAt = stopAt,
            Overrides = o => ((TeaP1LivingLiquidStateOverrides)o).StartFrom = from,
            Probe = probe,
        });

    [Test]
    public void ASeedRollsTheLaterPartsTheSameWhereverTheRunStarts()
    {
        for (var seed = 1; seed <= 60; seed++)
        {
            var rolled = new List<Rolled>();
            foreach (var from in new TeaP1LivingLiquidPart?[] { null, Protean1, Cascade2 })
                Start(from, seed, p => { if (p.Crossed(0.5f)) rolled.Add(Rolled.Of(Stage.Scenario(p).State!)); }, stopAt: 0.6f);

            Assert.That(rolled, Has.Count.EqualTo(3), $"seed {seed}");
            Assert.That(rolled.Distinct().ToList(), Has.Count.EqualTo(1), $"seed {seed}");
        }
    }

    // A start after the pull plays what it played before the pull existed. Each row was taken from that build, in a
    // real run of the seed: the run's own stream by its next draw, and where the main tank's bot stands on a spot it
    // takes with jitter, 9 s into Protean 1 and 11.5 s into Cascade 2. A roll drawn from the run's stream ahead of a
    // later start's, or a move added to its bots, shows here.
    [TestCase(Protean1, 1, 701796, "-0.276 4.410")]
    [TestCase(Protean1, 2, 89001, "3.221 -2.760")]
    [TestCase(Protean1, 3, 476206, "-3.142 2.862")]
    [TestCase(Protean1, 7, 25025, "-4.169 -0.221")]
    [TestCase(Protean1, 1234567, 684645, "3.243 -2.765")]
    [TestCase(Cascade2, 1, 701796, "-2.666 2.715")]
    [TestCase(Cascade2, 2, 89001, "-0.017 -3.854")]
    [TestCase(Cascade2, 3, 476206, "2.862 2.762")]
    [TestCase(Cascade2, 7, 25025, "-3.808 0.058")]
    [TestCase(Cascade2, 1234567, 684645, "2.764 -2.676")]
    public void ALaterStartPlaysASeedAsItDidBeforeThePullWasPlayed(TeaP1LivingLiquidPart from, int seed, int theStreamsNextDraw, string theMainTankStands)
    {
        var at = from == Protean1 ? 9.0f : 11.5f;
        var (next, stands) = (-1, "");
        var run = Start(from, seed, p =>
        {
            if (!p.Crossed(at)) return;
            var tank = p.Member(MainTank)!.Position;
            stands = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tank.X:F3} {tank.Z:F3}");
            next = p.World.Rng.Next(1_000_000);
        }, stopAt: at + 0.1f);

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That((next, stands), Is.EqualTo((theStreamsNextDraw, theMainTankStands)));
    }

    [Test]
    public void EachStartClearsTheFightFromThereOn([Values(null, Protean1, Cascade2)] TeaP1LivingLiquidPart? from)
    {
        var lasts = (from ?? Pull) switch { Pull => 124.5f, Protean1 => 63.6f, _ => 39.7f };
        for (var i = 0; i < 10; i++)
        {
            var seed = Random.Shared.Next();
            TeaP1LivingLiquidParts? parts = null;
            var run = Start(from, seed, p => parts ??= Stage.Scenario(p).State!.Parts);

            Assert.That(run.Passed, run.ToString);
            Assert.That(parts, Is.EqualTo(new TeaP1LivingLiquidParts(from ?? Pull, Cascade2)), $"seed {seed}");
            Assert.That(run.Elapsed, Is.InRange(lasts, lasts + 12f), $"it plays to the Living Liquid's fall (seed {seed})");
        }
    }

    // The strat stops looking at 60.5, and nothing of the pull keeps the run going from there but an Embolus still on
    // its way out, gone by 63.6.
    [Test]
    public void ThePullAloneEndsByItselfWithNobodyDead([Values] TeaP1OpenQuarter open)
    {
        var seed = Random.Shared.Next();
        var run = TeaP1LivingLiquidLpduAiTests.Play(new Roll { First = Pull, Last = Pull, Open1 = open }, PerRole.All[(int)open], seed);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.InRange(60.5f, 68f), $"seed {seed}");
    }
}
