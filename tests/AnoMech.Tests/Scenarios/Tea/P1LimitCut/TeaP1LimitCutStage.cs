using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Tea.P1LimitCut;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.TeaLane;

namespace AnoMech.Tests;

// Where a number stands on one of its group's spots: the pair hit next on the inner ring, the other pair at the rim.
internal enum TeaLane { InnerOdd, InnerEven, OuterOdd, OuterEven }

// Limit Cut played with no strat, so its rules can be tested apart from one: the scenario runs with a strat index past
// its list, which seats all eight and moves nobody, and this puts every seat, by teleport, where it is safe, a frame
// after each cue. A test then moves one seat into a mistake.
//
// Numbers 1, 2, 5, 6 take the first pair's blaster in the west-to-north-east half and 3, 4, 7, 8 the opposite one.
// A group's spot k is 45k degrees clockwise of its first and has gone off by the time the group stands on it; the
// odd number about to be hit faces clockwise along the rim, so Cruise Chaser lands counter-clockwise of it and
// cleaves on along the rim, and his charge runs back counter-clockwise through the even number. On a spot at
// bearing b the pair hit next stands 14 y out, the odd at b - 2 and the even at b - 30, and the group's other pair
// 19.3 y out at b - 4 and b - 8.
//
//   group A   spot 2 from 12.5: #1 cleaved 15.4, #2 charged 16.9.   spot 5 from 21.7: #5 at 24.8, #6 at 26.3.
//   group B   spot 3 from 14.7: #3 cleaved 20.1, #4 charged 21.6.   spot 6 from 24.25: #7 at 29.4, #8 at 30.9.
//
// Group A leaves its spot 4 only after #4's charge: that charge runs through group A's spot 5 when Cruise Chaser
// lands 4.5 y behind #3, though not at 3 y.
//
// He lands 3 y behind unless a test says otherwise, whatever the scenario's own distance is tuned to.
internal sealed class TeaP1LimitCutStage(TeaP1FirstBlasters axis = TeaP1FirstBlasters.WestEast, float landsBehind = 3f)
{
    // A strat index past the scenario's list.
    public const int NoStrat = int.MaxValue;

    public const float InnerRadius = 14f;
    public const float OuterRadius = 19.3f;

    // #1 M1, #2 R1, #3 MT, #4 M2, #5 R2, #6 H1, #7 H2, #8 OT.
    public static readonly PartyRole[] Numbers = [MeleeDpsA, PhysRangedDps, MainTank, MeleeDpsB, CasterDps, RegenHealer, ShieldHealer, OffTank];

    private static readonly Dictionary<int, (float At, TeaLane Lane, int Spot)[]> Walks = new()
    {
        [1] = [(8.1f, InnerOdd, 0), (10.3f, InnerOdd, 1), (12.5f, InnerOdd, 2), (15.5f, OuterOdd, 3), (19.1f, OuterOdd, 4), (21.7f, OuterOdd, 5), (26.4f, OuterOdd, 6)],
        [2] = [(8.1f, InnerEven, 0), (10.3f, InnerEven, 1), (12.5f, InnerEven, 2), (17.0f, OuterEven, 3), (19.1f, OuterEven, 4), (21.7f, OuterEven, 5), (26.4f, OuterEven, 6)],
        [5] = [(8.1f, OuterOdd, 0), (10.3f, OuterOdd, 1), (12.5f, OuterOdd, 2), (17.0f, InnerOdd, 3), (19.1f, InnerOdd, 4), (21.7f, InnerOdd, 5), (24.9f, OuterOdd, 6)],
        [6] = [(8.1f, OuterEven, 0), (10.3f, OuterEven, 1), (12.5f, OuterEven, 2), (17.0f, InnerEven, 3), (19.1f, InnerEven, 4), (21.7f, InnerEven, 5), (26.4f, OuterEven, 6)],
        [3] = [(8.1f, InnerOdd, 0), (10.3f, InnerOdd, 1), (12.5f, InnerOdd, 2), (14.7f, InnerOdd, 3), (20.2f, OuterOdd, 4), (22.5f, OuterOdd, 5), (24.9f, OuterOdd, 6)],
        [4] = [(8.1f, InnerEven, 0), (10.3f, InnerEven, 1), (12.5f, InnerEven, 2), (14.7f, InnerEven, 3), (21.7f, InnerEven, 4), (23.6f, OuterEven, 5), (26.6f, OuterEven, 6)],
        [7] = [(8.1f, OuterOdd, 0), (10.3f, OuterOdd, 1), (12.5f, OuterOdd, 2), (14.7f, OuterOdd, 3), (20.2f, InnerOdd, 4), (22.5f, InnerOdd, 5), (24.25f, InnerOdd, 6), (29.5f, OuterOdd, 6)],
        [8] = [(8.1f, OuterEven, 0), (10.3f, OuterEven, 1), (12.5f, OuterEven, 2), (14.7f, OuterEven, 3), (20.2f, InnerEven, 4), (22.5f, InnerEven, 5), (24.25f, InnerEven, 6)],
    };

