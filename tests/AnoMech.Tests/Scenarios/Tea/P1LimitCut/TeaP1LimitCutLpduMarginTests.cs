using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea.P1LimitCut;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaP1LimitCutLpduAiTests;
using static AnoMech.Tests.TeaP1LimitCutScenarioTests;
using Stage = AnoMech.Tests.TeaP1LimitCutStage;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies where Cruise Chaser lands the scenario's own distance (3 y) behind an
// odd number that faces exactly along the rim. Nobody has measured that distance, and a player's facing is never
// exact, so these runs play every first-blaster axis with him landing 1.5, 3 and 4.5 y behind, and the scenario's
// own distance if it has been tuned off those, and the odd number turned 5 degrees inward, not at
// all, and 5 degrees outward just before he lands, and measure how far every bot stands outside every cleave,
// charge and blast as it goes off, with Cruise Chaser where that run put him: the hit pair's partner, the group's
// other pair, and the other group wherever it is at that second, holding or walking. The numbers shift a seat each
// run and the player's seat moves round, so every role is a bot under every number. The player's seat is left out
// and so is a hit aimed through it: under the debug driver it walks by movement input and can stop a frame's step
// off its spot, and in play it is a human, whom TeaP1LimitCutLpduHumanTests covers.
//
// Nothing turns a human odd number, and the further it faces off the rim the nearer its cleave comes to the bots:
// outward to its group's odd waiting at the rim, inward to #4 as it crosses ahead of #5's. So the scenario's own
// landing distance is also played with the odd 15 degrees off either way, held only to catching nobody. Past about
// 20 degrees outward, or 17 inward for #5, the cleave reaches them.
public class TeaP1LimitCutLpduMarginTests
{
    // Two at least, so a hit aimed through the player's seat on one seed is aimed through bots on another.
    private static int SeedsPerRun => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 4));

    private static readonly float[] LandsBehind = new[] { 1.5f, 3f, 4.5f, P1Geometry.CruiseChaserLandsBehind }.Distinct().Order().ToArray();
    private static readonly float[] DegreesOutward = [-5f, 0f, 5f];
    private static readonly float[] DegreesOutwardWellOffTheRim = [-15f, 15f];
    private const float JustBeforeHeLands = 0.05f;
    private const float OffItsTurnAtMost = 1f;

    private const float BlastRadius = 10f, ConeHalfAngle = MathF.PI / 4f, ConeRange = 25f, ChargeLength = 50f, ChargeHalfWidth = 5f;

    private const string PairBlast = "a pair's blast", MiddleBlast = "a middle blast";
    private const string ConePartner = "cone: the hit pair's even", ConeOtherOdd = "cone: the group's other odd",
                         ConeOtherEven = "cone: the group's other even", ConeOtherGroup = "cone: the other group",
                         FiveOnFour = "cone: #4 outside #5's";
    private const string ChargePartner = "charge: its own odd, walking on", ChargeSeven = "charge: #7, gone outside",
                         ChargeOtherOdd = "charge: the group's other odd", ChargeOtherEven = "charge: the group's other even",
                         FourOnEight = "charge: #8 outside #4's", ChargeOtherGroup = "charge: the other group";

    // Clearances with every bot exactly on its spot, and what the bots' 0.3 y jitter can take: one bot's from a
    // blast, two bots' from a cleave (it moves with the odd), and the worst of three from a charge, whose line is
    // aimed from a few yalms away by the even, so that its jitter turns it. A bystander is read a frame after the
    // hit, which costs a walking one another 0.11 y. The cleaved odd is taken 1.4 s into its walk at 6.5 y/s
    // as its even is charged. `Usual` rows hold where he lands the scenario's own distance behind an odd facing
    // exactly along the rim, and were measured with it at 3 y; the others hold in every case played.
    private static readonly (string Check, bool Usual, float Exact, float AtLeast)[] Table =
    [
        (PairBlast, false, 1.16f, 0.84f),
        (MiddleBlast, false, 4.0f, 3.68f),
        (ConeOtherOdd, true, 2.09f, 1.47f),
        (ConePartner, false, 2.43f, 1.81f),
        (ConeOtherEven, false, 1.70f, 1.08f),
        (ConeOtherEven, true, 2.98f, 2.36f),
        (FiveOnFour, false, 5.79f, 4.8f),
        (ConeOtherGroup, false, 5.79f, 4.8f),
        (ChargePartner, false, 9.99f, 8.7f),
        (ChargePartner, true, 11.03f, 9.7f),
        (ChargeOtherEven, true, 3.06f, 1.9f),
        (ChargeOtherOdd, true, 4.32f, 3.2f),
        (ChargeSeven, true, 4.32f, 3.2f),
        (FourOnEight, false, 5.03f, 4.2f),
        (ChargeOtherGroup, false, 6.83f, 2.3f),
        (ChargeOtherGroup, true, 11.90f, 9.0f),
    ];

    private sealed record Case(TeaP1FirstBlasters Axis, float LandsBehind, float DegreesOutward)
    {
        public bool Usual => LandsBehind == P1Geometry.CruiseChaserLandsBehind && DegreesOutward == 0f;
        public bool WellOffTheRim => MathF.Abs(DegreesOutward) > 5f;
    }

    [Test]
    public void KeepsItsClearancesWhereverCruiseChaserLands()
    {
        var usual = new Dictionary<string, float>();
        var any = new Dictionary<string, float>();
        var wellOffTheRim = new Dictionary<string, float>();
        var tightest = new Dictionary<string, string>();
        var held = new HashSet<(PartyRole, int)>();
        var problems = new List<string>();
        var cases = (from axis in Enum.GetValues<TeaP1FirstBlasters>()
                     from landsBehind in LandsBehind
                     from degreesOutward in DegreesOutward
                     select new Case(axis, landsBehind, degreesOutward))
            .Concat(from axis in Enum.GetValues<TeaP1FirstBlasters>()
                    from degreesOutward in DegreesOutwardWellOffTheRim
                    select new Case(axis, P1Geometry.CruiseChaserLandsBehind, degreesOutward)).ToList();
        for (var run = 0; run < cases.Count; run++)
        for (var i = 0; i < SeedsPerRun; i++)
        {
            var at = cases[run];
            var numbers = PerRole.All.Select((_, k) => PerRole.All[(k + run) % 8]).ToArray();
            var player = PerRole.All[(run / 8 + run + 3 * i) % 8];
            for (var number = 1; number <= 8; number++)
                if (numbers[number - 1] != player) held.Add((numbers[number - 1], number));
            var result = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
            {
                PlayerRole = player,
                Overrides = o => Pin((TeaP1LimitCutStateOverrides)o, numbers, at.Axis, at.LandsBehind),
                Probe = p =>
                {
                    TurnTheOddJustBeforeHeLands(p, at, numbers);
                    problems.AddRange(CleavesOffTheTurnItWasGiven(p, at, numbers, player));
                    Measure(p, at, numbers, player, (check, clearance) =>
                    {
                        if (at.WellOffTheRim)
                        {
                            Note(wellOffTheRim, $"{check}, the odd {F(at.DegreesOutward)} degrees outward", clearance);
                            return;
                        }
                        if (clearance < any.GetValueOrDefault(check, float.MaxValue))
                            tightest[check] = $"first blasters {at.Axis}, landing {F(at.LandsBehind)} y behind, {F(at.DegreesOutward)} degrees outward";
                        Note(any, check, clearance);
                        if (at.Usual) Note(usual, check, clearance);
                    });
                },
            });
            if (!result.Passed) problems.Add($"{at}: {result}");
        }

        TestContext.Out.WriteLine($"Smallest clearance (y) in every case played, and where he lands {F(P1Geometry.CruiseChaserLandsBehind)} y behind an odd facing along the rim:");
        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            any.OrderBy(m => m.Key).Select(m => $"  {m.Key}: {F(m.Value)} ({tightest[m.Key]}); usual {F(usual.GetValueOrDefault(m.Key, float.NaN))}")));
        TestContext.Out.WriteLine($"And landing {F(P1Geometry.CruiseChaserLandsBehind)} y behind an odd well off the rim:");
        TestContext.Out.WriteLine(string.Join(Environment.NewLine, wellOffTheRim.OrderBy(m => m.Key).Select(m => $"  {m.Key}: {F(m.Value)}")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(held, Has.Count.EqualTo(64), "every role was a bot under every number");
            foreach (var (check, usualOnly, _, atLeast) in Table)
                Assert.That((usualOnly ? usual : any).GetValueOrDefault(check, float.NaN), Is.GreaterThanOrEqualTo(atLeast),
                            usualOnly ? $"{check}, landing {F(P1Geometry.CruiseChaserLandsBehind)} y behind an odd facing along the rim" : $"{check}, in every case");
            foreach (var (check, clearance) in any)
                Assert.That(clearance, Is.GreaterThan(0f), $"{check}, in every case ({tightest[check]})");
            Assert.That(wellOffTheRim.Keys, Has.Member($"{ConeOtherOdd}, the odd 15 degrees outward"));
            Assert.That(wellOffTheRim.Keys, Has.Member($"{FiveOnFour}, the odd -15 degrees outward"));
            foreach (var (check, clearance) in wellOffTheRim)
                Assert.That(clearance, Is.GreaterThan(0f), $"{check}, landing {F(P1Geometry.CruiseChaserLandsBehind)} y behind");
        });
    }

    private static void Note(Dictionary<string, float> margins, string check, float clearance)
        => margins[check] = MathF.Min(margins.GetValueOrDefault(check, float.MaxValue), clearance);

    private static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // The strat has turned it along the rim by then; outward is toward the wall.
    private static void TurnTheOddJustBeforeHeLands(ScenarioProbe p, Case at, PartyRole[] numbers)
    {
        if (at.DegreesOutward == 0f) return;
        var stage = new Stage(at.Axis);
        for (var hit = 0; hit < CruiseChaserLandsAt.Length; hit++)
        {
            if (!p.Crossed(CruiseChaserLandsAt[hit] - JustBeforeHeLands)) continue;
            var odd = 2 * hit + 1;
            p.Member(numbers[odd - 1])!.SetRotation(Stage.ClockwiseAlongTheRim(stage.HitSpot(odd)) + at.DegreesOutward * MathF.PI / 180f);
        }
    }

    // A turned case measures nothing of its own unless the turn it gave the odd is the one he cleaves along.
    private static IEnumerable<string> CleavesOffTheTurnItWasGiven(ScenarioProbe p, Case at, PartyRole[] numbers, PartyRole player)
    {
        if (at.DegreesOutward == 0f) yield break;
        var stage = new Stage(at.Axis);
        for (var hit = 0; hit < AlphaSwordsAt.Length; hit++)
        {
            var odd = 2 * hit + 1;
            if (numbers[odd - 1] == player || !p.Crossed(AlphaSwordsAt[hit])) continue;
            var given = Stage.ClockwiseAlongTheRim(stage.HitSpot(odd)) + at.DegreesOutward * MathF.PI / 180f;
            var off = AngleBetween(CruiseChaser(p).Rotation, given) * 180f / MathF.PI;
            if (off > OffItsTurnAtMost)
                yield return $"{at}: he cleaves #{odd} {F(off)} degrees off the facing this case gave it";
        }
    }

    private static void Measure(ScenarioProbe p, Case at, PartyRole[] numbers, PartyRole player, Action<string, float> note)
    {
        var bots = Enumerable.Range(1, 8)
            .Where(n => numbers[n - 1] != player && p.Member(numbers[n - 1]).IsAlive())
            .Select(n => (Number: n, At: Flat(p.Member(numbers[n - 1])!.Position)))
            .ToList();

        for (var pair = 0; pair < HawkBlasterPairsAt.Length; pair++)
        {
            if (!p.Crossed(HawkBlasterPairsAt[pair])) continue;
            var bearing = new Stage(at.Axis).FirstBearing + 45f * (pair % 4);
            foreach (var bot in bots)
                note(PairBlast, MathF.Min(Vector2.Distance(bot.At, BlastCentre(bearing)), Vector2.Distance(bot.At, BlastCentre(bearing + 180f))) - BlastRadius);
        }
        if (HawkBlasterMiddlesAt.Any(p.Crossed))
            foreach (var bot in bots)
                note(MiddleBlast, bot.At.Length() - BlastRadius);

        for (var hit = 0; hit < AlphaSwordsAt.Length; hit++)
        {
            var (odd, even) = (2 * hit + 1, 2 * hit + 2);
            if (numbers[odd - 1] == player || numbers[even - 1] == player) continue;
            if (p.Crossed(AlphaSwordsAt[hit]))
            {
                var cruiseChaser = CruiseChaser(p);
                foreach (var bot in bots.Where(b => b.Number != odd))
                {
                    var clearance = OutsideTheCone(bot.At, cruiseChaser.Placement(), ConeRange + cruiseChaser.HitboxRadius);
                    note(bot.Number == even ? ConePartner
                         : !SameGroup(bot.Number, odd) ? ConeOtherGroup
                         : bot.Number % 2 == 1 ? ConeOtherOdd : ConeOtherEven, clearance);
                    if (odd == 5 && bot.Number == 4) note(FiveOnFour, clearance);
                }
            }
            if (p.Crossed(SuperBlasstyChargesAt[hit]))
            {
                var cruiseChaser = CruiseChaser(p);
                foreach (var bot in bots.Where(b => b.Number != even))
                {
                    var clearance = OutsideTheCharge(bot.At, cruiseChaser.Placement(), ChargeLength + cruiseChaser.HitboxRadius);
                    note(bot.Number == odd ? (odd == 7 ? ChargeSeven : ChargePartner)
                         : !SameGroup(bot.Number, even) ? ChargeOtherGroup
                         : bot.Number % 2 == 1 ? ChargeOtherOdd
                         : even == 4 ? FourOnEight : ChargeOtherEven, clearance);
                }
            }
        }
    }

    private static bool SameGroup(int a, int b) => (a is 3 or 4 or 7 or 8) == (b is 3 or 4 or 7 or 8);

    // Cardinal centres are 14 y out, diagonal ones on (+-10, +-10).
    internal static Vector2 BlastCentre(float bearing)
    {
        var onRing = Stage.OnRing(bearing, 14f);
        return MathF.Abs(onRing.X) < 0.01f || MathF.Abs(onRing.Y) < 0.01f ? onRing : new Vector2(MathF.Sign(onRing.X) * 10f, MathF.Sign(onRing.Y) * 10f);
    }

    // How far outside a cone a point stands; negative inside it, by how far from its nearest edge.
    internal static float OutsideTheCone(Vector2 at, Placement from, float range)
    {
        var toward = at - from.Position2;
        var distance = toward.Length();
        if (distance < 1e-4f) return -range;
        var off = MathF.Acos(Math.Clamp(Vector2.Dot(toward / distance, Stage.Facing(from.Rotation)), -1f, 1f)) - ConeHalfAngle;
        if (off <= 0f) return distance > range ? distance - range : -MathF.Min(distance * MathF.Sin(-off), range - distance);
        if (off >= MathF.PI / 2f) return distance;
        var along = distance * MathF.Cos(off);
        var across = distance * MathF.Sin(off);
        return along <= range ? across : MathF.Sqrt((along - range) * (along - range) + across * across);
    }

    // The same for a line that starts at `from` and has no reach behind it.
    internal static float OutsideTheCharge(Vector2 at, Placement from, float length)
    {
        var toward = at - from.Position2;
        var facing = Stage.Facing(from.Rotation);
        var along = Vector2.Dot(toward, facing);
        var across = MathF.Abs(toward.X * facing.Y - toward.Y * facing.X);
        var before = along < 0f ? -along : along > length ? along - length : 0f;
        var wide = MathF.Max(across - ChargeHalfWidth, 0f);
        if (before > 0f || wide > 0f) return MathF.Sqrt(before * before + wide * wide);
        return -MathF.Min(MathF.Min(along, length - along), ChargeHalfWidth - across);
    }
}
