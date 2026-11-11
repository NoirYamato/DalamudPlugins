using MitCue.Core.Actions;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

public class TimingRulesTests
{
    private static readonly ReminderOptions Defaults = new();

    // ---- DefaultOffset -------------------------------------------------------------------------

    [Fact]
    public void DefaultOffset_Note_IsThreeSecondsBefore() =>
        Assert.Equal(-3, TimingRules.DefaultOffset(null, 90), Kit.Tolerance);

    [Theory]
    [InlineData(ActionKind.PartyMit, 20, -6)]
    [InlineData(ActionKind.PartyMit, 15, -4.5)]
    [InlineData(ActionKind.PartyMit, 5, -2)]     // 1.5 s clamps up to 2 s
    [InlineData(ActionKind.PartyMit, 30, -8)]    // 9 s clamps down to 8 s
    [InlineData(ActionKind.TankMit, 10, -3)]
    [InlineData(ActionKind.TankMit, 120, -8)]
    [InlineData(ActionKind.Regen, 15, -4.5)]
    [InlineData(ActionKind.Regen, 6, -2)]
    public void DefaultOffset_MitigationAndRegen_LeadByThirtyPercentOfTheDurationClamped(ActionKind kind, double duration, double expected) =>
        Assert.Equal(expected, TimingRules.DefaultOffset(Kit.Act(1, "X", kind, duration: duration), 90), Kit.Tolerance);

    [Theory]
    [InlineData(ActionKind.PartyMit)]
    [InlineData(ActionKind.TankMit)]
    [InlineData(ActionKind.Regen)]
    public void DefaultOffset_MitigationAndRegen_WithoutDuration_AreThreeSecondsBefore(ActionKind kind) =>
        Assert.Equal(-3, TimingRules.DefaultOffset(Kit.Act(1, "X", kind), 90), Kit.Tolerance);

    [Fact]
    public void DefaultOffset_ZeroDuration_CountsAsUnknown() =>
        Assert.Equal(-3, TimingRules.DefaultOffset(Kit.Act(1, "X", ActionKind.PartyMit, duration: 0), 90), Kit.Tolerance);

    [Theory]
    [InlineData(80, -3)]   // 10 s below level 90
    [InlineData(90, -6)]   // 20 s from level 90
    [InlineData(100, -6)]
    [InlineData(40, -3)]   // below every threshold: the lowest one
    public void DefaultOffset_UsesTheDurationAtTheSyncLevel(int syncLevel, double expected)
    {
        var action = new ActionInfo
        {
            Id = 1,
            Name = "X",
            Kind = ActionKind.PartyMit,
            DurationByLevel = new Dictionary<int, double> { [50] = 10, [90] = 20 },
        };
        Assert.Equal(expected, TimingRules.DefaultOffset(action, syncLevel), Kit.Tolerance);
    }

    [Theory]
    [InlineData(0, -4)]      // instant: at least 4 s
    [InlineData(1.5, -4)]
    [InlineData(2, -4.5)]    // 2 s cast + 2.5 s
    [InlineData(2.5, -5)]
    public void DefaultOffset_Shield_LeadsByCastTimePlusTwoAndAHalfAtLeastFour(double castTime, double expected) =>
        Assert.Equal(expected, TimingRules.DefaultOffset(Kit.Act(1, "X", ActionKind.Shield, duration: 30, castTime: castTime), 90), Kit.Tolerance);

    [Theory]
    [InlineData(null)]
    [InlineData(10.0)]
    public void DefaultOffset_Invuln_IsTwoSecondsBefore(double? duration) =>
        Assert.Equal(-2, TimingRules.DefaultOffset(Kit.Act(1, "X", ActionKind.Invuln, duration: duration), 90), Kit.Tolerance);

    [Theory]
    [InlineData(null)]
    [InlineData(15.0)]
    public void DefaultOffset_Heal_IsOneSecondAfter(double? duration) =>
        Assert.Equal(1, TimingRules.DefaultOffset(Kit.Act(1, "X", ActionKind.Heal, duration: duration), 90), Kit.Tolerance);

