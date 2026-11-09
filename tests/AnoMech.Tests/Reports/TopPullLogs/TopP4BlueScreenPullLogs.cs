using System.Numerics;
using AnoMech.Scenarios.Top;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Scenarios.Top.TopP4Constants;

namespace AnoMech.Tests;

// P4 Blue Screen, timed from Omega's Wave Cannon cast.
internal sealed class TopP4BlueScreenPullLogs : ITopPullLogMeasures
{
    private const string Section = "P4 Blue Screen";
    private const string Constants = "AnoMech/Scenarios/Top/TopP4Constants.cs";
    private const string Actions = "AnoMech/Scenarios/Top/TopP4Actions.cs";
    private const string Scenario = "AnoMech/Scenarios/Top/P4BlueScreen/TopP4BlueScreenScenario.cs";
    private const string State = "AnoMech/Scenarios/Top/P4BlueScreen/TopP4BlueScreenState.cs";
    private const int Rounds = 3;

    // A beam whose heading lies within this of an aimed line went off along it.
    private const double OnLineDegrees = 0.5;

    private readonly Dictionary<string, List<double>> times = [];
    private readonly List<double> resolveAfterAim = [], lineStartOffCentre = [], omegaHeadings = [];
    private readonly List<Beam> beamResults = [];
    private readonly List<LineStack> lineStacks = [];
    private readonly LethalHits repeatHits = new(), ringHits = new();

    // Per player and round: how many beams hit them and whether one of them killed them.
    private readonly List<(int Beams, bool Died)> beamTakers = [];
    private readonly List<string> beamDetails = [], beamTakerDetails = [], lineStackDetails = [], markedRounds = [];
    private int consecutiveMarks, markPairs, beamsHittingMore, beamTakersShielded, lineStackTargetsShielded;

    // In degrees: off the nearest line its round aimed, and off its target's bearing as it hit and as the round aimed.
    private sealed record Beam(double OffLines, double OffTargetHit, double OffTargetAimed);

    // One line stack: how far it went off the line to its marked player, how many it hit, whether they were all tanks, and how
    // many of those not under an invulnerability it killed and how many lived.
    private sealed record LineStack(double OffTarget, int Hit, bool TanksOnly, int Killed, int Survived);

    // The timeline's events: the key the log's times go under, and the text and occurrence of the scenario's
    // world.Events.Add line for it.
    private static readonly (string Key, string Event, string Line, int Occurrence)[] Timeline =
    [
        ("markers 1", "Round 1's line-stack markers", "MarkStackTargets(0)", 1),
        ("aim 1", "Round 1's aim (the repeats' cast)", "AimEveryonesLine);", 1),
        ("beams 1", "Round 1's beams", "FireBeams);", 1),
        ("ring cast 1", "The first Wave Repeater's cast", "ringHelper?.Cast(Actions.WaveRepeater, ", 1),
        ("visual 32534 1", "Omega's first 32534", "omega?.Cast(P4ActionId.WaveCannonVisual)", 1),
        ("line stacks 1", "Round 1's line stacks", "FireLineStacks(0)", 1),
        ("markers 2", "Round 2's markers", "MarkStackTargets(1)", 1),
        ("aim 2", "Round 2's aim", "AimEveryonesLine);", 2),
        ("ring 2 1", "The first Wave Repeater's second ring", "ringHelper?.Cast(Actions.WaveRepeaterRing2)", 1),
        ("visual 32534 2", "Omega's second 32534", "omega?.Cast(P4ActionId.WaveCannonVisual)", 2),
        ("beams 2", "Round 2's beams", "FireBeams);", 2),
        ("ring 3 1", "The first Wave Repeater's third ring", "ringHelper?.Cast(Actions.WaveRepeaterRing3)", 1),
        ("ring 4 1", "The first Wave Repeater's fourth ring", "ringHelper?.Cast(Actions.WaveRepeaterRing4)", 1),
        ("visual 31618 1", "Omega's 31618", "omega?.Cast(P4ActionId.WaveCannonVisual2)", 1),
        ("line stacks 2", "Round 2's line stacks", "FireLineStacks(1)", 1),
        ("markers 3", "Round 3's markers", "MarkStackTargets(2)", 1),
        ("aim 3", "Round 3's aim", "AimEveryonesLine);", 3),
        ("ring cast 2", "The second Wave Repeater's cast", "ringHelper?.Cast(Actions.WaveRepeater, ", 2),
        ("visual 31619 1", "Omega's 31619", "omega?.Cast(P4ActionId.WaveCannonVisual3)", 1),
        ("beams 3", "Round 3's beams", "FireBeams);", 3),
        ("visual 31620 1", "Omega's 31620", "omega?.Cast(P4ActionId.WaveCannonVisual4)", 1),
        ("line stacks 3", "Round 3's line stacks", "FireLineStacks(2)", 1),
        ("ring 2 2", "The second Wave Repeater's second ring", "ringHelper?.Cast(Actions.WaveRepeaterRing2)", 2),
        ("ring 3 2", "The second Wave Repeater's third ring", "ringHelper?.Cast(Actions.WaveRepeaterRing3)", 2),
        ("ring 4 2", "The second Wave Repeater's fourth ring", "ringHelper?.Cast(Actions.WaveRepeaterRing4)", 2),
    ];

