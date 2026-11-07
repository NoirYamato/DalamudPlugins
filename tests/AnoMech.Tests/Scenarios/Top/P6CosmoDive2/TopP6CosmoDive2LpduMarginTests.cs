using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6CosmoDive2;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP6CosmoDive2LpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the design promises, measured on every bot
// against the scenario's own hit areas as each hit takes its targets: every first exaflare, both turns and both exaflare
// steps, over several seeds for the AI's jitter. The player's seat moves round, so every role is a bot under every
// pattern. It is left out of the measures: under the debug driver it walks by movement input and can stop a frame's
// step past its spot, and in play it is a human, whom TopP6CosmoDive2LpduHumanTests covers. For the same reason the
// hits aimed at the player are kept out of the table and only held to the slack below; whoever stands where, the tanks
// must still be the two closest to Alpha Omega at the dive, and the off tank the farthest for the autos.
public class TopP6CosmoDive2LpduMarginTests
{
    // Two at least, so a role the player sits in on one seed is a bot on another.
    private static int SeedsPerPattern => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 3));

    // A bot lands anywhere within AiManager's default jitter of its spot. Each walk aims from one jittered spot to the
    // next, so a bot caught mid-walk can stray about one and a half jitters off its unjittered path. The design's table
    // is rounded to 0.01.
    private const float Jitter = 0.3f, Slack = 0.05f;

    // The design's clearances, unjittered on the strat's timing as the sim plays it, less the jitter each check can lose:
    // the measured bot's own, and that of the bot a hit is centred on or dropped under. Up to the hold the bots dodge as
    // Exaflares/WC1's do, so the exaflare and puddle rows are WC1's. Each sixth puddle drops under a bot just short of
    // the third exaflare's point. After the dive M2 waits in the middle, 2 y outside the Flash Gale on the main tank
    // stepped back to (0, -7).
    private static readonly (string Check, float Design, float Jitters)[] Table =
    [
        ("an exaflare", 2.08f, 1.5f),
        ("a puddle", 2.30f, 3f),
        ("the hold outside the sixth puddles", 4.95f, 2.5f),
        ("the tanks closer to Alpha Omega than any non-tank bot at the dive", 7.5f, 2f),
        ("the tanks more than 8 y apart at the dive", 1.19f, 2f),
        ("the six outside both busters", 11.15f, 2f),
        ("the six inside the stack", 6f, 2f),
        ("another player's Flash Gale", 2f, 2f),
        ("the off tank's lead as the farthest from Alpha Omega", 7.5f, 2f),
    ];

    // Off the table, with the slack the P3, P4 and P5 margin tests ask for. The player holds a buster and a Flash Gale
    // whenever it sits in a tank's seat; it holds the stack only when it happens to be the farthest of the six.
    private const float JitterSlack = 0.5f;

    private static readonly string[] Beyond = ["a bot outside the player's buster", "a bot outside the player's Flash Gale"];

    private static readonly string[] BeyondWhenItHappens = ["a bot of the six inside the player's stack"];

    // Every exaflare and puddle at least this far from every bot, whatever the jitter.
    private const float HitFloor = 1f;

    private static IEnumerable<(int First, bool Clockwise, float Step)> AllPatterns()
        => from step in Steps
           from pattern in Patterns
           select (pattern.First, pattern.Clockwise, step);

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

    [Test]
    public void KeepsTheDesignsClearancesForEveryPatternAndStep()
    {
        var margins = new Margins();
        var held = new HashSet<(PartyRole, int, bool, float)>();
        var problems = new List<string>();
        var run = 0;
        foreach (var (first, clockwise, step) in AllPatterns())
        for (var i = 0; i < SeedsPerPattern; i++, run++)
        {
            var player = PerRole.All[(run * 3) % 8];
            foreach (var role in PerRole.All.Where(r => r != player))
                held.Add((role, first, clockwise, step));
            var seed = Random.Shared.Next();
            var measure = margins.For(player, $"first {first}, {(clockwise ? "clockwise" : "counter-clockwise")}, step {step}, player {player}, seed {seed}");
            ScenarioRun result;
            AoeQuery.Evaluated += measure;
            try
            {
                result = ScenarioRun.Execute(typeof(TopP6CosmoDive2Scenario), LpduStrat, seed, new ScenarioRunOptions
                {
                    PlayerRole = player,
                    Overrides = o => Pin((TopP6CosmoDive2StateOverrides)o, first, clockwise, step),
                    Probe = margins.Watch,
                });
            }
            finally
            {
                AoeQuery.Evaluated -= measure;
            }
            if (!result.Passed) problems.Add(result.ToString());
            problems.AddRange(margins.TakeProblems().Select(p => $"seed {seed}, {player} the player: {p}"));
        }

        TestContext.Out.WriteLine($"{run} runs; the bots' tightest clearances:{Environment.NewLine}{margins}");
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(held, Has.Count.EqualTo(8 * AllPatterns().Count()), "every role was a bot under every pattern");
            foreach (var (check, design, jitters) in Table)
                Assert.That(margins.Of(check), Is.GreaterThanOrEqualTo(design - jitters * Jitter - Slack), () => margins.Describe(check));
            Assert.That(margins.Of("an exaflare"), Is.GreaterThanOrEqualTo(HitFloor), () => margins.Describe("an exaflare"));
            Assert.That(margins.Of("a puddle"), Is.GreaterThanOrEqualTo(HitFloor), () => margins.Describe("a puddle"));
            foreach (var check in Beyond)
                Assert.That(margins.Of(check), Is.GreaterThan(JitterSlack), () => margins.Describe(check));
            foreach (var check in BeyondWhenItHappens.Where(margins.Has))
                Assert.That(margins.Of(check), Is.GreaterThan(JitterSlack), () => margins.Describe(check));
        });
    }

    private sealed class Margins
    {
        private const float SixthPuddlesHit = 24f;

        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private readonly List<string> problems = [];
        private readonly HashSet<(float, uint, Vector3)> seen = [];
        private ScenarioProbe? probe;
        private PartyRole player;
        private string playing = "";

        public void Watch(ScenarioProbe p) => probe = p;

        public bool Has(string check) => tightest.ContainsKey(check);

        public float Of(string check) => tightest.TryGetValue(check, out var t) ? t.Clearance : float.NaN;

        public string Describe(string check) => Has(check) ? $"{check} ({tightest[check].Where})" : $"{check} was never measured";

        public List<string> TakeProblems()
        {
            var taken = problems.ToList();
            problems.Clear();
            return taken;
        }

        public Action<AoeQuery> For(PartyRole seat, string pattern)
        {
            probe = null;
            seen.Clear();
            player = seat;
            playing = pattern;
            return Measure;
        }

        private void Measure(AoeQuery hit)
        {
            if (probe is not { } p || !seen.Add((p.Time, hit.ActionId, hit.Source.Position))) return;
            var members = PerRole.All.Where(r => p.Member(r).IsAlive()).ToDictionary(r => r, r => Flat(p.Member(r)!.Position));
            var bots = members.Where(m => m.Key != player).ToList();
            var source = Flat(hit.Source.Position);
            float Clearance(Vector2 at) => hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? float.NaN;
            bool On(Vector2 at) => Vector2.Distance(at, source) < 0.01f;
            var onThePlayer = members.TryGetValue(player, out var playerAt) && On(playerAt);
            switch (hit.ActionId)
            {
                case ActionId.WaveCannon_7BAD or ActionId.WaveCannon_7BAE:
                    foreach (var (_, at) in bots) Note("an exaflare", Clearance(at));
                    break;
                case ActionId.WaveCannon_7BAF:
                    foreach (var (role, at) in bots)
                    {
                        Note("a puddle", Clearance(at));
                        if (p.Time > SixthPuddlesHit && !role.IsTank()) Note("the hold outside the sixth puddles", Clearance(at));
                    }
                    break;
                case ActionId.CosmoDive_7BA7:
                    foreach (var (role, at) in bots.Where(b => !On(b.Value)))
                        Note(onThePlayer ? "a bot outside the player's buster"
                             : role.IsTank() ? "the tanks more than 8 y apart at the dive"
                             : "the six outside both busters", Clearance(at));
                    TheTanksClosest(members, bots);
                    break;
                case ActionId.CosmoDive_7BA8:
                    foreach (var (_, at) in bots.Where(b => !b.Key.IsTank()))
                        Note(onThePlayer ? "a bot of the six inside the player's stack" : "the six inside the stack", -Clearance(at));
                    break;
                case ActionId.Unknown7ddf:
                    foreach (var (_, at) in bots.Where(b => !On(b.Value)))
                        Note(onThePlayer ? "a bot outside the player's Flash Gale" : "another player's Flash Gale", Clearance(at));
                    TheOffTankFarthest(members, bots);
                    break;
            }
        }

        private void TheTanksClosest(Dictionary<PartyRole, Vector2> members, List<KeyValuePair<PartyRole, Vector2>> bots)
        {
            var closest = members.OrderBy(m => m.Value.Length()).Take(2).Select(m => m.Key).ToList();
            if (!closest.All(r => r.IsTank()))
                problems.Add($"t={probe!.Time:F2} {closest[0]} and {closest[1]} were the closest to Alpha Omega at the dive");
            var nearestNonTank = bots.Where(b => !b.Key.IsTank()).Min(b => b.Value.Length());
            foreach (var (_, at) in bots.Where(b => b.Key.IsTank()))
                Note("the tanks closer to Alpha Omega than any non-tank bot at the dive", nearestNonTank - at.Length());
        }

        private void TheOffTankFarthest(Dictionary<PartyRole, Vector2> members, List<KeyValuePair<PartyRole, Vector2>> bots)
        {
            var farthest = members.MaxBy(m => m.Value.Length()).Key;
            if (farthest != OffTank) problems.Add($"t={probe!.Time:F2} {farthest} was the farthest from Alpha Omega for the autos");
            else if (player != OffTank)
                Note("the off tank's lead as the farthest from Alpha Omega",
                     members[OffTank].Length() - bots.Where(b => b.Key != OffTank).Max(b => b.Value.Length()));
        }

        private void Note(string check, float clearance)
        {
            if (tightest.TryGetValue(check, out var before) && before.Clearance <= clearance) return;
            tightest[check] = (clearance, $"t={probe!.Time.ToString("F2", CultureInfo.InvariantCulture)}, {playing}");
        }

        public override string ToString()
            => string.Join(Environment.NewLine, tightest.OrderBy(t => t.Key)
                                                        .Select(t => $"  {t.Key}: {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})"));
    }
}
