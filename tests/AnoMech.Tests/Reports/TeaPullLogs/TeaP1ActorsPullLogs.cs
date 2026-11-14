using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// Who stands where: the bosses before the pull and as they appear, the Liquid Rages and their water, the helpers, what
// the arena holds, and how the bosses walk.
internal sealed class TeaP1ActorsPullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 actors";

    private readonly List<string> liquidSpots = [], handSpots = [], parked = [], layouts = [], secondLayouts = [], barriers = [], mapEffects = [];
    private readonly List<double> liquidHeadings = [], handHeadings = [], handAfterCascade = [], handHpShare = [], handHpBesideTheLiquid = [];
    private readonly Dictionary<string, HashSet<int>> maxHp = [];
    private readonly List<double> ringRadii = [], tornadoesLeave = [], puddlesLeave = [], tornadoesStay = [], secondLeave = [];
    private readonly List<int> helpers = [];
    private readonly List<double> liquidSpeeds = [], handSpeeds = [], liquidStops = [], handStops = [], liquidAutos = [], handAutos = [];
    private readonly List<double> dropsyDistances = [];
    private readonly List<(double Distance, string Detail)> closest = [];
    private readonly List<double> farthest = [];
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
        if (pull.End is not { } end) return;
        // A player a throw carried out is at 0 HP a moment later: only those who lived on say where one can stand.
        var reach = Samples(pull.Start, end, 0.25)
                    .SelectMany(t => pull.Living(t).Where(p => pull.DiedWithin(p, t, PullLog.DeathAfterHit) is null).Select(p => pull.Positions.At(p, t)))
                    .Where(p => p is { Gap.TotalSeconds: < 0.5 })
                    .Select(p => (double)Vector2.Distance(p!.Value.Position, TeaPull.Centre))
                    .ToList();
        if (reach.Count > 0) farthest.Add(reach.Max());
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
        // The status comes a tick after the step in, so the nearest the player came in the 3 s before it is what counts.
        foreach (var gain in pull.Statuses.Where(s => s.Gained && s.Status == TeaLogIds.Dropsy && s.Time >= set.Appeared && s.Time <= until))
        {
            var near = Samples(gain.Time.AddSeconds(-3), gain.Time, 0.1).Select(t => pull.Where(gain.Target, t)).OfType<Vector2>()
                                                                       .Select(at => set.Tornadoes.Min(t => (double)Vector2.Distance(t.Position, at))).ToList();
            if (near.Count > 0) dropsyDistances.Add(near.Min());
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
            if (walked && rests && pull.TargetOf(actor, moves[i].Time) is { } target && pull.IsAlive(target, moves[i].Time)
                && pull.Positions.At(target, moves[i].Time.AddSeconds(0.5)) is { Gap.TotalSeconds: < 0.5 } pose)
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
            SimSource.Line(TeaSource.Constants, "float LiquidStartsFacing ="), "not played: a start puts it on the centre, mid-fight",
            liquidSpots.Count == 0 ? "no pull logged from its start"
                : $"{Tally(liquidSpots)}, facing {headings.Describe(" rad")}",
            LogFinding.Unplayed(liquidSpots.Count));

        var after = new Spread(handAfterCascade);
        var share = new Spread(handHpShare);
        yield return new LogFinding(Section, "The Liquid Hand appears: where, facing, how long after Cascade lands, and with what hit points",
            SimSource.Line(TeaSource.Constants, "float HandStartsFromTheCentre ="),
            $"not played: a start puts it {P1Geometry.HandStartsFromTheCentre} y from the centre, mid-fight",
            handSpots.Count == 0 ? "no Liquid Hand"
                : $"{Tally(handSpots)}, facing {new Spread(handHeadings).Describe(" rad")}, {after.Describe(" s")} after Cascade lands; its hit points as it "
                  + $"appears over the Liquid's as Cascade landed: {share.Describe("", "0.000")}; over the Liquid's as it appears: "
                  + new Spread(handHpBesideTheLiquid).Describe("", "0.000"),
            LogFinding.Unplayed(handSpots.Count));

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
            dropsy.Count > 0 ? $"{dropsy.Count} gains of Dropsy, their holders within {dropsy.Describe(" y")} of a tornado's centre in the 3 s before (client positions)"
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
            SimSource.Line(TeaSource.LimitCut, "(where he stands is UNVERIFIED)"), "Cruise Chaser hidden until he first lands; Brute Justice not drawn",
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
        yield return new LogFinding(Section, "The phase's track", SimSource.Line(TeaSource.Constants, "ushort Bgm ="), $"{Bgm}",
            "ACT logs no music", Verdict.NotInTheseLogs);

        var rim = new Spread(farthest);
        yield return new LogFinding(Section, "The rim: the farthest a player stood from the centre and lived on",
            SimSource.Line(TeaSource.Constants, "float ArenaRadius ="), $"{P1Geometry.ArenaRadius} y, a wall",
            rim.Count == 0 ? "no pull" : $"{Spread.F(rim.Max)} y at the most, over {rim.Count} pulls, among players who lived 3 s on (client positions)",
            rim.Count == 0 ? Verdict.NotInTheseLogs : rim.Max > P1Geometry.ArenaRadius + 1 ? Verdict.Differs : Verdict.Inconclusive);

        var speeds = new Spread(liquidSpeeds.Concat(handSpeeds));
        yield return new LogFinding(Section, "The bosses' walking speed: over the full steps between two of a boss's own move lines",
            SimSource.Line(TeaSource.Constants, "float BossWalkSpeed ="), $"{P1Rules.BossWalkSpeed} y/s",
            speeds.Count == 0 ? "no boss walked"
                : $"the Living Liquid median {Spread.F(new Spread(liquidSpeeds).Median)} y/s ({new Spread(liquidSpeeds).Describe(" y/s")}); "
                  + $"the Liquid Hand median {Spread.F(new Spread(handSpeeds).Median)} y/s ({new Spread(handSpeeds).Describe(" y/s")})",
            speeds.Count == 0 ? Verdict.NotInTheseLogs : Math.Abs(speeds.Median - P1Rules.BossWalkSpeed) <= 0.5 ? Verdict.Matches : Verdict.Differs);

        yield return new LogFinding(Section, "The bosses' stand-off: how far from its target a boss comes to rest, centre to centre",
            SimSource.Line(TeaSource.Mechanics, "enemy.Follow(target, P1Rules.BossWalkSpeed);"),
            "the Living Liquid 4.0 y and the Liquid Hand 2.1 y: its hitbox and half a yalm",
            liquidStops.Count + handStops.Count + liquidAutos.Count + handAutos.Count == 0 ? "no boss walked or attacked"
                : $"at the end of a walk: the Liquid {new Spread(liquidStops).Describe(" y")}, the Hand {new Spread(handStops).Describe(" y")}; "
                  + $"as an auto-attack goes off: the Liquid median {Spread.F(new Spread(liquidAutos).Median)} y ({new Spread(liquidAutos).Describe(" y")}), "
                  + $"the Hand median {Spread.F(new Spread(handAutos).Median)} y ({new Spread(handAutos).Describe(" y")}). The boss's spot is the "
                  + "server's, its target's the client's",
            liquidStops.Count + handStops.Count + liquidAutos.Count + handAutos.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);
    }

    // "a x3, b x1"
    internal static string Tally(IEnumerable<string> values)
        => string.Join(", ", values.GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} x{g.Count()}"));
}
