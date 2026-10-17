using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

// Walks the LPDU bots between their spots without brushing a rot onto anyone. Each order is
// planned when it is given, from where all eight players stand and what they hold, against what
// everyone else is about to do: the other bots' routes, and for the human a straight walk to its
// own spot from the same cue, up to a second late. A bot takes the straight line when it keeps
// clear of everyone it could swap a rot with, else the earliest clear wait or detour: rot holders
// round the outside of the boss's hitbox, the defamation rings outside the stack rings, tether
// players through the middle. A bot standing in the human's way steps aside, then back.
internal sealed class TopP3HelloWorldTraffic(SimWorld world, Func<PartyRole, Vector2, bool> mayStepAsideTo)
{
    public enum Lane { Middle, StackRing, DefamationRing }

    // From From to Until, stay between Nearest and Farthest from the partner: a tether that must
    // hold, or must have broken by then.
    public readonly record struct Tether(PartyRole Partner, float Nearest, float Farthest, float From, float Until);

    public readonly record struct Order(Vector2 Destination, Lane Lane, Tether? Tether = null);

    private const float RunSpeed = 6.5f;
    private const float SwapClearance = 2f;
    private const float PassingARot = 1.5f;
    private const float ExplosionClearance = 6f;
    private const float ExplosionLead = 0.4f, ExplosionTail = 0.1f;
    private const float SettlingAfterArrival = 1f;
    private const float JitterRadius = 0.3f;
    private const float SampleSeconds = 0.05f;
    private const float HumanLatestStart = 1f;
    private const int HumanGuesses = 6;
    private const float OffPlanDrift = 1f;
    private const float StepAsideClearance = 2.6f;
    private static readonly float[] StepAsideDistances = [1f, 1.5f, 2f, 2.6f, 3.2f, 4f, 5f];
    private const int StepAsideDirections = 16;
    private const float StepBackAfter = 0.3f;

    // Seconds a route may arrive later and still be preferred: walking over sprinting, and the
    // lanes over any other one-corner detour.
    private const float SprintPenalty = 1.5f, OffLanePenalty = 4f;
    private const int MostRoutesTried = 800;

    private static readonly float[] StackRings = [13.5f, 15.5f], DefamationRings = [16.5f, 18.3f];
    private const float MiddleRadius = 4f;
    private const float RingStepDegrees = 20f;
    private static readonly float[] RingCuts = [0f, 15f, 30f, 45f];
    private static readonly float[] AnyCornerRadii = [3f, 6f, 9f, 11.5f, 14f, 16.5f, 18.5f];
    private static readonly float[] Waits = [0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f, 1.3f, 1.6f, 2f, 2.5f, 3f, 4f];
    private static readonly float[] Speeds = [RunSpeed, AiManager.SprintSpeed];

    private readonly Path[]?[] plans = new Path[]?[8];
    private readonly Lane[] lanes = new Lane[8];
    private readonly Tether?[] tethers = new Tether?[8];
    private readonly Path?[] straightGuesses = new Path?[8];
    private readonly int[] versions = new int[8];
    private readonly HashSet<int> unplanned = [];
    private readonly bool[,] canSwap = new bool[8, 8];
    private readonly List<(int Holder, float Time)> explosions = [];
    private float now;
    private Vector2? humanDestination;

    private int Human => (int)world.Party.PlayerRole;

    public void Send(IReadOnlyDictionary<PartyRole, Order> orders, float deadline)
    {
        Observe();
        if (orders.TryGetValue((PartyRole)Human, out var mine) && Alive(Human))
            ExpectTheHumanToWalkStraightTo(mine.Destination);

        var bots = orders.Where(o => (int)o.Key != Human && Alive((int)o.Key))
                         .Select(o => (Role: (int)o.Key, Order: o.Value with { Destination = Jittered(o.Value.Destination) }))
                         .OrderByDescending(o => Vector2.Distance(Position(o.Role), o.Order.Destination))
                         .ToList();
        foreach (var (role, order) in bots)
        {
            unplanned.Add(role);
            tethers[role] = order.Tether;
            straightGuesses[role] = Path.Walk(Position(role), now, now, [order.Destination], RunSpeed);
        }
        foreach (var role in InTheHumansWay(except: bots.Select(b => b.Role)))
            GetOutOfTheHumansWay(role);
        foreach (var (role, order) in bots)
        {
            unplanned.Remove(role);
            Go(role, BestRoute(role, order.Destination, order.Lane, deadline), order.Lane);
        }
    }

