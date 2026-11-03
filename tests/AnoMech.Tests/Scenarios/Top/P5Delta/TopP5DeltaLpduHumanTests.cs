using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Delta;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP5DeltaLpduAiTests;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who plays LPDU from what a
// player can see: the tethers and debuffs at 10.1, its partner bot taking a side, the fists, the arms' turn icons, the
// Final's monitor and the Swivel Cannon casts, the Beyond Defense jump, the first lines firing and the debuffs wearing
// off. It decides its own band by the healer rule (in a tie it waits for the other pair of its colour to line up and
// takes the band they leave), takes the side its partner leaves, flexes on the fists itself, and at each cue walks to
// its LPDU spot a fixed reaction time late, straight, curving or sprinting, and never steps round anyone. Whatever its
// seat, part, healer layout, eye, cue and reaction time, nobody may die, and the human keeps the slack the margin tests
// ask of the bots from every hit it dodges: surviving a line by a few hundredths is a death in the game.
public class TopP5DeltaLpduHumanTests
{
    // Three fixed rolls per seat, one per walking style; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 3));
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];
    private const float JitterSlack = 0.5f;

    // InnerBlue refuses Beyond Defense, so its partner takes it; BeyondDefense is the inner blue it lands on. Monitor,
    // NearWorld and DistantWorld are blues of either band.
    public enum Part { InnerBlue, BeyondDefense, OuterBlue, Monitor, MonitorAndBeyondDefense, NearWorld, DistantWorld, InnerGreen, OuterGreen }

    // The healers of the human's colour: in one pair (it is inner), one in each pair or none (a tie the roll decides),
    // or the human's seat forcing its band against the healer rule (Beyond Defense parts by asking to eat it).
    public enum Healers { OnePair, BothPairs, NoPair, ForcedBand }

    public enum Walk { Straight, Curving, Sprinting }

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

    private static IEnumerable<TestCaseData> Matrix()
        => from part in Enum.GetValues<Part>()
           from healers in Enum.GetValues<Healers>()
           from delay in ReactionTimes
           select new TestCaseData(part, healers, delay).SetName($"{{m}}({part}, {healers}, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingLpduIsNeverCaught(Part part, Healers healers, float delay)
    {
        var tightest = new Clearances();
        var runs = 0;
        var problems = new List<string>();
        foreach (var seat in PerRole.All)
        foreach (var seed in Seeds)
        {
            if (Roll.For(seat, part, healers, seed) is not { } roll) continue;
            runs++;
            problems.AddRange(Play(roll, delay, tightest));
        }

        TestContext.Out.WriteLine($"{TestContext.CurrentContext.Test.Name}: {runs} runs; the human's tightest clearances:{Environment.NewLine}{tightest}");
        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.GreaterThan(0), "a seat could play it");
            Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(30)));
            Assert.That(tightest.Within(JitterSlack), Is.Empty, "the human's clearances");
        });
    }

    private static List<string> Play(Roll roll, float delay, Clearances tightest)
    {
        var guide = new Guide(roll, delay);
        var problems = new List<string>();
        var measure = tightest.For(roll);
        ScenarioRun run;
        AoeQuery.Evaluated += measure;
        try
        {
            run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), LpduStrat, roll.Seed, new ScenarioRunOptions
            {
                PlayerRole = roll.Seat,
                Overrides = o => roll.Pin((TopP5DeltaStateOverrides)o),
                Takeovers = [new Takeover(0f, null)],
                Probe = p =>
                {
                    guide.Tick(p);
                    tightest.Watch(p);
                },
            });
        }
        finally
        {
            AoeQuery.Evaluated -= measure;
        }

        if (!run.Passed) problems.Add(run.ToString());
        problems.AddRange(guide.Problems);
        if (run.Passed && guide.Walks < guide.Legs)
            problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
        return problems.Select(problem => $"{roll} ({delay:F1}s late): {problem}").ToList();
    }

    // The test rolls everything the human could see and pins it; the bots read the same roll from the scenario.
    internal sealed record Roll(
        PartyRole Seat, Part Part, Healers Healers, int Seed, Walk Walk, NorthSouth Eye, Side Swivel, Side OmegaMonitor,
        Side PlayerMonitor, ArmRotation[] Arms, PartyRole[] TetherOrder, uint[] FistColors, bool Inner,
        IReadOnlyList<(string Setting, PartyRole Role, Action<TopP5DeltaStateOverrides> Apply)> Requests)
    {
        public bool IsBlue => Part is not (Part.InnerGreen or Part.OuterGreen);

        public bool DecidesItsBand => Healers is Healers.OnePair or Healers.ForcedBand;

        public PartyRole Partner => TetherOrder[Array.IndexOf(TetherOrder, Seat) ^ 1];

        public uint FistOf(PartyRole role) => FistColors[Array.IndexOf(TetherOrder, role)];

        public static Roll? For(PartyRole seat, Part part, Healers healers, int seed)
        {
            var rng = new Random(seed * 1009 + (int)seat * 31 + (int)part * 7 + (int)healers);
            var blue = part is not (Part.InnerGreen or Part.OuterGreen);
            var preferInner = part switch
            {
                Part.InnerBlue or Part.BeyondDefense or Part.MonitorAndBeyondDefense or Part.InnerGreen => true,
                Part.OuterBlue or Part.OuterGreen => false,
                _ => (seed + (int)seat) % 2 == 0,
            };
            var fixedBand = part is not (Part.Monitor or Part.NearWorld or Part.DistantWorld);
            foreach (var inner in fixedBand ? new[] { preferInner } : new[] { preferInner, !preferInner })
                if (Build(seat, part, healers, seed, rng, blue, inner) is { } roll)
                    return roll;
            return null;
        }

        private static Roll? Build(PartyRole seat, Part part, Healers healers, int seed, Random rng, bool blue, bool inner)
        {
            var (mineHasAHealer, otherHasAHealer) = healers switch
            {
                Healers.OnePair => (inner, !inner),
                Healers.BothPairs => (true, true),
                Healers.NoPair => (false, false),
                _ => (!inner, inner),
            };
            if (IsHealer(seat) && !mineHasAHealer) return null;

            var healersLeft = new List<PartyRole> { RegenHealer, ShieldHealer }.Where(h => h != seat).OrderBy(_ => rng.Next()).ToList();
            var others = PerRole.All.Where(r => r != seat && !IsHealer(r)).OrderBy(_ => rng.Next()).ToList();
            PartyRole Take(List<PartyRole> from)
            {
                var role = from[0];
                from.RemoveAt(0);
                return role;
            }
            var partner = mineHasAHealer && !IsHealer(seat) ? Take(healersLeft) : Take(others);
            PartyRole[] otherPair = otherHasAHealer ? [Take(healersLeft), Take(others)] : [Take(others), Take(others)];
            var rest = others.Concat(healersLeft).OrderBy(_ => rng.Next()).ToList();

            var colour = blue ? 0 : 4;
            var mineRolledInner = healers == Healers.OnePair ? rng.Next(2) == 0 : inner;
            var order = new PartyRole[8];
            PartyRole[] mine = rng.Next(2) == 0 ? [seat, partner] : [partner, seat];
            if (rng.Next(2) == 0) otherPair = [otherPair[1], otherPair[0]];
            mine.CopyTo(order, colour + (mineRolledInner ? 0 : 2));
            otherPair.CopyTo(order, colour + (mineRolledInner ? 2 : 0));
            rest.CopyTo(order, 4 - colour);

            var fists = new uint[8];
            foreach (var group in new[] { 0, 4 })
            {
                uint[] colours = [.. new[] { Yellow, Yellow, Blue, Blue }.OrderBy(_ => rng.Next())];
                colours.CopyTo(fists, group);
            }

            var requests = new List<(string, PartyRole, Action<TopP5DeltaStateOverrides>)>();
            void Ask(string setting, PartyRole role, Action<TopP5DeltaStateOverrides> apply) => requests.Add((setting, role, apply));
            var forced = healers == Healers.ForcedBand;
            switch (part)
            {
                case Part.InnerBlue:
                    Ask("Beyond Defence No", seat, o => o.BeyondDefence[seat] = false);
                    break;
                case Part.BeyondDefense or Part.MonitorAndBeyondDefense when forced:
                    Ask("Beyond Defence Yes", seat, o => o.BeyondDefence[seat] = true);
                    break;
                case Part.BeyondDefense or Part.MonitorAndBeyondDefense:
                    Ask("Beyond Defence No", partner, o => o.BeyondDefence[partner] = false);
                    break;
                case Part.Monitor when inner:
                    Ask("Beyond Defence No", seat, o => o.BeyondDefence[seat] = false);
                    break;
                case Part.NearWorld:
                    Ask("Hello World Near", seat, o => o.HelloWorld[seat] = HelloWorldOption.Near);
                    break;
                case Part.DistantWorld:
                    Ask("Hello World Far", seat, o => o.HelloWorld[seat] = HelloWorldOption.Far);
                    break;
            }
            if (part is Part.Monitor or Part.MonitorAndBeyondDefense)
                Ask("Monitor Yes", seat, o => o.Monitor[seat] = true);
            if (forced && part is not (Part.BeyondDefense or Part.MonitorAndBeyondDefense))
            {
                var band = (blue, inner) switch
                {
                    (true, true) => PlayerTetherAssignment.CloseInner,
                    (true, false) => PlayerTetherAssignment.CloseOuter,
                    (false, true) => PlayerTetherAssignment.FarInner,
                    _ => PlayerTetherAssignment.FarOuter,
                };
                Ask($"Tether {band}", seat, o => o.Tether[seat] = band);
            }

            var walk = Enum.GetValues<Walk>()[(seed + (int)seat) % 3];
            return new Roll(seat, part, healers, seed * 7919 + (int)seat, walk, (seed + (int)seat) % 2 == 0 ? NorthSouth.North : NorthSouth.South,
                            rng.Next(2) == 0 ? Side.Left : Side.Right, rng.Next(2) == 0 ? Side.Left : Side.Right,
                            rng.Next(2) == 0 ? Side.Left : Side.Right,
                            [.. Enumerable.Range(0, 6).Select(_ => rng.Next(2) == 0 ? ArmRotation.Clockwise : ArmRotation.CounterClockwise)],
                            order, fists, inner, requests);
        }

        public void Pin(TopP5DeltaStateOverrides o)
        {
            o.EyeSpawn = Eye;
            o.SwivelCannonSide = Swivel;
            o.OmegaMonitorSide = OmegaMonitor;
            o.PlayerMonitorSide = PlayerMonitor;
            o.ArmRotations = Arms;
            o.TetherOrder = TetherOrder;
            o.FistColors = FistColors;
            foreach (var (_, _, apply) in Requests) apply(o);
        }

        public override string ToString()
            => $"seed {Seed}, {Seat} ({Part}, {Healers}, {Walk}, eye {(Eye == NorthSouth.North ? "N" : "S")}, order {string.Join(" ", TetherOrder)}"
               + (Requests.Count > 0 ? $", {string.Join(", ", Requests.Select(r => $"{r.Role} {r.Setting}"))}" : "") + ")";
    }

    // Each cue queues a walk that sets off a reaction time after the cue and what it waits for, aimed when it sets off.
    // Spots are the design's, in the eye frame (x toward the Final, z away from the eye); for eye S the world is that
    // frame turned 180 degrees.
    private sealed class Guide(Roll roll, float late)
    {
        private const float RunSpeed = 6.5f, Settled = 0.5f, StillPerFrame = 0.01f;
        private const float LeastCurve = 1f, MostCurve = 2f;

        public readonly List<string> Problems = [];
        public int Legs { get; private set; }
        public int Walks { get; private set; }

        private sealed record Leg(float NotBefore, Func<ScenarioProbe, bool> Ready, Func<ScenarioProbe, Vector2[]> Route, bool Sprint = false);

        private readonly Random rng = new(roll.Seed);
        private readonly Queue<Leg> legs = new();
        private readonly Dictionary<PartyRole, Vector2> lastSeen = new();
        private float? readySince;
        private (float Depart, Vector2[] Points, float Speed)? walking;
        private TopP5DeltaState? state;
        private bool inner;
        private int side;
        private bool planned, checkedTheLineUp, checkedTheFlex;

        private int Colour => roll.IsBlue ? 0 : 4;

        private Vector2 World(Vector2 eyeFrame) => ToWorld(eyeFrame, roll.Eye);

        private Vector2 EyeFrame(Vector2 world) => ToWorld(world, roll.Eye);

        private Vector2 EyeFrameOf(ScenarioProbe p, PartyRole role) => EyeFrame(Flat(p.Member(role)!.Position));

        // The Final at rel E faces the centre, so its Left monitor cleaves rel S and the blues stand rel N; the Swivel
        // Cannon's Left cast cleaves rel N, so rel S is safe.
        private int BlueHalf => roll.OmegaMonitor == Side.Left ? -1 : 1;

        private int SafeHalf => roll.Swivel == Side.Left ? 1 : -1;

        private bool Holds(PartyRole holder) => state!.PlayerMonitorRole == holder;

        public void Tick(ScenarioProbe p)
        {
            state ??= Scenario(p).LastState;
            var me = p.Member(roll.Seat)!;
            if (!me.IsAlive()) return;
            if (!planned)
            {
                planned = true;
                Plan();
            }
            if (p.Crossed(17f)) CheckTheLineUp(p);
            if (p.Crossed(22f)) CheckTheFlex(p);
            Advance(p, me);
            foreach (var role in PerRole.All) lastSeen[role] = Flat(p.Member(role)!.Position);
        }

        // The opening clump is by role and not turned with the eye, as the bots' is.
        private void Plan()
        {
            Queue(0.5f, _ => true, _ => [Opening(roll.Seat)]);
            Queue(10.1f, LineUpPartnersSettled, p => [World(LineUpSpot(p))]);
            Queue(19.05f, _ => true, _ => [World(FistSetUpSpot())]);
            if (roll.IsBlue)
                Queue(28.1f, p => !inner || !p.Member(roll.Seat)!.HasStatus(StatusId.HWRemoteTether),
                      p => inner ? [Flat(p.Member(OuterOnMySide())!.Position)] : [World(setUp)]);
            Queue(30.1f, _ => true, _ => [World(BaitSpot())]);
            Queue(35.6f, p => !roll.IsBlue || !inner || state!.BeyondDefenseTarget is not null, MonitorStage, sprint: true);
            if (!roll.IsBlue) Queue(37.8f, _ => true, _ => [World(Corner())]);
            Queue(43.5f, _ => true, _ => [World(SwivelSpot())]);
            if (!roll.IsBlue) Queue(43.5f, NoTwiceComeRuinLeft, _ => [World(Collapse())]);
            Queue(56.1f, _ => true, _ => [World(End())]);
            if (!roll.IsBlue) Queue(56.2f, NoHelloVulnerabilityLeft, _ => [World(WalkTogether())]);
            if (roll.Seat == MainTank) Queue(58f, BothOuterGreensBroke, _ => [World(new Vector2(0f, -5.7f * SafeHalf))]);
        }

        private void Queue(float notBefore, Func<ScenarioProbe, bool> ready, Func<ScenarioProbe, Vector2[]> route, bool sprint = false)
        {
            legs.Enqueue(new Leg(notBefore, ready, route, sprint));
            Legs++;
        }

        private static Vector2 Opening(PartyRole role) => role switch
        {
            MainTank => new(-2.1f, -5.08f), OffTank => new(2.1f, -5.08f),
            RegenHealer => new(-0.7f, 5.7f), ShieldHealer => new(-0.7f, 6.5f), MeleeDpsA => new(-0.7f, 7.3f),
            MeleeDpsB => new(0.7f, 5.7f), PhysRangedDps => new(0.7f, 6.5f), _ => new(0.7f, 7.3f),
        };

        // ---- The line-up: its band by the healer rule (or the band the other pair leaves), its side opposite its partner's.

        private PartyRole[] OtherPair => roll.TetherOrder.Skip(Colour).Take(4).Where(r => r != roll.Seat && r != roll.Partner).ToArray();

        private bool LineUpPartnersSettled(ScenarioProbe p)
            => (roll.DecidesItsBand ? new[] { roll.Partner } : [.. OtherPair, roll.Partner]).All(r => SettledOnALineUpSpot(p, r) is not null);

        private int? SettledOnALineUpSpot(ScenarioProbe p, PartyRole role)
        {
            var at = Flat(p.Member(role)!.Position);
            if (lastSeen.TryGetValue(role, out var before) && Vector2.Distance(before, at) > StillPerFrame) return null;
            var spot = Enumerable.Range(Colour, 4).MinBy(i => Vector2.Distance(World(LineUp[i]!.Value), at));
            return Vector2.Distance(World(LineUp[spot]!.Value), at) <= Settled ? spot - Colour : null;
        }

        private Vector2 LineUpSpot(ScenarioProbe p)
        {
            var partnerSpot = SettledOnALineUpSpot(p, roll.Partner) ?? 0;
            var partnerInner = partnerSpot < 2;
            if (roll.DecidesItsBand)
            {
                inner = roll.Healers == Healers.ForcedBand ? roll.Inner : TheHealerRuleSaysInner();
                if (partnerInner != inner)
                    Problems.Add($"its partner lined up {Band(partnerInner)}, the human read its pair {Band(inner)}");
            }
            else
                inner = OtherPair.All(r => SettledOnALineUpSpot(p, r) is >= 2);
            side = partnerSpot % 2 == 0 ? 1 : -1;
            return LineUp[Colour + (inner ? 0 : 2) + (side > 0 ? 1 : 0)]!.Value;
        }

        private bool TheHealerRuleSaysInner() => IsHealer(roll.Seat) || IsHealer(roll.Partner);

        private static string Band(bool inner) => inner ? "inner" : "outer";

        private void CheckTheLineUp(ScenarioProbe p)
        {
            if (checkedTheLineUp) return;
            checkedTheLineUp = true;
            var mine = Array.IndexOf(ExpectedLineUp(state!), roll.Seat) - Colour < 2;
            if (mine != inner) Problems.Add($"the strat's rule puts the human's pair {Band(mine)}, the human stood {Band(inner)}");
            if (mine != roll.Inner) Problems.Add($"the roll meant the human's pair to be {Band(roll.Inner)}, the strat put it {Band(mine)}");
            foreach (var other in OtherPair)
                if (SettledOnALineUpSpot(p, other) is { } spot && spot < 2 == inner)
                    Problems.Add($"{other} of the other pair lined up {Band(spot < 2)} too");
        }

        // ---- The fists: an inner player swaps sides when its fist matches the outer player's on its side.

        // Read off where the outer pair stands: they never move between the line-up and the drop.
        private PartyRole OuterOnMySide()
        {
            var outerSpot = World(LineUp[Colour + 2]!.Value with { Y = 2.5f * side });
            PartyRole[] outerPair = inner ? OtherPair : [roll.Seat, roll.Partner];
            return outerPair.MinBy(r => Vector2.Distance(lastSeen.GetValueOrDefault(r), outerSpot));
        }

        private Vector2 setUp;

        private Vector2 FistSetUpSpot()
        {
            if (inner && roll.FistOf(roll.Seat) == roll.FistOf(OuterOnMySide())) side = -side;
            setUp = roll.IsBlue ? new Vector2(-13.5f, (inner ? 6.5f : 2.5f) * side) : new Vector2(9.64f, 9.64f * side);
            return setUp;
        }

        private void CheckTheFlex(ScenarioProbe p)
        {
            if (checkedTheFlex) return;
            checkedTheFlex = true;
            if (MathF.Sign(EyeFrameOf(p, roll.Partner).Y) == side)
                Problems.Add($"its partner {roll.Partner} took the human's side after the fists");
        }

        // ---- The baits: the inner blues mid for Beyond Defense, the others beside their arm, away from its turn.

        private Vector2 BaitSpot()
        {
            if (roll.IsBlue && inner) return new Vector2(0f, 5f * side);
            var bearing = (roll.IsBlue, inner, side < 0) switch
            {
                (true, _, true) => 300f, (true, _, false) => 240f,
                (false, true, true) => 0f, (false, true, false) => 180f,
                (false, false, true) => 60f, _ => 120f,
            };
            var arm = Enumerable.Range(0, 6).Single(i => ArmBearing(i, roll.Eye) == bearing);
            return OnRing(bearing + ArmBaitDegrees * roll.Arms[arm].Mul, ArmBaitRadius);
        }

        // ---- The monitors: the greens stage inside every first line, then take their corner once it has fired; the
        // blues stack in the half the Final spares, the holder at the front stepping sideways so its own monitor points
        // outward (a Right monitor faces the Final when the far half is rel S), the Beyond Defense target 13 y toward the
        // Final; holding the monitor too, it ends its step at 13 y or 1.2 y short of it. Sidestepping as it arrives, the
        // human stands on its final spot from well before the first lines.

        private Vector2[] MonitorStage(ScenarioProbe p)
        {
            if (!roll.IsBlue) return [World(new Vector2(inner ? 0f : 9f, (inner ? 12f : 10f) * side))];
            var beyondDefense = state!.BeyondDefenseTarget == roll.Seat;
            if (!Holds(roll.Seat)) return [World(new Vector2(beyondDefense ? 13f : 0f, 3.2f * BlueHalf))];
            var faceTowardTheFinal = (roll.PlayerMonitor == Side.Right) == (BlueHalf > 0);
            var step = new Vector2(faceTowardTheFinal ? 1.2f : -1.2f, 0f);
            var final = beyondDefense ? new Vector2(faceTowardTheFinal ? 13f : 11.8f, 5.1f * BlueHalf) : new Vector2(0f, 5.1f * BlueHalf) + step;
            return [World(final - step), World(final)];
        }

        private Vector2 Corner() => new(inner ? -9.3f : 9.3f, 13f * side);

        // ---- The Swivel Cannon and Hello World.

        private bool OnTheSafeHalf => side == SafeHalf;

        private Vector2 SwivelSpot()
        {
            Vector2 spot;
            if (roll.IsBlue)
                spot = state!.NearWorldRole == roll.Seat ? new(0f, 6f) : state.FarWorldRole == roll.Seat ? new(0f, 18.5f) : new(9.5f, 16f);
            else if (inner)
                spot = OnTheSafeHalf ? new(-9.64f, 9.64f) : new(-9.6f, -3.4f);
            else
                spot = OnTheSafeHalf ? new(15.5f, 10.7f) : new(-18.5f, 1.5f);
            return spot with { Y = spot.Y * SafeHalf };
        }

        private Vector2 Collapse() => inner && !OnTheSafeHalf ? new Vector2(-9.6f, 4f * SafeHalf) : SwivelSpot();

        private static bool NoTwiceComeRuinLeft(ScenarioProbe p)
            => PerRole.All.All(r => !p.Member(r).IsAlive() || !p.Member(r)!.HasStatus(StatusId.TwiceComeRuin));

        private Vector2 End()
            => !roll.IsBlue && !inner ? new Vector2(OnTheSafeHalf ? 8.8f : -8.8f, -0.5f * SafeHalf) : new Vector2(0f, 6.5f * SafeHalf);

        private static bool NoHelloVulnerabilityLeft(ScenarioProbe p)
            => PerRole.All.All(r => !p.Member(r).IsAlive() || !p.Member(r)!.HasStatus(StatusId.MagicVulnerabilityUp));

        private Vector2 WalkTogether() => !roll.IsBlue && !inner ? new Vector2(OnTheSafeHalf ? 4f : -4f, -0.5f * SafeHalf) : End();

        private bool BothOuterGreensBroke(ScenarioProbe p)
            => roll.TetherOrder.Skip(4).All(r => !p.Member(r).IsAlive() || !p.Member(r)!.HasStatus(StatusId.HWLocalTether));

        // ---- Walking.

        private void Advance(ScenarioProbe p, SimCharacter me)
        {
            if (legs.TryPeek(out var next))
            {
                if (p.Time >= next.NotBefore) readySince ??= next.Ready(p) ? p.Time : null;
                if (readySince is { } ready && p.Time >= ready + late)
                {
                    legs.Dequeue();
                    readySince = null;
                    Walks++;
                    var from = Flat(me.Position);
                    var route = Curve(from, next.Route(p));
                    var sprint = roll.Walk == Walk.Sprinting || next.Sprint && roll.IsBlue && !inner && Holds(roll.Seat);
                    walking = (p.Time, [from, .. route], sprint ? AiManager.SprintSpeed : RunSpeed);
                }
            }
            if (walking is not { } w) return;
            var (at, heading, arrived) = Along(w.Points, (p.Time - w.Depart) * w.Speed);
            me.SetPosition(new Placement(new Vector3(at.X, 0f, at.Y), MathF.Atan2(heading.X, heading.Y)));
            if (arrived) walking = null;
        }

        // A curving walk bulges one to two yalms off its first leg; a sidestep after it stays straight.
        private Vector2[] Curve(Vector2 from, Vector2[] route)
        {
            var to = route[0];
            var length = Vector2.Distance(from, to);
            if (roll.Walk != Walk.Curving || length <= 1f) return route;
            var bulge = Math.Min(LeastCurve + (MostCurve - LeastCurve) * (float)rng.NextDouble(), length / 4f);
            var normal = new Vector2(-(to - from).Y, (to - from).X) / length * (rng.Next(2) == 0 ? -1f : 1f);
            return [.. Enumerable.Range(1, 16).Select(i => i / 16f).Select(u => from + (to - from) * u + normal * (bulge * 4f * u * (1f - u))), .. route[1..]];
        }

        private static (Vector2 At, Vector2 Heading, bool Arrived) Along(Vector2[] points, float walked)
        {
            var heading = new Vector2(0f, 1f);
            for (var i = 1; i < points.Length; i++)
            {
                var leg = Vector2.Distance(points[i - 1], points[i]);
                if (leg > 1e-4f) heading = (points[i] - points[i - 1]) / leg;
                if (walked <= leg) return (leg < 1e-4f ? points[i] : Vector2.Lerp(points[i - 1], points[i], walked / leg), heading, false);
                walked -= leg;
            }
            return (points[^1], heading, true);
        }
    }

    // How close the human came to each hit it dodged (or how deep it stood in one it had to take), as each resolved,
    // over every run of a case: the scenario's own areas, read off AoeQuery.
    private sealed class Clearances
    {
        private readonly Dictionary<string, (float Clearance, string Where)> tightest = new();
        private ScenarioProbe? probe;
        private Roll? playing;
        private readonly HashSet<(float, uint, Placement)> seen = [];

        public void Watch(ScenarioProbe p) => probe = p;

        public Action<AoeQuery> For(Roll roll)
        {
            var seat = roll.Seat;
            seen.Clear();
            probe = null;
            playing = roll;
            return hit =>
            {
                if (probe is not { } p || p.Member(seat) is not { } me || !me.IsAlive()) return;
                if (!seen.Add((p.World.Events.Elapsed, hit.ActionId, hit.Source))) return;
                var on = Vector2.Distance(Flat(hit.Source.Position), Flat(me.Position)) < 0.01f;
                var clearance = hit.SignedDistance(me.Position) ?? float.NaN;
                switch (hit.ActionId)
                {
                    case ActionId.OpticalLaser: Note("the Optical Laser", clearance); break;
                    case ActionId.HyperPulseDeltaCharging or ActionId.HyperPulseDeltaShoot: Note("Hyper Pulse", clearance); break;
                    case ActionId.OversampledWaveCannonAoe when !on: Note("another's monitor", clearance); break;
                    case ActionId.PilePitch when Array.IndexOf(Scenario(p).LastState!.TetherOrder.ToArray(), seat) < 4
                                                 && Scenario(p).LastState!.BeyondDefenseTarget != seat:
                        Note("inside Pile Pitch", -clearance);
                        break;
                    case ActionId.PilePitch: Note("outside Pile Pitch", clearance); break;
                    case ActionId.SwivelCannonL or ActionId.SwivelCannonR: Note("the Swivel Cannon", clearance); break;
                    case ActionId.HelloNearWorld or ActionId.HelloNearWorldJump or ActionId.HelloDistantWorld
                        or ActionId.HelloDistantWorldJump when !on:
                        Note("another's Hello World", clearance);
                        break;
                }
            };
        }

        private void Note(string name, float clearance)
        {
            if (tightest.TryGetValue(name, out var before) && before.Clearance <= clearance) return;
            tightest[name] = (clearance, $"t={probe!.Time.ToString("F2", CultureInfo.InvariantCulture)} {playing}");
        }

        public IEnumerable<string> Within(float slack)
            => tightest.Where(t => !(t.Value.Clearance > slack))
                       .Select(t => $"{t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})");

        public override string ToString()
            => string.Join(Environment.NewLine, tightest.OrderBy(t => t.Value.Clearance)
                                                        .Select(t => $"  {t.Key} {t.Value.Clearance.ToString("F2", CultureInfo.InvariantCulture)} ({t.Value.Where})"));
    }
}
