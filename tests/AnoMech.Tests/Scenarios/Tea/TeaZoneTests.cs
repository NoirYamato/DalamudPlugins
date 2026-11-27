using System.Globalization;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.I1LimitCut;
using AnoMech.Scenarios.Tea.P1LimitCut;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using AnoMech.Scenarios.Tea.P3Wormhole;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.NegativeRun;

namespace AnoMech.Tests;

// TEA's zone: upstream's file with NoirMech's P1 phase, its EU waymarks and its reason for a death past the rim, beside
// upstream's I1 and P3 phases and its duty data. The weather each phase sets as a run starts: retail's settled weather
// on the first run after the zone-in, a change on every later one.
public class TeaZoneTests
{
    // In one stay a run of P3 (Wormhole's Beyond Time) and then one of P1: the P1 run brings back its Fair Skies, which a
    // phase that set no weather would have left as Beyond Time.
    [Test]
    public void P1BringsBackFairSkiesAfterAP3RunInTheSameStay()
    {
        using var run = new PracticeRun(PracticeRun.Clean, phase: TeaZone.P3);
        run.StartInZone(run.Params());
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);

        run.Game.StartScenario(new RunScenarioParams(new TimelineScenario(TeaZone.P1, PracticeRun.Clean), new LocalPartyProvider(), null, 0, null));
        run.Until(() => run.Started.Count == 2 && run.Game.Status == SessionStatus.Succeeded);

        var weathers = run.Fake.Timeline.Rows.Where(row => row["kind"] == "weather").Select(row => (row["id"], row["detail"])).ToList();
        Assert.That(weathers, Is.EqualTo(new[]
        {
            ($"{TeaConstants.Weather.BeyondTime}", "apply"),
            ($"{TeaConstants.Weather.FairSkies}", "transition=0.5"),
        }));
    }

    // A run places the first preset unless another is picked.
    [Test]
    public void TheEuWaymarksComeFirstSoTheyAreTheDefaultAndUpstreamsPfTrinesSecond()
    {
        var presets = TeaZone.Instance.WaymarkPresets;

        Assert.That(presets.Select(preset => preset.Name), Is.EqualTo(new[] { "EU", "PF Trines" }));
        Assert.That(presets[0].Markers, Is.EqualTo(TeaP1Constants.EuWaymarks));
        Assert.That(presets[1].Markers, Is.EqualTo(TeaConstants.PfTrinesWaymarks));
    }

    // From the zone-in: the entrance barrier on the south edge, the zone's 17 invisible helpers and the arena's slots as
    // the zone loads; from the run's start, the arena's eight objects and its seven slots set one by one. All as the
    // zone's duty data has them, in a run of NoirMech's P1.
    [Test]
    public void ARunOfP1StandsOnUpstreamsDutyData()
    {
        var run = TimelineRun.Execute(world => world.Events.Add(1f, () => { }), new TimelineRunOptions { Phase = TeaZone.P1 });
        var init = TeaReplayData.DutyInitData;
        var objects = run.Rows.Where(row => row["kind"] == "eobj_spawn").ToList();

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            var entrance = objects.Single(row => row["id"] == $"{init.EntranceBarrier.EObjId}");
            Assert.That((Float(entrance["x"]), Float(entrance["z"])), Is.EqualTo((100f, 115f)), "the barrier");
            Assert.That((Field(entrance, "layout"), Field(entrance, "gimmick")), Is.EqualTo((init.EntranceBarrier.EObjLayoutId, init.EntranceBarrier.WallLayoutId)));
            Assert.That(run.Spawned(init.HelperPool.BNpcBaseId), Has.Count.EqualTo(17), "the zone's helpers");
            Assert.That(run.Rows.Where(row => row["kind"] == "map_effect_snapshot").Select(row => row["detail"]),
                Is.Not.Empty.And.All.EqualTo("states=0,1,4,1,4,4,4,4"), "the slots as the zone loads");
            Assert.That(objects.Where(row => row["id"] == $"{TeaConstants.ArenaEObjId}").Select(row => (Field(row, "layout"), Field(row, "gimmick"))),
                Is.EquivalentTo(TeaConstants.ArenaLayouts), "the arena's objects");
            Assert.That(run.Rows.Where(row => row["kind"] == "map_effect").Select(row => (row["id"], Field(row, "flags"))),
                Is.EqualTo(TeaConstants.ArenaStates.Select((state, i) => ($"{i + 1}", ((uint)state << 16) | state))), "the arena's slots at the start");
        });
    }

    // Upstream's own TEA tests read the environment's action alone; NoirMech's read the reason too.
    [TestCase("P1")]
    [TestCase("P3")]
    public void GoingPastTheRimKillsWithItsReasonInEveryPhase(string phaseName)
    {
        var phase = phaseName == "P1" ? TeaZone.P1 : TeaZone.P3;
        var run = TimelineRun.Execute(world => world.Events.Add(1f, () => TimelineRun.Place(world, MeleeDpsA, 0f, 20.5f)),
            new TimelineRunOptions { Phase = phase });

        Assert.That(run.Deaths.Select(death => (death.Role, death.ActionId)), Is.EqualTo(new[] { (MeleeDpsA, (uint?)TheEnvironment) }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(TeaZone.WentPastTheRim));
    }

    // Upstream's I1 Limit Cut stays off the menu beside NoirMech's own, which plays the same intermission on its own clock;
    // its files and tests stay, the tests running it outside the catalog. The phases read P1, then P3.
    [Test]
    public void TheMenuListsTheLivingLiquidAndLimitCutThenWormholeAndNotUpstreamsI1LimitCut()
    {
        var tea = ScenarioCatalog.Create().Scenarios.Where(scenario => scenario.Phase.Zone == TeaZone.Instance).ToList();

        Assert.That(tea.Select(scenario => (scenario.GetType(), scenario.Phase)), Is.EqualTo(new (Type, IPhase)[]
        {
            (typeof(TeaP1LivingLiquidScenario), TeaZone.P1),
            (typeof(TeaP1LimitCutScenario), TeaZone.P1),
            (typeof(TeaP3WormholeScenario), TeaZone.P3),
        }));
        Assert.That(ScenarioCatalog.Create().Scenarios, Has.None.TypeOf<TeaI1LimitCutScenario>());
    }

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    // One `name=0x...` field of a row's detail.
    private static uint Field(Dictionary<string, string> row, string name)
        => Convert.ToUInt32(row["detail"].Split(' ').Single(field => field.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..], 16);
}
