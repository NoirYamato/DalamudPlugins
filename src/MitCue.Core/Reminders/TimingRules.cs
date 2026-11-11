using MitCue.Core.Actions;

namespace MitCue.Core.Reminders;

/// <summary>Default timing rules (docs/DESIGN.md §5.1, use windows §5.2).</summary>
/// <remarks>
/// <para>
/// All times are timeline seconds; offsets are relative to the anchor hit (negative = before it).
/// An action's effect duration D is "known" when the action has a positive duration at the sync
/// level (a zero or missing duration means "no lasting effect / unknown").
/// </para>
/// <para>
/// Which side of the hit an action is pressed on comes from <see cref="ActionInfo.When"/> (actions.yaml
/// <c>when</c>, docs/research/actions.md §6):
/// </para>
/// <list type="bullet">
/// <item><see cref="UseTiming.Default"/>: the kind decides. Heals and healer limit breaks go after the hit,
/// everything else (party/tank mitigation, shields, regens, invulns, buffs, tank limit breaks, other) before it.</item>
/// <item><see cref="UseTiming.Before"/>: pre-placed effects that act on a later event, pressed before the damage even
/// when they heal: Liturgy of the Bell (heals as the damage consumes its stacks), Excogitation (heals when the
/// target drops below 50 % HP), Earthly Star (detonated after the damage, full potency after maturing 10 s),
/// Horoscope and Macrocosmos (heal on a re-press or on expiry).</item>
/// <item><see cref="UseTiming.After"/>: pressed after the damage (the healer limit breaks).</item>
/// </list>
/// </remarks>
public static class TimingRules
{
    // §5.1 default press times.
    private const double FallbackLead = 3;           // notes, buffs, movement, other, unknown durations
    private const double DurationLeadShare = 0.3;    // mitigation/regen/pre-placed: 30 % of the effect duration...
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
    /// <para>By <see cref="ActionInfo.When"/> first:</para>
    /// <list type="bullet">
    /// <item><see cref="UseTiming.Before"/>: −<see cref="ActionInfo.Lead"/> when the action has a positive one (Earthly
    /// Star: −10, the star must mature before the damage). Without a lead, heals and healer limit breaks (which have no
    /// before-the-hit rule of their own) are timed like mitigation of the same duration, −clamp(0.3·D, 2, 8) or −3 when
    /// D is unknown (Liturgy of the Bell −6, Excogitation −8, Macrocosmos −4.5, Horoscope −3); every other kind
    /// keeps its own rule below, which already puts it before the hit.</item>
    /// <item><see cref="UseTiming.After"/>: +2 for limit breaks, else +1 (after the damage, like a heal).</item>
    /// <item><see cref="UseTiming.Default"/>: by kind. Party/tank mitigation and regens: −clamp(0.3·D, 2, 8), or −3 when
    /// D is unknown. Shields: −max(4, cast time + 2.5). Invulns: −2. Heals: +1. Buffs, movement and other: −3. Limit
    /// breaks: +2 for healer LBs (every job of the action is a healer), else −3.</item>
    /// </list>
    /// <para>Notes (<paramref name="action"/> null) and actions the catalog does not know: −3.</para>
    /// </remarks>
    public static double DefaultOffset(ActionInfo? action, int syncLevel)
    {
        if (action == null)
            return -FallbackLead;

        return action.When switch
        {
            UseTiming.Before => -BeforeHitLead(action, syncLevel),
            UseTiming.After => AfterHitDelay(action),
            _ => KindOffset(action, syncLevel),
        };
    }

