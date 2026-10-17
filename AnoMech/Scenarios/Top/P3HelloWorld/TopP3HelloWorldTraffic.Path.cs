using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

internal sealed partial class TopP3HelloWorldTraffic
{
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

    // A fan holds one player's guesses, each a little later or a little further round than the
    // last, so between two of them the player is somewhere on the segment.
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
