using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P3Monitors;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The LPDU slideshow's example (https://ff14.toolboxgaming.space/?id=736282987145761): the line-up
// runs RegenHealer ShieldHealer MainTank OffTank MeleeDpsA MeleeDpsB PhysRangedDps CasterDps north
// to south, and OffTank, MeleeDpsA and PhysRangedDps carry monitors, so they take spots M1-M3 and
// the other five N1-N5. Spots are written for Omega beaming west; east mirrors x. The automarkers
// count the same way: Bind 1-3 for M1-M3, Attack 1-5 for N1-N5.
public class TopP3MonitorsLpduAiTests
{
    private const int LpduStrat = 0;
    private const float Tolerance = 0.75f;
    private const float FacingTolerance = 0.01f;

    private const float South = 0f;
    private const float East = MathF.PI / 2;
    private const float North = MathF.PI;
    private const float West = -MathF.PI / 2;

    private static readonly PartyRole[] SlideshowMonitors = [OffTank, MeleeDpsA, PhysRangedDps];

    private static readonly Vector2 M1 = new(10.05f, -12.18f);
    private static readonly Vector2 M2 = new(-9.84f, -8.35f);
    private static readonly Vector2 M3 = new(-9.92f, 9.24f);
    private static readonly Vector2 N1 = new(1.98f, -16.60f);
    private static readonly Vector2 N2 = new(13.54f, -4.44f);
    private static readonly Vector2 N3 = new(13.71f, 4.99f);
    private static readonly Vector2 N4 = new(6.14f, 11.20f);
    private static readonly Vector2 N5 = new(2.23f, 18.34f);

    // Bearings 320, 304.9 ... 214.3 degrees at r 13.15.
    private static readonly Dictionary<PartyRole, Vector2> CongaSpots = new()
    {
        [RegenHealer] = new(-8.45f, -10.07f), [ShieldHealer] = new(-10.78f, -7.52f),
        [MainTank] = new(-12.37f, -4.45f), [OffTank] = new(-13.11f, -1.08f),
        [MeleeDpsA] = new(-12.93f, 2.37f), [MeleeDpsB] = new(-11.87f, 5.66f),
        [PhysRangedDps] = new(-9.98f, 8.56f), [CasterDps] = new(-7.41f, 10.86f),
    };

    private const float CriticalErrorResolves = 8.1f;
    private const float CongaFormedBy = 12f;
    private const float MonitorsAppear = 19.4f;

    // Critical Error is cast from the start, so nobody leaves the stack for the line before it resolves.
    [Test]
    public void StaysTogetherUntilCriticalErrorResolves()
    {
        var spreadBefore = 0f;
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = CriticalErrorResolves + 0.4f,
            Probe = p =>
            {
                if (p.Time < 5f) return;
                var spots = PerRole.All.Select(r => Flat(p.Member(r)!.Position)).ToList();
                spreadBefore = MathF.Max(spreadBefore, spots.Max(a => spots.Max(b => Vector2.Distance(a, b))));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(spreadBefore, Is.LessThan(5f), $"seed {run.Seed}");
    }

    // Every player stands on its conga spot from 12s until the monitors appear, then leaves it.
    [TestCase(OmegaMonitor.West, MainTank)]
    [TestCase(OmegaMonitor.East, CasterDps)]
    public void HoldsTheCongaLineUntilTheMonitorsAppear(OmegaMonitor omega, PartyRole player)
    {
        float? appeared = null;
        var offLine = new Dictionary<PartyRole, (float Time, Vector2 At)>();
        var leftAt = new Dictionary<PartyRole, float>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = MonitorsAppear + 4f,
            Overrides = o => Pin((TopP3MonitorsStateOverrides)o, omega, SlideshowMonitors,
                                 [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left]),
            Probe = p =>
            {
                if (p.Time < CongaFormedBy) return;
                appeared ??= SlideshowMonitors.Any(r => p.Member(r)!.HasStatus(TopConstants.StatusId.PlayerMonitorLeft)
                                                        || p.Member(r)!.HasStatus(TopConstants.StatusId.PlayerMonitorRight))
                    ? p.Time
                    : null;
                foreach (var (role, spot) in CongaSpots)
                {
                    var at = Flat(p.Member(role)!.Position);
                    if (Vector2.Distance(at, spot) < Tolerance) continue;
                    if (appeared is null) offLine.TryAdd(role, (p.Time, at));
                    else leftAt.TryAdd(role, p.Time);
                }
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(appeared, Is.EqualTo(MonitorsAppear).Within(0.05f), $"seed {run.Seed}");
            foreach (var (role, (time, at)) in offLine)
                Assert.Fail($"{role} off its conga spot {CongaSpots[role]} at {time:F2}s, at {at} (seed {run.Seed})");
            // With Omega beaming west, spot M3 lies within a step of PhysRangedDps's conga spot.
            var leaving = PerRole.All.Where(r => omega == OmegaMonitor.East || r != PhysRangedDps);
            Assert.That(leftAt.Keys, Is.SupersetOf(leaving), $"not everyone left the line (seed {run.Seed})");
        });
    }