    /// <summary>Earliest/latest useful use times for an item covering <paramref name="hit"/>.</summary>
    /// <remarks>
    /// <para>
    /// Earliest: hit − D + 1 when D is known (an effect started earlier expires before the hit), else press − 10;
    /// never after the press time. For <see cref="UseTiming.Before"/> actions D is the longest of their effects,
    /// because a pre-placed heal still works on the hit as long as any of its effects lasts (Horoscope becomes
    /// Horoscope Helios, 30 s, when Helios follows): Liturgy of the Bell placed up to 19 s, Excogitation up to
    /// 44 s and Earthly Star up to 19 s before the hit are on time, not early.
    /// </para>
    /// <para>
    /// Latest: press + <see cref="ReminderOptions.HealLateWindow"/> for actions used after the hit (see
    /// <see cref="IsUsedAfterHit"/>) and for anything planned after the hit (press &gt; hit). Otherwise, including
    /// the pre-placed heals, hit − <see cref="ReminderOptions.MitigationLatestBeforeHit"/>: the effect has to be in
    /// place when the damage lands. Never earlier than press + 0.5, so that a reminder is always due for a moment.
    /// </para>
    /// </remarks>
    public static (double Earliest, double Latest) UseWindow(ActionInfo? action, int syncLevel, double hit, double pressAt, ReminderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var duration = action?.When == UseTiming.Before ? LongestEffect(action, syncLevel) : KnownDuration(action, syncLevel);
        var earliest = duration is { } d
            ? hit - d + ExpiryMargin
            : pressAt - UnknownDurationLead;
        earliest = Math.Min(earliest, pressAt);

        var latest = IsUsedAfterHit(action) || pressAt > hit
            ? pressAt + options.HealLateWindow
            : Math.Max(hit - options.MitigationLatestBeforeHit, pressAt + MinDueAfterPress);

        return (earliest, latest);
    }

    /// <summary>
    /// True for actions meant for after the damage: <see cref="UseTiming.After"/> actions, and with
    /// <see cref="UseTiming.Default"/> heals and healer limit breaks. False for <see cref="UseTiming.Before"/>
    /// actions (pre-placed heals), notes and everything else.
    /// </summary>
    public static bool IsUsedAfterHit(ActionInfo? action) =>
        action != null && action.When switch
        {
            UseTiming.After => true,
            UseTiming.Before => false,
            _ => IsAfterHitKind(action),
        };

    /// <summary>The kind's default offset (<see cref="UseTiming.Default"/>), see <see cref="DefaultOffset"/>.</summary>
    private static double KindOffset(ActionInfo action, int syncLevel) => action.Kind switch
    {
        ActionKind.PartyMit or ActionKind.TankMit or ActionKind.Regen => -DurationLead(action, syncLevel),
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

    /// <summary>Seconds before the hit for a <see cref="UseTiming.Before"/> action, see <see cref="DefaultOffset"/>.</summary>
    private static double BeforeHitLead(ActionInfo action, int syncLevel)
    {
        if (action.Lead is { } lead && lead > 0 && double.IsFinite(lead))
            return lead;
        return IsAfterHitKind(action) ? DurationLead(action, syncLevel) : -KindOffset(action, syncLevel);
    }

    /// <summary>Seconds after the hit for a <see cref="UseTiming.After"/> action: 2 for limit breaks, else 1.</summary>
    private static double AfterHitDelay(ActionInfo action) =>
        action.Kind == ActionKind.LimitBreak ? HealerLimitBreakDelay : HealDelay;

    /// <summary>clamp(0.3·D, 2, 8) seconds, or 3 when D is unknown.</summary>
    private static double DurationLead(ActionInfo action, int syncLevel) =>
        KnownDuration(action, syncLevel) is { } d
            ? Math.Clamp(DurationLeadShare * d, MinDurationLead, MaxDurationLead)
            : FallbackLead;

    /// <summary>Kinds used after the hit by default: heals and healer limit breaks.</summary>
    private static bool IsAfterHitKind(ActionInfo action) =>
        action.Kind == ActionKind.Heal || (action.Kind == ActionKind.LimitBreak && CommonRole(action) == Role.Healer);

    /// <summary>The effect duration at the sync level when known (positive), else null.</summary>
    private static double? KnownDuration(ActionInfo? action, int syncLevel) =>
        action?.DurationAt(syncLevel) is { } d && d > 0 ? d : null;

    /// <summary>The longest of the main duration and every listed effect at the sync level (positive), else null.</summary>
    private static double? LongestEffect(ActionInfo? action, int syncLevel)
    {
        var longest = KnownDuration(action, syncLevel);
        if (action == null)
            return longest;
        foreach (var effect in action.Effects)
        {
            if (effect.DurationAt(syncLevel) is { } d && d > 0 && (longest is not { } l || d > l))
                longest = d;
        }
        return longest;
    }

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
