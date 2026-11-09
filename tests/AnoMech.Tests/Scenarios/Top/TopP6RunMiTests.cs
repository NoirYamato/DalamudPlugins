using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// Run: ****mi* cast at 2.0: its 16 s cast's bar is up until 17.7, and its damage lands 30 s after the cast starts, at
// 32.0. A melee limit break 3 on Alpha Omega whose own cast bar ends from the cast's start until before the damage
// defeats Alpha Omega as it resolves; otherwise the damage kills everyone.
public class TopP6RunMiTests
{
    private const float CastAt = 2f;
    private const float MeleeLimitBreak3Cast = 4.5f;
    private const float Frame = ScenarioRun.FrameSeconds;

    private const float DamageLands = CastAt + P6Duration.RunMiLands;

    private static readonly TimelineRunOptions Human = new() { PlayerRole = MeleeDpsA, HumanPlayer = true, UserActionsEnabled = true };

    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    private static string RunMiKills => $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded})";

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void ItsCastIsShown()
    {
        var run = RunMi((world, alphaOmega, limitBreaks) => world.Events.Add(3f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy)));

        Assert.That(run.Sent("cast", P6ActionId.RunMi).Select(c => c.Time), Is.EqualTo(new[] { CastAt }).Within(TimelineRun.FrameLate));
    }

    // Pressed at 3.0, it resolves at 7.5: Alpha Omega falls then, and nobody dies. Pressed at 1.0, before the cast, it
    // still lands in it; pressed at 13.4, it lands at 17.9, after the bar has gone; at 27.4, at 31.9, just before the damage.
    [TestCase(1f)]
    [TestCase(3f)]
    [TestCase(13.1f)]
    [TestCase(13.4f)]
    [TestCase(20f)]
    [TestCase(27.4f)]
    public void AMeleeLimitBreak3LandingBeforeTheDamageDefeatsAlphaOmega(float pressAt)
    {
        var fell = new Dictionary<string, bool>();
        var run = RunMi((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(pressAt, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
            world.Events.Add(pressAt + 4.4f, () => fell["before"] = alphaOmega.Enemy!.Dead);
            world.Events.Add(pressAt + 4.6f, () => fell["after"] = alphaOmega.Enemy!.Dead);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(fell, Is.EqualTo(new Dictionary<string, bool> { ["before"] = false, ["after"] = true }));
    }

    // Pressed at 27.5 or later, it lands as the damage does or after.
    [TestCase(27.5f)]
    [TestCase(27.6f)]
    public void OneLandingAsTheDamageLandsIsTooLate(float pressAt)
    {
        var run = RunMi((world, alphaOmega, limitBreaks) => world.Events.Add(pressAt, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy)));

        AssertEveryoneDiedToIt(run);
    }

    [Test]
    public void WithoutOneItKillsEveryone()
        => AssertEveryoneDiedToIt(RunMi((_, _, _) => { }));

    [Test]
    public void AnotherKindDoesntDefeatAlphaOmega()
        => AssertEveryoneDiedToIt(RunMi((world, alphaOmega, limitBreaks) => world.Events.Add(3f, () => limitBreaks.Press(CasterDps, Job(CasterDps), alphaOmega.Enemy))));

    // A melee limit break 2 lands in the cast too, but only a limit break 3 finishes Alpha Omega.
    [Test]
    public void AMeleeLimitBreak2DoesntDefeatAlphaOmega()
        => AssertEveryoneDiedToIt(RunMi((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 3f, LimitBreakHandler.ActionId(Job(MeleeDpsA), 2), () => alphaOmega.Enemy),
            Human));

    // The human's melee limit break 3 aimed at nothing, or at the main tank, lands in the cast but finishes nothing.
    [TestCase(false)]
    [TestCase(true)]
    public void OneAimedElsewhereDoesntDefeatAlphaOmega(bool atTheMainTank)
        => AssertEveryoneDiedToIt(RunMi((world, _, _) => TopP6HumanLimitBreak.Press(world, 3f, LimitBreakHandler.ActionId(Job(MeleeDpsA), 3),
            () => atTheMainTank ? world.Party.Get(MainTank) : null), Human));

    // The melee's press waits for a gauge left at one bar, and the deaths say so.
    [Test]
    public void AShortGaugeSaysSo()
    {
        var run = RunMi((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(2.5f, () => limitBreaks.SetGauge(1f));
            world.Events.Add(3f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
        });

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause).Distinct(), Is.EqualTo(new[] { $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded}{TopP6LimitBreaks.GaugeWasShort})" }));
    }

    // A press that gives up as the damage lands, as the strat's does, still left the gauge short.
    [Test]
    public void AShortGaugeSaysSoThoughThePressGivesUpAsTheDamageLands()
    {
        var run = RunMi((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(2.5f, () => limitBreaks.SetGauge(1f));
            world.Events.Add(3f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy, TopP6AlphaOmega.RunMiLandsAt(CastAt)));
        });

        Assert.That(run.Deaths, Has.Count.EqualTo(8), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause).Distinct(), Is.EqualTo(new[] { $"Run: ****mi* ({TopP6AlphaOmega.RunMiLanded}{TopP6LimitBreaks.GaugeWasShort})" }));
    }

    // The human's own press counts as a bot's.
    [Test]
    public void AHumansMeleeLimitBreak3DefeatsItToo()
    {
        var run = RunMi((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 3f, LimitBreakHandler.ActionId(Job(MeleeDpsA), 3), () => alphaOmega.Enemy),
            Human);

        Assert.That(run.Passed, run.ToString);
    }

    // The human's client hears its cast end only on a later frame: started 4.52 s before the damage, the human's lands
    // 0.02 s before it, and its resolve reaches the run 0.03 s after it. The damage waits for it, and it defeats Alpha
    // Omega.
    [Test]
    public void AHumansMeleeLimitBreak3LandingJustBeforeTheDamageDefeatsItThoughHeardAfter()
    {
        const float HeardLate = 0.05f;
        bool? fell = null;
        var run = RunMi((world, alphaOmega, _) =>
        {
            world.Events.Add(DamageLands - MeleeLimitBreak3Cast - 0.02f, () =>
            {
                var userActions = (FakeUserActions)Natives.UserActions;
                var limitBreak3 = LimitBreakHandler.ActionId(Job(MeleeDpsA), 3);
                var target = (ulong)alphaOmega.Enemy!.GameObjectId;
                userActions.Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, limitBreak3, target, MeleeLimitBreak3Cast));
                world.Events.Add(MeleeLimitBreak3Cast + HeardLate,
                    () => userActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, limitBreak3, target)));
            });
            world.Events.Add(DamageLands + 0.2f, () => fell = alphaOmega.Enemy!.Dead);
        }, Human);

        Assert.That(run.Passed, run.ToString);
        Assert.That(fell, Is.True, "Alpha Omega has fallen");
    }

    // A client that never reports the resolve of a press that landed before the damage holds the damage only a moment.
    [Test]
    public void ALandedPressWhoseResolveNeverComesHoldsTheDamageOnlyAMoment()
    {
        var client = new TopP6HumanLimitBreakBetweenFrames(LimitBreakHandler.ActionId(Job(MeleeDpsA), 3),
            startsOn: now => now + MeleeLimitBreak3Cast >= DamageLands - 1f, resolvesOn: _ => false);
        var run = RunMi((_, _, _) => { }, Human, (world, alphaOmega) => client.Hear(world, alphaOmega.Enemy));

        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(Enum.GetValues<PartyRole>()), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Has.All.EqualTo(DamageLands + 0.25f).Within(TimelineRun.FrameLate), run.ToString);
    }

    // The human M1 holds the Gauntlet's enrage window beside Run: ****mi* cast at 6.005, between two frames, its damage
    // landing at 36.005. Its client is heard between the sim's frames, as in the game, and its resolve as the bar's own
    // frame begins. Landing in the frame before the bar goes up, it neither defeats Alpha Omega nor fills the window, which
    // kills the human as it closes at 31.505, 4.5 s before the damage kills the rest; landing with the bar, it does both,
    // though the bar's own frame settles it before Run: ****mi* listens.
    [TestCase(false)]
    [TestCase(true)]
    public void TheWindowAndTheDefeatCountTheSamePressesAsTheBarGoesUp(bool landsWithTheBar)
    {
        const float LateCastAt = 6.005f;
        var damageLands = TopP6AlphaOmega.RunMiLandsAt(LateCastAt);
        var client = new TopP6HumanLimitBreakBetweenFrames(LimitBreakHandler.ActionId(Job(MeleeDpsA), 3),
            startsOn: now => now + MeleeLimitBreak3Cast + (landsWithTheBar ? 0f : Frame) >= LateCastAt,
            resolvesOn: now => now + Frame >= LateCastAt);
        bool? alphaOmegaFell = null;
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            alphaOmega.RunMi(LateCastAt, limitBreaks);
            limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(MeleeDpsA, LateCastAt, damageLands - MeleeLimitBreak3Cast, TopP6LimitBreaks.EnrageMeleeRule,
                LandsBy: damageLands), () => alphaOmega.Enemy);
            world.Events.Add(LateCastAt + 5f, () => alphaOmegaFell = alphaOmega.Enemy!.Dead);
        }, Human, (world, alphaOmega) => client.Hear(world, alphaOmega.Enemy));

        var landsAt = client.StartedAt + MeleeLimitBreak3Cast;
        Assert.That(landsAt, landsWithTheBar ? Is.InRange(LateCastAt, LateCastAt + Frame) : Is.InRange(LateCastAt - Frame, LateCastAt - 0.001f));
        Assert.That(alphaOmegaFell, Is.EqualTo(landsWithTheBar), "Alpha Omega falls");
        if (landsWithTheBar)
        {
            Assert.That(run.Passed, run.ToString);
            return;
        }
        Assert.That(run.Deaths.Select(d => (d.Role, d.Cause)), Is.EqualTo(
            new[] { (MeleeDpsA, TopP6LimitBreaks.EnrageMeleeRule + TopP6LimitBreaks.YoursLandedBeforeTheBar) }
                .Concat(Enum.GetValues<PartyRole>().Where(r => r != MeleeDpsA).Select(r => (r, RunMiKills)))), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.EqualTo(
            new[] { damageLands - MeleeLimitBreak3Cast }.Concat(Enumerable.Repeat(damageLands, 7))).Within(TimelineRun.FrameLate), run.ToString);
    }

    // The same human, its client heard between frames, at the other end: landing in the frame before the damage, its
    // resolve heard only once the damage's frame has begun, it fills the window, which waits for it, and defeats Alpha
    // Omega, the damage waiting for it too.
    [Test]
    public void TheWindowAndTheDefeatCountAPressLandingInTheFrameBeforeTheDamage()
    {
        const float LateCastAt = 6.005f;
        var damageLands = TopP6AlphaOmega.RunMiLandsAt(LateCastAt);
        var client = new TopP6HumanLimitBreakBetweenFrames(LimitBreakHandler.ActionId(Job(MeleeDpsA), 3),
            startsOn: now => now + MeleeLimitBreak3Cast + Frame >= damageLands, resolvesOn: now => now >= damageLands);
        bool? alphaOmegaFell = null;
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            alphaOmega.RunMi(LateCastAt, limitBreaks);
            limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(MeleeDpsA, LateCastAt, damageLands - MeleeLimitBreak3Cast, TopP6LimitBreaks.EnrageMeleeRule,
                LandsBy: damageLands), () => alphaOmega.Enemy);
            world.Events.Add(damageLands + 0.5f, () => alphaOmegaFell = alphaOmega.Enemy!.Dead);
        }, Human, (world, alphaOmega) => client.Hear(world, alphaOmega.Enemy));

        Assert.That(client.StartedAt + MeleeLimitBreak3Cast, Is.InRange(damageLands - Frame, damageLands - 0.001f), "it lands in the frame before the damage");
        Assert.That(run.Passed, run.ToString);
        Assert.That(alphaOmegaFell, Is.True, "Alpha Omega falls");
    }

    // Landing in the damage's own frame, the human's is too late: the damage kills everyone as it lands.
    [Test]
    public void APressLandingInTheDamagesOwnFrameIsTooLate()
    {
        var client = new TopP6HumanLimitBreakBetweenFrames(LimitBreakHandler.ActionId(Job(MeleeDpsA), 3),
            startsOn: now => now + MeleeLimitBreak3Cast >= DamageLands);
        var run = RunMi((_, _, _) => { }, Human, (world, alphaOmega) => client.Hear(world, alphaOmega.Enemy));

        Assert.That(client.StartedAt + MeleeLimitBreak3Cast, Is.InRange(DamageLands, DamageLands + Frame), "it lands in the damage's frame");
        AssertEveryoneDiedToIt(run);
    }

    private static void AssertEveryoneDiedToIt(TimelineRunResult run)
    {
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(Enum.GetValues<PartyRole>()), run.ToString);
        Assert.That(run.Deaths.Select(d => (d.ActionId, d.Cause)).Distinct(), Is.EqualTo(new[] { ((uint?)P6ActionId.RunMi, RunMiKills) }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Has.All.EqualTo(DamageLands).Within(TimelineRun.FrameLate), run.ToString);
    }

    private static TimelineRunResult RunMi(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null,
        Action<SimWorld, TopP6AlphaOmega>? betweenFrames = null)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            alphaOmega.RunMi(CastAt, limitBreaks);
            timeline(world, alphaOmega, limitBreaks);
        }, options ?? new TimelineRunOptions { PlayerRole = OffTank }, betweenFrames);
}
