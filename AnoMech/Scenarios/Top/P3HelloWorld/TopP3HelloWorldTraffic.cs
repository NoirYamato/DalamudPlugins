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
// everyone else is about to do: the other bots' routes, and the human's likely walks (the .Human
// part). A bot takes the straight line when it keeps clear of everyone it could swap a rot with,
// else the earliest clear wait or detour: rot holders round the outside of the boss's hitbox, the
// defamation rings outside the stack rings, tether players through the middle.
internal sealed partial class TopP3HelloWorldTraffic(SimWorld world, IReadOnlyList<float> holdSpotsThrough)
{
    public enum Lane { Middle, StackRing, DefamationRing }

    // From From to Until, stay between Nearest and Farthest from the partner: a tether that must
    // hold, or must have broken by then.
    public readonly record struct Tether(PartyRole Partner, float Nearest, float Farthest, float From, float Until);

    // PassesWith: the order meets that player to hand a rot over, wherever it is guessed to go.
    public readonly record struct Order(Vector2 Destination, Lane Lane, Tether? Tether = null, PartyRole? PassesWith = null);

    private const float RunSpeed = 6.5f, SprintSpeed = AiManager.SprintSpeed;
    private const float SwapClearance = 2f;
    private const float ExplosionClearance = 6f;
    private const float ExplosionLead = 0.4f, ExplosionTail = 0.1f;
    private const float SettlingAfterArrival = 1f;
    private const float JitterRadius = 0.3f;
    private const float SampleSeconds = 0.05f;
    private const float OffPlanDrift = 1f;
    private const float HoldSpotsAhead = 1.5f;

    // Seconds a route may arrive later and still be preferred: walking over sprinting, and the
    // lanes over any other one-corner detour.
    private const float SprintPenalty = 1.5f, OffLanePenalty = 4f;
    private const int MostRoutesTried = 400;
    private const float MayArriveLateBy = 1.5f;

    private static readonly float[] StackRings = [13.5f, 15.5f], DefamationRings = [16.5f, 18.3f];
    private const float MiddleRadius = 4f;
    private const float RingStepDegrees = 20f;
    private static readonly float[] RingCuts = [0f, 15f, 30f, 45f];
    private static readonly float[] AnyCornerRadii = [3f, 6f, 9f, 11.5f, 14f, 16.5f, 18.5f];
    private static readonly float[] Waits = [0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f, 1.3f, 1.6f, 2f, 2.5f, 3f, 4f];
    private static readonly float[] Speeds = [RunSpeed, SprintSpeed];

    private readonly Path[]?[] plans = new Path[]?[8];
    private readonly Lane[] lanes = new Lane[8];
    private readonly Tether?[] tethers = new Tether?[8];
    private readonly int?[] passesWith = new int?[8];
    private readonly Vector2?[] homes = new Vector2?[8];
    private readonly float[] deadlines = new float[8];
    private readonly Path?[] straightGuesses = new Path?[8];
    private readonly int[] versions = new int[8];
    private readonly int[] ordersGiven = new int[8], warnedOnOrder = new int[8];
    private readonly HashSet<int> unplanned = [];
    private readonly bool[,] canSwap = new bool[8, 8];
    private readonly List<(int Holder, float Time)> explosions = [];
    private float now;

