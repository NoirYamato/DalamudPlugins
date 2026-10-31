using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P4BlueScreen;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Scenarios.Top.TopP4Constants;
using static AnoMech.Tests.NegativeRun;
using static AnoMech.Tests.TopP4BlueScreenLpduAiTests;

namespace AnoMech.Tests;

// Unless a test says otherwise the markers are pinned split, R1+R2 / H1+H2 / M1+M2, so nobody flexes: the
// rows stand on their 13.5 y quickmarch spots at each aim, and the west light party (MT R1 H1 M1) stacks at
// (-4.97, 12.01) in sets 1 and 3, the east one mirrored (x east, z south).
//
//   3.0   Omega's Wave Cannon cast (5 s). 5.6 / 15.7 / 25.6: the markers.
//   8.0 / 17.9 / 28.0   aims: a repeat line starts down every player's line (snapshot 5 s later).
//   8.6 / 18.5 / 28.6   the beams, down the same lines. 13.6 / 23.4 / 33.5: the line stacks.
//   11.0  Wave Repeater (hits at 15.7, r < 6); rings 18.1 (6-12), 20.1 (12-18), 22.1 (18-24).
//   28.2  Wave Repeater (hits at 32.9); rings 35.2, 37.2, 39.2. The run ends after the last ring.
public class TopP4BlueScreenScenarioTests
{
    private static NegativeRun<TopP4BlueScreenScenario> BlueScreen(PartyRole player, PartyRole[][]? markers = null)
        => Negative<TopP4BlueScreenScenario>(player)
            .Overrides<TopP4BlueScreenStateOverrides>(o => Pin(o, markers ?? SplitMarkers));

    private static readonly Vector2 OutOfEveryLineNorth = new(0, -13);

    // MT stands 1 y from R1 at the aim: each is in the other's line.
    [Test]
    public void TwoPlayersOnOneLineAtTheAimBothDieToTheBeams()
        => BlueScreen(MainTank)
            .TeleportAt(7.5f, to: new(-13.5f, -1f))
            .ShouldKill(P4ActionId.WaveCannonBeam, MainTank, PhysRangedDps);

    // MT leaves its line between the aim and the beams for a spot 1 y from R1, then joins its stack: R1's beam
    // hits MT once, and MT's own beam goes down MT's old line and misses R1. Had MT's beam followed MT, both
    // would have taken two.
    [Test]
    public void LeavingAfterTheAimLeavesTheLinesWhereTheyWere()
    {
        var run = Run(MainTank, SplitMarkers, stopAt: 14.5f,
                      new Takeover(8.2f, new Vector2(-13.5f, -1f)),
                      new Takeover(9f, OuterStackWest));

        Assert.That(run.Passed, run.ToString);
    }

    // M1's line and the west line stack overlap near Omega: MT waits there, in M1's repeat line but still in its
    // own stack. A repeat kills at its snapshot but shows the death a second later, after the stack.
    [TestCase(12.5f)]
    [TestCase(22.5f)]
    [TestCase(32.5f)]
    public void StandingInARepeatLineKills(float at)
        => BlueScreen(MainTank)
            .TeleportAt(at, to: InMeleesRepeatLineAndTheWestStack)
            .ShouldKill(P4ActionId.WaveCannonRepeat, MainTank);

    // r 7 at bearing 213.75, 1.4 y from M1's line and at most 1.7 y from the west line stacks; outside ring 1.
    private static readonly Vector2 InMeleesRepeatLineAndTheWestStack = new(-3.89f, 5.82f);

    // Just south of Omega both lines overlap. The west line still holds four, the east five.
    [Test]
    public void StandingInBothLineStacksKills()
        => BlueScreen(MeleeDpsA)
            .TeleportAt(13.2f, to: new(0, 3))
            .ShouldKill(P4ActionId.LineStack, MeleeDpsA);

    [Test]
    public void ALineStackOfThreeKillsAllThree()
        => BlueScreen(MeleeDpsA)
            .TeleportAt(13.2f, to: new(0, -8))
            .ShouldKill(P4ActionId.LineStack, MainTank, PhysRangedDps, RegenHealer);

    [Test]
    public void AMarkedPlayerAloneDies()
        => BlueScreen(PhysRangedDps)
            .TeleportAt(13.2f, to: new(-13.5f, 0))
            .ShouldKill(P4ActionId.LineStack, PhysRangedDps);

