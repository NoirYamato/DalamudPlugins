using System.Numerics;
using AnoMech.Core.Game;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// The P3 intermission, timed from the first Wave Repeater's cast.
internal sealed class TopP3IntermissionPullLogs : ITopPullLogMeasures
{
    private const string Section = "P3 Intermission";
    private const string Constants = "AnoMech/Scenarios/Top/TopP3Constants.cs";
    private const string Scenario = "AnoMech/Scenarios/Top/P3Intermission/TopP3IntermissionScenario.cs";
    private const string State = "AnoMech/Scenarios/Top/P3Intermission/TopP3IntermissionState.cs";

    private static readonly int[] NorthTriangle = [0, 120, 240];

    // Unnamed actions P2's two Omegas (31561 to 31563) and P3's Omega (31564) use on themselves as the one turns into
    // the other.
    private static readonly uint[] MergeActions = [31561, 31562, 31563, 31564];

    private int pulls;
    private readonly List<double> spreadDurations = [], stackDurations = [];
    private readonly List<double> spreadsGoOff = [], stacksGoOff = [], stacksAfterSpreads = [];
    private readonly List<double> armRadii = [], armOffHexagon = [], armOffFacing = [];
    private readonly List<(int Bearing, bool Left)> armKinds = [];
    private readonly List<(bool Left, uint Timeline)> warps = [];
    private readonly List<string> firstSets = [];
    private readonly List<double> firstWarps = [], secondWarps = [];
    private readonly List<double> omegaHeadings = [], targetable = [];
    private readonly List<double> voidZoneShown = [], voidZoneGone = [];
    private readonly List<VoidZoneVisit> voidZoneClosest = [];
    private readonly List<string> armSets = [];
    private readonly List<int> leftArmsPerPull = [];
    private readonly Dictionary<uint, List<double>> merges = [];
    private int omegaShows, voidZoneEffects, singleKindSets;

    // The closest a living player came to the centre while the void zone stood, and when anyone first stood inside the
    // sim's radius; times from the first Wave Repeater's cast.
    private sealed record VoidZoneVisit(string Pull, double Distance, double At, string Who, double? FirstInside, double Gone);

    public void Observe(TopPull pull)
    {
        if (pull.FirstCast(P3ActionId.WaveRepeater) is not { } waves) return;
        pulls++;
        ObserveFodder(pull);
        ObserveArms(pull, waves);
        ObserveOmega(pull, waves);
        ObserveVoidZone(pull, waves);
    }

    private void ObserveFodder(TopPull pull)
    {
        var fodder = pull.Statuses.Where(s => s.Gained && s.Status is P3StatusId.SniperCannonFodder or P3StatusId.HighPoweredSniperCannonFodder)
                         .ToList();
        if (fodder.Count == 0) return;
        spreadDurations.AddRange(fodder.Where(s => s.Status == P3StatusId.SniperCannonFodder).Select(s => (double)s.Duration));
        stackDurations.AddRange(fodder.Where(s => s.Status == P3StatusId.HighPoweredSniperCannonFodder).Select(s => (double)s.Duration));
        var given = fodder.Min(s => s.Time);
        // A cannon can go off early, as one did at another player when its holder died, so each kind's time is the median of
        // its hits.
        var spreads = Volley(pull, ActionId.SniperCannon, given);
        var stacks = Volley(pull, ActionId.HighPoweredSniperCannon, given);
        if (spreads is { } spread) spreadsGoOff.Add(spread);
        if (stacks is { } stack) stacksGoOff.Add(stack);
        if (spreads is { } s1 && stacks is { } s2) stacksAfterSpreads.Add(s2 - s1);
    }

    private static double? Volley(TopPull pull, uint action, DateTimeOffset given)
    {
        var hits = pull.Hits.Where(h => h.Action == action && TopPull.Seconds(given, h.Time) is > 0 and < 30)
                       .Select(h => TopPull.Seconds(given, h.Time)).ToList();
        return hits.Count == 0 ? null : new Spread(hits).Median;
    }

