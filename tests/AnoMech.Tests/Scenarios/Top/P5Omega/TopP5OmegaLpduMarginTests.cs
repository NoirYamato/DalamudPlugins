using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Omega;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP5OmegaLpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the LPDU spots keep, measured on every bot against
// the scenario's own hit areas (AoeQuery) as each one decides who it hits, and against the picks each Hello World jump,
// the monitors and the Blaster tethers make. Each case plays every Omega pair with its first cannons; every pair plays
// several times with the player in another seat each time, and the runs share out both monitor halves, all four beetles
// and every set and seating of the Hello World jobs, so every role is a bot under every pair and holds every job of
// each Hello World as a bot under every half and every beetle. The player's seat is left out of the measures: under the
// debug driver it walks by movement input and can stop a frame's step past its spot, and in play it is a human, whom
// TopP5OmegaLpduHumanTests covers. The hits centred on the player and the jumps it takes part in have rows of their own,
// which allow it that step on top of its jitter.
public class TopP5OmegaLpduMarginTests
{
    // Each pair plays this many times, the player in another seat and on another seed each time; MarginSeeds asks for more.
    private static int RunsPerPair => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 4));

    // A bot lands anywhere within AiManager's default jitter of its spot, or 0.1 y for the first Hello World; the middle
    // takes no jitter, and the walks out of it head for jittered spots. The player can stop a sprinting frame's step past
    // its own jittered spot. The design's table is rounded to 0.01.
    private const float Jitter = 0.3f, FirstHelloWorldJitter = 0.1f, LeavingTheMiddle = 0.05f, Rounding = 0.02f;
    private const float PlayerStep = AiManager.SprintSpeed * ScenarioRun.FrameSeconds;

    // The design's clearance at the exact spots, less the jitter each check can lose: one bot's against a fixed hit, two
    // where a hit is centred on a bot, four for a jump's pick (the bot it jumps from counts twice).
    //   The stacks: Legs keeps 2.5 y out, 1.77 y off F's line (Superliminal Steel leaves 3.21 y either side of it), 11.9 y
    //   from M with Sword and 8.42 y with Shield; 30 degrees off the cannons, 1.25 y. Staff and Shield stands 10 y out,
    //   7.07 y off F's line (Optimized Blizzard III's arm along it reaches 5 y either side), 7.65 y from M; Staff and Sword
    //   17 y out, 12.19 y from M.
    //   The first Hello World: the monitor holders 10 y into their half, everyone else 0.2 y out of it. The Distant World
    //   10.2 y from the nearer monitor; Attack 4 9 y from the Distant World, whose puddle is 8 y. Near jump 1 from (10, 0):
    //   Attacks 2 and 3 at 9.85 y, the Distant World at 14 y; jump 2 from one to the other at 8 y, the Near World at 9.85 y.
    //   Distant jump 1 from (0.2, 10): Attack 1 at 29 y, Attack 2 at 23.44 y; jump 2 from Attack 1: Attack 4 at 38 y,
    //   the south monitor at 30.74 y.
    //   The second Hello World, the beetle north: the others 3 y behind the tether takers in the middle (a pass needs
    //   0.1 y ahead). Bind 2's Blaster 16.51 y from the Distant World. The Distant World 9 y from Attack 4. Near jump 1
    //   from (0, 10): Attacks 2 and 3 at 9.85 y, the Distant World at 14.14 y; jump 2 as in the first. Distant jump 1 from
    //   (-10, 0): Attack 1 at 29 y, Bind 1 at 25.54 y; jump 2 from Attack 1: Attack 4 at 38 y, Bind 2 at 32.93 y.
    //   The Attacks on the rim stand 19.42 y out.
    private static readonly (string Check, float Design, float Allowance)[] Table =
    [
        ("every bot outside Optimized Blizzard III", 2.07f, Jitter),
        ("every bot outside Efficient Bladework", 1.90f, Jitter),
        ("every bot inside Beyond Strength's safe circle", 1.58f, Jitter),
        ("every bot outside Superliminal Steel", 1.44f, Jitter),
        ("every bot outside the Diffuse Wave Cannons", 1.25f, Jitter),
        ("the monitor holders inside the monitors' half", 10f, FirstHelloWorldJitter),
        ("every other bot outside the monitors' half", 0.2f, FirstHelloWorldJitter),
        ("every bot but its target outside a monitor", 3.2f, 2 * FirstHelloWorldJitter),
        ("first Hello World: every bot but its target outside a puddle", 1f, 2 * FirstHelloWorldJitter),
        ("first Hello World: Near jump 1 goes to Attack 2 or 3 by", 4.15f, 4 * FirstHelloWorldJitter),
        ("first Hello World: Near jump 2 goes to the other of Attacks 2 and 3 by", 1.85f, 4 * FirstHelloWorldJitter),
        ("first Hello World: Distant jump 1 goes to Attack 1 by", 5.56f, 4 * FirstHelloWorldJitter),
        ("first Hello World: Distant jump 2 goes to Attack 4 by", 7.26f, 4 * FirstHelloWorldJitter),
        ("every bot but the takers off the tethers' paths once the takers leave the middle", 3.1f, LeavingTheMiddle),
        ("every bot but its target outside a Blaster", 1.51f, 2 * Jitter),
        ("second Hello World: every bot but its target outside a puddle", 1f, 2 * Jitter),
        ("second Hello World: Near jump 1 goes to Attack 2 or 3 by", 4.29f, 4 * Jitter),
        ("second Hello World: Near jump 2 goes to the other of Attacks 2 and 3 by", 1.85f, 4 * Jitter),
        ("second Hello World: Distant jump 1 goes to Attack 1 by", 3.46f, 4 * Jitter),
        ("second Hello World: Distant jump 2 goes to Attack 4 by", 5.07f, 4 * Jitter),
        ("every bot inside the arena", 0.58f, Jitter),
        ("every bot outside the player's monitor", 3.2f, 2 * FirstHelloWorldJitter + PlayerStep),
        ("first Hello World: every bot outside the player's puddle", 1f, 2 * FirstHelloWorldJitter + PlayerStep),
        ("first Hello World: a jump the player takes part in goes where it should by", 1.85f, 4 * FirstHelloWorldJitter + 2 * PlayerStep),
        ("every bot outside the player's Blaster", 1.51f, 2 * Jitter + PlayerStep),
        ("second Hello World: every bot outside the player's puddle", 1f, 2 * Jitter + PlayerStep),
        ("second Hello World: a jump the player takes part in goes where it should by", 1.85f, 4 * Jitter + 2 * PlayerStep),
    ];

    // Seconds to spare, which must be positive.
    private static readonly string[] Timing =
    [
        "every bot standing still at each Omega attack and cannon",
        "the tethers on their takers before the takers leave the middle",
    ];

    private const float TethersAt = 45.35f, TakersLeaveAt = 53f, BlasterAt = 57.58f, SecondHelloWorldFrom = 50f;
    private const float RunSpeed = 6.5f;

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
    public void KeepsTheLpduClearancesForEveryOmegaPair(bool firstCannonsFireNorthAndSouth)
    {
        var pairs = OmegaPairs.All.Where(o => o.FirstCannonsFireNorthAndSouth == firstCannonsFireNorthAndSouth).ToArray();
        var margins = new Dictionary<string, (float Clearance, string Where)>();
        var problems = new List<string>();
        var pairsAsBots = new HashSet<(int Pair, PartyRole Role)>();
        var firstJobsAsBots = new HashSet<(MonitorSide Monitors, int Job, PartyRole Role)>();
        var secondJobsAsBots = new HashSet<(Direction Beetle, int Job, PartyRole Role)>();
        var runs = pairs.Length * RunsPerPair;
        for (var n = 0; n < runs; n++)
        {
            var pair = n % pairs.Length;
            var player = PerRole.All[(n + 5 * (n / pairs.Length)) % 8];
            var roll = Roll(pairs[pair], n % 192);
            var seed = 7919 * n + (firstCannonsFireNorthAndSouth ? 1 : 2);
            var where = $"{roll}, player {player}, seed {seed}";
            var measurer = new Measurer(roll, player, where, margins);
            ScenarioRun run;
            AoeQuery.Evaluated += measurer.OnHit;
            try
            {
                run = ScenarioRun.Execute(typeof(TopP5OmegaScenario), LpduStrat, seed, new ScenarioRunOptions
                {
                    PlayerRole = player,
                    Overrides = o => roll.Pin((TopP5OmegaStateOverrides)o),
                    Probe = measurer.Tick,
                });
            }
            finally
            {
                AoeQuery.Evaluated -= measurer.OnHit;
            }
            if (!run.Passed) problems.Add($"{where}: {run}");
            problems.AddRange(measurer.Finish().Select(problem => $"{where}: {problem}"));
            foreach (var role in PerRole.All.Where(r => r != player))
            {
                pairsAsBots.Add((pair, role));
                firstJobsAsBots.Add((roll.Monitors, Array.IndexOf(roll.FirstOrder, role), role));
                secondJobsAsBots.Add((roll.Beetle, Array.IndexOf(roll.SecondOrder, role), role));
            }
        }

        TestContext.Out.WriteLine($"{runs} runs; the bots' tightest clearances:");
        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"  {m.Key}: {m.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({m.Value.Where})")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(pairsAsBots, Has.Count.EqualTo(8 * pairs.Length), "every role was a bot under every Omega pair");
            Assert.That(firstJobsAsBots, Has.Count.EqualTo(2 * 8 * 8), "every role held every first Hello World job as a bot under both monitor halves");
            Assert.That(secondJobsAsBots, Has.Count.EqualTo(4 * 8 * 8), "every role held every second Hello World job as a bot under every beetle");
            foreach (var (check, design, allowance) in Table)
                Assert.That(Margin(margins, check), Is.GreaterThanOrEqualTo(design - allowance - Rounding), () => Describe(margins, check));
            foreach (var check in Timing)
                Assert.That(Margin(margins, check), Is.GreaterThan(0f), () => Describe(margins, check));
        });
    }

    // The 192 Hello World layouts, in turn: the monitors' half, the beetle, the set of jobs and which seat starts the
    // seating, every seat a place on. The tethers start on two seats the layout picks.
    private static OmegaRoll Roll(OmegaPairs pairs, int layout)
    {
        var monitors = layout % 2 == 0 ? MonitorSide.Left : MonitorSide.Right;
        var beetle = Direction.Cardinal[layout / 2 % 4];
        var jobs = layout / 8 % HelloWorldJobs.Length;
        var shift = layout / 24 % 8;
        PartyRole[] byPerson = [.. Enumerable.Range(0, 8).Select(person => PerRole.All[(person + shift) % 8])];
        PartyRole[] tethers = [PerRole.All[layout % 8], PerRole.All[(layout / 8 + layout % 8 + 1) % 8]];
        if (tethers[1] == tethers[0]) tethers[1] = PerRole.All[(layout % 8 + 4) % 8];
        return new OmegaRoll(pairs, monitors, beetle, jobs, byPerson, tethers);
    }

    private static float Margin(Dictionary<string, (float Clearance, string Where)> margins, string check)
        => margins.TryGetValue(check, out var margin) ? margin.Clearance : float.NaN;

    private static string Describe(Dictionary<string, (float Clearance, string Where)> margins, string check)
        => margins.TryGetValue(check, out var margin) ? $"{check} ({margin.Where})" : $"{check} was never measured";

    private sealed class Measurer(OmegaRoll roll, PartyRole player, string where, Dictionary<string, (float Clearance, string Where)> margins)
    {
        private SimWorld? world;
        private readonly HashSet<(float, uint, Placement)> seen = [];
        private readonly Dictionary<PartyRole, float> stoppedAt = new();
        private readonly HashSet<PartyRole> walking = [];
        private readonly Dictionary<(bool Second, bool Near), List<PartyRole>> chains = new();
        private readonly List<PartyRole> monitored = [], blasted = [];
        private readonly List<string> problems = [];
        private float lastOffTheTakers = TethersAt;
        private bool takersLeft, tethersLeft;

        public void Tick(ScenarioProbe p)
        {
            world ??= p.World;
            foreach (var bot in Bots())
            {
                Note("every bot inside the arena", Geometry.ArenaRadius - At(bot).Length());
                if (world.Party.Get(bot)!.MoveDestination is not null) walking.Add(bot);
                else if (walking.Remove(bot)) stoppedAt[bot] = p.Time;
            }
            if (p.Time > TethersAt && p.Time < BlasterAt) WatchTheTethers(p.Time);
        }

        private IEnumerable<PartyRole> Bots() => PerRole.All.Where(r => r != player && world!.Party.Get(r).IsAlive());

        private Vector2 At(PartyRole role) => Flat(world!.Party.Get(role)!.Position);

        private PartyRole RoleOf(SimCharacter? member) => PerRole.All.FirstOrDefault(r => ReferenceEquals(world!.Party.Get(r), member));

        private void Note(string check, float clearance)
        {
            if (margins.TryGetValue(check, out var before) && before.Clearance <= clearance) return;
            margins[check] = (clearance, $"t={world!.Events.Elapsed.ToString("F2", CultureInfo.InvariantCulture)}, {where}");
        }

        // A tether goes to whoever stands in its path ahead of its holder, 0.1 y on toward the beetle and within 0.5 y of
        // the line. Before the takers leave the middle it may change hands; from then on it must stay with them.
        private void WatchTheTethers(float now)
        {
            var tethers = world!.Children.OfType<SimTether>().Where(t => t.TetherId == TetherId.PassableTether && t.IsActive).ToList();
            var takers = roll.SecondOrder[2..4];
            var onTheTakers = tethers.Count == 2 && takers.All(taker => tethers.Any(t => RoleOf(t.A) == taker));
            if (now < TakersLeaveAt)
            {
                if (!onTheTakers) lastOffTheTakers = now;
                return;
            }
            if (!takersLeft)
            {
                takersLeft = true;
                Note("the tethers on their takers before the takers leave the middle", TakersLeaveAt - lastOffTheTakers);
            }
            if (!onTheTakers)
            {
                if (!tethersLeft) problems.Add($"t={now:F2} the tethers were on {string.Join(", ", tethers.Select(t => RoleOf(t.A)))}");
                tethersLeft = true;
                return;
            }
            foreach (var tether in tethers.Where(t => RoleOf(t.A) != player))
            foreach (var bot in Bots().Where(b => !takers.Contains(b)))
                Note("every bot but the takers off the tethers' paths once the takers leave the middle", OffThePath(Flat(tether.A!.Position), Flat(tether.B!.Position), At(bot)));
        }

        private static float OffThePath(Vector2 holder, Vector2 beetle, Vector2 at)
        {
            var length = Vector2.Distance(holder, beetle);
            var ahead = (beetle - holder) / length;
            var forward = Vector2.Dot(at - holder, ahead);
            var across = MathF.Abs(ahead.X * (at - holder).Y - ahead.Y * (at - holder).X);
            var dx = MathF.Max(MathF.Max(0.1f - forward, forward - length), 0f);
            var dy = MathF.Max(across - 0.5f, 0f);
            return dx == 0f && dy == 0f ? -MathF.Min(MathF.Min(forward - 0.1f, length - forward), 0.5f - across) : MathF.Sqrt(dx * dx + dy * dy);
        }

        // Every area check raises Evaluated, and a resolve checks the party and then the NPCs with the same query.
        public void OnHit(AoeQuery hit)
        {
            if (world is null) return;
            if (!seen.Add((world.Events.Elapsed, hit.ActionId, hit.Source))) return;
            var source = Flat(hit.Source.Position);
            switch (hit.ActionId)
            {
                case ActionId.OptimizedBlizzardIII:
                    TheStack(hit, "every bot outside Optimized Blizzard III");
                    break;
                case ActionId.EfficientBladework:
                    TheStack(hit, "every bot outside Efficient Bladework");
                    break;
                case ActionId.BeyondStrength:
                    TheStack(hit, "every bot inside Beyond Strength's safe circle");
                    break;
                case ActionId.SuperliminalSteelOmenL or ActionId.SuperliminalSteelOmenR:
                    TheStack(hit, "every bot outside Superliminal Steel");
                    break;
                case ActionId.OmegaDiffuseWaveCannonAOE:
                    TheStack(hit, "every bot outside the Diffuse Wave Cannons");
                    break;
                case ActionId.OversampledWaveCannonAoe:
                    if (monitored.Count == 0) TheMonitorsHalf();
                    var monitorTarget = TargetOf(source);
                    monitored.Add(monitorTarget);
                    Around(hit, monitorTarget, monitorTarget == player ? "every bot outside the player's monitor" : "every bot but its target outside a monitor");
                    break;
                case ActionId.HelloNearWorld or ActionId.HelloNearWorldJump or ActionId.HelloDistantWorld or ActionId.HelloDistantWorldJump:
                    var second = world.Events.Elapsed > SecondHelloWorldFrom;
                    var puddleTarget = Chain(second, hit.ActionId is ActionId.HelloNearWorld or ActionId.HelloNearWorldJump, source);
                    Around(hit, puddleTarget, $"{(second ? "second" : "first")} Hello World: "
                                              + (puddleTarget == player ? "every bot outside the player's puddle" : "every bot but its target outside a puddle"));
                    break;
                case ActionId.BlasterAoe:
                    var blasterTarget = TargetOf(source);
                    blasted.Add(blasterTarget);
                    Around(hit, blasterTarget, blasterTarget == player ? "every bot outside the player's Blaster" : "every bot but its target outside a Blaster");
                    break;
            }
        }

        // A bot still walking counts the time it needs to get there, as a negative.
        private void TheStack(AoeQuery hit, string check)
        {
            var now = world!.Events.Elapsed;
            foreach (var bot in Bots())
            {
                Note(check, Clearance(hit, At(bot)));
                var spare = world.Party.Get(bot)!.MoveDestination is { } to
                    ? -Vector2.Distance(At(bot), Flat(to)) / RunSpeed
                    : now - stoppedAt.GetValueOrDefault(bot, 0f);
                Note("every bot standing still at each Omega attack and cannon", spare);
            }
        }

        private void Around(AoeQuery hit, PartyRole target, string check)
        {
            foreach (var bot in Bots().Where(b => b != target)) Note(check, Clearance(hit, At(bot)));
        }

        // The monitors take two of those on their half, at random: their holders deep in it, everyone else out of it.
        private void TheMonitorsHalf()
        {
            var west = roll.Monitors == MonitorSide.Left ? 1f : -1f;
            foreach (var bot in Bots())
                if (roll.FirstOrder[2..4].Contains(bot)) Note("the monitor holders inside the monitors' half", -At(bot).X * west);
                else Note("every other bot outside the monitors' half", At(bot).X * west);
        }

        private PartyRole Chain(bool second, bool near, Vector2 source)
        {
            if (!chains.TryGetValue((second, near), out var chain)) chains[(second, near)] = chain = [];
            var target = TargetOf(source);
            if (chain.Count > 0)
            {
                var order = second ? roll.SecondOrder : roll.FirstOrder;
                var last = chain[^1];
                var intended = JumpTargets(near, order, chain);
                var candidates = PerRole.All.Where(r => r != last && world!.Party.Get(r).IsAlive()).Select(r => (r, At(r)));
                var (margin, runnerUp) = JumpPick(near, At(last), candidates, intended);
                var jump = last == player || runnerUp == player || intended.Contains(player)
                    ? "a jump the player takes part in goes where it should by"
                    : $"{(near ? "Near" : "Distant")} jump {chain.Count} goes to "
                      + (near ? chain.Count == 1 ? "Attack 2 or 3 by" : "the other of Attacks 2 and 3 by" : chain.Count == 1 ? "Attack 1 by" : "Attack 4 by");
                Note($"{(second ? "second" : "first")} Hello World: {jump}", margin);
            }
            chain.Add(target);
            return target;
        }

        private PartyRole TargetOf(Vector2 source)
            => PerRole.All.Where(r => world!.Party.Get(r).IsAlive()).MinBy(r => Vector2.Distance(At(r), source));

        private static float Clearance(AoeQuery hit, Vector2 at)
            => hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? throw new InvalidOperationException($"no area for action {hit.ActionId}");

        // Each Hello World goes Near holder, Attacks 2 and 3 either way round, and Distant holder, Attack 1, Attack 4; the
        // monitors hit their two holders and the Blasters their two takers.
        public IEnumerable<string> Finish()
        {
            if (world is null) return ["the run never started"];
            foreach (var (second, order) in new[] { (false, roll.FirstOrder), (true, roll.SecondOrder) })
            {
                var name = second ? "second" : "first";
                var near = chains.GetValueOrDefault((second, true), []);
                var distant = chains.GetValueOrDefault((second, false), []);
                if (near.Count != 3 || near[0] != order[0] || !near.Skip(1).Order().SequenceEqual(new[] { order[5], order[6] }.Order()))
                    problems.Add($"the {name} Hello Near World went {string.Join(", ", near)}");
                if (!distant.SequenceEqual([order[1], order[4], order[7]]))
                    problems.Add($"the {name} Hello Distant World went {string.Join(", ", distant)}");
            }
            if (!monitored.Order().SequenceEqual(roll.FirstOrder[2..4].Order()))
                problems.Add($"the monitors hit {string.Join(", ", monitored)}");
            if (!blasted.Order().SequenceEqual(roll.SecondOrder[2..4].Order()))
                problems.Add($"the Blasters hit {string.Join(", ", blasted)}");
            return problems;
        }
    }
}
