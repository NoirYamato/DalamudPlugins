using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// The sunny-day soak only proves nobody dies. These are the room the strat leaves on every
// unpinned roll, read off the statuses each frame: a debugger appearing is a rot going off, a rot
// appearing is a pass, a regression disappearing is its tether popping.
public class TopP3HelloWorldLpduMarginTests
{
    private const int Seeds = 30;
    private const float JitterSlack = 0.5f;

    private static readonly float[] SetStarts = [19.1f, 40.1f, 61.2f, 82.5f];
    private static readonly float[] Resolves = [29.1f, 50.1f, 71.2f, 92.5f];
    private const float PassFrom = 10.3f, PassUntil = 12.3f;

    private sealed class Margins
    {
        public float ExplosionClearance = float.MaxValue;
        public float AccidentalContact = float.MaxValue;
        public float DefamationInside = float.MaxValue, DefamationOutside = float.MaxValue;
        public float StackInside = float.MaxValue, StackOutside = float.MaxValue;
        public float BreakGap = float.MaxValue;
        public int Passes;
        public readonly List<string> Problems = [];
    }

    [Test]
    public void KeepsTheSlideshowMarginsOnRandomRolls()
    {
        var margins = new Margins();
        for (var i = 0; i < Seeds; i++)
        {
            var seed = Random.Shared.Next();
            var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, seed,
                                          new ScenarioRunOptions { StopAt = 100f, Probe = Measure(margins, seed) });
            if (!run.Passed) margins.Problems.Add(run.ToString());
        }

