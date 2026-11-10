using System.Numerics;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// P3 Monitors, timed from Omega's Oversampled Wave Cannon cast.
internal sealed class TopP3MonitorsPullLogs : ITopPullLogMeasures
{
    private const string Section = "P3 Monitors";
    private const string Scenario = "AnoMech/Scenarios/Top/P3Monitors/TopP3MonitorsScenario.cs";
    private const int TargetsPerMonitor = 2;

    private readonly List<double> loadings = [], castAfterError = [], hitAfterCast = [];
    private readonly List<Volley> volleys = [];

    // One monitor: whose it is, which side it fires to, the players on that side nearest its owner first, and the rest.
    private sealed record Monitor(string Owner, bool Omegas, bool Right, List<uint> OnSide, List<uint> Others);

    // Each circle is the set of players one helper's hit landed on: its target and anyone else within its 7 y.
    private sealed record Volley(string Pull, List<Monitor> Monitors, List<HashSet<uint>> Circles, Dictionary<uint, string> Names);

    public void Observe(TopPull pull)
    {
        if (pull.FirstCast(P3ActionId.OversampledWaveCannonEast, P3ActionId.OversampledWaveCannonWest) is not { } cast) return;
        var monitorStatuses = pull.Statuses.Where(s => s.Gained && s.Status is StatusId.PlayerMonitorLeft or StatusId.PlayerMonitorRight
                                                       && Math.Abs(TopPull.Seconds(cast.Time, s.Time)) < 5).ToList();
        loadings.AddRange(monitorStatuses.Select(s => TopPull.Seconds(cast.Time, s.Time)));
        if (pull.Casts.Where(c => c.Action == P3ActionId.CriticalError && c.Time < cast.Time).Cast<ActCast?>().LastOrDefault() is { } error)
            castAfterError.Add(TopPull.Seconds(error.Time, cast.Time));
        var hits = pull.Hits.Where(h => h.Action == ActionId.OversampledWaveCannonAoe && h.HitSomeone
                                        && TopPull.Seconds(cast.Time, h.Time) is > 0 and < 15)
                       .ToList();
        if (hits.Count == 0) return;
        var firedAt = hits.Min(h => h.Time);
        hitAfterCast.Add(TopPull.Seconds(cast.Time, firedAt));

        var alive = pull.Players.Where(p => pull.IsAlive(p, firedAt) && pull.Positions.At(p, firedAt) is not null).ToList();
        var omegaRight = cast.Action == P3ActionId.OversampledWaveCannonEast;
        var monitors = new List<Monitor> { Aim(pull, "Omega", true, TopPull.Centre, pull.CastAt(cast)?.Heading ?? MathF.PI, omegaRight, alive, firedAt) };
        foreach (var status in monitorStatuses.Where(s => pull.IsAlive(s.Target, firedAt)))
            if (pull.Positions.At(status.Target, firedAt) is { Heading: { } heading } owner)
                monitors.Add(Aim(pull, pull.Who(status.Target), false, owner.Position, heading, status.Status == StatusId.PlayerMonitorRight,
                                 alive.Where(p => p != status.Target).ToList(), firedAt));
        var circles = hits.GroupBy(h => h.Source).Select(g => g.Select(h => h.Target).ToHashSet()).ToList();
        volleys.Add(new Volley(pull.Name, monitors, circles, pull.Players.ToDictionary(p => p, pull.Who)));
    }

    // Sides as the sim takes them (CharacterFind.OnSideN): a player lies on its owner's right when it lies along
    // (-cos heading, sin heading) from it.
    private static Monitor Aim(TopPull pull, string owner, bool omegas, Vector2 from, float heading, bool right, List<uint> players,
                               DateTimeOffset at)
    {
        var rightward = new Vector2(-MathF.Cos(heading), MathF.Sin(heading));
        var placed = players.Select(p => (Player: p, Position: pull.Positions.At(p, at)!.Value.Position))
                            .OrderBy(p => Vector2.Distance(p.Position, from))
                            .ToList();
        bool OnSide(Vector2 position) => Vector2.Dot(position - from, rightward) is var dot && (right ? dot > 0 : dot < 0);
        return new Monitor(owner, omegas, right, placed.Where(p => OnSide(p.Position)).Select(p => p.Player).ToList(),
                           placed.Where(p => !OnSide(p.Position)).Select(p => p.Player).ToList());
    }

