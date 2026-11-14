using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Core.Reminders;

/// <summary>Default timing rules (docs/DESIGN.md §5.1, use windows §5.2).</summary>
/// <remarks>
/// <para>
/// All times are timeline seconds; offsets are relative to the anchor hit (negative = before it).
/// An action's effect duration D is "known" when the action has a positive duration at the sync
/// level (a zero or missing duration means "no lasting effect / unknown").
/// </para>
/// <para>
/// <see cref="ForEntry"/> times a whole plan entry: every caller that needs an entry's press time or use windows (the
/// scheduler, the importer's estimates, the import review) goes through it, so they agree. <see cref="DefaultOffset"/>
/// and <see cref="UseWindow"/> are its rules for one action.
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
/// Horoscope and Macrocosmos (heal on a re-press or on expiry). Also actions with a lead of their own: Neutral Sect
/// (4.5 s, time to cast the Aspected Helios that puts up its barrier) and Passage of Arms (2 s, pressed last).</item>
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
    private const double AfterPreviousHit = 0.3;     // a later hit of a series: pressed this long after the hit before it
    private const double SameHit = 0.001;            // hits this close are one (a series hit and a mechanic of its own)
    private const double WrittenPrecision = 0.005;   // plan offsets are written to 0.01 s: a press this close to a floor is at it
    private const double CoverageMargin = 0.5;       // a later hit is covered when the effect outlasts it by this much...
    private const double CoverageLead = 2;           // ...also pressed on the NOW cue: the default due lead (1.5 s) + 0.5 s
    private const double CoverageSpanMargin = 2.5;   // the press is then still 0.5 s before the first hit

    // §5.2 use window.
    private const double ExpiryMargin = 1;           // an effect must outlast the hit by this much
    private const double UnknownDurationLead = 10;   // earliest use without a known duration: press − 10
    private const double MinDueAfterPress = 0.5;     // a reminder is always due for a moment after its press time

    /// <summary>The use window settings of <see cref="ForEntry"/> without options (never changed).</summary>
    private static readonly ReminderOptions DefaultOptions = new();

    /// <summary>The timing of a plan entry: its hit, its press time and each item's use window.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="mechanic">Its anchor mechanic (the fight's mechanic named by <see cref="PlanEntry.Mechanic"/>).</param>
    /// <param name="fight">The fight (sync level).</param>
    /// <param name="actions">The catalog the items' actions are resolved in.</param>
    /// <param name="options">Use window settings (the defaults when null).</param>
    /// <remarks>
    /// <para>
    /// The hit is the mechanic's hit at <see cref="PlanEntry.HitIndex"/>. All items share one press time: hit + the entry's
    /// offset, or without one the automatic offset. That is <see cref="DefaultOffset"/> of the entry's timing action, the
    /// first item action the catalog resolves (for an either-or item: its first resolvable option), with these rules of
    /// the anchor mechanic (fight file fields, <see cref="Mechanic"/>):
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Mechanic.PressAfter"/>: after the hit, like heals (+1 s, or the action's own later default: +2 s
    /// for a healer limit break).</item>
    /// <item><see cref="Mechanic.HealLead"/>: an entry timed by a heal used after the hit (<see cref="IsUsedAfterHit"/>) is
    /// pressed that long before the hit instead, to top the targets up for it.</item>
    /// </list>
    /// <para>
    /// Otherwise an entry timed by a heal used after the hit or by a buff, movement or other action (which give the other
    /// items no lead) is pressed with its earliest action pressed before the hit (<see cref="EarliestPreHitOffset"/>) when
    /// that is earlier: "Bless/Whisper/Succor" with Whispering Dawn's −6.3 s, so the Succor lands before the hit;
    /// "Reci/Deploy/Soil/Fey Illum" with Fey Illumination's −6 s, time for the whole combo. An entry timed by mitigation,
    /// a shield, a regen or an invuln keeps its action's lead ("Reprisal/Party Mit": Reprisal's −3 s still covers the
    /// mechanic's second hit).
    /// </para>
    /// <para>
    /// Several hits (the entry for the whole mechanic, no hit index): mitigation pressed before the first hit (party or
    /// tank mitigation, invulns) also covers the later hits its effect can reach. For each such item with a duration D,
    /// the last later hit hR with hR − first hit ≤ D − 2.5 s is covered: the press is no earlier than hR − D + 2 s, so even
    /// pressed on the NOW cue (1.5 s before) the effect outlasts hR by 0.5 s, and the press is still 0.5 s before the first
    /// hit (Optical Sight, hits 6.1 s apart: a 10 s Reprisal at −1.9 s instead of −3 s). A use that would end before hR + 0.5 s
    /// is early.
    /// </para>
    /// <para>
    /// A later hit of a <see cref="Mechanic.Series"/> (on that mechanic, or on one whose hit is that hit): the automatic
    /// press is never before the hit before it + 0.3 s (for an entry timed by a shield with a cast: minus the cast, so the
    /// shield lands then), and the reminder does not turn due before that either (<see cref="EntryTiming.DueNotBefore"/>).
    /// A written press at or after that floor keeps it too (a press the importer moved later, so that the action is ready
    /// again); one before the floor has none.
    /// </para>
    /// <para>
    /// Each item's use window is <see cref="UseWindow"/> of its own timing action around that press time; a use before the
    /// series' floor, when the entry has it, or with an automatic press one that ends before a later hit the item covers,
    /// is early.
    /// </para>
    /// </remarks>
    public static EntryTiming ForEntry(PlanEntry entry, Mechanic mechanic, FightDefinition fight, IActionCatalog actions, ReminderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(mechanic);
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(actions);
        options ??= DefaultOptions;

        var level = fight.SyncLevel;
        var hit = mechanic.HitAt(entry.HitIndex ?? 0);
        var items = entry.Do ?? [];
        var timing = new ActionInfo?[items.Count];
        for (var i = 0; i < items.Count; i++)
            timing[i] = items[i] is { } item ? TimingAction(item, actions) : null;

        var (automatic, timingAction) = AutomaticOffset(timing, mechanic, level);

        // Mitigation for the whole of a multi-hit mechanic also covers the later hits it can reach.
        var covers = new double?[items.Count];
        if (automatic < 0 && (entry.HitIndex ?? 0) == 0)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (CoveredHit(timing[i], mechanic, hit, level) is { } covered)
                {
                    covers[i] = covered.Hit - covered.Duration + CoverageMargin;
                    automatic = Math.Max(automatic, covered.Hit - covered.Duration + CoverageLead - hit);
                }
            }
        }

        // A later hit of a series: not before the hit before it (a cast shield lands then).
        double? floor = null;
        if (PreviousSeriesHit(fight, hit) is { } previous)
        {
            floor = previous + AfterPreviousHit - (timingAction is { Kind: ActionKind.Shield } shield ? shield.CastTime : 0);
            automatic = Math.Max(automatic, floor.Value - hit);
        }

        var isAutomatic = entry.Offset is null;
        var pressAt = hit + (entry.Offset ?? automatic);
        var notBefore = floor is { } f && pressAt >= f - WrittenPrecision ? floor : null;

        var windows = new ItemTiming[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var (earliest, latest) = UseWindow(timing[i], level, hit, pressAt, options);
            if (Later(isAutomatic ? covers[i] : null, notBefore) is { } notEarly)
                earliest = Math.Min(Math.Max(earliest, notEarly), pressAt);
            windows[i] = new ItemTiming(timing[i], earliest, latest);
        }

        return new EntryTiming
        {
            Hit = hit,
            AutomaticOffset = automatic,
            IsAutomatic = isAutomatic,
            PressAt = pressAt,
            DueNotBefore = notBefore,
            Items = windows,
        };
    }

    /// <summary>
    /// The earliest default offset among the actions pressed before the hit (not <see cref="IsUsedAfterHit"/>), when one
    /// is before the hit (negative); null when none is. Nulls (notes, unknown actions) are skipped.
    /// </summary>
    public static double? EarliestPreHitOffset(IEnumerable<ActionInfo?> actions, int syncLevel)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return EarliestPreHit(actions, syncLevel)?.Offset;
    }

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
            ? EarliestCoveringUse(hit, d)
            : pressAt - UnknownDurationLead;
        earliest = Math.Min(earliest, pressAt);

        var latest = IsUsedAfterHit(action) || pressAt > hit
            ? pressAt + options.HealLateWindow
            : Math.Max(hit - options.MitigationLatestBeforeHit, pressAt + MinDueAfterPress);

        return (earliest, latest);
    }

    /// <summary>
    /// The earliest use of an effect of <paramref name="duration"/> seconds that still covers <paramref name="hit"/>: one
    /// used earlier does not outlast the hit by 1 s (the expiry margin), so <see cref="UseWindow"/> starts here.
    /// </summary>
    public static double EarliestCoveringUse(double hit, double duration) => hit - duration + ExpiryMargin;

    /// <summary>
    /// When a reminder pressed at <paramref name="pressAt"/> turns due (its "NOW" cue): <see cref="ReminderOptions.DueLead"/>
    /// before the press time, but not before <paramref name="notBefore"/> (<see cref="EntryTiming.DueNotBefore"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static double DueAt(double pressAt, double? notBefore, ReminderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var due = pressAt - options.DueLead;
        return notBefore is { } floor ? Math.Max(due, floor) : due;
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

    /// <summary>
    /// The automatic offset of an entry whose items have these timing actions, before a series' floor (see
    /// <see cref="ForEntry"/>), and the action it is the default of (null for a note or an unknown action).
    /// </summary>
    private static (double Offset, ActionInfo? Timing) AutomaticOffset(IReadOnlyList<ActionInfo?> timing, Mechanic mechanic, int syncLevel)
    {
        ActionInfo? first = null;
        foreach (var action in timing)
        {
            if (action != null)
            {
                first = action;
                break;
            }
        }

        var offset = DefaultOffset(first, syncLevel);

        // The hit makes what was pressed before it pointless (HP set to 1): the calls are the heal-up after it.
        if (mechanic.PressAfter)
            return (Math.Max(offset, HealDelay), first);

        // Heals that top the targets up for the hit (the tanks at 1 HP before a tankbuster).
        if (mechanic.HealLead is { } healLead && IsUsedAfterHit(first))
            return (-healLead, first);

        // A heal or a buff gives no lead of its own: an action of the entry that goes before the hit times it.
        if (first != null && (IsUsedAfterHit(first) || first.Kind is ActionKind.Buff or ActionKind.Movement or ActionKind.Other)
            && EarliestPreHit(timing, syncLevel) is { } earliest && earliest.Offset < offset)
        {
            return (earliest.Offset, earliest.Action);
        }

        return (offset, first);
    }

    /// <summary>The first action with the earliest default offset before the hit (see <see cref="EarliestPreHitOffset"/>).</summary>
    private static (ActionInfo Action, double Offset)? EarliestPreHit(IEnumerable<ActionInfo?> actions, int syncLevel)
    {
        (ActionInfo Action, double Offset)? earliest = null;
        foreach (var action in actions)
        {
            if (action == null || IsUsedAfterHit(action))
                continue;
            var offset = DefaultOffset(action, syncLevel);
            if (offset < 0 && (earliest is not { } e || offset < e.Offset))
                earliest = (action, offset);
        }

        return earliest;
    }

    /// <summary>
    /// For mitigation (party or tank mitigation, an invuln) pressed before the hit with a known duration: the last later
    /// hit of the mechanic it covers together with <paramref name="hit"/> (no more than its duration − 2.5 s after it), with
    /// that duration; else null.
    /// </summary>
    private static (double Hit, double Duration)? CoveredHit(ActionInfo? action, Mechanic mechanic, double hit, int syncLevel)
    {
        if (action is not { Kind: ActionKind.PartyMit or ActionKind.TankMit or ActionKind.Invuln } || IsUsedAfterHit(action)
            || (action.When == UseTiming.Before ? LongestEffect(action, syncLevel) : KnownDuration(action, syncLevel)) is not { } duration)
        {
            return null;
        }

        (double Hit, double Duration)? covered = null;
        foreach (var later in mechanic.Hits)
        {
            if (later > hit + SameHit && later - hit <= duration - CoverageSpanMargin)
                covered = (later, duration);
        }

        return covered;
    }

    /// <summary>The later of two optional times (null when both are).</summary>
    private static double? Later(double? a, double? b) => a is { } x && b is { } y ? Math.Max(x, y) : a ?? b;

    /// <summary>
    /// The hit before <paramref name="hit"/> in a <see cref="Mechanic.Series"/> of the fight that has it as a later hit, or
    /// null when no series does.
    /// </summary>
    private static double? PreviousSeriesHit(FightDefinition fight, double hit)
    {
        foreach (var series in fight.Mechanics)
        {
            if (!series.Series)
                continue;
            for (var k = 1; k < series.Hits.Count; k++)
            {
                if (Math.Abs(series.Hits[k] - hit) <= SameHit)
                    return series.Hits[k - 1];
            }
        }

        return null;
    }

    /// <summary>The timing action of an item: its action, or the first option of an either-or item the catalog resolves.</summary>
    private static ActionInfo? TimingAction(PlanItem item, IActionCatalog actions)
    {
        foreach (var id in item.ActionIds())
        {
            if (actions.Get(id) is { } action)
                return action;
        }

        return null;
    }

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
