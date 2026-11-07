using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6WaveCannon2;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// Cosmo Arrow 1 is Cosmo Arrow 2's mechanic, so TopP6Exasquares casts exactly what upstream's Exasquares/WC2 casts for
// its lines: every cast and ActionEffect of 31651 (the 10 y lines), 32337 (Inhale) and 31652 (the rolling 5 y
// lines), from the same spot, facing the same way, on the same frame. Both cast Cosmo Arrow at 1.90.
//
//    9.60 / 11.61  the two sets hit (In first: the cross |c| < 5, then the outer lines 10 < |c| < 20)
//   11.91 on, every 2 s, rolling lines |c - line| < 2.5:
//         In first  ±7.5 / ±7.5 ±12.5 / ±2.5 ±17.5 / ±2.5 / ±7.5 / ±12.5 / ±17.5
//         Out first ±7.5 / ±2.5 ±7.5 / ±2.5 ±12.5 / ±7.5 ±17.5 / ±12.5 / ±17.5
public class TopP6ExasquaresTests
{
    private const float CastAt = 1.9f;
    private static readonly uint[] LineActions = [ActionId.CosmoArrowOmen, ActionId.Inhale, ActionId.CosmoArrowDamage];

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    // Either way: the two sets' 12 casts and 12 ActionEffects, and 36 rolling lines.
    [TestCase(true)]
    [TestCase(false)]
    public void EveryLineIsUpstreamsLine(bool inFirst)
    {
        var upstream = UpstreamsLines(inFirst);
        var ours = OurLines(inFirst);

        Assert.That(upstream, Has.Count.EqualTo(60));
        Assert.That(ours, Is.EqualTo(upstream));
    }

    [Test]
    public void InFirstTheCrossLandsFirstAndRollsOutSevenTimes()
    {
        var waves = new TopP6Exasquares(true, CastAt).Waves;

        Assert.That(waves.Select(w => w.HitAt), Is.EqualTo(new[] { 9.6f, 11.61f, 11.91f, 13.91f, 15.91f, 17.91f, 19.91f, 21.91f, 23.91f }).Within(1e-3f));
        Assert.That(waves.Select(w => w.HalfWidth), Is.EqualTo(new[] { 5f, 5f, 2.5f, 2.5f, 2.5f, 2.5f, 2.5f, 2.5f, 2.5f }));
        Assert.That(waves.Select(w => Lines(w.Lines)), Is.EqualTo(new[]
        {
            "0", "-15 15", "-7.5 7.5", "-12.5 -7.5 7.5 12.5", "-17.5 -2.5 2.5 17.5", "-2.5 2.5", "-7.5 7.5", "-12.5 12.5", "-17.5 17.5",
        }));
    }

    [Test]
    public void OutFirstTheOuterLinesLandFirstAndRollOnSixTimes()
    {
        var waves = new TopP6Exasquares(false, CastAt).Waves;

        Assert.That(waves.Select(w => w.HitAt), Is.EqualTo(new[] { 9.6f, 11.61f, 11.91f, 13.91f, 15.91f, 17.91f, 19.91f, 21.91f }).Within(1e-3f));
        Assert.That(waves.Select(w => Lines(w.Lines)), Is.EqualTo(new[]
        {
            "-15 15", "0", "-7.5 7.5", "-7.5 -2.5 2.5 7.5", "-12.5 -2.5 2.5 12.5", "-17.5 -7.5 7.5 17.5", "-12.5 12.5", "-17.5 17.5",
        }));
    }