        TestContext.Out.WriteLine(
            $"explosion clearance {margins.ExplosionClearance:F2}, accidental contact {margins.AccidentalContact:F2}, "
            + $"defamation inside {margins.DefamationInside:F2} / outside {margins.DefamationOutside:F2}, "
            + $"stack inside {margins.StackInside:F2} / outside {margins.StackOutside:F2}, break gap {margins.BreakGap:F2}s");
        Assert.Multiple(() =>
        {
            Assert.That(margins.Problems, Is.Empty);
            Assert.That(margins.Passes, Is.EqualTo(12 * Seeds), "four rots passed in each of the first three sets");
            Assert.That(margins.ExplosionClearance, Is.GreaterThan(5f + 1f), "nearest other player when a rot goes off");
            Assert.That(margins.AccidentalContact, Is.GreaterThan(P3Geometry.RotContactRadius + 0.3f),
                        "nearest a player who could catch a rot comes to one outside the passes");
            Assert.That(margins.DefamationInside, Is.GreaterThan(JitterSlack), "how far inside a defamation its target stands");
            Assert.That(margins.DefamationOutside, Is.GreaterThan(JitterSlack), "how far outside a defamation everyone else stands");
            Assert.That(margins.StackInside, Is.GreaterThan(JitterSlack), "how far inside a stack its partners stand");
            Assert.That(margins.StackOutside, Is.GreaterThan(JitterSlack), "how far outside a stack everyone else stands");
            Assert.That(margins.BreakGap, Is.GreaterThan(1f), "seconds between two tether breaks");
        });
    }

    private static Action<ScenarioProbe> Measure(Margins margins, int seed)
    {
        var before = new Dictionary<PartyRole, HashSet<ushort>>();
        var bugHolders = new List<(SimCharacter Holder, bool Defamation)>();
        var breaks = new List<float>();
        return p =>
        {
            var now = new Dictionary<PartyRole, HashSet<ushort>>();
            foreach (var role in PerRole.All)
                if (p.Member(role) is { } member && member.IsAlive())
                    now[role] = member.ActiveStatuses.Select(s => s.StatusId).ToHashSet();
            var set = SetStarts.Count(start => p.Time >= start);

            foreach (var resolve in Resolves.Where(r => p.Crossed(r - 0.05f)))
            {
                bugHolders.Clear();
                foreach (var (role, statuses) in now)
                {
                    if (statuses.Contains(P3StatusId.CriticalOverflowBug)) bugHolders.Add((p.Member(role)!, true));
                    if (statuses.Contains(P3StatusId.CriticalSynchronizationBug)) bugHolders.Add((p.Member(role)!, false));
                }
            }
            if (Resolves.Any(p.Crossed)) MeasureHits(p, margins, bugHolders, lastSet: set == 4);

            foreach (var (role, statuses) in now)
            {
                if (!before.TryGetValue(role, out var had)) continue;
                var member = p.Member(role)!;
                if (statuses.Except(had).Any(id => id is P3StatusId.UnderflowDebugger or P3StatusId.PerformanceDebugger))
                    margins.ExplosionClearance = Math.Min(margins.ExplosionClearance, NearestOther(p, member));
                if (p.Time > 8.5f && statuses.Except(had).Any(id => id is P3StatusId.RedRot or P3StatusId.BlueRot))
                {
                    margins.Passes++;
                    if (!InPass(p.Time)) margins.Problems.Add($"seed {seed}: {role} caught a rot at t={p.Time:F2}, outside the passes");
                }
                if (had.Except(statuses).Any(id => id is P3StatusId.LocalRegression or P3StatusId.RemoteRegression)
                    && breaks.All(b => p.Time - b > 0.01f))
                {
                    var local = had.Contains(P3StatusId.LocalRegression);
                    if (breaks.Count > 0) margins.BreakGap = Math.Min(margins.BreakGap, p.Time - breaks[^1]);
                    breaks.Add(p.Time);
                    if (EarliestBreak(set, local) is var earliest && p.Time < earliest)
                        margins.Problems.Add($"seed {seed}: set {set} {(local ? "local" : "remote")} tether broke at t={p.Time:F2}, before {earliest:F1}");
                }
            }

            if (p.Time >= 8f && !InPass(p.Time))
                foreach (var (holder, held) in now)
                foreach (var (other, otherHeld) in now)
                    if (holder != other && CouldCatch(held, otherHeld))
                        margins.AccidentalContact = Math.Min(margins.AccidentalContact, Distance(p.Member(holder)!, p.Member(other)!));

            before.Clear();
            foreach (var (role, statuses) in now) before[role] = statuses;
        };
    }

    private static void MeasureHits(ScenarioProbe p, Margins margins, List<(SimCharacter Holder, bool Defamation)> holders, bool lastSet)
    {
        foreach (var (holder, defamation) in holders)
        {
            var others = PerRole.All.Select(r => p.Member(r)!).Where(m => m != holder && m.IsAlive())
                                    .Select(m => Distance(holder, m)).Order().ToList();
            var radius = defamation ? 20f : 5f;
            var partners = defamation ? (lastSet ? 0 : 1) : (lastSet ? 2 : 1);
            var inside = partners > 0 ? radius - others[partners - 1] : float.MaxValue;
            var outside = others[partners] - radius;
            if (defamation)
            {
                margins.DefamationInside = Math.Min(margins.DefamationInside, inside);
                margins.DefamationOutside = Math.Min(margins.DefamationOutside, outside);
            }
            else
            {
                margins.StackInside = Math.Min(margins.StackInside, inside);
                margins.StackOutside = Math.Min(margins.StackOutside, outside);
            }
        }
    }

    private static bool CouldCatch(HashSet<ushort> holder, HashSet<ushort> other)
        => (holder.Contains(P3StatusId.RedRot) && !other.Contains(P3StatusId.RedRot) && !other.Contains(P3StatusId.UnderflowDebugger))
           || (holder.Contains(P3StatusId.BlueRot) && !other.Contains(P3StatusId.BlueRot) && !other.Contains(P3StatusId.PerformanceDebugger));

    private static bool InPass(float time)
        => SetStarts.Take(3).Any(start => time >= start + PassFrom && time <= start + PassUntil);

    // Remote at the remote pop, local at the local pop; in the last set the local pair stands
    // together, so its tether pops as it goes live.
    private static float EarliestBreak(int set, bool local)
        => set == 4 && local ? 91f : SetStarts[set - 1] + (local ? 15f : 12.3f);

    private static float NearestOther(ScenarioProbe p, SimCharacter member)
        => PerRole.All.Select(r => p.Member(r)!).Where(m => m != member && m.IsAlive()).Min(m => Distance(member, m));

    private static float Distance(SimCharacter a, SimCharacter b)
        => Vector2.Distance(new(a.Position.X, a.Position.Z), new(b.Position.X, b.Position.Z));
}