    [Theory]
    [InlineData(ActionKind.Buff)]
    [InlineData(ActionKind.Movement)]
    [InlineData(ActionKind.Other)]
    public void DefaultOffset_BuffMovementOther_AreThreeSecondsBefore(ActionKind kind)
    {
        Assert.Equal(-3, TimingRules.DefaultOffset(Kit.Act(1, "X", kind), 90), Kit.Tolerance);
        Assert.Equal(-3, TimingRules.DefaultOffset(Kit.Act(1, "X", kind, duration: 20), 90), Kit.Tolerance);
    }

    [Theory]
    [InlineData(new[] { Job.PLD }, -3)]                                // tank LB3
    [InlineData(new[] { Job.PLD, Job.WAR, Job.DRK, Job.GNB }, -3)]     // tank LB1/2
    [InlineData(new[] { Job.WHM, Job.SCH, Job.AST, Job.SGE }, 2)]      // healer LB
    [InlineData(new[] { Job.SGE }, 2)]
    [InlineData(new[] { Job.MNK, Job.DRG }, -3)]                       // other roles
    [InlineData(new Job[0], -3)]                                       // jobs unknown
    [InlineData(new[] { Job.WHM, Job.PLD }, -3)]                       // mixed roles
    public void DefaultOffset_LimitBreak_DependsOnTheRoleOfItsJobs(Job[] jobs, double expected) =>
        Assert.Equal(expected, TimingRules.DefaultOffset(Kit.Act(1, "LB", ActionKind.LimitBreak, jobs: jobs), 90), Kit.Tolerance);

    // ---- UseWindow -----------------------------------------------------------------------------

    [Fact]
    public void UseWindow_Mitigation_EffectMustLastPastTheHit_UsableUntilJustBeforeIt()
    {
        var (earliest, latest) = TimingRules.UseWindow(Kit.Temperance, 90, hit: 100, pressAt: 94, Defaults);
        Assert.Equal(81, earliest, Kit.Tolerance);  // 100 − 20 + 1
        Assert.Equal(99.7, latest, Kit.Tolerance);  // 100 − 0.3
    }

