using System.Numerics;

namespace AnoMech.Tests;

// How the human of the Exaflares blocks' human suites curves a walk: one to two yalms off its line, to a random side,
// but never toward a puddle's circle it can see. A circle shows from its drop until it goes off, and the ones a walk
// starts in are the ones it leaves.
internal static class TopP6CurvingWalk
{
    private const float LeastCurve = 1f, MostCurve = 2f;
    private const float PuddleShownFor = 3f, PuddleRadius = 6f, CurveAwayWithin = 2f;

    // The walk's points after `from`, the last on `to`. `puddles` is every drop seen so far.
    public static Vector2[] Points(Vector2 from, Vector2 to, float now, Random rng, IEnumerable<(float Dropped, Vector2 At)> puddles)
    {
        var length = Vector2.Distance(from, to);
        if (length <= 1f) return [to];
        var bulge = Math.Min(LeastCurve + (MostCurve - LeastCurve) * (float)rng.NextDouble(), length / 4f);
        var normal = new Vector2(-(to - from).Y, (to - from).X) / length * (rng.Next(2) == 0 ? -1f : 1f);
        var ahead = puddles.Where(puddle => now < puddle.Dropped + PuddleShownFor && Vector2.Distance(from, puddle.At) > PuddleRadius)
                           .Select(puddle => puddle.At)
                           .ToList();
        var middle = (from + to) / 2f;
        var bulgingThisWay = Nearest(ahead, middle + normal * bulge);
        if (bulgingThisWay < PuddleRadius + CurveAwayWithin && Nearest(ahead, middle - normal * bulge) > bulgingThisWay)
            normal = -normal;
        return [.. Enumerable.Range(1, 16).Select(i => i / 16f).Select(u => from + (to - from) * u + normal * (bulge * 4f * u * (1f - u)))];
    }

    private static float Nearest(List<Vector2> puddles, Vector2 at)
        => puddles.Select(puddle => Vector2.Distance(at, puddle)).DefaultIfEmpty(float.MaxValue).Min();
}