    // The server places each arm on its spot as it warps in, the right arms playing one warp timeline and the left the other.
    private void ObserveArms(TopPull pull, ActCast waves)
    {
        var kinds = pull.Combatants.Where(c => c.Added && c.BNpcBase is BNpcBaseId.LeftArmUnit or BNpcBaseId.RightArmUnit)
                        .GroupBy(c => c.Actor).ToDictionary(g => g.Key, g => g.Last().BNpcBase == BNpcBaseId.LeftArmUnit);
        var placed = pull.Moves.Where(m => m.Placed && kinds.ContainsKey(m.Actor) && TopPull.Seconds(waves.Time, m.Time) is >= 0 and < 15)
                         .ToList();
        if (placed.Count == 6) leftArmsPerPull.Add(placed.Count(m => kinds[m.Actor]));
        foreach (var (set, index) in TopPull.Clusters(placed, m => m.Time, 1).Take(2).Select((s, i) => (s, i)))
        {
            var arms = set.Select(m => (Move: m, Bearing: TopPull.Bearing(m.Position), Left: kinds[m.Actor]))
                          .Select(a => (a.Move, a.Bearing, Spot: (int)(Math.Round(a.Bearing / 60) * 60) % 360, a.Left))
                          .OrderBy(a => a.Spot)
                          .ToList();
            foreach (var arm in arms)
            {
                armRadii.Add(Vector2.Distance(arm.Move.Position, TopPull.Centre));
                armOffHexagon.Add(Math.Min(Math.Abs(arm.Bearing - arm.Spot), 360 - Math.Abs(arm.Bearing - arm.Spot)));
                armOffFacing.Add(TopPull.AngleBetween(arm.Move.Heading, TopPull.HeadingTo(arm.Move.Position, TopPull.Centre)) * 180 / Math.PI);
                armKinds.Add((arm.Spot, arm.Left));
            }
            if (index == 0) firstSets.Add(arms.Select(a => a.Spot).SequenceEqual(NorthTriangle) ? "north" : "south");
            if (arms.Select(a => a.Left).Distinct().Count() == 1) singleKindSets++;
            armSets.Add($"{pull.Name} set {index + 1}: {string.Join(", ", arms.Select(a => $"{a.Spot} {(a.Left ? "left" : "right")}"))}");
            var warpsOfSet = pull.Timelines.Where(t => kinds.ContainsKey(t.Actor) && set.Any(m => m.Actor == t.Actor)
                                                       && TopPull.Seconds(set[0].Time, t.Time) is >= 0 and < 1)
                                 .ToList();
            warps.AddRange(warpsOfSet.Select(t => (kinds[t.Actor], t.Timeline)));
            if (warpsOfSet.Count > 0)
                (index == 0 ? firstWarps : secondWarps).Add(TopPull.Seconds(waves.Time, warpsOfSet.Min(t => t.Time)));
        }
    }

    private void ObserveOmega(TopPull pull, ActCast waves)
    {
        var omega = pull.Combatants.Where(c => c.Added && c.BNpcBase == OmegaP3).Cast<ActCombatant?>().FirstOrDefault();
        if (omega is not { } spawn) return;
        if (spawn.Heading is { } heading) omegaHeadings.Add(heading);
        omegaShows += pull.Timelines.Count(t => t.Actor == spawn.Actor && t.Timeline == P3TimelineId.OmegaShow);
        foreach (var action in MergeActions)
            if (pull.FirstHit(h => h.Action == action && TopPull.Seconds(waves.Time, h.Time) is > -5 and < 15) is { } merge)
            {
                if (!merges.TryGetValue(action, out var offsets)) merges[action] = offsets = [];
                offsets.Add(TopPull.Seconds(waves.Time, merge.Time));
            }
        if (pull.Targetables.Where(t => t.Actor == spawn.Actor && t.Targetable).Cast<ActTargetable?>().FirstOrDefault() is { } shown)
            targetable.Add(TopPull.Seconds(waves.Time, shown.Time));
    }