    public void Observe(TopPull pull)
    {
        if (pull.FirstCast(P4ActionId.WaveCannonCast) is not { } cast) return;
        void Time(string key, DateTimeOffset at)
        {
            if (!times.TryGetValue(key, out var list)) times[key] = list = [];
            list.Add(TopPull.Seconds(cast.Time, at));
        }
        bool InPhase(DateTimeOffset at) => TopPull.Seconds(cast.Time, at) is >= 0 and < 45;

        if (pull.CastAt(cast) is { } aimedAt) omegaHeadings.Add(aimedAt.Heading);
        omegaHeadings.AddRange(pull.Hits.Where(h => h.Action is P4ActionId.WaveCannonVisual or P4ActionId.WaveCannonVisual2
                                                        or P4ActionId.WaveCannonVisual3 or P4ActionId.WaveCannonVisual4
                                                    && InPhase(h.Time) && h.Rotation is not null)
                                        .Select(h => (double)h.Rotation!.Value));

        List<List<ActHit>> HitGroups(uint action) => TopPull.Clusters(pull.Hits.Where(h => h.Action == action && InPhase(h.Time)), h => h.Time, 1);
        var markers = TopPull.Clusters(pull.Hits.Where(h => h.Action == P4ActionId.StackMarker && h.HitSomeone && InPhase(h.Time)), h => h.Time, 1);
        var aims = TopPull.Clusters(pull.Casts.Where(c => c.Action == P4ActionId.WaveCannonRepeat && InPhase(c.Time)), c => c.Time, 1);
        var beams = HitGroups(P4ActionId.WaveCannonBeam);
        var resolves = HitGroups(P4ActionId.WaveCannonRepeat);
        var stacks = HitGroups(P4ActionId.LineStack);
        for (var round = 0; round < Rounds; round++)
        {
            if (round < markers.Count) Time($"markers {round + 1}", markers[round][0].Time);
            if (round < aims.Count) Time($"aim {round + 1}", aims[round][0].Time);
            if (round < beams.Count) Time($"beams {round + 1}", beams[round][0].Time);
            if (round < stacks.Count) Time($"line stacks {round + 1}", stacks[round][0].Time);
            if (round < aims.Count && round < resolves.Count)
                resolveAfterAim.Add(TopPull.Seconds(aims[round][0].Time, resolves[round][0].Time));
            if (round < aims.Count && round < beams.Count) ObserveBeams(pull, round + 1, aims[round], beams[round]);
            if (round < markers.Count && round < stacks.Count) ObserveLineStacks(pull, round + 1, markers[round], stacks[round]);
        }

        var marked = markers.Take(Rounds).Select(m => m.Select(h => h.Target).ToHashSet()).ToList();
        markedRounds.Add($"{pull.Name}: " + string.Join(" / ", marked.Select(m => string.Join(" + ", m.Select(pull.Who)))));
        for (var i = 1; i < marked.Count; i++)
        {
            markPairs++;
            if (marked[i].Overlaps(marked[i - 1])) consecutiveMarks++;
        }

        foreach (var (key, action, occurrences) in new[]
                 {
                     ("visual 32534", P4ActionId.WaveCannonVisual, 2), ("visual 31618", P4ActionId.WaveCannonVisual2, 1),
                     ("visual 31619", P4ActionId.WaveCannonVisual3, 1), ("visual 31620", P4ActionId.WaveCannonVisual4, 1),
                 })
            foreach (var (use, n) in HitGroups(action).Take(occurrences).Select((u, i) => (u, i + 1)))
                Time($"{key} {n}", use[0].Time);
        var rings = TopPull.Clusters(pull.Casts.Where(c => c.Action == P3ActionId.WaveRepeater && InPhase(c.Time)), c => c.Time, 1);
        foreach (var (ring, n) in rings.Take(2).Select((r, i) => (r, i + 1)))
        {
            Time($"ring cast {n}", ring[0].Time);
            foreach (var (action, step) in new[] { (P3ActionId.WaveRepeaterRing2, 2), (P3ActionId.WaveRepeaterRing3, 3), (P3ActionId.WaveRepeaterRing4, 4) })
                if (pull.FirstHit(h => h.Action == action && TopPull.Seconds(ring[0].Time, h.Time) is > 0 and < 15) is { } hit)
                    Time($"ring {step} {n}", hit.Time);
        }

        lineStartOffCentre.AddRange(aims.SelectMany(a => a).Select(pull.CastAt).OfType<ActCastAt>()
                                        .Select(at => (double)Vector2.Distance(at.Position, TopPull.Centre)));

        string At(ActHit hit) => $"at {Spread.F(TopPull.Seconds(cast.Time, hit.Time))} s";
        repeatHits.Observe(pull, pull.Hits.Where(h => h.Action == P4ActionId.WaveCannonRepeat && InPhase(h.Time)), h => $"a repeat {At(h)}");
        ringHits.Observe(pull, pull.Hits.Where(h => TopPull.IsWaveRepeaterRing(h.Action) && InPhase(h.Time)),
                         h => $"{TopPull.WaveRepeaterRing(h.Action)} {At(h)}");
    }

