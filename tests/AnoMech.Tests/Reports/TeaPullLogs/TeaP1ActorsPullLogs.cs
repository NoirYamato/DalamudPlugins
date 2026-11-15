using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// Who stands where: the bosses before the pull and as they appear, the Liquid Rages and their water, the helpers, what
// the arena holds, and how the bosses walk.
internal sealed class TeaP1ActorsPullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 actors";

    // How the zone holds the rim.
    private const string RimInTheZone = "world.EnforceArenaBoundary(";
    private const bool TheSimsRimKills = true;
    private static string RimInTheSim => $"no wall: whoever stands beyond {P1Geometry.ArenaRadius} y dies";

    // The two bosses' hitboxes, and a player's.
    private const float LiquidHitbox = 3.5f, HandHitbox = 1.6f, PlayerHitbox = 0.5f;

    private readonly List<string> liquidSpots = [], handSpots = [], parked = [], layouts = [], secondLayouts = [], barriers = [], mapEffects = [];
    private readonly List<double> liquidHeadings = [], handHeadings = [], handAfterCascade = [], handHpShare = [], handHpBesideTheLiquid = [];
    private readonly Dictionary<string, HashSet<int>> maxHp = [];
    private readonly List<double> ringRadii = [], tornadoesLeave = [], puddlesLeave = [], tornadoesStay = [], secondLeave = [];
    private readonly List<int> helpers = [];
    private readonly List<double> liquidSpeeds = [], handSpeeds = [], liquidStops = [], handStops = [], liquidAutos = [], handAutos = [];
    private readonly List<double> dropsyDistances = [];
    private readonly List<(double Distance, string Detail)> closest = [];
    private readonly List<double> farthest = [], outsideDied = [], outsideAt = [], halfShare = [];
    private readonly List<string> outside = [], music = [];
    private int outsideLived, musicAtAPullsStart;
    private int tornadoes, tornadoesWithWater, sets, setsWithAnOpenQuarter, repeats, barrierPulls, pulls;
    private readonly List<string> mapEffectsInAPull = [];

    public void Observe(TeaPull pull)
    {
        pulls++;
        ObserveStanding(pull);
        ObserveTheHand(pull);
        ObserveHitPoints(pull);
        ObserveTornadoes(pull);
        ObserveWalking(pull, pull.Liquid, TeaLogIds.LiquidAutoAttack, liquidSpeeds, liquidStops, liquidAutos);
        ObserveWalking(pull, pull.Hand, TeaLogIds.HandAutoAttack, handSpeeds, handStops, handAutos);
        ObserveMusic(pull);
        if (pull.End is not { } end) return;
        ObserveTheRim(pull, end);
        // A player a throw carried out is at 0 HP a moment later: only those who lived on say where one can stand.
        var reach = Samples(pull.Start, end, 0.25)
                    .SelectMany(t => pull.Living(t).Where(p => pull.DiedWithin(p, t, PullLog.DeathAfterHit) is null).Select(p => pull.Positions.At(p, t)))
                    .Where(p => p is { Gap.TotalSeconds: < 0.5 })
                    .Select(p => (double)Vector2.Distance(p!.Value.Position, TeaPull.Centre))
                    .ToList();
        if (reach.Count > 0) farthest.Add(reach.Max());
    }

    // Each time a living player's position reads beyond the rim: how far out, and how soon it reads 0 HP.
    private void ObserveTheRim(TeaPull pull, DateTimeOffset end)
    {
        foreach (var player in pull.Players)
        {
            var beyond = false;
            foreach (var time in Samples(pull.Start, end, 0.1))
            {
                if (!pull.IsAlive(player, time) || pull.Positions.At(player, time) is not { Gap.TotalSeconds: < 0.5 } pose)
                {
                    beyond = false;
                    continue;
                }
                var out0 = Vector2.Distance(pose.Position, TeaPull.Centre);
                if (out0 <= P1Geometry.ArenaRadius)
                {
                    beyond = false;
                    continue;
                }
                if (beyond) continue;
                beyond = true;
                outsideAt.Add(out0);
                if (pull.DiedWithin(player, time, PullLog.DeathAfterHit) is { } died) outsideDied.Add(PullLog.Seconds(time, died));
                else outsideLived++;
                outside.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s: {pull.Who(player)} {Spread.F(out0)} y out"
                            + (pull.DiedWithin(player, time, PullLog.DeathAfterHit) is { } at ? $", at 0 HP {Spread.F(PullLog.Seconds(time, at))} s later" : ", lives on"));
            }
        }
    }

    private void ObserveMusic(TeaPull pull)
    {
        if (pull.MusicAtReset.Any(m => PullLog.Seconds(m.Time, pull.Start) < 1)) musicAtAPullsStart++;
        foreach (var change in pull.MusicAtReset)
            music.Add($"{pull.Name}: track {change.Bgm} set {Spread.F(PullLog.Seconds(change.Time, pull.Start), "0.0")} s before the pull");
        foreach (var change in pull.Music)
            music.Add($"{pull.Name} at {Spread.F(pull.Clock(change.Time))} s: track {change.Bgm}"
                      + (pull.Hits.Any(h => h.Action == P1ActionId.JKick && Math.Abs(PullLog.Seconds(h.Time, change.Time)) < 0.1) ? ", with J Kick" : ""));
    }

    private void ObserveStanding(TeaPull pull)
    {
        if (pull.LoggedFromItsStart && pull.SetUpWithTheLiquid(P1BNpcBaseId.LivingLiquid).Cast<ActCombatant?>().LastOrDefault() is { Position: { } at } liquid)
        {
            liquidSpots.Add(TeaPull.Spot(at));
            if (liquid.Heading is { } heading) liquidHeadings.Add(heading);
        }
        foreach (var (name, bnpcBase) in new[] { ("Cruise Chaser", P1BNpcBaseId.CruiseChaser), ("Brute Justice", TeaLogIds.BruteJustice) })
            if (pull.SetUpWithTheLiquid(bnpcBase).Cast<ActCombatant?>().LastOrDefault() is { Position: { } spot })
                parked.Add($"{name} at {TeaPull.Spot(spot)}");
        var standingHelpers = pull.SetUpWithTheLiquid(P1BNpcBaseId.Helper).Select(c => c.Actor).Distinct().Count();
        if (standingHelpers > 0) helpers.Add(standingHelpers);
        foreach (var barrier in pull.StandingObjects.Where(o => o.Added && o.BaseId == TeaLogIds.EntranceBarrier))
            if (pull.Where(barrier.Actor, pull.Start) is { } stands)
            {
                barriers.Add(TeaPull.Spot(stands));
                barrierPulls++;
            }
        if (pull.MapEffectsAtReset.Count > 0)
            mapEffects.Add(string.Join(", ", pull.MapEffectsAtReset.OrderBy(m => m.Slot).Select(m => $"slot {m.Slot} {m.Flags:X8}")));
        mapEffectsInAPull.AddRange(pull.MapEffects.Select(m => $"{pull.Name} at {Spread.F(pull.Clock(m.Time))} s: slot {m.Slot} {m.Flags:X8}"));
    }

    private void ObserveTheHand(TeaPull pull)
    {
        if (pull.Combatants.Where(c => c.Added && c.BNpcBase == P1BNpcBaseId.LiquidHand).Cast<ActCombatant?>().FirstOrDefault() is not { } hand) return;
        if (hand.Position is { } at) handSpots.Add(TeaPull.Spot(at));
        if (hand.Heading is { } heading) handHeadings.Add(heading);
        if (pull.Volleys(P1ActionId.Cascade).Select(v => (DateTimeOffset?)v[0].Time).FirstOrDefault() is not { } cascade) return;
        handAfterCascade.Add(PullLog.Seconds(cascade, hand.Time));
        if (pull.Liquid is not { } liquid || pull.HpAt(hand.Actor, hand.Time) is not { } handHp) return;
        if (pull.HpAt(liquid, cascade) is { } before && before > 0) handHpShare.Add((double)handHp / before);
        if (pull.HpAt(liquid, hand.Time) is { } beside && beside > 0) handHpBesideTheLiquid.Add((double)handHp / beside);
        // The Liquid's last reading before its hit points halve, against twice what the Hand appears with.
        var whole = pull.HitPoints.Where(h => h.Actor == liquid && h.Time > cascade && h.Time <= hand.Time && h.Hp > 1.5 * handHp).Select(h => (int?)h.Hp).LastOrDefault()
                    ?? pull.HpAt(liquid, cascade);
        if (whole is { } last && last > 0) halfShare.Add((double)handHp / last);
    }

    private void ObserveHitPoints(TeaPull pull)
    {
        foreach (var (name, bnpcBase) in new[]
                 {
                     ("Living Liquid", P1BNpcBaseId.LivingLiquid), ("Liquid Hand", P1BNpcBaseId.LiquidHand), ("Liquid Rage", P1BNpcBaseId.LiquidRage),
                     ("Jagd Doll", TeaLogIds.JagdDoll), ("Embolus", P1BNpcBaseId.Embolus),
                 })
            foreach (var actor in pull.Actors(bnpcBase))
                if (pull.MaxHp(actor) is { } max)
                {
                    if (!maxHp.TryGetValue(name, out var seen)) maxHp[name] = seen = [];
                    seen.Add(max);
                }
    }

    private void ObserveTornadoes(TeaPull pull)
    {
        var puddles = pull.Objects.Where(o => o.Added && o.BaseId == P1EObjId.LiquidRagePuddle).ToList();
        foreach (var (set, index) in pull.TornadoSets.Select((s, i) => (s, i)))
        {
            sets++;
            if (set.OpenBearing is not null) setsWithAnOpenQuarter++;
            layouts.Add($"{pull.Name}, set {index + 1}: {set.Layout}");
            var water = new List<uint>();
            foreach (var (_, position) in set.Tornadoes)
            {
                tornadoes++;
                ringRadii.Add(Vector2.Distance(position, TeaPull.Centre));
                var under = puddles.Where(p => Math.Abs(PullLog.Seconds(set.Appeared, p.Time)) < 1
                                               && pull.Where(p.Actor, p.Time) is { } at && Vector2.Distance(at, position) < 0.1f).ToList();
                if (under.Count > 0) tornadoesWithWater++;
                water.AddRange(under.Select(p => p.Actor));
            }
            var gone = pull.Combatants.Where(c => !c.Added && set.Tornadoes.Any(t => t.Actor == c.Actor) && c.Time > set.Appeared)
                           .Select(c => (DateTimeOffset?)c.Time).Min();
            var waterGone = pull.Objects.Where(o => !o.Added && water.Contains(o.Actor) && o.Time > set.Appeared).Select(o => (DateTimeOffset?)o.Time).Min();
            var drainage = pull.Hits.Where(h => h.Action == P1ActionId.Drainage && h.Time > set.Appeared).Select(h => (DateTimeOffset?)h.Time).Min();
            if (index == 0 && drainage is { } hit)
            {
                if (gone is { } left) tornadoesLeave.Add(PullLog.Seconds(hit, left));
                if (waterGone is { } dried) puddlesLeave.Add(PullLog.Seconds(hit, dried));
            }
            var fell = pull.Liquid is { } liquid ? pull.HitPoints.Where(h => h.Actor == liquid && h.Hp == 0).Select(h => (DateTimeOffset?)h.Time).FirstOrDefault() : null;
            if (index == 1 && fell is { } fall && gone is { } after) secondLeave.Add(PullLog.Seconds(fall, after));
            if (gone is null && pull.End is { } ended) tornadoesStay.Add(PullLog.Seconds(set.Appeared, ended));
            ObserveTheWater(pull, set, gone ?? pull.End ?? set.Appeared);
        }
        if (pull.TornadoSets.Count >= 2)
        {
            var (first, second) = (pull.TornadoSets[0], pull.TornadoSets[1]);
            secondLayouts.Add($"{pull.Name}: {first.Layout}, then {second.Layout}");
            if (first.OpenBearing == second.OpenBearing) repeats++;
        }
    }

    // Standing in the water gives Dropsy. Where nobody gained it, the closest a living player came to a tornado bounds
    // the water from above.
    private void ObserveTheWater(TeaPull pull, TeaTornadoSet set, DateTimeOffset until)
    {

        foreach (var gain in pull.Statuses.Where(s => s.Gained && s.Status == TeaLogIds.Dropsy && s.Time >= set.Appeared && s.Time <= until))
        {
            if (pull.Positions.At(gain.Target, gain.Time) is { Gap.TotalSeconds: < 0.5 } pose)
                dropsyDistances.Add(set.Tornadoes.Min(t => (double)Vector2.Distance(t.Position, pose.Position)));
        }
        var dropsy = pull.Holdings(TeaLogIds.Dropsy);
        (double Distance, string Detail)? nearest = null;
        foreach (var time in Samples(set.Appeared.AddSeconds(1), until, 0.1))
            foreach (var player in pull.Living(time))
            {
                if (pull.Positions.At(player, time) is not { Gap.TotalSeconds: < 0.5 } pose) continue;
                if (dropsy.Any(d => d.Holder == player && d.From <= time.AddSeconds(3) && d.To >= time)) continue;
                var distance = set.Tornadoes.Min(t => (double)Vector2.Distance(t.Position, pose.Position));
                if (nearest is null || distance < nearest.Value.Distance)
                    nearest = (distance, $"{pull.Name}: {pull.Who(player)} {Spread.F(distance)} y from a tornado's centre at {Spread.F(pull.Clock(time))} s");
            }
        if (nearest is { } found) closest.Add(found);
    }

    // A boss's speed over its full steps, how far from its target it comes to rest, and how far its target
    // stands as each auto-attack goes off.
    private static void ObserveWalking(TeaPull pull, uint? boss, uint autoAttack, List<double> speeds, List<double> stops, List<double> autos)
    {
        if (boss is not { } actor) return;
        var moves = pull.Moves.Where(m => m.Actor == actor && !m.Placed).ToList();
        speeds.AddRange(TeaP1DollsPullLogs.Speeds(pull, actor));
        for (var i = 1; i < moves.Count; i++)
        {
            var walked = Vector2.Distance(moves[i - 1].Position, moves[i].Position) > 0.3f && PullLog.Seconds(moves[i - 1].Time, moves[i].Time) < 0.45;
            var rests = i + 1 == moves.Count || PullLog.Seconds(moves[i].Time, moves[i + 1].Time) > 1.5
                                             || Vector2.Distance(moves[i].Position, moves[i + 1].Position) < 0.01f;
            // Only a walk that ends on a target standing still shows where the boss stops.
            if (walked && rests && pull.TargetOf(actor, moves[i].Time) is { } target && pull.IsAlive(target, moves[i].Time)
                && pull.Positions.At(target, moves[i].Time) is { Gap.TotalSeconds: < 0.5 } pose
                && pull.Positions.At(target, moves[i].Time.AddSeconds(-1.5)) is { } earlier && Vector2.Distance(earlier.Position, pose.Position) < 0.6f
                && pull.Positions.At(target, moves[i].Time.AddSeconds(0.5)) is { } later && Vector2.Distance(later.Position, pose.Position) < 0.6f)
                stops.Add(Vector2.Distance(moves[i].Position, pose.Position));
        }
        foreach (var hit in pull.Hits.Where(h => h.Source == actor && h.Action == autoAttack && h.TargetPosition is not null))
            if (pull.Where(actor, hit.Time) is { } at)
                autos.Add(Vector2.Distance(at, hit.TargetPosition!.Value));
    }

    internal static IEnumerable<DateTimeOffset> Samples(DateTimeOffset from, DateTimeOffset to, double step)
    {
        for (var t = from; t <= to; t = t.AddSeconds(step)) yield return t;
    }

    public IEnumerable<LogFinding> Findings()
    {
        var headings = new Spread(liquidHeadings);
        yield return new LogFinding(Section, "The Living Liquid before the pull: its spot and facing (0 faces south)",
            SimSource.Line(TeaSource.Constants, "Vector3 LiquidBeforeThePull ="),
            $"{TeaPull.Spot(TeaPull.Centre + new Vector2(P1Geometry.LiquidBeforeThePull.X, P1Geometry.LiquidBeforeThePull.Z))}, facing 0 rad",
            liquidSpots.Count == 0 ? "no pull logged from its start"
                : $"{Tally(liquidSpots)}, facing {headings.Describe(" rad")}",
            liquidSpots.Count == 0 ? Verdict.NotInTheseLogs
            : liquidSpots.All(spot => spot == TeaPull.Spot(TeaPull.Centre + new Vector2(P1Geometry.LiquidBeforeThePull.X, P1Geometry.LiquidBeforeThePull.Z)))
              && (headings.Count == 0 || Math.Abs(headings.Mean) <= 0.05) ? Verdict.Matches
            : Verdict.Differs);

        var after = new Spread(handAfterCascade);
        var share = new Spread(handHpShare);
        yield return new LogFinding(Section, "The Liquid Hand appears: where, facing, how long after Cascade lands, and with what hit points",
            SimSource.Line(TeaSource.Constants, "Vector3 HandAppearsOn ="),
            $"{TeaPull.Spot(TeaPull.Centre + new Vector2(P1Geometry.HandAppearsOn.X, P1Geometry.HandAppearsOn.Z))}, facing 0 rad, "
            + $"{P1Duration.TornadoesAppearAfterCascade} s after Cascade lands; no health is played",
            handSpots.Count == 0 ? "no Liquid Hand"
                : $"{Tally(handSpots)}, facing {new Spread(handHeadings).Describe(" rad")}, {after.Describe(" s")} after Cascade lands; its hit points as it "
                  + $"appears over the Liquid's as Cascade landed: {share.Describe("", "0.000")}; over the Liquid's as it appears: "
                  + new Spread(handHpBesideTheLiquid).Describe("", "0.000") + "; over the Liquid's last reading before its own halve: "
                  + new Spread(halfShare).Describe("", "0.0000"),
            handSpots.Count == 0 ? Verdict.NotInTheseLogs
            : handSpots.All(spot => spot == TeaPull.Spot(TeaPull.Centre + new Vector2(P1Geometry.HandAppearsOn.X, P1Geometry.HandAppearsOn.Z)))
              && (after.Count == 0 || LogFinding.Compare(after, P1Duration.TornadoesAppearAfterCascade, 0.25) == Verdict.Matches) ? Verdict.Matches
            : Verdict.Differs);

        yield return new LogFinding(Section, "Maximum hit points", TeaSource.NoHealthPlayed, "no health is played",
            maxHp.Count == 0 ? "no enemy with hit points"
                : string.Join("; ", maxHp.Select(m => $"{m.Key} {string.Join(" or ", m.Value.Order())}")),
            LogFinding.Unplayed(maxHp.Count));

        var radii = new Spread(ringRadii);
        yield return new LogFinding(Section, "The tornado ring's radius: each Liquid Rage's distance from the centre as it appears",
            SimSource.Line(TeaSource.Constants, "float TornadoRingRadius ="), $"{P1Geometry.TornadoRingRadius} y", radii.Describe(" y"),
            LogFinding.Compare(radii, P1Geometry.TornadoRingRadius, 0.1));

        yield return new LogFinding(Section, "The tornadoes' layout: three Liquid Rages a quarter turn apart, on the cardinals or the intercardinals",
            SimSource.Line(TeaSource.State, "var tornadoes1 = (TeaP1OpenQuarter)rng.NextInt(8);"), "one of the 8 open quarters, each as likely",
            sets == 0 ? "no Liquid Rages"
                : $"{setsWithAnOpenQuarter} of {sets} sets leave one quarter open: {Tally(layouts.Select(l => l[(l.IndexOf(": ", StringComparison.Ordinal) + 2)..]))}. "
                  + "Too few to tell the odds",
            sets == 0 ? Verdict.NotInTheseLogs : setsWithAnOpenQuarter == sets ? Verdict.Matches : Verdict.Differs)
        {
            Details = layouts,
        };

        yield return new LogFinding(Section, "Cascade 2's layout against the first",
            SimSource.Line(TeaSource.State, "and that Cascade 2's tornadoes can"), "rolled afresh, so it may stand where the first did",
            secondLayouts.Count == 0 ? "no pull reaches Cascade 2"
                : $"{repeats} of {secondLayouts.Count} second sets stand where the first did: {string.Join("; ", secondLayouts)}",
            secondLayouts.Count == 0 ? Verdict.NotInTheseLogs : repeats > 0 ? Verdict.Matches : Verdict.Inconclusive);

        yield return new LogFinding(Section, "A Liquid Rage's water: the object under it",
            SimSource.Line(TeaSource.Constants, "uint LiquidRagePuddle ="), $"event object {P1EObjId.LiquidRagePuddle} on each Liquid Rage's spot",
            tornadoes == 0 ? "no Liquid Rages" : $"{tornadoesWithWater} of {tornadoes} Liquid Rages have object {P1EObjId.LiquidRagePuddle} added on their spot as they appear",
            tornadoes == 0 ? Verdict.NotInTheseLogs : tornadoesWithWater == tornadoes ? Verdict.Matches : Verdict.Differs);

        var dropsy = new Spread(dropsyDistances);
        var nearest = closest.Count == 0 ? (double?)null : closest.Min(c => c.Distance);
        yield return new LogFinding(Section, "The water's radius",
            SimSource.Line(TeaSource.Constants, "float PuddleRadius ="), $"{P1Geometry.PuddleRadius} y: kills whoever enters; in the fight it gives Dropsy",
            dropsy.Count > 0 ? $"{dropsy.Count} gains of Dropsy, their holders {dropsy.Describe(" y")} from a tornado's centre as they gained it (client positions of a player on the move)"
                : nearest is { } n ? $"nobody gained Dropsy; the closest a living player came to a tornado's centre was {Spread.F(n)} y (client positions, good to about a yalm)"
                : "no Liquid Rages",
            dropsy.Count > 0 ? dropsy.Max <= P1Geometry.PuddleRadius + 1 ? Verdict.Matches : dropsy.Max > P1Geometry.PuddleRadius + 2.5 ? Verdict.Differs : Verdict.Inconclusive
            : nearest is { } near ? near < P1Geometry.PuddleRadius - 1 ? Verdict.Differs : Verdict.Inconclusive
            : Verdict.NotInTheseLogs)
        {
            Details = closest.Select(c => c.Detail).ToList(),
        };

        var leave = new Spread(tornadoesLeave);
        yield return new LogFinding(Section, "The first Liquid Rages leave, after the first Drainage",
            SimSource.Line(TeaSource.Mechanics, "Tornadoes.Leave();"), "0 s: with Drainage, water and all",
            leave.Count == 0 ? "no first set seen to leave after a Drainage"
                : $"the Liquid Rages are removed {leave.Describe(" s")} after it, their water's objects {new Spread(puddlesLeave).Describe(" s")} after it: "
                  + "when the client drops them, not when the water stops hurting",
            leave.Count == 0 ? Verdict.NotInTheseLogs : leave.Min < -0.5 ? Verdict.Differs : Verdict.Inconclusive);

        var stay = new Spread(tornadoesStay);
        yield return new LogFinding(Section, "The second Liquid Rages leave",
            SimSource.Line(TeaSource.Mechanics, "Tornadoes.Leave();", 2), "as the Living Liquid falls",
            secondLeave.Count > 0 ? $"removed {new Spread(secondLeave).Describe(" s")} after the Living Liquid falls: when the client drops them"
                : stay.Count == 0 ? "no set stood to a pull's end" : $"no second set seen to go; sets still stood {stay.Describe(" s")} after appearing as their pull ended",
            secondLeave.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);

        yield return new LogFinding(Section, "The helpers set up with the Living Liquid before the pull",
            SimSource.Line(TeaSource.Constants, "int LivingLiquidHelpers ="), $"{P1Rules.LivingLiquidHelpers}",
            helpers.Count == 0 ? "no pull with its standing combatants" : $"{Tally(helpers.Select(h => $"{h}"))}",
            helpers.Count == 0 ? Verdict.NotInTheseLogs : helpers.All(h => h == P1Rules.LivingLiquidHelpers) ? Verdict.Matches : Verdict.Differs);

        yield return new LogFinding(Section, "Cruise Chaser and Brute Justice before the pull",
            SimSource.Line(TeaSource.LimitCut, "private static readonly Vector3 BruteJusticeLandsAt ="), "Cruise Chaser hidden until he first lands; Brute Justice not drawn",
            parked.Count == 0 ? "neither stands in a pull" : Tally(parked), LogFinding.Unplayed(parked.Count));

        yield return new LogFinding(Section, "The entrance barrier's object and spot",
            SimSource.Line(TeaSource.Zone, "public EntranceBarrier EntranceBarrier =>"), $"event object {TeaLogIds.EntranceBarrier} at (0.00, 15.00)",
            barriers.Count == 0 ? "no such object stands before a pull" : $"object {TeaLogIds.EntranceBarrier} stands at {Tally(barriers)} before {barrierPulls} pulls",
            barriers.Count == 0 ? Verdict.NotInTheseLogs : barriers.All(b => b == "(0.00, 15.00)") ? Verdict.Matches : Verdict.Differs);

        yield return new LogFinding(Section, "Map effects", SimSource.Line(TeaSource.Zone, "public sealed class TeaZone : IZone"), "none set",
            mapEffects.Count == 0 ? $"none sent before or during the {pulls} pulls"
                : $"sent at each reset before a pull: {Tally(mapEffects)}; {mapEffectsInAPull.Count} sent during a pull",
            LogFinding.Unplayed(mapEffects.Count))
        {
            Details = mapEffectsInAPull,
        };

        yield return new LogFinding(Section, "The phase's weather", SimSource.Line(TeaSource.Constants, "byte? Weather ="), "left to the territory",
            "ACT logs no weather", Verdict.NotInTheseLogs);
        var tracks = music.Select(m => m[(m.IndexOf("track ", StringComparison.Ordinal) + 6)..].Split(' ', ',')[0]).ToList();
        yield return new LogFinding(Section, "The phase's track: each change of music the duty sends",
            SimSource.Line(TeaSource.Constants, "ushort Bgm ="), $"{Bgm} from a run's start, at the fight's own clock into the track",
            music.Count == 0 ? $"no change of music before or during the {pulls} pulls: the territory's own track plays on through every pull"
                : $"{music.Count} changes over {pulls} pulls, {musicAtAPullsStart} of them as a pull opens: {Tally(tracks.Select(t => $"track {t}"))}. A pull "
                  + $"starts no track: track {Bgm} is only set again after a pull that changed it",
            music.Count == 0 ? Verdict.NotInTheseLogs
            : tracks.Where((_, i) => music[i].Contains("before the pull", StringComparison.Ordinal)).All(t => t == $"{Bgm}") ? Verdict.Matches : Verdict.Differs)
        {
            Details = music,
        };

        var rim = new Spread(farthest);
        var (died, went) = (new Spread(outsideDied), new Spread(outsideAt));
        yield return new LogFinding(Section, "The rim: what becomes of a living player whose position reads beyond it",
            SimSource.Line(TeaSource.Zone, RimInTheZone), RimInTheSim,
            rim.Count + went.Count == 0 ? "no pull"
                : $"{died.Count} of {went.Count} times a living player read beyond {P1Geometry.ArenaRadius} y ({went.Describe(" y")}) it was at 0 HP within 3 s "
                  + $"({died.Describe(" s")}); {outsideLived} lived on"
                  + (rim.Count == 0 ? "" : $". The farthest a player stood and lived 3 s on: {Spread.F(rim.Max)} y, over {rim.Count} pulls"),
            rim.Count + went.Count == 0 ? Verdict.NotInTheseLogs : went.Count == 0 ? Verdict.Inconclusive
            : (outsideLived == 0) == TheSimsRimKills ? Verdict.Matches : Verdict.Differs)
        {
            Details = outside,
        };

        var speeds = new Spread(liquidSpeeds.Concat(handSpeeds));
        yield return new LogFinding(Section, "The bosses' walking speed: over the full steps between two of a boss's own move lines",
            SimSource.Line(TeaSource.Constants, "float BossWalkSpeed ="), $"{P1Rules.BossWalkSpeed} y/s",
            speeds.Count == 0 ? "no boss walked"
                : $"the Living Liquid {Median(liquidSpeeds, " y/s", "no full step")}; the Liquid Hand {Median(handSpeeds, " y/s", "no full step")}",
            speeds.Count == 0 ? Verdict.NotInTheseLogs : Math.Abs(speeds.Median - P1Rules.BossWalkSpeed) <= 0.5 ? Verdict.Matches : Verdict.Differs);

        var (liquidStop, handStop) = (LiquidHitbox + PlayerHitbox + P1Rules.BossStopsPastTheHitboxes, HandHitbox + PlayerHitbox + P1Rules.BossStopsPastTheHitboxes);
        var (liquidRests, handRests) = (new Spread(liquidStops), new Spread(handStops));
        yield return new LogFinding(Section, "The bosses' stand-off: how far from a target that stands still a boss's walk ends, centre to centre",
            SimSource.Line(TeaSource.Constants, "float BossStopsPastTheHitboxes ="),
            $"the Living Liquid {Spread.F(liquidStop, "0.0")} y and the Liquid Hand {Spread.F(handStop, "0.0")} y: {P1Rules.BossStopsPastTheHitboxes} y past the two hitboxes",
            liquidStops.Count + handStops.Count + liquidAutos.Count + handAutos.Count == 0 ? "no boss walked or attacked"
                : $"the Liquid {Median(liquidStops, " y", "no such walk")}, the Hand {Median(handStops, " y", "no such walk")}; as an auto-attack goes off, "
                  + $"with tanks who walk in: the Liquid {Median(liquidAutos, " y", "none")}, the Hand {Median(handAutos, " y", "none")}. "
                  + "The boss's spot is the server's, its target's the client's",
            liquidStops.Count + handStops.Count == 0 ? liquidAutos.Count + handAutos.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive
            : (handRests.Count == 0 || Math.Abs(handRests.Median - handStop) <= 0.3) && (liquidRests.Count == 0 || Math.Abs(liquidRests.Median - liquidStop) <= 0.3)
                ? handRests.Count >= 5 ? Verdict.Matches : Verdict.Inconclusive
                : Verdict.Differs);
    }

    // "median 4.09 y (4.10 y (n=26, 3.80 to 4.60, sd 0.20))", or `none` with no sample.
    private static string Median(List<double> values, string unit, string none)
        => values.Count == 0 ? none : $"median {Spread.F(new Spread(values).Median)}{unit} ({new Spread(values).Describe(unit)})";

    // "a x3, b x1"
    internal static string Tally(IEnumerable<string> values)
        => string.Join(", ", values.GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} x{g.Count()}"));
}