    // A human later than the guess, or walking another way, is guessed again from where it is, and
    // whoever is now in its way gets out of it.
    public void KeepWatchingTheHuman()
    {
        if (!Alive(Human) || humanDestination is not { } destination || plans[Human] is not { } guess) return;
        var at = Position(Human);
        if (DistanceTo(at, guess, world.Events.Elapsed) <= OffPlanDrift) return;
        Observe();
        if (Vector2.Distance(at, destination) > OffPlanDrift) ExpectTheHumanToWalkStraightTo(destination);
        foreach (var role in InTheHumansWay(except: []))
            GetOutOfTheHumansWay(role);
    }

    // The human's seat only follows the order under the debug bot driver, as the soak runs it.
    private void ExpectTheHumanToWalkStraightTo(Vector2 destination)
    {
        var from = Position(Human);
        humanDestination = destination;
        plans[Human] = Enumerable.Range(0, HumanGuesses)
                                 .Select(i => Path.Walk(from, now, now + HumanLatestStart * i / (HumanGuesses - 1), [destination], RunSpeed))
                                 .ToArray();
        world.Party.Get(Human)!.MoveTo(new Vector3(destination.X, 0f, destination.Y), RunSpeed);
    }

    private List<int> InTheHumansWay(IEnumerable<int> except)
    {
        if (!Alive(Human) || plans[Human] is not { } human || human[0].Arrival <= now) return [];
        var skip = except.ToHashSet();
        return Enumerable.Range(0, 8)
                         .Where(r => r != Human && !skip.Contains(r) && Alive(r) && canSwap[r, Human])
                         .Where(r => !Meet(plans[r]![0].Destination, human[0].Destination) && ClosestApproach(plans[r]!, human) < SwapClearance)
                         .ToList();
    }

    private void GetOutOfTheHumansWay(int role)
    {
        var plan = plans[role]![0];
        if (plan.Arrival > now)
        {
            Go(role, BestRoute(role, plan.Destination, lanes[role], plan.Arrival + Waits[^1]), lanes[role]);
            return;
        }
        var home = Position(role);
        var human = plans[Human]!;
        var passed = human.Max(p => p.Arrival);
        var aside = StepAsideSpots(home, human)
                    .Where(spot => mayStepAsideTo((PartyRole)role, spot))
                    .Select(spot => BestRoute(role, spot, lanes[role], passed, directOnly: true))
                    .FirstOrDefault(route => Shortfall(role, route) <= 0f);
        if (aside is null)
        {
            DiagnosticLog.Warn($"[P3 Hello World] {(PartyRole)role} has nowhere to step aside from the human.");
            return;
        }
        DiagnosticLog.Info($"[P3 Hello World] {(PartyRole)role} steps aside from the human to ({aside.Destination.X:F1},{aside.Destination.Y:F1}).");
        Go(role, aside, lanes[role]);
        var version = versions[role];
        world.Events.Add(passed + StepBackAfter - now, () => StepBack(role, home, version));
    }

    private void StepBack(int role, Vector2 home, int version)
    {
        if (versions[role] != version || !Alive(role)) return;
        Observe();
        Go(role, BestRoute(role, home, lanes[role], now + Waits[^1] + 1f), lanes[role]);
    }

    // The nearest spots round it that the human's walk passes far enough from.
    private IEnumerable<Vector2> StepAsideSpots(Vector2 from, Path[] human)
    {
        var passed = human.Max(p => p.Arrival);
        return StepAsideDistances
               .SelectMany(k => Enumerable.Range(0, StepAsideDirections).Select(i => from + k * Heading(360f * i / StepAsideDirections)))
               .Where(spot => spot.Length() < TopConstants.Geometry.ArenaRadius - 1f && ClosestApproach(spot, human, passed) >= StepAsideClearance)
               .OrderBy(spot => Vector2.Distance(spot, from));
    }

