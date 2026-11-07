using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// Cosmo Dive cast at 2.0 (5.6 s, so it ends at 7.6); its hits land 2.4 s later, at 10.0: a buster (31655, r 8) on each
// of the two players closest to Alpha Omega, a stack (31656, r 6) on the farthest of the rest. Unless a test moves
// someone, everyone stands where LPDU's first Cosmo Dive puts them: the main tank at (-4.6, -4.6) and the off-tank at
// (4.6, -4.6), both r 6.5 and 9.2 y apart, the rest stacked at (7.5, 7.5), r 10.6.
public class TopP6CosmoDiveTests
{
    private static readonly Vector2 Stack = new(7.5f, 7.5f);
    private static readonly PartyRole[] TheSix = [RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static readonly Dictionary<PartyRole, Vector2> ForTheDive = new Dictionary<PartyRole, Vector2>
    {
        [MainTank] = new(-4.6f, -4.6f),
        [OffTank] = new(4.6f, -4.6f),
    }.Concat(TheSix.Select(role => KeyValuePair.Create(role, Stack))).ToDictionary();

    [Test]
    public void TheTwoClosestTakeTheBustersAndTheFarthestTheStack()
    {
        var seats = new Dictionary<string, PartyRole>();
        var run = Dive(seats);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Sent("ability", ActionId.CosmoDive_7BA7).Select(b => seats[b.Target]), Is.EquivalentTo(new[] { MainTank, OffTank }));
        Assert.That(TheSix, Does.Contain(seats[run.Sent("ability", ActionId.CosmoDive_7BA8).Single().Target]));
        Assert.That(run.Sent("ability", ActionId.CosmoDive_7BA7, ActionId.CosmoDive_7BA8).Select(a => a.Time), Has.All.EqualTo(10f).Within(TimelineRun.FrameLate));
    }

    // Leaving the stack also leaves it five short.
    [Test]
    public void ANonTankClosestToAlphaOmegaDies()
    {
        var run = Dive(new(), (1.5f, RegenHealer, new Vector2(0f, 3f)));

        Assert.That(FirstDeaths(run), Is.EquivalentTo(
            new[] { (RegenHealer, ActionId.CosmoDive_7BA7, TopP6AlphaOmega.CosmoDiveBusterOnANonTank) }
                .Concat(TheSix.Where(r => r != RegenHealer).Select(r => (r, ActionId.CosmoDive_7BA8, TopP6AlphaOmega.TooFewInTheCosmoDiveStack(5))))),
            run.ToString);
    }

    // The off-tank closes in to 3.6 y of the main tank: each is in the other's buster.
    [Test]
    public void TanksTooCloseTogetherBothDie()
    {
        var run = Dive(new(), (1.5f, OffTank, new Vector2(-1f, -4.6f)));

        Assert.That(FirstDeaths(run), Is.EquivalentTo(new[]
        {
            (MainTank, ActionId.CosmoDive_7BA7, TopP6AlphaOmega.InSomeoneElsesCosmoDiveBuster),
            (OffTank, ActionId.CosmoDive_7BA7, TopP6AlphaOmega.InSomeoneElsesCosmoDiveBuster),
        }), run.ToString);
    }

    // The off-tank, still second closest, stands 3.5 y from the stack: in it, and the stack in its buster.
    [Test]
    public void ABusterTargetInTheStackDies()
    {
        var run = Dive(new(), (1.5f, OffTank, new Vector2(7.5f, 4f)));

        Assert.That(FirstDeaths(run), Is.EquivalentTo(
            new[] { (OffTank, ActionId.CosmoDive_7BA8, TopP6AlphaOmega.CosmoDiveBusterAndStack) }
                .Concat(TheSix.Select(r => (r, ActionId.CosmoDive_7BA7, TopP6AlphaOmega.InSomeoneElsesCosmoDiveBuster)))),
            run.ToString);
    }

    // The caster stands apart, neither closest nor farthest.
    [Test]
    public void AStackOfFiveKillsAllFive()
    {
        var run = Dive(new(), (1.5f, CasterDps, new Vector2(-7f, 7f)));

        Assert.That(FirstDeaths(run), Is.EquivalentTo(
            TheSix.Where(r => r != CasterDps).Select(r => (r, ActionId.CosmoDive_7BA8, TopP6AlphaOmega.TooFewInTheCosmoDiveStack(5)))),
            run.ToString);
    }

    // The healer is closest until just before the hits.
    [Test]
    public void TheTargetsAreTakenAsTheHitsLand()
    {
        var run = Dive(new(), (1.5f, RegenHealer, new Vector2(0f, 3f)), (9.9f, RegenHealer, Stack));

        Assert.That(run.Deaths, Is.Empty, run.ToString);
    }

    // Everyone on their spot at 1.0, then the moves, on the run's clock.
    private static TimelineRunResult Dive(Dictionary<string, PartyRole> seats, params (float At, PartyRole Role, Vector2 To)[] moves)
        => TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(1f, () =>
            {
                TimelineRun.Place(world, ForTheDive);
                foreach (var (id, role) in TimelineRun.Seats(world)) seats[id] = role;
            });
            foreach (var (at, role, to) in moves)
                world.Events.Add(at, () => TimelineRun.Place(world, role, to.X, to.Y));
            alphaOmega.CosmoDive(2f);
        });

    private static IEnumerable<(PartyRole, uint, string)> FirstDeaths(TimelineRunResult run)
        => run.Deaths.Where(d => d.Time <= run.Deaths[0].Time + 1e-3f)
                     .Select(d => (d.Role, d.ActionId ?? 0, d.Cause[(d.Cause.IndexOf('(') + 1)..^1]));
}
