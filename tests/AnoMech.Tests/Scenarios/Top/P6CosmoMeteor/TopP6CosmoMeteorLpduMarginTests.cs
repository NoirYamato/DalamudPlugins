using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6CosmoMeteor;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP6Constants;
using static AnoMech.Tests.TopP6CosmoMeteorLpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances LPDU's spots keep, measured on every bot against
// the scenario's own hit areas (AoeQuery) as each one decides who it hits, and against each flare's kill radius as it
// lands. Every one of the 56 flare combinations is played with each of the five stack targets it leaves, with the spread
// waves each seed draws; the player sits in a different seat in each of a combination's five runs, so every role is a
// bot under every combination. The player's seat is left out of the measures: under the debug driver it walks by
// movement input and can stop a frame's step past its spot, and in play it is a human, whom
// TopP6CosmoMeteorLpduHumanTests covers. For the same reason the hits aimed at the player are kept out of the table and
// only held to the slack below.
public class TopP6CosmoMeteorLpduMarginTests
{
    // A bot lands anywhere within AiManager's default jitter of its spot; the design's table is rounded to 0.01.
    private const float Jitter = 0.3f, Rounding = 0.02f;

    // The design's clearance at the exact spots, less the jitter each check can lose: two jitters wherever a hit is
    // centred on one bot or dropped where one stood, one against the arena's fixed edge.
    private static readonly (string Check, float Design, float Jitters)[] Table =
    [
        ("every bot outside every bait as it decides", 3f, 2f),
        ("every bot outside every other bot's spread", 4.95f, 2f),
        ("every bot without a flare inside the stack", 6f, 2f),
        ("every bot with a flare outside the stack", 16.61f, 2f),
        ("every bot outside every other bot's flare kill radius", 2.61f, 2f),
        ("every bot inside the arena", 1.5f, 1f),
    ];

    // Off the table, with the slack the P3 and P4 margin tests ask for.
    private const float JitterSlack = 0.5f;

