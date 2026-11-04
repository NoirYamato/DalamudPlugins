using System.Globalization;
using System.Numerics;
using System.Text.Json;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Delta;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Tests.TopP5DeltaLpduAiTests;

namespace AnoMech.Tests;

// The eye spawns on any cardinal. From the eye, the rule places the rest: the beetle on the rim 90 degrees
// counter-clockwise of it, the Final opposite the beetle, the eye unit 45 y out beyond the eye's cardinal and the six
// arm units on the rim at the eye's bearing and every 60 degrees after it, all facing the centre. The hazards follow
// their casters round, so which spots are safe is read from the eye.
public class TopP5DeltaEyeTests
{
    private const float Tolerance = 0.01f;
    private const float EyeUnitDistance = 45f;
    private const int SeedsPerEye = 30;

    private static readonly uint[] EyeEffects = [0x00020001, 0x00800040, 0x10000001, 0x00080004];

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

    private static IEnumerable<TestCaseData> EachEye() => Eyes.Select(e => new TestCaseData(e.Name).SetName($"{{m}}(eye {e.Name})"));

    private static IEnumerable<TestCaseData> EachEyeAndSwivel()
        => from eye in Eyes
           from swivelLeft in new[] { true, false }
           select new TestCaseData(eye.Name, swivelLeft).SetName($"{{m}}(eye {eye.Name}, Swivel Cannon {(swivelLeft ? "Left" : "Right")})");

    // ---- The roll ---------------------------------------------------------------------------------------

