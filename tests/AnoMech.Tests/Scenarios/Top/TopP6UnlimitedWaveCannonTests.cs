using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// Unlimited Wave Cannon cast at 1.9, as both blocks that play it start. Its four exaflares cast 31661 (12 s, r 8, its
// bar ending at 11.7) at 1.9, 2.9, 3.9 and 4.9, from r 24 on neighbouring compass points the way they turn, facing the
// centre; then each explodes again every step (1.1 s, or 1.0 s), 8 y further, through the centre to r 24 on the far
// side. The first exaflare's first explosion, t0, is at 13.9. Puddles (31663, 3 s, r 6) drop under everyone at 11.9,
// 13.7, 15.6, 17.5, 19.4 and 21.4, each hitting 2.7 s later.
public class TopP6UnlimitedWaveCannonTests
{
    private const float CastAt = 1.9f;
    private static readonly float[] Steps = [1.1f, 1.0f];
    private static readonly int[] Points = Enumerable.Range(0, 8).ToArray();

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void TheExaflaresStartOnTheRolledPointAndOnNeighbouringPointsTheWayTheyTurn(
        [ValueSource(nameof(Points))] int first, [Values] bool clockwise)
    {
        var wave = new TopP6UnlimitedWaveCannon(first, clockwise, CastAt);
        var turn = clockwise ? 45 : -45;

        Assert.That(Enumerable.Range(0, 4).Select(wave.Bearing),
                    Is.EqualTo(Enumerable.Range(0, 4).Select(k => ((45 * first + turn * k) % 360 + 360) % 360)));
    }

    [Test]
    public void EachExaflareRollsFromTheRimThroughTheCentreToTheFarRim(
        [ValueSource(nameof(Points))] int first, [Values] bool clockwise)
    {
        var wave = new TopP6UnlimitedWaveCannon(first, clockwise, CastAt);

        Assert.Multiple(() =>
        {
            foreach (var explosion in wave.Explosions)
            {
                var expected = TopP6AlphaOmega.AtBearing(wave.Bearing(explosion.Exaflare), 24f - 8f * explosion.Step);
                Assert.That(Vector3.Distance(explosion.Position, expected), Is.LessThan(1e-3f), $"exaflare {explosion.Exaflare} step {explosion.Step}");
            }
        });
        Assert.That(wave.Explosions, Has.Count.EqualTo(28));
    }

    [Test]
    public void EachExaflareGoesOffASecondAfterTheLastThenEveryStep([ValueSource(nameof(Steps))] float step)
    {
        var wave = new TopP6UnlimitedWaveCannon(0, true, CastAt, step);

        Assert.Multiple(() =>
        {
            foreach (var explosion in wave.Explosions)
            {
                var expected = explosion.Step == 0
                    ? CastAt + explosion.Exaflare + 11.7f
                    : CastAt + explosion.Exaflare + 12f + step * explosion.Step;
                Assert.That(explosion.HitAt, Is.EqualTo(expected).Within(1e-3f), $"exaflare {explosion.Exaflare} step {explosion.Step}");
            }
        });
        Assert.That(wave.FirstExplosionAt, Is.EqualTo(13.9f).Within(1e-3f));
    }

    // At r 14, bearings counted from the first exaflare the way they turn, times after t0: the dodge's table.
    [TestCase(1.1f, -45, 7.4f, 8.5f)]
    [TestCase(1.1f, 0, 1.1f, 2.2f)]
    [TestCase(1.1f, 45, 2.1f, 3.2f)]
    [TestCase(1.1f, 90, 3.1f, 4.2f)]
    [TestCase(1.1f, 135, 4.1f, 5.2f)]
    [TestCase(1.1f, 180, 4.4f, 5.5f)]
    [TestCase(1.1f, 225, 5.4f, 6.5f)]
    [TestCase(1.1f, 270, 6.4f, 7.5f)]
    [TestCase(1.0f, -45, 7f, 8f)]
    [TestCase(1.0f, 0, 1f, 2f)]
    [TestCase(1.0f, 45, 2f, 3f)]
    [TestCase(1.0f, 90, 3f, 4f)]
    [TestCase(1.0f, 135, 4f, 5f)]
    [TestCase(1.0f, 180, 4f, 5f)]
    [TestCase(1.0f, 225, 5f, 6f)]
    [TestCase(1.0f, 270, 6f, 7f)]
    public void EachCompassPointAtR14IsHitWhenTheDodgeExpects(float step, int fromFirst, float firstHit, float secondHit)
    {
        Assert.Multiple(() =>
        {
            foreach (var (first, clockwise) in new[] { (0, true), (3, false), (6, true), (7, false) })
            {
                var wave = new TopP6UnlimitedWaveCannon(first, clockwise, CastAt, step);
                var spot = TopP6AlphaOmega.AtBearing(wave.Bearing(0) + wave.Turn * fromFirst, 14f);
                var hits = wave.Explosions
                    .Where(e => Vector3.Distance(e.Position, spot) <= TopP6UnlimitedWaveCannon.ExplosionRadius)
                    .Select(e => e.HitAt - wave.FirstExplosionAt);
                Assert.That(hits, Is.EqualTo(new[] { firstHit, secondHit }).Within(1e-3f), $"first {first}, clockwise {clockwise}");
            }
        });
    }

