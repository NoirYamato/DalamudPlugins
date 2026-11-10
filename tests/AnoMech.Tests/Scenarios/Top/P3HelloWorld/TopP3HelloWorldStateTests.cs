using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;

namespace AnoMech.Tests;

// A set's layout is the bearing between its two defamation-colour towers, which stand 45° either side of it: an
// intercardinal layout puts the towers on the cardinals, a cardinal one on the intercardinals.
public class TopP3HelloWorldStateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 200);

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private AnoMech.Multiplayer.MultiplayerManager? leftInstalled;
    private SimParty party = null!;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
        party = Party();
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    [Test]
    public void AutoRollsEverySetFromAllEightLayouts()
    {
        var layouts = Seeds.SelectMany(seed => Roll(seed, towers: null).Layouts).ToList();

        Assert.That(layouts.Distinct(), Is.EquivalentTo(Direction.All));
        Assert.That(Seeds.Count(seed => Roll(seed, towers: null).Layouts.Select(d => Direction.Cardinal.Contains(d)).Distinct().Count() == 2),
                    Is.GreaterThan(0), "a run with sets on both rings");
    }

    [TestCase(TowerSpots.Cardinals)]
    [TestCase(TowerSpots.Intercardinals)]
    public void TheTowersSettingPicksTheRingEverySet(TowerSpots towers)
    {
        var layouts = Seeds.SelectMany(seed => Roll(seed, towers).Layouts).ToList();
        var expected = towers == TowerSpots.Cardinals ? Direction.Intercardinal : Direction.Cardinal;

        Assert.That(layouts.Distinct(), Is.EquivalentTo(expected));
    }

    [TestCase(null)]
    [TestCase(TowerSpots.Cardinals)]
    [TestCase(TowerSpots.Intercardinals)]
    public void SameLayoutEverySetRepeatsTheFirstSet(TowerSpots? towers)
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var layouts = Roll(seed, towers, sameLayoutEverySet: true).Layouts;
                Assert.That(layouts.Distinct().Count(), Is.EqualTo(1), $"seed {seed}");
                Assert.That(layouts[0], Is.EqualTo(Roll(seed, towers).Layouts[0]), $"seed {seed}");
            }
        });
    }

    // The layouts are rolled last, so the setting moves none of the other rolls.
    [Test]
    public void TheTowersSettingLeavesTheOtherRollsAlone()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var auto = Roll(seed, towers: null);
                foreach (var towers in Enum.GetValues<TowerSpots>())
                {
                    var forced = Roll(seed, towers);
                    Assert.That(forced.Kits.List, Is.EqualTo(auto.Kits.List), $"seed {seed}, {towers}: kits");
                    Assert.That(forced.DefamationColour, Is.EqualTo(auto.DefamationColour), $"seed {seed}, {towers}: colour");
                }
            }
        });
    }

    private TopP3HelloWorldState Roll(int seed, TowerSpots? towers, bool sameLayoutEverySet = false)
        => new(new Rng(seed), party, new TopP3HelloWorldStateOverrides
        {
            Towers = towers,
            SameLayoutEverySet = sameLayoutEverySet ? true : null,
        });

    private static SimParty Party()
    {
        SimParty? seated = null;
        var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.MainTank,
            StopAt = 0.1f,
            Probe = p => seated ??= p.World.Party,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return seated!;
    }
}
