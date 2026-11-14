using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// Every death and the hit that left its player at 0 HP.
internal sealed class TeaP1DeathsPullLogs : ITeaPullLogMeasures
{
    private const string Section = "P1 deaths";

    // How long before a death a hit of the fight's still counts as what the player died to.
    private const double Before = 4;

    private readonly List<string> deaths = [], ends = [];
    private readonly Dictionary<string, int> causes = [];

    public void Observe(TeaPull pull)
    {
        var inThisPull = new Dictionary<string, int>();
        foreach (var death in pull.Deaths)
        {
            var recent = pull.Hits.Where(h => h.Target == death.Player && pull.IsOfTheFight(h.Source) && PullLog.Seconds(h.Time, death.Died) is >= -0.05 and < Before)
                             .ToList();
            var fatal = recent.Where(h => pull.HpAfter(h) == 0).Cast<ActHit?>().FirstOrDefault();
            var blamed = fatal ?? recent.Cast<ActHit?>().LastOrDefault();
            var cause = blamed is { } hit ? $"{pull.ActionName(hit.Action)} ({hit.Action}) from {Kind(pull, hit.Source)}" : "no hit of the fight's";
            causes[cause] = causes.GetValueOrDefault(cause) + 1;
            inThisPull[cause] = inThisPull.GetValueOrDefault(cause) + 1;
            deaths.Add($"{pull.Name} at {Spread.F(pull.Clock(death.Died))} s: {pull.Who(death.Player)} dies to {cause}"
                       + (blamed is { } last ? $", from {pull.HpAt(death.Player, last.Time)} HP{(fatal is null ? "; no effect result of it shows 0 HP" : "")}" : "")
                       + (recent.Count > 1 ? $"; the fight's hits on it in the {Before} s before: {string.Join(", ", recent.Select(h => $"{pull.ActionName(h.Action)} at {Spread.F(pull.Clock(h.Time))}"))}" : ""));
        }
        var dead = pull.End is { } end ? pull.Players.Count(p => !pull.IsAlive(p, end)) : 0;
        ends.Add($"{pull.Name}: {pull.Lasted}, reached {pull.Reached}; {pull.Deaths.Count} deaths, {dead} of {pull.Players.Count()} dead as it ended"
                 + (inThisPull.Count == 0 ? "" : $": {string.Join("; ", inThisPull.OrderByDescending(c => c.Value).Select(c => $"{c.Key} x{c.Value}"))}"));
    }

    private static string Kind(TeaPull pull, uint actor) => pull.BaseOf(actor) switch
    {
        P1BNpcBaseId.LivingLiquid => "the Living Liquid",
        P1BNpcBaseId.LiquidHand => "the Liquid Hand",
        P1BNpcBaseId.LiquidRage => "a Liquid Rage",
        P1BNpcBaseId.Embolus => "an Embolus",
        P1BNpcBaseId.Helper => "a helper",
        TeaLogIds.JagdDoll => "a Jagd Doll",
        P1BNpcBaseId.CruiseChaser => "Cruise Chaser",
        TeaLogIds.BruteJustice => "Brute Justice",
        _ => "something else",
    };

    public IEnumerable<LogFinding> Findings()
    {
        yield return new LogFinding(Section, "How each pull ended",
            SimSource.Line(TeaSource.Scenario, "A mistake kills"), "a mistake kills by the fight's own rule; no health is played, so nothing else does",
            ends.Count == 0 ? "no pull" : $"{ends.Count} pulls, below", ends.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = ends,
        };

        yield return new LogFinding(Section, "What killed: the hit whose effect result left each player at 0 HP, else the fight's last hit on it",
            SimSource.Line(TeaSource.Actions, "Death reasons describe the fight, never a strat's spots"),
            "a second Protean Wave, a Sluice, a Liquid Rage's water, Drainage's rules, Throttle running out, the wrong slam, a touched Embolus",
            causes.Count == 0 ? "no deaths" : string.Join("; ", causes.OrderByDescending(c => c.Value).Select(c => $"{c.Key} x{c.Value}")),
            causes.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive)
        {
            Details = deaths,
        };
    }
}
