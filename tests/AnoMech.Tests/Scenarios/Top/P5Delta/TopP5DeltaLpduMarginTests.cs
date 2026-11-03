using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Delta;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP5DeltaLpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the LPDU spots keep, measured on every bot
// against the scenario's own hit areas (AoeQuery) as each one resolves. Each case plays every arm-turn pattern with both
// sides of the Final's monitor and of the player's monitor, and every combination of fist flexes. With the Swivel
// Cannon Left the monitor holder takes Beyond Defense; with it Right the holder is the other inner blue or an outer
// blue. The player's seat is left out of the measures (under the debug driver it walks by movement input and can stop
// a frame's step past its spot; TopP5DeltaLpduHumanTests plays it as a human), so it is seated away from the run's
// Beyond Defense target and monitor holder. Who each hit lands on and when each tether pops are checked on everyone.
public class TopP5DeltaLpduMarginTests
{
    private const int Layouts = 256;

    // A bot lands anywhere within AiManager's default jitter of its spot; the rows are rounded to 0.01.
    private const float Jitter = 0.3f, Rounding = 0.02f;

    // The least each row keeps at the LPDU spots, less the jitter the check can lose: one bot's against a fixed hit,
    // two between bots. The monitor stage is walked to without jitter.
    private static readonly (string Check, float Required, float Jitters)[] Table =
    [
        ("everyone outside the Optical Laser", 1.5f, 1f),
        ("a fist partner inside the pairing circle", 2 * Jitter, 2f),
        ("every other fist outside the pairing circle", 2 * Jitter, 2f),
        ("the inner blue pair apart as the tethers go live", 12.5f, 2f),
        ("the outer blue pair within 6 y until it leaves for the arms", 0f, 2f),
        ("each green pair apart until its planned break", 12f, 2f),
        ("the plain blues outside the holder's monitor half", 1.5f, 0f),
        ("every spot outside the Swivel Cannon", 0.6f, 1f),
        ("everyone inside the arena", 0.5f, 1f),
    ];

    // Off the table, with the slack the P3 and P4 margin tests ask for. A Hyper Pulse line is checked against where
    // each bot stands as it fires and against every step walked in the 0.6 s before, the time between two lines.
    private const float JitterSlack = 0.5f, PathWindow = 0.6f;

    private static readonly string[] Beyond =
    [
        "every bot outside every Hyper Pulse line as it fires",
        "every walking path outside every Hyper Pulse line in the 0.6 s before it fires",
        "the blues outside the Final's monitor half",
        "the Beyond Defense target outside every monitor half",
        "nobody but its target in a monitor's circle",
        "the stack inside Pile Pitch",
        "everyone else outside Pile Pitch",
        "nobody but its target in a Hello World circle",
    ];

    // Seconds to spare, which must be positive.
    private static readonly string[] Timing =
    [
        "the stretched inner blues pop as the tethers go live",
        "the outer blues pop half a second after the first break's stacks end",
        "the first green break after every Twice-come Ruin",
        "the last green break after every Hello World vulnerability",
    ];

    // The stretched pair pops on the frame its tether goes live at 28.1; Pile Pitch's Twice-come Ruin, the last, wears
    // off at 47.98.
    private const float InnerBluesPopBy = 28.25f, BreakGapSlack = 0.5f;
    private const float LastRuinAt = 47.98f;

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

    private static IEnumerable<TestCaseData> EyesAndSwivels()
        => from eyeNorth in new[] { true, false }
           from swivelLeft in new[] { true, false }
           select new TestCaseData(eyeNorth, swivelLeft)
               .SetName($"{{m}}(eye {(eyeNorth ? "N" : "S")}, Swivel Cannon {(swivelLeft ? "Left" : "Right")})");

