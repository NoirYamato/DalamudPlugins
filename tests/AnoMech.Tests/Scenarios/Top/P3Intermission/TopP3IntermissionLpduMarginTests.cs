using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Intermission;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.TopP3IntermissionLpduHumanTests;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the clearances the design promises for the
// dodge, measured on every bot at each hit's snapshot. Each debuff roll is rotated through all eight
// seats, and each seed seats the player somewhere else, so every role holds every debuff as a bot in
// both patterns; the seeds vary the jitter. The player's seat is left out: under the debug driver it
// walks by movement input and can stop a frame's step past its spot, and in play it is a human,
// whom TopP3IntermissionLpduHumanTests covers.
public class TopP3IntermissionLpduMarginTests
{
    private const int LpduStrat = 0;
    // Two at least, so a role the player sits in on one seed is a bot on another.
    private static int SeedsPerRotation => Math.Max(2, TestContext.Parameters.Get("MarginSeeds", 3));

    // A bot lands anywhere within AiManager's default jitter of its spot; the design's table is rounded to 0.01.
    private const float Jitter = 0.3f, Rounding = 0.02f;
    private const float BlowRadius = 11f, CannonRadius = 6f, RingEdge = 18f;

    // Hits snapshot 0.3 s before their sheet time; the instant rings go off when cast.
    private const float Ring3At = 18.4f, Ring4At = 20.4f, Set1BlowsAt = 26.1f, Ring7At = 26.6f, CannonsAt = 28.3f,
                        Set2BlowsAt = 28.6f, Ring8At = 28.7f;

    // The design's clearance table, less the jitter each check can lose: one bot's against a fixed
    // hit, two bots' between groups.
    private static readonly (string Check, float Design, float Jitters)[] Table =
    [
        ("wait spot to the nearest first arm", 1.24f, 1f),
        ("wait spot outside ring 3", 0.73f, 1f),
        ("wait spot outside ring 7", 0.73f, 1f),
        ("in spot inside ring 4", 1.23f, 1f),
        ("final spot to the nearest second arm", 1.13f, 1f),
        ("final spot inside ring 8", 1.38f, 1f),
        ("two groups' cannons", 2.21f, 2f),
    ];

    // Off the table: what else the dodge must keep, with the same slack the other P3 margin tests ask
    // for. The void zone's end is UNVERIFIED, so nobody may count on it going on time: while it is up,
    // everyone stays a third of a second's walk out of it.
    private const float JitterSlack = 0.5f, VoidZoneSlack = 2f;

    private static readonly (string Check, float Within)[] Beyond =
    [
        ("everyone outside ring 1", JitterSlack),
        ("everyone outside ring 2", JitterSlack),
        ("everyone outside ring 5", JitterSlack),
        ("everyone outside ring 6", JitterSlack),
        ("everyone out of the void zone", VoidZoneSlack),
        ("stack partners inside their stack", JitterSlack),
    ];

    [TestCase(FirstArms.NorthTriangle)]
    [TestCase(FirstArms.SouthTriangle)]
    public void KeepsTheDesignsClearancesForEveryRoleAndDebuff(FirstArms arms)
    {
        var margins = new Dictionary<string, float>();
        var held = new HashSet<(PartyRole, SniperDebuff)>();
        var problems = new List<string>();
        PartyRole[] shuffled = [.. PerRole.All.OrderBy(_ => Random.Shared.Next())];
        for (var rotation = 0; rotation < 8; rotation++)
        for (var i = 0; i < SeedsPerRotation; i++)
        {
            var seed = Random.Shared.Next();
            PartyRole[] order = [.. Enumerable.Range(0, 8).Select(k => shuffled[(k + rotation) % 8])];
            var groups = Groups(order);
            var player = PerRole.All[(rotation + i) % 8];
            foreach (var (role, group) in groups.Where(g => g.Key != player)) held.Add((role, group.Debuff));
            var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = player,
                Overrides = o => TopP3IntermissionLpduAiTests.Pin((TopP3IntermissionStateOverrides)o, arms, Automarkers.On, order),
                Probe = p => Measure(p, player, groups, arms, margins),
            });
            if (!run.Passed) problems.Add(run.ToString());
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine,
            margins.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value.ToString("F2", CultureInfo.InvariantCulture)}")));
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(held, Has.Count.EqualTo(8 * 3), "every role held every debuff");
            foreach (var (check, design, jitters) in Table)
                Assert.That(margins[check], Is.GreaterThanOrEqualTo(design - jitters * Jitter - Rounding), check);
            foreach (var (check, within) in Beyond)
                Assert.That(margins[check], Is.GreaterThan(within), check);
        });
    }

    private static void Measure(ScenarioProbe p, PartyRole player, Dictionary<PartyRole, Group> groups, FirstArms arms,
                                Dictionary<string, float> margins)
    {
        void Note(string check, float clearance) => margins[check] = MathF.Min(margins.GetValueOrDefault(check, float.MaxValue), clearance);

        var members = PerRole.All.Where(r => r != player && p.Member(r).IsAlive())
                             .Select(r => (Role: r, At: Flat(p.Member(r)!.Position))).ToList();
        foreach (var (_, at) in members)
        {
            var r = at.Length();
            if (p.Time is >= 15.3f and < 30.7f) Note("everyone out of the void zone", r - P3Geometry.VoidZoneRadius);
            if (p.Crossed(14.0f)) Note("everyone outside ring 1", r - P3Geometry.WaveRepeaterInner2);
            if (p.Crossed(16.4f)) Note("everyone outside ring 2", MathF.Max(P3Geometry.WaveRepeaterInner2 - r, r - P3Geometry.WaveRepeaterInner3));
            if (p.Crossed(22.1f)) Note("everyone outside ring 5", r - P3Geometry.WaveRepeaterInner2);
            if (p.Crossed(24.5f)) Note("everyone outside ring 6", MathF.Max(P3Geometry.WaveRepeaterInner2 - r, r - P3Geometry.WaveRepeaterInner3));
            if (p.Crossed(Ring3At)) Note("wait spot outside ring 3", r - RingEdge);
            if (p.Crossed(Ring4At)) Note("in spot inside ring 4", RingEdge - r);
            if (p.Crossed(Set1BlowsAt)) Note("wait spot to the nearest first arm", NearestArm(at, FirstSet(arms)) - BlowRadius);
            if (p.Crossed(Ring7At)) Note("wait spot outside ring 7", r - RingEdge);
            if (p.Crossed(Set2BlowsAt)) Note("final spot to the nearest second arm", NearestArm(at, SecondSet(arms)) - BlowRadius);
            if (p.Crossed(Ring8At)) Note("final spot inside ring 8", RingEdge - r);
        }

        if (!p.Crossed(CannonsAt)) return;
        foreach (var a in members)
        foreach (var b in members.Where(b => b.Role > a.Role))
        {
            var d = Vector2.Distance(a.At, b.At);
            if (groups[a.Role].WallBearing == groups[b.Role].WallBearing) Note("stack partners inside their stack", CannonRadius - d);
            else Note("two groups' cannons", d - CannonRadius);
        }
    }
}
