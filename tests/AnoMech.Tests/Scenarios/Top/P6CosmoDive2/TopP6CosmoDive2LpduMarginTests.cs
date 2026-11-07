using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6CosmoDive2;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6CosmoDive2LpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the design promises, measured on every bot
// with the scenario's own hit areas as each hit resolves, for every first exaflare, both turns and both exaflare steps;
// the seeds vary the AI's jitter, and the player's seat moves round so every role is a bot in most runs. The player's
// seat is left out: under the debug driver it walks by movement input and can stop a frame's step past its spot, and in
// play it is a human, whom TopP6CosmoDive2LpduHumanTests covers.
public class TopP6CosmoDive2LpduMarginTests
{
    private static int SeedsPerRun => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 3));

    // Each needs at least this much, in yalms.
    private static readonly (string Check, float AtLeast)[] Table =
    [
        ("exaflares", 1f),
        ("puddles", 1f),
        ("the hold to the exaflares", 1f),
        ("the hold to the sixth puddles", 1f),
        ("the tanks' walk to the exaflares", 1f),
        ("the tanks are the two closest by", 1f),
        ("the party outside the busters", 1f),
        ("the six inside the stack", 1f),
        ("the tanks more than 8 y apart by", 0f),
        ("outside the Flash Gales", 1f),
        ("the off-tank the farthest by", 1f),
    ];

    private const float PartyLeavesForTheHold = 21.5f;

    [Test]
    public void KeepsTheDesignsClearancesForEveryPatternAndStep()
    {
        var margins = new Dictionary<string, (float Clearance, string Where)>();
        var problems = new List<string>();
        var runs = 0;
        foreach (var step in Steps)
        foreach (var (first, clockwise) in Patterns)
        for (var i = 0; i < SeedsPerRun; i++)
        {
            var player = PerRole.All[(runs++ * 3) % 8];
            var where = $"first {first}, clockwise {clockwise}, step {step}, player {player}";
            ScenarioProbe? probe = null;
            var seen = new HashSet<(float, uint, Vector3)>();
            void Note(string check, float clearance, string detail)
            {
                if (margins.TryGetValue(check, out var before) && before.Clearance <= clearance) return;
                margins[check] = (clearance, $"{where}, t={probe!.Time.ToString("F2", CultureInfo.InvariantCulture)}: {detail}");
            }
            void Measure(AoeQuery hit)
            {
                if (probe is not { } p || !seen.Add((p.Time, hit.ActionId, hit.Source.Position))) return;
                MeasureHit(p, hit, player, Note);
            }
            AoeQuery.Evaluated += Measure;
            ScenarioRun run;
            try
            {
                run = ScenarioRun.Execute(typeof(TopP6CosmoDive2Scenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
                {
                    PlayerRole = player,
                    Overrides = o => Pin((TopP6CosmoDive2StateOverrides)o, first, clockwise, step),
                    Probe = p => probe = p,
                });
            }
            finally
            {
                AoeQuery.Evaluated -= Measure;
            }
            if (!run.Passed) problems.Add($"{where}: {run}");
        }

        TestContext.Out.WriteLine($"{runs} runs; the bots' tightest clearances:");
        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"  {m.Key}: {m.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({m.Value.Where})")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(20)));
            foreach (var (check, atLeast) in Table)
                Assert.That(margins.TryGetValue(check, out var m) ? m.Clearance : float.NaN, Is.GreaterThan(atLeast), check);
        });
    }

    private static void MeasureHit(ScenarioProbe p, AoeQuery hit, PartyRole player, Action<string, float, string> note)
    {
        var bots = PerRole.All.Where(r => r != player && p.Member(r).IsAlive()).ToList();
        float Clearance(PartyRole role) => hit.SignedDistance(p.Member(role)!.Position) ?? float.NaN;
        bool On(PartyRole role) => Vector2.Distance(Flat(hit.Source.Position), Flat(p.Member(role)!.Position)) < 0.01f;
        switch (hit.ActionId)
        {
            case ActionId.WaveCannon_7BAD or ActionId.WaveCannon_7BAE:
                foreach (var role in bots)
                {
                    note("exaflares", Clearance(role), $"{role}");
                    if (p.Time < PartyLeavesForTheHold) continue;
                    note(role.IsTank() ? "the tanks' walk to the exaflares" : "the hold to the exaflares", Clearance(role), $"{role}");
                }
                break;
            case ActionId.WaveCannon_7BAF:
                foreach (var role in bots)
                {
                    note("puddles", Clearance(role), $"{role}");
                    if (p.Time > 24f && !role.IsTank()) note("the hold to the sixth puddles", Clearance(role), $"{role}");
                }
                break;
            case ActionId.CosmoDive_7BA7:
                foreach (var role in bots.Where(r => !On(r)))
                    if (role.IsTank()) note("the tanks more than 8 y apart by", Clearance(role), $"{role}");
                    else note("the party outside the busters", Clearance(role), $"{role}");
                MeasureWhoIsClosest(p, note);
                break;
            case ActionId.CosmoDive_7BA8:
                foreach (var role in bots.Where(r => !r.IsTank()))
                    note("the six inside the stack", -Clearance(role), $"{role}");
                break;
            case ActionId.Unknown7ddf:
                foreach (var role in bots.Where(r => !On(r)))
                    note("outside the Flash Gales", Clearance(role), $"{role}");
                var byDistance = PerRole.All.OrderByDescending(r => Flat(p.Member(r)!.Position).Length()).ToList();
                note("the off-tank the farthest by",
                     byDistance[0] == OffTank ? Flat(p.Member(OffTank)!.Position).Length() - Flat(p.Member(byDistance[1])!.Position).Length() : -1f,
                     $"farthest {byDistance[0]}");
                break;
        }
    }

    private static void MeasureWhoIsClosest(ScenarioProbe p, Action<string, float, string> note)
    {
        var byDistance = PerRole.All.OrderBy(r => Flat(p.Member(r)!.Position).Length()).ToList();
        var tanksFirst = byDistance.Take(2).All(r => r.IsTank());
        note("the tanks are the two closest by",
             tanksFirst ? Flat(p.Member(byDistance[2])!.Position).Length() - Flat(p.Member(byDistance[1])!.Position).Length() : -1f,
             $"closest {byDistance[0]}, {byDistance[1]}, then {byDistance[2]}");
    }
}
