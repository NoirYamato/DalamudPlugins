using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Intermission;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.NegativeRun;

namespace AnoMech.Tests;

// The roll is pinned (TopP3IntermissionLpduAiTests.Pin): R1 MT OT R2 spread, M1 M2 stack, H1 H2
// nothing, so the conga (R1 M1 MT H1 H2 OT M2 R2) sends R1 W, MT SW, OT SE, R2 E, M1+H1 NW and M2+H2 NE.
// With the north triangle first, the arms at N, SE and SW go off first and the groups finish at
// R1 (-16,4.5), MT (-11.89,11.61), OT (11.89,11.61), R2 (16,4.5), M1+H1 (-4.11,-16.11),
// M2+H2 (4.11,-16.11) (x east, z south).
//
//   9.2   fodder. 9.3: Omega starts to show; the first Wave Repeater (lands 14.3, hits at 14.0, r < 6).
//   12.3  the first arms warp in; 15.4 the second. 15.3-30.7: the void zone, r 6.
//   16.4 / 18.4 / 20.4  rings 6-12, 12-18, 18-24. 17.4: the second Wave Repeater (hits 22.1).
//   24.5 / 26.6 / 28.7  its rings. 26.1 / 28.6: each set's Colossal Blows hit, r 11.
//   28.3  the Sniper Cannons. 32.3: Omega turns targetable and the run ends.
public class TopP3IntermissionScenarioTests
{
    private static NegativeRun<TopP3IntermissionScenario> Intermission(PartyRole player, FirstArms arms = FirstArms.NorthTriangle)
        => Negative<TopP3IntermissionScenario>(player)
            .Overrides<TopP3IntermissionStateOverrides>(o => TopP3IntermissionLpduAiTests.Pin(o, arms));

    [Test]
    public void StandingInTheMiddleAsTheFirstWaveLandsKills()
        => Intermission(MainTank)
            .TeleportAt(13.5f, to: new(0, 3))
            .ShouldKill(P3ActionId.WaveRepeater, MainTank);

    [TestCase(15.6f, 9f, P3ActionId.WaveRepeaterRing2)]
    [TestCase(17f, 15f, P3ActionId.WaveRepeaterRing3)]
    [TestCase(19f, 19f, P3ActionId.WaveRepeaterRing4)]
    public void StandingInTheFirstWavesBandAsItsRingLandsKills(float at, float southOfCentre, uint ring)
        => Intermission(MainTank)
            .TeleportAt(at, to: new(0, southOfCentre))
            .ShouldKill(ring, MainTank);

    // The south arm belongs to the second set, so the ring lands first.
    [TestCase(23f, 9f, P3ActionId.WaveRepeaterRing2)]
    [TestCase(26.2f, 15f, P3ActionId.WaveRepeaterRing3)]
    public void StandingInTheSecondWavesBandAsItsRingLandsKills(float at, float southOfCentre, uint ring)
        => Intermission(MainTank)
            .TeleportAt(at, to: new(0, southOfCentre))
            .ShouldKill(ring, MainTank);

    // R1 waits on the wall at bearing 254, its own spread circle clear of everyone and both sets of
    // arms out of reach, until the last ring.
    [Test]
    public void StandingOnTheWallAsTheLastRingLandsKills()
        => Intermission(PhysRangedDps)
            .TeleportAt(27f, to: new(-18.26f, 5.24f))
            .ShouldKill(P3ActionId.WaveRepeaterRing4, PhysRangedDps);

    [TestCase(FirstArms.NorthTriangle, -16f)]
    [TestCase(FirstArms.SouthTriangle, 16f)]
    public void StandingNextToAFirstArmKills(FirstArms arms, float z)
        => Intermission(MainTank, arms)
            .TeleportAt(25f, to: new(0, z))
            .ShouldKill(P3ActionId.ColossalBlow, MainTank);