    private Path BestRoute(int role, Vector2 to, Lane lane, float deadline, bool directOnly = false)
    {
        var from = Position(role);
        var straight = Path.Walk(from, now, now, [to], RunSpeed);
        if (straight.Arrival <= deadline && Shortfall(role, straight) <= 0f) return straight;

        var candidates = (directOnly ? [new Shape([to], 0f)] : Shapes(from, to, lane))
            .SelectMany(shape => Waits.SelectMany(wait => Speeds.Select(speed =>
            {
                var arrival = now + wait + Length(from, shape.Via) / speed;
                return (shape.Via, Wait: wait, Speed: speed, Arrival: arrival,
                        Score: arrival + shape.Penalty + (speed > RunSpeed ? SprintPenalty : 0f));
            })))
            .OrderBy(c => c.Score)
            .ToList();
        Path? least = null;
        var leastShortfall = float.MaxValue;
        foreach (var c in candidates.Where(c => c.Arrival <= deadline).Take(MostRoutesTried))
        {
            var route = Path.Walk(from, now, now + c.Wait, c.Via, c.Speed);
            var shortfall = Shortfall(role, route);
            if (shortfall <= 0f) return route;
            if (shortfall < leastShortfall) (least, leastShortfall) = (route, shortfall);
        }
        foreach (var c in candidates.Where(c => c.Arrival > deadline).Take(MostRoutesTried))
        {
            var route = Path.Walk(from, now, now + c.Wait, c.Via, c.Speed);
            if (Shortfall(role, route) <= 0f) return route;
        }
        if (least is null)
        {
            var fastest = candidates.MinBy(c => c.Arrival);
            least = Path.Walk(from, now, now + fastest.Wait, fastest.Via, fastest.Speed);
            leastShortfall = Shortfall(role, least);
        }
        DiagnosticLog.Warn($"[P3 Hello World] {(PartyRole)role} has no clear way to ({to.X:F1},{to.Y:F1}); {leastShortfall:F2} y short.");
        return least;
    }

    private readonly record struct Shape(Vector2[] Via, float Penalty);

    private static IEnumerable<Shape> Shapes(Vector2 from, Vector2 to, Lane lane)
    {
        yield return new Shape([to], 0f);
        var rings = lane switch { Lane.StackRing => StackRings, Lane.DefamationRing => DefamationRings, _ => [] };
        foreach (var via in rings.Length > 0 ? rings.SelectMany(r => AroundTheRing(from, to, r)) : ThroughTheMiddle(to))
            yield return new Shape(via, 0f);
        foreach (var r in AnyCornerRadii)
        for (var bearing = 0f; bearing < 360f; bearing += 15f)
            yield return new Shape([OnRing(bearing, r), to], OffLanePenalty);
    }

    // Out onto the ring, either way round it, and in again: cutting a corner at either end saves
    // walking the ring's full arc.
    private static IEnumerable<Vector2[]> AroundTheRing(Vector2 from, Vector2 to, float radius)
    {
        var start = Bearing(from);
        var end = Bearing(to);
        foreach (var direction in new[] { 1f, -1f })
        {
            var span = Wrap360(direction * (end - start));
            foreach (var cutIn in RingCuts)
            foreach (var cutOut in RingCuts)
            {
                var arc = span - cutIn - cutOut;
                if (arc < 0f) continue;
                var steps = Math.Max(1, (int)MathF.Ceiling(arc / RingStepDegrees));
                yield return Enumerable.Range(0, steps + 1)
                                       .Select(i => OnRing(start + direction * (cutIn + arc * i / steps), radius))
                                       .Append(to)
                                       .ToArray();
            }
        }
    }

    private static IEnumerable<Vector2[]> ThroughTheMiddle(Vector2 to)
    {
        yield return [Vector2.Zero, to];
        for (var bearing = 0f; bearing < 360f; bearing += 30f)
            yield return [OnRing(bearing, MiddleRadius), to];
    }

