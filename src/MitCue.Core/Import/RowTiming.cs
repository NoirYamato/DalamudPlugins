using MitCue.Core.Fights;

namespace MitCue.Core.Import;

/// <summary>A time written in a row's time column, matched to one hit of the row's mechanic.</summary>
/// <param name="SheetTime">The sheet time (seconds of the sheet's phase clock).</param>
/// <param name="HitIndex">The hit of the mechanic it marks (0-based).</param>
/// <param name="Timeline">The timeline time the sheet time stands for: the hit plus the sheet's row offset (δ) for the first hit.</param>
internal sealed record RowRef(double SheetTime, int HitIndex, double Timeline);

/// <summary>
/// Row-relative timing (docs/DESIGN.md §6 steps 6–7, docs/research/mechanics.md §4.3): the times of a row are matched to
/// the hits of its mechanic, so that an action time converts to timeline time without a phase zero point, and the
/// matched pairs give the per-phase calibration.
/// </summary>
internal static class RowTiming
{
    /// <summary>How far (seconds) a later time of a row may be from the hit its spacing predicts and still mark that hit.</summary>
    public const double MatchTolerance = 1.5;

    /// <summary>
    /// The row's times matched to hits. The first time marks the first hit (shifted by <paramref name="rowOffset"/>, the
    /// sheet's δ for this mechanic); each further time (list members, a range end, and for a range the times its label lists
    /// inside the range, as in "Wave Cannon ×6 … at 0:15 / 0:25 / 0:35 …") marks the next hit whose time matches its distance
    /// from the first within <see cref="MatchTolerance"/>, or nothing (a range end that is no hit, such as the rot
    /// explosions of the Hello World rounds).
    /// </summary>
    public static IReadOnlyList<RowRef> Align(RowLayout row, Mechanic mechanic, double rowOffset)
    {
        if (row.Time is not { } time)
            return [];

        var times = new List<double>(time.All);
        if (time.End is { } end && row.Label is { } label)
        {
            foreach (var m in ParsedTime.FindAll(label))
            {
                foreach (var t in m.Time.All)
                {
                    if (t >= time.Seconds - 0.01 && t <= end + 0.01)
                        times.Add(t);
                }
            }
        }

        times = times.Order().ToList();
        var distinct = new List<double>();
        foreach (var t in times)
        {
            if (distinct.Count == 0 || t - distinct[^1] > 0.01)
                distinct.Add(t);
        }

        var hits = mechanic.Hits.Count > 0 ? mechanic.Hits : [mechanic.Hit];
        var first = new RowRef(distinct[0], 0, hits[0] + rowOffset);
        var refs = new List<RowRef> { first };
        var last = 0;
        for (var i = 1; i < distinct.Count; i++)
        {
            var predicted = first.Timeline + (distinct[i] - first.SheetTime);
            var best = -1;
            var bestError = double.MaxValue;
            for (var k = last + 1; k < hits.Count; k++)
            {
                var error = Math.Abs(hits[k] - predicted);
                if (error < bestError)
                {
                    best = k;
                    bestError = error;
                }
            }

            if (best < 0 || bestError > MatchTolerance)
                continue;
            refs.Add(new RowRef(distinct[i], best, hits[best]));
            last = best;
        }

        return refs;
    }

    /// <summary>
    /// The timeline time of a sheet time on a row: through the row's nearest matched time when it has any (exact to the
    /// sheet's rounding and immune to drift), else through the phase zero point; null when neither is known.
    /// </summary>
    public static double? ToTimeline(double sheetTime, IReadOnlyList<RowRef> refs, double? phaseZero)
    {
        if (refs.Count > 0)
        {
            var nearest = refs.OrderBy(r => Math.Abs(r.SheetTime - sheetTime)).ThenBy(r => r.SheetTime).First();
            return nearest.Timeline + (sheetTime - nearest.SheetTime);
        }

        return phaseZero is { } zero ? zero + sheetTime : null;
    }

    /// <summary>
    /// The hit a press time belongs to: for actions used before the damage the first hit at or after the press, for
    /// actions used after it (heals) the last hit at or before the press.
    /// </summary>
    public static int HitFor(Mechanic mechanic, double press, bool afterHit)
    {
        const double slack = 0.05;
        var hits = mechanic.Hits.Count > 0 ? mechanic.Hits : [mechanic.Hit];
        if (afterHit)
        {
            for (var k = hits.Count - 1; k >= 0; k--)
            {
                if (hits[k] <= press + slack)
                    return k;
            }

            return 0;
        }

        for (var k = 0; k < hits.Count; k++)
        {
            if (hits[k] >= press - slack)
                return k;
        }

        return hits.Count - 1;
    }

    /// <summary>The median of the values (the mean of the middle two for an even count).</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
