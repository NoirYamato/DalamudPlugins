using System.Numerics;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Tea;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

// A made-up TEA pull for the report's tests: the Living Liquid stands 15 y north before it, and the players are named
// by seat, a tank first.
internal static class TeaTestPull
{
    public const uint Liquid = 0x40000010, Hand = 0x40000011, Helper = 0x40000020, OtherHelper = 0x40000021, Chaser = 0x40000030;
    public const uint RageNorth = 0x40000041, RageWest = 0x40000042, RageSouth = 0x40000043;
    public const int LiquidMaxHp = 3356051;

    private static readonly JobId[] Seats =
        [JobId.Gunbreaker, JobId.DarkKnight, JobId.WhiteMage, JobId.Scholar, JobId.Samurai, JobId.Monk, JobId.Dancer, JobId.BlackMage];

    public static readonly Vector2 Centre = new(100f, 100f);

    public static uint Player(int seat) => 0x10000000u + (uint)seat;

    public static string LiquidStands(int hp = LiquidMaxHp, float x = 100f, float y = 85f)
        => Enemy(-10, Liquid, P1BNpcBaseId.LivingLiquid, new Vector2(x, y), 0f, hp, LiquidMaxHp);

    public static string HelperStands(uint helper = Helper) => Enemy(-10, helper, P1BNpcBaseId.Helper, new Vector2(97.4f, 108.37f), 0f, 44, 44);

    // Three Liquid Rages 15 y out to the north, west and south: the east quarter is open.
    public static string[] Tornadoes(double seconds) =>
    [
        Enemy(seconds, RageNorth, P1BNpcBaseId.LiquidRage, new Vector2(100f, 85f), 0f, 3103771, 3103771),
        Enemy(seconds, RageWest, P1BNpcBaseId.LiquidRage, new Vector2(85f, 100f), 0f, 3103771, 3103771),
        Enemy(seconds, RageSouth, P1BNpcBaseId.LiquidRage, new Vector2(100f, 115f), 0f, 3103771, 3103771),
    ];

    // The raw rotation an ability line carries for a heading.
    public static ushort Raw(double heading) => (ushort)Math.Round((heading + Math.PI) / (2 * Math.PI) * 65535);

    public static TeaPull Of(IEnumerable<string> lines, IEnumerable<string>? standing = null, int players = 8, double? ends = 200, uint? self = null)
    {
        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin, End = ends is { } end ? Origin.AddSeconds(end) : null, Self = self,
            Jobs = Enumerable.Range(1, players).ToDictionary(Player, seat => (uint)Seats[seat - 1]),
            Standing = (standing ?? [LiquidStands()]).Select(l => ActLine.Parse(l)!).ToList(),
        };
        pull.Lines.AddRange(lines.Select(l => ActLine.Parse(l)!));
        return new TeaPull(pull);
    }

    public static List<LogFinding> Findings(ITeaPullLogMeasures measures, params TeaPull[] pulls)
    {
        foreach (var pull in pulls) measures.Observe(pull);
        return measures.Findings().ToList();
    }

    public static LogFinding Named(this IEnumerable<LogFinding> findings, string start)
        => findings.Single(f => f.Name.StartsWith(start, StringComparison.Ordinal));
}

public class TeaPullTests
{
    [TestCase(LiquidMaxHp, true)]
    [TestCase(LiquidMaxHp - 20000, false)]
    public void APullIsLoggedFromItsStartWhenTheLiquidStoodAtFullHealth(int hp, bool fromItsStart)
        => Assert.That(Of([], [LiquidStands(hp)]).LoggedFromItsStart, Is.EqualTo(fromItsStart));

    [Test]
    public void ThePullsOwnLiquidIsTheLastToJoin()
    {
        var pull = Of([], [Enemy(-300, 0x40000001, P1BNpcBaseId.LivingLiquid, new Vector2(88f, 101f), 0f, 5000, LiquidMaxHp), LiquidStands()]);

        Assert.That(pull.Liquid, Is.EqualTo(Liquid));
    }

