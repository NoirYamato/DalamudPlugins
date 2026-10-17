using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who
// plays the guides and nothing else: it waits on its loose-spread spot, then walks straight at run
// speed to its role's spot at each cue, a fixed reaction time late, and never steps round anyone.
// The bots must keep every rot off it and off themselves, for every kit in every seat.
public class TopP3HelloWorldLpduHumanTests
{
    // Ten fixed rolls per seat; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 10)).Select(i => 11 * i);
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];

    private static IEnumerable<TestCaseData> KitsAndReactionTimes()
        => from kit in Enum.GetValues<HelloWorldKit>()
           from late in ReactionTimes
           select new TestCaseData(kit, late).SetName($"AHumanPlayingTheGuidesIsNeverCaught({kit}, {late:F1}s late)");

    [TestCaseSource(nameof(KitsAndReactionTimes))]
    public void AHumanPlayingTheGuidesIsNeverCaught(HelloWorldKit kit, float late)
    {
        var problems = PerRole.All.SelectMany(seat => Seeds.SelectMany(seed => Play(seat, kit, seed, _ => late).Problems)).ToList();
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

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
            var game = Play(seat, kit, seed, cue => cue == (set, Step.Pass) ? 1f : 0f);
            problems.AddRange(game.Problems);
            var caught = game.CaughtRotAt.GetValueOrDefault(set);
            if (caught is null || caught <= Guide.SetStarts[set - 1] + Guide.PassAt + 1f)
                problems.Add($"seed {seed}: {seat} should have taken its rot in set {set} a second late, took it at {caught}");
        }
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    internal enum Step { Towers, Pass, RemotePops, LocalPops }

    private sealed record Outcome(List<string> Problems, Dictionary<int, float?> CaughtRotAt);

    private static Outcome Play(PartyRole seat, HelloWorldKit kit, int seed, Func<(int Set, Step Step), float> late)
    {
        var roll = Roll.For(seat, kit, seed);
        var guide = new Guide(seat, roll, late);
        var problems = new List<string>();
        var caught = new Dictionary<int, float?>();
        var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = seat,
            Overrides = o => roll.Pin((TopP3HelloWorldStateOverrides)o),
            Takeovers = [new Takeover(0f, null)],
            Probe = p =>
            {
                guide.Tick(p);
                foreach (var stray in guide.StrayRots(p)) problems.Add($"seed {seed}, {seat} ({kit}): {stray}");
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
    }

    // What the guides tell one player to do, as degrees from the set's layout (L negative) and
    // yalms from the centre.
    private sealed class Guide(PartyRole seat, Roll roll, Func<(int Set, Step Step), float> late)
    {
        public static readonly float[] SetStarts = [19.1f, 40.1f, 61.2f, 82.5f];
        public const float PassAt = 10.3f;
        private const float LooseSpreadAt = 0.5f, TowersAt = 1f, TetheredLatestAt = 5f, RemotePopsAt = 12.3f, LocalPopsAt = 15f;
        private const float RunSpeed = 6.5f;
        private const float InPlace = 1f;
        private const float PassWindow = 3.2f;

        private static readonly Dictionary<PartyRole, float> LooseSpreadBearings = new()
        {
            [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
            [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
        };

        private readonly Dictionary<PartyRole, bool> right = new();
        private readonly Dictionary<PartyRole, HashSet<ushort>> rotsBefore = new();
        private readonly HashSet<int> stretched = [];
        private bool humanHadRot;
        private (float Depart, Func<ScenarioProbe, Vector2> To)? pending;
        private (float Depart, Vector2 From, Vector2 To)? walking;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (!me.IsAlive()) return;
            if (p.Crossed(LooseSpreadAt)) WalkAt(LooseSpreadAt + late((0, Step.Towers)), _ => OnRing(LooseSpreadBearings[seat], 8.5f));
            for (var set = 1; set <= 4; set++)
            {
                var n = set;
                var start = SetStarts[n - 1];
                if (p.Crossed(start + TowersAt))
                {
                    TakeTheClosestSides(p, n);
                    if (IsSoaker(n, seat)) WalkAt(start + TowersAt + late((n, Step.Towers)), _ => TowerSpot(n, seat));
                }
                if (!IsSoaker(n, seat) && p.Time >= start + TowersAt && p.Time < start + PassAt && !stretched.Contains(n)
                    && (p.Time >= start + TetheredLatestAt || SoakersInPlace(p, n)))
                {
                    stretched.Add(n);
                    WalkAt(p.Time + late((n, Step.Towers)), _ => TowerSpot(n, seat));
                }
                if (p.Crossed(start + PassAt) && PassSpot(n) is { } pass)
                    WalkAt(start + PassAt + late((n, Step.Pass)), pass);
                if (p.Crossed(start + RemotePopsAt) && RemotePopSpot(n) is { } c)
                    WalkAt(start + RemotePopsAt + late((n, Step.RemotePops)), _ => c);
                if (p.Crossed(start + LocalPopsAt) && LocalPopSpot(n) is { } d)
                    WalkAt(start + LocalPopsAt + late((n, Step.LocalPops)), _ => d);
            }
            Walk(p);
        }

        // The next walk is aimed when it sets off, so a walk onto a player goes to where they stand then.
        private void WalkAt(float depart, Func<ScenarioProbe, Vector2> to)
            => pending = (depart, to);

        private void Walk(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (pending is { } next && p.Time >= next.Depart)
            {
                walking = (p.Time, Flat(me.Position), next.To(p));
                pending = null;
            }
            if (walking is not { } w) return;
            var along = w.To - w.From;
            var length = along.Length();
            var walked = Math.Min(length, (p.Time - w.Depart) * RunSpeed);
            var at = length < 1e-4f ? w.To : w.From + along / length * walked;
            me.SetPosition(new Vector3(at.X, 0f, at.Y));
            if (walked >= length) walking = null;
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
                          .All(r => Vector2.Distance(Flat(p.Member(r)!.Position), TowerSpot(set, r)) <= InPlace);

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
            (HelloWorldRole.Local, false) => Mine(set, Mirrored(set, 42f, 7.7f)),
            (HelloWorldRole.Remote, true) => Mine(set, Sides(set, (-141f, 16f), (146f, 13.1f))),
            _ => null,
        };

        private Vector2? LocalPopSpot(int set) => (roll.RoleIn(set, seat), set == 4) switch
        {
            (HelloWorldRole.Local, false) => Mine(set, Mirrored(set, 22f, 7.4f)),
            (HelloWorldRole.Remote, false) => Mine(set, Sides(set, (-158f, 12f), (165f, 12f))),
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
            var defamationRot = roll.DefamationColour == RotColour.Red ? P3StatusId.RedRot : P3StatusId.BlueRot;
            var set = SetStarts.Count(start => p.Time >= start);
            foreach (var role in PerRole.All)
            {
                if (p.Member(role) is not { } member || !member.IsAlive()) continue;
                var rots = member.ActiveStatuses.Select(s => s.StatusId).Where(id => id is P3StatusId.RedRot or P3StatusId.BlueRot).ToHashSet();
                if (rotsBefore.TryGetValue(role, out var had) && p.Time > 8.5f)
                    foreach (var rot in rots.Except(had))
                    {
                        var expected = rot == defamationRot ? HelloWorldRole.Local : HelloWorldRole.Remote;
                        var inPass = set is >= 1 and <= 3 && p.Time >= SetStarts[set - 1] + PassAt && p.Time <= SetStarts[set - 1] + PassAt + PassWindow;
                        if (!inPass || roll.RoleIn(set, role) != expected)
                            strays.Add($"{role} caught a rot at t={p.Time:F2} (set {set} {roll.RoleIn(Math.Max(set, 1), role)})");
                    }
                rotsBefore[role] = rots;
            }
            return strays;
        }

        public int? HumanCaughtARot(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            var has = me.HasStatus(P3StatusId.RedRot) || me.HasStatus(P3StatusId.BlueRot);
            var caught = has && !humanHadRot && p.Time > 8.5f;
            humanHadRot = has;
            return caught ? SetStarts.Count(start => p.Time >= start) : null;
        }

        private static Vector2 OnRing(float bearingDegrees, float radius)
        {
            var radians = bearingDegrees * MathF.PI / 180f;
            return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
        }

        private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
    }
}
