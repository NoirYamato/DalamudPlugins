using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// Cosmo Memory cast at 1.9: its bar ends at 7.6, when a human must carry 80% mitigation (a tank's limit break 3) or die,
// though only while tank mitigation is required; bots always live. Its ActionEffect at 7.9 gives everyone Brilliant
// Dynamis and Alpha Omega Code: ****mi*. The main tank's limit break 3 goes off halfway through the cast, at 4.9.
public class TopP6CosmoMemoryTests
{
    private const float HalfwayThroughTheCast = 4.9f;

    [TestCase(OffTank)]
    [TestCase(RegenHealer)]
    [TestCase(ShieldHealer)]
    [TestCase(MeleeDpsA)]
    [TestCase(MeleeDpsB)]
    [TestCase(PhysRangedDps)]
    [TestCase(CasterDps)]
    public void ABotMainTanksLimitBreakCarriesAHumanInAnyOtherSeat(PartyRole seat)
    {
        var run = CosmoMemory(new TimelineRunOptions { PlayerRole = seat, HumanPlayer = true, UserActionsEnabled = true },
            world => TopP6LimitBreaks.LimitBreak3(world, MainTank));

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void AHumanMainTankWhoPressesLives()
    {
        var run = CosmoMemory(new TimelineRunOptions { HumanPlayer = true, UserActionsEnabled = true }, PressLimitBreak3);

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void AHumanMainTankWhoDoesNotPressDies()
    {
        var run = CosmoMemory(new TimelineRunOptions { HumanPlayer = true, UserActionsEnabled = true },
            world => TopP6LimitBreaks.LimitBreak3(world, MainTank));

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (MainTank, (uint?)ActionId.CosmoMemory) }), run.ToString);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void NothingIsCheckedUnlessTankMitigationIsRequired(bool userActions, bool tankMitigation)
    {
        var run = CosmoMemory(new TimelineRunOptions
        {
            HumanPlayer = true,
            UserActionsEnabled = userActions,
            Configure = config => config.EnableTankMitigation = tankMitigation,
        }, _ => { });

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void ItsHitLeavesBrilliantDynamisOnEveryoneAndCodeMiOnAlphaOmega()
    {
        var before = new List<bool>();
        var after = new List<bool>();
        var run = TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            alphaOmega.CosmoMemory(1.9f);
            world.Events.Add(7.5f, () => before.AddRange(Statuses(world, alphaOmega)));
            world.Events.Add(8.5f, () => after.AddRange(Statuses(world, alphaOmega)));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(before, Has.Count.EqualTo(9).And.All.False);
        Assert.That(after, Has.Count.EqualTo(9).And.All.True);
    }

    [Test]
    public void ABlockStartingAfterItGetsTheSameStatuses()
    {
        var statuses = new List<bool>();
        TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(1f, alphaOmega.ApplyCosmoMemoryStatuses);
            world.Events.Add(1.5f, () => statuses.AddRange(Statuses(world, alphaOmega)));
        });

        Assert.That(statuses, Has.Count.EqualTo(9).And.All.True);
    }

    [Test]
    public void TheGaugeIsFullFromItsCast()
    {
        var bars = -1;
        TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            alphaOmega.CosmoMemory(1.9f);
            world.Events.Add(2f, () => bars = world.Party.LimitBreak.FilledBars);
        });

        Assert.That(bars, Is.EqualTo(3));
    }

    // Everyone's Brilliant Dynamis, then Alpha Omega's Code: ****mi*.
    private static IEnumerable<bool> Statuses(SimWorld world, TopP6AlphaOmega alphaOmega)
        => Enum.GetValues<PartyRole>().Select(role => world.Party.Get(role)!.HasStatus(StatusId.BrilliantDynamis))
               .Append(alphaOmega.Enemy!.HasStatus(StatusId.CodeMi));

    // The human main tank's own button, as the client's press applies it.
    private static void PressLimitBreak3(SimWorld world)
    {
        var tank = world.Party.Get(MainTank)!;
        var limitBreak3 = LimitBreakHandler.ActionId(PartyPresets.Standard[(int)MainTank].ClassJob, 3);
        JobActions.ApplyEffects(tank, limitBreak3, (ulong)tank.GameObjectId, new Random(1));
    }

    private static TimelineRunResult CosmoMemory(TimelineRunOptions options, Action<SimWorld> halfwayThroughTheCast)
        => TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            alphaOmega.CosmoMemory(1.9f);
            world.Events.Add(HalfwayThroughTheCast, () => halfwayThroughTheCast(world));
        }, options);
}