    private const float GatherAt = 0.05f;
    private const float MiddleAt = 31.0f;

    private static readonly float[] Cues =
        Walks.Values.SelectMany(walk => walk.Select(step => step.At)).Append(GatherAt).Append(MiddleAt).Distinct().Order().ToArray();

    // The player's seat stays on the stage until then though it is a human from the start, pressing its own buttons.
    // Null: it follows the stage until a test first takes it over.
    public float? HumanUntil { get; init; }

    // Only a dead number's hit draws from it: the numbers and the axis are pinned.
    public int? Seed { get; init; }

    public float FirstBearing => (270f + 45f * (int)axis) % 360f;

    public float GroupBearing(int number) => FirstBearing + (number is 3 or 4 or 7 or 8 ? 180f : 0f);

    public Vector2 Spot(int number, TeaLane lane, int spot)
    {
        var bearing = GroupBearing(number) + 45f * spot;
        return lane switch
        {
            InnerOdd => OnRing(bearing - 2f, InnerRadius),
            InnerEven => OnRing(bearing - 30f, InnerRadius),
            OuterOdd => OnRing(bearing - 4f, OuterRadius),
            _ => OnRing(bearing - 8f, OuterRadius),
        };
    }

    // Where `number` stands as its own hit lands.
    public Vector2 HitSpot(int number)
        => Spot(number, number % 2 == 1 ? InnerOdd : InnerEven, number switch { 1 or 2 => 2, 3 or 4 => 3, 5 or 6 => 5, _ => 6 });

    public static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }

    // The facing of someone at `at` who looks clockwise along the rim, in Placement's convention.
    public static float ClockwiseAlongTheRim(Vector2 at) => MathF.PI / 2f - MathF.Atan2(at.X, -at.Y);

    public static Vector2 Facing(float rotation) => new(MathF.Sin(rotation), MathF.Cos(rotation));

    public void Pin(TeaP1LimitCutStateOverrides overrides)
    {
        overrides.Numbers = Numbers;
        overrides.FirstBlasters = axis;
        overrides.CruiseChaserLandsBehind = landsBehind;
    }

    // Ends as a scenario run does: after J Kick, at `stopAt`, or 5 s after the first death.
    public ScenarioRun Run(PartyRole player, float? stopAt = null, Action<ScenarioProbe>? probe = null, params Takeover[] takeovers)
        => ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), NoStrat, Seed ?? Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TeaP1LimitCutStateOverrides)o),
            Takeovers = HumanUntil is null ? takeovers : takeovers.Prepend(new Takeover(0f, null)).ToList(),
            Probe = p =>
            {
                Follow(p);
                probe?.Invoke(p);
            },
        });

    // Called every frame, puts every seat the stage still moves where the cues just crossed ask.
    public void Follow(ScenarioProbe p)
    {
        foreach (var cue in Cues)
            if (p.Crossed(cue)) Put(p, cue);
    }

    private void Put(ScenarioProbe p, float cue)
    {
        for (var number = 1; number <= 8; number++)
        {
            var role = Numbers[number - 1];
            if (p.Member(role) is not { } member || !FollowsTheStage(p, member, cue)) continue;
            if (SpotAt(number, role, cue) is not { } spot) continue;
            member.StopMoving();
            member.SetPosition(new Placement(new Vector3(spot.X, 0f, spot.Y), ClockwiseAlongTheRim(spot)));
        }
    }

    private bool FollowsTheStage(ScenarioProbe p, AnoMech.Core.SimObjects.SimCharacter member, float cue)
    {
        if (!ReferenceEquals(member, p.World.Party.Player)) return true;
        return HumanUntil is { } until ? cue < until : p.World.Party.IsBotDriven(member);
    }

    private Vector2? SpotAt(int number, PartyRole role, float cue)
    {
        if (cue == GatherAt) return OnRing(45f * (int)role, 1f);
        if (cue == MiddleAt) return OnRing(45f * (int)role, 2.5f);
        foreach (var step in Walks[number])
            if (step.At == cue) return Spot(number, step.Lane, step.Spot);
        return null;
    }
}