    [Test]
    public void TheRollGivesEveryCardinalAndAPinnedEyeIsTheOnlyOne()
    {
        var rolled = Enumerable.Range(1, 400)
                               .Select(seed => new TopP5DeltaState(new Rng(seed), new TopP5DeltaStateOverrides(), MainTank).EyeSpawn)
                               .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rolled.Distinct(), Is.SubsetOf(Eyes.Select(e => e.Eye)));
            foreach (var (name, eye, _) in Eyes)
            {
                Assert.That(rolled.Count(e => e == eye), Is.InRange(60, 140), $"eye {name} in 400 rolls");
                var pinned = new TopP5DeltaStateOverrides { EyeSpawn = eye };
                Assert.That(Enumerable.Range(1, 50).Select(seed => new TopP5DeltaState(new Rng(seed), pinned, MainTank).EyeSpawn),
                            Has.All.EqualTo(eye), $"pinned {name}");
            }
        });
    }

    // The debug row's pick reaches a peer and the lobby like any fight-wide roll.
    [TestCaseSource(nameof(EachEye))]
    public void APinnedEyeTravelsToAPeerAndShowsInTheLobby(string eyeName)
    {
        var eye = EyeNamed(eyeName);
        var host = new TopP5DeltaStateOverrides { EyeSpawn = eye };
        var peer = new TopP5DeltaStateOverrides();

        Assert.That(ScenarioSettingsSync.Apply(peer, ScenarioSettingsSync.Serialize(host)!), Is.True);
        Assert.That(peer.EyeSpawn, Is.EqualTo(eye));
        Assert.That(ScenarioSettingsSummary.Describe(host), Has.Member($"Eye spawn: {FieldName(eye)}"));
    }

    [TestCaseSource(nameof(EachEye))]
    public void TheReplayCarriesTheEye(string eyeName)
    {
        var eye = EyeNamed(eyeName);
        TopP5DeltaAiReplayStateMessage? sent = null;
        TopP5DeltaState? replayed = null;
        var run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 1f,
            Overrides = o => ((TopP5DeltaStateOverrides)o).EyeSpawn = eye,
            Probe = p =>
            {
                if (!p.Crossed(0.5f)) return;
                var scenario = Scenario(p);
                sent = (TopP5DeltaAiReplayStateMessage)JsonSerializer.Deserialize<MpMessage>(
                    JsonSerializer.Serialize<MpMessage>(scenario.BuildReplayStateMessage()!))!;
                replayed = (TopP5DeltaState?)scenario.StartReplay(sent, LpduStrat, MainTank, p.World);
            },
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(sent!.EyeSpawnEffectIndex, Is.EqualTo(eye.EffectIndex));
        Assert.That(replayed!.EyeSpawn, Is.EqualTo(eye));
    }

    // ---- Where everything spawns -------------------------------------------------------------------------

    [TestCaseSource(nameof(EachEye))]
    public void TheBeetleTheFinalAndTheEyeUnitStandRoundTheEyeFacingTheCentre(string eyeName)
    {
        var eye = EyeNamed(eyeName);
        var seen = Spawns(eye);
        Assert.Multiple(() =>
        {
            AssertOnRingFacingTheCentre(seen.Single(BNpcBaseId.BeetleHelper), BearingOf(eye) - 90f, Geometry.ArenaRadius, "the beetle");
            AssertOnRingFacingTheCentre(seen.Single(BNpcBaseId.FinalHelper), BearingOf(eye) + 90f, Geometry.ArenaRadius, "the Final");
            AssertOnRingFacingTheCentre(seen.Single(BNpcBaseId.OpticalUnit), BearingOf(eye), EyeUnitDistance, "the eye unit");
        });
    }

    // In spawn order, which is the State's: the bearing each index gets is the one the LPDU tests bait it by.
    [TestCaseSource(nameof(EachEye))]
    public void TheArmsStandAtTheEyesBearingAndEverySixtyDegreesFacingTheCentre(string eyeName)
    {
        var eye = EyeNamed(eyeName);
        var arms = Spawns(eye).Arms;
        Assert.That(arms, Has.Count.EqualTo(6));
        var eyeFrameBearings = arms.Select(arm => Wrapped(CompassBearing(Flat(arm.Position)) - BearingOf(eye))).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(eyeFrameBearings.Select(b => (int)MathF.Round(b) % 360).Order(), Is.EqualTo(new[] { 0, 60, 120, 180, 240, 300 }),
                        $"eye-frame bearings {string.Join(", ", eyeFrameBearings.Select(b => b.ToString("F1", CultureInfo.InvariantCulture)))}");
            for (var arm = 0; arm < arms.Count; arm++)
                AssertOnRingFacingTheCentre(arms[arm], BearingOf(eye) + ArmBearing(arm, eye), Geometry.ArenaRadius, $"arm {arm}");
        });
    }

    // The eye's MapEffect slot is its cardinal's octant + 1, as the P2 eye's is (UNVERIFIED for E and W).
    [TestCaseSource(nameof(EachEye))]
    public void TheEyeIsDrawnOnItsCardinal(string eyeName)
    {
        var eye = EyeNamed(eyeName);
        var effects = new List<(string Index, uint Flags)>();
        var run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 44.5f,
            Overrides = o => ((TopP5DeltaStateOverrides)o).EyeSpawn = eye,
            Probe = p =>
            {
                if (!p.Crossed(44f)) return;
                effects.AddRange(p.Fake.Timeline.Rows.Where(r => r["kind"] == "map_effect")
                                  .Select(r => (r["id"], Convert.ToUInt32(r["detail"].Split("flags=0x")[1], 16))));
            },
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        var slot = ((int)(BearingOf(eye) / 45f) + 1).ToString(CultureInfo.InvariantCulture);
        Assert.That(effects, Is.EqualTo(EyeEffects.Select(flags => (slot, flags))));
    }

    // ---- The hazards turn with it ------------------------------------------------------------------------

    // Read off the hit areas as they resolve, deaths only reported so the strat can't stop the run. The Optical Laser
    // covers the eye line and not the beetle-Final line it would cover with the eye a quarter turn round. The Swivel
    // Cannon's Left cast covers the eye's half and its Right cast the other, each with the 15-degree reach past the
    // beetle-Final line, and spares the mirror image.
    [TestCaseSource(nameof(EachEyeAndSwivel))]
    public void TheLaserAndTheSwivelCannonCoverTheirSidesOfTheEye(string eyeName, bool swivelLeft)
    {
        var eye = EyeNamed(eyeName);
        var side = swivelLeft ? Side.Left : Side.Right;
        var hits = new Dictionary<uint, AoeQuery>();
        void Note(AoeQuery hit)
        {
            if (hit.ActionId is ActionId.OpticalLaser or ActionId.SwivelCannonL or ActionId.SwivelCannonR) hits.TryAdd(hit.ActionId, hit);
        }

        AoeQuery.Evaluated += Note;
        ScenarioRun run;
        try
        {
            run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
            {
                StopAt = 53.7f,
                GodMode = true,
                Overrides = o =>
                {
                    var overrides = (TopP5DeltaStateOverrides)o;
                    overrides.EyeSpawn = eye;
                    overrides.SwivelCannonSide = side;
                },
            });
        }
        finally
        {
            AoeQuery.Evaluated -= Note;
        }

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(hits.Keys, Is.EquivalentTo(new[] { ActionId.OpticalLaser, side.SwivelCannonActionId }));
        var laser = hits[ActionId.OpticalLaser];
        var swivel = hits[side.SwivelCannonActionId];
        var eyeHalf = side == Side.Left ? -1f : 1f;
        Assert.Multiple(() =>
        {
            Assert.That(Clearance(laser, eye, new(0f, -15f)), Is.LessThan(0f), "the laser by the eye");
            Assert.That(Clearance(laser, eye, new(0f, 15f)), Is.LessThan(0f), "the laser across the arena from the eye");
            Assert.That(Clearance(laser, eye, new(-12f, 0f)), Is.GreaterThan(0f), "the laser 12 y toward the beetle");
            Assert.That(Clearance(laser, eye, new(12f, 0f)), Is.GreaterThan(0f), "the laser 12 y toward the Final");
            Assert.That(Clearance(swivel, eye, new(9.5f, 12f * eyeHalf)), Is.LessThan(0f), "the Swivel Cannon on its half");
            Assert.That(Clearance(swivel, eye, new(9.5f, 5f * eyeHalf)), Is.LessThan(0f), "the Swivel Cannon on its half, near the line");
            Assert.That(Clearance(swivel, eye, new(9.5f, -5f * eyeHalf)), Is.LessThan(0f), "the Swivel Cannon's reach past the line");
            Assert.That(Clearance(swivel, eye, new(9.5f, -12f * eyeHalf)), Is.GreaterThan(0f), "the Swivel Cannon's safe side");
        });
    }

    // Upstream's DiesToOpticalLaser and DiesToSwivelCannon, a quarter turn round; and a player on the mirror-image spot
    // lives through the same hit.
    [TestCase("E")]
    [TestCase("W")]
    public void AStepOntoTheEyeLineDiesToTheOpticalLaser(string eyeName)
    {
        var eye = EyeNamed(eyeName);
        Delta(RegenHealer, eye, Side.Left)
            .TeleportAt(29.2f, to: ToWorld(new(0f, -3f), eye))
            .ShouldKill(ActionId.OpticalLaser, RegenHealer);
    }

    [TestCase("E")]
    [TestCase("W")]
    public void AStepTowardTheBeetleLivesThroughTheOpticalLaser(string eyeName)
    {
        var run = Standing(EyeNamed(eyeName), Side.Left, 29.2f, new(-12f, 0f), until: 30.6f);
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Where(d => d.Role == RegenHealer), Is.Empty, run.ToString);
    }

    [TestCase("E", true)]
    [TestCase("E", false)]
    [TestCase("W", true)]
    [TestCase("W", false)]
    public void TheSwivelCannonsHalfDies(string eyeName, bool swivelLeft)
    {
        var eye = EyeNamed(eyeName);
        var side = swivelLeft ? Side.Left : Side.Right;
        Delta(RegenHealer, eye, side)
            .TeleportAt(52f, to: ToWorld(new(9.5f, -12f * side.Mul), eye))
            .ShouldKill(side.SwivelCannonActionId, RegenHealer);
    }

    [TestCase("E", true)]
    [TestCase("E", false)]
    [TestCase("W", true)]
    [TestCase("W", false)]
    public void TheSwivelCannonsOtherHalfLives(string eyeName, bool swivelLeft)
    {
        var side = swivelLeft ? Side.Left : Side.Right;
        var run = Standing(EyeNamed(eyeName), side, 52f, new(9.5f, 12f * side.Mul), until: 53.7f);
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Where(d => d.Role == RegenHealer), Is.Empty, run.ToString);
    }

    // ---- Both strats -------------------------------------------------------------------------------------

    private static IEnumerable<TestCaseData> EachEyeAndStrat()
        => from eye in Eyes
           from strat in new[] { LpduStrat, NaStrat }
           select new TestCaseData(eye.Name, strat).SetName($"{{m}}(eye {eye.Name}, {(strat == LpduStrat ? "LPDU" : "NA")} strat)");

    [TestCaseSource(nameof(EachEyeAndStrat))]
    public void BothStratsClearTheFight(string eyeName, int strat)
    {
        var eye = EyeNamed(eyeName);
        var failures = Enumerable.Range(0, SeedsPerEye)
                                 .Select(_ => ScenarioRun.Execute(typeof(TopP5DeltaScenario), strat, Random.Shared.Next(), new ScenarioRunOptions
                                 {
                                     Overrides = o => ((TopP5DeltaStateOverrides)o).EyeSpawn = eye,
                                 }))
                                 .Where(run => !run.Passed)
                                 .ToList();
        Assert.That(failures, Is.Empty, () => $"{failures.Count}/{SeedsPerEye} seeds failed:{Environment.NewLine}"
                                              + string.Join(Environment.NewLine, failures.Select(run => run.ToString())));
    }

    // ---- Helpers -----------------------------------------------------------------------------------------

    // Every enemy in spawn order, which for the arms is the State's.
    private sealed record Spawned(IReadOnlyList<(uint Id, Placement At)> All)
    {
        public Placement Single(uint bnpcBaseId) => All.Single(s => s.Id == bnpcBaseId).At;

        public List<Placement> Arms => All.Where(s => s.Id is BNpcBaseId.LeftArmUnit or BNpcBaseId.RightArmUnit).Select(s => s.At).ToList();
    }

    // As the scenario placed them, before Hyper Pulse turns the arms.
    private static Spawned Spawns(NorthSouth eye)
    {
        var seen = new List<(uint Id, Placement At)>();
        var run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 24.5f,
            Overrides = o => ((TopP5DeltaStateOverrides)o).EyeSpawn = eye,
            Probe = p =>
            {
                if (p.Crossed(24f))
                    seen.AddRange(p.World.Children.OfType<SimEnemy>().Select(e => (e.SpawnConfig.BNpcBaseId, e.Placement())));
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return new Spawned(seen);
    }

    private static void AssertOnRingFacingTheCentre(Placement placement, float bearing, float radius, string what)
    {
        var expected = OnRing(bearing, radius);
        var at = Flat(placement.Position);
        Assert.That(Vector2.Distance(at, expected), Is.LessThan(Tolerance), $"{what} at {at}, expected {expected}");
        var facing = new Vector2(MathF.Sin(placement.Rotation), MathF.Cos(placement.Rotation));
        Assert.That(Vector2.Dot(facing, Vector2.Normalize(-at)), Is.GreaterThan(1f - Tolerance), $"{what} at {at} facing {facing}, not the centre");
    }

    private static float CompassBearing(Vector2 at) => Wrapped(MathF.Atan2(at.X, -at.Y) * 180f / MathF.PI);

    private static float Wrapped(float degrees) => (degrees % 360f + 360f) % 360f;

    private static float Clearance(AoeQuery hit, NorthSouth eye, Vector2 eyeFrame)
    {
        var at = ToWorld(eyeFrame, eye);
        return hit.SignedDistance(new Vector3(at.X, 0f, at.Y)) ?? throw new InvalidOperationException($"no area for action {hit.ActionId}");
    }

    private static string FieldName(NorthSouth eye)
        => typeof(NorthSouth).GetFields().Single(f => f.FieldType == typeof(NorthSouth) && ReferenceEquals(f.GetValue(null), eye)).Name;

    private static readonly PartyRole[] Order =
        [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    // Upstream's TopP5DeltaScenarioTests roll on its strat, with the eye and the Swivel Cannon set.
    private static void PinUpstreamsRoll(TopP5DeltaStateOverrides o, NorthSouth eye, Side swivel)
    {
        o.EyeSpawn = eye;
        o.SwivelCannonSide = swivel;
        o.OmegaMonitorSide = Side.Left;
        o.PlayerMonitorSide = Side.Left;
        o.TetherOrder = Order;
        o.FistColors = [Yellow, Blue, Blue, Yellow, Yellow, Blue, Blue, Yellow];
        o.ArmRotations = [ArmRotation.CounterClockwise, ArmRotation.CounterClockwise, ArmRotation.CounterClockwise,
                          ArmRotation.Clockwise, ArmRotation.Clockwise, ArmRotation.Clockwise];
        o.HelloWorld[MainTank] = HelloWorldOption.Far;
        o.HelloWorld[OffTank] = HelloWorldOption.Near;
        o.Monitor[ShieldHealer] = true;
        o.BeyondDefence[MainTank] = true;
    }

    private static NegativeRun<TopP5DeltaScenario> Delta(PartyRole player, NorthSouth eye, Side swivel)
        => NegativeRun.Negative<TopP5DeltaScenario>(player, NaStrat)
                      .Overrides<TopP5DeltaStateOverrides>(o => PinUpstreamsRoll(o, eye, swivel));

    // The regen healer, on upstream's roll, steps onto `eyeFrame` at `at` and stays.
    private static ScenarioRun Standing(NorthSouth eye, Side swivel, float at, Vector2 eyeFrame, float until)
        => ScenarioRun.Execute(typeof(TopP5DeltaScenario), NaStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = RegenHealer,
            StopAt = until,
            WriteArtifactsOnFailure = false,
            Overrides = o => PinUpstreamsRoll((TopP5DeltaStateOverrides)o, eye, swivel),
            Takeovers = [new Takeover(at, ToWorld(eyeFrame, eye))],
        });
}