    // The round's lines are the repeats' casts' headings. A beam that left them says where the game aims it: compare its
    // heading with its target's bearing as the round aimed and as the beam hit (the target is whichever player it points
    // closest to as hit).
    private void ObserveBeams(TopPull pull, int round, List<ActCast> aim, List<ActHit> beams)
    {
        var lines = aim.Select(pull.CastAt).OfType<ActCastAt>().Select(at => (double)at.Heading).ToList();
        foreach (var shot in beams.GroupBy(b => b.Source).Select(g => g.ToList()))
        {
            if (shot[0].Rotation is not { } rotation || lines.Count == 0) continue;
            double Off(Vector2 at) => Degrees(TopPull.AngleBetween(TopPull.HeadingTo(TopPull.Centre, at), rotation));
            var hit = shot.Where(b => b.HitSomeone && b.TargetPosition is not null).ToList();
            if (hit.Count == 0) continue;
            var target = hit.MinBy(h => Off(h.TargetPosition!.Value));
            var beam = new Beam(Degrees(lines.Min(l => TopPull.AngleBetween(l, rotation))), Off(target.TargetPosition!.Value),
                                pull.Positions.At(target.Target, aim[0].Time) is { } aimed ? Off(aimed.Position) : double.NaN);
            beamResults.Add(beam);
            if (hit.Count > 1) beamsHittingMore++;
            beamDetails.Add($"{pull.Name} round {round}, beam at {pull.Who(target.Target)}{(hit.Count > 1 ? $" (hit {hit.Count})" : "")}: "
                            + $"{Spread.F(beam.OffLines)} degrees off the nearest aimed line, off its target {Spread.F(beam.OffTargetAimed)} "
                            + $"as aimed and {Spread.F(beam.OffTargetHit)} as hit");
        }

        foreach (var taken in beams.Where(b => b.HitSomeone).GroupBy(b => b.Target).Select(g => g.ToList()))
        {
            var outcomes = taken.Select(pull.Outcome).ToList();
            if (outcomes.All(o => o == HitOutcome.AlreadyDead)) continue;
            var beamCount = taken.Select(b => b.Source).Distinct().Count();
            var shielded = outcomes.Contains(HitOutcome.Invulnerable);
            var died = outcomes.Contains(HitOutcome.Killed);
            if (shielded) beamTakersShielded++;
            else beamTakers.Add((beamCount, died));
            if (shielded || beamCount >= 2 || died)
                beamTakerDetails.Add($"{pull.Name} round {round}: {beamCount} {(beamCount == 1 ? "beam" : "beams")} on {pull.Who(taken[0].Target)}: "
                                     + string.Join(", then ", taken.Select(pull.Describe)));
        }
    }

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    // Each line stack goes off toward one marked player, from the centre. A dead marker's line still goes off, at someone
    // else, so only lines with a living marker show where they aim.
    private void ObserveLineStacks(TopPull pull, int round, List<ActHit> markers, List<ActHit> stacks)
    {
        foreach (var line in stacks.GroupBy(h => h.Source))
        {
            var hit = line.Where(h => h.HitSomeone).ToList();
            var firedAt = line.First().Time;
            var marked = markers.Where(m => pull.IsAlive(m.Target, firedAt))
                                .Select(m => (m.Target, Pose: pull.Positions.At(m.Target, firedAt))).Where(m => m.Pose is not null).ToList();
            var (toward, off) = line.First().Rotation is { } heading && marked.Count > 0
                ? marked.Select(m => (m.Target, Off: Degrees(TopPull.AngleBetween(TopPull.HeadingTo(TopPull.Centre, m.Pose!.Value.Position), heading))))
                        .MinBy(m => m.Off)
                : (0u, double.NaN);
            var outcomes = hit.Select(pull.Outcome).ToList();
            lineStacks.Add(new LineStack(off, hit.Count, hit.Count > 0 && hit.All(h => pull.IsTank(h.Target)),
                                         outcomes.Count(o => o == HitOutcome.Killed), outcomes.Count(o => o == HitOutcome.Survived)));
            lineStackTargetsShielded += outcomes.Count(o => o == HitOutcome.Invulnerable);
            string Took(ActHit h) => pull.Outcome(h) == HitOutcome.Survived ? pull.Who(h.Target) : $"{pull.Who(h.Target)} ({pull.Describe(h)})";
            var took = hit.Count == 0 ? "nobody"
                : string.Join(" + ", hit.Select(Took)) + (outcomes.All(o => o == HitOutcome.Survived) ? hit.Count == 1 ? ", who survived" : ", all survived" : "");
            var aim = !double.IsNaN(off) ? $"the line toward {pull.Who(toward)} ({Spread.F(off)} degrees off it)"
                : marked.Count == 0 ? "with no marked player alive, a line"
                : "a line with no rotation logged";
            lineStackDetails.Add($"{pull.Name} round {round}: {aim} hit {took}");
        }
    }

