using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who
// plays the guides and nothing else: it waits on its loose-spread spot, then walks at each cue to
// its role's spot from the design table, a fixed reaction time late, and never steps round anyone.
// The bots must keep every rot off it and off themselves, for every kit in every seat.
public class TopP3HelloWorldLpduHumanTests
{
    // Ten fixed rolls per seat; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 10)).Select(i => 11 * i);
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];

    public enum Style
    {
        Straight,
        // The guides' tethered player stretches once "my rot person actually goes in their tower",
        // before the soakers have reached the spots the bots wait for.
        StretchingAsTheSoakersArrive,
        // Off the guides: drifting one to two yalms off the line, taking the guides' lanes to the
        // towers (rot holders round the outside of the boss's hitbox, tethered players round the
        // inside), sprinting everywhere, or setting off ahead of each cue as far as the guides allow.
        Curving,
        Lanes,
        Sprinting,
        Early,
    }

    private static IEnumerable<TestCaseData> Matrix(params Style[] styles)
        => from style in styles
           from kit in Enum.GetValues<HelloWorldKit>()
           from delay in ReactionTimes
           select new TestCaseData(style, kit, delay).SetName($"{{m}}({style}, {kit}, {delay:F1}s {(style == Style.Early ? "early" : "late")})");

    private static IEnumerable<TestCaseData> GuideStyles() => Matrix(Style.Straight, Style.StretchingAsTheSoakersArrive);

    private static IEnumerable<TestCaseData> OffGuideStyles() => Matrix(Style.Curving, Style.Lanes, Style.Sprinting, Style.Early);

    [TestCaseSource(nameof(GuideStyles))]
    public void AHumanPlayingTheGuidesIsNeverCaught(Style style, HelloWorldKit kit, float delay)
        => AssertNeverCaught(kit, Seeds, Human.For(style, delay));

    [TestCaseSource(nameof(OffGuideStyles))]
    public void AHumanPlayingItsOwnWayIsNeverCaught(Style style, HelloWorldKit kit, float delay)
        => AssertNeverCaught(kit, Seeds, Human.For(style, delay));

    // A pass a second behind the bots still lands inside the window, and that rot then goes off a
    // second later in the next set, or in the last set as the bots line up for Monitors.
    [TestCase(1, HelloWorldKit.RemoteFirst)]
    [TestCase(1, HelloWorldKit.LocalFirst)]
    [TestCase(2, HelloWorldKit.Defamation)]
    [TestCase(2, HelloWorldKit.Stack)]
    [TestCase(3, HelloWorldKit.LocalFirst)]
    [TestCase(3, HelloWorldKit.RemoteFirst)]
    public void PassingASecondLateKillsNobody(int set, HelloWorldKit kit)
    {
        var problems = new List<string>();
        foreach (var seed in Seeds)
        {
            var seat = PerRole.All[seed % 8];
            var game = Play(seat, kit, seed, new Human(cue => cue == (set, Step.Pass) ? 1f : 0f));
            problems.AddRange(game.Problems);
            var caught = game.CaughtRotAt.GetValueOrDefault(set);
            if (caught is null || caught <= Guide.SetStarts[set - 1] + Guide.PassAt + 1f)
                problems.Add($"seed {seed}: {seat} should have taken its rot in set {set} a second late, took it at {caught}");
        }
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    public enum Walk { Straight, Sprinting, Curving, Lanes }

    internal enum Step { Towers, Pass, RemotePops, LocalPops }

    // Late is how long after each cue it sets off; a negative one sets off ahead of the cue.
    internal sealed record Human(Func<(int Set, Step Step), float> Late, Walk Walk = Walk.Straight, float StretchWhenSoakersWithin = 1f)
    {
        public static Human For(Style style, float delay) => style switch
        {
            Style.StretchingAsTheSoakersArrive => new(_ => delay, StretchWhenSoakersWithin: 4f),
            Style.Curving => new(_ => delay, Walk.Curving),
            Style.Lanes => new(_ => delay, Walk.Lanes),
            Style.Sprinting => new(_ => delay, Walk.Sprinting),
            Style.Early => new(_ => -delay, StretchWhenSoakersWithin: 4f),
            _ => new(_ => delay),
        };
    }

    internal sealed record Outcome(List<string> Problems, Dictionary<int, float?> CaughtRotAt);

    private static void AssertNeverCaught(HelloWorldKit kit, IEnumerable<int> seeds, Human human)
    {
        var problems = (from seat in PerRole.All
                        from seed in seeds
                        from problem in Play(seat, kit, seed, human).Problems
                        select problem).ToList();
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    internal static Outcome Play(PartyRole seat, HelloWorldKit kit, int seed, Human human, bool artifacts = true, bool alwaysArtifacts = false)
    {
        var roll = Roll.For(seat, kit, seed);
        var guide = new Guide(seat, roll, human, new Random(seed * 8 + (int)seat));
        var problems = new List<string>();
        var caught = new Dictionary<int, float?>();
        var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = seat,
            WriteArtifactsOnFailure = artifacts,
            AlwaysWriteArtifacts = alwaysArtifacts,
            Overrides = o => roll.Pin((TopP3HelloWorldStateOverrides)o),
            Takeovers = [new Takeover(0f, null)],
            Probe = p =>
            {
                guide.Tick(p);
                foreach (var stray in guide.StrayRots(p)) problems.Add($"seed {seed}, {seat} ({kit}): {stray}");
                foreach (var stray in guide.BotsOffTheirTowerSpots(p)) problems.Add($"seed {seed}, {seat} ({kit}): {stray}");
                if (guide.HumanCaughtARot(p) is { } set) caught[set] = p.Time;
            },
        });
        if (!run.Passed) problems.Add($"{seat} ({kit}), {run}");
        return new Outcome(problems, caught);
    }

    // The test rolls the state from its own seed and pins it, so the human knows its partners and
    // the towers without reading the scenario.
    private sealed record Roll(PartyRole[] KitOrder, Direction[] Layouts, RotColour DefamationColour)
    {
        public static Roll For(PartyRole seat, HelloWorldKit kit, int seed)
        {
            var rng = new Random(seed);
            var others = PerRole.All.Where(r => r != seat).OrderBy(_ => rng.Next()).ToList();
            others.Insert(2 * (int)kit + rng.Next(2), seat);
            var layouts = Enumerable.Range(0, 4).Select(_ => Direction.Intercardinal[rng.Next(4)]).ToArray();
            return new Roll([.. others], layouts, rng.Next(2) == 0 ? RotColour.Red : RotColour.Blue);
        }

        public void Pin(TopP3HelloWorldStateOverrides o)
        {
            o.KitOrder = KitOrder;
            o.Layouts = Layouts;
            o.DefamationColour = DefamationColour;
        }

        public HelloWorldKit KitOf(PartyRole role) => (HelloWorldKit)(Array.IndexOf(KitOrder, role) / 2);

        public PartyRole[] Pair(HelloWorldKit kit) => [KitOrder[2 * (int)kit], KitOrder[2 * (int)kit + 1]];

        public HelloWorldRole RoleIn(int set, PartyRole role)
        {
            var first = KitOf(role) switch
            {
                HelloWorldKit.Defamation => HelloWorldRole.Defamation,
                HelloWorldKit.Stack => HelloWorldRole.Stack,
                HelloWorldKit.RemoteFirst => HelloWorldRole.Remote,
                _ => HelloWorldRole.Local,
            };
            return (HelloWorldRole)(((int)first + set - 1) % 4);
        }

        public ushort DefamationRot => DefamationColour == RotColour.Red ? P3StatusId.RedRot : P3StatusId.BlueRot;

        public ushort StackRot => DefamationColour == RotColour.Red ? P3StatusId.BlueRot : P3StatusId.RedRot;
    }

    // What the guides tell one player to do, as degrees from the set's layout (L negative) and
    // yalms from the centre, from the design's spot table. Each walk waits for what the guides
    // wait for: the towers to show, the soakers to reach theirs, everything to go off before the
    // pass, the pass before the remote pop, and the remote pop's vuln to wear off before the local one.
    private sealed class Guide(PartyRole seat, Roll roll, Human human, Random rng)
    {
        public static readonly float[] SetStarts = [19.1f, 40.1f, 61.2f, 82.5f];
        public const float PassAt = 10.3f;
        private const float LooseSpreadAt = 0.5f, TowersAt = 1f, TetheredLatestAt = 5f, RemotePopsAt = 12.3f, LocalPopsAt = 15f;
        private const float RunSpeed = 6.5f;
        private const float PassWindow = 3.2f;
        private const float Resolve = 10f, OffSpotTolerance = 0.8f, TowersInBy = 8.5f;
        private const float AfterTheResolve = 0.05f, RemotePopVulnLasts = 1.2f;
        private const float StackLane = 13.5f, InnerLane = 7f, LanesFrom = 4f;
        private const float LeastCurve = 1f, MostCurve = 2f;
        private static readonly float[] DefamationLanes = [17f, 18.3f];

        private static readonly Dictionary<PartyRole, float> LooseSpreadBearings = new()
        {
            [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
            [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
        };

        private sealed record Leg(int Set, Step Step, float NotBefore, Func<ScenarioProbe, bool> Ready, Func<ScenarioProbe, Vector2> To);

        private static readonly Func<ScenarioProbe, bool> Always = _ => true;

        private readonly Dictionary<PartyRole, bool> right = new();
        private readonly Dictionary<PartyRole, HashSet<ushort>> rotsBefore = new();
        private readonly Dictionary<int, float> remoteTetherBrokeAt = new();
        private readonly Queue<Leg> legs = new();
        private float? readySince;
        private bool humanHadRot;
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private const float RoundClearance = 2.5f, SoakerSettledWithin = 1.5f;

        public void Tick(ScenarioProbe p)
        {
            if (!p.Member(seat).IsAlive()) return;
            if (p.Crossed(LooseSpreadAt))
                legs.Enqueue(new Leg(0, Step.Towers, LooseSpreadAt + Math.Max(0f, Late(0, Step.Towers)), Always, _ => OnRing(LooseSpreadBearings[seat], 8.5f)));
            for (var set = 1; set <= 4; set++)
            {
                if (p.Crossed(SetStarts[set - 1] + TowersAt + Math.Min(0f, Late(set, Step.Towers))))
                {
                    TakeTheClosestSides(p, set);
                    PlanTheSet(set);
                }
                NoteWhenTheRemoteTetherBroke(p, set);
            }
            Advance(p);
        }

        private float Late(int set, Step step) => human.Late((set, step));

        private void PlanTheSet(int set)
        {
            var start = SetStarts[set - 1];
            legs.Enqueue(IsSoaker(set, seat)
                ? new Leg(set, Step.Towers, Math.Max(start, start + TowersAt + Late(set, Step.Towers)), Always, _ => TowerSpot(set, seat))
                : new Leg(set, Step.Towers, start + TowersAt, p => p.Time >= start + TetheredLatestAt || SoakersInPlace(p, set), _ => TowerSpot(set, seat)));
            if (PassSpot(set) is { } pass)
                legs.Enqueue(new Leg(set, Step.Pass, Math.Max(start + Resolve + AfterTheResolve, start + PassAt + Late(set, Step.Pass)), Always, pass));
            if (RemotePopSpot(set) is { } c)
                legs.Enqueue(new Leg(set, Step.RemotePops, start + RemotePopsAt + Late(set, Step.RemotePops), p => HasDoneItsPass(p, set), _ => c));
            if (LocalPopSpot(set) is { } d)
                legs.Enqueue(new Leg(set, Step.LocalPops, start + LocalPopsAt + Late(set, Step.LocalPops), p => RemoteVulnHasWornOff(p, set), _ => d));
        }

        // A walk sets off once its cue has come and what it waits for has happened, a reaction time
        // after both, and is aimed when it sets off, so a walk onto a player goes to where they stand then.
        private void Advance(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (legs.TryPeek(out var next))
            {
                readySince ??= next.Ready(p) ? p.Time : null;
                if (readySince is { } ready && p.Time >= Math.Max(next.NotBefore, ready + Math.Max(0f, Late(next.Set, next.Step))))
                {
                    legs.Dequeue();
                    readySince = null;
                    var to = next.To(p);
                    var route = RoundWhoeverStandsInTheWay(p, Route(Flat(me.Position), to, next.Step, next.Set), to, next.Set);
                    walking = (p.Time, route, Pace(route, next.Step, next.Set, p.Time));
                }
            }
            if (walking is not { } w) return;
            var (at, arrived) = Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Vector3(at.X, 0f, at.Y));
            if (arrived) walking = null;
        }

        // Like a player, the human sets off round a soaker standing on its tower spot in the way of a
        // straight walk, whom it could catch a rot from or give one to, instead of walking through them:
        // a soaker holds its tower, so it won't step aside the way the other bots do. It passes on
        // whichever side leaves the most room, and only if that beats the straight walk.
        private Vector2[] RoundWhoeverStandsInTheWay(ScenarioProbe p, Vector2[] route, Vector2 to, int set)
        {
            if (route.Length != 2 || set == 0) return route;
            var from = route[0];
            var along = to - from;
            if (along.LengthSquared() < 1f) return route;
            var others = PerRole.All.Where(r => r != seat && p.Member(r).IsAlive() && IsSoaker(set, r) && CouldSwapARot(p, r))
                                .Select(r => (Role: r, At: Flat(p.Member(r)!.Position)))
                                .Where(o => Vector2.Distance(o.At, to) >= 0.5f && OnOneOfItsTowerSpots(set, o.Role, o.At))
                                .Select(o => o.At)
                                .ToList();
            float Room(Vector2[] walk) => others.Count == 0 ? float.MaxValue
                : others.Min(at => walk.Zip(walk.Skip(1), (a, b) => DistanceToSegment(at, a, b)).Min());
            var straight = Room(route);
            if (straight >= RoundClearance) return route;
            var inTheWay = others.Where(at => DistanceToSegment(at, from, to) < RoundClearance)
                                 .MinBy(at => Vector2.Dot(at - from, along));
            var side = Vector2.Normalize(new Vector2(-along.Y, along.X));
            var best = new[] { 1f, -1f }.Select(s => new[] { from, inTheWay + side * s * RoundClearance, to })
                                        .MaxBy(Room)!;
            return Room(best) > straight ? best : route;
        }

        private bool OnOneOfItsTowerSpots(int set, PartyRole role, Vector2 at)
        {
            var (l, r) = ASpots(set, roll.RoleIn(set, role));
            return Math.Min(Vector2.Distance(at, l), Vector2.Distance(at, r)) <= SoakerSettledWithin;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var u = ab.LengthSquared() < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / ab.LengthSquared(), 0f, 1f);
            return Vector2.Distance(point, a + ab * u);
        }

        private bool CouldSwapARot(ScenarioProbe p, PartyRole other)
        {
            var (me, them) = (p.Member(seat)!, p.Member(other)!);
            bool Catches(SimCharacter from, SimCharacter to, ushort rot, ushort debugger)
                => from.HasStatus(rot) && !to.HasStatus(rot) && !to.HasStatus(debugger);
            return Catches(them, me, P3StatusId.RedRot, P3StatusId.UnderflowDebugger) || Catches(me, them, P3StatusId.RedRot, P3StatusId.UnderflowDebugger)
                   || Catches(them, me, P3StatusId.BlueRot, P3StatusId.PerformanceDebugger) || Catches(me, them, P3StatusId.BlueRot, P3StatusId.PerformanceDebugger);
        }

        // A lane long enough to miss the towers at run speed is sprinted.
        private float Pace(Vector2[] route, Step step, int set, float now)
        {
            if (human.Walk == Walk.Sprinting) return AiManager.SprintSpeed;
            var length = route.Zip(route.Skip(1), Vector2.Distance).Sum();
            var lateForTheTowers = human.Walk == Walk.Lanes && step == Step.Towers && set > 0
                                   && now + length / RunSpeed > SetStarts[set - 1] + TowersInBy;
            return lateForTheTowers ? AiManager.SprintSpeed : RunSpeed;
        }

        private Vector2[] Route(Vector2 from, Vector2 to, Step step, int set)
        {
            var length = Vector2.Distance(from, to);
            switch (human.Walk)
            {
                case Walk.Curving when length > 1f:
                    var side = rng.Next(2) == 0 ? -1f : 1f;
                    var bulge = Math.Min(LeastCurve + (MostCurve - LeastCurve) * (float)rng.NextDouble(), length / 4f);
                    var normal = new Vector2(-(to - from).Y, (to - from).X) / length * side;
                    return Enumerable.Range(0, 17).Select(i => i / 16f)
                                     .Select(u => from + (to - from) * u + normal * (bulge * 4f * u * (1f - u))).ToArray();
                case Walk.Lanes when step == Step.Towers && set > 0 && length > LanesFrom:
                    var lane = roll.RoleIn(set, seat) switch
                    {
                        HelloWorldRole.Defamation => DefamationLanes[rng.Next(2)],
                        HelloWorldRole.Stack => StackLane,
                        _ => InnerLane,
                    };
                    var start = Bearing(from);
                    var turn = Wrap180(Bearing(to) - start);
                    var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(turn) / 10f));
                    return [from, .. Enumerable.Range(0, steps + 1).Select(i => OnRing(start + turn * i / steps, lane)), to];
                default:
                    return [from, to];
            }
        }

        private static (Vector2 At, bool Arrived) Along(Vector2[] points, float walked)
        {
            for (var i = 1; i < points.Length; i++)
            {
                var leg = Vector2.Distance(points[i - 1], points[i]);
                if (walked <= leg) return (leg < 1e-4f ? points[i] : Vector2.Lerp(points[i - 1], points[i], walked / leg), false);
                walked -= leg;
            }
            return (points[^1], true);
        }

        private void TakeTheClosestSides(ScenarioProbe p, int set)
        {
            foreach (var kit in Enum.GetValues<HelloWorldKit>())
            {
                var pair = roll.Pair(kit).OrderBy(r => (int)r).ToArray();
                var (l, r) = ASpots(set, roll.RoleIn(set, pair[0]));
                var first = Flat(p.Member(pair[0])!.Position);
                var second = Flat(p.Member(pair[1])!.Position);
                var keep = Vector2.Distance(first, l) + Vector2.Distance(second, r);
                var swap = Vector2.Distance(first, r) + Vector2.Distance(second, l);
                right[pair[0]] = swap < keep - 0.5f;
                right[pair[1]] = !right[pair[0]];
            }
        }

        private bool SoakersInPlace(ScenarioProbe p, int set)
            => PerRole.All.Where(r => IsSoaker(set, r) && p.Member(r).IsAlive())
                          .All(r => Vector2.Distance(Flat(p.Member(r)!.Position), TowerSpot(set, r)) <= human.StretchWhenSoakersWithin);

        // The remote pair takes its rot from the stacks, a stack waits for its remote to have it,
        // and the local waits for the rot it takes from its defamation.
        private bool HasDoneItsPass(ScenarioProbe p, int set) => (roll.RoleIn(set, seat), set == 4) switch
        {
            (HelloWorldRole.Remote, false) => Holds(p, seat, roll.StackRot),
            (HelloWorldRole.Stack, false) => Holds(p, SameSide(set, HelloWorldRole.Remote), roll.StackRot),
            (HelloWorldRole.Local, false) => Holds(p, seat, roll.DefamationRot),
            _ => true,
        };

        private static bool Holds(ScenarioProbe p, PartyRole role, ushort rot) => !p.Member(role).IsAlive() || p.Member(role)!.HasStatus(rot);

        private bool RemoteVulnHasWornOff(ScenarioProbe p, int set)
            => roll.RoleIn(set, seat) != HelloWorldRole.Local || set == 4
               || remoteTetherBrokeAt.TryGetValue(set, out var broke) && p.Time >= broke + RemotePopVulnLasts;

        private void NoteWhenTheRemoteTetherBroke(ScenarioProbe p, int set)
        {
            if (remoteTetherBrokeAt.ContainsKey(set) || p.Time < SetStarts[set - 1] + Resolve) return;
            if (PerRole.All.Where(r => roll.RoleIn(set, r) == HelloWorldRole.Remote)
                           .All(r => !p.Member(r).IsAlive() || !p.Member(r)!.HasStatus(P3StatusId.RemoteRegression)))
                remoteTetherBrokeAt[set] = p.Time;
        }

        private bool IsSoaker(int set, PartyRole role) => roll.RoleIn(set, role) is HelloWorldRole.Defamation or HelloWorldRole.Stack;

        private Vector2 TowerSpot(int set, PartyRole role)
        {
            var (l, r) = ASpots(set, roll.RoleIn(set, role));
            return right[role] ? r : l;
        }

        private (Vector2 L, Vector2 R) ASpots(int set, HelloWorldRole role) => role switch
        {
            HelloWorldRole.Defamation => Mirrored(set, 45f, 18.1f),
            HelloWorldRole.Stack => Mirrored(set, 154f, 12f),
            HelloWorldRole.Remote => Mirrored(set, 166f, 11.4f),
            _ => set == 4 ? Sides(set, (-165f, 10.5f), (165f, 11.2f)) : Mirrored(set, 74f, 14.2f),
        };

        private Func<ScenarioProbe, Vector2>? PassSpot(int set) => (roll.RoleIn(set, seat), set == 4) switch
        {
            (HelloWorldRole.Defamation, _) or (HelloWorldRole.Local, false) => _ => Mine(set, Mirrored(set, 45f, 13.7f)),
            (HelloWorldRole.Remote, false) => p => Flat(p.Member(SameSide(set, HelloWorldRole.Stack))!.Position),
            (HelloWorldRole.Stack, true) => _ => Mine(set, Sides(set, (-137f, 8.4f), (127f, 9.8f))),
            _ => null,
        };

        private Vector2? RemotePopSpot(int set) => (roll.RoleIn(set, seat), set == 4) switch
        {
            (HelloWorldRole.Remote, false) => Mine(set, Sides(set, (-141f, 17f), (149f, 11.6f))),
            (HelloWorldRole.Stack, false) => Mine(set, Sides(set, (-120f, 10f), (112f, 10f))),
            (HelloWorldRole.Local, false) => Mine(set, Sides(set, (-31f, 7.7f), (51f, 8.4f))),
            (HelloWorldRole.Remote, true) => Mine(set, Sides(set, (-141f, 16f), (146f, 13.1f))),
            _ => null,
        };

        private Vector2? LocalPopSpot(int set) => (roll.RoleIn(set, seat), set == 4) switch
        {
            (HelloWorldRole.Local, false) => Mine(set, Sides(set, (-21f, 7.4f), (44f, 7.4f))),
            (HelloWorldRole.Remote, false) => Mine(set, Sides(set, (-158f, 12f), (155f, 10.4f))),
            (HelloWorldRole.Stack, true) => Mine(set, Sides(set, (-126f, 14.2f), (113f, 14.3f))),
            (HelloWorldRole.Remote, true) => Mine(set, Sides(set, (-154f, 14.6f), (153f, 11.9f))),
            (HelloWorldRole.Local, true) => Mine(set, Sides(set, (-167f, 12.9f), (175f, 12.3f))),
            _ => null,
        };

        private PartyRole SameSide(int set, HelloWorldRole role)
            => PerRole.All.Single(r => roll.RoleIn(set, r) == role && right[r] == right[seat]);

        private Vector2 Mine(int set, (Vector2 L, Vector2 R) spots) => right[seat] ? spots.R : spots.L;

        private (Vector2 L, Vector2 R) Mirrored(int set, float degrees, float radius) => Sides(set, (-degrees, radius), (degrees, radius));

        private (Vector2 L, Vector2 R) Sides(int set, (float Degrees, float Radius) l, (float Degrees, float Radius) r)
        {
            var layout = roll.Layouts[set - 1].RadiansFromNorth * 180f / MathF.PI;
            return (OnRing(layout + l.Degrees, l.Radius), OnRing(layout + r.Degrees, r.Radius));
        }

        // A rot may only change hands at a set's pass, from its defamation to its local tether and
        // from its stack to its remote tether.
        public IEnumerable<string> StrayRots(ScenarioProbe p)
        {
            var strays = new List<string>();
            var set = SetStarts.Count(start => p.Time >= start);
            foreach (var role in PerRole.All)
            {
                if (p.Member(role) is not { } member || !member.IsAlive()) continue;
                var rots = member.ActiveStatuses.Select(s => s.StatusId).Where(id => id is P3StatusId.RedRot or P3StatusId.BlueRot).ToHashSet();
                if (rotsBefore.TryGetValue(role, out var had) && p.Time > 8.5f)
                    foreach (var rot in rots.Except(had))
                    {
                        var expected = rot == roll.DefamationRot ? HelloWorldRole.Local : HelloWorldRole.Remote;
                        var inPass = set is >= 1 and <= 3 && p.Time >= SetStarts[set - 1] + Resolve && p.Time <= SetStarts[set - 1] + PassAt + PassWindow;
                        if (!inPass || roll.RoleIn(set, role) != expected)
                            strays.Add($"{role} caught a rot at t={p.Time:F2} (set {set} {roll.RoleIn(Math.Max(set, 1), role)})");
                    }
                rotsBefore[role] = rots;
            }
            return strays;
        }

        // However the bots dodged the human on the way, each stands on one of its role's two spots when
        // the set resolves, the two of a pair on different ones, and the human's partner on the side the
        // human took the other of. Which side a pair of bots splits to on a near-tie is theirs to pick.
        public IEnumerable<string> BotsOffTheirTowerSpots(ScenarioProbe p)
        {
            var set = Array.FindIndex(SetStarts, start => p.Crossed(start + Resolve - 0.05f)) + 1;
            if (set == 0) return [];
            var problems = new List<string>();
            var onRight = new Dictionary<PartyRole, bool>();
            foreach (var role in PerRole.All.Where(role => role != seat && p.Member(role).IsAlive()))
            {
                var (l, r) = ASpots(set, roll.RoleIn(set, role));
                var at = Flat(p.Member(role)!.Position);
                var (toL, toR) = (Vector2.Distance(at, l), Vector2.Distance(at, r));
                onRight[role] = toR < toL;
                if (Math.Min(toL, toR) > OffSpotTolerance)
                    problems.Add($"{role} stood {Math.Min(toL, toR):F2} y off both its set {set} {roll.RoleIn(set, role)} spots as the towers went off");
            }
            foreach (var kit in Enum.GetValues<HelloWorldKit>())
            {
                var pair = roll.Pair(kit).ToArray();
                if (pair.Contains(seat))
                {
                    var partner = pair.Single(r => r != seat);
                    if (onRight.TryGetValue(partner, out var side) && side == right[seat])
                        problems.Add($"{partner} took the human's own set {set} side");
                }
                else if (onRight.TryGetValue(pair[0], out var first) && onRight.TryGetValue(pair[1], out var second) && first == second)
                    problems.Add($"{pair[0]} and {pair[1]} both took the set {set} {(first ? "R" : "L")} spot");
            }
            return problems;
        }

        public int? HumanCaughtARot(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            var has = me.HasStatus(P3StatusId.RedRot) || me.HasStatus(P3StatusId.BlueRot);
            var caught = has && !humanHadRot && p.Time > 8.5f;
            humanHadRot = has;
            return caught ? SetStarts.Count(start => p.Time >= start) : null;
        }

        private static float Bearing(Vector2 p) => MathF.Atan2(p.X, -p.Y) * 180f / MathF.PI;

        private static float Wrap180(float degrees) => ((degrees % 360f) + 540f) % 360f - 180f;

        private static Vector2 OnRing(float bearingDegrees, float radius)
        {
            var radians = bearingDegrees * MathF.PI / 180f;
            return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
        }

        private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
    }
}