    [Test]
    public void OnlyWhatJoinedWithTheLiquidWasSetUpForThePull()
    {
        var pull = Of([], [Enemy(-300, 0x40000002, P1BNpcBaseId.Helper, Centre, 0f, 44, 44), LiquidStands(), HelperStands(), HelperStands(OtherHelper)]);

        Assert.That(pull.SetUpWithTheLiquid(P1BNpcBaseId.Helper).Select(c => c.Actor), Is.EquivalentTo(new[] { Helper, OtherHelper }));
    }

    [Test]
    public void AnEnemyIsTurnedToWhomeverItLastTurnedToOrStruck()
    {
        var pull = Of(
        [
            Hit(5, Liquid, TeaLogIds.LiquidAutoAttack, Player(1)), Memory(20, "Change", Liquid, ("NPCTargetID", Hex(Player(2)))),
            Hit(30, Liquid, TeaLogIds.LiquidAutoAttack, Player(1)), Memory(40, "Change", Liquid, ("NPCTargetID", "E0000000")),
        ]);

        Assert.That(new[] { 1, 10, 25, 35, 45 }.Select(s => pull.TargetOf(Liquid, Origin.AddSeconds(s))),
                    Is.EqualTo(new uint?[] { null, Player(1), Player(2), Player(1), null }));
    }

    [Test]
    public void ThreeTornadoesNameTheQuarterTheyLeaveOpen()
    {
        var set = Of(Tornadoes(20)).TornadoSets.Single();

        Assert.Multiple(() =>
        {
            Assert.That(set.OpenBearing, Is.EqualTo(90));
            Assert.That(new[] { new Vector2(100f, 85f), new Vector2(85f, 100f), new Vector2(100f, 115f) }.Select(set.Which),
                        Is.EqualTo(new TeaP1Tornado?[] { TeaP1Tornado.West, TeaP1Tornado.Middle, TeaP1Tornado.East }));
        });
    }

    // A fairy's ids sit among the enemies', and the plugin's own runs log nothing else acting.
    [Test]
    public void APetsAbilityIsNoneOfTheFights()
    {
        const uint fairy = 0x40000099;
        var fairyOnly = Of([Enemy(1, fairy, 1008, Centre, 0f, 27000, 27000), Hit(4, fairy, 0x322, Player(3))]);
        var theFight = Of([Hit(4, Liquid, TeaLogIds.LiquidAutoAttack, Player(1))]);

        Assert.That((fairyOnly.TheFightActed, theFight.TheFightActed), Is.EqualTo((false, true)));
    }

    [Test]
    public void AHitOnAPlayerJustRaisedShowsNothingOfItsDamage()
    {
        var pull = Of([Gains(100, 418, 5f, Player(8), Player(8)), Hit(101, Helper, P1ActionId.HawkBlaster, Player(8), sequence: 0x31)]);

        Assert.That(pull.Outcome(pull.Hits[0]), Is.EqualTo(HitOutcome.Invulnerable));
    }

    [Test]
    public void APlayerAnEarlierHitLeftAtZeroIsDying()
    {
        var pull = Of([Hit(74.17, Helper, P1ActionId.Sluice, Player(3), sequence: 0x40), EffectResult(74.9, Player(3), 0x40, 0, Centre)]);

        Assert.That(new[] { 74.1, 74.26, 80 }.Select(s => pull.IsDying(Player(3), Origin.AddSeconds(s))), Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void APullReachesItsFurthestMechanic()
    {
        var pull = Of([Cast(14.6, Liquid, P1ActionId.Cascade, 3.7f), Cast(66.1, Liquid, P1ActionId.ProteanWaveFromTheLiquid, 2.7f)]);

        Assert.That(pull.Reached, Is.EqualTo("Protean 1"));
    }

    [Test]
    public void AnAutoAttackIsNamedForWhatItIs()
        => Assert.That(Of([]).ActionName(TeaLogIds.HandAutoAttack), Is.EqualTo("auto-attack"));
}
