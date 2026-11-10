using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Intermission;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

public class TopP3IntermissionStateTests
{
    private static readonly float[] Hexagon = [0f, 60f, 120f, 180f, 240f, 300f];
    private static readonly float[] NorthTriangle = [0f, 120f, 240f];

    // Three arms of each kind on random spots: every spot is sometimes either kind, and now and then a set is all one.
    [Test]
    public void ThreeArmsOfEachKindStandOnRandomSpots()
    {
        var party = Party();
        var rolls = Enumerable.Range(1, 200)
                              .Select(seed => new TopP3IntermissionState(new Rng(seed), party, new TopP3IntermissionStateOverrides()).LeftArmBearings)
                              .ToList();

        Assert.Multiple(() =>
        {
            foreach (var left in rolls)
            {
                Assert.That(left, Has.Count.EqualTo(3).And.Unique);
                Assert.That(left, Is.SubsetOf(Hexagon));
            }
            foreach (var bearing in Hexagon)
                Assert.That(rolls.Count(left => left.Contains(bearing)), Is.InRange(60, 140), $"left arms at {bearing}");
            Assert.That(rolls.Count(left => left.All(NorthTriangle.Contains) || !left.Any(NorthTriangle.Contains)), Is.InRange(5, 40),
                        "rolls whose sets are each all one kind");
        });
    }

    // The arms are drawn before the debuffs, so pinning a seat's debuff or the first set leaves them as the seed had them.
    [Test]
    public void PinningTheOtherRollsLeavesTheArmKinds()
    {
        var party = Party();
        PerRole.ForceSeats = true;
        try
        {
            Assert.Multiple(() =>
            {
                foreach (var seed in Enumerable.Range(1, 50))
                {
                    var rolled = new TopP3IntermissionState(new Rng(seed), party, new TopP3IntermissionStateOverrides());
                    var pinned = new TopP3IntermissionStateOverrides { FirstArms = FirstArms.SouthTriangle };
                    pinned.Debuff[MainTank] = SniperDebuff.Stack;
                    pinned.Debuff[CasterDps] = SniperDebuff.Nothing;
                    Assert.That(new TopP3IntermissionState(new Rng(seed), party, pinned).LeftArmBearings, Is.EqualTo(rolled.LeftArmBearings),
                                $"seed {seed}");
                }
            });
        }
        finally
        {
            PerRole.ForceSeats = false;
        }
    }

    private static SimParty Party()
    {
        SimParty? party = null;
        var run = ScenarioRun.Execute(typeof(TopP3IntermissionScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 0.1f,
            Probe = p => party ??= p.World.Party,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return party!;
    }
}
