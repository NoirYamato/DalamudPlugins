using System.Numerics;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// P3 Hello World, timed from Omega's Hello World cast.
internal sealed class TopP3HelloWorldPullLogs : ITopPullLogMeasures
{
    private const string Section = "P3 Hello World";
    private const string Constants = "AnoMech/Scenarios/Top/TopP3Constants.cs";
    private const string Scenario = "AnoMech/Scenarios/Top/P3HelloWorld/TopP3HelloWorldScenario.cs";

    private readonly List<double> towerRadii = [];
    private readonly List<TowerSet> towerSets = [];
    private readonly List<(double Duration, double Delay)> towerDebuffs = [];
    private readonly List<(double Duration, double Delay)> latentDefects = [];
    private readonly List<Regression> regressions = [];
    private readonly List<Catch> catches = [];
    private readonly Dictionary<string, List<double>> times = [];
    private readonly Dictionary<string, List<double>> resolveOffsets = [];

    // The timeline's events: what the log shows, and the text and occurrence of the scenario's world.Events.Add line for it.
    private static readonly (string Key, string Event, string Line, int Occurrence)[] Timeline =
    [
        ("debuffs", "Hello World hits and its debuffs land", "ApplyHelloWorldDebuffs", 1),
        ("bugs", "The code smells turn into bugs and rots", "TurnSmellsIntoBugs", 1),
        ("start 1", "Set 1's towers and Latent Defect cast", "StartSet(1)", 1),
        ("regressions 1", "Set 1's regressions form", "StartRegressions(1)", 1),
        ("resolve 1", "Set 1's towers, stacks and defamations go off", "ResolveSet(", 1),
        ("start 2", "Set 2's towers and Latent Defect cast", "StartSet(2)", 1),
        ("regressions 2", "Set 2's regressions form", "StartRegressions(2)", 1),
        ("resolve 2", "Set 2's towers, stacks and defamations go off", "ResolveSet(", 2),
        ("start 3", "Set 3's towers and Latent Defect cast", "StartSet(3)", 1),
        ("regressions 3", "Set 3's regressions form", "StartRegressions(3)", 1),
        ("resolve 3", "Set 3's towers, stacks and defamations go off", "ResolveSet(", 3),
        ("start 4", "Set 4's towers and Latent Defect cast", "StartSet(4)", 1),
        ("regressions 4", "Set 4's regressions form", "StartRegressions(4)", 1),
        ("resolve 4", "Set 4's towers, stacks and defamations go off", "ResolveSet(", 4),
        ("critical error", "Critical Error's cast", "Actions.CriticalError", 1),
        ("critical error hits", "Critical Error hits", "ResolveCriticalError", 1),
    ];

    private sealed record TowerSet(string Pull, int Set, string Placement, bool SameColoursAdjacent, bool SameAsLastSet, string Layout);

    // BrokeAfter (seconds from forming) is null when the tethers ran out or a holder died; Distance is the server's, from the
    // break's two helpers.
    private sealed record Regression(
        string Pull, int Set, bool Local, double? BrokeAfter, double? Distance, double? DistanceAtStart, string Pair);

    private sealed record Catch(string Pull, string Colour, double Distance, double Gap, string Who, bool BySelf);

    public void Observe(TopPull pull)
    {
        if (pull.FirstCast(P3ActionId.HelloWorld) is not { } cast) return;
        ObserveTowers(pull, cast);
        ObserveDebuffs(pull);
        ObserveRegressions(pull, cast);
        ObserveRots(pull, cast);
        ObserveTimeline(pull, cast);
    }