    public IEnumerable<LogFinding> Findings()
    {
        var landed = new Spread(loadings);
        yield return new LogFinding(Section, "The players' monitor statuses land, after Omega's cast starts",
            SimSource.Line(Scenario, "UNVERIFIED: the Loading statuses land with the cast's start"), "0 s: with the cast",
            landed.Describe(" s"), LogFinding.Compare(landed, 0, 0.15));

        var explained = Rules.Select(r => (r.Name, Pulls: volleys.Count(v => Fits(v, r)))).ToList();
        yield return new LogFinding(Section, "Who the monitors hit: how many pulls each rule explains, circle for circle",
            SimSource.Line(Scenario, "party.Find.OnSideN(world.Rng, source, side.Mul(), TargetsPerMonitor"),
            Rules[0].Name,
            volleys.Count == 0 ? "no monitors fired"
                : string.Join("; ", explained.Select(e => $"{e.Name}: {e.Pulls} of {volleys.Count}"))
                  + $". Circles per pull: {string.Join(", ", volleys.Select(v => v.Circles.Count))}. Sides from the owners' logged "
                  + "headings as the circles land",
            volleys.Count == 0 ? Verdict.NotInTheseLogs : explained[0].Pulls == volleys.Count ? Verdict.Matches : Verdict.Differs)
        {
            Details = volleys.Select(Describe).ToList(),
        };

        var (errorAt, errorSeconds) = SimSource.EventTime(Scenario, "TopP3Actions.CriticalError");
        var (startAt, startSeconds) = SimSource.EventTime(Scenario, "StartMonitors);");
        var (fireAt, fireSeconds) = SimSource.EventTime(Scenario, "FireMonitors);");
        var cast = new Spread(castAfterError);
        yield return new LogFinding(Section, $"Omega's monitor cast, after Critical Error's cast ({errorAt})", startAt,
            startSeconds - errorSeconds is { } sim ? $"{Spread.F(sim)} s" : "not found in the scenario", cast.Describe(" s"),
            startSeconds - errorSeconds is { } simCast ? LogFinding.Compare(cast, simCast, 0.25) : Verdict.Inconclusive);
        var fired = new Spread(hitAfterCast);
        yield return new LogFinding(Section, "The monitors fire, after Omega's monitor cast", fireAt,
            fireSeconds - startSeconds is { } simFire ? $"{Spread.F(simFire)} s" : "not found in the scenario", fired.Describe(" s"),
            fireSeconds - startSeconds is { } simFired ? LogFinding.Compare(fired, simFired, 0.25) : Verdict.Inconclusive);
    }

    // A rule for whom each monitor targets: two players on its side, topped up from the other side as the sim's are, or,
    // for Omega's with OmegaHitsItsSide, every player on its side and nobody else.
    private sealed record Rule(string Name, bool OmegaHitsItsSide, bool Nearest);

    private static readonly Rule[] Rules =
    [
        new("every monitor, Omega's too, hits two random players on its side, topped up from the other side", false, false),
        new("the same, but each hits the two nearest its owner", false, true),
        new("Omega's monitor hits every player on its side and no other, the players' two at random as above", true, false),
        new("the same, but the players' hit the two nearest their owner", true, true),
    ];

    // Each monitor's hits as slots, one per circle, each taking a circle that holds a player it may target.
    private static IEnumerable<Func<HashSet<uint>, bool>> Slots(Monitor monitor, Rule rule)
    {
        var targets = monitor.Omegas && rule.OmegaHitsItsSide
            ? monitor.OnSide
            : monitor.OnSide.Concat(monitor.Others).Take(TargetsPerMonitor).ToList();
        if (rule.Nearest)
            return targets.Select(p => (Func<HashSet<uint>, bool>)(circle => circle.Contains(p)));
        var pool = targets.Count <= monitor.OnSide.Count ? monitor.OnSide : monitor.OnSide.Concat(monitor.Others).ToList();
        return targets.Select(_ => (Func<HashSet<uint>, bool>)(circle => circle.Overlaps(pool)));
    }

    // Whether the circles and the rule's slots pair off one to one.
    private static bool Fits(Volley volley, Rule rule)
    {
        var slots = volley.Monitors.SelectMany(m => Slots(m, rule)).ToList();
        if (slots.Count != volley.Circles.Count) return false;
        var taken = new bool[slots.Count];
        bool Assign(int circle)
        {
            if (circle == volley.Circles.Count) return true;
            for (var s = 0; s < slots.Count; s++)
            {
                if (taken[s] || !slots[s](volley.Circles[circle])) continue;
                taken[s] = true;
                if (Assign(circle + 1)) return true;
                taken[s] = false;
            }
            return false;
        }
        return Assign(0);
    }

    private static string Describe(Volley volley)
    {
        string Names(IEnumerable<uint> players) => string.Join(" ", players.Select(p => volley.Names.GetValueOrDefault(p, "someone")));
        var monitors = volley.Monitors.Select(m => $"{m.Owner} fires {(m.Right ? "right" : "left")} at [{Names(m.OnSide)}]");
        var circles = volley.Circles.Select(c => $"[{Names(c)}]");
        return $"{volley.Pull}: {string.Join("; ", monitors)} (nearest first); the circles hit {string.Join(" ", circles)}";
    }
}
