using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Intermission;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat with the bots' own AI. Here the seat is a human who
// plays the guides from what a player can see: its own debuff, its sign (with Automarkers Off, which
// bots step back or forward), which three arms warp in first, each ring as it lands and the void
// zone going. At each cue it walks straight to the next spot of the dodge, a fixed reaction time
// late, and never steps round anyone. Whatever its seat, debuff and pattern, nobody may die.
public class TopP3IntermissionLpduHumanTests
{
    private const int LpduStrat = 0;

    // Four fixed rolls per seat; HumanSeeds asks for more, as SunnySeeds does for the soak.
    private static IEnumerable<int> Seeds => Enumerable.Range(1, TestContext.Parameters.Get("HumanSeeds", 4)).Select(i => 13 * i);
    private static readonly float[] ReactionTimes = [0f, 0.5f, 1f];

    private static IEnumerable<TestCaseData> Matrix()
        => from debuff in Enum.GetValues<SniperDebuff>()
           from arms in Enum.GetValues<FirstArms>()
           from markers in Enum.GetValues<Automarkers>()
           from delay in ReactionTimes
           select new TestCaseData(debuff, arms, markers, delay)
               .SetName($"{{m}}({debuff}, {arms}, automarkers {markers}, {delay:F1}s late)");

    [TestCaseSource(nameof(Matrix))]
    public void AHumanPlayingTheGuidesIsNeverCaught(SniperDebuff debuff, FirstArms arms, Automarkers markers, float delay)
    {
        var tightest = new Clearances();
        var problems = (from seat in PerRole.All
                        from seed in Seeds
                        from problem in Play(seat, debuff, arms, markers, delay, seed, tightest)
                        select problem).ToList();

        TestContext.Out.WriteLine($"the human's tightest clearances: {tightest}");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    private static List<string> Play(PartyRole seat, SniperDebuff debuff, FirstArms arms, Automarkers markers, float delay, int seed,
                                     Clearances tightest)
    {
        var order = DebuffOrder(seat, debuff, seed);
        var groups = Groups(order);
        var guide = new Guide(seat, delay);
        var problems = new List<string>();
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = seat,
            Overrides = o => TopP3IntermissionLpduAiTests.Pin((TopP3IntermissionStateOverrides)o, arms, markers, order),
            Takeovers = [new Takeover(0f, null)],
            Probe = p =>
            {
                guide.Tick(p);
                tightest.Measure(p, seat, groups, FirstSet(arms));
                problems.AddRange(BotsOffTheirFinalSpots(p, seat, groups, arms));
            },
        });

