using MitCue.Core.Actions;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;

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

    // ---- when: before / after (actions.yaml when + lead) -------------------------------------------

    [Theory]
    [InlineData(20.0, -6)]     // Liturgy of the Bell
    [InlineData(45.0, -8)]     // Excogitation: 13.5 s clamps down to 8 s
    [InlineData(15.0, -4.5)]   // Macrocosmos
    [InlineData(10.0, -3)]     // Horoscope
    [InlineData(5.0, -2)]      // 1.5 s clamps up to 2 s
    [InlineData(null, -3)]     // duration unknown
    [InlineData(0.0, -3)]      // no lasting effect counts as unknown
    public void DefaultOffset_HealPressedBefore_WithoutLead_IsTimedLikeMitigationOfTheSameDuration(double? duration, double expected) =>
        Assert.Equal(expected, TimingRules.DefaultOffset(Kit.Act(1, "X", ActionKind.Heal, duration: duration, when: UseTiming.Before), 90), Kit.Tolerance);

    [Theory]
    [InlineData(ActionKind.Heal, 10.0, -10)]       // Earthly Star: the star needs 10 s to mature
    [InlineData(ActionKind.Heal, 2.5, -2.5)]
    [InlineData(ActionKind.Heal, 30.0, -30)]       // no clamp: the lead is the data file's minimum
    [InlineData(ActionKind.Shield, 7.0, -7)]       // the lead wins over the kind's rule (−4)...
    [InlineData(ActionKind.PartyMit, 1.0, -1)]     // ...also when it is shorter (−6 for 20 s)
    [InlineData(ActionKind.LimitBreak, 5.0, -5)]
    public void DefaultOffset_PressedBefore_WithLead_IsHitMinusLead(ActionKind kind, double lead, double expected) =>
        Assert.Equal(expected, TimingRules.DefaultOffset(Kit.Act(1, "X", kind, duration: 20, when: UseTiming.Before, lead: lead), 90), Kit.Tolerance);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-4.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void DefaultOffset_PressedBefore_UnusableLead_IsIgnored(double lead) =>
        Assert.Equal(-6, TimingRules.DefaultOffset(Kit.Act(1, "X", ActionKind.Heal, duration: 20, when: UseTiming.Before, lead: lead), 90), Kit.Tolerance);

    [Fact]
    public void DefaultOffset_PressedBefore_OtherKindsKeepTheirOwnBeforeRule()
    {
        static double Before(ActionInfo a) => TimingRules.DefaultOffset(new ActionInfo
        {
            Id = a.Id,
            Name = a.Name,
            Kind = a.Kind,
            CastTime = a.CastTime,
            Jobs = a.Jobs,
            DurationByLevel = a.DurationByLevel,
            When = UseTiming.Before,
        }, 90);

        Assert.Equal(-6, Before(Kit.Temperance), Kit.Tolerance);        // party mit, 20 s
        Assert.Equal(-4, Before(Kit.DivineBenison), Kit.Tolerance);     // instant shield
        Assert.Equal(-4.5, Before(Kit.Adloquium), Kit.Tolerance);       // 2 s cast shield
        Assert.Equal(-4.5, Before(Kit.MedicaII), Kit.Tolerance);        // regen, 15 s
        Assert.Equal(-2, Before(Kit.HallowedGround), Kit.Tolerance);    // invuln
        Assert.Equal(-3, Before(Kit.Swiftcast), Kit.Tolerance);         // buff
        Assert.Equal(-3, Before(Kit.Act(1, "LB", ActionKind.LimitBreak, jobs: [Job.PLD])), Kit.Tolerance);
        Assert.Equal(-3, Before(Kit.Act(1, "LB", ActionKind.LimitBreak, jobs: [Job.WHM])), Kit.Tolerance);           // healer LB: no own before rule, duration unknown
        Assert.Equal(-4.5, Before(Kit.Act(1, "LB", ActionKind.LimitBreak, duration: 15, jobs: [Job.WHM])), Kit.Tolerance);
    }

    [Theory]
    [InlineData(ActionKind.Heal, 1)]
    [InlineData(ActionKind.PartyMit, 1)]     // after the hit even though mitigation is normally before
    [InlineData(ActionKind.Shield, 1)]
    [InlineData(ActionKind.Other, 1)]
    [InlineData(ActionKind.LimitBreak, 2)]   // healer LBs, and any LB the data marks "after"
    public void DefaultOffset_PressedAfter_IsAfterTheHit(ActionKind kind, double expected)
    {
        Assert.Equal(expected, TimingRules.DefaultOffset(Kit.Act(1, "X", kind, duration: 20, jobs: [Job.PLD], when: UseTiming.After), 90), Kit.Tolerance);
        Assert.Equal(expected, TimingRules.DefaultOffset(Kit.Act(1, "X", kind, duration: 20, jobs: [Job.PLD], when: UseTiming.After, lead: 10), 90), Kit.Tolerance); // a lead only applies before the hit
    }

    [Fact]
    public void DefaultOffset_DefaultTiming_IgnoresTheLead() =>
        Assert.Equal(1, TimingRules.DefaultOffset(Kit.Act(1, "X", ActionKind.Heal, duration: 20, lead: 10), 90), Kit.Tolerance);

    [Fact]
    public void IsUsedAfterHit_FollowsWhen()
    {
        Assert.False(TimingRules.IsUsedAfterHit(Kit.Bell));                // heal placed before the damage
        Assert.False(TimingRules.IsUsedAfterHit(Kit.EarthlyStar));
        Assert.True(TimingRules.IsUsedAfterHit(Kit.PulseOfLife));
        Assert.True(TimingRules.IsUsedAfterHit(Kit.Act(1, "X", ActionKind.PartyMit, duration: 20, when: UseTiming.After)));
        Assert.False(TimingRules.IsUsedAfterHit(Kit.Act(1, "LB", ActionKind.LimitBreak, jobs: [Job.AST], when: UseTiming.Before)));
    }

    [Fact]
    public void UseWindow_PreplacedHeal_CoversTheHitLikeMitigation()
    {
        var (earliest, latest) = TimingRules.UseWindow(Kit.Bell, 90, hit: 100, pressAt: 94, Defaults);
        Assert.Equal(81, earliest, Kit.Tolerance);   // 20 s: placed up to 19 s before the hit is on time
        Assert.Equal(99.7, latest, Kit.Tolerance);   // must be placed before the damage, not press + 5

        (earliest, latest) = TimingRules.UseWindow(Kit.Excogitation, 90, hit: 100, pressAt: 92, Defaults);
        Assert.Equal(56, earliest, Kit.Tolerance);   // 45 s: up to 44 s before
        Assert.Equal(99.7, latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_PreplacedHeal_UsesItsLongestEffect()
    {
        var (earliest, latest) = TimingRules.UseWindow(Kit.Horoscope, 90, hit: 100, pressAt: 97, Defaults);
        Assert.Equal(71, earliest, Kit.Tolerance);   // Horoscope Helios lasts 30 s, not the plain 10 s
        Assert.Equal(99.7, latest, Kit.Tolerance);

        Assert.Equal(81, TimingRules.UseWindow(Kit.EarthlyStar, 90, hit: 100, pressAt: 90, Defaults).Earliest, Kit.Tolerance); // 20 s star, effects 10 + 10

        // Default-timed actions keep the main duration even when an effect lasts longer.
        var temperance = Kit.Act(1, "Temperance", ActionKind.PartyMit, duration: 20, effects: [Kit.Effect("Divine Grace", 30)]);
        Assert.Equal(81, TimingRules.UseWindow(temperance, 90, hit: 100, pressAt: 94, Defaults).Earliest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_PreplacedHeal_EffectsWithoutPositiveDurationAreIgnored()
    {
        var action = Kit.Act(1, "X", ActionKind.Heal, when: UseTiming.Before, effects: [Kit.Effect("none yet", 0)]);
        Assert.Equal(87, TimingRules.UseWindow(action, 90, hit: 100, pressAt: 97, Defaults).Earliest, Kit.Tolerance); // unknown: press − 10

        var onlyEffect = Kit.Act(1, "X", ActionKind.Heal, when: UseTiming.Before, effects: [Kit.Effect("lingering", 12)]);
        Assert.Equal(89, TimingRules.UseWindow(onlyEffect, 90, hit: 100, pressAt: 97, Defaults).Earliest, Kit.Tolerance);
    }

    [Theory]
    [InlineData(100, 100.5)]   // pressed on the hit: still due for half a second
    [InlineData(99.5, 100)]
    [InlineData(99.2, 99.7)]   // exactly where both bounds meet
    [InlineData(90, 99.7)]
    public void UseWindow_PreplacedHeal_LatestIsNeverBeforeHalfASecondAfterThePress(double pressAt, double expectedLatest) =>
        Assert.Equal(expectedLatest, TimingRules.UseWindow(Kit.Bell, 90, 100, pressAt, Defaults).Latest, Kit.Tolerance);

    [Fact]
    public void UseWindow_PreplacedHeal_PlannedAfterTheHit_GetsTheLateWindow()
    {
        var (earliest, latest) = TimingRules.UseWindow(Kit.Bell, 90, 100, 100.01, Defaults);
        Assert.Equal(81, earliest, Kit.Tolerance);
        Assert.Equal(105.01, latest, Kit.Tolerance);
    }

    [Fact]
    public void UseWindow_PressedAfter_UsableUntilTheLateWindowAfterThePress()
    {
        var mitAfter = Kit.Act(1, "X", ActionKind.PartyMit, duration: 20, when: UseTiming.After);
        Assert.Equal(100, TimingRules.UseWindow(mitAfter, 90, 100, 95, Defaults).Latest, Kit.Tolerance);  // even when planned before the hit
        var (earliest, latest) = TimingRules.UseWindow(Kit.PulseOfLife, 90, 100, 102, Defaults);
        Assert.Equal(92, earliest, Kit.Tolerance);
        Assert.Equal(107, latest, Kit.Tolerance);
    }

    /// <summary>The actions.yaml entries with <c>when</c>/<c>lead</c> (docs/research/actions.md §6), at TOP's level 90.</summary>
    [Theory]
    [InlineData("Liturgy of the Bell", -6, 81, 99.7)]
    [InlineData("Excogitation", -8, 56, 99.7)]
    [InlineData("Earthly Star", -10, 81, 99.7)]
    [InlineData("Horoscope", -3, 71, 99.7)]
    [InlineData("Macrocosmos", -4.5, 86, 99.7)]
    [InlineData("Tetragrammaton", 1, 91, 106)]          // a heal without "when": after the damage
    [InlineData("Healing Wind", 2, 92, 107)]            // healer LB1 ("when: after")
    [InlineData("Pulse of Life", 2, 92, 107)]
    public void RealCatalog_PressTimesAndWindows(string name, double offset, double earliest, double latest)
    {
        var action = RepoFiles.Action(name);
        var pressAt = 100 + TimingRules.DefaultOffset(action, 90);
        Assert.Equal(offset, pressAt - 100, Kit.Tolerance);

        var window = TimingRules.UseWindow(action, 90, 100, pressAt, Defaults);
        Assert.Equal(earliest, window.Earliest, Kit.Tolerance);
        Assert.Equal(latest, window.Latest, Kit.Tolerance);
    }

    [Fact]
    public void RealCatalog_EveryBeforeActionIsPressedBeforeTheHit_EveryAfterActionAfterIt()
    {
        var actions = RepoFiles.Actions.All.ToList();
        Assert.Contains(actions, a => a.When == UseTiming.Before);
        Assert.Contains(actions, a => a.When == UseTiming.After);
        foreach (var action in actions)
        {
            foreach (var level in new[] { 80, 90, 100 })
            {
                var offset = TimingRules.DefaultOffset(action, level);
                var (earliest, latest) = TimingRules.UseWindow(action, level, 100, 100 + offset, Defaults);
                Assert.True(earliest <= 100 + offset && 100 + offset < latest, $"{action} at {level}: window [{earliest}, {latest}] around press {100 + offset}");
                if (action.When == UseTiming.Before)
                {
                    Assert.True(offset < 0, $"{action}: offset {offset}");
                    Assert.True(latest < 100, $"{action}: latest {latest}");
                    if (action.Lead is { } lead)
                        Assert.Equal(-lead, offset, Kit.Tolerance);
                }
                else if (action.When == UseTiming.After)
                {
                    Assert.True(offset > 0, $"{action}: offset {offset}");
                    Assert.True(TimingRules.IsUsedAfterHit(action), action.ToString());
                }
            }
        }
    }
}