    // Nobody moves and nobody dies (godmode), so every ActionEffect lists everyone standing in its line.
    [TestCase(true)]
    [TestCase(false)]
    public void TheWavesHitExactlyWhoTheLinesHit(bool inFirst)
    {
        var seats = new Dictionary<string, PartyRole>();
        var run = TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(0.5f, () =>
            {
                TimelineRun.Place(world, Spots);
                foreach (var (id, role) in TimelineRun.Seats(world)) seats[id] = role;
            });
            alphaOmega.CosmoArrow(new TopP6Exasquares(inFirst, CastAt));
        }, new TimelineRunOptions { GodMode = true });
        var waves = new TopP6Exasquares(inFirst, CastAt).Waves;
        var hitsBySet = run.Sent("ability", ActionId.CosmoArrowOmen).GroupBy(a => MathF.Round(a.Time, 1));
        var hitsByRoll = run.Sent("ability", ActionId.CosmoArrowDamage).GroupBy(a => MathF.Round(a.Time, 1));
        var actual = hitsBySet.Concat(hitsByRoll)
            .Select(wave => wave.SelectMany(a => a.Hits).Select(id => seats[id]).Distinct().OrderBy(r => r).ToList())
            .ToList();
        var expected = waves
            .Select(wave => Spots.Where(s => wave.Hits(new Vector3(s.Value.X, 0f, s.Value.Y))).Select(s => s.Key).OrderBy(r => r).ToList())
            .ToList();

        Assert.That(actual, Is.EqualTo(expected), run.ToString);
        Assert.That(expected.Sum(roles => roles.Count), Is.GreaterThan(12));
    }

    // Out first the outer lines land first; the others wait in the centre square, which they leave clear until 11.61.
    [Test]
    public void StandingInALineAsItLandsKills()
    {
        var run = TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(0.5f, () =>
            {
                foreach (var role in Enum.GetValues<PartyRole>()) TimelineRun.Place(world, role, 1f, 1f);
                TimelineRun.Place(world, MainTank, 8f, 15f);
            });
            alphaOmega.CosmoArrow(new TopP6Exasquares(false, CastAt));
        });

        var first = run.Deaths.Where(d => d.Time <= run.Deaths[0].Time).Select(d => (d.Role, d.ActionId));
        Assert.That(first, Is.EqualTo(new[] { (MainTank, (uint?)ActionId.CosmoArrowOmen) }), run.ToString);
    }

    // Spread over the band cells: the cross, the rolling bands, the outer lines and the corners, none on a band's edge.
    private static readonly Dictionary<PartyRole, Vector2> Spots = new()
    {
        [MainTank] = new(0.5f, -0.5f),
        [OffTank] = new(7.5f, 3f),
        [RegenHealer] = new(3f, 12.5f),
        [ShieldHealer] = new(12f, 12f),
        [MeleeDpsA] = new(17f, 6f),
        [MeleeDpsB] = new(-8f, -8f),
        [PhysRangedDps] = new(2.5f, 17.5f),
        [CasterDps] = new(-14.5f, 2f),
    };

    private static string Lines(IEnumerable<float> lines) => string.Join(" ", lines.OrderBy(l => l).Select(l => l.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    // Each cast or ActionEffect of a line as its row has it: frame, kind, action, world spot and facing.
    private static List<string> LineRows(IEnumerable<Dictionary<string, string>> rows)
        => rows.Where(row => row["kind"] is "cast" or "ability" && LineActions.Any(id => row["id"] == id.ToString()))
               .Select(row => $"{row["t"]} {row["kind"]} {row["id"]} ({row["x"]}, {row["z"]}) {row["rot"]}")
               .Order()
               .ToList();

    private static List<string> UpstreamsLines(bool inFirst)
    {
        IReadOnlyList<Dictionary<string, string>> rows = [];
        var run = ScenarioRun.Execute(typeof(TopP6WaveCannon2Scenario), 0, 1, new ScenarioRunOptions
        {
            Solo = true,
            GodMode = true,
            StopAt = 25f,
            Overrides = o => ((TopP6WaveCannon2StateOverrides)o).InFirst = inFirst,
            Probe = p => rows = p.Fake.Timeline.Rows,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return LineRows(rows);
    }

    private static List<string> OurLines(bool inFirst)
    {
        var run = TimelineRun.Execute(world =>
        {
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            alphaOmega.CosmoArrow(new TopP6Exasquares(inFirst, CastAt));
        }, new TimelineRunOptions { GodMode = true });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return LineRows(run.Rows);
    }
}
