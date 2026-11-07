using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Scenarios.Top.P6CosmoDive2;

public sealed class TopP6CosmoDive2State
{
    // The compass point the first exaflare starts on: 0 north, then clockwise every 45°. The other three start on the
    // next points the way they turn.
    public int FirstExaflare { get; }
    public bool Clockwise { get; }

    // Seconds between an exaflare's explosions.
    public float ExaflareStep { get; }

    public TopP6CosmoDive2State(Rng rng, TopP6CosmoDive2StateOverrides overrides)
    {
        (FirstExaflare, Clockwise) = TopP6UnlimitedWaveCannon.Roll(rng, overrides.FirstExaflare, overrides.Clockwise);
        ExaflareStep = overrides.ExaflareStep ?? P6Duration.ExaflareStep;
    }
}
