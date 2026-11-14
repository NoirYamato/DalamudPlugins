using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LimitCut;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaLane;
using static AnoMech.Tests.TeaP1LimitCutLpduAiTests;
using static AnoMech.Tests.TeaP1LimitCutScenarioTests;
using Stage = AnoMech.Tests.TeaP1LimitCutStage;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI, which is never thrown. Here the seat is a
// human from the first frame, who plays the strat from what a player can see: the number over its own head, where
// the first two Hawk Blasters go off (its group takes one of them), every later blast, and Cruise Chaser's cleaves
// and charges. It starts on the entrance, as a first run does, and walks in. At each cue it walks straight to its
// number's next spot a reaction time late, at a player's pace, facing the way it walks.
//
// Nothing turns a human or puts it exactly on a spot, so each number takes the spot it is hit on in more than one
// way (Stance). Cruise Chaser lands by the odd number's facing and aims his charge at the even one, and both the
// cleave and the charge pass close to the group's other pair, waiting at the rim. An odd number turns clockwise
// along the rim once it stands there, or 10 degrees short of that or past it, or not at all: it then faces the way
// it walked in, along the chord from its last spot, 22.5 degrees outward of the rim. An even number stands on its
// spot or 1.5 y outward of it.
//
// Pressing, it uses Arm's Length or Surecast 2 s before its own hit and is not moved. Not pressing, it is thrown:
// when it finds itself somewhere it did not walk to, it waits for the slide to end, then its reaction time, and
// takes its number's way back.
//   an odd number is pushed 5 y on along the rim, onto floor that has already blown, and walks on as usual;
//   #2 lands among its group's first spots, which blow again: it cuts across to the middle at once, waits there,
//      and steps out again before the middle blows;
//   #4 lands beside the other group's waiting pair and stays with that group;
//   #6 sprints in as far as the middle blaster's edge, and on in once that has gone off;
//   #8 has nothing left to dodge.
// Whatever its seat, number, pace, reaction time and stance, nobody may die, but where a thrown even number's way
// out is too narrow to hold a player to (RunsATooNarrowWayOutMayLose): those runs may lose no more than they do
// today.
public class TeaP1LimitCutLpduHumanTests
{
    // Five fixed rolls per seat; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private const int UsualSeeds = 5;
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", UsualSeeds)).Select(i => 17 * i);
    private static readonly float[] ReactionTimes = [0f, 0.25f, 0.5f];

    // A player's speeds; the legs a sprinter sprints are the long ones.
    public enum Pace { Walking, Sprinting }

    // How the human takes the spot it is hit on: an odd number's facing, an even number's place.
    public enum Stance { Exact, TenDegreesInward, TenDegreesOutward, AsWalked, Wide }

    private const float TurnedOffTheRim = 10f * MathF.PI / 180f;
    private const float WideOfItsSpot = 1.5f;

    private static Stance[] StancesOf(int number)
        => number % 2 == 1 ? [Stance.Exact, Stance.TenDegreesInward, Stance.TenDegreesOutward, Stance.AsWalked] : [Stance.Exact, Stance.Wide];

    // Runs with a death, of the 40 on the usual rolls; null where the case is held to none.
    //   #2 thrown from its spot lands among its group's first spots, which blow again. Walking, a quarter second
    //      late it clears that blast by 0.2 y; half a second late it is inside it in more than half. From 1.5 y wide
    //      of its spot the rim stops it sooner, further from that blast.
    //   #4 thrown from its spot is stopped by the rim on the edge of the next pair's blast. From 1.5 y wide the rim
    //      stops it 20 degrees further on, inside that blast 1.7 s before it goes off, with 7.5 y of rim to its
    //      edge: it gets out only by setting off at once, or sprinting a quarter second late.
    private static int? RunsATooNarrowWayOutMayLose(int number, Pace pace, float late, bool presses, Stance stance)
    {
        if (presses || late == 0f) return null;
        return (number, stance, pace) switch
        {
            (2, Stance.Exact, Pace.Walking) => late <= 0.25f ? 0 : 22,
            (4, Stance.Wide, Pace.Walking) => 40,
            (4, Stance.Wide, Pace.Sprinting) => late <= 0.25f ? null : 19,
            _ => null,
        };
    }