    private void ObserveTowers(TopPull pull, ActCast helloWorld)
    {
        var sets = TopPull.Clusters(pull.Casts.Where(c => IsTower(c.Action) && c.Time >= helloWorld.Time), c => c.Time, 1);
        string? lastLayout = null;
        foreach (var (set, number) in sets.Select((s, i) => (s, i + 1)))
        {
            var towers = set.Select(c => (Red: c.Action == P3ActionId.RedTower, At: pull.CastAt(c)?.Position))
                            .Where(t => t.At is not null)
                            .Select(t => (t.Red, At: t.At!.Value, Bearing: TopPull.Bearing(t.At.Value)))
                            .ToList();
            towerRadii.AddRange(towers.Select(t => (double)Vector2.Distance(t.At, TopPull.Centre)));
            if (towers.Count != 4) continue;
            var placement = towers.All(t => OffGrid(t.Bearing, 0) < 5) ? "cardinals"
                : towers.All(t => OffGrid(t.Bearing, 45) < 5) ? "intercardinals"
                : "elsewhere";
            var adjacent = new[] { true, false }.All(red =>
                towers.Where(t => t.Red == red).Select(t => t.Bearing).ToList() is [var a, var b] && Math.Abs(Degrees(a, b) - 90) < 5);
            var layout = string.Join(", ", towers.OrderBy(t => t.Bearing).Select(t => $"{(t.Red ? "red" : "blue")} {Compass(t.Bearing)}"));
            towerSets.Add(new TowerSet(pull.Name, number, placement, adjacent, layout == lastLayout, layout));
            lastLayout = layout;
        }
    }

    private void ObserveDebuffs(TopPull pull)
    {
        foreach (var gain in pull.Statuses.Where(s => s.Gained && s.Status is P3StatusId.RedTowerDebuff or P3StatusId.BlueTowerDebuff))
            if (pull.LastHit(h => IsTower(h.Action) && h.Target == gain.Target && h.Time <= gain.Time) is { } soak)
                towerDebuffs.Add((gain.Duration, TopPull.Seconds(soak.Time, gain.Time)));
        foreach (var gain in pull.Statuses.Where(s => s.Gained && s.Status == P3StatusId.LatentDefect))
            if (pull.LastHit(h => h.Action == P3ActionId.HelloWorld && h.Target == gain.Target && h.Time <= gain.Time.AddSeconds(0.05)) is { } hit)
                latentDefects.Add((gain.Duration, TopPull.Seconds(hit.Time, gain.Time)));
    }

    private void ObserveRegressions(TopPull pull, ActCast helloWorld)
    {
        foreach (var local in new[] { false, true })
        {
            var status = local ? P3StatusId.LocalRegression : P3StatusId.RemoteRegression;
            var starts = TopPull.Clusters(pull.Statuses.Where(s => s.Gained && s.Status == status && s.Time >= helloWorld.Time), s => s.Time, 1);
            foreach (var (start, set) in starts.Select((s, i) => (s, i + 1)))
            {
                var pair = start.Select(s => s.Target).Distinct().ToList();
                if (pair.Count != 2) continue;
                var startedAt = start[0].Time;
                var lostAt = pull.Statuses.Where(s => !s.Gained && s.Status == status && pair.Contains(s.Target) && s.Time > startedAt)
                                 .Select(s => (DateTimeOffset?)s.Time).FirstOrDefault();
                var breakHit = lostAt is { } lost
                    ? pull.FirstHit(h => h.Action == ActionId.HwTetherBreak && TopPull.Seconds(lost, h.Time) is >= -0.05 and <= 0.5)
                    : null;
                double? brokeAfter = null, distance = null;
                if (lostAt is { } brokeAt && breakHit is { } hit)
                {
                    brokeAfter = TopPull.Seconds(startedAt, brokeAt);
                    distance = BreakDistance(pull, pair, brokeAt, hit);
                }
                regressions.Add(new Regression(pull.Name, set, local, brokeAfter, distance, pull.Distance(pair[0], pair[1], startedAt),
                                               $"{pull.Who(pair[0])} + {pull.Who(pair[1])}"));
            }
        }
    }

