using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6CosmoDive1;

namespace AnoMech.Tests;

// One roll, Cosmo Arrow's pattern: Auto rolls it from the seed, In or Out pins it.
public class TopP6CosmoDive1StateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 200);

    [Test]
    public void AutoRollsBothPatterns()
        => Assert.That(Rolls(new TopP6CosmoDive1StateOverrides()), Has.Some.True.And.Some.False);

    [Test]
    public void ASeedReproducesItsRoll()
    {
        var first = Rolls(new TopP6CosmoDive1StateOverrides());
        var again = Rolls(new TopP6CosmoDive1StateOverrides());

        Assert.That(again, Is.EqualTo(first));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheSettingPinsThePattern(bool inFirst)
        => Assert.That(Rolls(new TopP6CosmoDive1StateOverrides { InFirst = inFirst }), Has.All.EqualTo(inFirst));

    private static List<bool> Rolls(TopP6CosmoDive1StateOverrides overrides)
        => Seeds.Select(seed => new TopP6CosmoDive1State(new Rng(seed), overrides).InFirst).ToList();
}
