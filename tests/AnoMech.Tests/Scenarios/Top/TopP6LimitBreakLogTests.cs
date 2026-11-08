using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The run's log of limit breaks the human presses, heard through IUserActions.Acted as their client plays each step: who
// pressed it, which one at which level and of which kind, aimed at what, and when its cast started, resolved or was
// interrupted. Its kind and level come from the human's job. The human is M1, a DRG, unless a test says otherwise.
public class TopP6LimitBreakLogTests
{
    private static uint LimitBreak(JobId job, int level) => LimitBreakHandler.ActionId((uint)job, level);

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void AHumansCastIsLoggedFromItsStartToItsResolve()
    {
        var log = new Dictionary<float, (bool Casting, float? ResolvedAt)>();
        TopP6LimitBreakPress? press = null;
        ulong alphaOmegaId = 0;
        TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            TopP6HumanLimitBreak.Press(world, 2f, LimitBreak(JobId.Dragoon, 3), () => alphaOmega.Enemy);
            foreach (var at in new[] { 3f, 7f })
                world.Events.Add(at, () =>
                {
                    press = limitBreaks.Presses.Single();
                    log[at] = (press.IsCasting, press.ResolvedAt);
                    alphaOmegaId = (ulong)alphaOmega.Enemy!.GameObjectId;
                });
        }, Human);

        Assert.That(log[3f], Is.EqualTo((true, (float?)null)));
        Assert.That(log[7f].ResolvedAt, Is.EqualTo(6.5f).Within(TimelineRun.FrameLate));
        Assert.Multiple(() =>
        {
            Assert.That(press!.Seat, Is.EqualTo(MeleeDpsA));
            Assert.That(press.ByHuman, Is.True);
            Assert.That(press.Job, Is.EqualTo((uint)JobId.Dragoon));
            Assert.That(press.ActionId, Is.EqualTo(LimitBreak(JobId.Dragoon, 3)));
            Assert.That(press.Level, Is.EqualTo(3));
            Assert.That(press.Kind, Is.EqualTo(TopP6LimitBreakKind.Melee));
            Assert.That(press.TargetId, Is.EqualTo(alphaOmegaId));
            Assert.That(press.CastStartedAt, Is.EqualTo(2f).Within(TimelineRun.FrameLate));
            Assert.That(press.CastSeconds, Is.EqualTo(4.5f).Within(0.01f));
            Assert.That(press.InterruptedAt, Is.Null);
        });
    }

    [Test]
    public void AnInstantOneIsLoggedAsItResolves()
    {
        TopP6LimitBreakPress? press = null;
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            TopP6HumanLimitBreak.Press(world, 2f, LimitBreak(JobId.Warrior, 3));
            world.Events.Add(3f, () => press = limitBreaks.Presses.Single());
        }, Human with { PlayerRole = MainTank });

        Assert.That(press!.Kind, Is.EqualTo(TopP6LimitBreakKind.Tank));
        Assert.That(press.CastSeconds, Is.EqualTo(0f));
        Assert.That(press.ResolvedAt, Is.EqualTo(press.CastStartedAt));
    }

    // Cut short, it never resolves, and spends nothing.
    [Test]
    public void AnInterruptedCastIsLoggedSo()
    {
        var after = (InterruptedAt: (float?)null, ResolvedAt: (float?)null, Gauge: -1f);
        TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            TopP6HumanLimitBreak.Press(world, 2f, LimitBreak(JobId.Dragoon, 3), () => alphaOmega.Enemy, interruptedAfter: 1f);
            world.Events.Add(8f, () =>
            {
                var press = limitBreaks.Presses.Single();
                after = (press.InterruptedAt, press.ResolvedAt, limitBreaks.Gauge);
            });
        }, Human);

        Assert.That(after.InterruptedAt, Is.EqualTo(3f).Within(TimelineRun.FrameLate));
        Assert.That(after.ResolvedAt, Is.Null);
        Assert.That(after.Gauge, Is.EqualTo(3f));
    }

    // Rampart is no limit break: nothing is logged.
    [Test]
    public void OnlyLimitBreaksAreLogged()
    {
        var count = -1;
        TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(2f, () => ((FakeUserActions)Natives.UserActions).Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, 7531, 0)));
            world.Events.Add(3f, () => count = limitBreaks.Presses.Count);
        }, Human);

        Assert.That(count, Is.Zero);
    }

    // A human whose job doesn't fit the seat is logged by their own job: a BLM in M1 casts a caster limit break 3.
    [Test]
    public void ItsKindIsTheHumansJobs()
    {
        TopP6LimitBreakPress? press = null;
        TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)JobId.BlackMage;
            });
            TopP6HumanLimitBreak.Press(world, 2f, LimitBreak(JobId.BlackMage, 3), () => alphaOmega.Enemy);
            world.Events.Add(3f, () => press = limitBreaks.Presses.Single());
        }, Human);

        Assert.That((press!.Seat, press.Kind, press.Level), Is.EqualTo((MeleeDpsA, TopP6LimitBreakKind.Caster, 3)));
    }

    // Disposed with its run, it hears no more.
    [Test]
    public void ItStopsHearingOnceDisposed()
    {
        var count = -1;
        TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(1f, limitBreaks.Dispose);
            TopP6HumanLimitBreak.Press(world, 2f, LimitBreak(JobId.Dragoon, 3), () => alphaOmega.Enemy);
            world.Events.Add(8f, () => count = limitBreaks.Presses.Count);
        }, Human);

        Assert.That(count, Is.Zero);
    }

    [TestCase(JobId.Warrior, TopP6LimitBreakKind.Tank)]
    [TestCase(JobId.Paladin, TopP6LimitBreakKind.Tank)]
    [TestCase(JobId.DarkKnight, TopP6LimitBreakKind.Tank)]
    [TestCase(JobId.Gunbreaker, TopP6LimitBreakKind.Tank)]
    [TestCase(JobId.WhiteMage, TopP6LimitBreakKind.Healer)]
    [TestCase(JobId.Scholar, TopP6LimitBreakKind.Healer)]
    [TestCase(JobId.Astrologian, TopP6LimitBreakKind.Healer)]
    [TestCase(JobId.Sage, TopP6LimitBreakKind.Healer)]
    [TestCase(JobId.Monk, TopP6LimitBreakKind.Melee)]
    [TestCase(JobId.Dragoon, TopP6LimitBreakKind.Melee)]
    [TestCase(JobId.Ninja, TopP6LimitBreakKind.Melee)]
    [TestCase(JobId.Samurai, TopP6LimitBreakKind.Melee)]
    [TestCase(JobId.Reaper, TopP6LimitBreakKind.Melee)]
    [TestCase(JobId.Viper, TopP6LimitBreakKind.Melee)]
    [TestCase(JobId.Bard, TopP6LimitBreakKind.PhysicalRanged)]
    [TestCase(JobId.Machinist, TopP6LimitBreakKind.PhysicalRanged)]
    [TestCase(JobId.Dancer, TopP6LimitBreakKind.PhysicalRanged)]
    [TestCase(JobId.BlackMage, TopP6LimitBreakKind.Caster)]
    [TestCase(JobId.Summoner, TopP6LimitBreakKind.Caster)]
    [TestCase(JobId.RedMage, TopP6LimitBreakKind.Caster)]
    [TestCase(JobId.Pictomancer, TopP6LimitBreakKind.Caster)]
    public void EachJobsLimitBreaksAreItsKindAtTheirLevel(JobId job, TopP6LimitBreakKind kind)
    {
        Assert.That(TopP6LimitBreaks.KindOf((uint)job), Is.EqualTo(kind));
        for (var level = 1; level <= 3; level++)
            Assert.That(TopP6LimitBreaks.LevelOf((uint)job, LimitBreak(job, level)), Is.EqualTo(level), $"level {level}");
        Assert.That(TopP6LimitBreaks.LevelOf((uint)job, 7531), Is.Zero);
    }

    private static readonly TimelineRunOptions Human = new() { PlayerRole = MeleeDpsA, HumanPlayer = true, UserActionsEnabled = true };
}