    [Test]
    public void SixPuddleSetsDropOnTheTimelineAndHitAsTheirBarsEnd()
    {
        var sets = new TopP6UnlimitedWaveCannon(0, true, CastAt).PuddleSets;

        Assert.That(sets.Select(s => s.DropAt), Is.EqualTo(new[] { 11.9f, 13.7f, 15.6f, 17.5f, 19.4f, 21.4f }).Within(1e-3f));
        Assert.That(sets.Select(s => s.HitAt - s.DropAt), Has.All.EqualTo(2.7f).Within(1e-3f));
        Assert.That(TopP6UnlimitedWaveCannon.PuddleRadius, Is.EqualTo(6f));
        Assert.That(TopP6UnlimitedWaveCannon.ExplosionRadius, Is.EqualTo(8f));
    }

    // Godmode, everyone at the centre: the run's casts are the ones the wave says, where and when it says.
    [Test]
    public void EveryExplosionIsCastWhereAndWhenTheWaveSays(
        [ValueSource(nameof(Points))] int first, [Values] bool clockwise, [ValueSource(nameof(Steps))] float step)
    {
        var wave = new TopP6UnlimitedWaveCannon(first, clockwise, CastAt, step);
        var run = Play(wave, new TimelineRunOptions { GodMode = true }, Everyone(Vector2.Zero));
        var casts = run.Sent("cast", ActionId.WaveCannon_7BAD);
        var explosions = run.Sent("ability", ActionId.WaveCannon_7BAD, ActionId.WaveCannon_7BAE);
        var goesOff = wave.Explosions.Select(e => e.Step == 0 ? e.HitAt + 0.3f : e.HitAt).Order();

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(casts.Select(c => c.Time), Is.EqualTo(new[] { 1.9f, 2.9f, 3.9f, 4.9f }).Within(TimelineRun.FrameLate));
        Assert.That(casts.Select(c => c.Position), Is.EqualTo(Enumerable.Range(0, 4).Select(k => wave.Lane(k).Start.Position)).Using<Vector3>(Near));
        Assert.That(casts.Select(c => new Vector2(MathF.Sin(c.Rotation), MathF.Cos(c.Rotation))),
                    Is.EqualTo(Enumerable.Range(0, 4).Select(k => -Flat(wave.Lane(k).Start.Position) / 24f)).Using<Vector2>((a, b) => Vector2.Distance(a, b) < 0.01f));
        Assert.That(explosions, Has.Count.EqualTo(28));
        Assert.That(explosions.Select(e => e.Time).Order(), Is.EqualTo(goesOff).Within(TimelineRun.FrameLate));
        Assert.That(explosions.Select(e => e.Position), Is.EquivalentTo(wave.Explosions.Select(e => e.Position)).Using<Vector3>(Near));
    }

    [Test]
    public void EachPuddleSetDropsUnderEveryoneWhereTheyStand()
    {
        var wave = new TopP6UnlimitedWaveCannon(0, true, CastAt);
        var run = Play(wave, new TimelineRunOptions { GodMode = true }, Spread);
        var puddles = run.Sent("cast", ActionId.WaveCannon_7BAF);

        Assert.That(puddles.GroupBy(p => MathF.Round(p.Time, 1)).Select(set => set.Key),
                    Is.EqualTo(wave.PuddleSets.Select(s => MathF.Round(s.DropAt, 1))));
        Assert.That(puddles.GroupBy(p => MathF.Round(p.Time, 1)).Select(set => set.Count()), Has.All.EqualTo(8));
        Assert.That(puddles.Select(p => p.Position), Has.All.Matches<Vector3>(p => Spread.Values.Any(s => Vector2.Distance(Flat(p), s) < 0.01f)));
    }

