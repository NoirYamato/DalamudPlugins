using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LimitCut;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Scenarios.Tea.P1LimitCut.TeaP1FirstBlasters;
using static AnoMech.Tests.TeaLane;
using Stage = AnoMech.Tests.TeaP1LimitCutStage;

namespace AnoMech.Tests;

// The fight's rules, played with no strat (TeaP1LimitCutStage): everyone is put where the fight asks, and a test
// moves the player's seat into one mistake. The numbers are the stage's (#1 M1, #2 R1, #3 MT, #4 M2, #5 R2, #6 H1,
// #7 H2, #8 OT) and the first blasters stand west and east unless a test says otherwise (x east, z south):
//
//   5.9-6.25  the numbers.
//   8.0 / 10.2 / 12.4 / 14.6   Hawk Blaster pairs W+E, NW+SE, N+S, NE+SW; again at 19.0 / 21.2 / 23.5 / 25.7.
//   16.8 / 27.9   the middle blaster. A blast kills at once; the death shows a second later.
//   15.4 #1 cleaved on the north spot, 16.9 #2 charged there; 20.1 #3 and 21.6 #4 south-west;
//   24.8 #5 and 26.3 #6 south-east; 29.4 #7 and 30.9 #8 north. Cruise Chaser lands 0.5 s before each cleave and
//   turns to its number again 0.1 s before it.
//   34.1  J Kick. The run ends a second after its hit shows.
public class TeaP1LimitCutScenarioTests
{
    private const string BlastRule = "Hawk Blasters go off unmarked";