    [Test]
    public void ATankMayTakeALineStackAlone()
    {
        var run = Run(MainTank, [[MainTank, CasterDps], [RegenHealer, ShieldHealer], [MeleeDpsA, MeleeDpsB]], stopAt: 14.5f,
                      new Takeover(13.2f, OutOfEveryLineNorth));

        Assert.That(run.Passed, run.ToString);
    }

    // OT joins MT's line, which leaves the east line one short.
    [Test]
    public void BothTanksMayShareALineStack()
        => BlueScreen(MainTank, [[MainTank, CasterDps], [RegenHealer, ShieldHealer], [MeleeDpsA, MeleeDpsB]])
            .TeleportAt(13.2f, to: OutOfEveryLineNorth)
            .MoveBotAt(13.2f, OffTank, to: OutOfEveryLineNorth + new Vector2(0.6f, 0))
            .ShouldKill(P4ActionId.LineStack, CasterDps, ShieldHealer, MeleeDpsB);

    // H2 joins the west line, which then holds five; R2 and M2 step out of the east one, leaving OT alone in it.
    [Test]
    public void FiveInALineStackAndATankAloneSurvive()
    {
        var run = Run(ShieldHealer, [[PhysRangedDps, OffTank], [RegenHealer, ShieldHealer], [MeleeDpsA, MeleeDpsB]], stopAt: 14.5f,
                      new Takeover(13.2f, OuterStackWest),
                      new Takeover(13.2f, OutOfEveryLineNorth, Bot: CasterDps),
                      new Takeover(13.2f, OutOfEveryLineNorth + new Vector2(1f, 0), Bot: MeleeDpsB));

        Assert.That(run.Passed, run.ToString);
    }

    // Ring 1's spot is north of Omega, clear of every line. The third line stack lands before ring 5's death shows,
    // so MT holds a set 3 marker there and takes its stack alone, as a tank may.
    [TestCase(15.0f, -5.5f, P3ActionId.WaveRepeater)]
    [TestCase(17.5f, 9f, P3ActionId.WaveRepeaterRing2)]
    [TestCase(19.5f, 15f, P3ActionId.WaveRepeaterRing3)]
    [TestCase(21.5f, 19f, P3ActionId.WaveRepeaterRing4)]
    [TestCase(32.0f, -5.5f, P3ActionId.WaveRepeater)]
    [TestCase(34.5f, 9f, P3ActionId.WaveRepeaterRing2)]
    [TestCase(36.5f, 15f, P3ActionId.WaveRepeaterRing3)]
    [TestCase(38.5f, 19f, P3ActionId.WaveRepeaterRing4)]
    public void StandingInARingsBandAsItLandsKills(float at, float z, uint ring)
        => BlueScreen(MainTank, [[PhysRangedDps, CasterDps], [RegenHealer, ShieldHealer], [MainTank, CasterDps]])
            .TeleportAt(at, to: new(0, z))
            .ShouldKill(ring, MainTank);

    [Test]
    public void LeavingTheArenaKills()
        => BlueScreen(MainTank)
            .TeleportAt(2f, to: new(0, 21))
            .ShouldKill(TheEnvironment, MainTank);