        if (!run.Passed) problems.Add(run.ToString());
        if (guide.Group != groups[seat])
            problems.Add($"the human read its group as {guide.Group?.ToString() ?? "nothing"}, the strat puts it in {groups[seat]}");
        if (guide.ArmsRead != arms)
            problems.Add($"the human read the first arms as {guide.ArmsRead?.ToString() ?? "nothing"}");
        if (run.Passed && guide.Walks < guide.Legs)
            problems.Add($"the human set off on {guide.Walks} of its {guide.Legs} walks");
        return problems.Select(problem => $"seed {seed}, {seat} ({debuff}, {arms}, automarkers {markers}, {delay:F1}s late): {problem}")
                       .ToList();
    }

    // Pinned, so the test knows every group without reading the scenario; the human still reads its own.
    private static PartyRole[] DebuffOrder(PartyRole seat, SniperDebuff debuff, int seed)
    {
        var rng = new Random(seed * 8 + (int)seat);
        var others = PerRole.All.Where(r => r != seat).OrderBy(_ => rng.Next()).ToList();
        var (first, count) = debuff switch
        {
            SniperDebuff.Spread => (0, TopP3IntermissionState.Spreads),
            SniperDebuff.Stack => (TopP3IntermissionState.Spreads, TopP3IntermissionState.Stacks),
            _ => (TopP3IntermissionState.Spreads + TopP3IntermissionState.Stacks, TopP3IntermissionState.Unmarked),
        };
        others.Insert(first + rng.Next(count), seat);
        return [.. others];
    }

    internal sealed record Group(SniperDebuff Debuff, int Number)
    {
        public float WallBearing => Debuff == SniperDebuff.Spread ? SpreadWalls[Number] : StackWalls[Number];

        public override string ToString() => $"{Debuff} {Number + 1}";
    }

    private static readonly float[] SpreadWalls = [270f, 210f, 150f, 90f];
    private static readonly float[] StackWalls = [330f, 30f];

    internal static readonly PartyRole[] CongaLine =
        [PhysRangedDps, MeleeDpsA, MainTank, RegenHealer, ShieldHealer, OffTank, MeleeDpsB, CasterDps];

    // Numbered west to east along the conga within each debuff; nothing k joins stack k.
    internal static Dictionary<PartyRole, Group> Groups(IReadOnlyList<PartyRole> debuffOrder)
    {
        SniperDebuff DebuffOf(PartyRole role)
        {
            var i = debuffOrder.ToList().IndexOf(role);
            return i < TopP3IntermissionState.Spreads ? SniperDebuff.Spread
                : i < TopP3IntermissionState.Spreads + TopP3IntermissionState.Stacks ? SniperDebuff.Stack
                : SniperDebuff.Nothing;
        }
        return Enum.GetValues<SniperDebuff>()
                   .SelectMany(debuff => CongaLine.Where(r => DebuffOf(r) == debuff).Select((role, n) => (role, group: new Group(debuff, n))))
                   .ToDictionary(t => t.role, t => t.group);
    }

    internal static float[] FirstSet(FirstArms arms) => arms == FirstArms.NorthTriangle ? [0f, 120f, 240f] : [60f, 180f, 300f];

    internal static float[] SecondSet(FirstArms arms) => FirstSet(arms == FirstArms.NorthTriangle ? FirstArms.SouthTriangle : FirstArms.NorthTriangle);

    // The guides' dodge from a wall bearing b: lean 10.8 degrees towards the second-set arm beside it
    // to wait, step in to r 16.77 behind ring 3, then cross 15.7 degrees into the first arm's spent circle.
    internal static Vector2 Wait(float wall, float[] firstSet) => OnRing(wall + 10.8f * TowardsTheSecondArms(wall, firstSet), 18.73f);

    internal static Vector2 In(float wall, float[] firstSet) => OnRing(wall + 10.3f * TowardsTheSecondArms(wall, firstSet), 16.77f);

    internal static Vector2 Final(float wall, float[] firstSet) => OnRing(wall - 15.7f * TowardsTheSecondArms(wall, firstSet), 16.62f);

    private static float TowardsTheSecondArms(float wall, float[] firstSet)
        => firstSet.Any(b => MathF.Abs(Wrap180(b - (wall - 30f))) < 1f) ? 1f : -1f;

    private static Vector2 CongaSpot(PartyRole role, float z) => new(-9f + 2.57f * Array.IndexOf(CongaLine, role), z);

    private const float CongaZ = 9.6f, SpreadStepZ = 13.6f, StackStepZ = 5.6f, SteppedFrom = 2f;

    private static readonly Dictionary<PartyRole, float> LooseSpreadBearings = new()
    {
        [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
    };

    // Each cue queues a walk that sets off a reaction time later and is aimed when it sets off. The one
    // allowance: a human a second late into the spent circle sprints, as the guides tell everyone to
    // there. At a run it is still inside a second arm's reach as those blows snapshot, and dies.
    private sealed class Guide(PartyRole seat, float late)
    {
        private const float RunSpeed = 6.5f;
        private const float SprintWhenThisLate = 1f;

        public Group? Group { get; private set; }
        public FirstArms? ArmsRead { get; private set; }
        public int Legs { get; private set; }
        public int Walks { get; private set; }

        private readonly Queue<(float Depart, Func<Vector2> To, bool Sprint)> legs = new();
        private (float Depart, Vector2 From, Vector2 To, float Speed)? walking;
        private SniperDebuff? debuff;
        private float[] firstSet = [];
        private Dictionary<string, float>? armBearings;
        private int rowsRead, ring3s, ring4s;
        private bool voidZoneSeen, voidZoneGone;

        public void Tick(ScenarioProbe p)
        {
            var me = p.Member(seat)!;
            if (!me.IsAlive()) return;
            if (p.Crossed(0.5f)) Queue(p.Time, () => CongaSpot(seat, CongaZ));
            ReadTheDebuffs(p, me);
            ReadTheGroup(p, me);
            ReadTheTimeline(p);
            ReadTheVoidZone(p);
            Advance(p, me);
        }

        private void Queue(float cue, Func<Vector2> to, bool sprint = false)
        {
            legs.Enqueue((cue + late, to, sprint));
            Legs++;
        }

        // Everyone's fodder shows on the party list at once, so having none is a read too.
        private void ReadTheDebuffs(ScenarioProbe p, SimCharacter me)
        {
            if (debuff is not null || !PerRole.All.Any(r => HasFodder(p.Member(r)))) return;
            debuff = me.HasStatus(P3StatusId.SniperCannonFodder) ? SniperDebuff.Spread
                : me.HasStatus(P3StatusId.HighPoweredSniperCannonFodder) ? SniperDebuff.Stack
                : SniperDebuff.Nothing;
            if (debuff != SniperDebuff.Nothing)
                Queue(p.Time, () => CongaSpot(seat, debuff == SniperDebuff.Spread ? SpreadStepZ : StackStepZ));
        }

        private static bool HasFodder(SimCharacter? member)
            => member is not null && (member.HasStatus(P3StatusId.SniperCannonFodder) || member.HasStatus(P3StatusId.HighPoweredSniperCannonFodder));

        private void ReadTheGroup(ScenarioProbe p, SimCharacter me)
        {
            if (Group is not null || debuff is not { } mine) return;
            Group = MySign(p, me) is { } sign ? FromSign(sign) : FromTheBotsSteps(p, mine);
            if (Group is { } group)
                Queue(p.Time, () => OnRing(group.WallBearing, 18.73f));
        }

        private Sign? MySign(ScenarioProbe p, SimCharacter me)
        {
            var id = me.GameObjectId.ObjectId.ToString("X8");
            return p.Fake.Timeline.Rows.Where(r => r["kind"] == "ac_target" && r["id"] == "0x1F6" && r["target"] == id)
                    .Select(r => (Sign?)(Sign)Convert.ToUInt32(r["p"].Split(',')[0], 16))
                    .LastOrDefault();
        }

        private static Group? FromSign(Sign sign) => sign switch
        {
            >= Sign.Attack1 and <= Sign.Attack4 => new Group(SniperDebuff.Spread, sign - Sign.Attack1),
            Sign.Bind1 or Sign.Bind2 => new Group(SniperDebuff.Stack, sign - Sign.Bind1),
            Sign.Ignore1 or Sign.Ignore2 => new Group(SniperDebuff.Nothing, sign - Sign.Ignore1),
            _ => null,
        };

        // With no signs, a player counts the bots that stepped like it did, west of its own slot,
        // once every bot that will step has.
        private Group? FromTheBotsSteps(ScenarioProbe p, SniperDebuff mine)
        {
            var bots = PerRole.All.Where(r => r != seat && p.Member(r).IsAlive())
                              .Select(r => (At: Flat(p.Member(r)!.Position), Debuff: Stepped(Flat(p.Member(r)!.Position))))
                              .ToList();
            var spreads = bots.Count(b => b.Debuff == SniperDebuff.Spread);
            var stacks = bots.Count(b => b.Debuff == SniperDebuff.Stack);
            if (spreads != TopP3IntermissionState.Spreads - (mine == SniperDebuff.Spread ? 1 : 0)
                || stacks != TopP3IntermissionState.Stacks - (mine == SniperDebuff.Stack ? 1 : 0))
                return null;
            var myX = CongaSpot(seat, CongaZ).X;
            return new Group(mine, bots.Count(b => b.Debuff == mine && b.At.X < myX));
        }

        private static SniperDebuff Stepped(Vector2 at)
            => at.Y > CongaZ + SteppedFrom ? SniperDebuff.Spread : at.Y < CongaZ - SteppedFrom ? SniperDebuff.Stack : SniperDebuff.Nothing;

        // The first arms show together; then rings 3 and 4 of the first wave, and ring 3 of the second.
        private void ReadTheTimeline(ScenarioProbe p)
        {
            armBearings ??= p.World.Children.OfType<SimEnemy>()
                             .Where(e => e.BNpcBaseId is BNpcBaseId.LeftArmUnit or BNpcBaseId.RightArmUnit)
                             .ToDictionary(e => e.EntityId.ToString("X8"), e => Bearing(Flat(e.Position)));
            var rows = p.Fake.Timeline.Rows;
            var warped = new List<float>();
            for (; rowsRead < rows.Count; rowsRead++)
            {
                var row = rows[rowsRead];
                if (row["kind"] == "ac" && row["id"] == "0x197" && armBearings.TryGetValue(row["actor"], out var bearing))
                    warped.Add(bearing);
                if (row["kind"] != "ability") continue;
                if (row["id"] == $"{P3ActionId.WaveRepeaterRing3}" && ++ring3s == 1)
                    Queue(p.Time, () => In(Group!.WallBearing, firstSet));
                else if (row["id"] == $"{P3ActionId.WaveRepeaterRing3}" && ring3s == 2)
                    Queue(p.Time, () => Final(Group!.WallBearing, firstSet), sprint: late >= SprintWhenThisLate);
                else if (row["id"] == $"{P3ActionId.WaveRepeaterRing4}" && ++ring4s == 1)
                    Queue(p.Time, () => Wait(Group!.WallBearing, firstSet));
            }
            if (ArmsRead is not null || warped.Count == 0) return;
            firstSet = [.. warped.Order()];
            ArmsRead = firstSet.Any(b => MathF.Abs(Wrap180(b)) < 1f)
                ? FirstArms.NorthTriangle
                : FirstArms.SouthTriangle;
            Queue(p.Time, () => Wait(Group!.WallBearing, firstSet));
        }

        private void ReadTheVoidZone(ScenarioProbe p)
        {
            var shown = p.World.Children.OfType<SimEventObject>().Any(o => o.EObjRowId == P3EObjId.VoidZone && o.IsAlive);
            voidZoneSeen |= shown;
            if (!voidZoneSeen || shown || voidZoneGone) return;
            voidZoneGone = true;
            Queue(p.Time, () => OnRing(LooseSpreadBearings[seat], 8.5f));
        }

        private void Advance(ScenarioProbe p, SimCharacter me)
        {
            if (legs.TryPeek(out var next) && p.Time >= next.Depart)
            {
                legs.Dequeue();
                Walks++;
                walking = (p.Time, Flat(me.Position), next.To(), next.Sprint ? AiManager.SprintSpeed : RunSpeed);
            }
            if (walking is not { } w) return;
            var length = Vector2.Distance(w.From, w.To);
            var walked = (p.Time - w.Depart) * w.Speed;
            var at = walked >= length ? w.To : Vector2.Lerp(w.From, w.To, walked / length);
            me.SetPosition(new Vector3(at.X, 0f, at.Y));
            if (walked >= length) walking = null;
        }
    }

    // However the human walked, every bot stands on its group's final spot as the cannons go off.
    private const float CannonsAt = 28.3f, OffSpotTolerance = 0.75f;

    private static IEnumerable<string> BotsOffTheirFinalSpots(ScenarioProbe p, PartyRole seat, Dictionary<PartyRole, Group> groups, FirstArms arms)
    {
        if (!p.Crossed(CannonsAt)) return [];
        return PerRole.All.Where(r => r != seat && p.Member(r).IsAlive())
                      .Select(r => (Role: r, Off: Vector2.Distance(Flat(p.Member(r)!.Position), Final(groups[r].WallBearing, FirstSet(arms)))))
                      .Where(b => b.Off > OffSpotTolerance)
                      .Select(b => $"{b.Role} stood {b.Off:F2} y off its {groups[b.Role]} final spot as the cannons went off")
                      .ToList();
    }

    // How close the human came to each hit it dodged, at the hit's snapshot, over every run of a case.
    private sealed class Clearances
    {
        private readonly Dictionary<string, float> tightest = new();

        public void Measure(ScenarioProbe p, PartyRole seat, Dictionary<PartyRole, Group> groups, float[] firstSet)
        {
            var me = p.Member(seat);
            if (!me.IsAlive()) return;
            var at = Flat(me!.Position);
            var r = at.Length();
            if (p.Time is >= 15.3f and < 30.7f) Note("void zone", r - P3Geometry.VoidZoneRadius);
            foreach (var (time, name, inner, outer) in Rings)
                if (p.Crossed(time)) Note(name, MathF.Max(inner - r, r - outer));
            if (p.Crossed(26.1f)) Note("set 1 blows", NearestArm(at, firstSet) - BlowRadius);
            if (p.Crossed(28.6f)) Note("set 2 blows", NearestArm(at, SecondSetOf(firstSet)) - BlowRadius);
            if (p.Crossed(CannonsAt)) Note("cannons", Cannons(p, seat, groups));
        }

        private static readonly (float Time, string Name, float Inner, float Outer)[] Rings =
        [
            (14.0f, "ring 1", float.NegativeInfinity, 6f), (16.4f, "ring 2", 6f, 12f), (18.4f, "ring 3", 12f, 18f),
            (20.4f, "ring 4", 18f, 24f), (22.1f, "ring 5", float.NegativeInfinity, 6f), (24.5f, "ring 6", 6f, 12f),
            (26.6f, "ring 7", 12f, 18f), (28.7f, "ring 8", 18f, 24f),
        ];

        private const float BlowRadius = 11f, CannonRadius = 6f;

        // Inside its own group's circles, outside everyone else's, and its own spread clear of the rest.
        private static float Cannons(ScenarioProbe p, PartyRole seat, Dictionary<PartyRole, Group> groups)
        {
            var me = Flat(p.Member(seat)!.Position);
            var tightest = float.MaxValue;
            foreach (var other in PerRole.All.Where(o => o != seat && p.Member(o).IsAlive()))
            {
                var holder = groups[other].Debuff != SniperDebuff.Nothing || groups[seat].Debuff != SniperDebuff.Nothing;
                if (!holder) continue;
                var d = Vector2.Distance(me, Flat(p.Member(other)!.Position));
                var together = groups[other].WallBearing == groups[seat].WallBearing;
                tightest = MathF.Min(tightest, together ? CannonRadius - d : d - CannonRadius);
            }
            return tightest;
        }

        private void Note(string name, float clearance)
            => tightest[name] = MathF.Min(tightest.GetValueOrDefault(name, float.MaxValue), clearance);

        public override string ToString()
            => string.Join(", ", tightest.OrderBy(t => t.Value).Select(t => $"{t.Key} {t.Value.ToString("F2", CultureInfo.InvariantCulture)}"));
    }

    private static float[] SecondSetOf(float[] firstSet) => [.. firstSet.Select(b => (b + 60f) % 360f).Order()];

    internal static float NearestArm(Vector2 at, IEnumerable<float> bearings)
        => bearings.Min(b => Vector2.Distance(at, OnRing(b, P3Geometry.ArmRadius)));

    internal static float Bearing(Vector2 at) => (MathF.Atan2(at.X, -at.Y) * 180f / MathF.PI + 360f) % 360f;

    private static float Wrap180(float degrees) => ((degrees % 360f) + 540f) % 360f - 180f;

    internal static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