    // The main tank stays where set 1 dropped; everyone else steps out to r 7, opposite the first exaflare, just before
    // it hits. Nothing else reaches either spot before the main tank's death shows.
    [Test]
    public void StandingOnYourPuddleAsItHitsKills()
    {
        var wave = new TopP6UnlimitedWaveCannon(0, true, CastAt);
        var set = wave.PuddleSets[0];
        var run = Play(wave, new TimelineRunOptions(), Everyone(Vector2.Zero),
            (set.HitAt - 0.2f, Everyone(new Vector2(0f, 7f), (MainTank, Vector2.Zero))));

        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (MainTank, (uint?)ActionId.WaveCannon_7BAF) }), run.ToString);
    }

    // The main tank waits on the rim where the first exaflare goes off; everyone else at the centre, whose own
    // puddles hit later.
    [Test]
    public void StandingWhereAnExaflareGoesOffKills()
    {
        var wave = new TopP6UnlimitedWaveCannon(2, false, CastAt);
        var rim = TopP6AlphaOmega.AtBearing(wave.Bearing(0), 18f);
        var run = Play(wave, new TimelineRunOptions(), Everyone(Vector2.Zero, (MainTank, Flat(rim))));

        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (MainTank, (uint?)ActionId.WaveCannon_7BAD) }), run.ToString);
    }

    // Just before set 1 hits, everyone steps off their puddle: the main tank 7 y to the side of where the first
    // exaflare's second explosion (r 16, at 15.0) lands, the rest to (5, 12), which no exaflare reaches until 20.3.
    [Test]
    public void StandingWhereALaterExplosionGoesOffKills()
    {
        var wave = new TopP6UnlimitedWaveCannon(5, true, CastAt);
        var outward = TopP6AlphaOmega.AtBearing(wave.Bearing(0), 1f);
        var beside = TopP6AlphaOmega.AtBearing(wave.Bearing(0), 16f) + new Vector3(-outward.Z, 0f, outward.X) * 7f;
        var run = Play(wave, new TimelineRunOptions(), Everyone(Vector2.Zero),
            (wave.PuddleSets[0].HitAt - 0.2f, Everyone(new Vector2(5f, 12f), (MainTank, Flat(beside)))));

        Assert.That(beside.Length(), Is.LessThan(19f));
        Assert.That(FirstDeaths(run), Is.EqualTo(new[] { (MainTank, (uint?)ActionId.WaveCannon_7BAE) }), run.ToString);
    }

    // Everyone takes their spot at 1.0, once spawned; `moves` are (time, spots) on the run's clock.
    private static TimelineRunResult Play(TopP6UnlimitedWaveCannon wave, TimelineRunOptions options, IReadOnlyDictionary<PartyRole, Vector2> spots,
        params (float At, IReadOnlyDictionary<PartyRole, Vector2> Spots)[] moves)
        => TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(1f, () => TimelineRun.Place(world, spots));
            foreach (var (at, moved) in moves)
                world.Events.Add(at, () => TimelineRun.Place(world, moved));
            alphaOmega.UnlimitedWaveCannon(wave);
        }, options);

    private static IEnumerable<(PartyRole, uint?)> FirstDeaths(TimelineRunResult run)
        => run.Deaths.Where(d => d.Time <= run.Deaths[0].Time + 1e-3f).Select(d => (d.Role, d.ActionId));

    // Everyone on `spot`, but for the exceptions.
    private static Dictionary<PartyRole, Vector2> Everyone(Vector2 spot, params (PartyRole Role, Vector2 Spot)[] but)
        => Enum.GetValues<PartyRole>().ToDictionary(r => r, r => but.Where(b => b.Role == r).Select(b => b.Spot).DefaultIfEmpty(spot).First());

    private static readonly Dictionary<PartyRole, Vector2> Spread = Enum.GetValues<PartyRole>()
        .ToDictionary(r => r, r => Flat(TopP6AlphaOmega.AtBearing(45f * (int)r, 10f)));

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private static bool Near(Vector3 a, Vector3 b) => Vector2.Distance(Flat(a), Flat(b)) < 0.01f;
}