    public IEnumerable<LogFinding> Findings()
    {
        var (castAt, castSeconds) = SimSource.EventTime(Scenario, "omega?.Cast(P4ActionId.WaveCannonCast)");
        foreach (var (key, name, line, occurrence) in Timeline)
        {
            var (where, seconds) = SimSource.EventTime(Scenario, line, occurrence);
            var measured = new Spread(times.GetValueOrDefault(key, []));
            var sim = seconds - castSeconds;
            yield return new LogFinding(Section, $"Timeline: {name}, from Omega's Wave Cannon cast ({castAt})", where,
                sim is { } s ? $"{Spread.F(s)} s" : "not found in the scenario", measured.Describe(" s"),
                sim is { } simSeconds ? LogFinding.Compare(measured, simSeconds, 0.25) : Verdict.Inconclusive);
        }

        var repeatCast = new DataminingGameData(new FakeRsvFunctions()).Action(P4ActionId.WaveCannonRepeat)?.CastSeconds;
        var resolve = new Spread(resolveAfterAim);
        yield return new LogFinding(Section, "Each round's repeats resolve, after its aim",
            SimSource.Line(Scenario, "UNVERIFIED: no log times the later aims and beams"),
            repeatCast is { } repeatSeconds ? $"{Spread.F(repeatSeconds)} s, the repeat's cast" : "the repeat's cast",
            resolve.Describe(" s"), repeatCast is { } cast ? LogFinding.Compare(resolve, cast, 0.15) : Verdict.Inconclusive);

        yield return new LogFinding(Section, "The repeats' orange lines flash late in the cast",
            SimSource.Line(Constants, "float RepeatOmenDelay ="), $"shown {P4Duration.RepeatOmenDelay} s into the cast",
            "what the client draws, and when, is not logged", Verdict.NotInTheseLogs);

        var offLines = new Spread(beamResults.Select(b => b.OffLines));
        var strays = beamResults.Where(b => b.OffLines > OnLineDegrees).ToList();
        var followed = strays.Count(b => b.OffTargetHit < b.OffTargetAimed);
        yield return new LogFinding(Section, "The beams fire down the lines fixed at the aim, not at the players where they now stand",
            SimSource.Line(Scenario, "UNVERIFIED: the beams fire down the lines fixed at the aim"),
            "each beam goes off along its player's line from the aim",
            beamResults.Count == 0 ? "no beams"
                : $"{beamResults.Count - strays.Count} of {beamResults.Count} beams went off within {OnLineDegrees} degrees of one of their "
                  + $"round's aimed lines; {strays.Count} left every one of them, by up to {Spread.F(offLines.Max)} degrees, and {followed} "
                  + "of those pointed closer to their target where it stood as they hit than where it stood at the aim (client positions). "
                  + $"{beamsHittingMore} beams hit more than one player",
            beamResults.Count == 0 ? Verdict.NotInTheseLogs : strays.Count == 0 ? Verdict.Matches : Verdict.Differs)
        {
            Details = beamDetails,
        };

        var doubled = beamTakers.Where(t => t.Beams >= 2).ToList();
        var single = beamTakers.Where(t => t.Beams == 1).ToList();
        yield return new LogFinding(Section, "Two beams kill, one does not",
            SimSource.Line(Scenario, "private const string TwoBeams"), "a player in two beams' lines dies; one beam is survived",
            beamTakers.Count + beamTakersShielded == 0 ? "no beams"
                : $"{doubled.Count} times a player took two or more beams and {doubled.Count(t => t.Died)} of those died of them; "
                  + $"{single.Count(t => t.Died)} of the {single.Count} who took one died of it"
                  + (beamTakersShielded == 0 ? "" : $"; {beamTakersShielded} under an invulnerability left out"),
            doubled.Count == 0 ? beamTakers.Count + beamTakersShielded == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive
            : doubled.All(t => t.Died) && !single.Any(t => t.Died) ? Verdict.Matches
            : Verdict.Differs)
        {
            Details = beamTakerDetails,
        };

        var unknownDamage = SimSource.Line(Actions, "UNVERIFIED: no damage values are known, so a repeat line");
        yield return repeatHits.Finding(Section, "A repeat line kills whoever it hits", unknownDamage,
            "every hit kills, the game's damage being unknown");
        yield return ringHits.Finding(Section, "Wave Repeater's middle and rings kill whoever they hit", unknownDamage,
            "every hit kills, the game's damage being unknown");

        var offCentre = new Spread(lineStartOffCentre);
        yield return new LogFinding(Section, "The lines start at the centre: each repeat cast's location",
            SimSource.Line(Scenario, "UNVERIFIED: the lines start at the centre"), "the centre", offCentre.Describe(" y from the centre"),
            offCentre.Count == 0 ? Verdict.NotInTheseLogs : offCentre.Max < 0.1 ? Verdict.Matches : Verdict.Differs);

        var stackOff = new Spread(lineStacks.Where(l => !double.IsNaN(l.OffTarget)).Select(l => l.OffTarget));
        var counts = lineStacks.GroupBy(l => l.Hit).OrderBy(g => g.Key).Select(g => $"{g.Count()} lines hit {g.Key}");
        yield return new LogFinding(Section, "Each line stack runs from the centre toward its marked player where that player stands as it fires",
            SimSource.Line(Scenario, "Each line runs from the centre toward its target where it stands now"),
            "toward the marked player's position as it fires",
            stackOff.Count == 0 ? "no line stacks"
                : $"off the line to the nearest living marked player: {stackOff.Describe(" degrees")} (client positions)",
            stackOff.Count == 0 ? Verdict.NotInTheseLogs : stackOff.Max < 5 ? Verdict.Matches : Verdict.Differs)
        {
            Details = lineStackDetails,
        };

        yield return LineStackMinimum(counts);

        yield return new LogFinding(Section, "Each set's markers are rolled on their own",
            SimSource.Line(State, "UNVERIFIED: each set is"), "a player can be marked in consecutive sets, any pair can be picked",
            markPairs == 0 ? "no markers" : $"a player was marked again in the next set {consecutiveMarks} of {markPairs} times",
            markPairs == 0 ? Verdict.NotInTheseLogs : consecutiveMarks > 0 ? Verdict.Matches : Verdict.Inconclusive)
        {
            Details = markedRounds,
        };

        var headings = new Spread(omegaHeadings.Select(h => TopPull.AngleBetween(h, Math.PI) * 180 / Math.PI));
        yield return new LogFinding(Section, "Omega faces north the whole phase: his heading off north at his casts",
            SimSource.Line(Scenario, "UNVERIFIED: Omega faces north for the whole phase"), "0 degrees", headings.Describe(" degrees"),
            headings.Count == 0 ? Verdict.NotInTheseLogs : headings.Max < 1 ? Verdict.Matches : Verdict.Differs);

        yield return new LogFinding(Section, "Whether the marker, rings, beams and line stacks show from an invisible helper",
            $"{SimSource.Line(Scenario, "UNVERIFIED: whether the instant rings, beams and line stacks show")}, "
            + SimSource.Line(Scenario, "UNVERIFIED: whether the marker shows"),
            "the sim casts them from invisible helpers", "what the client draws is not logged", Verdict.NotInTheseLogs);
        yield return new LogFinding(Section, "P4's weather and music", SimSource.Line(Constants, "byte Weather ="),
            $"P3's: weather {TopP4Constants.Weather}, BGM {TopP4Constants.Bgm}", "ACT logs neither", Verdict.NotInTheseLogs);
    }

