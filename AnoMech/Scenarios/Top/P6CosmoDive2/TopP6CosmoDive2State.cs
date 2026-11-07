using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Scenarios.Top.P6CosmoDive2;

public sealed class TopP6CosmoDive2State
{
    private const int CompassPoints = 8;

    // The compass point the first exaflare starts on: 0 north, then clockwise every 45°. The other three start on the
    // next points the way they turn.
    public int FirstExaflare { get; }
    public bool Clockwise { get; }

    // Seconds between an exaflare's explosions.
    public float ExaflareStep { get; }

    public TopP6CosmoDive2State(Rng rng, TopP6CosmoDive2StateOverrides overrides)
    {
        // Both are drawn whether or not they are forced, so forcing one leaves the other as the seed had it.
        var firstExaflare = rng.Next(CompassPoints);
        var clockwise = rng.NextBool();
        FirstExaflare = overrides.FirstExaflare ?? firstExaflare;
        Clockwise = overrides.Clockwise ?? clockwise;
        ExaflareStep = overrides.ExaflareStep ?? P6Duration.ExaflareStep;
    }
}