    [TestCase(7.5f, -13.5f, -1f, MainTank, "two Wave Cannon beams")]
    [TestCase(12.5f, -3.89f, 5.82f, MainTank, "repeated Wave Cannon")]
    [TestCase(13.2f, 0f, 3f, MainTank, "both line stacks")]
    [TestCase(13.2f, 0f, -8f, MeleeDpsA, "only 3 players")]
    [TestCase(13.2f, -13.5f, 0f, PhysRangedDps, "only 1 player")]
    [TestCase(17.5f, 0f, 9f, MainTank, "stay outside 12 y")]
    public void EveryDeathSaysWhy(float at, float x, float z, PartyRole player, string because)
    {
        var run = Run(player, SplitMarkers, stopAt: null, new Takeover(at, new Vector2(x, z)));

        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(because), run.ToString);
    }

    [Test]
    public void TheMarkersGoOnEachSetsTargets()
    {
        PartyRole[][] sets = [[MainTank, PhysRangedDps], [CasterDps, MeleeDpsB], [RegenHealer, OffTank]];
        var markers = Markers(MainTank, o => Pin(o, sets));

        Assert.That(markers.Keys, Is.EqualTo(new[] { 5.6f, 15.7f, 25.6f }).Within(0.05f));
        Assert.Multiple(() =>
        {
            for (var set = 0; set < 3; set++)
                Assert.That(markers.Values.ElementAt(set), Is.EquivalentTo(sets[set]), $"set {set + 1}");
        });
    }

    // Set 2's six No rows leave only H1 and H2; set 3 forces M1 and leaves the other target to the roll.
    [Test]
    public void TheMarkerOverridesPickEachSetsTargets()
    {
        var markers = Markers(OffTank, o =>
        {
            o.MarkerInSet1[MainTank] = true;
            o.MarkerInSet1[OffTank] = true;
            foreach (var role in PerRole.All.Except([RegenHealer, ShieldHealer]))
                o.MarkerInSet2[role] = false;
            o.MarkerInSet3[MeleeDpsA] = true;
        });

        Assert.That(markers.Keys, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(markers.Values.ElementAt(0), Is.EquivalentTo(new[] { MainTank, OffTank }), "set 1");
            Assert.That(markers.Values.ElementAt(1), Is.EquivalentTo(new[] { RegenHealer, ShieldHealer }), "set 2");
            Assert.That(markers.Values.ElementAt(2), Has.Member(MeleeDpsA).And.Length.EqualTo(2).And.Unique, "set 3");
        });
    }

    [Test]
    public void ThreeYesesInOneSetConflict()
    {
        var overrides = new TopP4BlueScreenStateOverrides();
        overrides.MarkerInSet2[MainTank] = true;
        overrides.MarkerInSet2[OffTank] = true;
        overrides.MarkerInSet2[RegenHealer] = true;
        overrides.MarkerInSet3[MainTank] = true;
        overrides.MarkerInSet1[MainTank] = true;

        PerRole.ForceSeats = true;
        try
        {
            Assert.That(overrides.Validate().Problems, Has.Count.EqualTo(1).And.Some.Contains("a marker in set 2"));
        }
        finally
        {
            PerRole.ForceSeats = false;
        }
    }

    [Test]
    public void FollowsTheFightsTimeline()
    {
        var rows = new List<Dictionary<string, string>>();
        string? omega = null;
        var run = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => Pin((TopP4BlueScreenStateOverrides)o, SplitMarkers),
            Probe = p =>
            {
                omega ??= p.World.Children.OfType<SimEnemy>().SingleOrDefault(e => e.BNpcBaseId == OmegaP3)?.EntityId.ToString("X8");
                if (p.Crossed(39.25f)) rows.AddRange(p.Fake.Timeline.Rows);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.GreaterThan(39.2f), "the run lasts until the last ring");
        Assert.Multiple(() =>
        {
            Assert.That(Times(rows, "cast", P4ActionId.WaveCannonCast, actor: omega), Is.EqualTo(new[] { 3.0f }).Within(0.05f), "Omega's cast");
            Assert.That(Times(rows, "cast", P4ActionId.WaveCannonRepeat), Is.EqualTo(Eight(8.0f, 17.9f, 28.0f)).Within(0.05f), "aims");
            Assert.That(Times(rows, "ability", P4ActionId.WaveCannonBeam), Is.EqualTo(Eight(8.6f, 18.5f, 28.6f)).Within(0.05f), "beams");
            Assert.That(Times(rows, "ability", P4ActionId.WaveCannonRepeat), Is.EqualTo(Eight(13.3f, 23.2f, 33.3f)).Within(0.05f), "repeats");
            Assert.That(Times(rows, "ability", P4ActionId.LineStack), Is.EqualTo(new[] { 13.6f, 13.6f, 23.4f, 23.4f, 33.5f, 33.5f }).Within(0.05f), "line stacks");
            Assert.That(Times(rows, "cast", P3ActionId.WaveRepeater), Is.EqualTo(new[] { 11.0f, 28.2f }).Within(0.05f), "Wave Repeater casts");
            Assert.That(Times(rows, "ability", P3ActionId.WaveRepeater), Is.EqualTo(new[] { 16.0f, 33.2f }).Within(0.05f), "ring 1");
            Assert.That(Times(rows, "ability", P3ActionId.WaveRepeaterRing2), Is.EqualTo(new[] { 18.1f, 35.2f }).Within(0.05f), "ring 2");
            Assert.That(Times(rows, "ability", P3ActionId.WaveRepeaterRing3), Is.EqualTo(new[] { 20.1f, 37.2f }).Within(0.05f), "ring 3");
            Assert.That(Times(rows, "ability", P3ActionId.WaveRepeaterRing4), Is.EqualTo(new[] { 22.1f, 39.2f }).Within(0.05f), "ring 4");
            Assert.That(rows.Where(r => r["kind"] is "ability" or "cast").Select(Time).Max(), Is.EqualTo(39.2f).Within(0.05f),
                        "nothing is cast after the last ring");
        });
    }

    // The repeat lines start down each player's line, so they point where the players stood at the aim.
    [Test]
    public void EachRepeatLinePointsAtItsPlayer()
    {
        var bearings = new List<float>();
        var players = new List<float>();
        var run = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 8.2f,
            Overrides = o => Pin((TopP4BlueScreenStateOverrides)o, SplitMarkers),
            Probe = p =>
            {
                if (!p.Crossed(8.05f)) return;
                players.AddRange(PerRole.All.Select(r => Bearing(Flat(p.Member(r)!.Position))).Order());
                bearings.AddRange(p.Fake.Timeline.Rows
                                   .Where(r => r["kind"] == "cast" && r["id"] == $"{P4ActionId.WaveCannonRepeat}")
                                   .Select(r => Bearing(new Vector2(MathF.Sin(Float(r["rot"])), MathF.Cos(Float(r["rot"])))))
                                   .Order());
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(bearings, Is.EqualTo(players).Within(0.5f), $"lines {string.Join(", ", bearings)}; players {string.Join(", ", players)}");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void EveryoneOnTheStratsSpotsSurvives(int pins)
    {
        PartyRole[][][] markers =
        [
            SplitMarkers,
            [[MainTank, RegenHealer], [OffTank, MeleeDpsB], [PhysRangedDps, MeleeDpsA]],
            [[MeleeDpsA, MeleeDpsB], [CasterDps, ShieldHealer], [MainTank, OffTank]],
        ];
        var run = Run(MainTank, markers[pins], stopAt: null);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.GreaterThan(39.2f), "the run lasts until the last ring");
    }

    private static ScenarioRun Run(PartyRole player, PartyRole[][] markers, float? stopAt, params Takeover[] takeovers)
        => ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TopP4BlueScreenStateOverrides)o, markers),
            Takeovers = takeovers,
        });

    // Marker rows by when they went out, each with the roles they marked.
    private static SortedDictionary<float, PartyRole[]> Markers(PartyRole player, Action<TopP4BlueScreenStateOverrides> overrides)
    {
        var rows = new List<Dictionary<string, string>>();
        var roleById = new Dictionary<string, PartyRole>();
        var run = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = 26f,
            Overrides = o => overrides((TopP4BlueScreenStateOverrides)o),
            Probe = p =>
            {
                if (!p.Crossed(25.9f)) return;
                foreach (var role in PerRole.All)
                    roleById[p.Member(role)!.GameObjectId.ObjectId.ToString("X8")] = role;
                rows.AddRange(p.Fake.Timeline.Rows.Where(r => r["kind"] == "ability" && r["id"] == $"{P4ActionId.StackMarker}"));
            },
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        return new SortedDictionary<float, PartyRole[]>(
            rows.GroupBy(r => MathF.Round(Time(r), 1))
                .ToDictionary(g => g.Key, g => g.Select(r => roleById[r["target"]]).ToArray()));
    }

    private static float[] Times(IEnumerable<Dictionary<string, string>> rows, string kind, uint actionId, string? actor = null)
        => rows.Where(r => r["kind"] == kind && r["id"] == $"{actionId}" && (actor is null || r["actor"] == actor))
               .Select(Time)
               .Order()
               .ToArray();

    private static float[] Eight(params float[] times) => times.SelectMany(t => Enumerable.Repeat(t, 8)).ToArray();

    private static float Time(Dictionary<string, string> row) => Float(row["t"]);

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    private static float Bearing(Vector2 position)
    {
        var degrees = MathF.Atan2(position.X, -position.Y) * 180f / MathF.PI;
        return (degrees + 360f) % 360f;
    }
}
