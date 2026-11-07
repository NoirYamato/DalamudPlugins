namespace AnoMech.Scenarios.Top.P6CosmoDive1;

// User-controlled overrides for TopP6CosmoDive1State's roll, bound by the scenario's settings UI; null leaves it random.
public sealed class TopP6CosmoDive1StateOverrides
{
    // Cosmo Arrow: true = in first, false = out first.
    public bool? InFirst { get; set; }
}
