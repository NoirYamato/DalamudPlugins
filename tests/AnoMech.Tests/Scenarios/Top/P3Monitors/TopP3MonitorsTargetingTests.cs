using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Monitors;

namespace AnoMech.Tests;

// Who a monitor's beam takes: the half-plane on its side of the line its owner faces along, the
// two nearest the owner inside it, and random others when fewer than two stand there.
public class TopP3MonitorsTargetingTests
{
    private const float FacingSouth = 0f;
    private const float FacingEast = MathF.PI / 2;
    private const float FacingNorth = MathF.PI;

    private sealed record Spot(Vector3 Position, float Rotation = 0f) : IPositioned
    {
        public Spot(float x, float z) : this(new Vector3(x, 0, z)) { }
    }

    private static Placement Facing(float rotation, float x = 0, float z = 0) => new(new Vector3(x, 0, z), rotation);

    // Facing north the right hand is east; facing south it is west.
    [TestCase(FacingNorth, MonitorSide.Right, 5f, 0f)]
    [TestCase(FacingNorth, MonitorSide.Left, -5f, 0f)]
    [TestCase(FacingSouth, MonitorSide.Right, -5f, 0f)]
    [TestCase(FacingSouth, MonitorSide.Left, 5f, 0f)]
    [TestCase(FacingEast, MonitorSide.Right, 0f, 5f)]
    [TestCase(FacingEast, MonitorSide.Left, 0f, -5f)]
    public void BeamsToTheOwnersHand(float facing, MonitorSide side, float x, float z)
    {
        Assert.That(TopP3MonitorsScenario.InBeam(Facing(facing), side, new Vector3(x, 0, z)), Is.True);
        Assert.That(TopP3MonitorsScenario.InBeam(Facing(facing), side, new Vector3(-x, 0, -z)), Is.False);
    }

    // P5's monitors pick through CharacterFind.OnSideN, which takes its side's members first.
    [TestCase(FacingNorth, MonitorSide.Left)]
    [TestCase(FacingNorth, MonitorSide.Right)]
    [TestCase(1.1f, MonitorSide.Left)]
    [TestCase(-2.4f, MonitorSide.Right)]
    public void AgreesWithP5OnWhichSideIsWhich(float facing, MonitorSide side)
    {
        var owner = Facing(facing, x: 3, z: -2);
        var spots = (from x in Enumerable.Range(-4, 9) from z in Enumerable.Range(-4, 9) select new Spot(4f * x + 0.5f, 4f * z + 0.25f)).ToList();
        var inBeam = spots.Where(s => TopP3MonitorsScenario.InBeam(owner, side, s.Position)).ToList();
        var p5Side = new CharacterFind<Spot>(spots).OnSideN(new Rng(1), owner, side.Mul(), count: spots.Count).Take(inBeam.Count);

        Assert.That(inBeam, Is.Not.Empty);
        Assert.That(p5Side, Is.EquivalentTo(inBeam));
    }

    [Test]
    public void TakesTheTwoNearestInTheBeam()
    {
        var owner = new Spot(0, 0);
        var near = new Spot(3, 0);
        var next = new Spot(2, 4);
        var far = new Spot(8, -1);
        var behind = new Spot(-1, 0);
        var targets = TopP3MonitorsScenario.MonitorTargets([far, behind, owner, next, near], Facing(FacingNorth), MonitorSide.Right, owner, new Rng(1));

        Assert.That(targets, Is.EqualTo(new[] { near, next }));
    }

    [Test]
    public void FillsAShortBeamWithOthersAtRandomButNeverItsOwner()
    {
        var owner = new Spot(0, 0);
        var inside = new Spot(6, 0);
        var others = Enumerable.Range(1, 5).Select(i => new Spot(-i, i)).ToList();
        var alive = others.Append(owner).Append(inside).ToList();
        var seconds = Enumerable.Range(0, 50)
            .Select(seed => TopP3MonitorsScenario.MonitorTargets(alive, Facing(FacingNorth), MonitorSide.Right, owner, new Rng(seed)))
            .Select(targets =>
            {
                Assert.That(targets, Has.Count.EqualTo(2));
                Assert.That(targets[0], Is.EqualTo(inside));
                return targets[1];
            })
            .ToList();

        Assert.That(seconds, Has.None.EqualTo(owner).And.None.EqualTo(inside));
        Assert.That(seconds.Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void AnEmptyBeamTakesTwoDifferentPlayersAtRandom()
    {
        var owner = new Spot(0, 0);
        var west = Enumerable.Range(1, 4).Select(i => new Spot(-i, 0)).ToList();
        var targets = TopP3MonitorsScenario.MonitorTargets(west.Append(owner).ToList(), Facing(FacingNorth), MonitorSide.Right, owner, new Rng(7));

        Assert.That(targets, Has.Count.EqualTo(2));
        Assert.That(targets, Is.Unique);
        Assert.That(targets, Has.None.EqualTo(owner));
    }
}