    public void Send(IReadOnlyDictionary<PartyRole, Order> orders, float deadline)
    {
        Observe();
        if (orders.TryGetValue((PartyRole)Human, out var mine) && Alive(Human))
        {
            passesWith[Human] = (int?)mine.PassesWith;
            TheHumanIsSentTo(mine.Destination);
        }

        var bots = orders.Where(o => (int)o.Key != Human && Alive((int)o.Key))
                         .Select(o => (Role: (int)o.Key, Order: o.Value with { Destination = Jittered(o.Value.Destination) }))
                         .OrderByDescending(o => Vector2.Distance(Position(o.Role), o.Order.Destination))
                         .ToList();
        foreach (var (role, order) in bots)
        {
            unplanned.Add(role);
            tethers[role] = order.Tether;
            passesWith[role] = (int?)order.PassesWith;
            homes[role] = order.Destination;
            deadlines[role] = deadline;
            steppedAsideRound[role] = null;
            ordersGiven[role]++;
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

    private Path BestRoute(int role, Vector2 to, Lane lane, float deadline)
    {
        var (route, shortfall) = SearchRoutes(role, to, lane, deadline);
        if (shortfall > 0f) WarnOncePerOrder(role, $"has no clear way to ({to.X:F1},{to.Y:F1}); {shortfall:F2} y short");
        return route;
    }

    private Path? ClearRoute(int role, Vector2 to, Lane lane, float deadline)
        => SearchRoutes(role, to, lane, deadline) is ({ } route, <= 0f) ? route : null;

    // The earliest clear route by the deadline, else the earliest clear one a little after it, else the
    // one that falls least short.
    private (Path Route, float Shortfall) SearchRoutes(int role, Vector2 to, Lane lane, float deadline)
    {
        var from = Position(role);
        var straight = Path.Walk(from, now, now, [to], RunSpeed);
        if (straight.Arrival <= deadline && Shortfall(role, straight, giveUpAbove: 0f) <= 0f) return (straight, 0f);

        var candidates = Shapes(from, to, lane)
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
            var shortfall = Shortfall(role, route, giveUpAbove: leastShortfall);
            if (shortfall <= 0f) return (route, 0f);
            if (shortfall < leastShortfall) (least, leastShortfall) = (route, shortfall);
        }
        foreach (var c in candidates.Where(c => c.Arrival > deadline && c.Arrival <= deadline + MayArriveLateBy).Take(MostRoutesTried))
        {
            var route = Path.Walk(from, now, now + c.Wait, c.Via, c.Speed);
            if (Shortfall(role, route, giveUpAbove: 0f) <= 0f) return (route, 0f);
        }
        if (least is null)
        {
            var fastest = candidates.MinBy(c => c.Arrival);
            least = Path.Walk(from, now, now + fastest.Wait, fastest.Via, fastest.Speed);
            leastShortfall = Shortfall(role, least);
        }
        return (least, leastShortfall);
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
    // while it walks or settles, and its tether's rule. Two spots the strat puts closer than the
    // swap clearance only owe that gap, unless strict, and a pair already closer only owes not
    // closing in. Stops counting once past giveUpAbove.
    private float Shortfall(int role, Path route, float giveUpAbove = float.MaxValue, bool strict = false)
    {
        var others = Enumerable.Range(0, 8).Where(q => q != role && Alive(q) && !unplanned.Contains(q) && plans[q] is not null).ToList();
        var swappers = others.Where(q => canSwap[role, q] && !PassTheirRot(role, q))
                             .Select(q => (Who: q, Required: SwapClearanceOwed(route, q, strict)))
                             .ToList();
        var end = others.Select(q => plans[q]!.Max(p => p.Arrival)).Append(route.Arrival).Max() + SampleSeconds;
        var blasts = explosions.Where(e => e.Time - ExplosionLead <= route.Arrival + SettlingAfterArrival && e.Time + ExplosionTail >= now)
                               .Where(e => e.Holder == role || others.Contains(e.Holder))
                               .ToList();
        if (blasts.Count > 0) end = Math.Max(end, blasts.Max(e => e.Time + ExplosionTail));

        var shortfall = new float[8];
        var total = tethers[role] is { } rule && rule.Until >= now && Alive((int)rule.Partner) ? TetherShortfall(route, rule) : 0f;
        void Owe(int q, float missing)
        {
            if (missing <= shortfall[q]) return;
            total += missing - shortfall[q];
            shortfall[q] = missing;
        }
        for (var t = now; t <= end && total <= giveUpAbove; t += SampleSeconds)
        {
            var at = route.At(t);
            foreach (var (q, required) in swappers)
                Owe(q, required - DistanceTo(at, plans[q]!, t));
            foreach (var (holder, time) in blasts)
            {
                if (t < time - ExplosionLead || t > time + ExplosionTail) continue;
                foreach (var q in holder == role ? others : [holder])
                    Owe(q, ExplosionClearance - DistanceTo(at, plans[q]!, t));
            }
        }
        return total;
    }

    private float SwapClearanceOwed(Path route, int other, bool strict)
    {
        var designed = strict ? SwapClearance : Vector2.Distance(route.Destination, plans[other]![^1].Destination) - 0.05f;
        var already = DistanceTo(route.At(now), plans[other]!, now) - 0.05f;
        return Math.Min(SwapClearance, Math.Min(designed, already));
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

    // A bot that has strayed from its plan (a death, a knockback) is replaced by where it stands;
    // the human is guessed again.
    private void Observe()
    {
        now = world.Events.Elapsed;
        unplanned.Clear();
        explosions.Clear();
        for (var r = 0; r < 8; r++)
        {
            if (!Alive(r)) continue;
            var at = Position(r);
            if (r == Human) GuessAgainIfTheHumanStrayed(at);
            else if (plans[r] is not { } fan || DistanceTo(at, fan, now) > OffPlanDrift)
                plans[r] = [Path.Standing(at, now)];
            foreach (var colour in HelloWorldColour.Both)
                if (world.Party.Get(r)!.FindStatus(colour.RotStatusId) is { } rot)
                    explosions.Add((r, now + rot.RemainingTime));
        }
        for (var a = 0; a < 8; a++)
        for (var b = 0; b < 8; b++)
            canSwap[a, b] = a != b && Alive(a) && Alive(b) && HelloWorldColour.Both.Any(c => Passes(a, b, c) || Passes(b, a, c));
    }

    // The two meet at a pass, one walking onto the other to take its rot.
    private bool PassTheirRot(int role, int with) => passesWith[role] == with || passesWith[with] == role;

    private bool Passes(int holder, int other, HelloWorldColour colour)
    {
        var from = world.Party.Get(holder)!;
        var to = world.Party.Get(other)!;
        return from.HasStatus(colour.RotStatusId) && !to.HasStatus(colour.RotStatusId) && !to.HasStatus(colour.DebuggerStatusId);
    }

    private void WarnOncePerOrder(int role, string problem)
    {
        if (warnedOnOrder[role] == ordersGiven[role]) return;
        warnedOnOrder[role] = ordersGiven[role];
        DiagnosticLog.Warn($"[P3 Hello World] {(PartyRole)role} {problem}.");
    }

    // From a little before each resolve until it goes off, every bot stands where the resolve needs
    // it: in its tower, in its stack, clear of the towers it must not soak.
    private float HoldsItsSpotFrom
        => holdSpotsThrough.Where(resolve => resolve >= now).DefaultIfEmpty(float.PositiveInfinity).Min() - HoldSpotsAhead;

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
}
