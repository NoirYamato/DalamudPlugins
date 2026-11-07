using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6CosmoDive1;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6CosmoDive1LpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances LPDU's spots keep, measured on every bot against
// the scenario's own hit areas (AoeQuery) as each one resolves: every exasquare line, both autos' Flash Gales and Cosmo
// Dive's busters and stack. Each pattern runs with the player in every seat, so every role is a bot in seven seats of
// eight, and over several seeds for the bots' jitter. The player's seat is left out of the measures: under the debug
// driver it walks by movement input and can stop a frame's step past its spot, and in play it is a human, whom
// TopP6CosmoDive1LpduHumanTests covers. For the same reason the hits on the player are kept out of the table and only
// held to the slack below, and the player is no rival in who is closest or farthest.
public class TopP6CosmoDive1LpduMarginTests
{
    private static int SeedsPerSeat => Math.Max(1, TestContext.Parameters.Get("MarginSeeds", 10));

    // A bot lands anywhere within AiManager's default jitter of its spot; the rows are rounded to 0.01.
    private const float Jitter = 0.3f, Rounding = 0.02f;

    // The least each row keeps at LPDU's spots, less the jitter the check can lose: one bot's against a fixed hit, two
    // between bots. The exasquare row holds mid-walk too: every move lands a wave early, and the tanks' walks to their
    // dive spots stay clear of the waves they cross. The farthest anyone stands out is the out-first corner, r 16.97.
    private static readonly (string Check, float Design, float Jitters)[] Table =
    [
        ("every bot outside every exasquare line as it lands", 2.0f, 1f),
        ("the off-tank farther from Alpha Omega than any other bot at each auto", 8.5f, 2f),
        ("nobody but its target in a Flash Gale", 6.0f, 2f),
        ("the tanks closer to Alpha Omega than any non-tank bot at the dive", 4.11f, 2f),
        ("the tanks more than 8 y apart at the dive", 1.19f, 2f),
        ("the six outside both busters", 4.44f, 2f),
        ("the six inside the stack", 6.0f, 2f),
        ("the tanks outside the stack", 6.44f, 2f),
        ("everyone inside the arena", 3.03f, 1f),
    ];

    // Off the table, with the slack the P3, P4 and P5 margin tests ask for. The player holds a Flash Gale and a buster
    // whenever it sits in a tank's seat; it holds the stack only when it happens to be the farthest of the six.
    private const float JitterSlack = 0.5f;

    private static readonly string[] Beyond =
    [
        "a bot outside the player's Flash Gale",
        "a bot outside the player's buster",
    ];

    private static readonly string[] BeyondWhenItHappens =
    [
        "a bot of the six inside the player's stack",
        "a bot tank outside the player's stack",
    ];

    // Seconds to spare: the tanks settle on their dive spots 2.2 s (in first) or 4.2 s (out first) before the hits.
    private static readonly (string Check, float Least)[] Timing =
    [
        ("the tanks settled on their dive spots before the dive", 1.5f),
    ];