    // The break hit comes from one helper per end of the tether, placed where the server had that player.
    private static double? BreakDistance(TopPull pull, List<uint> pair, DateTimeOffset lostAt, ActHit breakHit)
    {
        var helpers = pull.Hits.Where(h => h.Action == ActionId.HwTetherBreak && Math.Abs(TopPull.Seconds(breakHit.Time, h.Time)) < 0.05)
                          .Select(h => h.Source).ToHashSet();
        var placed = pull.Moves.Where(m => m.Placed && helpers.Contains(m.Actor) && Math.Abs(TopPull.Seconds(lostAt, m.Time)) <= 0.1)
                         .Select(m => m.Position).ToList();
        if (placed.Count < 2) return null;
        var ends = pair.Select(p => pull.Positions.At(p, lostAt) is { } pose
                                        ? placed.MinBy(at => Vector2.Distance(at, pose.Position))
                                        : (Vector2?)null).ToList();
        return ends is [{ } a, { } b] && a != b ? Vector2.Distance(a, b) : null;
    }

    private void ObserveRots(TopPull pull, ActCast helloWorld)
    {
        foreach (var (status, colour) in new[] { (P3StatusId.RedRot, "red"), (P3StatusId.BlueRot, "blue") })
        {
            var holdings = pull.Holdings(status).Where(h => h.From >= helloWorld.Time).ToList();
            if (holdings.Count == 0) continue;
            var handedOut = holdings.Min(h => h.From);
            foreach (var (receiver, caughtAt, _) in holdings.Where(h => TopPull.Seconds(handedOut, h.From) > 0.5))
            {
                if (pull.Positions.At(receiver, caughtAt) is not { } at) continue;
                var givers = holdings.Where(h => h.Holder != receiver && h.From < caughtAt.AddSeconds(-0.05) && h.To > caughtAt)
                                     .Select(h => (h.Holder, Pose: pull.Positions.At(h.Holder, caughtAt)))
                                     .Where(h => h.Pose is not null)
                                     .ToList();
                if (givers.Count == 0) continue;
                var (giver, pose) = givers.MinBy(h => Vector2.Distance(h.Pose!.Value.Position, at.Position));
                var from = pose!.Value;
                catches.Add(new Catch(pull.Name, colour, Vector2.Distance(from.Position, at.Position),
                                      Math.Max(at.Gap.TotalSeconds, from.Gap.TotalSeconds), $"{pull.Who(receiver)} from {pull.Who(giver)}",
                                      receiver == pull.Self));
            }
        }
    }

    private void ObserveTimeline(TopPull pull, ActCast helloWorld)
    {
        void Time(string key, DateTimeOffset at) => Add(times, key, TopPull.Seconds(helloWorld.Time, at));

        if (pull.FirstHit(h => h.Action == P3ActionId.HelloWorld) is { } hit) Time("debuffs", hit.Time);
        if (pull.FirstStatus(s => s.Gained && s.Status is P3StatusId.CriticalOverflowBug or P3StatusId.CriticalSynchronizationBug) is { } bug)
            Time("bugs", bug.Time);
        var starts = TopPull.Clusters(pull.Casts.Where(c => c.Action == P3ActionId.LatentDefect), c => c.Time, 1);
        foreach (var (start, set) in starts.Select((s, i) => (s, i + 1))) Time($"start {set}", start[0].Time);
        var tethers = TopPull.Clusters(pull.Statuses.Where(s => s.Gained && s.Status is P3StatusId.LocalRegression or P3StatusId.RemoteRegression),
                                       s => s.Time, 1);
        foreach (var (start, set) in tethers.Select((s, i) => (s, i + 1))) Time($"regressions {set}", start[0].Time);
        var soaks = TopPull.Clusters(pull.Hits.Where(h => IsTower(h.Action)), h => h.Time, 1);
        foreach (var (soak, set) in soaks.Select((s, i) => (s, i + 1)))
        {
            var at = soak.Min(h => h.Time);
            Time($"resolve {set}", at);
            foreach (var (kind, action) in new[] { ("stack", P3ActionId.Stack), ("defamation", P3ActionId.Defamation) })
                if (pull.FirstHit(h => h.Action == action && Math.Abs(TopPull.Seconds(at, h.Time)) < 2) is { } bugHit)
                    Add(resolveOffsets, kind, TopPull.Seconds(at, bugHit.Time));
        }
        if (pull.FirstCast(P3ActionId.CriticalError) is { } error) Time("critical error", error.Time);
        if (pull.FirstHit(h => h.Action == P3ActionId.CriticalError) is { } errorHit) Time("critical error hits", errorHit.Time);
    }

