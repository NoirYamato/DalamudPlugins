using System.Numerics;
using AnoMech.Core.Game.Ai;

namespace AnoMech.Tests;

// How the human of the P6 and P5 Omega human suites walks a leg: at a run, straight or curving, or sprinting straight.
// A curving walk bulges one to two yalms off its line, a quarter of its length at most, to a random side, but never
// toward a puddle's circle it can see. A circle shows from its drop until it goes off, and the ones a walk starts in are
// the ones it leaves.
public static class TopP6HumanWalk
{
    public enum Walk { Straight, Curving, Sprinting }

    private const float RunSpeed = 6.5f;
    private const float LeastCurve = 1f, MostCurve = 2f;
    private const float PuddleShownFor = 3f, PuddleRadius = 6f, CurveAwayWithin = 2f;

    public static float Speed(Walk walk) => walk == Walk.Sprinting ? AiManager.SprintSpeed : RunSpeed;

    // The walk's points after `from`, the last on `to`. `puddles` is every drop seen so far.
    public static Vector2[] Points(Walk walk, Vector2 from, Vector2 to, Random rng, float now = 0f,
        IEnumerable<(float Dropped, Vector2 At)>? puddles = null)
    {
        var length = Vector2.Distance(from, to);
        if (walk != Walk.Curving || length <= 1f) return [to];
        var bulge = Math.Min(LeastCurve + (MostCurve - LeastCurve) * (float)rng.NextDouble(), length / 4f);
        var normal = new Vector2(-(to - from).Y, (to - from).X) / length * (rng.Next(2) == 0 ? -1f : 1f);
        var ahead = (puddles ?? []).Where(puddle => now < puddle.Dropped + PuddleShownFor && Vector2.Distance(from, puddle.At) > PuddleRadius)
                                   .Select(puddle => puddle.At)
                                   .ToList();
        var middle = (from + to) / 2f;
        var bulgingThisWay = Nearest(ahead, middle + normal * bulge);
        if (bulgingThisWay < PuddleRadius + CurveAwayWithin && Nearest(ahead, middle - normal * bulge) > bulgingThisWay)
            normal = -normal;
        return [.. Enumerable.Range(1, 16).Select(i => i / 16f).Select(u => from + (to - from) * u + normal * (bulge * 4f * u * (1f - u)))];
    }

    // Where a walk along `points` stands `walked` yalms in, the way it faces, and whether it has arrived.
    public static (Vector2 At, Vector2 Heading, bool Arrived) Along(Vector2[] points, float walked)
    {
        var heading = new Vector2(0f, 1f);
        for (var i = 1; i < points.Length; i++)
        {
            var leg = Vector2.Distance(points[i - 1], points[i]);
            if (leg > 1e-4f) heading = (points[i] - points[i - 1]) / leg;
            if (walked <= leg) return (leg < 1e-4f ? points[i] : Vector2.Lerp(points[i - 1], points[i], walked / leg), heading, false);
            walked -= leg;
        }
        return (points[^1], heading, true);
    }

    private static float Nearest(List<Vector2> puddles, Vector2 at)
        => puddles.Select(puddle => Vector2.Distance(at, puddle)).DefaultIfEmpty(float.MaxValue).Min();
}