    // A line with a non-tank in it that killed someone puts the game's minimum above its size, and one all its targets lived
    // through puts it at or below; the lines of fewer than the minimum holding only tanks show the exception.
    private LogFinding LineStackMinimum(IEnumerable<string> counts)
    {
        var minimum = P4Rules.LineStackMinimum;
        var evidence = lineStacks.Where(l => l.Killed + l.Survived > 0).ToList();
        var killers = evidence.Where(l => !l.TanksOnly && l.Killed > 0).Select(l => l.Hit).ToList();
        var lived = evidence.Where(l => !l.TanksOnly && l.Killed == 0).Select(l => l.Hit).ToList();
        var tankLines = evidence.Where(l => l.TanksOnly && l.Hit < minimum).ToList();
        var lower = killers.Count == 0 ? 1 : killers.Max() + 1;
        int? upper = lived.Count == 0 ? null : lived.Min();
        var contradicted = minimum < lower || minimum > upper || tankLines.Any(l => l.Killed > 0);
        var verdict = evidence.Count == 0 ? lineStacks.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive
            : contradicted ? Verdict.Differs
            : lower == minimum && upper == minimum && tankLines.Count > 0 ? Verdict.Matches
            : Verdict.Inconclusive;
        return new LogFinding(Section, "A line stack needs four to share it, unless only tanks are in it",
            SimSource.Line(Constants, "int LineStackMinimum ="), $"fewer than {minimum} die, unless all are tanks",
            lineStacks.Count == 0 ? "no line stacks"
                : $"{string.Join(", ", counts)}. Leaving out targets under an invulnerability, "
                  + (killers.Count == 0 ? "no line with a non-tank in it killed" : $"the largest line with a non-tank in it that killed hit {killers.Max()}")
                  + (lived.Count == 0 ? ", none was lived through" : $", the smallest all lived through hit {upper}")
                  + $", so the game's minimum lies {(upper is { } most ? $"from {lower} to {most}" : $"at {lower} or more")}; "
                  + (tankLines.Count == 0 ? $"no line of fewer than {minimum} held only tanks"
                      : $"{tankLines.Count(l => l.Killed == 0)} of {tankLines.Count} lines of fewer than {minimum} holding only tanks were lived through")
                  + (lineStackTargetsShielded == 0 ? ""
                      : $"; {lineStackTargetsShielded} {(lineStackTargetsShielded == 1 ? "target" : "targets")} under an invulnerability left out")
                  + ". Each line is listed under the finding above",
            verdict);
    }
}
