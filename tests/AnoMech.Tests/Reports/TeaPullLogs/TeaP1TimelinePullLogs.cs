using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The Living Liquid's timeline: what the opening does, from the combat flag and from Cascade's cast, and each part's
// literals from the cast its part is timed by.
internal sealed class TeaP1TimelinePullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 timeline";
    private const string Flag = "flag", Cascade1 = "Cascade cast 1", Wave1 = "Protean Wave cast 1", Cascade2 = "Cascade cast 2";
    private const double Tolerance = 0.25;

    // What the opening does, with the second the community timeline gives it on its own clock.
    private static readonly (string Key, string Name, double? Community)[] Opening =
    [
        ("Fluid Swing 1", "the first Fluid Swing", 11.4),
        ("Cascade cast 1", "Cascade's cast starts", 15.5),
        ("Cascade lands 1", "Cascade lands", 19.5),
        ("Hand appears 1", "the Liquid Hand appears", 19.5),
        ("tornadoes appear 1", "the Liquid Rages appear", 19.5),
        ("glow 1", "the Hand glows, its model state set", 30.6),
        ("tornado wave cast 1", "the tornadoes' telegraphed Protean Wave, cast", 33.7),
        ("dolls appear 1", "the Jagd Dolls appear", null),
        ("dolls targetable 1", "the Jagd Dolls turn targetable", 34.7),
        ("slam 1", "the Hand's slam", 35.8),
        ("tornado wave lands 1", "the tornadoes' telegraphed wave lands", 36.7),
        ("Fluid Swing 2", "the second Fluid Swing", 37.7),
        ("Fluid Strike 1", "the first Fluid Strike", 37.7),
        ("tornado unseen 1", "the tornadoes' unseen Protean Wave", 38.7),
        ("Exhaust 1", "the dolls' first Exhaust", 39.7),
        ("Hand of Pain cast 1", "Hand of Pain 1, cast", 39.8),
        ("Pressurize 1", "Pressurize", 41.6),
        ("Emboli appear 1", "the Emboli appear", 42.2),
        ("Hand of Pain lands 1", "Hand of Pain 1 lands", 42.8),
        ("Exhaust 2", "the dolls' second Exhaust", 50.3),
        ("Fluid Swing 3", "the third Fluid Swing", 56.7),
        ("Fluid Strike 2", "the second Fluid Strike", 56.7),
    ];

    private readonly Dictionary<(string Anchor, string Key), List<(string Pull, double Seconds)>> offsets = [];
    private readonly List<double> splashSpacing = [], waveBars = [], waveLands = [];
    private readonly List<int> splashCounts = [];

    public void Observe(TeaPull pull)
    {
        var events = Events(pull);
        // What the bosses do over a dead party is no part of the fight's timeline: a Hand of Pain comes early then.
        var wiped = pull.Players.Any() && pull.Players.All(p => pull.Deaths.Any(d => d.Player == p && d.Raised is null))
            ? pull.Players.Max(p => pull.Deaths.Last(d => d.Player == p).Died) : (DateTimeOffset?)null;
        foreach (var (key, at) in events)
        {
            if (at > wiped) continue;
            if (pull.LoggedFromItsStart) Add(pull, Flag, key, pull.Clock(at));
            foreach (var anchor in new[] { Cascade1, Wave1, Cascade2 })
                if (events.TryGetValue(anchor, out var from))
                    Add(pull, anchor, key, PullLog.Seconds(from, at));
        }
        foreach (var run in PullLog.Clusters(pull.Volleys(P1ActionId.Splash).Select(v => v[0].Time), t => t, 10))
        {
            splashCounts.Add(run.Count);
            splashSpacing.AddRange(run.Zip(run.Skip(1), PullLog.Seconds));
        }
        foreach (var cast in pull.CastsOf(P1ActionId.ProteanWaveFromTheLiquid))
        {
            waveBars.Add(cast.Seconds);
            if (pull.FirstHit(h => h.Action == cast.Action && h.Source == cast.Source && PullLog.Seconds(cast.Time, h.Time) is > 0 and < 5) is { } lands)
                waveLands.Add(PullLog.Seconds(cast.Time, lands.Time));
        }
    }

    private void Add(TeaPull pull, string anchor, string key, double seconds)
    {
        if (!offsets.TryGetValue((anchor, key), out var list)) offsets[(anchor, key)] = list = [];
        list.Add((pull.Name, seconds));
    }

    private Spread From(string anchor, string key) => new((offsets.GetValueOrDefault((anchor, key)) ?? []).Select(o => o.Seconds));

    // Each pull's own value, on one line.
    private List<string> PerPull(string anchor, string key)
        => offsets.GetValueOrDefault((anchor, key)) is { Count: > 0 } list ? [string.Join("; ", list.Select(o => $"{o.Pull}: {Spread.F(o.Seconds)}"))] : [];

    // Every event of the fight the pull shows, each kind numbered in the order it came.
    internal static Dictionary<string, DateTimeOffset> Events(TeaPull pull)
    {
        var events = new Dictionary<string, DateTimeOffset>();
        void Number(string key, IEnumerable<DateTimeOffset> times)
        {
            var n = 1;
            foreach (var time in times.Order()) events[$"{key} {n++}"] = time;
        }
        IEnumerable<DateTimeOffset> Hits(uint action) => pull.Volleys(action).Select(v => v[0].Time);
        IEnumerable<DateTimeOffset> Starts(uint action) => PullLog.Clusters(pull.CastsOf(action), c => c.Time, 0.5).Select(c => c[0].Time);
        IEnumerable<DateTimeOffset> Appear(uint bnpcBase)
            => PullLog.Clusters(pull.Combatants.Where(c => c.Added && c.BNpcBase == bnpcBase), c => c.Time, 1).Select(c => c[0].Time);

        Number("Fluid Swing", Hits(P1ActionId.FluidSwing));
        Number("Fluid Strike", Hits(P1ActionId.FluidStrike));
        Number("Cascade cast", Starts(P1ActionId.Cascade));
        Number("Cascade lands", Hits(P1ActionId.Cascade));
        Number("Hand of Pain cast", Starts(P1ActionId.HandOfPain));
        Number("Hand of Pain lands", Hits(P1ActionId.HandOfPain));
        Number("Hand appears", Appear(P1BNpcBaseId.LiquidHand).Take(1));
        Number("tornadoes appear", pull.TornadoSets.Select(s => s.Appeared));
        Number("glow", pull.ModelStates.Where(m => m.Actor == pull.Hand && m.State != 0).Select(m => m.Time));
        Number("slam", pull.Slams.Select(h => h.Time));
        Number("tornado wave cast", Starts(P1ActionId.TornadoWave));
        Number("tornado wave lands", Hits(P1ActionId.TornadoWave));
        Number("tornado unseen", Hits(P1ActionId.TornadoWaveUnseen));
        Number("dolls appear", Appear(TeaLogIds.JagdDoll).Take(1));
        Number("dolls targetable", PullLog.Clusters(pull.Targetables.Where(t => t.Targetable && pull.BaseOf(t.Actor) == TeaLogIds.JagdDoll), t => t.Time, 1)
                                          .Select(c => c[0].Time).Take(1));
        Number("Exhaust", Hits(TeaLogIds.Exhaust));
        Number("Pressurize", Hits(P1ActionId.Pressurize));
        Number("Emboli appear", Appear(P1BNpcBaseId.Embolus));
        Number("Protean Wave cast", Starts(P1ActionId.ProteanWaveFromTheLiquid));
        Number("Protean Wave lands", Hits(P1ActionId.ProteanWaveFromTheLiquid));
        var sequence = 1;
        foreach (var cast in Starts(P1ActionId.ProteanWaveFromTheLiquid).Order())
        {
            var set = 1;
            foreach (var unseen in Hits(P1ActionId.ProteanWaveStraightAhead).Where(t => PullLog.Seconds(cast, t) is > 0 and < 12).Order())
                events[$"set {sequence}.{set++}"] = unseen;
            sequence++;
        }
        Number("Sluice cast", Starts(P1ActionId.Sluice));
        Number("Sluice lands", Hits(P1ActionId.Sluice));
        Number("Splash", Hits(P1ActionId.Splash));
        Number("tethers", PullLog.Clusters(pull.Tethers.Where(t => t.Id == P1TetherId.Drainage), t => t.Time, 1).Select(c => c[0].Time).Take(1));
        Number("Drainage", pull.Hits.Where(h => h.Action == P1ActionId.Drainage).Select(h => h.Time).Distinct());
        Number("Liquid Gaol", Hits(P1ActionId.LiquidGaol));
        Number("Throttle", PullLog.Clusters(pull.Statuses.Where(s => s.Gained && s.Status == P1StatusId.Throttle), s => s.Time, 1).Select(c => c[0].Time));
        if (pull.Liquid is { } liquid && pull.HitPoints.Where(h => h.Actor == liquid && h.Hp == 0).Select(h => (DateTimeOffset?)h.Time).FirstOrDefault() is { } fell)
            events["Liquid falls 1"] = fell;
        return events;
    }

    public IEnumerable<LogFinding> Findings()
    {
        foreach (var (key, name, community) in Opening)
        {
            var (flag, cascade) = (From(Flag, key), From(Cascade1, key));
            yield return new LogFinding(Section, $"Opening: {name}", TeaSource.OpeningNotPlayed,
                "not played" + (community is { } t ? $"; the community timeline has it at {Spread.F(t, "0.0")} s on its own clock" : ""),
                $"{flag.Describe(" s")} from the combat flag; {cascade.Describe(" s")} from Cascade's cast",
                LogFinding.Unplayed(Math.Max(flag.Count, cascade.Count)))
            {
                Details = PerPull(flag.Count > 0 ? Flag : Cascade1, key),
            };
        }

        var wave = SimSource.PartTime(TeaSource.Scenario, "fight.ProteanWave(start + ");
        var cascade2 = SimSource.PartTime(TeaSource.Scenario, "fight.Cascade(start + ");
        var gaol = SimSource.PartTime(TeaSource.Scenario, "fight.LiquidGaol(start + ");
        var unseen1 = SimSource.PartTime(TeaSource.Scenario, "fight.UntelegraphedWaves(start + ");
        var pressurize = SimSource.PartTime(TeaSource.Scenario, "fight.Pressurize(start + ");

        yield return FromTheFlag("Protean 1's clock: the Liquid's telegraphed Protean Wave is cast", Wave1, "float ProteanOneIntoTheTrack =",
                                 P1Duration.ProteanOneIntoTheTrack, wave);
        yield return Row("Protean 1: Hand of Pain's cast, after the Liquid's telegraphed cast", Wave1, "Hand of Pain cast 2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.HandOfPain(start + "), wave.Seconds);
        yield return Row("Protean 1: the first unseen set, after the telegraphed cast", Wave1, "set 1.1", unseen1, wave.Seconds);
        yield return Row("Protean 1: the Sluices start with the first unseen set, after the telegraphed cast", Wave1, "Sluice cast 1", unseen1, wave.Seconds);
        yield return Row("Protean 1: the Sluices go off, after the telegraphed cast", Wave1, "Sluice lands 1", unseen1, wave.Seconds, plus: 3,
                         simNote: ", the sheet's 3 s after they start");
        yield return Row("Protean 1: the second unseen set, after the telegraphed cast", Wave1, "set 1.2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.LastUntelegraphedWaves(start + "), wave.Seconds);
        yield return Row("Protean 1: the first Splash, after the telegraphed cast", Wave1, "Splash 1",
                         SimSource.PartTime(TeaSource.Scenario, "fight.Splashes(start + "), wave.Seconds);
        yield return Row("Protean 1: the Drainage tethers appear, after the telegraphed cast", Wave1, "tethers 1",
                         SimSource.PartTime(TeaSource.Scenario, "fight.DrainageTethers(start + "), wave.Seconds);
        var drainage = SimSource.PartTime(TeaSource.Scenario, "fight.Drainage(start + ");
        yield return Row("Protean 1: the first tornado's Drainage, after the telegraphed cast", Wave1, "Drainage 1", drainage, wave.Seconds);
        yield return Row("Protean 1: the second tornado's Drainage, after the telegraphed cast", Wave1, "Drainage 2", drainage, wave.Seconds);

        var spacing = new Spread(splashSpacing);
        var (spacingAt, simSpacing) = SimSource.Assigned(TeaSource.Mechanics, "private const float SplashSpacing =");
        yield return new LogFinding(Section, "The Splashes' spacing", spacingAt, simSpacing is { } s ? $"{Spread.F(s)} s" : "not found in the mechanics",
            spacing.Describe(" s"), simSpacing is { } sim ? LogFinding.Compare(spacing, sim, 0.05) : Verdict.Inconclusive);
        var (countAt, simCount) = SimSource.Assigned(TeaSource.Mechanics, "private const int SplashCount =");
        var runs = splashCounts.Where(c => c >= 2).ToList();
        yield return new LogFinding(Section, "How many Splashes come in a row", countAt, simCount is { } c ? $"{c:0}" : "not found in the mechanics",
            runs.Count == 0 ? "no run of Splashes" : $"{string.Join(", ", runs)} in the {runs.Count} runs seen (a run the pull's end cut short reads low)",
            runs.Count == 0 ? Verdict.NotInTheseLogs : simCount is { } wanted && runs.Max() == (int)wanted ? Verdict.Matches : Verdict.Differs);

        var bars = new Spread(waveBars);
        var lands = new Spread(waveLands);
        yield return new LogFinding(Section, "The Liquid's telegraphed wave: its bar, and when it lands after the cast starts",
            SimSource.Line(TeaSource.Actions, "Whom is decided with the hit, as the bar ends"),
            "lands 3.0 s after the cast starts, its bar ending 0.3 s before",
            $"lands {lands.Describe(" s")} after the cast starts; the bar the log gives it: {bars.Describe(" s")}",
            LogFinding.Compare(lands, 3.0, 0.1));

        var simGap = P1Duration.CascadeTwoIntoTheTrack + cascade2.Seconds - P1Duration.ProteanOneIntoTheTrack - wave.Seconds;
        yield return new LogFinding(Section, "Cascade 2's cast, after Protean 1's telegraphed cast",
            SimSource.Line(TeaSource.Constants, "float CascadeTwoIntoTheTrack ="),
            simGap is { } gap ? $"{Spread.F(gap)} s" : "not found in the scenario", From(Wave1, Cascade2).Describe(" s"),
            simGap is { } value ? LogFinding.Compare(From(Wave1, Cascade2), value, Tolerance) : Verdict.Inconclusive);
        yield return FromTheFlag("Cascade 2's clock: Cascade 2 is cast", Cascade2, "float CascadeTwoIntoTheTrack =", P1Duration.CascadeTwoIntoTheTrack, cascade2);

        yield return Row("Cascade 2: Hand of Pain's cast, after Cascade 2's cast", Cascade2, "Hand of Pain cast 3",
                         SimSource.PartTime(TeaSource.Scenario, "fight.HandOfPain(start + ", 2), cascade2.Seconds);
        yield return Row("Cascade 2: Cascade lands, after its cast", Cascade2, "Cascade lands 2", cascade2, cascade2.Seconds, plus: 4,
                         simNote: ", the sheet's cast time");
        yield return Row("Cascade 2: the new Liquid Rages appear, after Cascade 2's cast", Cascade2, "tornadoes appear 2",
                         (SimSource.Line(TeaSource.Mechanics, "() => Tornadoes.Appear(newTornadoes)"), cascade2.Seconds), cascade2.Seconds, plus: 4,
                         simNote: ", as Cascade lands");
        yield return Row("Cascade 2: Liquid Gaol, after Cascade 2's cast", Cascade2, "Liquid Gaol 1", gaol, cascade2.Seconds);
        yield return Row("Cascade 2: Throttle lands, after Cascade 2's cast", Cascade2, "Throttle 1",
                         (SimSource.Line(TeaSource.Constants, "float ThrottleAfterLiquidGaol ="), gaol.Seconds), cascade2.Seconds,
                         plus: P1Duration.ThrottleAfterLiquidGaol, simNote: $", {P1Duration.ThrottleAfterLiquidGaol} s after Liquid Gaol");
        yield return Row("Cascade 2: the Liquid's telegraphed Protean Wave, cast, after Cascade 2's cast", Cascade2, "Protean Wave cast 2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.ProteanWave(start + ", 2), cascade2.Seconds);
        yield return Row("Cascade 2: Hand of Pain's second cast, after Cascade 2's cast", Cascade2, "Hand of Pain cast 4",
                         SimSource.PartTime(TeaSource.Scenario, "fight.HandOfPain(start + ", 3), cascade2.Seconds);
        yield return Row("Cascade 2: the first unseen set and the Sluices, after Cascade 2's cast", Cascade2, "set 2.1",
                         SimSource.PartTime(TeaSource.Scenario, "fight.UntelegraphedWaves(start + ", 2), cascade2.Seconds);
        yield return Row("Cascade 2: the tornadoes' telegraphed waves, cast, after Cascade 2's cast", Cascade2, "tornado wave cast 2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.TornadoWaves(start + "), cascade2.Seconds);
        yield return Row("Cascade 2: the second unseen set, after Cascade 2's cast", Cascade2, "set 2.2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.LastUntelegraphedWaves(start + ", 2), cascade2.Seconds);
        yield return Row("Cascade 2: the tornadoes' unseen waves, after Cascade 2's cast", Cascade2, "tornado unseen 2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.UntelegraphedTornadoWaves(start + "), cascade2.Seconds);
        yield return Row("Cascade 2: the Hand glows, after Cascade 2's cast", Cascade2, "glow 2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.TheHandGlows(start + "), cascade2.Seconds);
        yield return Row("Cascade 2: Pressurize, after Cascade 2's cast", Cascade2, "Pressurize 2", pressurize, cascade2.Seconds);
        yield return Row("Cascade 2: the Emboli appear, after Cascade 2's cast", Cascade2, "Emboli appear 2",
                         (SimSource.Line(TeaSource.Constants, "float EmbolusAfterPressurize ="), pressurize.Seconds), cascade2.Seconds,
                         plus: P1Duration.EmbolusAfterPressurize, simNote: $", {P1Duration.EmbolusAfterPressurize} s after Pressurize");
        yield return Row("Cascade 2: the Hand's slam, after Cascade 2's cast", Cascade2, "slam 2",
                         SimSource.PartTime(TeaSource.Scenario, "fight.TheHandSlams(start + "), cascade2.Seconds);
        // When it falls is the party's doing, so the literal is a pick and a pull only shows one party's time.
        var falls = Row("Cascade 2: the Living Liquid falls, after Cascade 2's cast", Cascade2, "Liquid falls 1",
                        SimSource.PartTime(TeaSource.Scenario, "fight.TheLiquidFalls(start + "), cascade2.Seconds, simNote: ", a pick: the party kills it");
        yield return falls.Verdict == Verdict.NotInTheseLogs ? falls : falls with { Verdict = Verdict.Inconclusive };

        foreach (var (key, name) in new[] { ("Hand of Pain cast 5", "Hand of Pain's fifth cast"), ("Splash 7", "the second run of Splashes") })
        {
            var spread = From(Cascade2, key);
            yield return new LogFinding(Section, $"After the second slam: {name}, after Cascade 2's cast",
                SimSource.Line(TeaSource.Scenario, "fight.TheLiquidFalls(start + "), "not played: the Liquid has fallen by then",
                spread.Describe(" s"), LogFinding.Unplayed(spread.Count));
        }

    }

    // A literal of a part, held against the event's time after the part's anchor.
    private LogFinding Row(string name, string anchor, string key, (string Where, float? Seconds) sim, float? anchorSim, double plus = 0,
                           string simNote = "")
    {
        var measured = From(anchor, key);
        var offset = sim.Seconds - anchorSim + (float)plus;
        return new LogFinding(Section, name, sim.Where, offset is { } o ? $"{Spread.F(o)} s{simNote}" : "not found in the scenario",
            measured.Describe(" s"), offset is { } value ? LogFinding.Compare(measured, value, Tolerance) : Verdict.Inconclusive)
        {
            Details = PerPull(anchor, key),
        };
    }

    // A part's place on the fight's clock, held against the time from the combat flag of the cast the part is timed by.
    private LogFinding FromTheFlag(string name, string key, string constant, float partStarts, (string Where, float? Seconds) cast)
    {
        var measured = From(Flag, key);
        var sim = partStarts + cast.Seconds;
        return new LogFinding(Section, $"{name}, after the combat flag", SimSource.Line(TeaSource.Constants, constant),
            sim is { } s ? $"{Spread.F(s)} s: the part starts {partStarts} s into the fight and casts {Spread.F(cast.Seconds!.Value)} s in" : "not found in the scenario",
            measured.Describe(" s"), sim is { } value ? LogFinding.Compare(measured, value, Tolerance) : Verdict.Inconclusive)
        {
            Details = PerPull(Flag, key),
        };
    }
}
