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
    private const string Actions = "AnoMech/Scenarios/Top/TopP3Actions.cs";
    private const string Scenario = "AnoMech/Scenarios/Top/P3Intermission/TopP3IntermissionScenario.cs";
    private const string State = "AnoMech/Scenarios/Top/P3Intermission/TopP3IntermissionState.cs";

    private static readonly int[] NorthTriangle = [0, 120, 240];

    // Unnamed actions P2's two Omegas (31561 to 31563) and P3's Omega (31564) use on themselves as the one turns into
    // the other.
    private static readonly uint[] MergeActions = [31561, 31562, 31563, 31564];

    // The intermission's hits land within this many seconds of the first Wave Repeater's cast.
    private const double Length = 30;

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
    private readonly List<VoidZoneVisit> voidZoneVisits = [];
    private readonly List<string> armSets = [];
    private readonly List<int> leftArmsPerPull = [];
    private readonly List<string> leftArmSpots = [];
    private readonly Dictionary<uint, List<double>> merges = [];
    private readonly LethalHits ringHits = new(), blowHits = new();
    private readonly List<(HitOutcome Outcome, string Detail)> doubledCannons = [], singleCannons = [], loneStacks = [];
    private readonly List<int> stackSizes = [];
    private int omegaShows, voidZoneEffects, singleKindSets;

    // While the void zone's object stood: the closest a player came to the centre and lived on, the first time one stood
    // inside the sim's radius and lived on, and who died soon after standing inside it; times from the first Wave
    // Repeater's cast.
    private sealed record VoidZoneVisit(
        string Pull, double Gone, double? FirstInside, (double Distance, double At, string Who)? Closest, double? FirstLivedInside,
        List<string> DiedInside);

    public void Observe(TopPull pull)
    {
        if (pull.FirstCast(P3ActionId.WaveRepeater) is not { } waves) return;
        pulls++;
        ObserveFodder(pull);
        ObserveArms(pull, waves);
        ObserveOmega(pull, waves);
        ObserveVoidZone(pull, waves);
        bool During(ActHit hit) => TopPull.Seconds(waves.Time, hit.Time) is >= 0 and < Length;
        string At(ActHit hit) => $"at {Spread.F(TopPull.Seconds(waves.Time, hit.Time))} s";
        ringHits.Observe(pull, pull.Hits.Where(h => TopPull.IsWaveRepeaterRing(h.Action) && During(h)), h => $"{TopPull.WaveRepeaterRing(h.Action)} {At(h)}");
        blowHits.Observe(pull, pull.Hits.Where(h => h.Action == P3ActionId.ColossalBlow && During(h)), h => $"Colossal Blow {At(h)}");
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
        ObserveCannonKills(pull, given);
    }

    private static double? Volley(TopPull pull, uint action, DateTimeOffset given)
    {
        var hits = CannonHits(pull, given).Where(h => h.Action == action).Select(h => TopPull.Seconds(given, h.Time)).ToList();
        return hits.Count == 0 ? null : new Spread(hits).Median;
    }

    private static IEnumerable<ActHit> CannonHits(TopPull pull, DateTimeOffset given)
        => pull.Hits.Where(h => h.Action is ActionId.SniperCannon or ActionId.HighPoweredSniperCannon && TopPull.Seconds(given, h.Time) is > 0 and < Length);

    // A cannon is one ability's hits, all logged at once. A player in two cannons' circles shows what two do, one in a single
    // cannon's what one does, unless that cannon was a stack it took alone.
    private void ObserveCannonKills(TopPull pull, DateTimeOffset given)
    {
        var hits = CannonHits(pull, given).Where(h => h.HitSomeone).ToList();
        var cannons = hits.GroupBy(h => (h.Source, h.Time)).Select(g => g.ToList()).ToList();
        List<ActHit> CannonOf(ActHit hit) => cannons.Single(c => c.Contains(hit));
        stackSizes.AddRange(cannons.Where(c => c[0].Action == ActionId.HighPoweredSniperCannon).Select(c => c.Count));
        foreach (var taken in hits.GroupBy(h => h.Target).Select(g => g.ToList()))
        {
            var outcomes = taken.Select(pull.Outcome).ToList();
            var outcome = outcomes.Contains(HitOutcome.Invulnerable) ? HitOutcome.Invulnerable
                : outcomes.Contains(HitOutcome.Killed) ? HitOutcome.Killed
                : outcomes.All(o => o == HitOutcome.AlreadyDead) ? HitOutcome.AlreadyDead
                : HitOutcome.Survived;
            if (outcome == HitOutcome.AlreadyDead) continue;
            var kinds = taken.Select(CannonOf).Select(c => $"{(c[0].Action == ActionId.SniperCannon ? "a spread" : "a stack")} of {c.Count}");
            var detail = $"{pull.Name}: {string.Join(" and ", kinds)} on {pull.Who(taken[0].Target)}: {string.Join(", then ", taken.Select(pull.Describe))}";
            var into = taken.Count >= 2 ? doubledCannons
                : taken[0].Action == ActionId.HighPoweredSniperCannon && CannonOf(taken[0]).Count == 1 ? loneStacks
                : singleCannons;
            into.Add((outcome, detail));
        }
    }

    // The server places each arm on its spot as it warps in, the right arms playing one warp timeline and the left the other.
    private void ObserveArms(TopPull pull, ActCast waves)
    {
        var kinds = pull.Combatants.Where(c => c.Added && c.BNpcBase is BNpcBaseId.LeftArmUnit or BNpcBaseId.RightArmUnit)
                        .GroupBy(c => c.Actor).ToDictionary(g => g.Key, g => g.Last().BNpcBase == BNpcBaseId.LeftArmUnit);
        var placed = pull.Moves.Where(m => m.Placed && kinds.ContainsKey(m.Actor) && TopPull.Seconds(waves.Time, m.Time) is >= 0 and < 15)
                         .ToList();
        if (placed.Count == 6)
        {
            var left = placed.Where(m => kinds[m.Actor]).ToList();
            leftArmsPerPull.Add(left.Count);
            leftArmSpots.Add(string.Join(", ", left.Select(m => Spot(TopPull.Bearing(m.Position))).Order()));
        }
        foreach (var (set, index) in TopPull.Clusters(placed, m => m.Time, 1).Take(2).Select((s, i) => (s, i)))
        {
            var arms = set.Select(m => (Move: m, Bearing: TopPull.Bearing(m.Position), Left: kinds[m.Actor]))
                          .Select(a => (a.Move, a.Bearing, Spot: Spot(a.Bearing), a.Left))
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

    // The hexagon's corner nearest the bearing, in degrees from north.
    private static int Spot(double bearing) => (int)(Math.Round(bearing / 60) * 60) % 360;

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

    // The logs show the zone's object come and go, and where living players stood meanwhile: one who stood inside the sim's
    // radius and lived on shows the zone was not killing there then.
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
                             .Select(s => (s.Player, s.Time, Distance: (double)Vector2.Distance(s.Pose!.Value.Position, TopPull.Centre),
                                           Died: pull.DiedWithin(s.Player, s.Time, TopPull.DeathAfterHit)))
                             .ToList();
        var lived = approaches.Where(a => a.Died is null).ToList();
        (double Distance, double At, string Who)? closest = null;
        if (lived.Count > 0)
        {
            var nearest = lived.MinBy(a => a.Distance);
            closest = (nearest.Distance, TopPull.Seconds(waves.Time, nearest.Time), pull.Who(nearest.Player));
        }
        var firstInside = approaches.Where(a => a.Distance < P3Geometry.VoidZoneRadius).Select(a => (DateTimeOffset?)a.Time).Min();
        var firstLivedInside = lived.Where(a => a.Distance < P3Geometry.VoidZoneRadius).Select(a => (DateTimeOffset?)a.Time).Min();
        var diedInside = approaches.Where(a => a.Died is not null && a.Distance < P3Geometry.VoidZoneRadius)
                                   .GroupBy(a => a.Player)
                                   .Select(g => g.First())
                                   .Select(a => $"{pull.Who(a.Player)} stood {Spread.F(a.Distance)} y out at {Spread.F(TopPull.Seconds(waves.Time, a.Time))} s "
                                                + $"and died {Spread.F(TopPull.Seconds(a.Time, a.Died!.Value))} s later")
                                   .ToList();
        double? Since(DateTimeOffset? at) => at is { } time ? TopPull.Seconds(waves.Time, time) : null;
        voidZoneVisits.Add(new VoidZoneVisit(pull.Name, TopPull.Seconds(waves.Time, gone.Time), Since(firstInside), closest,
                                             Since(firstLivedInside), diedInside));
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
            SimSource.Line(Scenario, ".Concat(Alive(stackHolders)"), "0 s: one volley",
            together.Describe(" s"), LogFinding.Compare(together, 0, 0.1));

        yield return TwoCannonsKill();
        yield return LoneStackKills();

        var radii = new Spread(armRadii);
        yield return new LogFinding(Section, "Arm units' radius: their distance from the centre as the server places them",
            SimSource.Line(Constants, "float ArmRadius ="), $"{P3Geometry.ArmRadius} y", radii.Describe(" y"),
            LogFinding.Compare(radii, P3Geometry.ArmRadius, 0.1));

        var offHexagon = new Spread(armOffHexagon);
        yield return new LogFinding(Section, "Arm units stand on a hexagon from north that never turns: each arm's bearing off the nearest 60 degrees",
            SimSource.Line(State, "float[] NorthTriangle = [0f, 120f, 240f]"), "0 degrees",
            offHexagon.Describe(" degrees"), offHexagon.Count == 0 ? Verdict.NotInTheseLogs : offHexagon.Max < 1 ? Verdict.Matches : Verdict.Differs);

        yield return ArmKinds();

        var simTimelines = warps.All(w => w.Timeline == (w.Left ? ActionTimelineId.WarpEnd2 : ActionTimelineId.WarpEnd));
        yield return new LogFinding(Section, "Each kind's warp-in timeline",
            SimSource.Line(Scenario, "ActionTimelineId.WarpEnd2 : ActionTimelineId.WarpEnd"), "left arms warp_end2, right arms warp_end",
            warps.Count == 0 ? "no warp timelines"
                : string.Join("; ", warps.GroupBy(w => (w.Left, w.Timeline)).Select(g => $"{(g.Key.Left ? "left" : "right")} {g.Key.Timeline} x{g.Count()}")),
            warps.Count == 0 ? Verdict.NotInTheseLogs : simTimelines ? Verdict.Matches : Verdict.Differs);

        var facing = new Spread(armOffFacing);
        yield return new LogFinding(Section, "Arm units face the centre: each arm's heading off the centre's direction",
            SimSource.Line(Scenario, "MathF.Atan2(-position.X, -position.Z)"), "0 degrees", facing.Describe(" degrees"),
            facing.Count == 0 ? Verdict.NotInTheseLogs : facing.Max < 1 ? Verdict.Matches : Verdict.Differs);

        var unknownDamage = SimSource.Line(Actions, "UNVERIFIED: no damage values are known, so every");
        yield return ringHits.Finding(Section, "Wave Repeater's middle and rings kill whoever they hit", unknownDamage,
            "every hit kills, the game's damage being unknown");
        yield return blowHits.Finding(Section, "Colossal Blow kills whoever it hits", unknownDamage,
            "every hit kills, the game's damage being unknown");

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

        foreach (var finding in VoidZoneFindings(wavesSeconds))
            yield return finding;

        yield return new LogFinding(Section, "The void zone's spawn arguments", SimSource.Line(Constants, "UNVERIFIED: the void zone's real spawn args"),
            $"event {P3EObjId.VoidZoneEventId:X8}, arg2 {P3EObjId.VoidZoneArg2}", "the log has the object's id and position, not its spawn packet",
            Verdict.NotInTheseLogs);
        yield return new LogFinding(Section, "P3's weather", SimSource.Line(Constants, "byte Weather ="), $"{Weather}",
            "ACT logs no weather", Verdict.NotInTheseLogs);
        yield return new LogFinding(Section, "Whether the instant rings show anything from an invisible helper",
            SimSource.Line(Scenario, "UNVERIFIED: whether the instant rings show anything"), "the sim casts them from invisible helpers",
            "what the client draws is not logged", Verdict.NotInTheseLogs);
    }

    // The sim rolls which three of the six spots get the left arms, so each pull with all six placed should show three of
    // each kind, on spots that change from pull to pull.
    private LogFinding ArmKinds()
    {
        var byBearing = armKinds.GroupBy(a => a.Bearing).OrderBy(g => g.Key)
                                .Select(g => $"{g.Key}: {g.Count(a => a.Left)} left, {g.Count(a => !a.Left)} right");
        var spots = leftArmSpots.Distinct().Count();
        return new LogFinding(Section, "Which kind of arm stands where",
            SimSource.Line(State, "LeftArmBearings = rng."),
            "three left arms and three right, the left on three of the six spots rolled at random, so a set can be all one kind",
            armKinds.Count == 0 ? "no arms"
                : $"left arms per pull: {string.Join(", ", leftArmsPerPull)}; {spots} different choices of spots for the left arms in "
                  + $"{leftArmSpots.Count} pulls; {singleKindSets} of {armSets.Count} sets are all one kind; by bearing: {string.Join("; ", byBearing)}",
            armKinds.Count == 0 ? Verdict.NotInTheseLogs
            : leftArmsPerPull.Any(n => n != 3) ? Verdict.Differs
            : spots >= 2 ? Verdict.Matches
            : Verdict.Inconclusive)
        {
            Details = armSets,
        };
    }

    private LogFinding TwoCannonsKill()
    {
        var doubled = doubledCannons.Where(c => c.Outcome != HitOutcome.Invulnerable).ToList();
        var single = singleCannons.Where(c => c.Outcome != HitOutcome.Invulnerable).ToList();
        var shielded = doubledCannons.Count + singleCannons.Count - doubled.Count - single.Count;
        var doubledDied = doubled.Count(c => c.Outcome == HitOutcome.Killed);
        var singleDied = single.Count(c => c.Outcome == HitOutcome.Killed);
        var verdict = doubled.Count + single.Count == 0 ? shielded == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive
            : doubledDied < doubled.Count || singleDied > 0 ? Verdict.Differs
            : doubled.Count == 0 ? Verdict.Inconclusive
            : Verdict.Matches;
        return new LogFinding(Section, "Two Sniper Cannons kill, one is survived", SimSource.Line(Scenario, "private const string TwoCannons"),
            "a player in two cannons' circles dies, a spread holder's own counting; one cannon is survived",
            doubled.Count + single.Count == 0 ? "no cannon hit a living player"
                : $"{doubledDied} of {doubled.Count} players in two or more cannons died of them; {singleDied} of {single.Count} in one died of it"
                  + (shielded == 0 ? "" : $"; {shielded} under an invulnerability left out"),
            verdict)
        {
            Details = doubledCannons.Concat(singleCannons.Where(c => c.Outcome != HitOutcome.Survived)).Select(c => c.Detail).ToList(),
        };
    }

    private LogFinding LoneStackKills()
    {
        var lone = loneStacks.Where(c => c.Outcome != HitOutcome.Invulnerable).ToList();
        var died = lone.Count(c => c.Outcome == HitOutcome.Killed);
        var sizes = string.Join(", ", stackSizes.GroupBy(s => s).OrderBy(g => g.Key).Select(g => $"{g.Count()} hit {g.Key}"));
        return new LogFinding(Section, "A High-powered Sniper Cannon its holder takes alone kills it",
            SimSource.Line(Scenario, "private const string StackAlone"), "a stack that hits only its holder kills it; two or three share it",
            (lone.Count == 0 ? "no stack hit a lone living player" : $"{died} of {lone.Count} stacks that hit one player killed it")
            + (stackSizes.Count == 0 ? "" : $". Of {stackSizes.Count} stacks, {sizes}"),
            lone.Count == 0 ? Verdict.NotInTheseLogs : died == lone.Count ? Verdict.Matches : Verdict.Differs)
        {
            Details = loneStacks.Select(c => c.Detail).ToList(),
        };
    }

    // The sim's spawn and despawn also start and end the zone's kill check, which the object's lifetime need not match.
    private IEnumerable<LogFinding> VoidZoneFindings(float? wavesSeconds)
    {
        var radius = P3Geometry.VoidZoneRadius;
        var (shownAt, shownSeconds) = SimSource.EventTime(Scenario, "SpawnVoidZone);");
        var (goneAt, goneSeconds) = SimSource.EventTime(Scenario, "DespawnVoidZone);");
        var simShown = shownSeconds - wavesSeconds;
        var simGone = goneSeconds - wavesSeconds;
        var firstInside = new Spread(voidZoneVisits.Where(v => v.FirstInside is not null).Select(v => v.FirstInside!.Value));
        var livedInside = new Spread(voidZoneVisits.Where(v => v.FirstLivedInside is not null).Select(v => v.FirstLivedInside!.Value));
        var closest = new Spread(voidZoneVisits.Where(v => v.Closest is not null).Select(v => v.Closest!.Value.Distance));
        var nearest = $"{Spread.F(closest.Min)} y";

        var shown = new Spread(voidZoneShown);
        yield return new LogFinding(Section, "The void zone's object appears (the client adds it), after the first Wave Repeater's cast", shownAt,
            simShown is { } s ? $"{Spread.F(s)} s, which also starts its kill check" : "not found in the scenario",
            shown.Describe(" s") + (shown.Count == 0 ? ""
                : firstInside.Count == 0 ? $". Nobody stood within {radius} y while it stood, so when it starts to kill is not in these logs"
                : $". Nobody stood within {radius} y before {Spread.F(firstInside.Min)} s, so when it starts to kill is not in these logs"),
            simShown is { } simSeconds ? LogFinding.Compare(shown, simSeconds, 0.25) : Verdict.Inconclusive);

        var gone = new Spread(voidZoneGone);
        yield return new LogFinding(Section, "The void zone's object goes (the client removes it), after the first Wave Repeater's cast", goneAt,
            simGone is { } g ? $"{Spread.F(g)} s, which also ends its kill check" : "not found in the scenario",
            gone.Describe(" s") + (gone.Count == 0 ? ""
                : livedInside.Count == 0 ? ". The object's lifetime; when the zone stops killing is not in these logs"
                : $". The object's lifetime, not when the zone stops killing: players stood within {radius} y before it went and lived (next row)"),
            gone.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);

        yield return new LogFinding(Section,
            "The void zone stops killing: the first time a player stood within its radius and lived on, after the first Wave Repeater's cast",
            goneAt, simGone is { } end ? $"{Spread.F(end)} s, at {radius} y" : "not found in the scenario",
            livedInside.Count == 0 ? $"nobody stood within {radius} y while its object stood and lived on"
                : $"in {livedInside.Count} of {voidZoneVisits.Count} pulls a player stood within {radius} y while its object stood and lived "
                  + $"{TopPull.DeathAfterHit} s on, first at {Spread.F(livedInside.Min)} to {Spread.F(livedInside.Max)} s (client positions). "
                  + $"So a {radius} y zone stops killing by {Spread.F(livedInside.Min)} s; one that kills until its object goes is narrower "
                  + $"than {nearest} (next row)",
            livedInside.Count == 0 ? Verdict.NotInTheseLogs
            : simGone is not { } simEnd ? Verdict.Inconclusive
            : simEnd <= livedInside.Min ? Verdict.Matches
            : Verdict.Differs)
        {
            Details = voidZoneVisits.SelectMany(v => v.DiedInside.Select(d => $"{v.Pull}: {d}")).ToList(),
        };

        yield return new LogFinding(Section, "Void zone's radius: the closest a player came to the centre while its object stood and lived on",
            SimSource.Line(Constants, "float VoidZoneRadius ="), $"{radius} y: kills anyone closer while it stands",
            closest.Count == 0 ? "no void zone"
                : $"{nearest}, the closest over {closest.Count} pulls (client positions). {voidZoneEffects} abilities or statuses in the logs "
                  + "come from the zone's object, so its radius and when it stops killing can't be told apart",
            closest.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = voidZoneVisits.Select(v => $"{v.Pull}: "
                                                 + (v.Closest is { } c ? $"closest {Spread.F(c.Distance)} y ({c.Who}) at {Spread.F(c.At)} s" : "nobody lived on")
                                                 + (v.FirstLivedInside is { } inside ? $", first lived within {radius} y at {Spread.F(inside)} s" : "")
                                                 + $", object gone at {Spread.F(v.Gone)} s").ToList(),
        };
    }
}