    [Test]
    public void EveryoneWhereTheFightAsksSurvives([Values] TeaP1FirstBlasters axis, [Values(1.5f, 3f, 4.5f)] float landsBehind)
    {
        var run = new Stage(axis, landsBehind).Run(MainTank);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.InRange(35.5f, 37f), "the run ends by itself after J Kick");
    }

    [TestCase(WestEast, -14f, 0f)]
    [TestCase(NorthwestSoutheast, 10f, 10f)]
    [TestCase(NorthSouth, 0f, -14f)]
    [TestCase(NortheastSouthwest, -10f, 10f)]
    public void StandingOnAFirstBlasterKills(TeaP1FirstBlasters axis, float x, float z)
        => AssertFirstDeath(Mistake(MainTank, 7.5f, new(x, z), axis), MainTank, P1ActionId.HawkBlaster, 9.0f, BlastRule);

    // 9.5 y from the west blaster's centre, and 10.5 y from it.
    [Test]
    public void ABlastReachesTenYalms()
    {
        AssertFirstDeath(Mistake(MainTank, 7.5f, new(-4.5f, 0f)), MainTank, P1ActionId.HawkBlaster, 9.0f, BlastRule);

        var outside = new Stage().Run(MainTank, stopAt: 9.5f, takeovers: new Takeover(7.5f, new Vector2(-3.5f, 0f)));
        Assert.That(outside.Passed, outside.ToString);
    }

    [TestCase(16.5f, 17.8f)]
    [TestCase(27.5f, 28.9f)]
    public void StandingInTheMiddleAsItsBlasterGoesOffKills(float at, float diesAt)
        => AssertFirstDeath(Mistake(MainTank, at, Vector2.Zero), MainTank, P1ActionId.HawkBlaster, diesAt, BlastRule);

    // M1 goes back east, where the first pair went off, before the fifth.
    [Test]
    public void TheSecondRoundLandsOnTheFirstsSpots()
        => AssertFirstDeath(Mistake(MeleeDpsA, 18.5f, new(14f, 0f)), MeleeDpsA, P1ActionId.HawkBlaster, 20.0f, BlastRule);

    [Test]
    public void TheHawkBlastersGoOffWhereAndWhenTheFightPutsThem([Values] TeaP1FirstBlasters axis)
    {
        var first = (int)axis * 45f + 270f;
        (float At, float Bearing)[] pairs =
        [
            (8.0f, first), (10.2f, first + 45f), (12.4f, first + 90f), (14.6f, first + 135f),
            (19.0f, first), (21.2f, first + 45f), (23.5f, first + 90f), (25.7f, first + 135f),
        ];
        var expected = pairs.SelectMany(pair => new (float At, Vector2 Centre)[] { (pair.At, Centre(pair.Bearing)), (pair.At, Centre(pair.Bearing + 180f)) })
                            .Append((At: 16.8f, Centre: Vector2.Zero))
                            .Append((At: 27.9f, Centre: Vector2.Zero))
                            .OrderBy(blast => blast.At)
                            .ToList();

        var blasts = Sent(new Stage(axis), P1ActionId.HawkBlaster);

        Assert.That(blasts, Has.Count.EqualTo(18));
        Assert.That(blasts.Select(b => b.At), Is.EqualTo(expected.Select(e => e.At)).Within(0.05f));
        Assert.Multiple(() =>
        {
            foreach (var at in expected.Select(e => e.At).Distinct())
                Assert.That(blasts.Where(b => MathF.Abs(b.At - at) < 0.05f).Select(b => Rounded(b.Position)),
                            Is.EquivalentTo(expected.Where(e => e.At == at).Select(e => Rounded(e.Centre))), $"the blasts at {at}");
        });
    }

    // Cardinal centres are 14 y out, diagonal ones on (+-10, +-10).
    private static Vector2 Centre(float bearing)
    {
        var onRing = Stage.OnRing(bearing, 14f);
        return MathF.Abs(onRing.X) < 0.01f || MathF.Abs(onRing.Y) < 0.01f ? onRing : new Vector2(MathF.Sign(onRing.X) * 10f, MathF.Sign(onRing.Y) * 10f);
    }

    [Test]
    public void TheNumbersAppearOnTheirPlayers()
    {
        var markers = new List<(float At, uint Lockon, PartyRole On)>();
        var run = new Stage().Run(MainTank, stopAt: 7f, probe: p =>
        {
            if (!p.Crossed(6.9f)) return;
            var seats = PerRole.All.ToDictionary(role => p.Member(role)!.EntityId.ToString("X8"), role => role);
            markers.AddRange(p.Fake.Timeline.Rows
                .Where(row => row["kind"] is "ac" or "ac_self" && row["id"] == "0x22")
                .Select(row => (Float(row["t"]), Convert.ToUInt32(row["p"].Split(',')[0], 16), seats[row["actor"]])));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(markers.Select(m => m.Lockon), Is.EqualTo(new uint[] { 79, 80, 81, 82, 83, 84, 85, 86 }));
        Assert.That(markers.Select(m => m.On), Is.EqualTo(Stage.Numbers));
        Assert.That(markers.Select(m => m.At), Is.EqualTo(new[] { 5.9f, 5.95f, 6.0f, 6.05f, 6.1f, 6.15f, 6.2f, 6.25f }).Within(0.04f));
    }

    // Each cleave catches its odd number alone and each charge its even number alone, so nobody is hit twice.
    [Test]
    public void EachHitGoesToItsOwnNumber([Values] TeaP1FirstBlasters axis)
    {
        var stage = new Stage(axis);
        Dictionary<string, PartyRole> seats = [];
        var hits = new List<(float At, uint Action, string[] Targets)>();
        var run = stage.Run(MainTank, probe: p =>
        {
            if (!p.Crossed(33f)) return;
            seats = PerRole.All.ToDictionary(role => p.Member(role)!.EntityId.ToString("X8"), role => role);
            hits.AddRange(p.Fake.Timeline.Rows
                .Where(row => row["kind"] == "ability" && row["id"] is "18484" or "19279")
                .Select(row => (Float(row["t"]), uint.Parse(row["id"]), row["targets"].Split(' ', StringSplitOptions.RemoveEmptyEntries))));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(hits.Select(h => h.At), Is.EqualTo(new[] { 15.4f, 16.9f, 20.1f, 21.6f, 24.8f, 26.3f, 29.4f, 30.9f }).Within(0.05f));
        Assert.That(hits.Select(h => h.Action), Is.EqualTo(Enumerable.Range(0, 8).Select(k => k % 2 == 0 ? P1ActionId.AlphaSword : P1ActionId.SuperBlasstyCharge)));
        Assert.That(hits.Select(h => h.Targets.Select(id => seats[id]).ToArray()), Is.EqualTo(Stage.Numbers.Select(role => new[] { role })));
    }

    // OT steps in front of #1 on the north spot: the cleave runs on along the rim through both.
    [Test]
    public void AFirstHitOnlyLeavesTheVulnerability()
    {
        var vulnerable = new List<PartyRole>();
        var run = new Stage().Run(OffTank, stopAt: 16.5f, takeovers: new Takeover(15.0f, new Vector2(6f, -14f)), probe: p =>
        {
            if (p.Crossed(16.2f))
                vulnerable.AddRange(PerRole.All.Where(role => p.Member(role)!.HasStatus(P1StatusId.CruiseChaserVulnerability)));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(vulnerable, Is.EquivalentTo(new[] { MeleeDpsA, OffTank }));
    }

    // M1, cleaved at 15.4, stands a yalm from #2 as it is charged.
    [Test]
    public void ACleavedPlayerInTheChargeDies()
    {
        var stage = new Stage();
        var run = stage.Run(MeleeDpsA, takeovers: new Takeover(16.0f, stage.HitSpot(2) + new Vector2(1f, 0f)));

        AssertFirstDeath(run, MeleeDpsA, P1ActionId.SuperBlasstyCharge, 17.9f, TeaP1Actions.SecondCruiseChaserHit);
    }

    // R1, charged at 16.9, stands 6 y ahead of #3 as it is cleaved.
    [Test]
    public void AChargedPlayerInALaterCleaveDies()
    {
        var stage = new Stage();
        var ahead = stage.HitSpot(3) + 6f * Stage.Facing(Stage.ClockwiseAlongTheRim(stage.HitSpot(3)));
        var run = stage.Run(PhysRangedDps, takeovers: new Takeover(19.3f, ahead));

        AssertFirstDeath(run, PhysRangedDps, P1ActionId.AlphaSword, 21.1f, TeaP1Actions.SecondCruiseChaserHit);
    }

    // #5's cleave runs on along the rim from the south-east spot, across the south-west one, where MT was cleaved
    // 4.7 s before.
    [Test]
    public void APlayerBackOnItsHitSpotIsCaughtByTheOtherGroupsNextCleave()
    {
        var stage = new Stage();
        var run = stage.Run(MainTank, takeovers: new Takeover(23.6f, stage.HitSpot(3)));

        AssertFirstDeath(run, MainTank, P1ActionId.AlphaSword, 25.8f, TeaP1Actions.SecondCruiseChaserHit);
    }

    [Test]
    public void JKickHitsEveryoneAndHurtsNobody()
    {
        var kicks = Sent(new Stage(), P1ActionId.JKick, out var everyoneVulnerable, readAt: 34.6f);

        Assert.That(kicks, Has.Count.EqualTo(1));
        Assert.That(kicks[0].At, Is.EqualTo(34.1f).Within(0.05f));
        Assert.That(kicks[0].Targets, Has.Length.EqualTo(8));
        Assert.That(everyoneVulnerable, Is.True, "all eight still carry Cruise Chaser's vulnerability");
    }

    [TestCase(1.5f)]
    [TestCase(3f)]
    [TestCase(4.5f)]
    public void CruiseChaserLandsBehindTheOddNumberByItsFacing(float landsBehind)
    {
        Placement? landed = null;
        Placement? beforeHeLands = null;
        var run = new Stage(landsBehind: landsBehind).Run(MeleeDpsA, stopAt: 15.3f, takeovers: new Takeover(14.7f, new Vector2(0f, -14f), Facing: 0f),
            probe: p =>
            {
                if (p.Crossed(14.8f)) beforeHeLands = CruiseChaser(p).Placement();
                if (p.Crossed(15.0f)) landed = CruiseChaser(p).Placement();
            });

        Assert.That(run.Passed, run.ToString);
        Assert.That(beforeHeLands!.Value.Position2, Is.EqualTo(Vector2.Zero), "he waits unseen in the middle");
        Assert.That(Vector2.Distance(landed!.Value.Position2, new Vector2(0f, -14f - landsBehind)), Is.LessThan(0.01f), $"landed at {landed.Value.Position2}");
        Assert.That(landed.Value.Rotation, Is.EqualTo(0f).Within(0.001f), "he faces the way the player faced");
    }

    // The stage's #1 faces along the rim, so he lands counter-clockwise of it, and turns to #2 for the charge
    // without leaving that spot.
    [Test]
    public void CruiseChaserChargesFromWhereHeCleaved()
    {
        var stage = new Stage();
        Placement? cleaving = null;
        Placement? charging = null;
        var run = stage.Run(MainTank, stopAt: 17.5f, probe: p =>
        {
            if (p.Crossed(15.45f)) cleaving = CruiseChaser(p).Placement();
            if (p.Crossed(16.95f)) charging = CruiseChaser(p).Placement();
        });

        var odd = stage.HitSpot(1);
        var even = stage.HitSpot(2);
        var behind = odd - 3f * Stage.Facing(Stage.ClockwiseAlongTheRim(odd));
        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(cleaving!.Value.Position2, behind), Is.LessThan(0.01f));
        Assert.That(cleaving.Value.Rotation, Is.EqualTo(Stage.ClockwiseAlongTheRim(odd)).Within(0.001f));
        Assert.That(Vector2.Distance(charging!.Value.Position2, behind), Is.LessThan(0.01f), "he has not moved");
        Assert.That(Vector2.Distance(Stage.Facing(charging.Value.Rotation), Vector2.Normalize(even - behind)), Is.LessThan(0.01f), "he faces #2");
    }

    // Once he has landed, the number steps 3 y back and inward: 0.9 y ahead of him and 2.1 y to the side, outside a
    // cone kept on the facing he landed with. Turned after it, the cleave runs on across the arena through others,
    // so the run stops before any of them falls.
    [TestCase(1, 14.9f, 15.4f)]
    [TestCase(3, 19.6f, 20.1f)]
    [TestCase(5, 24.3f, 24.8f)]
    [TestCase(7, 28.9f, 29.4f)]
    public void ANumberCannotStepOutOfItsOwnCleave(int number, float landsAt, float cleavedAt)
    {
        var seat = Stage.Numbers[number - 1];
        var stage = new Stage { HumanUntil = landsAt + 0.1f };
        var odd = stage.HitSpot(number);
        var steppedTo = odd + 3f * Stage.Facing(Stage.ClockwiseAlongTheRim(odd) - 0.75f * MathF.PI);
        var cleaved = false;
        Placement? cleaving = null;
        var run = stage.Run(seat, stopAt: cleavedAt + 0.9f, takeovers: new Takeover(landsAt + 0.1f, steppedTo), probe: p =>
        {
            if (p.Crossed(cleavedAt - 0.02f)) cleaving = CruiseChaser(p).Placement();
            if (p.Crossed(cleavedAt + 0.8f)) cleaved = p.Member(seat)!.HasStatus(P1StatusId.CruiseChaserVulnerability);
        });

        var behind = odd - 3f * Stage.Facing(Stage.ClockwiseAlongTheRim(odd));
        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(cleaving!.Value.Position2, behind), Is.LessThan(0.01f), "he has not moved");
        Assert.That(Vector2.Distance(Stage.Facing(cleaving.Value.Rotation), Vector2.Normalize(steppedTo - behind)), Is.LessThan(0.01f), $"he faces #{number}");
        Assert.That(cleaved, Is.True, $"#{number} was cleaved where it stepped to");
    }

    // He is hidden until a warp-in shows him, once, as he first lands.
    [Test]
    public void CruiseChaserAppearsAsHeFirstLands()
    {
        var warpIns = new List<float>();
        var run = new Stage().Run(MainTank, probe: p =>
        {
            if (!p.Crossed(33f)) return;
            var cruiseChaser = CruiseChaser(p).EntityId.ToString("X8");
            warpIns.AddRange(p.Fake.Timeline.Rows
                .Where(row => row["kind"] == "ac" && row["actor"] == cruiseChaser && row["id"] == "0x197"
                              && Convert.ToUInt32(row["p"].Split(',')[0], 16) == ActionTimelineId.WarpEnd)
                .Select(row => Float(row["t"])));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(warpIns, Is.EqualTo(new[] { 14.9f }).Within(0.05f));
    }

    // M1 dies to the north blaster at 13.4, 1.5 s before its landing; the run freezes at 18.4.
    [Test]
    public void ADeadNumbersCleaveGoesToSomeoneLiving()
    {
        var behindSomeoneLiving = false;
        var cleaves = 0;
        var run = new Stage().Run(MeleeDpsA, takeovers: new Takeover(12.0f, new Vector2(0f, -14f)), probe: p =>
        {
            if (p.Crossed(15.0f))
            {
                var landed = CruiseChaser(p).Placement();
                behindSomeoneLiving = PerRole.All.Except([MeleeDpsA]).Select(role => p.Member(role)!)
                    .Any(m => Vector3.Distance(landed.Position, m.Placement().MoveForward(-3f).Position) < 0.01f && MathF.Abs(landed.Rotation - m.Rotation) < 0.001f);
            }
            if (p.Crossed(15.6f))
                cleaves = p.Fake.Timeline.Rows.Count(row => row["kind"] == "ability" && row["id"] == $"{P1ActionId.AlphaSword}" && row["targets"].Length > 0);
        });

        AssertFirstDeath(run, MeleeDpsA, P1ActionId.HawkBlaster, 13.4f, BlastRule);
        Assert.That(behindSomeoneLiving, Is.True, "Cruise Chaser landed behind a living player");
        Assert.That(cleaves, Is.EqualTo(1), "and cleaved");
    }

    // R1 dies the same way; #1 is cleaved as usual and the charge turns to someone living.
    [Test]
    public void ADeadNumbersChargeGoesToSomeoneLiving()
    {
        var facesSomeoneLiving = false;
        var charges = 0;
        var run = new Stage().Run(PhysRangedDps, takeovers: new Takeover(12.0f, new Vector2(0f, -14f)), probe: p =>
        {
            if (p.Crossed(16.85f))
            {
                var aimed = CruiseChaser(p).Placement();
                facesSomeoneLiving = PerRole.All.Except([PhysRangedDps]).Select(role => p.Member(role)!)
                    .Any(m => MathF.Abs(aimed.Face(m.Position).Rotation - aimed.Rotation) < 0.001f);
            }
            if (p.Crossed(17.1f))
                charges = p.Fake.Timeline.Rows.Count(row => row["kind"] == "ability" && row["id"] == $"{P1ActionId.SuperBlasstyCharge}" && row["targets"].Length > 0);
        });

        AssertFirstDeath(run, PhysRangedDps, P1ActionId.HawkBlaster, 13.4f, BlastRule);
        Assert.That(facesSomeoneLiving, Is.True, "Cruise Chaser turned to a living player");
        Assert.That(charges, Is.EqualTo(1), "and charged");
    }

    // The run's own dice pick whom: over 40 seeds the dead M1's cleave lands behind most of the seven others.
    [Test]
    public void ADeadNumbersHitCanGoToAnyoneLiving()
    {
        var landedBehind = new HashSet<PartyRole>();
        for (var seed = 1; seed <= 40; seed++)
        {
            var run = new Stage { Seed = seed }.Run(MeleeDpsA, stopAt: 15.2f, takeovers: new Takeover(12.0f, new Vector2(0f, -14f)), probe: p =>
            {
                if (!p.Crossed(15.0f)) return;
                var landed = CruiseChaser(p).Placement();
                landedBehind.UnionWith(PerRole.All.Where(role => Vector3.Distance(landed.Position, p.Member(role)!.Placement().MoveForward(-3f).Position) < 0.01f));
            });
            Assert.That(run.Failure, Is.Null, run.ToString);
        }

        Assert.That(landedBehind, Has.No.Member(MeleeDpsA));
        Assert.That(landedBehind, Has.Count.GreaterThanOrEqualTo(5));
    }

    // M1 stands on the north-east blaster as it goes off at 14.6 and falls at 15.6. Until then it is still #1: Cruise
    // Chaser lands behind it at 14.9 and cleaves it at 15.4.
    [Test]
    public void ANumberABlastHasKilledDrawsItsOwnHitUntilItFalls()
    {
        var onTheBlaster = new Vector2(10f, -10f);
        Placement? landed = null;
        var mine = "";
        string[] cleaved = [];
        var run = new Stage().Run(MeleeDpsA, takeovers: new Takeover(14.2f, onTheBlaster, Facing: MathF.PI / 2f), probe: p =>
        {
            if (p.Crossed(15.0f)) landed = CruiseChaser(p).Placement();
            if (!p.Crossed(15.5f)) return;
            mine = p.Member(MeleeDpsA)!.EntityId.ToString("X8");
            cleaved = p.Fake.Timeline.Rows.Where(row => row["kind"] == "ability" && row["id"] == $"{P1ActionId.AlphaSword}")
                .SelectMany(row => row["targets"].Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        });

        AssertFirstDeath(run, MeleeDpsA, P1ActionId.HawkBlaster, 15.6f, BlastRule);
        Assert.That(Vector2.Distance(landed!.Value.Position2, onTheBlaster - new Vector2(3f, 0f)), Is.LessThan(0.01f), $"he landed at {landed.Value.Position2}");
        Assert.That(cleaved, Is.EqualTo(new[] { mine }), "he cleaved #1 alone");
    }

    // The pool can't count more busy than it holds, and a blast that finds none free is dropped with only a warning,
    // so the pool must keep one to spare.
    [Test]
    public void TheHelperPoolKeepsOneToSpare()
    {
        var peak = -1;
        var run = new Stage().Run(MainTank, probe: p =>
        {
            if (p.Crossed(35f)) peak = p.Game.Catalog.Scenarios.OfType<TeaP1LimitCutScenario>().Single().BusyHelperPeak;
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(peak, Is.InRange(1, P1Rules.LimitCutHelpers - 1));
        Assert.That(run.Warnings, Has.None.Contain("helpers busy"), run.ToString);
    }

    [Test]
    public void TwoSeatsOnOneNumberRefuseTheStart()
    {
        var run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), Stage.NoStrat, 1, new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            WriteArtifactsOnFailure = false,
            Overrides = o =>
            {
                var overrides = (TeaP1LimitCutStateOverrides)o;
                overrides.Number[MainTank] = 3;
                overrides.Number[CasterDps] = 3;
            },
        });

        Assert.That(run.Failure, Is.EqualTo("did not start"));
    }

    [Test]
    public void ItIsAPartyOnlyScenarioInTheP1Phase()
    {
        IScenario scenario = new TeaP1LimitCutScenario();

        Assert.Multiple(() =>
        {
            Assert.That(scenario.FullName(), Is.EqualTo("The Epic of Alexander — P1 Limit Cut"));
            Assert.That(scenario.Phase, Is.SameAs(TeaZone.P1));
            Assert.That(scenario.SupportsSolo, Is.False);
            Assert.That(scenario.SupportsMultiplayer, Is.False);
            Assert.That(scenario.AiStrats.Select(strat => (strat.Name, strat.Group)), Is.EqualTo(new[] { ("[LPDU] Light PF", "EU") }));
        });
    }

    private static ScenarioRun Mistake(PartyRole player, float at, Vector2 to, TeaP1FirstBlasters axis = WestEast)
        => new Stage(axis).Run(player, takeovers: new Takeover(at, to));

    internal static void AssertFirstDeath(ScenarioRun run, PartyRole role, uint actionId, float at, string because)
    {
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
        var first = run.Deaths[0];
        Assert.Multiple(() =>
        {
            Assert.That((first.Role, first.ActionId), Is.EqualTo((role, (uint?)actionId)), run.ToString);
            Assert.That(first.Time, Is.EqualTo(at).Within(0.1f), run.ToString);
            Assert.That(first.Cause, Does.Contain(because), run.ToString);
            Assert.That(run.Deaths.Count(death => death.Time <= first.Time + 0.01f), Is.EqualTo(1), $"nobody died with them{Environment.NewLine}{run}");
        });
    }

    internal static SimEnemy CruiseChaser(ScenarioProbe p)
        => p.World.Children.OfType<SimEnemy>().Single(enemy => enemy.BNpcBaseId == P1BNpcBaseId.CruiseChaser);

    private static List<(float At, Vector2 Position, string[] Targets)> Sent(Stage stage, uint actionId)
        => Sent(stage, actionId, out _, readAt: 33f);

    // A clean run's ActionEffects of `actionId`, at scenario-local spots.
    private static List<(float At, Vector2 Position, string[] Targets)> Sent(Stage stage, uint actionId, out bool everyoneVulnerable, float readAt)
    {
        var sent = new List<(float, Vector2, string[])>();
        var vulnerable = false;
        var run = stage.Run(MainTank, probe: p =>
        {
            if (!p.Crossed(readAt)) return;
            var origin = TeaZone.Instance.Origin;
            vulnerable = PerRole.All.All(role => p.Member(role)!.HasStatus(P1StatusId.CruiseChaserVulnerability));
            sent.AddRange(p.Fake.Timeline.Rows
                .Where(row => row["kind"] == "ability" && row["id"] == $"{actionId}")
                .Select(row => (Float(row["t"]), new Vector2(Float(row["x"]) - origin.X, Float(row["z"]) - origin.Z),
                                row["targets"].Split(' ', StringSplitOptions.RemoveEmptyEntries))));
        });
        Assert.That(run.Passed, run.ToString);
        everyoneVulnerable = vulnerable;
        return sent;
    }

    private static (float, float) Rounded(Vector2 position) => (MathF.Round(position.X, 1), MathF.Round(position.Y, 1));

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);
}
