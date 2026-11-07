namespace AnoMech.Scenarios.Top.P6CosmoDive2;

// User-forced rolls for TopP6CosmoDive2State; null leaves a field randomized.
public sealed class TopP6CosmoDive2StateOverrides
{
    // The compass point the first exaflare starts on: 0 north, then clockwise every 45°.
    public int? FirstExaflare { get; set; }

    // The way the other three exaflares follow it round.
    public bool? Clockwise { get; set; }

    // Headless runs pin the seconds between an exaflare's explosions. Internal, so settings sync and the summary skip it.
    internal float? ExaflareStep { get; set; }
}
