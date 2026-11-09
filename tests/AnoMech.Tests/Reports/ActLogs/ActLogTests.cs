using System.Numerics;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

public class ActLogTests
{
    private const uint Top = 1122, Elsewhere = 0x84;
    private const uint Player1 = 0x10000001, Player2 = 0x10000002, Boss = 0x40000010;

    [Test]
    public void APullRunsWhileTheGamesCombatFlagIsUp()
    {
        var pulls = Pulls(Zone(0, Top), Cast(5, Boss, 0x7B55, 4.7f), Combat(10, act: true, game: true), Cast(12, Boss, 0x7B55, 4.7f),
                          Combat(20, act: true, game: false), Cast(25, Boss, 0x7B55, 4.7f));

        Assert.That(pulls.Select(p => (p.Start, p.End, p.Lines.Count)),
                    Is.EqualTo(new[] { (Origin.AddSeconds(10), (DateTimeOffset?)Origin.AddSeconds(20), 1) }));
    }

    // ACT's own flag rides out a quick wipe and re-pull; the game's drops between them.
    [Test]
    public void AQuickRepullIsAPullOfItsOwn()
    {
        var pulls = Pulls(Zone(0, Top), Combat(10, true, true), Combat(20, true, false), Combat(25, true, true), Combat(40, false, false));

        Assert.That(pulls.Select(p => (p.Number, p.Start)), Is.EqualTo(new[] { (1, Origin.AddSeconds(10)), (2, Origin.AddSeconds(25)) }));
    }

    [Test]
    public void PullsInAnotherTerritoryAreLeftOut()
    {
        var pulls = Pulls(Zone(0, Elsewhere), Combat(10, true, true), Combat(20, true, false),
                          Zone(30, Top), Combat(40, true, true), Combat(50, true, false));

        Assert.That(pulls.Select(p => p.Start), Is.EqualTo(new[] { Origin.AddSeconds(40) }));
    }

    [Test]
    public void LeavingTheTerritoryEndsAPull()
        => Assert.That(Pulls(Zone(0, Top), Combat(10, true, true), Zone(30, Elsewhere)).Select(p => p.End),
                       Is.EqualTo(new DateTimeOffset?[] { Origin.AddSeconds(30) }));

    [Test]
    public void APullStillRunningAsTheLogEndsIsKept()
        => Assert.That(Pulls(Zone(0, Top), Combat(10, true, true), Cast(12, Boss, 0x7B55, 4.7f)).Select(p => (p.End, p.Lines.Count)),
                       Is.EqualTo(new[] { ((DateTimeOffset?)null, 1) }));

    [Test]
    public void OnlyTheLineTypesTheReportsReadAreKept()
    {
        var pull = Pulls(Zone(0, Top), Combat(10, true, true), Chat(11), Line(24, 12, 10), Cast(13, Boss, 0x7B55, 4.7f), Combat(20, true, false)).Single();

        Assert.That(pull.Lines.Select(l => l.Type), Is.EqualTo(new[] { ActLineType.StartsCasting }));
    }

    [Test]
    public void APullKnowsEachPlayersJobAndWhoLoggedIt()
    {
        var pull = Pulls(Zone(0, Top), Primary(0, Player1), Player(1, Player1, 24, Vector2.Zero), Player(1, Player2, 21, Vector2.Zero),
                         Combat(10, true, true), Combat(20, true, false)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(pull.Self, Is.EqualTo(Player1));
            Assert.That(pull.Jobs, Is.EquivalentTo(new Dictionary<uint, uint> { [Player1] = 24, [Player2] = 21 }));
        });
    }

    [Test]
    public void PlayersSeenInAnotherTerritoryAreForgotten()
    {
        var pull = Pulls(Zone(0, Elsewhere), Player(1, Player2, 21, Vector2.Zero), Zone(5, Top), Player(6, Player1, 24, Vector2.Zero),
                         Combat(10, true, true), Combat(20, true, false)).Single();

        Assert.That(pull.Jobs.Keys, Is.EqualTo(new[] { Player1 }));
    }

    [Test]
    public void APlayerJoiningDuringThePullIsKnownToIt()
    {
        var pull = Pulls(Zone(0, Top), Combat(10, true, true), Player(11, Player1, 24, Vector2.Zero), Combat(20, true, false)).Single();

        Assert.That(pull.Jobs.Keys, Is.EqualTo(new[] { Player1 }));
    }

    [Test]
    public void AClearShowsInThePullsDirectorCodes()
    {
        var pulls = Pulls(Zone(0, Top), Combat(10, true, true), Director(15, ActLog.DirectorClear), Combat(16, true, false),
                          Combat(30, true, true), Director(35, ActLog.DirectorWipe), Combat(36, true, false));

        Assert.That(pulls.Select(p => p.Cleared), Is.EqualTo(new[] { true, false }));
    }

    // The fade-out's last hits land after the wipe and must not open a pull; the first enemy ability after it does.
    [Test]
    public void WithoutCombatFlagsTheDutyDirectorsCodesSplitThePulls()
    {
        var pulls = Pulls(Zone(0, Top), Director(5, ActLog.DirectorCommence), Hit(6, Boss, 0x7B55, Player1), Director(20, ActLog.DirectorWipe),
                          Hit(22, Boss, 0x7B55, Player1), Director(30, ActLog.DirectorFadeOutDone),
                          Hit(40, Boss, 0x7B55, Player1), Director(60, ActLog.DirectorClear), Hit(62, Boss, 0x7B55, Player1));

        Assert.That(pulls.Select(p => (p.Start, p.End, p.Cleared)), Is.EqualTo(new[]
        {
            (Origin.AddSeconds(5), (DateTimeOffset?)Origin.AddSeconds(20), false),
            (Origin.AddSeconds(40), (DateTimeOffset?)Origin.AddSeconds(60), true),
        }));
    }

    [Test]
    public void APlayersAbilityOpensNoPullWithoutCombatFlags()
        => Assert.That(Pulls(Zone(0, Top), Hit(6, Player1, 0x7B55, Boss)), Is.Empty);

    private static List<ActPull> Pulls(params string[] lines) => ActLog.Pulls("Network_test.log", () => lines, Top).ToList();
}