    private const float DiveAt = 39.4f, Settled = 0.5f;

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    [TestCase(true)]
    [TestCase(false)]
    public void KeepsLpdusClearancesForEverySeat(bool inFirst)
    {
        var margins = new Dictionary<string, (float Clearance, string Where)>();
        var problems = new List<string>();
        var measured = new HashSet<PartyRole>();
        foreach (var player in PerRole.All)
        for (var i = 0; i < SeedsPerSeat; i++)
        {
            var seed = Random.Shared.Next();
            var measurer = new Measurer(player, $"player {player}, seed {seed}", margins, measured);
            ScenarioRun run;
            AoeQuery.Evaluated += measurer.OnHit;
            try
            {
                run = ScenarioRun.Execute(typeof(TopP6CosmoDive1Scenario), LpduStrat, seed, new ScenarioRunOptions
                {
                    PlayerRole = player,
                    Overrides = o => Pin((TopP6CosmoDive1StateOverrides)o, inFirst),
                    Probe = measurer.Tick,
                });
            }
            finally
            {
                AoeQuery.Evaluated -= measurer.OnHit;
            }
            if (!run.Passed) problems.Add(run.ToString());
            problems.AddRange(measurer.Finish().Select(problem => $"seed {run.Seed}, player {player}: {problem}"));
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({m.Value.Where})")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(40)));
            Assert.That(measured, Is.EquivalentTo(PerRole.All), "every role was measured as a bot");
            foreach (var (check, design, jitters) in Table)
                Assert.That(Margin(margins, check), Is.GreaterThanOrEqualTo(design - jitters * Jitter - Rounding), () => Describe(margins, check));
            foreach (var check in Beyond)
                Assert.That(Margin(margins, check), Is.GreaterThan(JitterSlack), () => Describe(margins, check));
            foreach (var check in BeyondWhenItHappens.Where(margins.ContainsKey))
                Assert.That(margins[check].Clearance, Is.GreaterThan(JitterSlack), () => Describe(margins, check));
            foreach (var (check, least) in Timing)
                Assert.That(Margin(margins, check), Is.GreaterThanOrEqualTo(least), () => Describe(margins, check));
        });
    }

    private static float Margin(Dictionary<string, (float Clearance, string Where)> margins, string check)
        => margins.TryGetValue(check, out var margin) ? margin.Clearance : float.NaN;

    private static string Describe(Dictionary<string, (float Clearance, string Where)> margins, string check)
        => margins.TryGetValue(check, out var margin) ? $"{check} ({margin.Where})" : $"{check} was never measured";

    private sealed class Measurer(PartyRole player, string playing, Dictionary<string, (float Clearance, string Where)> margins, HashSet<PartyRole> measured)
    {
        private ScenarioProbe? probe;
        private readonly Dictionary<PartyRole, float> settledOnDiveSpot = [];
        private readonly HashSet<(float, uint)> ranked = [];

        private IEnumerable<PartyRole> Bots => PerRole.All.Where(r => r != player && probe!.Member(r).IsAlive());

        private Vector2 At(PartyRole role) => Flat(probe!.Member(role)!.Position);

        private float FromAlphaOmega(PartyRole role) => At(role).Length();

        public void Tick(ScenarioProbe p)
        {
            probe = p;
            foreach (var bot in Bots)
            {
                measured.Add(bot);
                Note("everyone inside the arena", 20f - FromAlphaOmega(bot));
                if (bot.IsTank() && p.Time > 32f && p.Time < DiveAt && !settledOnDiveSpot.ContainsKey(bot)
                    && Vector2.Distance(At(bot), DiveSpot(bot)) < Settled)
                    settledOnDiveSpot[bot] = p.Time;
            }
        }

        public void OnHit(AoeQuery hit)
        {
            if (probe is not { } p) return;
            var source = Flat(hit.Source.Position);
            float Clearance(PartyRole role) => hit.SignedDistance(p.Member(role)!.Position) ?? float.NaN;
            bool IsTarget(PartyRole role) => Vector2.Distance(At(role), source) < 0.01f;
            var onThePlayer = IsTarget(player);
            var firstOfItsKind = ranked.Add((p.Time, hit.ActionId));
            switch (hit.ActionId)
            {
                case ActionId.CosmoArrowOmen or ActionId.CosmoArrowDamage:
                    foreach (var bot in Bots)
                        Note("every bot outside every exasquare line as it lands", Clearance(bot));
                    break;
                case ActionId.Unknown7ddf:
                    foreach (var bot in Bots.Where(b => !IsTarget(b)))
                        Note(onThePlayer ? "a bot outside the player's Flash Gale" : "nobody but its target in a Flash Gale", Clearance(bot));
                    if (firstOfItsKind && Bots.Contains(OffTank))
                        Note("the off-tank farther from Alpha Omega than any other bot at each auto",
                             FromAlphaOmega(OffTank) - Bots.Where(r => r != OffTank).Max(FromAlphaOmega));
                    break;
                case ActionId.CosmoDive_7BA7:
                    foreach (var bot in Bots.Where(b => !IsTarget(b)))
                        Note(onThePlayer ? "a bot outside the player's buster"
                             : bot.IsTank() ? "the tanks more than 8 y apart at the dive"
                             : "the six outside both busters", Clearance(bot));
                    if (firstOfItsKind)
                        foreach (var tank in Bots.Where(b => b.IsTank()))
                            Note("the tanks closer to Alpha Omega than any non-tank bot at the dive",
                                 Bots.Where(r => !r.IsTank()).Min(FromAlphaOmega) - FromAlphaOmega(tank));
                    break;
                case ActionId.CosmoDive_7BA8:
                    foreach (var bot in Bots)
                        if (bot.IsTank())
                            Note(onThePlayer ? "a bot tank outside the player's stack" : "the tanks outside the stack", Clearance(bot));
                        else
                            Note(onThePlayer ? "a bot of the six inside the player's stack" : "the six inside the stack", -Clearance(bot));
                    break;
            }
        }

        public IEnumerable<string> Finish()
        {
            foreach (var tank in new[] { MainTank, OffTank }.Where(t => t != player))
            {
                if (settledOnDiveSpot.TryGetValue(tank, out var at))
                    Note("the tanks settled on their dive spots before the dive", DiveAt - at, at);
                else
                    yield return $"{tank} never settled on its dive spot";
            }
        }

        private void Note(string check, float clearance, float? at = null)
        {
            if (margins.TryGetValue(check, out var before) && before.Clearance <= clearance) return;
            margins[check] = (clearance, $"t={(at ?? probe!.Time).ToString("F2", CultureInfo.InvariantCulture)}, {playing}");
        }
    }
}
