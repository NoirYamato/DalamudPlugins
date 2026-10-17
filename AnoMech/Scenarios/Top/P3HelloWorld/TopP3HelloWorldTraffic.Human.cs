using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

// The human is guessed, not planned. From a standstill it walks straight to its spot at run speed,
// up to a second late, or sprints there. Once it is seen off that walk, it is guessed again from
// where it is: walking round the boss outside its hitbox it keeps to its ring, as the guides'
// lanes do, and walking any other way it may keep its heading a while, which covers a curving
// walk. A human that leaves a spot it reached is heading for the next spot the strat gives it,
// whether or not its cue has come. A bot in the human's way steps aside to the nearest clear spot
// round its own spot, so repeated step-asides never walk it further off, and back once it can.
internal sealed partial class TopP3HelloWorldTraffic
{
    private const float HumanLatestStart = 1f;
    private const int HumanStarts = 6;
    private static readonly float[] HumanKeepsItsHeadingFor = [0f, 0.3f, 0.6f, 0.9f, 1.2f];
    private const float HumanTurnsWithin = 0.6f;
    private const float HumanWalksRoundFrom = 12f, HumanWalksRoundWithin = 0.7f, HumanWalksRoundAtMost = 270f;
    private static readonly float[] HumanLeavesItsRingEarlierBy = [0f, 15f, 30f, 45f];
    private const float HumanMoving = 1f, HumanSprinting = RunSpeed + 0.25f;
    private const float HumanOffItsWalk = 0.5f;
    private const float HumanSettlesWithin = 3f;
    private const float SameSpot = 2f;
    private const float StepAsideClearance = 2.6f;
    private static readonly float[] StepAsideDistances = [1f, 1.5f, 2f, 2.6f, 3.2f, 4f, 5f];
    private const int StepAsideDirections = 16;
    private const float StepBackAfter = 0.3f, StepBackRetry = 0.5f;

    private Vector2? humanDestination;
    private Vector2 humanSetOffFrom;
    private bool humanReachedIt;
    private readonly Queue<Vector2> humansNextSpots = new();
    private (float Time, Vector2 At)? humanSeen;
    private Vector2 humanVelocity;
    private float guessedPace;
    private readonly Vector2?[] steppedAsideRound = new Vector2?[8];

    private int Human => (int)world.Party.PlayerRole;

    // The spots the strat gives the human this set, in order, so a human who goes before its cue
    // is still guessed right: the guides send a tethered player out once its soaker is in its tower.
    public void ExpectTheHumanToVisit(IEnumerable<Vector2> spots)
    {
        humansNextSpots.Clear();
        foreach (var spot in spots) humansNextSpots.Enqueue(spot);
    }

    public void KeepWatchingTheHuman()
    {
        if (!Alive(Human)) return;
        NoteWhereTheHumanIs();
        if (KeepsToItsGuess(Position(Human), world.Events.Elapsed)) return;
        Observe();
        foreach (var role in InTheHumansWay(except: []))
            GetOutOfTheHumansWay(role);
    }

    // The human's seat only follows the order under the debug bot driver, as the soak runs it.
    private void TheHumanIsSentTo(Vector2 destination)
    {
        var reached = humansNextSpots.ToList().FindIndex(spot => Vector2.Distance(spot, destination) < SameSpot);
        for (var i = 0; i <= reached; i++) humansNextSpots.Dequeue();
        HeadFor(destination);
        world.Party.Get(Human)!.MoveTo(new Vector3(destination.X, 0f, destination.Y), RunSpeed);
    }

    private void HeadFor(Vector2 destination)
    {
        var from = Position(Human);
        humanDestination = destination;
        humanSetOffFrom = from;
        humanReachedIt = Vector2.Distance(from, destination) <= OffPlanDrift;
        plans[Human] = LikelyWalks(from, destination);
    }