    private static readonly string[] Beyond =
    [
        "every bot outside the player's bait",
        "every bot outside the player's spread",
        "every bot outside the player's flare kill radius",
    ];

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
    public void KeepsTheLpduClearancesForEveryFlareCombinationAndStackTarget()
    {
        var margins = new Dictionary<string, float>();
        var bots = new HashSet<(PartyRole, string)>();
        var problems = new List<string>();
        for (var combination = 0; combination < FlareCombinations.Length; combination++)
        {
            var flares = FlareCombinations[combination];
            var stacks = PerRole.All.Where(r => !flares.Contains(r)).ToArray();
            for (var k = 0; k < stacks.Length; k++)
            {
                var player = PerRole.All[(combination + k) % 8];
                var seed = 7919 * (combination * 5 + k) + 1;
                foreach (var role in PerRole.All.Where(r => r != player)) bots.Add((role, Describe(flares)));
                var measurer = new Measurer(player, flares, stacks[k], margins);
                ScenarioRun run;
                AoeQuery.Evaluated += measurer.OnHit;
                try
                {
                    run = ScenarioRun.Execute(typeof(TopP6CosmoMeteorScenario), LpduStrat, seed, new ScenarioRunOptions
                    {
                        PlayerRole = player,
                        Overrides = o => Pin((TopP6CosmoMeteorStateOverrides)o, flares, stacks[k]),
                        Probe = measurer.Tick,
                    });
                }
                finally
                {
                    AoeQuery.Evaluated -= measurer.OnHit;
                }
                if (!run.Passed) problems.Add($"flares {Describe(flares)}, stack {stacks[k]}: {run}");
                problems.AddRange(measurer.Finish().Select(problem => $"flares {Describe(flares)}, stack {stacks[k]}, player {player}, seed {seed}: {problem}"));
            }
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value.ToString("F2", CultureInfo.InvariantCulture)}")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(40)));
            Assert.That(bots, Has.Count.EqualTo(8 * FlareCombinations.Length), "every role was a bot under every flare combination");
            foreach (var (check, design, jitters) in Table)
                Assert.That(margins.GetValueOrDefault(check, float.NaN), Is.GreaterThanOrEqualTo(design - jitters * Jitter - Rounding), check);
            foreach (var check in Beyond)
                Assert.That(margins.GetValueOrDefault(check, float.NaN), Is.GreaterThan(JitterSlack), check);
        });
    }

    private sealed class Measurer(PartyRole player, PartyRole[] flares, PartyRole stack, Dictionary<string, float> margins)
    {
        private const float BaitsDropAt = 6.9f;

        private SimWorld? world;
        private readonly HashSet<(float, uint, Placement)> seen = [];
        private int baits, spreads, flaresLanded;
        private Vector2? playerAtTheDrop;
        private PartyRole? stackLandedOn;

        public void Tick(ScenarioProbe p)
        {
            world ??= p.World;
            if (p.Crossed(BaitsDropAt)) playerAtTheDrop = Flat(p.Member(player)!.Position);
            foreach (var bot in Bots())
                Note("every bot inside the arena", Geometry.ArenaRadius - At(bot).Length());
        }

        private IEnumerable<PartyRole> Bots() => PerRole.All.Where(r => r != player && world!.Party.Get(r).IsAlive());

        private Vector2 At(PartyRole role) => Flat(world!.Party.Get(role)!.Position);

        private void Note(string check, float clearance)
            => margins[check] = MathF.Min(margins.GetValueOrDefault(check, float.MaxValue), clearance);

        // Every area check raises Evaluated: the scenario's own and each cast's as it resolves, with the same area.
        public void OnHit(AoeQuery hit)
        {
            if (world is null) return;
            if (!seen.Add((world.Events.Elapsed, hit.ActionId, hit.Source))) return;
            var source = Flat(hit.Source.Position);
            switch (hit.ActionId)
            {
                case P6ActionId.CosmoMeteorBait:
                    baits++;
                    var playersBait = playerAtTheDrop is { } dropped && Vector2.Distance(source, dropped) < 0.05f;
                    foreach (var bot in Bots())
                        Note(playersBait ? "every bot outside the player's bait" : "every bot outside every bait as it decides", Clearance(hit, At(bot)));
                    break;
                case P6ActionId.CosmoMeteorSpread:
                    spreads++;
                    var spreadTarget = TargetOf(source);
                    foreach (var bot in Bots().Where(b => b != spreadTarget))
                        Note(spreadTarget == player ? "every bot outside the player's spread" : "every bot outside every other bot's spread", Clearance(hit, At(bot)));
                    break;
                case P6ActionId.CosmoMeteorStack:
                    stackLandedOn = TargetOf(source);
                    foreach (var bot in Bots())
                        if (flares.Contains(bot)) Note("every bot with a flare outside the stack", Clearance(hit, At(bot)));
                        else Note("every bot without a flare inside the stack", -Clearance(hit, At(bot)));
                    break;
                case P6ActionId.CosmoMeteorFlare:
                    flaresLanded++;
                    var flare = TargetOf(source);
                    foreach (var bot in Bots().Where(b => b != flare))
                        Note(flare == player ? "every bot outside the player's flare kill radius" : "every bot outside every other bot's flare kill radius",
                             Vector2.Distance(At(bot), source) - P6Geometry.FlareLethalWithin);
                    break;
            }
        }

        private static float Clearance(AoeQuery hit, Vector2 at)
            => hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? throw new InvalidOperationException($"no area for action {hit.ActionId}");

        private PartyRole TargetOf(Vector2 source)
            => PerRole.All.Where(r => world!.Party.Get(r).IsAlive()).MinBy(r => Vector2.Distance(At(r), source));

        // One area per bait, spread and flare: the scenario's own check and the cast's are the same area.
        public IEnumerable<string> Finish()
        {
            if (baits != 8) yield return $"{baits} baits decided, not 8";
            if (spreads != 16) yield return $"{spreads} spreads landed, not 16";
            if (flaresLanded != 3) yield return $"{flaresLanded} flares landed, not 3";
            if (stackLandedOn != stack) yield return $"the stack landed on {stackLandedOn?.ToString() ?? "nobody"}, not {stack}";
        }
    }
}
