namespace AnoMech.Scenarios.Top.P6CosmoDive1;

public sealed class TopP6CosmoDive1State
{
    // Cosmo Arrow's cross lands first, else its outer lines do. UNVERIFIED: that each is as likely.
    public bool InFirst { get; }

    public TopP6CosmoDive1State(Rng rng, TopP6CosmoDive1StateOverrides overrides)
    {
        InFirst = overrides.InFirst ?? rng.NextBool();
    }
}