    private static void Add(Dictionary<string, List<double>> into, string key, double value)
    {
        if (!into.TryGetValue(key, out var list)) into[key] = list = [];
        list.Add(value);
    }

    public IEnumerable<LogFinding> Findings()
    {
        var radii = new Spread(towerRadii);
        yield return new LogFinding(Section, "Tower ring radius: each tower's distance from the centre, from its cast's location",
            SimSource.Line(Constants, "TowerRingRadius ="), $"{P3Geometry.TowerRingRadius} y",
            radii.Describe(" y"), LogFinding.Compare(radii, P3Geometry.TowerRingRadius, 0.1));

        var cardinals = towerSets.Count(s => s.Placement == "cardinals");
        var adjacent = towerSets.Count(s => s.SameColoursAdjacent);
        var repeats = towerSets.Count(s => s.SameAsLastSet);
        var followers = towerSets.Count(s => s.Set > 1);
        yield return new LogFinding(Section, "Tower layout",
            SimSource.Line(Scenario, "(-45f, state.DefamationColour)"),
            "always on the cardinals, each colour's two towers side by side either side of a random intercardinal",
            towerSets.Count == 0 ? "no tower sets"
                : $"{cardinals} of {towerSets.Count} sets on the cardinals, {towerSets.Count(s => s.Placement == "intercardinals")} on the "
                  + $"intercardinals, {towerSets.Count(s => s.Placement == "elsewhere")} elsewhere; each colour's towers side by side in "
                  + $"{adjacent}; {repeats} of {followers} later sets repeat the set before",
            towerSets.Count == 0 ? Verdict.NotInTheseLogs : cardinals == towerSets.Count && adjacent == towerSets.Count ? Verdict.Matches : Verdict.Differs)
        {
            Details = towerSets.Select(s => $"{s.Pull} set {s.Set}: {s.Layout}").ToList(),
        };

        var debuffDurations = new Spread(towerDebuffs.Select(d => d.Duration));
        yield return new LogFinding(Section, "Tower debuff's duration",
            SimSource.Line(Constants, "float TowerDebuff ="), $"{P3Duration.TowerDebuff} s",
            debuffDurations.Describe(" s"), LogFinding.Compare(debuffDurations, P3Duration.TowerDebuff, 0.05));
        var debuffDelays = new Spread(towerDebuffs.Select(d => d.Delay));
        yield return new LogFinding(Section, "Tower debuff lands, after the tower's hit on its soaker",
            SimSource.Line(Scenario, "member.AddStatus(colour.TowerDebuffStatusId"), "0 s: with the hit",
            debuffDelays.Describe(" s"), LogFinding.Compare(debuffDelays, 0, 0.15));

        var latentDurations = new Spread(latentDefects.Select(d => d.Duration));
        yield return new LogFinding(Section, "Latent Defect's duration",
            SimSource.Line(Constants, "float LatentDefect ="), $"{P3Duration.LatentDefect} s",
            latentDurations.Describe(" s"), LogFinding.Compare(latentDurations, P3Duration.LatentDefect, 0.05));
        var latentDelays = new Spread(latentDefects.Select(d => d.Delay));
        yield return new LogFinding(Section, "Latent Defect lands, after Hello World's hit on its holder",
            SimSource.Line(Scenario, "member.AddStatus(P3StatusId.LatentDefect"), "0 s: with the hit",
            latentDelays.Describe(" s"), LogFinding.Compare(latentDelays, 0, 0.15));

        foreach (var local in new[] { false, true })
            yield return BreakDistance(local);
        yield return ArmingDelay();

        var own = new Spread(catches.Where(c => c.BySelf && c.Gap <= CatchSampleGap).Select(c => c.Distance));
        var others = new Spread(catches.Where(c => !c.BySelf && c.Gap <= CatchSampleGap).Select(c => c.Distance));
        yield return new LogFinding(Section, "Rot contact radius: how far a player stood from the nearest holder of a rot as it caught it",
            SimSource.Line(Constants, "RotContactRadius ="), $"{P3Geometry.RotContactRadius} y",
            catches.Count == 0 ? "no rot changed hands"
                : $"caught by the logging player, whose own position runs ahead of the server's while it walks in: {own.Describe(" y")}"
                  + $"{Median(own)}; by the others, whose positions trail the server's: {others.Describe(" y")}{Median(others)}. Only "
                  + $"catches with both positions within {CatchSampleGap} s of a sample; the server checks contact on its own positions, "
                  + "which the two medians bracket",
            catches.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = catches.Select(c => $"{c.Pull}: {c.Who}, {c.Colour}, {Spread.F(c.Distance)} y{(c.BySelf ? " (logging player)" : "")}, "
                                          + $"nearest sample {Spread.F(c.Gap)} s").ToList(),
        };

        foreach (var kind in new[] { "stack", "defamation" })
        {
            var offsets = new Spread(resolveOffsets.GetValueOrDefault(kind, []));
            yield return new LogFinding(Section, $"Each set's {kind}s go off with its towers: their hit after the towers' hit",
                SimSource.Line(Scenario, "private void ResolveSet("), "0 s: one resolve fires the towers, stacks and defamations",
                offsets.Describe(" s"), LogFinding.Compare(offsets, 0, TimelineTolerance));
        }

        foreach (var finding in TimelineFindings())
            yield return finding;
    }