    private static IEnumerable<TestCaseData> Matrix(bool heldTo)
        => from number in Enumerable.Range(1, 8)
           from pace in Enum.GetValues<Pace>()
           from late in ReactionTimes
           from presses in new[] { true, false }
           from stance in StancesOf(number)
           where RunsATooNarrowWayOutMayLose(number, pace, late, presses, stance) is null == heldTo
           select new TestCaseData(number, pace, late, presses, stance)
               .SetName($"{{m}}(#{number}, {pace}, {late.ToString("F2", CultureInfo.InvariantCulture)}s late, {(presses ? "pressing" : "thrown")}, {stance})");

    private static IEnumerable<TestCaseData> HeldToCases() => Matrix(heldTo: true);
    private static IEnumerable<TestCaseData> TooNarrowCases() => Matrix(heldTo: false);

    [TestCaseSource(nameof(HeldToCases))]
    public void AHumanPlayingTheStratIsNeverCaught(int number, Pace pace, float late, bool presses, Stance stance)
    {
        var tightest = new Clearances();
        var runs = 0;
        var problems = new List<string>();
        foreach (var seat in PerRole.All)
        foreach (var seed in Seeds)
        {
            runs++;
            problems.AddRange(Play(seat, number, pace, late, presses, stance, seed, tightest, out _));
        }

        TestContext.Out.WriteLine($"{runs} runs; the human's tightest clearances: {tightest}");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    [TestCaseSource(nameof(TooNarrowCases))]
    public void AThrownNumberWithATooNarrowWayOutLosesNoMoreRunsThanItDoes(int number, Pace pace, float late, bool presses, Stance stance)
    {
        var tightest = new Clearances();
        var (runs, lost, lostOnTheUsualRolls) = (0, 0, 0);
        var broken = new List<string>();
        foreach (var seat in PerRole.All)
        foreach (var seed in Seeds)
        {
            runs++;
            Play(seat, number, pace, late, presses, stance, seed, tightest, out var run, writeArtifacts: false);
            if (run.Failure is not null) broken.Add(run.ToString());
            else if (!run.Passed)
            {
                lost++;
                if (seed <= 17 * UsualSeeds) lostOnTheUsualRolls++;
            }
        }

        TestContext.Out.WriteLine($"{runs} runs, {lost} with a death, {lostOnTheUsualRolls} of them on the usual rolls; the human's tightest clearances: {tightest}");
        Assert.That(broken, Is.Empty, () => string.Join(Environment.NewLine, broken));
        Assert.That(lostOnTheUsualRolls, Is.LessThanOrEqualTo(RunsATooNarrowWayOutMayLose(number, pace, late, presses, stance) ?? 0), "runs with a death on the usual rolls");
    }

    private static List<string> Play(PartyRole seat, int number, Pace pace, float late, bool presses, Stance stance, int seed,
                                     Clearances tightest, out ScenarioRun run, bool writeArtifacts = true)
    {
        var rng = new Random(seed * 8 + (int)seat);
        var numbers = PerRole.All.Where(role => role != seat).OrderBy(_ => rng.Next()).ToList();
        numbers.Insert(number - 1, seat);
        var axis = (TeaP1FirstBlasters)rng.Next(4);
        var guide = new Guide(seat, pace, late, presses, stance);
        var problems = new List<string>();
        run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = seat,
            WriteArtifactsOnFailure = writeArtifacts,
            Overrides = o => Pin((TeaP1LimitCutStateOverrides)o, numbers, axis),
            Takeovers = [new Takeover(0f, null)],
            Probe = p =>
            {
                tightest.Measure(p, seat, number, axis);
                guide.Tick(p);
            },
        });

