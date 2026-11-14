using System.Numerics;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// Drainage's tethers and its hit, and Liquid Gaol's Throttle.
internal sealed class TeaP1DrainagePullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 Drainage and Throttle";

    private readonly List<double> tetherBeforeTheHit = [], nearestOther = [], throttleSeconds = [], throttleHeld = [], throttleAfterGaol = [];
    private readonly List<string> tethers = [], hits = [], gaols = [], throttles = [], cleanses = [];
    private readonly Dictionary<string, int> tornadoWithout = [];
    private readonly List<int> throttled = [], gaolCasters = [];
    private int tetherSets, tetherSetsOfTwo, startOnTanks, starts, passes, drainages, drainagesOnTheHolder, drainagesHittingOthers;
    private int cleansed, cleansedByAnAction, ranOut, ranOutKilled;

    public void Observe(TeaPull pull)
    {
        ObserveDrainage(pull);
        ObserveThrottle(pull);
    }

    private void ObserveDrainage(TeaPull pull)
    {
        var lines = pull.Tethers.Where(t => t.Id == P1TetherId.Drainage).ToList();
        if (lines.Count == 0) return;
        var set = pull.TornadoesAt(lines[0].Time);
        var sources = lines.Select(t => t.Source).Distinct().ToList();
        tetherSets++;
        if (sources.Count == TeaP1LivingLiquidState.DrainageTethers) tetherSetsOfTwo++;
        var named = sources.Select(s => pull.Where(s, lines[0].Time) is { } at ? set?.Which(at) : null).ToList();
        var without = Enum.GetValues<TeaP1Tornado>().Where(t => !named.Contains(t)).ToList();
        if (without.Count == 1 && named.All(n => n is not null))
            tornadoWithout[without[0].ToString()] = tornadoWithout.GetValueOrDefault(without[0].ToString()) + 1;
        foreach (var source in sources)
        {
            var own = lines.Where(t => t.Source == source).ToList();
            starts++;
            if (pull.IsTank(own[0].Target)) startOnTanks++;
            passes += own.Count - 1;
            var hit = pull.Hits.Where(h => h.Action == P1ActionId.Drainage && h.Source == source && h.Time > own[0].Time).ToList();
            var which = pull.Where(source, own[0].Time) is { } tornadoAt ? set?.Which(tornadoAt)?.ToString() ?? "a" : "a";
            tethers.Add($"{pull.Name}: the {which} tornado's tether ({set?.Layout ?? "no set"}) starts on {pull.Who(own[0].Target)} at "
                        + $"{Spread.F(pull.Clock(own[0].Time))} s" + string.Concat(own.Skip(1).Select(t => $", passes to {pull.Who(t.Target)} at {Spread.F(pull.Clock(t.Time))} s"))
                        + (hit.Count == 0 ? "; no Drainage follows" : $"; Drainage {Spread.F(PullLog.Seconds(own[0].Time, hit[0].Time))} s after it shows"));
            if (hit.Count == 0) continue;
            tetherBeforeTheHit.Add(PullLog.Seconds(own[0].Time, hit[0].Time));
            drainages++;
            var holder = own[^1].Target;
            var struck = hit.Where(h => h.HitSomeone).ToList();
            if (struck.Any(h => h.Target == holder)) drainagesOnTheHolder++;
            if (struck.Count > 1) drainagesHittingOthers++;
            var time = hit[0].Time;
            var others = pull.Living(time).Where(p => p != holder)
                             .Select(p => pull.Distance(p, holder, time)).OfType<double>().DefaultIfEmpty(double.NaN).Min();
            if (!double.IsNaN(others)) nearestOther.Add(others);
            foreach (var one in struck)
                hits.Add($"{pull.Name} at {Spread.F(pull.Clock(time))} s: Drainage from the {which} tornado on {pull.Who(one.Target)}"
                         + (one.Target == holder ? ", its tether's holder" : ", not its tether's holder")
                         + $", {TeaP1HandPullLogs.Damage(pull, one)}; {TeaP1HandPullLogs.Cover(pull, one.Target, source, time)}; the nearest other living player "
                         + $"{Spread.F(others)} y from the holder");
        }
    }

    private void ObserveThrottle(TeaPull pull)
    {
        foreach (var volley in pull.Volleys(P1ActionId.LiquidGaol))
        {
            var casters = volley.Select(h => h.Source).Distinct().Count();
            gaolCasters.Add(casters);
            var standing = pull.TornadoesAt(volley[0].Time)?.Tornadoes.Count ?? 0;
            gaols.Add($"{pull.Name} at {Spread.F(pull.Clock(volley[0].Time))} s: Liquid Gaol from {casters} of the {standing} Liquid Rages standing");
            var gains = pull.Statuses.Where(s => s.Gained && s.Status == P1StatusId.Throttle && PullLog.Seconds(volley[0].Time, s.Time) is >= 0 and < 3).ToList();
            if (gains.Count == 0) continue;
            throttled.Add(gains.Select(g => g.Target).Distinct().Count());
            throttleAfterGaol.Add(PullLog.Seconds(volley[0].Time, gains[0].Time));
            var spared = pull.Living(gains[0].Time).Except(gains.Select(g => g.Target)).Select(pull.Who).Order().ToList();
            throttles.Add($"{pull.Name}: Throttle on {string.Join(", ", gains.Select(g => pull.Who(g.Target)).Order())}; not on "
                          + (spared.Count == 0 ? "nobody living" : string.Join(", ", spared)));
        }
        throttleSeconds.AddRange(pull.Statuses.Where(s => s.Gained && s.Status == P1StatusId.Throttle).Select(s => (double)s.Duration));
        foreach (var gain in pull.Statuses.Where(s => s.Gained && s.Status == P1StatusId.Throttle))
        {
            var lost = pull.Statuses.Where(s => !s.Gained && s.Status == P1StatusId.Throttle && s.Target == gain.Target && s.Time >= gain.Time)
                           .Select(s => (DateTimeOffset?)s.Time).FirstOrDefault();
            if (lost is not { } at) continue;
            var held = PullLog.Seconds(gain.Time, at);
            if (held >= gain.Duration - 0.3)
            {
                throttleHeld.Add(held);
                ranOut++;
                var died = pull.DiedWithin(gain.Target, at.AddSeconds(-0.5), 3) is not null;
                if (died) ranOutKilled++;
                cleanses.Add($"{pull.Name}: {pull.Who(gain.Target)} holds Throttle its whole {Spread.F(held)} s: {(died ? "dies" : "lives")}");
                continue;
            }
            // A holder that died before its time lost it to that, which says nothing of a cleanse.
            if (!pull.IsAlive(gain.Target, at) || pull.DiedWithin(gain.Target, at.AddSeconds(-0.5), 1) is not null) continue;
            throttleHeld.Add(held);
            cleansed++;
            var cleanse = pull.Hits.Where(h => h.Target == gain.Target && ActorIds.IsPlayer(h.Source) && h.Action is P1PlayerActionId.Esuna or P1PlayerActionId.WardensPaean
                                               && PullLog.Seconds(h.Time, at) is >= -0.2 and < 1.5)
                              .Cast<ActHit?>().LastOrDefault();
            if (cleanse is not null) cleansedByAnAction++;
            cleanses.Add($"{pull.Name}: {pull.Who(gain.Target)} loses Throttle {Spread.F(held)} s after gaining it"
                         + (cleanse is { } c ? $", {Spread.F(PullLog.Seconds(c.Time, at))} s after {pull.Who(c.Source)}'s {pull.ActionName(c.Action)}"
                             : ", with no Esuna or Warden's Paean on it"));
        }
    }

    public IEnumerable<LogFinding> Findings()
    {
        var tethersAt = SimSource.PartTime(TeaSource.Scenario, "fight.DrainageTethers(start + ");
        var drainageAt = SimSource.PartTime(TeaSource.Scenario, "fight.Drainage(start + ");
        var before = new Spread(tetherBeforeTheHit);
        yield return new LogFinding(Section, "The Drainage tethers show, before their Drainage", tethersAt.Where,
            drainageAt.Seconds - tethersAt.Seconds is { } gap ? $"{Spread.F(gap)} s" : "not found in the scenario", before.Describe(" s"),
            drainageAt.Seconds - tethersAt.Seconds is { } sim ? LogFinding.Compare(before, sim, 0.25) : Verdict.Inconclusive)
        {
            Details = tethers,
        };

        yield return new LogFinding(Section, "Which tornadoes tether",
            SimSource.Line(TeaSource.State, "var tethering = AllBut((TeaP1Tornado)rng.NextInt(3));"), "two of the three, the third rolled evenly",
            tetherSets == 0 ? "no Drainage tether"
                : $"{tetherSetsOfTwo} of {tetherSets} pulls have two tornadoes tethering; without one: "
                  + string.Join(", ", tornadoWithout.OrderBy(t => t.Key).Select(t => $"{t.Key} x{t.Value}")) + ". Too few to tell the odds",
            tetherSets == 0 ? Verdict.NotInTheseLogs : tetherSetsOfTwo == tetherSets ? Verdict.Matches : Verdict.Differs);

        yield return new LogFinding(Section, "Whom the tethers start on",
            SimSource.Line(TeaSource.State, "UNVERIFIED whom the fight picks"), "any two players, unless the setting says the tanks or the rest",
            starts == 0 ? "no Drainage tether" : $"{startOnTanks} of {starts} tethers start on a tank",
            starts == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);

        yield return new LogFinding(Section, "A tether passes to whoever steps into it",
            SimSource.Line(TeaSource.Mechanics, "UNVERIFIED: that the fight refuses a holder the second tether"),
            "passes to whoever stands in its line, but for someone who already holds one",
            starts == 0 ? "no Drainage tether" : $"{passes} passes over {starts} tethers" + (passes == 0 ? ": every tether stayed on the player it started on" : ""),
            passes == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive);

        var apart = new Spread(nearestOther);
        yield return new LogFinding(Section, "Drainage hits its tether's holder, and whoever stands within its radius of the holder",
            SimSource.Line(TeaSource.Constants, "float DrainageRadius ="), $"the holder and everyone within {P1Geometry.DrainageRadius} y of it",
            drainages == 0 ? "no Drainage"
                : $"{drainagesOnTheHolder} of {drainages} Drainages hit their tether's holder; {drainagesHittingOthers} hit someone else too; the nearest other living "
                  + $"player stood {apart.Describe(" y")} from the holder (client positions)",
            drainages == 0 ? Verdict.NotInTheseLogs
            : drainagesOnTheHolder < drainages ? Verdict.Differs
            : drainagesHittingOthers == 0 && apart.Count > 0 && apart.Min < P1Geometry.DrainageRadius - 1 ? Verdict.Differs
            : drainagesHittingOthers > 0 ? Verdict.Matches : Verdict.Inconclusive);

        yield return new LogFinding(Section, "Drainage's damage and what covered it",
            SimSource.Line(TeaSource.Constants, "float DrainageMitigation ="),
            $"a tank seat lives through its own; a human tank on a tank's job needs {P1Rules.DrainageMitigation} of mitigation",
            hits.Count == 0 ? "no Drainage hit anyone" : $"{hits.Count} hits, each below with the hit points it took and the statuses that covered it",
            hits.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = hits,
        };

        yield return new LogFinding(Section, "Liquid Gaol: which tornadoes cast it",
            SimSource.Line(TeaSource.Mechanics, "UNVERIFIED which of the tornadoes cast it"), "all three",
            gaolCasters.Count == 0 ? "no Liquid Gaol" : string.Join("; ", gaols),
            gaolCasters.Count == 0 ? Verdict.NotInTheseLogs : gaolCasters.All(c => c == 3) ? Verdict.Matches : Verdict.Differs);

        yield return new LogFinding(Section, "Throttle: on how many players",
            SimSource.Line(TeaSource.State, "public const int ThrottleTargets ="), $"{TeaP1LivingLiquidState.ThrottleTargets}",
            throttled.Count == 0 ? "no Throttle" : $"{string.Join(", ", throttled)}, {new Spread(throttleAfterGaol).Describe(" s")} after Liquid Gaol",
            throttled.Count == 0 ? Verdict.NotInTheseLogs
            : throttled.All(n => n == TeaP1LivingLiquidState.ThrottleTargets) ? Verdict.Matches : Verdict.Differs)
        {
            Details = throttles,
        };

        var seconds = new Spread(throttleSeconds);
        yield return new LogFinding(Section, "Throttle's seconds",
            SimSource.Line(TeaSource.Constants, "float Throttle ="), $"{P1Duration.Throttle} s", seconds.Describe(" s"),
            LogFinding.Compare(seconds, P1Duration.Throttle, 0.25));

        var held = new Spread(throttleHeld);
        yield return new LogFinding(Section, "A cleanse takes Throttle off",
            SimSource.Line(TeaSource.Mechanics, "if (carriers.Remove(cleanse.Target)) cleanse.Target.RemoveStatus(P1StatusId.Throttle);"),
            "an Esuna or a Warden's Paean on a carrier takes it off",
            cleansed == 0 ? "no Throttle lost before its time"
                : $"{cleansedByAnAction} of {cleansed} Throttles lost early went within 1.5 s of an Esuna or a Warden's Paean on their holder; held {held.Describe(" s")}",
            cleansed == 0 ? Verdict.NotInTheseLogs : cleansedByAnAction == cleansed ? Verdict.Matches : Verdict.Inconclusive)
        {
            Details = cleanses,
        };

        yield return new LogFinding(Section, "Throttle running out kills its holder",
            SimSource.Line(TeaSource.Mechanics, "internal const string ThrottleRanOut ="), "whoever still carries it as it runs out dies",
            ranOut == 0 ? "no Throttle ran its whole time" : $"{ranOutKilled} of {ranOut} holders who kept it its whole time died",
            ranOut == 0 ? Verdict.NotInTheseLogs : ranOutKilled == ranOut ? Verdict.Matches : Verdict.Differs);
    }
}