    // A human that stops near its spot, having walked there, has taken its own spot instead.
    private void NoteWhereTheHumanIs()
    {
        var t = world.Events.Elapsed;
        var at = Position(Human);
        if (humanSeen is { } seen && t > seen.Time) humanVelocity = (at - seen.At) / (t - seen.Time);
        humanSeen = (t, at);
        if (humanDestination is not { } destination || humanReachedIt) return;
        if (Vector2.Distance(at, destination) <= OffPlanDrift)
            humanReachedIt = true;
        else if (humanVelocity.Length() < HumanMoving && Vector2.Distance(at, destination) <= HumanSettlesWithin
                 && Vector2.Distance(at, humanSetOffFrom) > OffPlanDrift)
        {
            humanDestination = at;
            humanReachedIt = true;
            StandsAt(at, t);
        }
    }

    // Off its guess, or changing pace: setting off, stopping, breaking into a sprint.
    private bool KeepsToItsGuess(Vector2 at, float t)
        => plans[Human] is { } guess && DistanceTo(at, guess, t) <= HumanOffItsWalk && PaceOf(humanVelocity) == guessedPace;

    private static float PaceOf(Vector2 velocity)
        => velocity.Length() < HumanMoving ? 0f : velocity.Length() > HumanSprinting ? SprintSpeed : RunSpeed;

    private void StandsAt(Vector2 at, float since)
    {
        plans[Human] = [Path.Standing(at, since)];
        guessedPace = 0f;
    }

    private void GuessAgainIfTheHumanStrayed(Vector2 at)
    {
        if (KeepsToItsGuess(at, now)) return;
        if (humanDestination is not { } destination)
        {
            StandsAt(at, now);
            return;
        }
        if (humanReachedIt && Vector2.Distance(at, destination) > OffPlanDrift && humansNextSpots.TryDequeue(out var next))
        {
            DiagnosticLog.Info($"[P3 Hello World] The human left for ({next.X:F1},{next.Y:F1}) before its cue.");
            HeadFor(next);
            return;
        }
        if (Vector2.Distance(at, destination) <= OffPlanDrift) StandsAt(at, now);
        else plans[Human] = LikelyWalks(at, destination);
    }

    // Standing, it may set off now or up to a second late, or sprint. Walking round the boss, it
    // keeps to its ring until its spot's bearing or a little before. Walking any other way, it may
    // keep its heading a while before turning for its spot.
    private Path[] LikelyWalks(Vector2 from, Vector2 to)
    {
        guessedPace = PaceOf(humanVelocity);
        if (guessedPace == 0f)
            return [Path.Walk(from, now, now, [to], SprintSpeed),
                    .. Enumerable.Range(0, HumanStarts).Select(i => Path.Walk(from, now, now + HumanLatestStart * i / (HumanStarts - 1), [to], RunSpeed))];
        var heading = Vector2.Normalize(humanVelocity);
        if (WhichWayRoundTheBoss(from, to, heading) is { } direction)
            return HumanLeavesItsRingEarlierBy.Select(cut => Path.Walk(from, now, now, RoundItsRing(from, to, direction, cut), guessedPace)).ToArray();
        var farthest = HumanTurnsWithin * Vector2.Distance(from, to);
        return HumanKeepsItsHeadingFor
               .Select(k => Path.Walk(from, now, now, k == 0f ? [to] : [from + heading * Math.Min(guessedPace * k, farthest), to], guessedPace))
               .ToArray();
    }

    private static float? WhichWayRoundTheBoss(Vector2 from, Vector2 to, Vector2 heading)
    {
        var round = Vector2.Dot(heading, Heading(Bearing(from) + 90f));
        if (from.Length() < HumanWalksRoundFrom || MathF.Abs(round) < HumanWalksRoundWithin) return null;
        var direction = MathF.Sign(round);
        return Wrap360(direction * (Bearing(to) - Bearing(from))) <= HumanWalksRoundAtMost ? direction : null;
    }

    private static Vector2[] RoundItsRing(Vector2 from, Vector2 to, float direction, float leavesEarlierBy)
    {
        var bearing = Bearing(from);
        var arc = Math.Max(0f, Wrap360(direction * (Bearing(to) - bearing)) - leavesEarlierBy);
        var steps = Math.Max(1, (int)MathF.Ceiling(arc / RingStepDegrees));
        return [.. Enumerable.Range(1, steps).Select(i => OnRing(bearing + direction * arc * i / steps, from.Length())), to];
    }

