using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The Protean Waves: the Liquid's telegraphed cones and its two unseen sets, the Sluices, the tornadoes' waves, the debuff
// a wave leaves and what a second one does.
internal sealed class TeaP1ProteanPullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 Protean Wave";

    // A player closer to a wave's source than this is left out of its angles: a yalm of error turns such a bearing by
    // twelve degrees and more.
    private const double TooCloseForAnAngle = 5;

    // How far a logged position of a player can be off: "closest" and "farthest" are told apart no finer.
    private const float PositionsGoodTo = 1f;

    private static readonly uint[] Waves =
    [
        P1ActionId.ProteanWaveFromTheLiquid, P1ActionId.ProteanWaveStraightAhead, P1ActionId.ProteanWaveAtAPlayer, P1ActionId.ProteanWaveAtTheClosest,
        P1ActionId.TornadoWave, P1ActionId.TornadoWaveUnseen,
    ];

    private static readonly uint[] Cleaves = [P1ActionId.FluidSwing, P1ActionId.FluidStrike];

    private static readonly uint[] Telegraphed = [P1ActionId.ProteanWaveFromTheLiquid, P1ActionId.ProteanWaveAtAPlayer, P1ActionId.TornadoWave];

    private readonly List<string> aheadHits = [], sequences = [], leftOut = [], sets = [], sluices = [], tornadoCasts = [], tornadoUnseen = [], turns = [], secondWaves = [];
    private readonly List<double> helperAim = [], ownAim = [], facingDrift = [], liquidMoved = [], turnsAfter = [], sluiceOff = [];
    private readonly List<double> tornadoAim = [], tornadoUnseenAim = [], unseenAim = [], hitOff = [], missedOff = [];
    private readonly List<double> debuffSeconds = [], knockbacks = [], firstWaveDamage = [];
    private readonly Dictionary<uint, int> debuffs = [];
    private readonly Dictionary<string, int> debuffFrom = [];
    private readonly LethalHits sluiceHits = new();
    private int telegraphs, conesAsTheSim, conesOnePerPlayer, leftOutIsTheTarget, leftOutKnown;
    private int unseenSets, unseenSetsOfFour, unseenAtTheClosest, sluiceSets, sluicesUnderTheFarthest, straightAhead;
    private int tornadoCones, tornadoAtTheClosest, tornadoUnseenCones, tornadoUnseenAtTheClosest, telegraphedHits;
    private int aheadKilled, firstWaves, firstWavesKilled, firstWavesKilledFromFull, laterWaves, laterWavesKilled, shielded;

    public void Observe(TeaPull pull)
    {
        if (pull.Liquid is not { } liquid) return;
        foreach (var cast in pull.CastsOf(P1ActionId.ProteanWaveFromTheLiquid))
            ObserveASequence(pull, liquid, cast);
        ObserveTheTornadoes(pull);
        ObserveAngles(pull);
        ObserveTheDebuff(pull);
        ObserveSecondWaves(pull);
        ObserveKnockbacks(pull);
        sluiceHits.Observe(pull, pull.Hits.Where(h => h.Action == P1ActionId.Sluice), h => $"a Sluice at {Spread.F(pull.Clock(h.Time))} s");
    }

    private void ObserveASequence(TeaPull pull, uint liquid, ActCast cast)
    {
        if (pull.CastAt(cast) is not { } own) return;
        telegraphs++;
        var living = pull.Living(cast.Time).ToList();
        var bearings = Bearings(pull, own.Position, living, cast.Time);
        var helpers = pull.Casts.Where(c => c.Action == P1ActionId.ProteanWaveAtAPlayer && Math.Abs(PullLog.Seconds(cast.Time, c.Time)) < 0.1)
                          .Select(pull.CastAt).OfType<ActCastAt>().ToList();
        var cones = 1 + helpers.Count;
        if (cones == living.Count + 1) conesAsTheSim++;
        if (cones == living.Count) conesOnePerPlayer++;
        var aimed = Assign(helpers.Select(h => (double)h.Heading).ToList(), bearings);
        helperAim.AddRange(aimed.Select(a => a.Off));
        var target = pull.TargetOf(liquid, cast.Time);
        var spare = living.Except(aimed.Select(a => a.Player)).ToList();
        if (spare.Count == 1)
        {
            leftOutKnown++;
            if (spare[0] == target) leftOutIsTheTarget++;
        }
        if (target is { } tank && bearings.TryGetValue(tank, out var toTheTank))
            ownAim.Add(TeaPull.Degrees(PullLog.AngleBetween(own.Heading, toTheTank)));
        sequences.Add($"{pull.Name} at {Spread.F(pull.Clock(cast.Time))} s: {living.Count} alive, the Liquid's own cone and {helpers.Count} from helpers; "
                      + $"the Liquid at {TeaPull.Spot(own.Position)} facing {Spread.F(own.Heading)} rad, turned to {(target is { } t ? pull.Who(t) : "nobody")}");
        leftOut.Add($"{pull.Name} at {Spread.F(pull.Clock(cast.Time))} s: no helper's cone fits {(spare.Count == 0 ? "nobody" : string.Join(", ", spare.Select(pull.Who)))}; "
                    + $"the Liquid is turned to {(target is { } t2 ? pull.Who(t2) : "nobody")}");

        var unseen = pull.Volleys(P1ActionId.ProteanWaveStraightAhead).Where(v => PullLog.Seconds(cast.Time, v[0].Time) is > 0 and < 12).ToList();
        foreach (var (volley, index) in unseen.Select((v, i) => (v, i)))
            ObserveAnUnseenSet(pull, liquid, own, volley[0], index);
        if (unseen.Count == 2)
        {
            var last = unseen[1][0].Time;
            var turned = pull.Moves.Where(m => m.Actor == liquid && m.Time > last
                                               && (Math.Abs(m.Heading - own.Heading) > 0.02 || Vector2.Distance(m.Position, own.Position) > 0.05f))
                             .Select(m => (DateTimeOffset?)m.Time).FirstOrDefault();
            if (turned is { } at)
            {
                turnsAfter.Add(PullLog.Seconds(last, at));
                turns.Add($"{pull.Name}: the Liquid's first turn or step after its second set comes {Spread.F(PullLog.Seconds(last, at))} s after it");
            }
        }
    }

    private void ObserveAnUnseenSet(TeaPull pull, uint liquid, ActCastAt telegraph, ActHit ahead, int index)
    {
        var time = ahead.Time;
        if (pull.Where(liquid, time) is not { } stands) return;
        if (ahead.Rotation is { } facing) facingDrift.Add(TeaPull.Degrees(PullLog.AngleBetween(facing, telegraph.Heading)));
        liquidMoved.Add(Vector2.Distance(stands, telegraph.Position));
        straightAhead++;

        var living = pull.Living(time).ToList();
        var bearings = Bearings(pull, stands, living, time);
        var cones = pull.Hits.Where(h => h.Action == P1ActionId.ProteanWaveAtTheClosest && SameVolley(h.Time, time)).GroupBy(h => h.Source)
                        .Where(g => g.First().Rotation is not null).Select(g => (double)g.First().Rotation!.Value).ToList();
        var aimed = Assign(cones, bearings);
        unseenAim.AddRange(aimed.Select(a => a.Off));
        var byDistance = living.Select(p => (Player: p, Distance: pull.Where(p, time) is { } at ? Vector2.Distance(at, stands) : float.NaN))
                               .Where(p => !float.IsNaN(p.Distance)).OrderBy(p => p.Distance).ToList();
        unseenSets++;
        if (cones.Count == Math.Min(4, living.Count)) unseenSetsOfFour++;
        // The closest by the client's positions, give or take the yalm those are good to.
        var reach = byDistance.Count >= cones.Count && cones.Count > 0 ? byDistance[cones.Count - 1].Distance + PositionsGoodTo : float.MaxValue;
        var same = aimed.All(a => byDistance.Any(p => p.Player == a.Player && p.Distance <= reach));
        if (same) unseenAtTheClosest++;
        sets.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s, set {index + 1}: {living.Count} alive, {cones.Count} cones from helpers, aimed at "
                 + $"{string.Join(", ", aimed.Select(a => pull.Who(a.Player)).Order())}; by distance from the Liquid: "
                 + string.Join(", ", byDistance.Select(p => $"{pull.Who(p.Player)} {Spread.F(p.Distance)}")) + (same ? "" : " (not the closest by these positions)"));

        if (index != 0) return;
        var laid = pull.Casts.Where(c => c.Action == P1ActionId.Sluice && Math.Abs(PullLog.Seconds(time, c.Time)) < 0.1)
                       .Select(pull.CastAt).OfType<ActCastAt>().ToList();
        if (laid.Count == 0) return;
        sluiceSets++;
        var under = laid.Select(s => living.Select(p => (Player: p, Distance: pull.Where(p, time) is { } at ? Vector2.Distance(at, s.Position) : float.MaxValue))
                                           .OrderBy(p => p.Distance).First()).ToList();
        sluiceOff.AddRange(under.Select(u => (double)u.Distance));
        var from = byDistance.Count >= laid.Count ? byDistance[^laid.Count].Distance - PositionsGoodTo : 0f;
        var underTheFarthest = under.All(u => byDistance.Any(p => p.Player == u.Player && p.Distance >= from));
        if (underTheFarthest) sluicesUnderTheFarthest++;
        sluices.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s: {laid.Count} Sluices, under {string.Join(", ", under.Select(u => $"{pull.Who(u.Player)} ({Spread.F(u.Distance)} y off)"))}"
                    + (underTheFarthest ? ", the farthest from the Liquid" : ", not the farthest by these positions"));
    }

    private void ObserveTheTornadoes(TeaPull pull)
    {
        foreach (var cast in pull.CastsOf(P1ActionId.TornadoWave))
        {
            if (pull.CastAt(cast) is not { } aim) continue;
            var living = pull.Living(cast.Time).ToList();
            if (Closest(pull, aim.Position, living, cast.Time) is not { } closest) continue;
            var (aimedAt, off, fits, note) = AimedAt(pull, aim.Position, aim.Heading, living, cast.Time, closest);
            tornadoCones++;
            if (fits) tornadoAtTheClosest++;
            tornadoAim.Add(off);
            tornadoCasts.Add($"{pull.Name} at {Spread.F(pull.Clock(cast.Time))} s, the tornado at {TeaPull.Spot(aim.Position)}: aimed "
                             + $"{Spread.F(off, "0.0")} degrees off {pull.Who(aimedAt)}{note}");
        }
        foreach (var cone in pull.Hits.Where(h => h.Action == P1ActionId.TornadoWaveUnseen).GroupBy(h => (h.Source, h.Time)))
        {
            if (cone.First().Rotation is not { } rotation || pull.Where(cone.Key.Source, cone.Key.Time) is not { } from) continue;
            var living = pull.Living(cone.Key.Time).ToList();
            if (Closest(pull, from, living, cone.Key.Time) is not { } closest) continue;
            var (aimedAt, off, fits, note) = AimedAt(pull, from, rotation, living, cone.Key.Time, closest);
            tornadoUnseenCones++;
            if (fits) tornadoUnseenAtTheClosest++;
            tornadoUnseenAim.Add(off);
            tornadoUnseen.Add($"{pull.Name} at {Spread.F(pull.Clock(cone.Key.Time))} s, the tornado at {TeaPull.Spot(from)}: hit "
                              + $"{string.Join(", ", cone.Where(h => h.HitSomeone).Select(h => pull.Who(h.Target)))}; aimed "
                              + $"{Spread.F(off, "0.0")} degrees off {pull.Who(aimedAt)}{note}");
        }
    }

    // The player whose bearing a cone lies nearest, how far off it, and whether that player is the source's closest, give
    // or take what the client's positions are good to.
    private static (uint Player, double Off, bool Fits, string Note) AimedAt(TeaPull pull, Vector2 from, double heading, List<uint> living, DateTimeOffset at,
                                                                              (uint Player, float Distance) closest)
    {
        var bearings = Bearings(pull, from, living, at);
        var aimed = bearings.MinBy(b => PullLog.AngleBetween(heading, b.Value));
        var off = TeaPull.Degrees(PullLog.AngleBetween(heading, aimed.Value));
        if (aimed.Key == closest.Player) return (aimed.Key, off, true, $", its closest player, {Spread.F(closest.Distance)} y away");
        var distance = pull.Where(aimed.Key, at) is { } stands ? Vector2.Distance(stands, from) : float.MaxValue;
        return (aimed.Key, off, distance <= closest.Distance + PositionsGoodTo,
                $", {Spread.F(distance)} y away; its closest player is {pull.Who(closest.Player)}, {Spread.F(closest.Distance)} y away");
    }

    // How far off each cone's axis every living player in reach stood, hit or missed.
    private void ObserveAngles(TeaPull pull)
    {
        foreach (var cone in pull.Hits.Where(h => Waves.Contains(h.Action)).GroupBy(h => (h.Source, h.Time)))
        {
            if (cone.First().Rotation is not { } rotation || pull.Where(cone.Key.Source, cone.Key.Time) is not { } from) continue;
            foreach (var player in pull.Living(cone.Key.Time))
            {
                if (pull.Positions.At(player, cone.Key.Time) is not { Gap.TotalSeconds: < 0.5 } pose) continue;
                var distance = Vector2.Distance(pose.Position, from);
                if (distance is < (float)TooCloseForAnAngle or > 40f) continue;
                var off = TeaPull.Degrees(PullLog.AngleBetween(rotation, PullLog.HeadingTo(from, pose.Position)));
                (cone.Any(h => h.Target == player) ? hitOff : missedOff).Add(off);
            }
        }
    }

    // The debuff is whatever status a wave's targets gain as its damage lands.
    private void ObserveTheDebuff(TeaPull pull)
    {
        foreach (var hit in pull.Hits.Where(h => Waves.Contains(h.Action) && h.HitSomeone))
            foreach (var gain in pull.Statuses.Where(s => s.Gained && s.Target == hit.Target && pull.IsOfTheFight(s.Source)
                                                          && s.Status != TeaLogIds.LuminousAetheroplasm && PullLog.Seconds(hit.Time, s.Time) is >= 0 and < 1.5))
                debuffs[gain.Status] = debuffs.GetValueOrDefault(gain.Status) + 1;
        if (debuffs.Count == 0) return;
        var debuff = debuffs.MaxBy(d => d.Value).Key;
        foreach (var gain in pull.Statuses.Where(s => s.Gained && s.Status == debuff))
        {
            debuffSeconds.Add(gain.Duration);
            var cause = pull.Hits.Where(h => h.Target == gain.Target && pull.IsOfTheFight(h.Source) && PullLog.Seconds(h.Time, gain.Time) is >= 0 and < 1.5)
                            .OrderBy(h => TeaPull.IsAnAutoAttack(h.Action) ? 0 : 1).Select(h => (uint?)h.Action).LastOrDefault();
            var name = cause is { } action ? $"{pull.ActionName(action)} ({action})" : "nothing of the fight's";
            debuffFrom[name] = debuffFrom.GetValueOrDefault(name) + 1;
        }
    }

    // A wave is a later one when its target took another wave, or a cleave, within the debuff's seconds before it, the
    // same instant included: both leave the debuff. The log cannot order two hits of one instant, so the first listed
    // is the first. The Liquid's straight-ahead cone on a player without the debuff is told apart: it is no helper's
    // cone.
    private void ObserveSecondWaves(TeaPull pull)
    {
        const double window = 4.0;
        foreach (var taken in pull.Hits.Where(h => (Waves.Contains(h.Action) || Cleaves.Contains(h.Action)) && h.HitSomeone).GroupBy(h => h.Target))
        {
            var ordered = taken.OrderBy(h => h.Time).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var hit = ordered[i];
                var outcome = pull.Outcome(hit);
                if (!Waves.Contains(hit.Action) || outcome == HitOutcome.AlreadyDead || pull.IsDying(hit.Target, hit.Time) || !pull.Settled(hit.Time)) continue;
                if (outcome == HitOutcome.Invulnerable)
                {
                    shielded++;
                    continue;
                }
                var last = i > 0 && PullLog.Seconds(ordered[i - 1].Time, hit.Time) < window && pull.Outcome(ordered[i - 1]) != HitOutcome.AlreadyDead
                    ? ordered[i - 1] : (ActHit?)null;
                var killed = outcome == HitOutcome.Killed || pull.DiedWithin(hit.Target, hit.Time, 1.5) is not null;
                var (before, max) = (pull.HpAt(hit.Target, hit.Time), pull.MaxHp(hit.Target));
                var took = (hit.Damage is { } damage ? $"for {damage}, " : "") + (before is { } b && pull.HpAfter(hit) is { } a ? $"{b} to {a} HP of {max}" : $"from {before} HP of {max}");
                if (last is { } earlier)
                {
                    laterWaves++;
                    if (killed) laterWavesKilled++;
                    secondWaves.Add($"{pull.Name} at {Spread.F(pull.Clock(hit.Time))} s: {pull.Who(hit.Target)} takes a wave "
                                    + $"{Spread.F(PullLog.Seconds(earlier.Time, hit.Time))} s after {pull.ActionName(earlier.Action)}: {took}{(killed ? ", dies" : "")}");
                    continue;
                }
                if (hit.Action == P1ActionId.ProteanWaveStraightAhead)
                {
                    if (killed) aheadKilled++;
                    aheadHits.Add($"{pull.Name} at {Spread.F(pull.Clock(hit.Time))} s: the straight-ahead cone on {pull.Who(hit.Target)}: {took}{(killed ? ", dies" : "")}");
                    continue;
                }
                firstWaves++;
                if (!killed && hit.Damage is { } dealt && max is { } most && most > 0)
                    firstWaveDamage.Add(dealt * 100.0 / most);
                if (!killed) continue;
                firstWavesKilled++;
                var fromFull = before is { } full && max is { } whole && full >= 0.9 * whole;
                if (fromFull) firstWavesKilledFromFull++;
                secondWaves.Add($"{pull.Name} at {Spread.F(pull.Clock(hit.Time))} s: {pull.Who(hit.Target)} takes a first wave: {took}, dies");
            }
        }
    }

    // A thrown player's step: from where the hit found it to where it stood 1.5 s on, along the line from the source.
    private void ObserveKnockbacks(TeaPull pull)
    {
        foreach (var hit in pull.Hits.Where(h => Telegraphed.Contains(h.Action) && h.HitSomeone && h.TargetPosition is not null))
        {
            telegraphedHits++;
            if (pull.Outcome(hit) != HitOutcome.Survived || pull.Where(hit.Source, hit.Time) is not { } from
                || pull.Where(hit.Target, hit.Time.AddSeconds(1.5)) is not { } after) continue;
            var before = hit.TargetPosition!.Value;
            if (Vector2.Distance(before, from) < 0.1f) continue;
            knockbacks.Add(Vector2.Dot(after - before, Vector2.Normalize(before - from)));
        }
    }

    // One volley's lines can come a few hundredths of a second apart.
    private static bool SameVolley(DateTimeOffset a, DateTimeOffset b) => Math.Abs(PullLog.Seconds(a, b)) < 0.2;

    private static Dictionary<uint, double> Bearings(TeaPull pull, Vector2 from, IEnumerable<uint> players, DateTimeOffset at)
        => players.Select(p => (Player: p, At: pull.Where(p, at))).Where(p => p.At is not null)
                  .ToDictionary(p => p.Player, p => PullLog.HeadingTo(from, p.At!.Value));

    private static (uint Player, float Distance)? Closest(TeaPull pull, Vector2 from, IEnumerable<uint> players, DateTimeOffset at)
        => players.Select(p => (Player: p, At: pull.Where(p, at))).Where(p => p.At is not null)
                  .Select(p => ((uint Player, float Distance)?)(p.Player, Vector2.Distance(p.At!.Value, from)))
                  .OrderBy(p => p!.Value.Distance).FirstOrDefault();

    // Each cone to the player nearest its axis, the closest pairs first and a player taking one cone; Off in degrees.
    internal static List<(uint Player, double Off)> Assign(IReadOnlyList<double> headings, IReadOnlyDictionary<uint, double> bearings)
    {
        var pairs = headings.SelectMany((h, cone) => bearings.Select(b => (Cone: cone, Player: b.Key, Off: PullLog.AngleBetween(h, b.Value))))
                            .OrderBy(p => p.Off).ToList();
        var taken = new HashSet<int>();
        var assigned = new List<(uint Player, double Off)>();
        foreach (var pair in pairs)
        {
            if (taken.Contains(pair.Cone) || assigned.Any(a => a.Player == pair.Player)) continue;
            taken.Add(pair.Cone);
            assigned.Add((pair.Player, TeaPull.Degrees(pair.Off)));
        }
        return assigned;
    }

    public IEnumerable<LogFinding> Findings()
    {
        yield return new LogFinding(Section, "The Liquid's telegraphed cast: how many cones",
            SimSource.Line(TeaSource.Mechanics, "CastFromHelper(waveAtAPlayer, at: liquid.Placement().Face(member.Position));"),
            "the Liquid's own cone along its facing and one more at every living player: one more cone than players",
            telegraphs == 0 ? "no telegraphed cast"
                : $"{conesOnePerPlayer} of {telegraphs} casts have as many cones as living players, the Liquid's own among them; "
                  + $"{conesAsTheSim} have one more",
            telegraphs == 0 ? Verdict.NotInTheseLogs : conesAsTheSim == telegraphs ? Verdict.Matches : Verdict.Differs)
        {
            Details = sequences,
        };

        yield return new LogFinding(Section, "The Liquid's telegraphed cast: whom the helpers' cones leave to the Liquid's own",
            SimSource.Line(TeaSource.Actions, "UNVERIFIED: that the Living Liquid's own telegraphed wave is a cone straight ahead"),
            "nobody: its own cone goes along its facing, whoever stands there",
            leftOutKnown == 0 ? "no cast with one player fewer than cones to place"
                : $"in {leftOutIsTheTarget} of {leftOutKnown} casts the one player no helper's cone fits is the player the Liquid is turned to "
                  + "(players standing together can stand in for each other here)",
            leftOutKnown == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = leftOut,
        };

        var helper = new Spread(helperAim);
        yield return new LogFinding(Section, "The helpers' telegraphed cones are aimed at players as the cast starts: each cone off its player's bearing",
            SimSource.Line(TeaSource.Mechanics, "CastFromHelper(waveAtAPlayer, at: liquid.Placement().Face(member.Position));"), "0 degrees",
            helper.Count == 0 ? "no helper's cone" : $"median {Spread.F(helper.Median, "0.0")} degrees ({helper.Describe(" degrees", "0.0")}), by client positions",
            helper.Count == 0 ? Verdict.NotInTheseLogs : helper.Median < 10 ? Verdict.Matches : Verdict.Differs);

        var own = new Spread(ownAim);
        yield return new LogFinding(Section, "The Liquid's own telegraphed cone: off the bearing of the player it is turned to",
            SimSource.Line(TeaSource.Mechanics, "liquid.Cast(waveFromTheLiquid);"), "along its facing, which follows its target",
            own.Describe(" degrees", "0.0"), own.Count == 0 ? Verdict.NotInTheseLogs : own.Max < 15 ? Verdict.Matches : Verdict.Inconclusive);

        var drift = new Spread(facingDrift);
        yield return new LogFinding(Section, "The Liquid keeps its facing from the telegraphed cast through both unseen sets: each straight-ahead cone off the cast's facing",
            SimSource.Line(TeaSource.Mechanics, "private void LockTheFacing()"), "0 degrees", drift.Describe(" degrees", "0.0"),
            drift.Count == 0 ? Verdict.NotInTheseLogs : drift.Max < 1 ? Verdict.Matches : Verdict.Differs);

        var moved = new Spread(liquidMoved);
        yield return new LogFinding(Section, "The Liquid stands still through the sequence: its spot at each unseen set off its spot at the cast",
            SimSource.Line(TeaSource.Mechanics, "so it also stands still meanwhile (UNVERIFIED"), "0 y", moved.Describe(" y"),
            moved.Count == 0 ? Verdict.NotInTheseLogs : moved.Max < 0.1 ? Verdict.Matches : Verdict.Differs);

        var turned = new Spread(turnsAfter);
        yield return new LogFinding(Section, "The Liquid turns back to its target after its second unseen set",
            SimSource.Line(TeaSource.Constants, "float LiquidHoldsItsFacingAfterTheLastWave ="), $"{P1Duration.LiquidHoldsItsFacingAfterTheLastWave} s after it",
            turned.Count == 0 ? "no sequence with both sets and a later move of the Liquid's"
                : $"its first turn or step logged after the set: {turned.Describe(" s")}. It moves only when its target does, so this bounds the hold from above",
            turned.Count == 0 ? Verdict.NotInTheseLogs : turned.Min < P1Duration.LiquidHoldsItsFacingAfterTheLastWave - 0.25 ? Verdict.Differs : Verdict.Inconclusive)
        {
            Details = turns,
        };

        var unseen = new Spread(unseenAim);
        yield return new LogFinding(Section, "An unseen set: one cone at each of the four players closest to the Liquid",
            SimSource.Line(TeaSource.Mechanics, "foreach (var member in world.Party.Find.ClosestN(liquid.Position, WavesPerSet))"),
            "four cones from helpers, or one per living player if fewer, at the closest",
            unseenSets == 0 ? "no unseen set"
                : $"{unseenSetsOfFour} of {unseenSets} sets have four helper cones or one per living player; in {unseenAtTheClosest} the cones fit the "
                  + $"closest players by client positions; each cone off its player's bearing: median {Spread.F(unseen.Median, "0.0")} degrees "
                  + $"({unseen.Describe(" degrees", "0.0")})",
            unseenSets == 0 ? Verdict.NotInTheseLogs : unseenSetsOfFour < unseenSets ? Verdict.Differs
            : unseenAtTheClosest == unseenSets ? Verdict.Matches : Verdict.Inconclusive)
        {
            Details = sets,
        };

        yield return new LogFinding(Section, "The straight-ahead cone of an unseen set: what it does to whoever it hits",
            SimSource.Line(TeaSource.Actions, "public static readonly EnemyAction ProteanWaveStraightAhead ="),
            "a wave like the others: it kills only a carrier of the debuff",
            straightAhead == 0 ? "no unseen set"
                : aheadHits.Count == 0 ? $"nobody hit by {straightAhead} straight-ahead cones"
                : $"{aheadKilled} of {aheadHits.Count} players it hit without the debuff died of it, over {straightAhead} cones",
            aheadHits.Count == 0 ? straightAhead == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive : aheadKilled > 0 ? Verdict.Differs : Verdict.Matches)
        {
            Details = aheadHits,
        };

        var sluiceMiss = new Spread(sluiceOff);
        yield return new LogFinding(Section, "The Sluices start under the four players farthest from the Liquid as the first set goes off",
            SimSource.Line(TeaSource.Mechanics, "world.Party.Find.FarestN(liquid.Position, Sluices)"), "four Sluices, each on one of the four farthest",
            sluiceSets == 0 ? "no Sluices"
                : $"in {sluicesUnderTheFarthest} of {sluiceSets} sets the players nearest the Sluices are the farthest from the Liquid; a Sluice's centre "
                  + $"off its player: {sluiceMiss.Describe(" y")} (client positions)",
            sluiceSets == 0 ? Verdict.NotInTheseLogs : sluicesUnderTheFarthest == sluiceSets ? Verdict.Matches : Verdict.Inconclusive)
        {
            Details = sluices,
        };

        yield return sluiceHits.Finding(Section, "A Sluice kills whoever stands in it as it goes off",
            SimSource.Line(TeaSource.Actions, "public static readonly EnemyAction Sluice ="), "every hit kills, short of a tank's invulnerability");

        var tornado = new Spread(tornadoAim);
        yield return new LogFinding(Section, "The tornadoes' telegraphed waves: each aimed at its tornado's closest player as the cast starts",
            SimSource.Line(TeaSource.Mechanics, "if (Tornadoes.Centre(which) is { } centre && world.Party.Find.Closest(centre) is { } closest)"),
            "one cone from each tornado at its closest player",
            tornadoCones == 0 ? "no tornado's telegraphed wave"
                : $"{tornadoAtTheClosest} of {tornadoCones} cones are aimed at their tornado's closest player, or one no more than a yalm farther; off the aimed player's bearing: median "
                  + $"{Spread.F(tornado.Median, "0.0")} degrees ({tornado.Describe(" degrees", "0.0")}), by client positions",
            tornadoCones == 0 ? Verdict.NotInTheseLogs : tornadoAtTheClosest >= tornadoCones * 0.8 ? Verdict.Matches : Verdict.Differs)
        {
            Details = tornadoCasts.Where(t => t.Contains("; its closest player is", StringComparison.Ordinal)).ToList(),
        };

        var tornadoLater = new Spread(tornadoUnseenAim);
        yield return new LogFinding(Section, "The tornadoes' unseen waves: each aimed at its tornado's closest player then",
            SimSource.Line(TeaSource.Mechanics, "=> At(at, () => FireFromEachTornado(Actions.TornadoWaveUnseen), file, line);"),
            "one cone from each tornado at its closest player",
            tornadoUnseenCones == 0 ? "no tornado's unseen wave"
                : $"{tornadoUnseenAtTheClosest} of {tornadoUnseenCones} cones are aimed at their tornado's closest player, or one no more than a yalm farther; off the aimed player's bearing: median "
                  + $"{Spread.F(tornadoLater.Median, "0.0")} degrees ({tornadoLater.Describe(" degrees", "0.0")}), by client positions",
            tornadoUnseenCones == 0 ? Verdict.NotInTheseLogs : tornadoUnseenAtTheClosest >= tornadoUnseenCones * 0.8 ? Verdict.Matches : Verdict.Differs)
        {
            Details = tornadoUnseen.Where(t => t.Contains("; its closest player is", StringComparison.Ordinal)).ToList(),
        };

        var (hit, missed) = (new Spread(hitOff), new Spread(missedOff));
        var half = TeaPull.Degrees(P1Geometry.ProteanWaveHalfAngle);
        yield return new LogFinding(Section, "A wave's half-angle: how far off a cone's axis the players it hit and missed stood",
            SimSource.Line(TeaSource.Constants, "float ProteanWaveHalfAngle ="), $"{Spread.F(half, "0.0")} degrees",
            hit.Count + missed.Count == 0 ? "no wave with a player in reach"
                : $"hit: {(hit.Count == 0 ? "nobody" : $"median {Spread.F(hit.Median, "0.0")} degrees off, 19 in 20 within {Spread.F(hit.Quantile(0.95), "0.0")} (n={hit.Count})")}; "
                  + $"missed: {(missed.Count == 0 ? "nobody" : $"19 in 20 beyond {Spread.F(missed.Quantile(0.05), "0.0")} degrees off (n={missed.Count})")}. Client "
                  + $"positions, which turn a bearing by ten degrees for a yalm of error at six yalms; players within {TooCloseForAnAngle} y of the source left out",
            hit.Count + missed.Count == 0 ? Verdict.NotInTheseLogs
            : hit.Count > 0 && hit.Median > half ? Verdict.Differs
            : (hit.Count == 0 || hit.Quantile(0.95) <= half + 5) && (missed.Count == 0 || missed.Quantile(0.05) >= half - 5) ? Verdict.Matches
            : Verdict.Inconclusive);

        var debuff = debuffs.Count == 0 ? (uint?)null : debuffs.MaxBy(d => d.Value).Key;
        yield return new LogFinding(Section, "The status a wave leaves",
            SimSource.Line(TeaSource.Constants, "ushort WaterResistanceDown ="), $"{P1StatusId.WaterResistanceDown}",
            debuff is { } found ? $"{found}, gained {debuffs[found]} times as a wave's damage landed"
                                  + (debuffs.Count > 1 ? $" (also {string.Join(", ", debuffs.Where(d => d.Key != found).Select(d => $"{d.Key} x{d.Value}"))})" : "")
                : "no wave hit anyone",
            debuff is null ? Verdict.NotInTheseLogs : debuff == P1StatusId.WaterResistanceDown ? Verdict.Matches : Verdict.Differs);

        var seconds = new Spread(debuffSeconds);
        yield return new LogFinding(Section, "The wave debuff's seconds",
            SimSource.Line(TeaSource.Constants, "float WaterResistanceDown ="), $"{P1Duration.WaterResistanceDown} s",
            seconds.Count == 0 ? "no debuff"
                : $"given for {seconds.Describe(" s")}",
            LogFinding.Compare(seconds, P1Duration.WaterResistanceDown, 0.25));

        yield return new LogFinding(Section, "What leaves the debuff: the fight's last hit on the player as it was gained",
            SimSource.Line(TeaSource.Actions, "private static EnemyAction Wave(uint actionId, params IEnemyActionEffect[] more)"),
            "every Protean Wave, and nothing else",
            debuffFrom.Count == 0 ? "no debuff" : string.Join("; ", debuffFrom.OrderByDescending(d => d.Value).Select(d => $"{d.Key} x{d.Value}")),
            debuffFrom.Count == 0 ? Verdict.NotInTheseLogs
            : debuffFrom.Keys.All(k => k.StartsWith("Protean Wave", StringComparison.Ordinal)) ? Verdict.Matches : Verdict.Differs);

        var firstDamage = new Spread(firstWaveDamage);
        yield return new LogFinding(Section, "A wave on a player who took a wave or a cleave within the debuff's seconds kills; a first wave does not",
            SimSource.Line(TeaSource.Actions, "public const string SecondWave ="),
            "a wave on a carrier of the debuff kills, two of one instant counting as a first and a second; a first never kills",
            firstWaves + laterWaves == 0 ? "no wave hit a living player"
                : $"{laterWavesKilled} of {laterWaves} later waves killed; {firstWavesKilled} of {firstWaves} first waves did, {firstWavesKilledFromFull} of them "
                  + $"a player at nine tenths of its health or more. A first wave a player lived through was for {firstDamage.Describe(" %", "0")} of its "
                  + "maximum hit points, before shields"
                  + (shielded == 0 ? "" : $"; {shielded} waves on a player under an invulnerability left out"),
            firstWaves + laterWaves == 0 ? Verdict.NotInTheseLogs
            : laterWavesKilled < laterWaves || firstWavesKilledFromFull > 0 ? Verdict.Differs
            : laterWaves == 0 ? Verdict.Inconclusive : Verdict.Matches)
        {
            Details = secondWaves,
        };

        var thrown = new Spread(knockbacks);
        yield return new LogFinding(Section, "The telegraphed waves' knockback",
            SimSource.Line(TeaSource.Constants, "float ProteanWaveKnockback ="), $"{P1Rules.ProteanWaveKnockback} y straight away from the wave's source",
            telegraphedHits == 0 ? "no telegraphed wave hit anyone"
                : thrown.Count == 0 ? $"{telegraphedHits} hits, none on a player who lived to show the throw"
                : $"{thrown.Describe(" y")} away from the source over the 1.5 s after the hit (client positions; a walk adds to it)",
            thrown.Count == 0 ? Verdict.NotInTheseLogs : Math.Abs(thrown.Median - P1Rules.ProteanWaveKnockback) <= 2 ? Verdict.Matches : Verdict.Differs);
    }
}