    // How far a route falls short of the room it owes everyone it passes, summed over them: swap
    // clearance from whoever it could swap a rot with, explosion clearance around a rot going off
    // while it walks or settles, and its tether's rule.
    private float Shortfall(int role, Path route)
    {
        var others = Enumerable.Range(0, 8).Where(q => q != role && Alive(q) && !unplanned.Contains(q) && plans[q] is not null).ToList();
        var swappers = others.Where(q => canSwap[role, q] && !Meet(route.Destination, plans[q]![^1].Destination))
                             .Select(q => (Who: q, Required: Math.Min(SwapClearance, Vector2.Distance(route.Destination, plans[q]![^1].Destination) - 0.05f)))
                             .ToList();
        var end = others.Select(q => plans[q]!.Max(p => p.Arrival)).Append(route.Arrival).Max() + SampleSeconds;
        var blasts = explosions.Where(e => e.Time - ExplosionLead <= route.Arrival + SettlingAfterArrival && e.Time + ExplosionTail >= now)
                               .Where(e => e.Holder == role || others.Contains(e.Holder))
                               .ToList();
        if (blasts.Count > 0) end = Math.Max(end, blasts.Max(e => e.Time + ExplosionTail));

        var shortfall = new float[8];
        for (var t = now; t <= end; t += SampleSeconds)
        {
            var at = route.At(t);
            foreach (var (q, required) in swappers)
                shortfall[q] = Math.Max(shortfall[q], required - DistanceTo(at, plans[q]!, t));
            foreach (var (holder, time) in blasts)
            {
                if (t < time - ExplosionLead || t > time + ExplosionTail) continue;
                foreach (var q in holder == role ? others : [holder])
                    shortfall[q] = Math.Max(shortfall[q], ExplosionClearance - DistanceTo(at, plans[q]!, t));
            }
        }
        var tether = tethers[role] is { } rule && rule.Until >= now && Alive((int)rule.Partner) ? TetherShortfall(route, rule) : 0f;
        return shortfall.Sum() + tether;
    }

    private float TetherShortfall(Path route, Tether rule)
    {
        var partner = (int)rule.Partner;
        var fan = unplanned.Contains(partner) ? [straightGuesses[partner]!] : plans[partner]!;
        var shortfall = 0f;
        for (var t = Math.Max(now, rule.From); ; t = Math.Min(t + SampleSeconds, rule.Until))
        {
            var at = route.At(t);
            var nearest = DistanceTo(at, fan, t);
            var farthest = fan.Max(p => Vector2.Distance(at, p.At(t)));
            shortfall = Math.Max(shortfall, Math.Max(0f, rule.Nearest - nearest) + Math.Max(0f, farthest - rule.Farthest));
            if (t >= rule.Until) return shortfall;
        }
    }

    private void Go(int role, Path route, Lane lane)
    {
        if (route.Legs.Length > 1 || route.Legs.Length == 1 && (route.Legs[0].Depart > now + 0.01f || route.Legs[0].Speed > RunSpeed))
            DiagnosticLog.Info($"[P3 Hello World] {(PartyRole)role} leaves at {route.Legs[0].Depart:F2} via "
                               + string.Join(" ", route.Legs.Select(l => $"({l.To.X:F1},{l.To.Y:F1})"))
                               + $"{(route.Legs[0].Speed > RunSpeed ? " sprinting" : "")}, arriving {route.Arrival:F2}.");
        plans[role] = [route];
        lanes[role] = lane;
        var version = ++versions[role];
        var member = world.Party.Get(role)!;
        if (route.Legs.Length == 0 || route.Legs[0].Depart > now) member.StopMoving();
        for (var i = 0; i < route.Legs.Length; i++)
        {
            var (depart, to, speed) = route.Legs[i];
            var first = i == 0;
            void Walk()
            {
                if (versions[role] != version) return;
                if (first && speed > RunSpeed) (member as ISimPartyMember)?.UseSprint(route.Arrival - depart + 1f);
                member.MoveTo(new Vector3(to.X, 0f, to.Y), speed);
            }
            if (depart <= now) Walk();
            else world.Events.Add(depart - now, Walk);
        }
    }

    // A plan the player has strayed from (a death, a knockback, the human going its own way) is
    // replaced by where it stands.
    private void Observe()
    {
        now = world.Events.Elapsed;
        unplanned.Clear();
        explosions.Clear();
        for (var r = 0; r < 8; r++)
        {
            if (!Alive(r)) continue;
            var at = Position(r);
            if (plans[r] is not { } fan || DistanceTo(at, fan, now) > OffPlanDrift)
                plans[r] = [Path.Standing(at, now)];
            foreach (var colour in HelloWorldColour.Both)
                if (world.Party.Get(r)!.FindStatus(colour.RotStatusId) is { } rot)
                    explosions.Add((r, now + rot.RemainingTime));
        }
        for (var a = 0; a < 8; a++)
        for (var b = 0; b < 8; b++)
            canSwap[a, b] = a != b && Alive(a) && Alive(b) && HelloWorldColour.Both.Any(c => Passes(a, b, c) || Passes(b, a, c));
    }

