using System.Numerics;

namespace AnoMech.Tests;

// Where each actor stood over a pull, as the logging client saw it. Every line that carries a position adds a sample and
// a time between two samples reads the straight line between them. Other players reach the client late and smoothed, so
// their positions trail the server's by a few tenths of a second.
internal sealed class ActPositions
{
    private readonly Dictionary<uint, List<Sample>> tracks = [];

    private readonly record struct Sample(DateTimeOffset Time, Vector2 Position, float? Heading);

    public ActPositions(IEnumerable<ActLine> lines)
    {
        // A 261 line carries only the coordinates that changed; the other is the last one it carried.
        var memory = new Dictionary<uint, (float? X, float? Y)>();
        foreach (var line in lines)
            switch (line.Type)
            {
                case ActLineType.AddCombatant:
                    Add(line.Hex(2), line.Time, line.Point(17), line.Float(20));
                    break;
                case ActLineType.Ability or ActLineType.AoeAbility:
                    Add(line.Hex(6), line.Time, line.Point(30), line.Float(33));
                    Add(line.Hex(2), line.Time, line.Point(40), line.Float(43));
                    break;
                case ActLineType.EffectResult or ActLineType.StatusList:
                    Add(line.Hex(2), line.Time, line.Point(11), line.Float(14));
                    break;
                case ActLineType.UpdateHp:
                    Add(line.Hex(2), line.Time, line.Point(10), line.Float(13));
                    break;
                case ActLineType.ActorMove or ActLineType.ActorSetPosition:
                    Add(line.Hex(2), line.Time, line.Point(6), line.Float(3));
                    break;
                case ActLineType.CombatantMemory when line.Text(2) is "Add" or "Change" && line.Hex(3) is { } actor:
                    var (newX, newY) = (line.PairFloat("PosX"), line.PairFloat("PosY"));
                    if (newX is null && newY is null) break;
                    var (lastX, lastY) = memory.GetValueOrDefault(actor);
                    var (x, y) = (newX ?? lastX, newY ?? lastY);
                    memory[actor] = (x, y);
                    if (x is { } knownX && y is { } knownY)
                        Add(actor, line.Time, new Vector2(knownX, knownY), line.PairFloat("Heading"));
                    break;
            }
        foreach (var track in tracks.Values)
            track.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    public bool Knows(uint actor) => tracks.ContainsKey(actor);

    public ActPose? At(uint actor, DateTimeOffset time)
    {
        if (!tracks.TryGetValue(actor, out var track) || track.Count == 0) return null;
        var after = FirstAtOrAfter(track, time);
        if (after == 0) return Pose(track[0], track[0].Time - time);
        if (after == track.Count) return Pose(track[^1], time - track[^1].Time);
        var (a, b) = (track[after - 1], track[after]);
        var span = (b.Time - a.Time).TotalSeconds;
        var t = span <= 0 ? 1f : (float)((time - a.Time).TotalSeconds / span);
        var heading = HeadingNear(track, after, time);
        var gap = TimeSpan.FromTicks(Math.Min((time - a.Time).Ticks, (b.Time - time).Ticks));
        return new ActPose(Vector2.Lerp(a.Position, b.Position, t), heading, gap);
    }

    private void Add(uint? actor, DateTimeOffset time, Vector2? position, float? heading)
    {
        if (actor is not { } id || position is not { } at || id == ActorIds.None) return;
        if (!tracks.TryGetValue(id, out var track)) tracks[id] = track = [];
        track.Add(new Sample(time, at, heading));
    }

    private static ActPose Pose(Sample sample, TimeSpan gap) => new(sample.Position, sample.Heading, gap);

    // The heading of the last sample at or before the time that has one; a turn is not something to interpolate.
    private static float? HeadingNear(List<Sample> track, int after, DateTimeOffset time)
    {
        for (var i = after; i >= 0; i--)
            if (i < track.Count && track[i].Time <= time && track[i].Heading is { } heading)
                return heading;
        return null;
    }

    private static int FirstAtOrAfter(List<Sample> track, DateTimeOffset time)
    {
        var (low, high) = (0, track.Count);
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (track[mid].Time < time) low = mid + 1;
            else high = mid;
        }
        return low;
    }
}

// Gap is how far the time lies from the nearest sample: the further, the less the position can be trusted.
internal readonly record struct ActPose(Vector2 Position, float? Heading, TimeSpan Gap);
