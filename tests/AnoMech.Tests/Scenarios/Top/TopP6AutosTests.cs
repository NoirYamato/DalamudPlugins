using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// Autos at 2.0: Alpha Omega's visual (31747) at 2.0 and 5.2, Flash Gale (32223, r 5) 0.9 s after each, at 2.9 and 6.1,
// on the main tank and on the player farthest from Alpha Omega. Unless a test moves someone, everyone stands where
// LPDU puts them: the main tank inside north (0, -5), the off-tank outside east (14.5, 0), the rest inside south (0, 6).
public class TopP6AutosTests
{
    private static readonly Dictionary<PartyRole, Vector2> ForTheAutos = new()
    {
        [MainTank] = new(0f, -5f),
        [OffTank] = new(14.5f, 0f),
        [RegenHealer] = new(0f, 6f),
        [ShieldHealer] = new(0f, 6f),
        [MeleeDpsA] = new(0f, 6f),
        [MeleeDpsB] = new(0f, 6f),
        [PhysRangedDps] = new(0f, 6f),
        [CasterDps] = new(0f, 6f),
    };

    [Test]
    public void TheMainTankAndTheFarthestTakeEachAuto()
    {
        var seats = new Dictionary<string, PartyRole>();
        var run = Autos(seats);
        var gales = run.Sent("ability", ActionId.Unknown7ddf);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Sent("ability", ActionId.AlphaOmegaAutoAttack).Select(a => a.Time), Is.EqualTo(new[] { 2f, 5.2f }).Within(TimelineRun.FrameLate));
        Assert.That(gales.Select(g => g.Time), Is.EqualTo(new[] { 2.9f, 2.9f, 6.1f, 6.1f }).Within(TimelineRun.FrameLate));
        Assert.That(gales.Select(g => seats[g.Target]), Is.EqualTo(new[] { MainTank, OffTank, MainTank, OffTank }));
    }

    [Test]
    public void ANonTankFarthestDies()
    {
        var run = Autos((1.5f, RegenHealer, new Vector2(0f, 18f)));

        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (RegenHealer, TopP6AlphaOmega.FlashGaleOnANonTank) }), run.ToString);
    }

    // Its row in the sheet has no name, so the death names it.
    [Test]
    public void EachDeathNamesFlashGale()
    {
        var run = Autos((1.5f, RegenHealer, new Vector2(0f, 18f)), (1.5f, MeleeDpsA, new Vector2(2f, -5f)));

        Assert.That(run.Deaths, Has.Count.EqualTo(2), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Has.All.StartsWith("Flash Gale ("), run.ToString);
    }

    [Test]
    public void StandingInTheMainTanksFlashGaleKills()
    {
        var run = Autos((1.5f, MeleeDpsA, new Vector2(2f, -5f)));

        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (MeleeDpsA, TopP6AlphaOmega.InSomeoneElsesFlashGale) }), run.ToString);
    }

    [Test]
    public void StandingInTheFarthestsFlashGaleKills()
    {
        var run = Autos((1.5f, PhysRangedDps, new Vector2(12f, 0f)));

        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (PhysRangedDps, TopP6AlphaOmega.InSomeoneElsesFlashGale) }), run.ToString);
    }

    // The off-tank, farthest at r 9, stands 4 y from the main tank: each tank is in the other's.
    [Test]
    public void TanksStandingTogetherBothDie()
    {
        var run = Autos((1.5f, OffTank, new Vector2(0f, -9f)));

        Assert.That(FirstDeaths(run), Is.EquivalentTo(new[]
        {
            (MainTank, TopP6AlphaOmega.InSomeoneElsesFlashGale),
            (OffTank, TopP6AlphaOmega.InSomeoneElsesFlashGale),
        }), run.ToString);
    }

    // The main tank is also the farthest, so takes both and nobody else is hit.
    [Test]
    public void TheMainTankMayTakeBoth()
    {
        var seats = new Dictionary<string, PartyRole>();
        var run = Autos(seats, (1.5f, MainTank, new Vector2(0f, -15f)), (1.5f, OffTank, new Vector2(3f, 6f)));

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Sent("ability", ActionId.Unknown7ddf).Select(g => seats[g.Target]), Has.All.EqualTo(MainTank));
    }

    // The healer is farthest until just before the hit.
    [Test]
    public void TheFarthestIsTakenAsTheHitLands()
    {
        var run = Autos((1.5f, RegenHealer, new Vector2(0f, 18f)), (2.85f, RegenHealer, new Vector2(0f, 6f)));

        Assert.That(run.Deaths, Is.Empty, run.ToString);
    }

    // Everyone on their spot at 1.0, then the moves, on the run's clock.
    private static TimelineRunResult Autos(params (float At, PartyRole Role, Vector2 To)[] moves) => Autos(new Dictionary<string, PartyRole>(), moves);

    private static TimelineRunResult Autos(Dictionary<string, PartyRole> seats, params (float At, PartyRole Role, Vector2 To)[] moves)
        => TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(1f, () =>
            {
                TimelineRun.Place(world, ForTheAutos);
                foreach (var (id, role) in TimelineRun.Seats(world)) seats[id] = role;
            });
            foreach (var (at, role, to) in moves)
                world.Events.Add(at, () => TimelineRun.Place(world, role, to.X, to.Y));
            alphaOmega.Autos(2f);
        });

    private static IEnumerable<(PartyRole, string)> FirstDeaths(TimelineRunResult run)
        => run.Deaths.Where(d => d.Time <= run.Deaths[0].Time + 1e-3f)
                     .Select(d => (d.Role, d.Cause[(d.Cause.IndexOf('(') + 1)..^1]));
}