    private void ObserveVoidZone(TopPull pull, ActCast waves)
    {
        if (pull.Objects.Where(o => o.Added && o.BaseId == P3EObjId.VoidZone).Cast<ActObject?>().FirstOrDefault() is not { } zone) return;
        voidZoneShown.Add(TopPull.Seconds(waves.Time, zone.Time));
        if (pull.Objects.Where(o => !o.Added && o.Actor == zone.Actor && o.Time > zone.Time).Cast<ActObject?>().FirstOrDefault() is not { } gone) return;
        voidZoneGone.Add(TopPull.Seconds(waves.Time, gone.Time));
        voidZoneEffects += pull.Hits.Count(h => h.Source == zone.Actor) + pull.Statuses.Count(s => s.Source == zone.Actor);
        var approaches = pull.Players
                             .SelectMany(p => Samples(zone.Time, gone.Time).Select(t => (Player: p, Time: t, Pose: pull.Positions.At(p, t))))
                             .Where(s => s.Pose is { Gap.TotalSeconds: < 0.5 } && pull.IsAlive(s.Player, s.Time))
                             .Select(s => (s.Player, s.Time, Distance: (double)Vector2.Distance(s.Pose!.Value.Position, TopPull.Centre)))
                             .ToList();
        if (approaches.Count == 0) return;
        var (player, at, distance) = approaches.MinBy(s => s.Distance);
        var firstInside = approaches.Where(s => s.Distance < P3Geometry.VoidZoneRadius).Select(s => (DateTimeOffset?)s.Time).Min();
        voidZoneClosest.Add(new VoidZoneVisit(pull.Name, distance, TopPull.Seconds(waves.Time, at), pull.Who(player),
                                              firstInside is { } inside ? TopPull.Seconds(waves.Time, inside) : null,
                                              TopPull.Seconds(waves.Time, gone.Time)));
    }

    private static IEnumerable<DateTimeOffset> Samples(DateTimeOffset from, DateTimeOffset to)
    {
        for (var t = from; t <= to; t = t.AddSeconds(0.1)) yield return t;
    }