    private List<int> InTheHumansWay(IEnumerable<int> except)
    {
        if (!Alive(Human) || plans[Human] is not { } human || human.Max(p => p.Arrival) <= now) return [];
        var skip = except.ToHashSet();
        return Enumerable.Range(0, 8)
                         .Where(r => r != Human && !skip.Contains(r) && Alive(r) && canSwap[r, Human])
                         .Where(r => !PassTheirRot(r, Human) && ClosestApproach(plans[r]!, human) < SwapClearance)
                         .ToList();
    }

    // A bot on its way goes on round the human if it can, else stops and steps aside like one at
    // rest, round where it is, and carries on once it can.
    private void GetOutOfTheHumansWay(int role)
    {
        var plan = plans[role]![0];
        var walking = steppedAsideRound[role] is null && plan.Arrival > now;
        var onward = walking ? SearchRoutes(role, plan.Destination, lanes[role], Math.Max(deadlines[role], plan.Arrival)) : default;
        if (walking && onward.Shortfall <= 0f)
        {
            Go(role, onward.Route, lanes[role]);
            return;
        }
        var round = steppedAsideRound[role] ?? (walking ? Position(role) : homes[role] ?? Position(role));
        var human = plans[Human]!;
        var passed = human.Max(p => p.Arrival);
        var holds = passed + StepBackAfter >= HoldsItsSpotFrom;
        var aside = holds ? null : StepAsideSpots(role, round, human, passed)
                                   .Select(spot => ClearWalk(role, spot))
                                   .FirstOrDefault(route => route is not null);
        if (aside is null)
        {
            if (walking) Go(role, onward.Route, lanes[role]);
            if (!holds) WarnOncePerOrder(role, "has nowhere to step aside from the human");
            return;
        }
        DiagnosticLog.Info($"[P3 Hello World] {(PartyRole)role} steps aside from the human to ({aside.Destination.X:F1},{aside.Destination.Y:F1}).");
        steppedAsideRound[role] = round;
        Go(role, aside, lanes[role]);
        var version = versions[role];
        world.Events.Add(Math.Min(passed + StepBackAfter, aside.Arrival + StepBackRetry) - now, () => StepBack(role, version));
    }

    // Until a new order comes, it keeps trying to get back to its spot, and goes back anyway in time
    // for a resolve.
    private void StepBack(int role, int version)
    {
        if (versions[role] != version || !Alive(role) || homes[role] is not { } home) return;
        Observe();
        var deadline = Math.Min(Math.Max(deadlines[role], now + Waits[^1] + 1f), HoldsItsSpotFrom);
        var route = ClearRoute(role, home, lanes[role], deadline);
        if (route is null && now + StepBackRetry >= HoldsItsSpotFrom) route = BestRoute(role, home, lanes[role], HoldsItsSpotFrom);
        if (route is not null)
        {
            steppedAsideRound[role] = null;
            Go(role, route, lanes[role]);
        }
        else world.Events.Add(StepBackRetry, () => StepBack(role, version));
    }

    // The nearest spots round it that the human's walks pass far enough from.
    private IEnumerable<Vector2> StepAsideSpots(int role, Vector2 round, Path[] human, float passed)
    {
        var at = Position(role);
        return StepAsideDistances
               .SelectMany(k => Enumerable.Range(0, StepAsideDirections).Select(i => round + k * Heading(360f * i / StepAsideDirections)))
               .Prepend(round)
               .Where(spot => spot.Length() < TopConstants.Geometry.ArenaRadius - 1f)
               .OrderBy(spot => Vector2.Distance(spot, at))
               .Where(spot => ClosestApproach(spot, human, passed) >= StepAsideClearance);
    }

    private Path? ClearWalk(int role, Vector2 to)
        => Speeds.Select(speed => Path.Walk(Position(role), now, now, [to], speed))
                 .FirstOrDefault(route => Shortfall(role, route, giveUpAbove: 0f, strict: true) <= 0f);
}