    [Theory]
    [InlineData(185u, 95.5, 71.0)]    // Adloquium, 30 s shield
    [InlineData(133u, 95.5, 86.0)]    // Medica II, 15 s regen
    [InlineData(30u, 98.0, 91.0)]     // Hallowed Ground, 10 s invuln
    [InlineData(7432u, 96.0, 86.0)]   // Divine Benison, 15 s shield
    public void UseWindow_DurationEffects_LatestJustBeforeTheHit(uint actionId, double pressAt, double expectedEarliest)
    {
        var action = Kit.Catalogued.Single(a => a.Id == actionId);
        var (earliest, latest) = TimingRules.UseWindow(action, 90, 100, pressAt, Defaults);
        Assert.Equal(expectedEarliest, earliest, Kit.Tolerance);
        Assert.Equal(99.7, latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_ShortEffect_EarliestIsNeverAfterThePressTime()
    {
        var (earliest, _) = TimingRules.UseWindow(Kit.Act(1, "X", ActionKind.Buff, duration: 2), 90, 100, 97, Defaults);
        Assert.Equal(97, earliest, Kit.Tolerance);  // 100 − 2 + 1 = 99 clamps to the press time
    }

    [Theory]
    [InlineData(ActionKind.Other)]
    [InlineData(ActionKind.Heal)]
    [InlineData(ActionKind.Invuln)]
    [InlineData(ActionKind.PartyMit)]
    public void UseWindow_UnknownDuration_EarliestIsTenSecondsBeforeThePress(ActionKind kind)
    {
        var (earliest, _) = TimingRules.UseWindow(Kit.Act(1, "X", kind), 90, 100, 97, Defaults);
        Assert.Equal(87, earliest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_Note_FollowsTheMitigationRules()
    {
        var (earliest, latest) = TimingRules.UseWindow(null, 90, 100, 97, Defaults);
        Assert.Equal(87, earliest, Kit.Tolerance);
        Assert.Equal(99.7, latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_ZeroDuration_CountsAsUnknown()
    {
        var (earliest, _) = TimingRules.UseWindow(Kit.Act(1, "X", ActionKind.Heal, duration: 0), 90, 100, 101, Defaults);
        Assert.Equal(91, earliest, Kit.Tolerance);
    }

    [Theory]
    [InlineData(100, 100.5)]   // pressed on the hit: still due for half a second
    [InlineData(99.9, 100.4)]
    [InlineData(99.5, 100)]
    [InlineData(99, 99.7)]
    public void UseWindow_Mitigation_LatestIsNeverBeforeHalfASecondAfterThePress(double pressAt, double expectedLatest)
    {
        var (_, latest) = TimingRules.UseWindow(Kit.Temperance, 90, 100, pressAt, Defaults);
        Assert.Equal(expectedLatest, latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_Heal_UsableUntilTheLateWindowAfterThePress()
    {
        var (earliest, latest) = TimingRules.UseWindow(Kit.Tetragrammaton, 90, 100, 101, Defaults);
        Assert.Equal(91, earliest, Kit.Tolerance);
        Assert.Equal(106, latest, Kit.Tolerance);

        // also when the plan puts the heal before the hit
        Assert.Equal(100, TimingRules.UseWindow(Kit.Tetragrammaton, 90, 100, 95, Defaults).Latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_HealerLimitBreak_IsTreatedLikeAHeal()
    {
        var lb = Kit.Act(1, "Pulse of Life", ActionKind.LimitBreak, jobs: [Job.WHM, Job.SCH, Job.AST, Job.SGE]);
        var (earliest, latest) = TimingRules.UseWindow(lb, 90, 100, 102, Defaults);
        Assert.Equal(92, earliest, Kit.Tolerance);
        Assert.Equal(107, latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_TankLimitBreak_IsTreatedLikeMitigation()
    {
        var lb = Kit.Act(1, "Last Bastion", ActionKind.LimitBreak, jobs: [Job.PLD]);
        Assert.Equal(99.7, TimingRules.UseWindow(lb, 90, 100, 97, Defaults).Latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_PlannedAfterTheHit_UsableUntilTheLateWindowAfterThePress()
    {
        var (earliest, latest) = TimingRules.UseWindow(Kit.Temperance, 90, 100, 102, Defaults);
        Assert.Equal(81, earliest, Kit.Tolerance);
        Assert.Equal(107, latest, Kit.Tolerance);

        Assert.Equal(106, TimingRules.UseWindow(null, 90, 100, 101, Defaults).Latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_UsesTheOptions()
    {
        var options = new ReminderOptions { MitigationLatestBeforeHit = 1, HealLateWindow = 8 };
        Assert.Equal(99, TimingRules.UseWindow(Kit.Temperance, 90, 100, 94, options).Latest, Kit.Tolerance);
        Assert.Equal(109, TimingRules.UseWindow(Kit.Tetragrammaton, 90, 100, 101, options).Latest, Kit.Tolerance);
        Assert.Throws<ArgumentNullException>(() => TimingRules.UseWindow(null, 90, 100, 97, null!));
    }

    [Fact]
    public void IsUsedAfterHit_HealsAndHealerLimitBreaksOnly()
    {
        Assert.True(TimingRules.IsUsedAfterHit(Kit.Tetragrammaton));
        Assert.True(TimingRules.IsUsedAfterHit(Kit.Act(1, "LB", ActionKind.LimitBreak, jobs: [Job.AST])));
        Assert.False(TimingRules.IsUsedAfterHit(Kit.Act(1, "LB", ActionKind.LimitBreak, jobs: [Job.GNB])));
        Assert.False(TimingRules.IsUsedAfterHit(Kit.Temperance));
        Assert.False(TimingRules.IsUsedAfterHit(null));
    }
}