        if (!run.Passed) problems.Add(run.ToString());
        if (guide.Number != number)
            problems.Add($"the human read its number as {guide.Number}");
        var itsGroup = new Stage(axis).GroupBearing(number) % 360f;
        if (guide.GroupBearing != itsGroup)
            problems.Add($"the human read its group's first blaster at {guide.GroupBearing}, the strat puts it at {itsGroup}");
        if (!guide.InTheMiddleAsTheNumbersShow)
            problems.Add("the human had not walked in to the middle by the time the numbers showed");
        if (presses && guide.Slides > 0)
            problems.Add("the human was thrown though it pressed");
        if (run.Passed && !presses && guide.Slides != 1)
            problems.Add($"the human was thrown {guide.Slides} times, not once");
        if (run.Passed && guide.LegsLeft > 0)
            problems.Add($"the human never set off on its last {guide.LegsLeft} walks");
        return problems.Select(problem => $"seed {seed}, {seat} as #{number} (first blasters {axis}, {pace}, {late:F2}s late, "
                                          + $"{(presses ? "pressing" : "thrown")}, {stance}): {problem}").ToList();
    }

    private static uint AntiKnockbackOf(PartyRole seat)
        => seat is RegenHealer or ShieldHealer or CasterDps ? P1PlayerActionId.Surecast : P1PlayerActionId.ArmsLength;

    // Each cue's walk sets off a reaction time after the cue is seen, from wherever the human is then; one marked
    // to follow the walk before it waits for that to arrive as well.
    private sealed class Guide(PartyRole seat, Pace pace, float late, bool presses, Stance stance)
    {
        private const float WalkSpeed = 6f, SprintSpeed = 7.8f, SprintsLegsOver = 12f;
        private const float PressBeforeItsHit = 2f;
        private const string Start = "start", Thrown = "thrown";

        private sealed record Leg(string Cue, Func<Vector2, Vector2> To, bool Follows = false, bool Turns = false, bool Sprints = false);

        public int Number { get; private set; }
        public float? GroupBearing { get; private set; }
        public bool InTheMiddleAsTheNumbersShow { get; private set; }
        public int Slides { get; private set; }
        public int LegsLeft => legs.Count - next;

        private readonly List<Leg> legs = [new(Start, _ => InTheMiddle(seat, WaitRadius))];
        private int next;
        private readonly Dictionary<string, float> seen = new() { [Start] = 0f };
        private (float Depart, Vector2 From, Vector2 To, float Speed, bool Turns)? walking;
        private Vector2? put;
        private Vector2 previous;
        private bool sliding, interrupted;
        private float freeAt;
        private int rowsRead, pairs, middles, cleaves, charges;
        private float lastPair = float.NegativeInfinity;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (!me.IsAlive()) return;
            ReadTheTimeline(p, me);
            var at = Flat(me.Position);
            if (p.Crossed(5.9f)) InTheMiddleAsTheNumbersShow = at.Length() < 2f;
            if (presses && Number > 0 && p.Crossed(HitAt(Number) - PressBeforeItsHit))
                p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, AntiKnockbackOf(seat), 0));
            if (StillSliding(p, at)) return;
            SetOffOnTheNextWalk(p, at);
            Walk(p, me);
        }

        private void ReadTheTimeline(ScenarioProbe p, SimCharacter me)
        {
            var rows = p.Fake.Timeline.Rows;
            var mine = me.EntityId.ToString("X8");
            var origin = TeaZone.Instance.Origin;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                if (row["kind"] is "ac" or "ac_self" && row["id"] == "0x22" && row["actor"] == mine)
                    ReadItsNumber(Convert.ToUInt32(row["p"].Split(',')[0], 16));
                if (row["kind"] != "ability") continue;
                if (row["id"] == $"{P1ActionId.HawkBlaster}")
                    ReadABlast(p.Time, new Vector2(Float(row["x"]) - origin.X, Float(row["z"]) - origin.Z));
                else if (row["id"] == $"{P1ActionId.AlphaSword}")
                    seen[$"C{2 * ++cleaves - 1}"] = p.Time;
                else if (row["id"] == $"{P1ActionId.SuperBlasstyCharge}")
                    seen[$"K{2 * ++charges}"] = p.Time;
            }
        }

        private void ReadItsNumber(uint lockon)
        {
            var number = Array.IndexOf(P1LockonId.LimitCutNumbers, lockon) + 1;
            if (number == 0 || Number != 0) return;
            Number = number;
            legs.AddRange(Route(number));
        }

        // The two of a pair go off together. Numbers 1, 2, 5, 6 take the first pair's blaster in the west-to-north-east
        // half, the others the opposite one.
        private void ReadABlast(float now, Vector2 centre)
        {
            if (centre.Length() < 1f)
            {
                seen[$"M{++middles}"] = now;
                return;
            }
            if (now - lastPair > 1f) seen[$"P{++pairs}"] = now;
            lastPair = now;
            if (pairs > 1) return;
            var bearing = MathF.Round(MathF.Atan2(centre.X, -centre.Y) * 180f / MathF.PI / 45f) * 45f;
            bearing = (bearing + 360f) % 360f;
            var inTheWestToNorthEastHalf = bearing is 270f or 315f or 0f or 45f;
            if (inTheWestToNorthEastHalf == (Number is 1 or 2 or 5 or 6)) GroupBearing = bearing;
        }

        private IEnumerable<Leg> Route(int number)
        {
            var (inner, outer) = number % 2 == 1 ? (InnerOdd, OuterOdd) : (InnerEven, OuterEven);
            Leg On(string cue, TeaLane lane, int spot, bool follows = false, bool turns = false)
                => new(cue, _ => LaneSpot(lane, spot), follows, turns);
            Leg runOut = new("P1", _ => RunOutSpot(number));
            Leg middle = new("K8", _ => InTheMiddle(seat, JKickRadius));
            return number switch
            {
                1 => [runOut, On("P2", inner, 1), On("P3", inner, 2, turns: true), On("C1", outer, 3), On("P5", outer, 4), On("P6", outer, 5), On("K6", outer, 6), middle],
                2 => [runOut, On("P2", inner, 1), On("P3", inner, 2), On("K2", outer, 3), On("P5", outer, 4), On("P6", outer, 5), On("K6", outer, 6), middle],
                3 => [runOut, On("P2", inner, 1), On("P3", inner, 2), On("P4", inner, 3, turns: true), On("C3", outer, 4), On("P6", outer, 5, follows: true), On("C5", outer, 6, follows: true), middle],
                4 => [runOut, On("P2", inner, 1), On("P3", inner, 2), On("P4", inner, 3), On("K4", inner, 4), On("P7", outer, 5), On("K6", outer, 6, follows: true), middle],
                5 => [runOut, On("P2", outer, 1), On("P3", outer, 2), On("K2", inner, 3), On("P5", inner, 4), On("P6", inner, 5, turns: true), On("C5", outer, 6), middle],
                6 => [runOut, On("P2", outer, 1), On("P3", outer, 2), On("K2", inner, 3), On("P5", inner, 4), On("P6", inner, 5), On("K6", outer, 6), middle],
                7 => [runOut, On("P2", outer, 1), On("P3", outer, 2), On("P4", outer, 3), On("C3", inner, 4), On("P6", inner, 5, follows: true), On("P7", inner, 6, follows: true, turns: true), On("C7", outer, 6), middle],
                _ => [runOut, On("P2", outer, 1), On("P3", outer, 2), On("P4", outer, 3), On("C3", inner, 4), On("P6", inner, 5, follows: true), On("P7", inner, 6, follows: true), middle],
            };
        }

        // Bearings are from the thrown number's own group's first blaster. The other group's spot 5 lies 45 degrees
        // on from it, and its spot 6 at 90.
        private IEnumerable<Leg> TheWayBack(int number)
        {
            var fast = pace == Pace.Sprinting;
            Leg middle = new("K8", _ => InTheMiddle(seat, JKickRadius));
            return number switch
            {
                2 =>
                [
                    new(Thrown, _ => FromItsGroup(41.3f, 16.2f), Sprints: fast),
                    new(Thrown, _ => FromItsGroup(94.6f, 10.03f), Follows: true, Sprints: fast),
                    new(Thrown, _ => FromItsGroup(59f, 2.33f), Follows: true, Sprints: fast),
                    new("P8", _ => FromItsGroup(4.6f, 12.54f), Sprints: fast),
                    middle,
                ],
                4 => [new(Thrown, _ => FromItsGroup(39f, Stage.OuterRadius)), new("K6", _ => FromItsGroup(84f, Stage.OuterRadius)), middle],
                6 => [new(Thrown, from => from * (11f / from.Length()), Sprints: true), new("M2", _ => InTheMiddle(seat, JKickRadius), Sprints: true), middle],
                _ => [middle],
            };
        }

        private Vector2 FromItsGroup(float degreesOn, float radius) => Stage.OnRing(GroupBearing!.Value + degreesOn, radius);

        private Vector2 LaneSpot(TeaLane lane, int spot)
        {
            var bearing = GroupBearing!.Value + 45f * spot;
            var wide = stance == Stance.Wide && spot == Stage.HitSpotOf(Number) ? WideOfItsSpot : 0f;
            return lane switch
            {
                InnerOdd => Stage.OnRing(bearing - 2f, Stage.InnerRadius),
                InnerEven => Stage.OnRing(bearing - 30f, Stage.InnerRadius + wide),
                OuterOdd => Stage.OnRing(bearing - 4f, Stage.OuterRadius),
                _ => Stage.OnRing(bearing - 8f, Stage.OuterRadius),
            };
        }

        private Vector2 RunOutSpot(int number) => number switch
        {
            2 or 4 => LaneSpot(InnerEven, 0),
            1 or 3 => FromItsGroup(-12f, Stage.InnerRadius),
            5 or 7 => FromItsGroup(-12f, Stage.OuterRadius),
            _ => FromItsGroup(-16f, Stage.OuterRadius),
        };

        // Somewhere it did not put itself: a knockback has it. It can do nothing until that has let go.
        private bool StillSliding(ScenarioProbe p, Vector2 at)
        {
            if (!sliding && put is { } last && Vector2.Distance(at, last) > 0.02f)
            {
                sliding = true;
                interrupted = walking is not null;
                walking = null;
                Slides++;
            }
            else if (sliding && Vector2.Distance(at, previous) < 1e-4f)
            {
                sliding = false;
                put = at;
                freeAt = p.Time + late;
                seen[Thrown] = p.Time;
                if (Number % 2 == 0)
                {
                    legs.RemoveRange(next, legs.Count - next);
                    legs.AddRange(TheWayBack(Number));
                }
                else if (interrupted) next--;
            }
            previous = at;
            return sliding;
        }

        private void SetOffOnTheNextWalk(ScenarioProbe p, Vector2 at)
        {
            if (next >= legs.Count) return;
            var leg = legs[next];
            if (!seen.TryGetValue(leg.Cue, out var cue) || p.Time < MathF.Max(cue + late, freeAt)) return;
            if (leg.Follows && walking is not null) return;
            next++;
            var to = leg.To(at);
            var sprints = leg.Sprints || (pace == Pace.Sprinting && Vector2.Distance(at, to) > SprintsLegsOver);
            walking = (p.Time, at, to, sprints ? SprintSpeed : WalkSpeed, leg.Turns);
        }

        private void Walk(ScenarioProbe p, SimCharacter me)
        {
            if (walking is not { } w) return;
            var length = Vector2.Distance(w.From, w.To);
            var walked = (p.Time - w.Depart) * w.Speed;
            var arrived = walked >= length;
            var at = arrived ? w.To : Vector2.Lerp(w.From, w.To, walked / length);
            var heading = arrived && w.Turns && OnTheSpotItIsCleavedOn(w.To) is { } turned ? turned
                : length > 1e-3f ? MathF.Atan2(w.To.X - w.From.X, w.To.Y - w.From.Y)
                : me.Rotation;
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), heading));
            put = at;
            previous = at;
            if (arrived) walking = null;
        }

        // Outward is toward the wall. Null: it does not turn.
        private float? OnTheSpotItIsCleavedOn(Vector2 spot) => stance switch
        {
            Stance.AsWalked => null,
            Stance.TenDegreesInward => Stage.ClockwiseAlongTheRim(spot) - TurnedOffTheRim,
            Stance.TenDegreesOutward => Stage.ClockwiseAlongTheRim(spot) + TurnedOffTheRim,
            _ => Stage.ClockwiseAlongTheRim(spot),
        };
    }

    // How close the human came to each hit that was not its own, and everyone else to the human's own, as it went
    // off, over every run of a case.
    private sealed class Clearances
    {
        private readonly Dictionary<string, float> tightest = new();

        public void Measure(ScenarioProbe p, PartyRole seat, int number, TeaP1FirstBlasters axis)
        {
            var me = p.Member(seat);
            if (!me.IsAlive()) return;
            var at = Flat(me!.Position);
            for (var pair = 0; pair < HawkBlasterPairsAt.Length; pair++)
            {
                if (!p.Crossed(HawkBlasterPairsAt[pair])) continue;
                var bearing = new Stage(axis).FirstBearing + 45f * (pair % 4);
                Note("a pair's blast", MathF.Min(Vector2.Distance(at, TeaP1LimitCutLpduMarginTests.BlastCentre(bearing)),
                                                Vector2.Distance(at, TeaP1LimitCutLpduMarginTests.BlastCentre(bearing + 180f))) - 10f);
            }
            if (HawkBlasterMiddlesAt.Any(p.Crossed)) Note("a middle blast", at.Length() - 10f);
            var others = PerRole.All.Where(role => role != seat && p.Member(role).IsAlive()).Select(role => Flat(p.Member(role)!.Position)).ToList();
            for (var hit = 0; hit < AlphaSwordsAt.Length; hit++)
            {
                if (p.Crossed(AlphaSwordsAt[hit]))
                {
                    float Outside(Vector2 who) => TeaP1LimitCutLpduMarginTests.OutsideTheCone(who, CruiseChaser(p).Placement(), 25f + CruiseChaser(p).HitboxRadius);
                    if (number != 2 * hit + 1) Note("another number's cleave", Outside(at));
                    else if (others.Count > 0) Note("its own cleave, off everyone else", others.Min(Outside));
                }
                if (p.Crossed(SuperBlasstyChargesAt[hit]))
                {
                    float Outside(Vector2 who) => TeaP1LimitCutLpduMarginTests.OutsideTheCharge(who, CruiseChaser(p).Placement(), 50f + CruiseChaser(p).HitboxRadius);
                    if (number != 2 * hit + 2) Note("another number's charge", Outside(at));
                    else if (others.Count > 0) Note("its own charge, off everyone else", others.Min(Outside));
                }
            }
        }

        private void Note(string name, float clearance)
            => tightest[name] = MathF.Min(tightest.GetValueOrDefault(name, float.MaxValue), clearance);

        public override string ToString()
            => string.Join(", ", tightest.OrderBy(t => t.Value).Select(t => $"{t.Key} {t.Value.ToString("F2", CultureInfo.InvariantCulture)}"));
    }

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);
}