    // Walking onto a rot holder to take its rot, as the passes do.
    private static bool Meet(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < PassingARot;

    private bool Passes(int holder, int other, HelloWorldColour colour)
    {
        var from = world.Party.Get(holder)!;
        var to = world.Party.Get(other)!;
        return from.HasStatus(colour.RotStatusId) && !to.HasStatus(colour.RotStatusId) && !to.HasStatus(colour.DebuggerStatusId);
    }

    private float ClosestApproach(Path[] a, Path[] b)
    {
        var end = Math.Max(a.Max(p => p.Arrival), b.Max(p => p.Arrival)) + SampleSeconds;
        var closest = float.MaxValue;
        for (var t = now; t <= end; t += SampleSeconds)
            foreach (var path in a)
                closest = Math.Min(closest, DistanceTo(path.At(t), b, t));
        return closest;
    }

    private float ClosestApproach(Vector2 spot, Path[] fan, float until)
    {
        var closest = float.MaxValue;
        for (var t = now; t <= until + SampleSeconds; t += SampleSeconds)
            closest = Math.Min(closest, DistanceTo(spot, fan, t));
        return closest;
    }

    // The human's guesses run along one line, so between two of them it is somewhere on the segment.
    private static float DistanceTo(Vector2 spot, Path[] fan, float t)
    {
        if (fan.Length == 1) return Vector2.Distance(spot, fan[0].At(t));
        var closest = float.MaxValue;
        var previous = fan[0].At(t);
        for (var i = 1; i < fan.Length; i++)
        {
            var next = fan[i].At(t);
            closest = Math.Min(closest, SegmentDistance(spot, previous, next));
            previous = next;
        }
        return closest;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-8f) return Vector2.Distance(p, a);
        return Vector2.Distance(p, a + ab * Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f));
    }

    // AiManager's jitter, from the same stream.
    private Vector2 Jittered(Vector2 spot)
    {
        var rng = world.Stream("ai-jitter");
        var theta = rng.NextDouble() * 2.0 * Math.PI;
        var r = JitterRadius * MathF.Sqrt((float)rng.NextDouble());
        return spot + new Vector2(r * MathF.Cos((float)theta), r * MathF.Sin((float)theta));
    }

    private bool Alive(int role) => world.Party.Get(role).IsAlive();

    private Vector2 Position(int role)
    {
        var p = world.Party.Get(role)!.Position;
        return new Vector2(p.X, p.Z);
    }

    private static float Length(Vector2 from, IReadOnlyList<Vector2> via)
    {
        var length = 0f;
        foreach (var point in via)
        {
            length += Vector2.Distance(from, point);
            from = point;
        }
        return length;
    }

    private static float Bearing(Vector2 p) => MathF.Atan2(p.X, -p.Y) * 180f / MathF.PI;

    private static float Wrap360(float degrees) => (degrees % 360f + 360f) % 360f;

    private static Vector2 Heading(float bearingDegrees) => OnRing(bearingDegrees, 1f);

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }

    // Piecewise-linear in time: standing at the first point until it sets off, then each leg at one speed.
    private sealed class Path
    {
        private readonly float[] times;
        private readonly Vector2[] points;

        private Path(float[] times, Vector2[] points, (float, Vector2, float)[] legs)
        {
            this.times = times;
            this.points = points;
            Legs = legs;
        }

        public (float Depart, Vector2 To, float Speed)[] Legs { get; }
        public float Arrival => times[^1];
        public Vector2 Destination => points[^1];

        public static Path Standing(Vector2 at, float since) => new([since], [at], []);

        public static Path Walk(Vector2 from, float since, float depart, IReadOnlyList<Vector2> via, float speed)
        {
            var times = new List<float> { since, depart };
            var points = new List<Vector2> { from, from };
            var legs = new List<(float, Vector2, float)>();
            foreach (var point in via)
            {
                legs.Add((times[^1], point, speed));
                times.Add(times[^1] + Vector2.Distance(points[^1], point) / speed);
                points.Add(point);
            }
            return new Path([.. times], [.. points], [.. legs]);
        }

        public Vector2 At(float t)
        {
            if (t <= times[0]) return points[0];
            for (var i = 1; i < times.Length; i++)
                if (t <= times[i])
                {
                    var span = times[i] - times[i - 1];
                    return span <= 1e-6f ? points[i] : Vector2.Lerp(points[i - 1], points[i], (t - times[i - 1]) / span);
                }
            return points[^1];
        }
    }
}