    [TestCaseSource(nameof(EyesAndSwivels))]
    public void KeepsTheLpduClearancesOnEveryLayout(bool eyeNorth, bool swivelLeft)
    {
        var margins = new Dictionary<string, float>();
        var reported = new Dictionary<string, float>();
        var problems = new List<string>();
        var flexes = new HashSet<(bool Blue, bool Green)>();
        for (var index = 0; index < Layouts; index++)
        {
            var layout = Layout.For(eyeNorth, swivelLeft, index);
            var measurer = new Measurer(layout, margins, reported);
            ScenarioRun run;
            AoeQuery.Evaluated += measurer.OnHit;
            try
            {
                run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), LpduStrat, layout.Seed, new ScenarioRunOptions
                {
                    PlayerRole = layout.Player,
                    Overrides = o => layout.Pin((TopP5DeltaStateOverrides)o),
                    Probe = measurer.Tick,
                });
            }
            finally
            {
                AoeQuery.Evaluated -= measurer.OnHit;
            }
            if (!run.Passed) problems.Add($"{layout}: {run}");
            problems.AddRange(measurer.Finish().Select(problem => $"{layout}: {problem}"));
            flexes.Add(layout.Flexes);
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value.ToString("F2", CultureInfo.InvariantCulture)}")));
        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            reported.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value.ToString("F2", CultureInfo.InvariantCulture)}")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(40)));
            Assert.That(flexes, Has.Count.EqualTo(4), "both flex outcomes of each colour");
            foreach (var (check, required, jitters) in Table)
                Assert.That(margins.GetValueOrDefault(check, float.NaN), Is.GreaterThanOrEqualTo(required - jitters * Jitter - Rounding), check);
            foreach (var check in Beyond)
                Assert.That(margins.GetValueOrDefault(check, float.NaN), Is.GreaterThan(JitterSlack), check);
            foreach (var check in Timing)
                Assert.That(margins.GetValueOrDefault(check, float.NaN), Is.GreaterThan(0f), check);
        });
    }

    // Positions 0-7 of AfterTheFlex are the strat's: inner blue N/S, outer blue N/S, inner green N/S, outer green N/S.
    internal sealed record Layout(
        bool EyeNorth, Side Swivel, Side OmegaMonitor, Side PlayerMonitor, ArmRotation[] Arms, PartyRole[] TetherOrder,
        uint[] FistColors, PartyRole[] AfterTheFlex, (bool Blue, bool Green) Flexes, PartyRole Holder, PartyRole BeyondDefense,
        PartyRole Refuses, PartyRole Player, int Seed)
    {
        public NorthSouth Eye => EyeNorth ? NorthSouth.North : NorthSouth.South;

        public static Layout For(bool eyeNorth, bool swivelLeft, int index)
        {
            var arms = Enumerable.Range(0, 6).Select(arm => (index >> arm & 1) == 0 ? ArmRotation.CounterClockwise : ArmRotation.Clockwise).ToArray();
            var omegaMonitor = (index >> 6 & 1) == 0 ? Side.Left : Side.Right;
            var playerMonitor = (index >> 7 & 1) == 0 ? Side.Left : Side.Right;
            var seed = 7919 * index + (eyeNorth ? 1 : 4) + (swivelLeft ? 0 : 2);
            var rng = new Random(seed);
            PartyRole[] roll = [.. PerRole.All.OrderBy(_ => rng.Next())];
            var lineUp = ExpectedLineUp(roll, false, false);

            var flexes = (index + index / 64) % 4;
            var byPosition = new uint[8];
            foreach (var (inner, flex) in new[] { (0, (flexes & 1) != 0), (4, (flexes & 2) != 0) })
            {
                var innerNorth = rng.Next(2) == 0 ? Yellow : Blue;
                byPosition[inner] = innerNorth;
                byPosition[inner + 1] = Other(innerNorth);
                byPosition[inner + 2] = flex ? innerNorth : Other(innerNorth);
                byPosition[inner + 3] = Other(byPosition[inner + 2]);
            }
            uint FistOfRole(PartyRole role) => byPosition[Array.IndexOf(lineUp, role)];
            var afterTheFlex = ExpectedAfterTheFlex(lineUp, FistOfRole);

            var beyondDefenseNorth = (index / 2 + index / 64) % 2 == 0;
            var beyondDefense = afterTheFlex[beyondDefenseNorth ? 0 : 1];
            var refuses = afterTheFlex[beyondDefenseNorth ? 1 : 0];
            var (holder, player) = swivelLeft ? (beyondDefense, afterTheFlex[4 + index % 4])
                : (index / 4 + index / 64) % 2 == 0 ? (refuses, afterTheFlex[2 + index / 2 % 2])
                : (afterTheFlex[2 + index / 8 % 2], afterTheFlex[4 + index / 2 % 4]);
            return new Layout(eyeNorth, swivelLeft ? Side.Left : Side.Right, omegaMonitor, playerMonitor, arms, roll,
                              [.. roll.Select(FistOfRole)], afterTheFlex, ((flexes & 1) != 0, (flexes & 2) != 0), holder,
                              beyondDefense, refuses, player, seed);
        }

        private static uint Other(uint fist) => fist == Yellow ? Blue : Yellow;

        public void Pin(TopP5DeltaStateOverrides o)
        {
            o.EyeSpawn = Eye;
            o.SwivelCannonSide = Swivel;
            o.OmegaMonitorSide = OmegaMonitor;
            o.PlayerMonitorSide = PlayerMonitor;
            o.ArmRotations = Arms;
            o.TetherOrder = TetherOrder;
            o.FistColors = FistColors;
            o.Monitor[Holder] = true;
            o.BeyondDefence[Refuses] = false;
        }

        // The unsafe-side pair member of a colour's inner (4) or outer (6) greens, and its safe-side partner.
        public PartyRole UnsafeSide(int north) => AfterTheFlex[Swivel.Mul > 0 ? north : north + 1];

        public PartyRole SafeSide(int north) => AfterTheFlex[Swivel.Mul > 0 ? north + 1 : north];

        public override string ToString()
            => $"eye {(EyeNorth ? "N" : "S")}, Swivel {Name(Swivel)}, Final's monitor {Name(OmegaMonitor)}, player monitor {Name(PlayerMonitor)}, "
               + $"arms {string.Concat(Arms.Select(a => a == ArmRotation.Clockwise ? "C" : "A"))}, order {string.Join(" ", AfterTheFlex)}, "
               + $"holder {Holder}, Beyond Defense {BeyondDefense}, player {Player}, seed {Seed}";

        private static string Name(Side side) => side == Side.Left ? "L" : "R";
    }

    private sealed class Measurer(Layout layout, Dictionary<string, float> margins, Dictionary<string, float> reported)
    {
        private SimWorld? world;
        private TopP5DeltaState? state;
        private readonly List<(float At, Dictionary<PartyRole, Vector2> Bots)> recent = [];
        private readonly Dictionary<PartyRole, float> brokeAt = new();
        private float lastRuin = float.NegativeInfinity, lastHelloVulnerability = float.NegativeInfinity;
        private readonly List<PartyRole> near = [], distant = [], monitored = [];
        private PartyRole? pilePitchTarget;
        private readonly HashSet<(float, uint, Placement)> seen = [];

        private const float TethersLiveAt = 28.1f, FistsSnapshotAt = 30.1f, OuterBluesLeaveAt = 30.3f;
        private const float InnerGreensCollapseAt = 48f, OuterGreensWalkTogetherAt = 61.2f, HelloHitsFrom = 54f;

        public void Tick(ScenarioProbe p)
        {
            world ??= p.World;
            state ??= Scenario(p).LastState;
            var now = p.Time;
            var bots = Bots().ToDictionary(r => r, At);
            recent.Add((now, bots));
            recent.RemoveAll(e => e.At < now - PathWindow - 0.1f);

            foreach (var at in bots.Values) Note("everyone inside the arena", Geometry.ArenaRadius - at.Length());
            TrackTheTethersAndDebuffs(now);

            if (p.Crossed(TethersLiveAt) && Apart(0) is { } innerBlues)
                Note("the inner blue pair apart as the tethers go live", innerBlues);
            if (now >= TethersLiveAt && now < OuterBluesLeaveAt && Apart(2) is { } outerBlues)
                Note("the outer blue pair within 6 y until it leaves for the arms", 6f - outerBlues);
            if (now >= TethersLiveAt && now < InnerGreensCollapseAt && Apart(4) is { } innerGreens)
                Note("each green pair apart until its planned break", innerGreens);
            if (now >= TethersLiveAt && now < OuterGreensWalkTogetherAt && Apart(6) is { } outerGreens)
                Note("each green pair apart until its planned break", outerGreens);
            if (p.Crossed(FistsSnapshotAt)) MeasureTheFistStacks(bots);
        }

        private IEnumerable<PartyRole> Bots() => PerRole.All.Where(r => r != layout.Player && world!.Party.Get(r).IsAlive());

        private Vector2 At(PartyRole role) => Flat(world!.Party.Get(role)!.Position);

        // How far apart a tether pair stands, when both are bots.
        private float? Apart(int north)
        {
            var (a, b) = (layout.AfterTheFlex[north], layout.AfterTheFlex[north + 1]);
            return a == layout.Player || b == layout.Player || !world!.Party.Get(a).IsAlive() || !world.Party.Get(b).IsAlive()
                ? null : Vector2.Distance(At(a), At(b));
        }

        private void Note(string check, float clearance)
            => margins[check] = MathF.Min(margins.GetValueOrDefault(check, float.MaxValue), clearance);

        private void Report(string what, float value)
            => reported[what] = MathF.Min(reported.GetValueOrDefault(what, float.MaxValue), value);

        private void TrackTheTethersAndDebuffs(float now)
        {
            foreach (var role in PerRole.All)
            {
                if (world!.Party.Get(role) is not { } member || !member.IsAlive()) continue;
                if (member.HasStatus(StatusId.TwiceComeRuin)) lastRuin = now;
                if (now >= HelloHitsFrom && member.HasStatus(StatusId.MagicVulnerabilityUp)) lastHelloVulnerability = now;
                var regression = Array.IndexOf(layout.TetherOrder, role) < 4 ? StatusId.HWRemoteTether : StatusId.HWLocalTether;
                if (now >= TethersLiveAt && !brokeAt.ContainsKey(role) && !member.HasStatus(regression)) brokeAt[role] = now;
            }
        }

        // Each stack is an inner and an outer player of one colour on one side: positions 0 and 2, 1 and 3, 4 and 6, 5 and 7.
        private void MeasureTheFistStacks(Dictionary<PartyRole, Vector2> bots)
        {
            foreach (var (role, at) in bots)
            {
                var partner = layout.AfterTheFlex[Array.IndexOf(layout.AfterTheFlex, role) ^ 2];
                if (bots.TryGetValue(partner, out var partnerAt))
                    Note("a fist partner inside the pairing circle", Geometry.RocketPunchAoeRadius - Vector2.Distance(at, partnerAt));
                foreach (var (other, otherAt) in bots.Where(b => b.Key != role && b.Key != partner))
                    Note("every other fist outside the pairing circle", Vector2.Distance(at, otherAt) - Geometry.RocketPunchAoeRadius);
            }
        }

        // Every area check raises Evaluated, and a resolve checks the party and then the NPCs with the same query.
        public void OnHit(AoeQuery hit)
        {
            if (world is null || state is null) return;
            var now = world.Events.Elapsed;
            if (!seen.Add((now, hit.ActionId, hit.Source))) return;
            var bots = Bots().ToList();
            switch (hit.ActionId)
            {
                case ActionId.OpticalLaser:
                    foreach (var bot in bots) Note("everyone outside the Optical Laser", Clearance(hit, At(bot)));
                    break;
                case ActionId.HyperPulseDeltaCharging or ActionId.HyperPulseDeltaShoot:
                    foreach (var bot in bots) Note("every bot outside every Hyper Pulse line as it fires", Clearance(hit, At(bot)));
                    foreach (var (_, walked) in recent.Where(e => e.At >= now - PathWindow))
                    foreach (var at in walked.Values)
                        Note("every walking path outside every Hyper Pulse line in the 0.6 s before it fires", Clearance(hit, at));
                    if (TheBeyondDefenseHolderStepsOutward() is { } stepped && bots.Contains(layout.Holder))
                        Report($"the Beyond Defense target holding the monitor, stepped {stepped}, to a Hyper Pulse line", Clearance(hit, At(layout.Holder)));
                    break;
                case ActionId.OversampledWaveCannonAoe:
                    var monitorTarget = TargetOf(hit);
                    if (monitored.Count == 0) MeasureTheMonitorHalves(bots);
                    monitored.Add(monitorTarget);
                    foreach (var bot in bots.Where(b => b != monitorTarget)) Note("nobody but its target in a monitor's circle", Clearance(hit, At(bot)));
                    break;
                case ActionId.PilePitch:
                    pilePitchTarget = TargetOf(hit);
                    foreach (var bot in bots)
                        if (TheStack().Contains(bot)) Note("the stack inside Pile Pitch", -Clearance(hit, At(bot)));
                        else Note("everyone else outside Pile Pitch", Clearance(hit, At(bot)));
                    break;
                case ActionId.SwivelCannonL or ActionId.SwivelCannonR:
                    foreach (var bot in bots) Note("every spot outside the Swivel Cannon", Clearance(hit, At(bot)));
                    break;
                case ActionId.HelloNearWorld or ActionId.HelloNearWorldJump:
                    Chain(hit, near, "Near", bots);
                    break;
                case ActionId.HelloDistantWorld or ActionId.HelloDistantWorldJump:
                    Chain(hit, distant, "Distant", bots);
                    break;
            }
        }

        private string? TheBeyondDefenseHolderStepsOutward()
            => layout.Holder != layout.BeyondDefense ? null
                : -layout.PlayerMonitor.Mul * -layout.OmegaMonitor.Mul > 0 ? "toward the Final" : "back toward the centre";

        private static float Clearance(AoeQuery hit, Vector2 at)
            => hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? throw new InvalidOperationException($"no area for action {hit.ActionId}");

        private PartyRole TargetOf(AoeQuery hit)
            => PerRole.All.Where(r => world!.Party.Get(r).IsAlive())
                          .MinBy(r => Vector2.Distance(At(r), Flat(hit.Source.Position)));

        private IEnumerable<PartyRole> TheStack() => layout.AfterTheFlex.Take(4).Where(r => r != layout.BeyondDefense);

        // A monitor hits the players on its side of the line through its holder along its facing: the side
        // CharacterFind.OnSideN picks, those with the right vector's dot product times the side's sign below zero.
        private void MeasureTheMonitorHalves(List<PartyRole> bots)
        {
            var final = world!.Children.OfType<SimEnemy>().Single(e => e.SpawnConfig.BNpcBaseId == BNpcBaseId.FinalHelper).Placement();
            var holder = world.Party.Get(state!.PlayerMonitorRole)!.Placement();
            foreach (var blue in bots.Where(b => Array.IndexOf(layout.AfterTheFlex, b) < 4))
            {
                var outsideTheFinals = OutsideTheHalf(final, state.OmegaMonitorSide, At(blue));
                Note("the blues outside the Final's monitor half", outsideTheFinals);
                if (blue == layout.BeyondDefense)
                    Note("the Beyond Defense target outside every monitor half",
                         blue == layout.Holder ? outsideTheFinals : MathF.Min(outsideTheFinals, OutsideTheHalf(holder, state.PlayerMonitorSide, At(blue))));
                else if (blue != layout.Holder)
                    Note("the plain blues outside the holder's monitor half", OutsideTheHalf(holder, state.PlayerMonitorSide, At(blue)));
            }
        }

        private static float OutsideTheHalf(Placement monitor, Side side, Vector2 at)
        {
            var right = Flat(monitor.MoveRight(1f).Position) - Flat(monitor.Position);
            return Vector2.Dot(at - Flat(monitor.Position), right) * side.Mul;
        }

        // Each jump goes to the member nearest (Near) or farthest from (Distant) the last one hit: how far the pick won by.
        private void Chain(AoeQuery hit, List<PartyRole> chain, string name, List<PartyRole> bots)
        {
            var target = TargetOf(hit);
            if (chain.Count > 0)
            {
                var from = At(chain[^1]);
                var others = PerRole.All.Where(r => r != chain[^1] && world!.Party.Get(r).IsAlive())
                                        .Select(r => Vector2.Distance(from, At(r))).Order().ToList();
                Report($"Hello {name} World jump {chain.Count}: the pick won by",
                       name == "Near" ? others[1] - others[0] : others[^1] - others[^2]);
            }
            chain.Add(target);
            foreach (var bot in bots.Where(b => b != target)) Note("nobody but its target in a Hello World circle", Clearance(hit, At(bot)));
        }

        public IEnumerable<string> Finish()
        {
            var problems = new List<string>();
            if (state is null) return ["the run never started"];
            if (!ExpectedAfterTheFlex(state).SequenceEqual(layout.AfterTheFlex))
                problems.Add($"the layout's own flex disagrees with the rule's: {string.Join(" ", ExpectedAfterTheFlex(state))}");

            float Pair(int north) => MathF.Max(brokeAt.GetValueOrDefault(layout.AfterTheFlex[north], float.NaN),
                                               brokeAt.GetValueOrDefault(layout.AfterTheFlex[north + 1], float.NaN));
            Note("the stretched inner blues pop as the tethers go live", InnerBluesPopBy - Pair(0));
            Note("the outer blues pop half a second after the first break's stacks end", Pair(2) - Pair(0) - Duration.HwTetherBreakStack - BreakGapSlack);
            Note("the first green break after every Twice-come Ruin", Pair(4) - MathF.Max(LastRuinAt, lastRuin));
            Note("the last green break after every Hello World vulnerability", Pair(6) - lastHelloVulnerability);
            Report("the first green break, after the last Twice-come Ruin by", Pair(4) - lastRuin);

            PartyRole[] nearChain = [state.NearWorldRole, layout.UnsafeSide(4), layout.SafeSide(4)];
            PartyRole[] distantChain = [state.FarWorldRole, layout.UnsafeSide(6), layout.SafeSide(6)];
            if (!near.SequenceEqual(nearChain))
                problems.Add($"Hello Near World went {string.Join(", ", near)}, not {string.Join(", ", nearChain)}");
            if (!distant.SequenceEqual(distantChain))
                problems.Add($"Hello Distant World went {string.Join(", ", distant)}, not {string.Join(", ", distantChain)}");
            if (!monitored.Order().SequenceEqual(layout.AfterTheFlex.Skip(4).Order()))
                problems.Add($"the monitors hit {string.Join(", ", monitored)}, not the four greens");
            if (pilePitchTarget is not { } pile || !TheStack().Contains(pile))
                problems.Add($"Pile Pitch went on {pilePitchTarget?.ToString() ?? "nobody"}, outside the stack");
            if (state.BeyondDefenseTarget != layout.BeyondDefense)
                problems.Add($"Beyond Defense went on {state.BeyondDefenseTarget?.ToString() ?? "nobody"}, not {layout.BeyondDefense}");
            return problems;
        }
    }
}