    private const double CatchSampleGap = 0.15;

    private static string Median(Spread spread) => spread.Count == 0 ? "" : $", median {Spread.F(spread.Median)} y";

    // A pair walking across the distance breaks at the server's next check, a little past it, so the closest break to the
    // distance bounds it: a local tether breaks at the farthest one or farther, a remote one at the closest or closer.
    // Tethers formed already past it broke at once wherever their pair stood and say nothing of where it lies.
    private LogFinding BreakDistance(bool local)
    {
        var walked = regressions.Where(r => r.Local == local && r.Distance is not null && r.BrokeAfter >= ImmediateBreak).ToList();
        var distances = new Spread(walked.Select(r => r.Distance!.Value));
        var simDistance = local ? P3Geometry.LocalRegressionBreakDistance : P3Geometry.RemoteRegressionBreakDistance;
        var bound = local ? distances.Max : distances.Min;
        var name = local ? "Local" : "Remote";
        var verdict = distances.Count == 0 ? Verdict.NotInTheseLogs
            : (local ? simDistance - bound : bound - simDistance) is >= 0 and <= BreakOvershoot ? Verdict.Matches
            : Verdict.Differs;
        return new LogFinding(Section, $"{name} regression's break distance: how far apart its pair stood, by the server, as it broke",
            SimSource.Line(Constants, $"{name}RegressionBreakDistance ="),
            $"breaks once the pair is {(local ? "closer than" : "farther apart than")} {simDistance} y",
            distances.Describe(" y") + (distances.Count == 0 ? "" : $" as the pair walked {(local ? "in" : "apart")}; so the game's distance "
                + $"lies between {Spread.F(local ? bound : bound - BreakOvershoot)} and {Spread.F(local ? bound + BreakOvershoot : bound)} y"),
            verdict)
        {
            Details = regressions.Where(r => r.Local == local)
                                 .Select(r => $"{r.Pull} set {r.Set}, {r.Pair}: "
                                              + (r.BrokeAfter is { } after
                                                     ? $"broke {Spread.F(after)} s after forming at {(r.Distance is { } d ? Spread.F(d) : "?")} y"
                                                     : "not broken")
                                              + $", {(r.DistanceAtStart is { } s ? Spread.F(s) : "?")} y apart as it formed (client)")
                                 .ToList(),
        };
    }

