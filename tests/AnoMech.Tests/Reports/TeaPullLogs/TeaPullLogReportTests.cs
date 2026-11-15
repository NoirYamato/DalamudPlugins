using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaPullLogReportTests
{
    [Test]
    public void WithNoPullNothingIsSettled()
    {
        var findings = TeaPullLogReport.Measures().SelectMany(m => m.Findings()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(findings.Select(f => f.Verdict), Is.All.EqualTo(Verdict.NotInTheseLogs));
            Assert.That(findings.Select(f => f.Measured), Has.None.Contains("NaN"));
        });
    }

    private const uint Orb = 0x40000071, BruteJustice = 0x40000031;

    // Pulls that give a measure one sample where it wants several, or one half of a pair: a wipe to the Living Liquid's
    // first auto-attack; one in which only the Living Liquid ever walks or attacks; and one whose Embolus is seen on two
    // move lines half a second apart and bursts with nobody left alive, and whose J Kick comes from an actor the log
    // places nowhere.
    private static TeaPull[] PullsThatShowLittle() =>
    [
        Of([Hit(3, Liquid, TeaLogIds.LiquidAutoAttack, Player(1), new Vector2(100f, 90f), new Vector2(100f, 85f))], ends: 5),
        Of(
        [
            .. Enumerable.Range(0, 6).Select(k => Moved(10 + 0.3 * k, Liquid, new Vector2(100f, 85f + 2.7f * k), 0f)),
            StatusList(11, Player(1), 40000, new Vector2(100f, 104f)),
            Hit(13, Liquid, TeaLogIds.LiquidAutoAttack, Player(1), new Vector2(100f, 104f), new Vector2(100f, 98.5f)),
        ], ends: 40),
        Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, new Vector2(100f, 110f), 0f, 1300000, LiquidMaxHp), .. Tornadoes(19.6),
            Hit(40, RageNorth, P1ActionId.Pressurize, RageNorth), Hit(40, RageWest, P1ActionId.Pressurize, RageWest),
            Enemy(40.4, Orb, P1BNpcBaseId.Embolus, new Vector2(100f, 85f), 0f, 3853782, 3853782),
            Moved(45.6, Orb, new Vector2(100f, 85.2f), 0f), Moved(46.1, Orb, new Vector2(100f, 86.2f), 0f),
            UpdateHp(46.5, Player(1), 0, Centre),
            Hit(47, Orb, P1ActionId.Outburst, Player(1), aoe: true),
            AbilityAt(128, Helper, P1ActionId.HawkBlaster, new Vector2(100f, 86f), 0f),
            Hit(154, BruteJustice, P1ActionId.JKick, Player(1), aoe: true),
        ], [LiquidStands(x: 100f, y: 100f)], players: 1),
    ];

    [Test]
    public void APullThatShowsLittlePrintsNoNumberItDoesNotHave()
    {
        var measures = TeaPullLogReport.Measures();
        foreach (var pull in PullsThatShowLittle())
        foreach (var measure in measures)
            measure.Observe(pull);

        var findings = measures.SelectMany(m => m.Findings()).ToList();

        Assert.Multiple(() =>
        {
            foreach (var finding in findings)
            {
                Assert.That(finding.Measured, Does.Not.Contain("NaN"), finding.Name);
                Assert.That(finding.Details, Has.None.Contains("NaN"), finding.Name);
            }
        });
    }

    // Each finding finds its line in the plugin's source by text: a sim edit that rewords or drops that text fails here,
    // naming the finding, rather than leaving the report to print "file:?".
    [Test]
    public void EveryFindingFindsItsLineInTheSource()
    {
        var findings = TeaPullLogReport.Measures().SelectMany(m => m.Findings()).ToList();

        Assert.Multiple(() =>
        {
            foreach (var finding in findings)
            {
                Assert.That(finding.Constant, Does.Not.Contain(":?"), finding.Name);
                Assert.That(finding.Sim, Does.Not.Contain("not found in the"), finding.Name);
            }
        });
    }
}