    // MT, a spread, dies to the north arm 4.1 y from both stack pairs: had its spread still fired, it
    // would have caught all four.
    [Test]
    public void AHolderWhoDiedFiresNothing()
    {
        var run = Run(MainTank, FirstArms.NorthTriangle, new Takeover(25f, new Vector2(0, -16)));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Elapsed, Is.GreaterThan(28.3f), "the run lasts until the cannons");
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)),
                    Is.EqualTo(new[] { (MainTank, (uint?)P3ActionId.ColossalBlow) }), run.ToString);
    }

    // OT spreads alone by the second set's south arm, after the first set and its ring.
    [Test]
    public void StandingNextToASecondArmKills()
        => Intermission(OffTank)
            .TeleportAt(27f, to: new(0, 17))
            .ShouldKill(P3ActionId.ColossalBlow, OffTank);

    [Test]
    public void StandingInTheVoidZoneKills()
    {
        var run = Run(MainTank, FirstArms.NorthTriangle, new Takeover(14.5f, new Vector2(2, 0)));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths, Has.Count.EqualTo(1), run.ToString);
        var death = run.Deaths[0];
        Assert.Multiple(() =>
        {
            Assert.That(death.Role, Is.EqualTo(MainTank));
            Assert.That(death.ActionId, Is.EqualTo(TheEnvironment));
            Assert.That(death.Cause, Does.Contain("void zone"));
            Assert.That(death.Time, Is.EqualTo(15.3f).Within(0.05f), "the zone kills from the frame it appears");
        });
    }

    [Test]
    public void VoidZoneShowsOnlyWhileItKills()
    {
        var shown = new Dictionary<float, bool>();
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => TopP3IntermissionLpduAiTests.Pin((TopP3IntermissionStateOverrides)o, FirstArms.NorthTriangle),
            Probe = p =>
            {
                foreach (var at in new[] { 15.2f, 15.4f, 30.6f, 30.8f })
                    if (p.Crossed(at))
                        shown[at] = p.World.Children.OfType<SimEventObject>().Any(o => o.EObjRowId == P3EObjId.VoidZone && o.IsAlive);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(shown, Is.EqualTo(new Dictionary<float, bool> { [15.2f] = false, [15.4f] = true, [30.6f] = true, [30.8f] = false }));
    }

    // MT steps 2.9 y from R1: each stands in the other's spread as well as their own.
    [Test]
    public void TwoSpreadsTooCloseKillBoth()
        => Intermission(MainTank)
            .TeleportAt(27.9f, to: new(-13.5f, 6f))
            .ShouldKill(ActionId.SniperCannon, MainTank, PhysRangedDps);

    // MT's spread catches the NW stack pair, and MT stands in their stack.
    [Test]
    public void SpreadInAStackKillsTheSpreadAndThePair()
        => Intermission(MainTank)
            .TeleportAt(27.9f, to: new(-3f, -15f))
            .ShouldKill(ActionId.SniperCannon, MainTank, MeleeDpsA, RegenHealer);

    // M1 brings its stack onto M2's: H1 is left out of both.
    [Test]
    public void BothStacksTogetherKillEveryoneInBoth()
        => Intermission(MeleeDpsA)
            .TeleportAt(27.9f, to: new(4.11f, -16.11f))
            .ShouldKill(ActionId.HighPoweredSniperCannon, MeleeDpsA, MeleeDpsB, ShieldHealer);

    // H1 waits somewhere no circle, ring or arm reaches.
    [Test]
    public void StackTakenAloneKillsTheHolder()
        => Intermission(RegenHealer)
            .TeleportAt(27.9f, to: new(-6.93f, 4f))
            .ShouldKill(ActionId.HighPoweredSniperCannon, MeleeDpsA);

    // H2 joins the NW stack: M1, H1 and H2 share it and live; only M2 is left alone.
    [Test]
    public void ThreePlayersInOneStackSurvive()
    {
        var run = Run(ShieldHealer, FirstArms.NorthTriangle, new Takeover(27.9f, new Vector2(-4.11f, -16.11f)));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)),
                    Is.EqualTo(new[] { (MeleeDpsB, (uint?)ActionId.HighPoweredSniperCannon) }), run.ToString);
    }

    [Test]
    public void LeavingTheArenaKills()
        => Intermission(MainTank)
            .TeleportAt(5f, to: new(0, 21))
            .ShouldKill(TheEnvironment, MainTank);

    [TestCase(FirstArms.NorthTriangle, new[] { 0f, 120f, 240f }, new[] { 60f, 180f, 300f })]
    [TestCase(FirstArms.SouthTriangle, new[] { 60f, 180f, 300f }, new[] { 0f, 120f, 240f })]
    public void TheFirstArmsWarpInAndGoOffFirst(FirstArms arms, float[] first, float[] second)
    {
        var bearings = new Dictionary<string, float>();
        var rows = new List<Dictionary<string, string>>();
        var shown = new Dictionary<float, float[]>();
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => TopP3IntermissionLpduAiTests.Pin((TopP3IntermissionStateOverrides)o, arms),
            Probe = p =>
            {
                foreach (var at in new[] { 12.2f, 12.4f, 15.3f, 15.5f })
                    if (p.Crossed(at))
                        shown[at] = Arms(p).Where(a => a.Proxy?.IsDrawObjectVisible == true).Select(a => Bearing(a.Position)).Order().ToArray();
                if (!p.Crossed(30f)) return;
                foreach (var arm in Arms(p))
                    bearings[arm.EntityId.ToString("X8")] = Bearing(arm.Position);
                rows.AddRange(p.Fake.Timeline.Rows);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(bearings.Values.Order(), Is.EqualTo(new[] { 0f, 60f, 120f, 180f, 240f, 300f }).Within(0.01f), "the hexagon");
        var warps = rows.Where(r => r["kind"] == "ac" && r["id"] == "0x197" && bearings.ContainsKey(r["actor"])).ToList();
        var blows = rows.Where(r => r["kind"] == "cast" && r["id"] == $"{P3ActionId.ColossalBlow}" && bearings.ContainsKey(r["actor"])).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(shown[12.2f], Is.Empty, "arms shown before 12.3");
            Assert.That(shown[12.4f], Is.EqualTo(first).Within(0.01f), "arms shown after 12.3");
            Assert.That(shown[15.3f], Is.EqualTo(first).Within(0.01f), "arms shown before 15.4");
            Assert.That(shown[15.5f], Is.EqualTo(new[] { 0f, 60f, 120f, 180f, 240f, 300f }).Within(0.01f), "arms shown after 15.4");
            Assert.That(BearingsAt(warps, bearings, 12.3f), Is.EqualTo(first).Within(0.01f), "warp-ins at 12.3");
            Assert.That(BearingsAt(warps, bearings, 15.4f), Is.EqualTo(second).Within(0.01f), "warp-ins at 15.4");
            Assert.That(warps, Has.Count.EqualTo(6), "one warp-in per arm");
            Assert.That(BearingsAt(blows, bearings, 24.4f), Is.EqualTo(first).Within(0.01f), "blows at 24.4");
            Assert.That(BearingsAt(blows, bearings, 26.9f), Is.EqualTo(second).Within(0.01f), "blows at 26.9");
        });
    }

    [Test]
    public void OmegaIsHiddenUntilHeStartsToShow()
    {
        var hidden = new Dictionary<float, bool>();
        var rows = new List<Dictionary<string, string>>();
        var omega = "";
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => TopP3IntermissionLpduAiTests.Pin((TopP3IntermissionStateOverrides)o, FirstArms.NorthTriangle),
            Probe = p =>
            {
                var boss = p.World.Children.OfType<SimEnemy>().SingleOrDefault(e => e.BNpcBaseId == OmegaP3);
                foreach (var at in new[] { 0.5f, 9.2f })
                    if (p.Crossed(at))
                        hidden[at] = boss?.Proxy is { IsDrawObjectVisible: false };
                if (!p.Crossed(30f) || boss is null) return;
                omega = boss.EntityId.ToString("X8");
                rows.AddRange(p.Fake.Timeline.Rows);
            },
        });

        Assert.That(run.Passed, run.ToString);
        var timelines = rows.Where(r => r["kind"] == "ac" && r["id"] == "0x197" && r["actor"] == omega).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.EqualTo(new Dictionary<float, bool> { [0.5f] = true, [9.2f] = true }));
            Assert.That(timelines.Select(r => r["p"]), Is.EqualTo(new[] { $"0x{P3TimelineId.OmegaShow:X}" }), "one show timeline");
            Assert.That(timelines.Select(r => float.Parse(r["t"], CultureInfo.InvariantCulture)), Has.All.EqualTo(9.3f).Within(0.05f));
        });
    }

    [TestCase(FirstArms.NorthTriangle)]
    [TestCase(FirstArms.SouthTriangle)]
    public void EveryoneOnTheStratsSpotsSurvives(FirstArms arms)
    {
        var targetable = new Dictionary<float, bool>();
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => TopP3IntermissionLpduAiTests.Pin((TopP3IntermissionStateOverrides)o, arms),
            Probe = p =>
            {
                foreach (var at in new[] { 32.2f, 32.4f })
                    if (p.Crossed(at))
                        targetable[at] = p.World.Children.OfType<SimEnemy>().Single(e => e.BNpcBaseId == OmegaP3).Targetable;
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.GreaterThan(32.3f), "the run ends once Omega turns targetable");
        Assert.That(targetable, Is.EqualTo(new Dictionary<float, bool> { [32.2f] = false, [32.4f] = true }));
    }

    private static ScenarioRun Run(PartyRole player, FirstArms arms, params Takeover[] takeovers)
        => ScenarioRun.Execute(typeof(TopP3IntermissionScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            WriteArtifactsOnFailure = false,
            Overrides = o => TopP3IntermissionLpduAiTests.Pin((TopP3IntermissionStateOverrides)o, arms),
            Takeovers = takeovers,
        });

    private static IEnumerable<SimEnemy> Arms(ScenarioProbe p)
        => p.World.Children.OfType<SimEnemy>().Where(e => e.BNpcBaseId is BNpcBaseId.LeftArmUnit or BNpcBaseId.RightArmUnit);

    private static float[] BearingsAt(IEnumerable<Dictionary<string, string>> rows, Dictionary<string, float> bearings, float at)
        => rows.Where(r => MathF.Abs(float.Parse(r["t"], CultureInfo.InvariantCulture) - at) < 0.05f)
               .Select(r => bearings[r["actor"]])
               .Order()
               .ToArray();

    private static float Bearing(Vector3 position)
    {
        var degrees = MathF.Atan2(position.X, -position.Z) * 180f / MathF.PI;
        return MathF.Round((degrees + 360f) % 360f, 2);
    }
}
