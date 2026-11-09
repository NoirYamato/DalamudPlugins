namespace AnoMech.Tests;

// The hits of a mechanic the sim kills with on their own, each with what it did to its target. A hit on a player under an
// invulnerability shows nothing of its damage, so it is listed but left out of the count.
internal sealed class LethalHits
{
    private readonly List<(HitOutcome Outcome, string Detail)> hits = [];

    public void Observe(TopPull pull, IEnumerable<ActHit> observed, Func<ActHit, string> describe)
    {
        foreach (var hit in observed.Where(h => h.HitSomeone))
            if (pull.Outcome(hit) is var outcome and not HitOutcome.AlreadyDead)
                hits.Add((outcome, $"{pull.Name}: {describe(hit)} on {pull.Who(hit.Target)}: {pull.Describe(hit)}"));
    }

    public LogFinding Finding(string section, string name, string constant, string sim)
    {
        var killed = hits.Count(h => h.Outcome == HitOutcome.Killed);
        var counted = hits.Count(h => h.Outcome is HitOutcome.Killed or HitOutcome.Survived);
        var shielded = hits.Count - counted;
        var measured = hits.Count == 0 ? "no hits on a living player"
            : $"{killed} of {counted} hits killed their target" + (shielded == 0 ? "" : $"; {shielded} on a player under an invulnerability left out");
        var verdict = counted == 0 ? hits.Count == 0 ? Verdict.NotInTheseLogs : Verdict.Inconclusive
            : killed == counted ? Verdict.Matches
            : Verdict.Differs;
        return new LogFinding(section, name, constant, sim, measured, verdict) { Details = hits.Select(h => h.Detail).ToList() };
    }
}
