using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea.P1LimitCut;
using AnoMech.Scenarios.Top.P3Intermission;
using AnoMech.Scenarios.Top.P3Monitors;
using AnoMech.Scenarios.Top.P5Delta;

namespace AnoMech.Tests;

// The shared setting reaches peers through ScenarioSettingsSync (also how a peer keeps and restores its own
// settings) and the lobby through ScenarioSettingsSummary, in every scenario that offers it. It is On unless set in
// the TOP scenarios and Off in TEA's.
public class AutomarkersTests
{
    private static IEnumerable<TestCaseData> Overrides()
    {
        yield return new TestCaseData((Func<object>)(() => new TopP3IntermissionStateOverrides()), Automarkers.On).SetName("{m}(P3 Intermission)");
        yield return new TestCaseData((Func<object>)(() => new TopP3MonitorsStateOverrides()), Automarkers.On).SetName("{m}(P3 Monitors)");
        yield return new TestCaseData((Func<object>)(() => new TopP5DeltaStateOverrides()), Automarkers.On).SetName("{m}(P5 Delta)");
        yield return new TestCaseData((Func<object>)(() => new TeaP1LimitCutStateOverrides()), Automarkers.Off).SetName("{m}(TEA Limit Cut)");
    }

    [TestCaseSource(nameof(Overrides))]
    public void HasItsDefault(Func<object> make, Automarkers byDefault)
    {
        Assert.That(Get(make()), Is.EqualTo(byDefault));
        Assert.That(ScenarioSettingsSummary.Describe(make()), Has.Member($"Automarkers: {byDefault}"));
    }

    [TestCaseSource(nameof(Overrides))]
    public void TheOtherValueTravelsToAPeerAndShowsInTheLobby(Func<object> make, Automarkers byDefault)
    {
        var other = Other(byDefault);
        var host = make();
        Set(host, other);
        var peer = make();

        Assert.That(ScenarioSettingsSync.Apply(peer, ScenarioSettingsSync.Serialize(host)!), Is.True);
        Assert.That(Get(peer), Is.EqualTo(other));
        Assert.That(ScenarioSettingsSummary.Describe(host), Has.Member($"Automarkers: {other}"));
    }

    [TestCaseSource(nameof(Overrides))]
    public void AValueThisBuildLacksKeepsOurs(Func<object> make, Automarkers byDefault)
    {
        var other = Other(byDefault);
        var host = make();
        Set(host, other);
        var json = ScenarioSettingsSync.Serialize(host)!;
        var sent = $"\"Automarkers\":{(int)other}";
        Assert.That(json, Does.Contain(sent));
        var peer = make();

        ScenarioSettingsSync.Apply(peer, json.Replace(sent, "\"Automarkers\":7"));
        Assert.That(Get(peer), Is.EqualTo(byDefault));
    }

    private static Automarkers Other(Automarkers value) => value == Automarkers.On ? Automarkers.Off : Automarkers.On;

    private static Automarkers Get(object overrides)
        => (Automarkers)overrides.GetType().GetProperty(nameof(Automarkers))!.GetValue(overrides)!;

    private static void Set(object overrides, Automarkers value)
        => overrides.GetType().GetProperty(nameof(Automarkers))!.SetValue(overrides, value);
}