    // Spot M1 aims away from Omega's beam, M2 north and M3 south: a left monitor's owner faces a
    // quarter turn clockwise from the beam, a right one's a quarter turn counter-clockwise.
    [Test]
    public void TakesTheSlideshowSpotsWhenOmegaBeamsWest()
        => AssertSpots(OmegaMonitor.West, SlideshowMonitors, [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left], at: 29.3f, new()
        {
            [OffTank] = M1, [MeleeDpsA] = M2, [PhysRangedDps] = M3,
            [RegenHealer] = N1, [ShieldHealer] = N2, [MainTank] = N3, [MeleeDpsB] = N4, [CasterDps] = N5,
        }, facings: new()
        {
            [OffTank] = South, [MeleeDpsA] = West, [PhysRangedDps] = West,
        });

    [Test]
    public void MirrorsTheSpotsWhenOmegaBeamsEast()
        => AssertSpots(OmegaMonitor.East, SlideshowMonitors, [MonitorSide.Right, MonitorSide.Left, MonitorSide.Right], at: 29.3f, new()
        {
            [OffTank] = Mirror(M1), [MeleeDpsA] = Mirror(M2), [PhysRangedDps] = Mirror(M3),
            [RegenHealer] = Mirror(N1), [ShieldHealer] = Mirror(N2), [MainTank] = Mirror(N3),
            [MeleeDpsB] = Mirror(N4), [CasterDps] = Mirror(N5),
        }, facings: new()
        {
            [OffTank] = South, [MeleeDpsA] = East, [PhysRangedDps] = East,
        });

    // RegenHealer, MeleeDpsB and CasterDps carry the monitors, so the other five take N1-N5 in
    // line-up order. The player's own seat holds spot M2's monitor and is turned like a bot.
    [TestCase(OmegaMonitor.West, MonitorSide.Right, North, MonitorSide.Left, East, MonitorSide.Right, East)]
    [TestCase(OmegaMonitor.East, MonitorSide.Left, North, MonitorSide.Right, West, MonitorSide.Left, West)]
    public void LabelsFollowTheLineUpNotTheSeats(OmegaMonitor omega, MonitorSide firstSide, float firstFacing,
                                                 MonitorSide secondSide, float secondFacing,
                                                 MonitorSide thirdSide, float thirdFacing)
    {
        var x = omega == OmegaMonitor.East ? new Vector2(-1, 1) : Vector2.One;
        AssertSpots(omega, [RegenHealer, MeleeDpsB, CasterDps], [firstSide, secondSide, thirdSide], at: 29.3f, new()
        {
            [RegenHealer] = M1 * x, [MeleeDpsB] = M2 * x, [CasterDps] = M3 * x,
            [ShieldHealer] = N1 * x, [MainTank] = N2 * x, [OffTank] = N3 * x, [MeleeDpsA] = N4 * x, [PhysRangedDps] = N5 * x,
        }, facings: new()
        {
            [RegenHealer] = firstFacing, [MeleeDpsB] = secondFacing, [CasterDps] = thirdFacing,
        }, player: MeleeDpsB);
    }

    private static IEnumerable<TestCaseData> SignCases()
    {
        yield return new TestCaseData(SlideshowMonitors, MainTank, new Dictionary<PartyRole, Sign>
        {
            [OffTank] = Sign.Bind1, [MeleeDpsA] = Sign.Bind2, [PhysRangedDps] = Sign.Bind3,
            [RegenHealer] = Sign.Attack1, [ShieldHealer] = Sign.Attack2, [MainTank] = Sign.Attack3,
            [MeleeDpsB] = Sign.Attack4, [CasterDps] = Sign.Attack5,
        }).SetName("{m}(the slideshow's monitors)");
        yield return new TestCaseData(new[] { CasterDps, RegenHealer, MeleeDpsB }, MeleeDpsB, new Dictionary<PartyRole, Sign>
        {
            [RegenHealer] = Sign.Bind1, [MeleeDpsB] = Sign.Bind2, [CasterDps] = Sign.Bind3,
            [ShieldHealer] = Sign.Attack1, [MainTank] = Sign.Attack2, [OffTank] = Sign.Attack3,
            [MeleeDpsA] = Sign.Attack4, [PhysRangedDps] = Sign.Attack5,
        }).SetName("{m}(the player holding a monitor, pinned out of line-up order)");
    }

