using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Cosmo Memory cast at 1.9 decides at 7.6, as its bar ends: everyone needs a tank's limit break 3 taken hold, pressed at
// least 1 s before (P6Duration.TankLimitBreak3TakesHold), or dies; it counts whoever pressed it, bot or human, and
// whatever "Require tank mitigation" says. Magic Number, cast at 2.0, decides the same way at 6.7.
public class TopP6TankLimitBreak3Tests
{
    private const float CosmoMemoryAt = 1.9f;
    private const float CosmoMemoryDecides = 7.6f;

    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    private static string CosmoMemoryKills => $"Cosmo Memory ({TopP6AlphaOmega.CosmoMemoryNeedsATankLimitBreak3})";

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [TestCase(4.9f)]
    [TestCase(6.5f)]
    public void ABotsTankLimitBreak3InTimeCarriesEveryone(float pressAt)
    {
        var run = CosmoMemory((world, limitBreaks) => world.Events.Add(pressAt, () => limitBreaks.Press(MainTank, Job(MainTank))));

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void OnePressedWithinASecondOfTheHitIsTooLate()
    {
        var run = CosmoMemory((world, limitBreaks) => world.Events.Add(6.9f, () => limitBreaks.Press(MainTank, Job(MainTank))));

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause).Distinct(),
                    Is.EqualTo(new[] { $"Cosmo Memory ({TopP6AlphaOmega.CosmoMemoryNeedsATankLimitBreak3}; one pressed within 1 s of the hit takes hold too late)" }));
        Assert.That(run.Deaths.Select(d => d.Time), Has.All.EqualTo(CosmoMemoryDecides).Within(TimelineRun.FrameLate));
    }

    [Test]
    public void WithoutOneEveryoneDies()
    {
        var run = CosmoMemory((_, _) => { });

        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(Enum.GetValues<PartyRole>()), run.ToString);
        Assert.That(run.Deaths.Select(d => (d.ActionId, d.Cause)).Distinct(), Is.EqualTo(new[] { ((uint?)ActionId.CosmoMemory, CosmoMemoryKills) }));
    }

    // The human main tank's own press, in time or a moment too late.
    [TestCase(4.9f, true)]
    [TestCase(7.1f, false)]
    public void AHumanTanksPressCountsTheSame(float pressAt, bool lives)
    {
        var run = CosmoMemory((world, _) => TopP6HumanLimitBreak.Press(world, pressAt, LimitBreakHandler.ActionId(Job(MainTank), 3)),
            new TimelineRunOptions { HumanPlayer = true, UserActionsEnabled = true });

        Assert.That(run.Passed, Is.EqualTo(lives), run.ToString);
        if (!lives) Assert.That(run.Deaths[0].Cause, Does.Contain("takes hold too late"));
    }

    [Test]
    public void ItChecksWhateverRequireTankMitigationSays()
    {
        var run = CosmoMemory((_, _) => { }, new TimelineRunOptions { Configure = config => config.EnableTankMitigation = false });

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
    }

    // The main tank's press waits for a gauge emptied at 1.0 that never fills, and every death says so.
    [Test]
    public void AShortGaugeSaysSo()
    {
        var run = CosmoMemory((world, limitBreaks) =>
        {
            world.Events.Add(2.5f, () => limitBreaks.SetGauge(1f));
            world.Events.Add(4.9f, () => limitBreaks.Press(MainTank, Job(MainTank)));
        });

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause).Distinct(), Is.EqualTo(new[] { $"Cosmo Memory ({TopP6AlphaOmega.CosmoMemoryNeedsATankLimitBreak3}{TopP6LimitBreaks.GaugeWasShort})" }));
    }

    // Its hit leaves Brilliant Dynamis on everyone and Code: ****mi* on Alpha Omega.
    [Test]
    public void ItsHitLeavesItsStatuses()
    {
        var after = new List<bool>();
        var run = CosmoMemory((world, limitBreaks) =>
        {
            world.Events.Add(4.9f, () => limitBreaks.Press(MainTank, Job(MainTank)));
            world.Events.Add(8.5f, () => after.AddRange(Enum.GetValues<PartyRole>().Select(r => world.Party.Get(r)!.HasStatus(StatusId.BrilliantDynamis))));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(after, Has.Count.EqualTo(8).And.All.True);
    }

    // The gauge: full from the cast, spent by the main tank's press, then rebuilding from the hit.
    [Test]
    public void ItFillsTheGaugeAndStartsItsRebuild()
    {
        var gauge = new Dictionary<float, float>();
        var (from, fullBy) = TopP6AlphaOmega.GaugeRebuildAfterCosmoMemory(CosmoMemoryAt);
        CosmoMemory((world, limitBreaks) =>
        {
            world.Events.Add(1.0f, () => limitBreaks.SetGauge(0f));
            world.Events.Add(4.9f, () => limitBreaks.Press(MainTank, Job(MainTank)));
            foreach (var at in new[] { 2.0f, 5.0f, 7.5f, 20f })
                world.Events.Add(at, () => gauge[at] = limitBreaks.Gauge);
        });

        Assert.That(from, Is.EqualTo(CosmoMemoryDecides).Within(0.001f));
        Assert.That(fullBy, Is.EqualTo(CosmoMemoryDecides + P6Duration.GaugeRebuild).Within(0.001f));
        Assert.That(gauge[2.0f], Is.EqualTo(3f));
        Assert.That(gauge[5.0f], Is.EqualTo(0f));
        Assert.That(gauge[7.5f], Is.EqualTo(0f));
        Assert.That(gauge[20f], Is.EqualTo(TopP6LimitBreaks.RebuiltBars(20f, from, fullBy)).Within(0.01f));
    }

    [TestCase(2.7f, true)]
    [TestCase(6.0f, false)]
    public void MagicNumberAsksTheSame(float pressAt, bool lives)
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            alphaOmega.MagicNumber(2f, limitBreaks);
            world.Events.Add(pressAt, () => limitBreaks.Press(MainTank, Job(MainTank)));
            world.Events.Add(7.5f, () => world.Party.WipeAllPlayers(0, "the test is over"));
        }, new TimelineRunOptions { PlayerRole = OffTank });

        var magicNumber = run.Deaths.Where(d => d.ActionId == P6ActionId.MagicNumber).ToList();
        Assert.That(magicNumber, lives ? Is.Empty : Has.Count.EqualTo(8), run.ToString);
        Assert.That(magicNumber.Select(d => d.Time), Has.All.EqualTo(6.7f).Within(TimelineRun.FrameLate));
    }

    private static TimelineRunResult CosmoMemory(Action<AnoMech.Core.SimObjects.SimWorld, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            alphaOmega.CosmoMemory(CosmoMemoryAt, limitBreaks);
            timeline(world, limitBreaks);
        }, options ?? new TimelineRunOptions { PlayerRole = OffTank });
}
