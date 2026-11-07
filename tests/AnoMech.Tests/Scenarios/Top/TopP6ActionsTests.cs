using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;
using Actions = AnoMech.Scenarios.Top.TopP6Actions;

namespace AnoMech.Tests;

// The Cosmo Meteor actions on their own, cast for Alpha Omega by its helpers. Unless a test moves someone, everyone
// stands on LPDU's meteor spread, r 13, 45° apart: R1 north, then R2, H2, M2, OT, M1, H1, MT clockwise.
public class TopP6ActionsTests
{
    private static readonly PartyRole[] Clockwise = [PhysRangedDps, CasterDps, ShieldHealer, MeleeDpsB, OffTank, MeleeDpsA, RegenHealer, MainTank];

    private static readonly Dictionary<PartyRole, Vector2> SpreadOut = Clockwise
        .Select((role, i) => (role, Flat(TopP6AlphaOmega.AtBearing(45f * i, 13f))))
        .ToDictionary(s => s.role, s => s.Item2);

    // Everyone baits at the centre at 2.0 (r 10, 4 s); all but the main tank are back on the spread by 4.0.
    [Test]
    public void ACosmoMeteorBaitKillsWhoeverStaysOnIt()
    {
        var run = Meteor(Everyone(Vector2.Zero), (alphaOmega, world) =>
        {
            world.Events.Add(2f, () => BaitedPuddles.Drop(world.Party, Actions.CosmoMeteorBait, alphaOmega.CastFromHelper));
            world.Events.Add(4f, () => TimelineRun.Place(world, SpreadOut.Where(s => s.Key != MainTank).ToDictionary()));
        });

        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (MainTank, (uint?)P6ActionId.CosmoMeteorBait) }), run.ToString);
    }

    // Four spreads at 2.0, the other four at 3.0, each on its own spot.
    [Test]
    public void OneSpreadEachIsLivedThrough()
    {
        var run = Meteor(SpreadOut, Spreads);

        Assert.That(run.Passed, run.ToString);
    }

    // The first melee stands 3 y from the main tank: each is hit by the other's spread a second after their own.
    [Test]
    public void StandingInAnothersSpreadKillsBoth()
    {
        var run = Meteor(Moved(SpreadOut, (MeleeDpsA, SpreadOut[MainTank] + new Vector2(3f, 0f))), Spreads);

        Assert.That(FirstDeaths(run), Is.EquivalentTo(new[]
        {
            (MainTank, (uint?)P6ActionId.CosmoMeteorSpread),
            (MeleeDpsA, (uint?)P6ActionId.CosmoMeteorSpread),
        }), run.ToString);
    }

    // The main tank's own spreads land 4 s apart, past the vulnerability the first leaves.
    [Test]
    public void TheVulnerabilityWearsOffBeforeTheNextSpread()
    {
        var run = Meteor(SpreadOut, (alphaOmega, world) =>
        {
            world.Events.Add(2f, () => alphaOmega.CastFromHelper(Actions.CosmoMeteorSpread, world.Party.Get(MainTank)));
            world.Events.Add(2f + P6Duration.SpreadVulnerability + 1f, () => alphaOmega.CastFromHelper(Actions.CosmoMeteorSpread, world.Party.Get(MainTank)));
        });

        Assert.That(run.Passed, run.ToString);
    }

    [TestCase(4, true)]
    [TestCase(5, false)]
    public void TheStackNeedsFive(int inIt, bool theyDie)
    {
        var stackers = Clockwise.Take(inIt).ToList();
        var run = Meteor(Moved(SpreadOut, stackers.Select(r => (r, new Vector2(0f, -13f))).ToArray()), (alphaOmega, world) =>
            world.Events.Add(2f, () => alphaOmega.CastFromHelper(Actions.CosmoMeteorStack, world.Party.Get(PhysRangedDps))));

        Assert.That(FirstDeaths(run).Select(d => d.Item1), theyDie ? Is.EquivalentTo(stackers) : Is.Empty, run.ToString);
    }

    // R1 holds the flare at (0, -18.5); the first melee waits 13.5 y from it, the rest stack in the south.
    [Test]
    public void AFlareSparesItsTargetAndKillsThoseTooNearIt()
    {
        var spots = Everyone(new Vector2(0f, 13f));
        spots[PhysRangedDps] = new Vector2(0f, -18.5f);
        spots[MeleeDpsA] = new Vector2(0f, -5f);
        var run = Meteor(spots, (alphaOmega, world) =>
            world.Events.Add(2f, () => alphaOmega.CastFromHelper(Actions.CosmoMeteorFlare, world.Party.Get(PhysRangedDps))));

        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (MeleeDpsA, (uint?)P6ActionId.CosmoMeteorFlare) }), run.ToString);
    }

    [Test]
    public void TheAddsEnrageWipesTheParty()
    {
        var run = Meteor(SpreadOut, (alphaOmega, world) => world.Events.Add(2f, () => alphaOmega.CastFromHelper(Actions.CosmoMeteorEnrage)));

        Assert.That(FirstDeaths(run).Select(d => d.Item1), Is.EquivalentTo(Clockwise), run.ToString);
    }

    private static void Spreads(TopP6AlphaOmega alphaOmega, SimWorld world)
    {
        world.Events.Add(2f, () => SpreadOn(alphaOmega, world, MainTank, OffTank, RegenHealer, ShieldHealer));
        world.Events.Add(3f, () => SpreadOn(alphaOmega, world, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps));
    }

    private static void SpreadOn(TopP6AlphaOmega alphaOmega, SimWorld world, params PartyRole[] targets)
    {
        foreach (var target in targets)
            alphaOmega.CastFromHelper(Actions.CosmoMeteorSpread, world.Party.Get(target));
    }

    // Everyone on their spot at 1.0; `schedule` runs as the run starts.
    private static TimelineRunResult Meteor(IReadOnlyDictionary<PartyRole, Vector2> spots, Action<TopP6AlphaOmega, SimWorld> schedule)
        => TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(1f, () => TimelineRun.Place(world, spots));
            schedule(alphaOmega, world);
        });

    private static IEnumerable<(PartyRole, uint?)> FirstDeaths(TimelineRunResult run)
        => run.Deaths.Where(d => d.Time <= run.Deaths[0].Time + 1e-3f).Select(d => (d.Role, d.ActionId));

    private static Dictionary<PartyRole, Vector2> Everyone(Vector2 spot) => Clockwise.ToDictionary(r => r, _ => spot);

    private static Dictionary<PartyRole, Vector2> Moved(IReadOnlyDictionary<PartyRole, Vector2> spots, params (PartyRole Role, Vector2 To)[] moves)
    {
        var moved = spots.ToDictionary();
        foreach (var (role, to) in moves) moved[role] = to;
        return moved;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
}