    // A tether whose pair already stood well past the break distance as it formed shows how soon the game lets it break.
    private LogFinding ArmingDelay()
    {
        var remoteBreaksBy = regressions.Where(r => !r.Local && r.Distance is not null).Select(r => r.Distance!.Value)
                                        .DefaultIfEmpty(double.NaN).Min();
        var localBreaksBy = regressions.Where(r => r.Local && r.Distance is not null).Select(r => r.Distance!.Value)
                                       .DefaultIfEmpty(double.NaN).Max();
        var formedPast = regressions.Where(r => r.DistanceAtStart is { } d
                                                && (r.Local ? d < localBreaksBy - ArmingMargin : d > remoteBreaksBy + ArmingMargin))
                                    .ToList();
        var broken = formedPast.Where(r => r.BrokeAfter is not null).ToList();
        var delays = new Spread(broken.Select(r => r.BrokeAfter!.Value));
        var verdict = broken.Count == 0 ? Verdict.NotInTheseLogs
            : broken.All(r => r.BrokeAfter < ImmediateBreak) ? Verdict.Matches
            : Verdict.Differs;
        return new LogFinding(Section, "Regression's arming delay: how soon a tether formed already past its break distance broke",
            SimSource.Line(Scenario, "tether.StretchLt(P3Geometry.LocalRegressionBreakDistance)"),
            "none: a tether breaks the first frame its pair stands past the distance",
            formedPast.Count == 0
                ? $"no tether formed more than {ArmingMargin} y past the distances above"
                : $"{formedPast.Count} tethers formed more than {ArmingMargin} y past the distances above (client positions); "
                  + $"they broke {delays.Describe(" s")} after forming",
            verdict)
        {
            Details = formedPast.Select(r => $"{r.Pull} set {r.Set} {(r.Local ? "local" : "remote")}, {r.Pair}: formed "
                                             + $"{Spread.F(r.DistanceAtStart!.Value)} y apart, "
                                             + (r.BrokeAfter is { } s ? $"broke {Spread.F(s)} s later" : "never broke")).ToList(),
        };
    }

    private const double ArmingMargin = 2;
    private const double ImmediateBreak = 0.5;

    // How far past the distance the closest of a score of walked-out breaks can lie: the spread of the breaks puts the
    // server's checks a few tenths of a yalm of walking apart.
    private const double BreakOvershoot = 0.5;

    private IEnumerable<LogFinding> TimelineFindings()
    {
        var (castAt, castSeconds) = SimSource.EventTime(Scenario, "Actions.HelloWorld)");
        foreach (var (key, name, line, occurrence) in Timeline)
        {
            var (where, seconds) = SimSource.EventTime(Scenario, line, occurrence);
            var measured = new Spread(times.GetValueOrDefault(key, []));
            var sim = seconds - castSeconds;
            yield return new LogFinding(Section, $"Timeline: {name}, from Hello World's cast ({castAt})", where,
                sim is { } s ? $"{Spread.F(s)} s" : "not found in the scenario",
                measured.Describe(" s"), sim is { } simSeconds ? LogFinding.Compare(measured, simSeconds, TimelineTolerance) : Verdict.Inconclusive);
        }
    }

    // The sim's event times are tenths of a second; a log's are good to about a frame.
    private const double TimelineTolerance = 0.25;

    private static bool IsTower(uint action) => action is P3ActionId.RedTower or P3ActionId.BlueTower;

    // How far a bearing lies from the nearest of offset, offset + 90, offset + 180 and offset + 270.
    private static double OffGrid(double bearing, double offset)
    {
        var d = ((bearing - offset) % 90 + 90) % 90;
        return Math.Min(d, 90 - d);
    }

    private static double Degrees(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static string Compass(double bearing) => new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(bearing / 45) % 8];
}