    public IEnumerable<LogFinding> Findings()
    {
        foreach (var (stack, durations, constant) in new[]
                 {
                     (false, spreadDurations, "float SniperCannonFodder ="),
                     (true, stackDurations, "float HighPoweredSniperCannonFodder ="),
                 })
        {
            var spread = new Spread(durations);
            var sim = stack ? P3Duration.HighPoweredSniperCannonFodder : P3Duration.SniperCannonFodder;
            yield return new LogFinding(Section, $"{(stack ? "High-powered Sniper Cannon Fodder" : "Sniper Cannon Fodder")}'s duration",
                SimSource.Line(Constants, constant), $"{sim} s", spread.Describe(" s"), LogFinding.Compare(spread, sim, 0.05));
        }

        var (fodderAt, fodderSeconds) = SimSource.EventTime(Scenario, "ApplyFodder);");
        var (cannonsAt, cannonsSeconds) = SimSource.EventTime(Scenario, "FireSniperCannons);");
        var simVolley = cannonsSeconds - fodderSeconds;
        foreach (var (name, offsets) in new[] { ("spreads", spreadsGoOff), ("stacks", stacksGoOff) })
        {
            var spread = new Spread(offsets);
            yield return new LogFinding(Section, $"Sniper Cannon {name} go off, after the fodder lands ({fodderAt})", cannonsAt,
                simVolley is { } s ? $"{Spread.F(s)} s" : "not found in the scenario", spread.Describe(" s"),
                simVolley is { } sim ? LogFinding.Compare(spread, sim, 0.25) : Verdict.Inconclusive);
        }
        var together = new Spread(stacksAfterSpreads);
        yield return new LogFinding(Section, "The stacks go off with the spreads: the stacks' hit after the spreads'",
            SimSource.Line(Scenario, "UNVERIFIED: the stacks going off with the spreads"), "0 s: one volley",
            together.Describe(" s"), LogFinding.Compare(together, 0, 0.1));

        var radii = new Spread(armRadii);
        yield return new LogFinding(Section, "Arm units' radius: their distance from the centre as the server places them",
            SimSource.Line(Constants, "float ArmRadius ="), $"{P3Geometry.ArmRadius} y", radii.Describe(" y"),
            LogFinding.Compare(radii, P3Geometry.ArmRadius, 0.1));

        var offHexagon = new Spread(armOffHexagon);
        yield return new LogFinding(Section, "Arm units stand on a hexagon from north that never turns: each arm's bearing off the nearest 60 degrees",
            SimSource.Line(State, "UNVERIFIED: the hexagon is assumed never to turn"), "0 degrees",
            offHexagon.Describe(" degrees"), offHexagon.Count == 0 ? Verdict.NotInTheseLogs : offHexagon.Max < 1 ? Verdict.Matches : Verdict.Differs);

        var simLeft = new[] { 0, 240, 300 };
        var byBearing = armKinds.GroupBy(a => a.Bearing).OrderBy(g => g.Key)
                                .Select(g => $"{g.Key}: {g.Count(a => a.Left)} left, {g.Count(a => !a.Left)} right").ToList();
        var agree = armKinds.Count(a => a.Left == simLeft.Contains(a.Bearing));
        yield return new LogFinding(Section, "Which kind of arm stands where",
            SimSource.Line(Scenario, "LeftArmBearings ="),
            "left arms at 0, 240 and 300 degrees, right arms at 60, 120 and 180, so each set has both kinds",
            armKinds.Count == 0 ? "no arms"
                : $"{agree} of {armKinds.Count} arms where the sim has their kind; by bearing: {string.Join("; ", byBearing)}. "
                  + $"Left arms per pull: {string.Join(", ", leftArmsPerPull)}; {singleKindSets} of {armSets.Count} sets are all one kind",
            armKinds.Count == 0 ? Verdict.NotInTheseLogs : agree == armKinds.Count ? Verdict.Matches : Verdict.Differs)
        {
            Details = armSets,
        };

        var simTimelines = warps.All(w => w.Timeline == (w.Left ? ActionTimelineId.WarpEnd2 : ActionTimelineId.WarpEnd));
        yield return new LogFinding(Section, "Each kind's warp-in timeline",
            SimSource.Line(Scenario, "ActionTimelineId.WarpEnd2 : ActionTimelineId.WarpEnd"), "left arms warp_end2, right arms warp_end",
            warps.Count == 0 ? "no warp timelines"
                : string.Join("; ", warps.GroupBy(w => (w.Left, w.Timeline)).Select(g => $"{(g.Key.Left ? "left" : "right")} {g.Key.Timeline} x{g.Count()}")),
            warps.Count == 0 ? Verdict.NotInTheseLogs : simTimelines ? Verdict.Matches : Verdict.Differs);

        var facing = new Spread(armOffFacing);
        yield return new LogFinding(Section, "Arm units face the centre: each arm's heading off the centre's direction",
            SimSource.Line(Scenario, "UNVERIFIED: the arms face the centre"), "0 degrees", facing.Describe(" degrees"),
            facing.Count == 0 ? Verdict.NotInTheseLogs : facing.Max < 1 ? Verdict.Matches : Verdict.Differs);

        var north = firstSets.Count(s => s == "north");
        yield return new LogFinding(Section, "Which triangle of arms warps in first",
            SimSource.Line(State, "UNVERIFIED: even odds"), "north (0, 120, 240) or south (60, 180, 300) at even odds",
            firstSets.Count == 0 ? "no arms" : $"north first in {north} of {firstSets.Count} pulls, south in {firstSets.Count - north}",
            firstSets.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);

        var (wavesAt, wavesSeconds) = SimSource.EventTime(Scenario, "firstWaveHelper?.Cast(Actions.WaveRepeater, ");
        foreach (var (name, line, offsets) in new[]
                 {
                     ("first", "WarpIn(state.FirstSetBearings)", firstWarps),
                     ("second", "WarpIn(state.SecondSetBearings)", secondWarps),
                 })
        {
            var (where, seconds) = SimSource.EventTime(Scenario, line);
            var spread = new Spread(offsets);
            var sim = seconds - wavesSeconds;
            yield return new LogFinding(Section, $"The {name} arms warp in, after the first Wave Repeater's cast ({wavesAt})", where,
                sim is { } s ? $"{Spread.F(s)} s" : "not found in the scenario", spread.Describe(" s"),
                sim is { } simSeconds ? LogFinding.Compare(spread, simSeconds, 0.25) : Verdict.Inconclusive);
        }

        var headings = new Spread(omegaHeadings);
        yield return new LogFinding(Section, "Omega's heading as he spawns (0 faces south)",
            SimSource.Line(Scenario, "UNVERIFIED: Omega facing south"), "0 rad", headings.Describe(" rad"),
            headings.Count == 0 ? Verdict.NotInTheseLogs : headings.Max - headings.Min < 0.05 && Math.Abs(headings.Mean) < 0.05 ? Verdict.Matches : Verdict.Differs);

        var (showAt, _) = SimSource.EventTime(Scenario, "PlayActionTimeline(P3TimelineId.OmegaShow)");
        var merging = string.Join("; ", MergeActions.Where(merges.ContainsKey)
                                                    .Select(a => $"{a} at {new Spread(merges[a]).Describe(" s")}"));
        yield return new LogFinding(Section, "Omega's appearance (his show timeline), after the first Wave Repeater's cast", showAt,
            "plays with the first Wave Repeater's cast",
            (omegaShows == 0 ? $"no pull of {pulls} logs the timeline on Omega" : $"logged {omegaShows} times")
            + (merging.Length == 0 ? "" : $". The Omegas' own unnamed actions as P2's merge into P3's: {merging}"),
            omegaShows == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);

        var (targetableAt, targetableSeconds) = SimSource.EventTime(Scenario, "omega?.SetTargetable(true)");
        var shown = new Spread(targetable);
        var simTargetable = targetableSeconds - wavesSeconds;
        yield return new LogFinding(Section, "Omega turns targetable, after the first Wave Repeater's cast", targetableAt,
            simTargetable is { } t ? $"{Spread.F(t)} s" : "not found in the scenario", shown.Describe(" s"),
            simTargetable is { } simShown ? LogFinding.Compare(shown, simShown, 0.25) : Verdict.Inconclusive);

        foreach (var (name, line, offsets) in new[] { ("appears", "SpawnVoidZone);", voidZoneShown), ("goes", "DespawnVoidZone);", voidZoneGone) })
        {
            var (where, seconds) = SimSource.EventTime(Scenario, line);
            var spread = new Spread(offsets);
            var sim = seconds - wavesSeconds;
            yield return new LogFinding(Section, $"The void zone {name}, after the first Wave Repeater's cast", where,
                sim is { } s ? $"{Spread.F(s)} s" : "not found in the scenario", spread.Describe(" s"),
                sim is { } simSeconds ? LogFinding.Compare(spread, simSeconds, 0.25) : Verdict.Inconclusive);
        }

        var entries = new Spread(voidZoneClosest.Where(c => c.FirstInside is not null).Select(c => c.FirstInside!.Value));
        var gaps = new Spread(voidZoneClosest.Where(c => c.FirstInside is not null).Select(c => c.Gone - c.FirstInside!.Value));
        yield return new LogFinding(Section, "Void zone's radius: how close living players came to the centre while it stood",
            SimSource.Line(Constants, "float VoidZoneRadius ="), $"{P3Geometry.VoidZoneRadius} y: kills anyone closer while it stands",
            voidZoneClosest.Count == 0 ? "no void zone"
                : (entries.Count == 0
                      ? $"nobody stood within {P3Geometry.VoidZoneRadius} y of the centre while it stood in {voidZoneClosest.Count} pulls"
                      : $"in {entries.Count} of {voidZoneClosest.Count} pulls someone stood within {P3Geometry.VoidZoneRadius} y, never "
                        + $"before {Spread.F(entries.Min)} s, {Spread.F(gaps.Min)} to {Spread.F(gaps.Max)} s before the zone went")
                  + $" (client positions). {voidZoneEffects} abilities or statuses in the logs come from the zone's object, so its "
                  + "radius and when it stops counting can't be told apart",
            voidZoneClosest.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = voidZoneClosest.Select(c => $"{c.Pull}: closest {Spread.F(c.Distance)} y ({c.Who}) at {Spread.F(c.At)} s, "
                                                  + (c.FirstInside is { } inside ? $"first inside {P3Geometry.VoidZoneRadius} y at {Spread.F(inside)} s"
                                                                                 : $"nobody inside {P3Geometry.VoidZoneRadius} y")
                                                  + $", zone gone at {Spread.F(c.Gone)} s").ToList(),
        };

        yield return new LogFinding(Section, "The void zone's spawn arguments", SimSource.Line(Constants, "UNVERIFIED: the void zone's real spawn args"),
            $"event {P3EObjId.VoidZoneEventId:X8}, arg2 {P3EObjId.VoidZoneArg2}", "the log has the object's id and position, not its spawn packet",
            Verdict.NotInTheseLogs);
        yield return new LogFinding(Section, "P3's weather", SimSource.Line(Constants, "byte Weather ="), $"{Weather}",
            "ACT logs no weather", Verdict.NotInTheseLogs);
        yield return new LogFinding(Section, "Whether the instant rings show anything from an invisible helper",
            SimSource.Line(Scenario, "UNVERIFIED: whether the instant rings show anything"), "the sim casts them from invisible helpers",
            "what the client draws is not logged", Verdict.NotInTheseLogs);
    }
}
