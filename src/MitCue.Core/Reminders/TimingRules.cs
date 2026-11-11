using MitCue.Core.Actions;

namespace MitCue.Core.Reminders;

/// <summary>Default timing rules (docs/DESIGN.md §5).</summary>
/// <remarks>
/// All times are timeline seconds; offsets are relative to the anchor hit (negative = before it).
/// An action's effect duration D is "known" when the action has a positive duration at the sync
/// level (a zero or missing duration means "no lasting effect / unknown").
/// </remarks>
public static class TimingRules
{
    // §5.1 default press times.
    private const double FallbackLead = 3;           // notes, buffs, movement, other, unknown durations
    private const double DurationLeadShare = 0.3;    // mitigation/regen: 30 % of the effect duration...
    private const double MinDurationLead = 2;        // ...but at least 2 s...
    private const double MaxDurationLead = 8;        // ...and at most 8 s before the hit
    private const double MinShieldLead = 4;
    private const double ShieldCastMargin = 2.5;     // cast time + margin, so the shield lands in time
    private const double InvulnLead = 2;
    private const double HealDelay = 1;              // heals go out after the damage
    private const double TankLimitBreakLead = 3;
    private const double HealerLimitBreakDelay = 2;

    // §5.2 use window.
    private const double ExpiryMargin = 1;           // an effect must outlast the hit by this much
    private const double UnknownDurationLead = 10;   // earliest use without a known duration: press − 10
    private const double MinDueAfterPress = 0.5;     // a reminder is always due for a moment after its press time

    /// <summary>Default offset (press − hit) for an action used without an explicit time.</summary>
    /// <remarks>
    /// Party/tank mitigation and regens: −clamp(0.3·D, 2, 8), or −3 when D is unknown.
    /// Shields: −max(4, cast time + 2.5). Invulns: −2. Heals: +1. Buffs, movement, other and
    /// notes (<paramref name="action"/> null): −3. Limit breaks: +2 for healer LBs (every job of
    /// the action is a healer), else −3.
    /// </remarks>
    public static double DefaultOffset(ActionInfo? action, int syncLevel)
    {
        if (action == null)
            return -FallbackLead;

        return action.Kind switch
        {
            ActionKind.PartyMit or ActionKind.TankMit or ActionKind.Regen =>
                KnownDuration(action, syncLevel) is { } d
                    ? -Math.Clamp(DurationLeadShare * d, MinDurationLead, MaxDurationLead)
                    : -FallbackLead,
            ActionKind.Shield => -Math.Max(MinShieldLead, action.CastTime + ShieldCastMargin),
            ActionKind.Invuln => -InvulnLead,
            ActionKind.Heal => HealDelay,
            ActionKind.LimitBreak => CommonRole(action) switch
            {
                Role.Tank => -TankLimitBreakLead,
                Role.Healer => HealerLimitBreakDelay,
                _ => -FallbackLead,
            },
            _ => -FallbackLead, // Buff, Movement, Other
        };
    }

    /// <summary>Earliest/latest useful use times for an item covering <paramref name="hit"/>.</summary>
    /// <remarks>
    /// Earliest: hit − D + 1 when D is known (an effect started earlier expires before the hit),
    /// else press − 10; never after the press time.
    /// Latest: press + <see cref="ReminderOptions.HealLateWindow"/> for heals, healer limit breaks
    /// and anything planned after the hit; otherwise hit − <see cref="ReminderOptions.MitigationLatestBeforeHit"/>,
    /// but never earlier than press + 0.5 so that a reminder is always due for a moment.
    /// </remarks>
    public static (double Earliest, double Latest) UseWindow(ActionInfo? action, int syncLevel, double hit, double pressAt, ReminderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var earliest = KnownDuration(action, syncLevel) is { } d
            ? hit - d + ExpiryMargin
            : pressAt - UnknownDurationLead;
        earliest = Math.Min(earliest, pressAt);

        var latest = IsUsedAfterHit(action) || pressAt > hit
            ? pressAt + options.HealLateWindow
            : Math.Max(hit - options.MitigationLatestBeforeHit, pressAt + MinDueAfterPress);

        return (earliest, latest);
    }

    /// <summary>True for actions meant for after the damage: heals and healer limit breaks.</summary>
    public static bool IsUsedAfterHit(ActionInfo? action) =>
        action != null && (action.Kind == ActionKind.Heal || (action.Kind == ActionKind.LimitBreak && CommonRole(action) == Role.Healer));

    /// <summary>The effect duration at the sync level when known (positive), else null.</summary>
    private static double? KnownDuration(ActionInfo? action, int syncLevel) =>
        action?.DurationAt(syncLevel) is { } d && d > 0 ? d : null;

    /// <summary>The role shared by every job of the action, or <see cref="Role.None"/> when there are no jobs or they differ.</summary>
    private static Role CommonRole(ActionInfo action)
    {
        var jobs = action.Jobs;
        if (jobs.Count == 0)
            return Role.None;
        var role = Jobs.RoleOf(jobs[0]);
        for (var i = 1; i < jobs.Count; i++)
        {
            if (Jobs.RoleOf(jobs[i]) != role)
                return Role.None;
        }
        return role;
    }
}
