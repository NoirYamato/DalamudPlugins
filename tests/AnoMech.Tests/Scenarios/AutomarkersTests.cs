using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Intermission;
using AnoMech.Scenarios.Top.P3Monitors;
using AnoMech.Scenarios.Top.P5Delta;

namespace AnoMech.Tests;

// The shared setting reaches peers through ScenarioSettingsSync (also how a peer keeps and restores its own
// settings) and the lobby through ScenarioSettingsSummary, in every scenario that offers it.
public class AutomarkersTests
{
    private static IEnumerable<TestCaseData> Overrides()
    {
        yield return new TestCaseData((Func<object>)(() => new TopP3IntermissionStateOverrides())).SetName("{m}(P3 Intermission)");
        yield return new TestCaseData((Func<object>)(() => new TopP3MonitorsStateOverrides())).SetName("{m}(P3 Monitors)");
        yield return new TestCaseData((Func<object>)(() => new TopP5DeltaStateOverrides())).SetName("{m}(P5 Delta)");
    }

    [TestCaseSource(nameof(Overrides))]
    public void DefaultsToOn(Func<object> make)
    {
        Assert.That(Get(make()), Is.EqualTo(Automarkers.On));
        Assert.That(ScenarioSettingsSummary.Describe(make()), Has.Member("Automarkers: On"));
    }

    [TestCaseSource(nameof(Overrides))]
    public void OffTravelsToAPeerAndShowsInTheLobby(Func<object> make)
    {
        var host = make();
        Set(host, Automarkers.Off);
        var peer = make();

        Assert.That(ScenarioSettingsSync.Apply(peer, ScenarioSettingsSync.Serialize(host)!), Is.True);
        Assert.That(Get(peer), Is.EqualTo(Automarkers.Off));
        Assert.That(ScenarioSettingsSummary.Describe(host), Has.Member("Automarkers: Off"));
    }

    [TestCaseSource(nameof(Overrides))]
    public void AValueThisBuildLacksKeepsOurs(Func<object> make)
    {
        var host = make();
        Set(host, Automarkers.Off);
        var json = ScenarioSettingsSync.Serialize(host)!;
        Assert.That(json, Does.Contain("\"Automarkers\":1"));
        var peer = make();

        ScenarioSettingsSync.Apply(peer, json.Replace("\"Automarkers\":1", "\"Automarkers\":7"));
        Assert.That(Get(peer), Is.EqualTo(Automarkers.On));
    }

    private static Automarkers Get(object overrides)
        => (Automarkers)overrides.GetType().GetProperty(nameof(Automarkers))!.GetValue(overrides)!;

    private static void Set(object overrides, Automarkers value)
        => overrides.GetType().GetProperty(nameof(Automarkers))!.SetValue(overrides, value);
}