    // Marked once the monitors appear, before anyone leaves the line for their spot.
    [TestCaseSource(nameof(SignCases))]
    public void AutomarkersSignTheMonitorsBindAndTheOthersAttackInLineUpOrder(PartyRole[] monitors, PartyRole player,
                                                                              Dictionary<PartyRole, Sign> signs)
    {
        var marks = Marks(monitors, player, Automarkers.On);

        Assert.That(marks.Keys, Is.EquivalentTo(PerRole.All));
        Assert.Multiple(() =>
        {
            foreach (var (role, sign) in signs)
            {
                Assert.That(marks[role].Sign, Is.EqualTo(sign), $"{role}'s sign");
                Assert.That(marks[role].At, Is.EqualTo(19.5f).Within(0.05f), $"{role}'s mark time");
            }
        });
    }

    [Test]
    public void AutomarkersOffPlacesNoSign()
        => Assert.That(Marks(SlideshowMonitors, MainTank, Automarkers.Off), Is.Empty);

    [Test]
    public void AutomarkersOnAndOffPutEveryoneOnTheSameSpots()
    {
        foreach (var seed in Enumerable.Range(0, 20).Select(_ => Random.Shared.Next()))
        {
            var on = SpotsBeforeTheMonitorsFire(seed, Automarkers.On);
            var off = SpotsBeforeTheMonitorsFire(seed, Automarkers.Off);
            foreach (var role in PerRole.All)
                Assert.That(Vector2.Distance(on[role], off[role]), Is.LessThan(0.01f),
                            $"{role}: {on[role]} with automarkers, {off[role]} without (seed {seed})");
        }
    }

    // The sign each member was marked with, and when; rows with no target are the clears.
    private static Dictionary<PartyRole, (Sign Sign, float At)> Marks(PartyRole[] monitors, PartyRole player, Automarkers markers)
    {
        var marks = new Dictionary<PartyRole, (Sign, float)>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = 20.5f,
            Overrides = o =>
            {
                var overrides = (TopP3MonitorsStateOverrides)o;
                Pin(overrides, OmegaMonitor.West, monitors, [MonitorSide.Left, MonitorSide.Right, MonitorSide.Left]);
                overrides.Automarkers = markers;
            },
            Probe = p =>
            {
                if (!p.Crossed(20.4f)) return;
                var roleById = PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
                foreach (var row in p.Fake.Timeline.Rows.Where(r => r["kind"] == "ac_target" && r["id"] == "0x1F6" && r["target"] != ""))
                    marks[roleById[row["target"]]] = ((Sign)Convert.ToUInt32(row["p"].Split(',')[0], 16),
                                                     float.Parse(row["t"], CultureInfo.InvariantCulture));
            },
        });

        Assert.That(run.Passed, run.ToString);
        return marks;
    }

    private static Dictionary<PartyRole, Vector2> SpotsBeforeTheMonitorsFire(int seed, Automarkers markers)
    {
        var seen = new Dictionary<PartyRole, Vector2>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 29.4f,
            Overrides = o => ((TopP3MonitorsStateOverrides)o).Automarkers = markers,
            Probe = p =>
            {
                if (!p.Crossed(29.3f)) return;
                foreach (var role in PerRole.All)
                    seen[role] = Flat(p.Member(role)!.Position);
            },
        });

        Assert.That(run.Passed, run.ToString);
        return seen;
    }

    private static Vector2 Mirror(Vector2 spot) => spot with { X = -spot.X };

    private static void AssertSpots(OmegaMonitor omega, PartyRole[] monitors, MonitorSide[] sides, float at,
                                    Dictionary<PartyRole, Vector2> expected, Dictionary<PartyRole, float>? facings = null,
                                    PartyRole player = MainTank)
    {
        var seen = new Dictionary<PartyRole, Vector2>();
        var turned = new Dictionary<PartyRole, float>();
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = at + 0.1f,
            Overrides = o => Pin((TopP3MonitorsStateOverrides)o, omega, monitors, sides),
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                foreach (var role in PerRole.All)
                    if (p.Member(role) is { } member)
                    {
                        seen[role] = Flat(member.Position);
                        turned[role] = member.Rotation;
                    }
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (role, spot) in expected)
                Assert.That(Vector2.Distance(seen[role], spot), Is.LessThan(Tolerance),
                            $"{role} at {seen[role]}, expected {spot} (seed {run.Seed})");
            foreach (var (role, facing) in facings ?? [])
                Assert.That(MathF.Abs(MathF.IEEERemainder(turned[role] - facing, MathF.Tau)), Is.LessThan(FacingTolerance),
                            $"{role} faces {turned[role]:F3}, expected {facing:F3} (seed {run.Seed})");
        });
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    internal static void Pin(TopP3MonitorsStateOverrides o, OmegaMonitor omega, PartyRole[] monitors, MonitorSide[] sides)
    {
        o.OmegaMonitor = omega;
        foreach (var role in PerRole.All)
            o.Monitor[role] = monitors.Contains(role);
        for (var i = 0; i < monitors.Length; i++)
            o.Side[monitors[i]] = sides[i];
    }
}
