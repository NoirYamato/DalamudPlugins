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
    private const double CoveringPressLead = 0.5;    // ...but pressed at least this long before the first hit
    private const double MinDueWindow = 1;           // ...and due at least this long before it (the latest use is then)
    private const double OwnLeadGuard = 2.5;         // an item's own lead presses an entry no earlier than D − 2.5 s for its
                                                     // other items: on the NOW cue (1.5 s before) they outlast the hit by 1 s
    private const double EffectBeforeHit = 1;        // what a press takes time to is in place this long before the hit, as
                                                     // "(Late)" has it (PlanImporter.LateOffset)
    private const double PlainGcdRecast = 3;         // a GCD with a recast under this has no cooldown of its own
                                                     // (PlanCheckOptions.MinRecast)

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
    /// The hit is the mechanic's hit at <see cref="PlanEntry.HitIndex"/>. The entry's press time is hit + its offset, or
    /// without one its automatic offset, which these rules give, in this order:
    /// </para>
    /// <list type="number">
    /// <item>
    /// The default of the entry's timing action (<see cref="DefaultOffset"/>), the first item action the catalog resolves
    /// (for an either-or item: its first resolvable option), with the anchor mechanic's fields first (fight file,
    /// <see cref="Mechanic"/>): <see cref="Mechanic.PressAfter"/> presses the entry after the hit, like heals (+1 s, or the
    /// action's own later default: +2 s for a healer limit break), and no rule below applies; with
    /// <see cref="Mechanic.HealLead"/> an entry timed by a heal used after the hit (<see cref="IsUsedAfterHit"/>) is pressed
    /// that long before the hit instead, to top the targets up for it. Otherwise an entry timed by a heal used after the
    /// hit or by a buff, movement or other action (which give the other items no lead) is pressed with its earliest action
    /// pressed before the hit (<see cref="EarliestPreHitOffset"/>) when that is earlier: "Bless/Whisper/Succor" with
    /// Whispering Dawn's −6.3 s, so the Succor lands before the hit; "Reci/Deploy/Soil/Fey Illum" with Fey Illumination's
    /// −6 s, time for the whole combo. An entry timed by mitigation, a shield, a regen or an invuln keeps its action's
    /// lead ("Reprisal/Party Mit": Reprisal's −3 s still covers the mechanic's second hit).
    /// </item>
    /// <item>
    /// An item's own lead (<see cref="ActionInfo.Lead"/> of a <see cref="UseTiming.Before"/> action) pulls the press
    /// earlier wherever the cell lists it: "CU + Neutral Sect" at Neutral Sect's −4.5 s, time for its Helios, like
    /// "Neutral Sect + CU". No lead, a pull or the timing action's own, presses the entry so early that another item's
    /// lasting effect D (mitigation, a shield, a regen, an invuln, a pre-placed heal) used on the NOW cue ends before the
    /// hit: no earlier than −(D − 2.5 s) ("CU + Earthly Star" and "Earthly Star + CU": −7.5 s, beside the 10 s CU; the
    /// star then matures 2.5 s after the hit, for its heal). The action whose lead pulled the press then times the entry
    /// (its series floor, below). Not on a <see cref="Mechanic.PressAfter"/> mechanic.
    /// </item>
    /// <item>
    /// Several hits (the entry for the whole mechanic, no hit index): mitigation pressed before the first hit (party or
    /// tank mitigation, invulns) also covers the later hits its effect can reach, the mechanic's and its damage times that
    /// are no timeline entries (<see cref="Mechanic.SubHits"/>: the four later cleaves of TOP's Diffuse Wave Cannon Kyrios,
    /// which a tank's invuln must last through, the five later Splashes of TEA's first). For each such item with a duration
    /// D, the last later hit hR it can cover is covered: the press is no earlier than hR − D + 2 s, so even pressed on the
    /// NOW cue (1.5 s before) the effect outlasts hR by 0.5 s (Optical Sight, hits 6.1 s apart: a 10 s Reprisal at −1.9 s
    /// instead of −3 s), and the item's NOW cue never comes before hR − D + 0.5 s (<see cref="ItemTiming.DueNotBefore"/>),
    /// so that a press on it still reaches hR. The item that needs the latest press times the entry, up to 0.5 s before the
    /// first hit (hR − first hit ≤ D − 2.5 s); an item reaches a later hit than that on its own, pressed 0.5 s before the
    /// first hit (<see cref="ItemTiming.PressAt"/>), while the entry's other items keep their press (TOP's Wave Cannon, the
    /// Wild Charge 8.3 s after the first protean: a 10 s Oblation at −0.5 s turns due 1.2 s before the first protean, and
    /// the Shadow Wall and The Blackest Night of its cell keep −4.5 s). It can cover hR when hR − first hit ≤ D − 1 s − the
    /// latest use before the hit (<see cref="ReminderOptions.MitigationLatestBeforeHit"/>), and that NOW cue still comes
    /// 1 s before the first hit, as late as it may be used: hR − first hit ≤ D − 1.5 s (a 10 s call on TEA's Wormhole does
    /// not wait for the #5 Alpha Sword 8.6 s after the Super Jump). A use that would end before hR + 0.5 s is early. Only
    /// as late as the entry's other items allow: an item pressed before the hit that takes time to its effect
    /// (<see cref="TimeToEffect"/>: a cast, or the GCD a follow-up needs first) has it in place 1 s before the first hit,
    /// as "(Late)" does ("Soil + Succor": −3 s at the latest), and an item with a lead of its own that is no mitigation
    /// keeps it (Earthly Star 10 s, Neutral Sect 4.5 s). A later hit that would need a later press, the entry's or the
    /// item's own, is not covered, so that the items' earliest uses stay those of the press ("Soil + Succor" on TOP's
    /// Pantokrator keeps the Succor's −4.5 s and covers the second line stack, not the third).
    /// </item>
    /// <item>
    /// A later hit of a <see cref="Mechanic.Series"/> (on that mechanic, or on one whose hit is that hit): nothing is
    /// pressed before the hit before it + 0.3 s, so that a shield is not spent on that hit and mitigation lasts through the
    /// later ones; an action whose effect lands a while after its press is pressed that much earlier, a shield by its cast,
    /// an action whose barrier comes from a cast after it by that cast (<see cref="ActionInfo.BarrierCast"/>: Neutral
    /// Sect's 1.5 s Aspected Helios), unless the entry lists an action that makes the next spell instant
    /// (<see cref="ActionInfo.InstantNextCast"/>: "Swiftcast/Succor" waits for the hit before it + 0.3 s, so that the
    /// instant Succor is not spent on it). The timing action's floor is the entry's, or the earlier floor of an item whose
    /// barrier comes from a cast after it, as its lead counts wherever the cell lists it (Neutral Sect beside Earthly Star,
    /// which times the entry; a shield beside the timing action waits for its floor): the automatic press is never before
    /// it, and the entry's reminders do not turn due before it (<see cref="EntryTiming.DueNotBefore"/>). Each item has a
    /// floor of its own action, never earlier than the entry's (<see cref="ItemTiming.DueNotBefore"/>), and an automatic
    /// press presses it no earlier: in "Succor/Soil/Seraph" on J Wave 5, timed by the Succor's 2 s cast 1.8 s before wave
    /// 4 + 0.3 s, the instant Sacred Soil and Consolation wait until then. A buff, movement or other action without a lead
    /// of its own has no effect the hit before could spend, nor has a note or an action the catalog does not know: it keeps
    /// the entry's floor, so that it comes with the cast it changes or goes with ("Reci → Succor": the Recitation with the
    /// Succor, 1.8 s before wave 4 + 0.3 s; "Stack middle → Succor": the note with it), and as the trigger of a follow-up
    /// the entry lists it waits with it (the Summon Seraph of "Succor/Soil/Seraph" with its Consolation). With an automatic
    /// press a step of a sequence, which comes before the step after it, has no later floor than that step: in "Indom →
    /// Succor" on J Wave 5 the Indomitability, a heal that alone waits for wave 4 + 0.3 s, comes with the Succor 1.8 s
    /// earlier, so that the Succor's cast still lands between waves 4 and 5. A written press keeps each floor it is at or
    /// after (a press the importer moved later, so that the action is ready again); one before the entry's floor has none.
    /// </item>
    /// </list>
    /// <para>
    /// Every item is pressed at the entry's press time, except that with an automatic press an item waits for its own
    /// series floor or for a later hit it reaches on its own (<see cref="ItemTiming.PressAt"/>) and a step of a sequence
    /// (<see cref="PlanEntry.Seq"/>) for the step before it (which waits for no later series floor than it: "Indom →
    /// Succor" on J Wave 5, both at the Succor's 718.4), and that an item pressed last (<see cref="IsPressedLast"/>:
    /// Passage of Arms, a channel any other action ends) comes with the entry's last item; the automatic press is then the
    /// earliest of the items' (<see cref="EntryTiming.AutomaticOffset"/>). Each item's use window is
    /// <see cref="UseWindow"/> of its own timing action around its press time; a use before the item's series floor, when
    /// it has one, or with an automatic press one that ends before a later hit the item covers, is early, and the item's
    /// reminder does not turn due before then either. Nor does it turn due before its earliest use that is not early, when
    /// its press, automatic or written, is at or after that use and the default due lead (1.5 s) would turn it due earlier:
    /// a press on the NOW cue is then not early ("Rampart + Dark Mind + Oblation + TBN" on TOP's Cosmo Dive, pressed with
    /// Rampart's −6 s: The Blackest Night, a 7 s shield, turns due 6 s before the hit, not 7.5 s; an 8 s Heart of Corundum
    /// 7 s before it). A written press before that earliest use (the sheet asks for an early press) keeps its plain NOW
    /// cue, and a due lead of the player's own longer than 1.5 s still applies to the other items. An item pressed last
    /// does not turn due before any other item of the entry, and the scheduler lists it after them ("Passage of Arms +
    /// Reprisal" on Wave Cannon: both 0.5 s before the first protean, due with the Reprisal, whose NOW cue waits for the
    /// Wild Charge, and listed after it). A step of a sequence does not turn due before the step before it either, whatever
    /// the press, but is early only by its own window.
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

        // Mitigation for the whole of a multi-hit mechanic also covers the later hits it can reach, as late as the entry's
        // other items allow: the entry is pressed for the hits each reaches pressed with it, an item that reaches a later
        // one only pressed just before the first hit is pressed then on its own; its NOW cue never comes before the
        // earliest use that still reaches them.
        var covers = new double?[items.Count];
        var reach = new double?[items.Count];
        if (automatic < 0 && (entry.HitIndex ?? 0) == 0)
        {
            var latest = LatestCoveringOffset(timing, actions, level);
            for (var i = 0; i < items.Count; i++)
            {
                if (CoveredHit(timing[i], mechanic, hit, level, latest, options) is { } covered)
                {
                    covers[i] = covered.NotEarly;
                    reach[i] = covered.Press;
                    if (covered.EntryPress is { } press)
                        automatic = Math.Max(automatic, press - hit);
                }
            }
        }

        // A later hit of a series: not before the hit before it (a cast shield, or a barrier cast after the press, lands then).
        var previous = PreviousSeriesHit(fight, hit);
        var floor = previous.HasValue ? EntryFloor(previous.Value, timingAction, timing) : (double?)null;
        if (floor.HasValue)
            automatic = Math.Max(automatic, floor.Value - hit);

        // Each item's series floor (ItemFloor: never earlier than the entry's, an instant shield beside the cast one that
        // times the entry lands after the hit before it too; a buff or a note keeps the entry's). With an automatic press a
        // step of a sequence, pressed before the step after it, waits for no later floor than that step.
        var isAutomatic = entry.Offset is null;
        var floors = new double?[items.Count];
        for (var i = 0; i < items.Count; i++)
            floors[i] = previous.HasValue && floor.HasValue ? ItemFloor(previous.Value, floor.Value, timing, i) : null;
        if (entry.Seq && isAutomatic)
        {
            for (var i = items.Count - 2; i >= 0; i--)
            {
                if (floors[i] is { } own && floors[i + 1] is { } next)
                    floors[i] = Math.Min(own, next);
            }
        }

        // Each item's automatic press: the entry's, or later for an item that waits for its own series floor or for a later
        // hit it reaches on its own, and a step of a sequence never before the step before it. An item pressed last (Passage
        // of Arms, a channel any other action ends) comes with the entry's last item. The entry's automatic press is the
        // earliest of them.
        var automaticPresses = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            automaticPresses[i] = Math.Max(hit + automatic, Math.Max(floors[i] ?? double.NegativeInfinity, reach[i] ?? double.NegativeInfinity));
            if (entry.Seq && i > 0)
                automaticPresses[i] = Math.Max(automaticPresses[i], automaticPresses[i - 1]);
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (IsPressedLast(timing[i]))
                automaticPresses[i] = automaticPresses.Max();
        }

        var automaticPress = items.Count > 0 ? automaticPresses.Min() : hit + automatic;
        var pressAt = entry.Offset is { } offset ? hit + offset : automaticPress;
        var notBefore = AtOrAfter(pressAt, floor);

        // Each item's press (a written one is every item's), and the time its reminder does not turn due before: a written
        // press keeps each series floor it is at or after, and no press turns due before an earliest use it is at or after.
        var presses = new double[items.Count];
        var dueFloors = new double?[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            presses[i] = isAutomatic ? automaticPresses[i] : pressAt;
            var itemNotBefore = floors[i] is { } own ? AtOrAfter(presses[i], own) ?? notBefore : notBefore;
            dueFloors[i] = Later(Later(isAutomatic ? covers[i] : null, itemNotBefore), OnTimeCue(timing[i], level, hit, presses[i]));
        }

        // An item pressed last turns due no earlier than any other: the others' NOW cues may be held (a series floor, a
        // later hit, an earliest use).
        for (var i = 0; i < items.Count; i++)
        {
            if (!IsPressedLast(timing[i]))
                continue;
            for (var j = 0; j < items.Count; j++)
            {
                if (j != i && dueFloors[j] is { } other)
                    dueFloors[i] = Later(dueFloors[i], Math.Min(other, presses[i]));
            }
        }

        // A step of a sequence does not turn due before the step before it, but is early only by its own window.
        var windows = new ItemTiming[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var (earliest, latest) = UseWindow(timing[i], level, hit, presses[i], options);
            if (dueFloors[i] is { } notEarly)
                earliest = Math.Min(Math.Max(earliest, notEarly), presses[i]);
            var due = entry.Seq && i > 0 && windows[i - 1].DueNotBefore is { } stepBefore ? Later(dueFloors[i], Math.Min(stepBefore, presses[i])) : dueFloors[i];
            windows[i] = new ItemTiming(timing[i], presses[i], earliest, latest, due);
        }

        return new EntryTiming
        {
            Hit = hit,
            AutomaticOffset = automaticPress - hit,
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

        var duration = CoverageDuration(action, syncLevel);
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
    /// How long a use of <paramref name="action"/> covers a hit (<see cref="UseWindow"/>, and in <see cref="ForEntry"/> the
    /// later hits mitigation reaches and the other items' guard against an own lead): the longest of its effects for a
    /// <see cref="UseTiming.Before"/> action (Neutral Sect's 30 s barrier), else its duration; null when unknown.
    /// </summary>
    public static double? CoverageDuration(ActionInfo? action, int syncLevel) =>
        action?.When == UseTiming.Before ? LongestEffect(action, syncLevel) : KnownDuration(action, syncLevel);

    /// <summary>
    /// The earliest use of an effect of <paramref name="duration"/> seconds that still covers <paramref name="hit"/>: one
    /// used earlier does not outlast the hit by 1 s (the expiry margin), so <see cref="UseWindow"/> starts here.
    /// </summary>
    public static double EarliestCoveringUse(double hit, double duration) => hit - duration + ExpiryMargin;

    /// <summary>
    /// Seconds from pressing <paramref name="action"/> to its effect: its cast, plus the recast of a GCD that is pressed right
    /// before it to make it usable (<see cref="ActionInfo.FollowUpOf"/> a GCD without a cooldown of its own, a recast under
    /// 3 s: Eukrasia's 1 s before Eukrasian Prognosis; not Macrocosmos, pressed long before Microcosmos). The importer's
    /// "(Late)" has that much in place 1 s before the hit, and so does multi-hit coverage (<see cref="ForEntry"/>).
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="actions">The catalog its trigger is resolved in.</param>
    /// <param name="syncLevel">The sync level (the trigger's recast).</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> or <paramref name="actions"/> is null.</exception>
    public static double TimeToEffect(ActionInfo action, IActionCatalog actions, int syncLevel)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(actions);
        return action.CastTime + (action.FollowUpOf is { } id && actions.Get(id) is { IsGcd: true } trigger
            && trigger.RecastAt(syncLevel) < PlainGcdRecast ? trigger.RecastAt(syncLevel) : 0);
    }

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
    /// The automatic offset of an entry whose items have these timing actions, before multi-hit coverage and a series'
    /// floor (the first two rules of <see cref="ForEntry"/>), and the action it is the default or the lead of (null for a
    /// note or an unknown action).
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

        (double Offset, ActionInfo? Timing) timed = (offset, first);
        if (mechanic.HealLead is { } healLead && IsUsedAfterHit(first))
        {
            // Heals that top the targets up for the hit (the tanks at 1 HP before a tankbuster).
            timed = (-healLead, first);
        }
        else if (first != null && (IsUsedAfterHit(first) || first.Kind is ActionKind.Buff or ActionKind.Movement or ActionKind.Other)
            && EarliestPreHit(timing, syncLevel) is { } earliest && earliest.Offset < offset)
        {
            // A heal or a buff gives no lead of its own: an action of the entry that goes before the hit times it.
            timed = (earliest.Offset, earliest.Action);
        }

        return WithOwnLeads(timing, timed, syncLevel);
    }

    /// <summary>
    /// The second rule of <see cref="ForEntry"/>: <paramref name="timed"/>, pulled earlier to the lead of an item that has
    /// one of its own (<see cref="OwnLead"/>) wherever the cell lists it ("CU + Neutral Sect": Neutral Sect's −4.5 s), but
    /// no earlier than the entry's other items with a lasting effect D allow (<see cref="Guarded"/>: −(D − 2.5 s), so that
    /// used on the NOW cue they still outlast the hit by 1 s), also when that lead already times the entry: "CU + Earthly
    /// Star" and "Earthly Star + CU" at −7.5 s. The action whose lead pulled it then times the entry.
    /// </summary>
    private static (double Offset, ActionInfo? Timing) WithOwnLeads(IReadOnlyList<ActionInfo?> timing, (double Offset, ActionInfo? Timing) timed, int syncLevel)
    {
        // The timing action's own lead first (rule 1 gave it), so that a later lead pulls from the guarded press.
        if (OwnLead(timed.Timing) is not null)
        {
            for (var i = 0; i < timing.Count; i++)
            {
                if (ReferenceEquals(timing[i], timed.Timing))
                {
                    timed = (Guarded(timing, i, timed.Offset, syncLevel), timed.Timing);
                    break;
                }
            }
        }

        for (var i = 0; i < timing.Count; i++)
        {
            if (OwnLead(timing[i]) is not { } lead || -lead >= timed.Offset)
                continue;
            var offset = Guarded(timing, i, -lead, syncLevel);
            if (offset < timed.Offset)
                timed = (offset, timing[i]);
        }

        return timed;
    }

    /// <summary>
    /// <paramref name="offset"/>, the lead of the item at <paramref name="index"/>, but no earlier than the entry's other
    /// items with a lasting effect D allow (<see cref="CoveringDuration"/>): −(D − 2.5 s), so that used on the NOW cue they
    /// still outlast the hit by 1 s.
    /// </summary>
    private static double Guarded(IReadOnlyList<ActionInfo?> timing, int index, double offset, int syncLevel)
    {
        for (var j = 0; j < timing.Count; j++)
        {
            if (j != index && CoveringDuration(timing[j], syncLevel) is { } duration)
                offset = Math.Max(offset, OwnLeadGuard - duration);
        }

        return offset;
    }

    /// <summary>The lead of a <see cref="UseTiming.Before"/> action with a positive one of its own (<see cref="ActionInfo.Lead"/>), else null.</summary>
    private static double? OwnLead(ActionInfo? action) =>
        action is { When: UseTiming.Before, Lead: { } lead } && lead > 0 && double.IsFinite(lead) ? lead : null;

    /// <summary>
    /// True for mitigation with a lead of its own (<see cref="OwnLead"/>): its effect starts on the press, so the lead only
    /// puts it after the entry's other items (Passage of Arms, 2 s: a channel any other action ends). It is pressed with
    /// the entry's last item and turns due no earlier than any other (<see cref="ForEntry"/>), and the scheduler lists
    /// it after them (<see cref="ReminderScheduler"/>).
    /// </summary>
    internal static bool IsPressedLast(ActionInfo? action) => action is not null && OwnLead(action) is not null && IsMitigation(action);

    /// <summary>Party or tank mitigation, or an invuln.</summary>
    private static bool IsMitigation(ActionInfo action) => action.Kind is ActionKind.PartyMit or ActionKind.TankMit or ActionKind.Invuln;

    /// <summary>
    /// The duration an action's effect must last through the hit with when it is pressed before it: mitigation, shields,
    /// regens, invulns and tank limit breaks, and the longest effect of a pre-placed heal (<see cref="UseTiming.Before"/>),
    /// when known; null for buffs, movement, other actions, heals used after the hit and notes.
    /// </summary>
    private static double? CoveringDuration(ActionInfo? action, int syncLevel) =>
        action is null || IsUsedAfterHit(action) || action.Kind is ActionKind.Buff or ActionKind.Movement or ActionKind.Other
            ? null
            : CoverageDuration(action, syncLevel);

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
    /// For mitigation (party or tank mitigation, an invuln) pressed before the hit with a known duration D, the later hits hR
    /// of the mechanic (<see cref="Mechanic.Hits"/> and <see cref="Mechanic.SubHits"/>) it can cover together with
    /// <paramref name="hit"/>: hR − hit ≤ D − 1 s − the latest use before the
    /// hit (<see cref="ReminderOptions.MitigationLatestBeforeHit"/>), the earliest use that still reaches hR,
    /// hR − D + 0.5 s, at least 1 s before the hit, and a press no later than <paramref name="latestOffset"/> after the hit
    /// allows (<see cref="LatestCoveringOffset"/>), the press being hR − D + 2 s but at least 0.5 s before the hit. For the
    /// last of them: that press and earliest use, and the press for the last one reached at hR − D + 2 s itself, which the
    /// entry gets (null when none is); else null. A later hit that would need a later press is not covered.
    /// </summary>
    private static (double? EntryPress, double Press, double NotEarly)? CoveredHit(ActionInfo? action, Mechanic mechanic, double hit, int syncLevel, double latestOffset, ReminderOptions options)
    {
        if (action is null || !IsMitigation(action) || IsUsedAfterHit(action) || CoverageDuration(action, syncLevel) is not { } duration)
        {
            return null;
        }

        double? entryPress = null;
        (double Press, double NotEarly)? covered = null;
        IEnumerable<double> hits = mechanic.SubHits.Count == 0 ? mechanic.Hits : mechanic.Hits.Concat(mechanic.SubHits).Order();
        foreach (var later in hits)
        {
            // Due from notEarly on, and pressed at the latest 0.5 s before the hit, it is due until the hit (UseWindow).
            var press = Math.Min(later - duration + CoverageLead, hit - CoveringPressLead);
            var notEarly = later - duration + CoverageMargin;
            if (later > hit + SameHit && later - hit <= duration - ExpiryMargin - options.MitigationLatestBeforeHit
                && notEarly <= hit - MinDueWindow && press - hit <= latestOffset)
            {
                covered = (press, notEarly);
                if (later - duration + CoverageLead <= hit - CoveringPressLead)
                    entryPress = press;
            }
        }

        return covered is { } c ? (entryPress, c.Press, c.NotEarly) : null;
    }

    /// <summary>
    /// The latest offset multi-hit coverage may give an entry with these timing actions (see <see cref="ForEntry"/>): an
    /// item pressed before the hit that takes time to its effect (<see cref="TimeToEffect"/>: a cast, or the GCD a follow-up
    /// needs first) has it in place 1 s before the hit, as "(Late)" presses it ("Succor": −3 s, "Eukrasian Prognosis":
    /// −2 s), and an item with a lead of its own that is no mitigation keeps that lead (Earthly Star −10 s, to mature;
    /// Neutral Sect −4.5 s, for its Helios). +∞ when no item limits it.
    /// </summary>
    private static double LatestCoveringOffset(IReadOnlyList<ActionInfo?> timing, IActionCatalog actions, int syncLevel)
    {
        var latest = double.PositiveInfinity;
        foreach (var action in timing)
        {
            if (action is null || IsUsedAfterHit(action))
                continue;
            if (TimeToEffect(action, actions, syncLevel) is var time && time > 0)
                latest = Math.Min(latest, -(time + EffectBeforeHit));
            if (OwnLead(action) is { } lead && !IsMitigation(action))
                latest = Math.Min(latest, -lead);
        }

        return latest;
    }

    /// <summary>
    /// The earliest use of an item pressed at <paramref name="press"/> that is not early (<see cref="UseWindow"/>: the hit −
    /// D + 1 s for its known duration D), when the press is at or after it but the default due lead (1.5 s) would turn the
    /// reminder due before it: the reminder turns due no earlier, so that a press on its NOW cue is not early (The Blackest
    /// Night, a 7 s shield, pressed with Rampart 6 s before the hit). Null otherwise, also for a press before that earliest
    /// use (the sheet asks for an early press), which keeps its plain NOW cue.
    /// </summary>
    private static double? OnTimeCue(ActionInfo? action, int syncLevel, double hit, double press)
    {
        if (CoverageDuration(action, syncLevel) is not { } duration)
            return null;
        var earliest = EarliestCoveringUse(hit, duration);
        return press >= earliest - WrittenPrecision && press - DefaultOptions.DueLead < earliest ? Math.Min(earliest, press) : null;
    }

    /// <summary>The later of two optional times (null when both are).</summary>
    private static double? Later(double? a, double? b) => a is { } x && b is { } y ? Math.Max(x, y) : a ?? b;

    /// <summary>
    /// <paramref name="floor"/> when <paramref name="pressAt"/> is at or after it (to the 0.01 s plan offsets are written
    /// to), else null: the floor a press keeps.
    /// </summary>
    private static double? AtOrAfter(double pressAt, double? floor) => floor is { } f && pressAt >= f - WrittenPrecision ? floor : null;

    /// <summary>
    /// The floor of an entry on a later hit of a series (see <see cref="ForEntry"/>): its timing action's
    /// (<see cref="SeriesFloor"/>), or the earlier one of an item whose barrier comes from a cast after it
    /// (<see cref="ActionInfo.BarrierCast"/>), since the lead for that cast counts wherever the cell lists it (Neutral Sect
    /// beside Earthly Star, which times the entry). A shield's cast does not lower it: a shield beside the timing action
    /// waits for its floor, and its cast still lands before the next hit. Nor does any cast when the entry lists an action
    /// that makes the next spell instant (<see cref="CastsInstantly"/>).
    /// </summary>
    private static double EntryFloor(double previous, ActionInfo? timingAction, IReadOnlyList<ActionInfo?> timing)
    {
        var instant = CastsInstantly(timing);
        var floor = SeriesFloor(previous, timingAction, instant);
        foreach (var action in timing)
        {
            if (action?.BarrierCast is > 0)
                floor = Math.Min(floor, SeriesFloor(previous, action, instant));
        }

        return floor;
    }

    /// <summary>
    /// The series floor of the item at <paramref name="index"/> (see <see cref="ForEntry"/>), never earlier than the
    /// entry's <paramref name="entryFloor"/>: its own action's (<see cref="SeriesFloor"/>). A note and a buff, movement or
    /// other action without a lead of its own (<see cref="KeepsTheEntryFloor"/>) keep the entry's floor, so that they come
    /// with the cast they change or go with (Recitation, Emergency Tactics or Swiftcast with a Succor, "Stack middle →
    /// Succor"); the trigger of a follow-up the entry lists waits with it (Summon Seraph with its Consolation).
    /// </summary>
    private static double ItemFloor(double previous, double entryFloor, IReadOnlyList<ActionInfo?> timing, int index)
    {
        var action = timing[index];
        var instant = CastsInstantly(timing);
        if (!KeepsTheEntryFloor(action))
            return Math.Max(entryFloor, SeriesFloor(previous, action, instant));

        var floor = entryFloor;
        foreach (var followUp in timing)
        {
            if (action is not null && followUp?.FollowUpOf == action.Id && !KeepsTheEntryFloor(followUp))
                floor = Math.Max(floor, SeriesFloor(previous, followUp, instant));
        }

        return floor;
    }

    /// <summary>
    /// True for an item the hit before a later hit of a series has nothing of to spend, so that it keeps the entry's series
    /// floor (<see cref="ItemFloor"/>): a note, an action the catalog does not know (<paramref name="action"/> null), and a
    /// buff, movement or other action without a lead of its own (its effect changes the next GCD, or the player). Not
    /// Neutral Sect, whose barrier comes with the Helios cast after it (its lead).
    /// </summary>
    private static bool KeepsTheEntryFloor(ActionInfo? action) =>
        action is null || (OwnLead(action) is null && action.Kind is ActionKind.Buff or ActionKind.Movement or ActionKind.Other);

    /// <summary>
    /// The floor of a press timed by <paramref name="action"/> on a later hit of a series (see <see cref="ForEntry"/>): the
    /// hit before it + 0.3 s, less the cast of a shield or of the GCD that puts up the action's barrier after it
    /// (<see cref="ActionInfo.BarrierCast"/>), so that the barrier lands after that hit; with no cast when the entry makes
    /// it <paramref name="instant"/> (<see cref="CastsInstantly"/>).
    /// </summary>
    private static double SeriesFloor(double previous, ActionInfo? action, bool instant) =>
        previous + AfterPreviousHit - (instant ? 0 : action is { Kind: ActionKind.Shield } shield ? shield.CastTime : action?.BarrierCast ?? 0);

    /// <summary>
    /// True when the entry lists an action that makes the next spell instant (<see cref="ActionInfo.InstantNextCast"/>:
    /// "Swiftcast/Succor"): its shield or barrier lands on the press, so a later hit of a series leaves it no cast
    /// (<see cref="SeriesFloor"/>). Elsewhere a cast only presses an item earlier than it needs to be, which its effect
    /// outlasts.
    /// </summary>
    private static bool CastsInstantly(IReadOnlyList<ActionInfo?> timing) => timing.Any(action => action?.InstantNextCast == true);

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
        if (OwnLead(action) is { } lead)
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
