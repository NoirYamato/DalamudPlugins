using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P4BlueScreen;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Scenarios.Top.TopP4Constants;
using static AnoMech.Tests.TopP4BlueScreenLpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays the
// raidplan from what a player can see: Omega's cast bar, the markers over everyone's heads, the beams, the
// line stacks and the rings. From each set's two markers it works out its own side, then at each cue walks
// straight to the next spot a fixed reaction time late, never stepping round anyone:
//   the cast bar: to its quickmarch spot;
//   the beams of sets 1 and 3: to its side's stack spot; the line stacks of sets 1 and 2: back out;
//   set 2: straight in on ring 2 (the raidplan's cue) or on the beams, then on to the stack spot;
//   set 3: straight in on ring 6.
// Its part in each set is pinned (marked on its own, marked with a same-side partner north or south of it,
// the other side's filler, or none), and whatever its seat, part, cue and reaction time, nobody may die.
public class TopP4BlueScreenLpduHumanTests
{
    private const int LpduStrat = 0;

    // Ten fixed rolls per seat; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 10)).Select(i => 17 * i);
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];

    // In every set: MarkedSplit pairs the human with a marker on the other side; MarkedStays with one south
    // of it on its own side, so the partner flexes; MarkedFlexes with one north of it, so the human flexes;
    // Filler marks two on the other side, the human being this side's melee; Mixed draws one of those or
    // Unmarked for each set from the seed.
    public enum Part { Unmarked, MarkedSplit, MarkedStays, MarkedFlexes, Filler, Mixed }

    public enum StepInCue { Beams, Ring }

    private static IEnumerable<TestCaseData> Matrix()
        => from part in Enum.GetValues<Part>()
           from cue in Enum.GetValues<StepInCue>()
           from delay in ReactionTimes
           select new TestCaseData(part, cue, delay).SetName($"{{m}}({part}, steps in on the {cue}, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingTheRaidplanIsNeverCaught(Part part, StepInCue cue, float delay)
    {
        var tightest = new Clearances();
        var runs = 0;
        var problems = new List<string>();
        foreach (var seat in PerRole.All.Where(s => CanPlay(s, part)))
        foreach (var seed in Seeds)
        {
            runs++;
            problems.AddRange(Play(seat, part, cue, delay, seed, tightest));
        }

        TestContext.Out.WriteLine($"{runs} runs; the human's tightest clearances: {tightest}");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    private static List<string> Play(PartyRole seat, Part part, StepInCue cue, float delay, int seed, Clearances tightest)
    {
        var rng = new Random(seed * 8 + (int)seat);
        var sets = Enumerable.Range(0, TopP4BlueScreenState.Sets).Select(_ => Markers(seat, part, rng)).ToArray();
        var guide = new Guide(seat, delay, cue);
        var problems = new List<string>();
        var run = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = seat,
            Overrides = o => Pin((TopP4BlueScreenStateOverrides)o, sets),
            Takeovers = [new Takeover(0f, null)],
            Probe = p =>
            {
                tightest.Measure(p, seat, sets);
                problems.AddRange(BotsOffTheirStackSpots(p, seat, sets));
                guide.Tick(p);
            },
        });

        if (!run.Passed) problems.Add(run.ToString());
        for (var set = 0; set < sets.Length; set++)
        {
            var west = WestAfterFlex(sets[set]).Contains(seat);
            if (guide.WestRead[set] != west)
                problems.Add($"the human read its set {set + 1} side as {Side(guide.WestRead[set])}, the strat puts it {Side(west)}");
        }
        if (run.Passed && guide.Walks < guide.Legs)
            problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
        return problems.Select(problem => $"seed {seed}, {seat} (markers {Describe(sets)}, steps in on the {cue}, {delay:F1}s late): {problem}")
                       .ToList();
    }

    private static string Side(bool? west) => west switch { true => "west", false => "east", null => "unread" };

    private static string Describe(PartyRole[][] sets) => string.Join(" / ", sets.Select(s => string.Join("+", s)));

    private static PartyRole[] SideOf(PartyRole role) => West.Contains(role) ? West : East;

    private static PartyRole[] OtherSideOf(PartyRole role) => West.Contains(role) ? East : West;

    private static int Row(PartyRole role) => Array.IndexOf(SideOf(role), role);

    private static readonly Part[] Parts = [Part.Unmarked, Part.MarkedSplit, Part.MarkedStays, Part.MarkedFlexes, Part.Filler];

    // A tank is never south of a same-side partner and a melee never north of one; only a melee fills.
    private static bool CanPlay(PartyRole seat, Part part) => part switch
    {
        Part.MarkedStays => Row(seat) < 3,
        Part.MarkedFlexes => Row(seat) > 0,
        Part.Filler => Row(seat) == 3,
        _ => true,
    };

    private static PartyRole[] Markers(PartyRole seat, Part part, Random rng)
    {
        T Pick<T>(IReadOnlyList<T> from) => from[rng.Next(from.Count)];
        var own = SideOf(seat);
        var other = OtherSideOf(seat);
        return part switch
        {
            Part.MarkedSplit => [seat, Pick(other)],
            Part.MarkedStays => [seat, Pick(own[(Row(seat) + 1)..])],
            Part.MarkedFlexes => [seat, Pick(own[..Row(seat)])],
            Part.Filler => Pick(Pairs(other).ToList()),
            Part.Unmarked => Pick(Pairs(PerRole.All).Where(pair => !pair.Contains(seat) && !IsFiller(seat, pair)).ToList()),
            _ => Markers(seat, Pick(Parts.Where(p => CanPlay(seat, p)).ToList()), rng),
        };
    }

    private static bool IsFiller(PartyRole seat, PartyRole[] markers) => Row(seat) == 3 && markers.All(OtherSideOf(seat).Contains);

    private static IEnumerable<PartyRole[]> Pairs(IReadOnlyList<PartyRole> roles)
        => roles.SelectMany((a, i) => roles.Skip(i + 1).Select(b => new[] { a, b }));

    // Each cue queues a walk that sets off a reaction time later and is aimed when it sets off; a walk queued
    // to follow another sets off when that one arrives.
    private sealed class Guide(PartyRole seat, float late, StepInCue cue)
    {
        private const float RunSpeed = 6.5f;

        public readonly bool?[] WestRead = new bool?[TopP4BlueScreenState.Sets];
        public int Legs { get; private set; }
        public int Walks { get; private set; }

        private readonly Queue<(float Depart, Func<Vector2> To, bool OnArrival)> legs = new();
        private (float Depart, Vector2 From, Vector2 To)? walking;
        private Dictionary<string, PartyRole>? roleById;
        private readonly List<PartyRole> marked = [];
        private int rowsRead, markerSets, beamRounds, stackRounds, ring2s;
        private float lastBeams = float.NegativeInfinity, lastStacks = float.NegativeInfinity;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (!me.IsAlive()) return;
            roleById ??= PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
            ReadTheTimeline(p);
            Advance(p, me);
        }

        private void ReadTheTimeline(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                var id = row["id"];
                if (row["kind"] == "cast" && id == $"{P4ActionId.WaveCannonCast}")
                    Queue(p.Time, () => Quickmarch[seat]);
                if (row["kind"] != "ability") continue;
                if (id == $"{P4ActionId.StackMarker}" && roleById!.TryGetValue(row["target"], out var target))
                    ReadAMarker(target);
                else if (id == $"{P4ActionId.WaveCannonBeam}" && FirstOfARound(ref lastBeams, p.Time))
                    OnTheBeams(p.Time, beamRounds++);
                else if (id == $"{P4ActionId.LineStack}" && FirstOfARound(ref lastStacks, p.Time) && stackRounds++ < 2)
                    Queue(p.Time, () => Quickmarch[seat]);
                else if (id == $"{P3ActionId.WaveRepeaterRing2}")
                    OnRingTwo(p.Time, ring2s++);
            }
        }

        private static bool FirstOfARound(ref float last, float now)
        {
            var first = now - last > 2f;
            last = now;
            return first;
        }

        private void ReadAMarker(PartyRole target)
        {
            marked.Add(target);
            if (marked.Count < TopP4BlueScreenState.TargetsPerSet) return;
            WestRead[markerSets++] = StandsWest([.. marked]);
            marked.Clear();
        }

        // The southern of two same-side markers crosses, and the other side's melee crosses the other way.
        private bool StandsWest(PartyRole[] markers)
        {
            var mineWest = West.Contains(seat);
            var westMarked = markers.Count(West.Contains);
            if (westMarked == 1) return mineWest;
            var markedSide = westMarked == 2 ? West : East;
            var crosser = markers.MaxBy(r => Array.IndexOf(markedSide, r));
            var crosses = seat == crosser || (!markedSide.Contains(seat) && Row(seat) == 3);
            return crosses ? !mineWest : mineWest;
        }

        private void OnTheBeams(float now, int round)
        {
            switch (round)
            {
                case 0:
                    Queue(now, StackSpot(0, OuterStackWest));
                    break;
                case 1 when cue == StepInCue.Beams:
                    Queue(now, () => StepIn[seat]);
                    Queue(now, StackSpot(1, InnerStackWest), onArrival: true);
                    break;
                case 1:
                    Queue(now, StackSpot(1, InnerStackWest));
                    break;
                case 2:
                    Queue(now, StackSpot(2, OuterStackWest));
                    break;
            }
        }

        private void OnRingTwo(float now, int ring)
        {
            if (ring == 0 && cue == StepInCue.Ring) Queue(now, () => StepIn[seat]);
            if (ring == 1) Queue(now, StackSpot(2, FinalStepInWest));
        }

        private Func<Vector2> StackSpot(int set, Vector2 westSpot)
            => () => WestRead[set] ?? West.Contains(seat) ? westSpot : westSpot with { X = -westSpot.X };

        private void Queue(float cue, Func<Vector2> to, bool onArrival = false)
        {
            legs.Enqueue((cue + late, to, onArrival));
            Legs++;
        }

        private void Advance(ScenarioProbe p, SimCharacter me)
        {
            if (legs.TryPeek(out var next) && p.Time >= next.Depart && (!next.OnArrival || walking is null))
            {
                legs.Dequeue();
                Walks++;
                walking = (p.Time, Flat(me.Position), next.To());
            }
            if (walking is not { } w) return;
            var length = Vector2.Distance(w.From, w.To);
            var walked = (p.Time - w.Depart) * RunSpeed;
            var at = walked >= length ? w.To : Vector2.Lerp(w.From, w.To, walked / length);
            me.SetPosition(new Vector3(at.X, 0f, at.Y));
            if (walked >= length) walking = null;
        }
    }

    // However the human walked, every bot stands in its clump just before each line stack.
    private static readonly float[] StacksAt = [13.6f, 23.4f, 33.5f];
    private const float JustBefore = 0.1f, OffSpotTolerance = 0.75f;

    private static IEnumerable<string> BotsOffTheirStackSpots(ScenarioProbe p, PartyRole seat, PartyRole[][] sets)
    {
        var set = Array.FindIndex(StacksAt, t => p.Crossed(t - JustBefore));
        if (set < 0) return [];
        var spots = Clumps(WestAfterFlex(sets[set]), set == 1 ? InnerStackWest : OuterStackWest);
        return PerRole.All.Where(r => r != seat && p.Member(r).IsAlive())
                      .Select(r => (Role: r, Off: Vector2.Distance(Flat(p.Member(r)!.Position), spots[r])))
                      .Where(b => b.Off > OffSpotTolerance)
                      .Select(b => $"{b.Role} stood {b.Off:F2} y off its set {set + 1} stack spot")
                      .ToList();
    }

    // How close the human came to each hit it dodged (or how deep it stood in a stack it had to share), at the
    // hit's snapshot, over every run of a case. The lines are the repeats' telegraphs, read as cast.
    private sealed class Clearances
    {
        private readonly Dictionary<string, float> tightest = new();
        private readonly List<(float At, Vector2 Direction)> aimed = [];
        private int rowsRead;

        private static readonly float[] AimsAt = [8.0f, 17.9f, 28.0f];
        private static readonly float[] BeamsAt = [8.6f, 18.5f, 28.6f];
        private static readonly float[] RepeatsAt = [13.0f, 22.9f, 33.0f];

        private static readonly (float Time, string Name, float Inner, float Outer)[] Rings =
        [
            (15.7f, "ring 1", float.NegativeInfinity, 6f), (18.1f, "ring 2", 6f, 12f), (20.1f, "ring 3", 12f, 18f),
            (22.1f, "ring 4", 18f, 24f), (32.9f, "ring 5", float.NegativeInfinity, 6f), (35.2f, "ring 6", 6f, 12f),
            (37.2f, "ring 7", 12f, 18f), (39.2f, "ring 8", 18f, 24f),
        ];

        public void Measure(ScenarioProbe p, PartyRole seat, PartyRole[][] sets)
        {
            ReadTheTelegraphs(p);
            var me = p.Member(seat);
            if (!me.IsAlive()) return;
            var at = Flat(me!.Position);
            for (var round = 0; round < 3; round++)
            {
                var lines = aimed.Where(l => MathF.Abs(l.At - AimsAt[round]) < 0.5f).Select(l => l.Direction).ToList();
                if (lines.Count == 0) continue;
                if (p.Crossed(BeamsAt[round]))
                    Note("other players' beams", lines.OrderBy(d => LineClearance(at, d)).Skip(1).Min(d => LineClearance(at, d)));
                if (p.Crossed(RepeatsAt[round]))
                    Note("repeats", lines.Min(d => LineClearance(at, d)));
                if (p.Crossed(StacksAt[round]))
                {
                    var stacks = sets[round].Select(t => Vector2.Normalize(Flat(p.Member(t)!.Position)))
                                            .Select(d => LineClearance(at, d)).Order().ToList();
                    Note("inside its own line stack", -stacks[0]);
                    Note("the other line stack", stacks[1]);
                }
            }
            var r = at.Length();
            foreach (var (time, name, inner, outer) in Rings)
                if (p.Crossed(time)) Note(name, MathF.Max(inner - r, r - outer));
        }

        private void ReadTheTelegraphs(ScenarioProbe p)
        {
            var rows = p.Fake.Timeline.Rows;
            for (; rowsRead < rows.Count; rowsRead++)
                if (rows[rowsRead] is var row && row["kind"] == "cast" && row["id"] == $"{P4ActionId.WaveCannonRepeat}")
                {
                    var rot = float.Parse(row["rot"], CultureInfo.InvariantCulture);
                    aimed.Add((p.Time, new Vector2(MathF.Sin(rot), MathF.Cos(rot))));
                }
        }

        private void Note(string name, float clearance)
            => tightest[name] = MathF.Min(tightest.GetValueOrDefault(name, float.MaxValue), clearance);

        public override string ToString()
            => string.Join(", ", tightest.OrderBy(t => t.Value).Select(t => $"{t.Key} {t.Value.ToString("F2", CultureInfo.InvariantCulture)}"));
    }

    private const float LineHalfWidth = 3f;

    // How far outside a line from the centre a point stands; negative inside it.
    internal static float LineClearance(Vector2 at, Vector2 direction)
    {
        var along = Vector2.Dot(at, direction);
        var across = MathF.Abs(at.X * direction.Y - at.Y * direction.X);
        return along >= 0 ? across - LineHalfWidth : MathF.Max(across - LineHalfWidth, -along);
    }
}
