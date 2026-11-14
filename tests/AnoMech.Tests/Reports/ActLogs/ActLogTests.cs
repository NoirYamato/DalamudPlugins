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

    // The second pull finds the boss still standing, the helper removed, the object added in the first pull, and only the
    // map effect sent since that pull closed.
    [Test]
    public void APullKnowsWhatStoodAsItOpened()
    {
        const uint helper = 0x40000020, barrier = 0x40000030, water = 0x40000031;
        var pulls = Pulls(Zone(0, Top), Enemy(1, Boss, 11335, Vector2.Zero, 0f), Enemy(1, helper, 9020, Vector2.Zero, 0f),
                          Memory(1, "Add", barrier, ("BNpcID", "1E8536")), MapEffect(2, 0x00010001, 1),
                          Combat(10, true, true), Memory(12, "Add", water, ("BNpcID", "1E9998")), MapEffect(13, 0x00020004, 7), Combat(20, true, false),
                          Removed(21, helper, 0), MapEffect(22, 0x00040004, 2), Combat(30, true, true), Combat(40, true, false));

        Assert.Multiple(() =>
        {
            Assert.That(pulls[0].Standing.Select(l => (l.Type, l.Hex(l.Type == ActLineType.CombatantMemory ? 3 : 2))), Is.EqualTo(new[]
            {
                (ActLineType.AddCombatant, (uint?)Boss), (ActLineType.AddCombatant, helper), (ActLineType.CombatantMemory, barrier),
                (ActLineType.MapEffect, 0x80037586),
            }));
            Assert.That(pulls[0].Lines.Select(l => l.Type), Is.EqualTo(new[] { ActLineType.CombatantMemory, ActLineType.MapEffect }));
            Assert.That(pulls[1].Standing.Select(l => (l.Type, l.Hex(l.Type == ActLineType.CombatantMemory ? 3 : 2), l.Time)), Is.EqualTo(new[]
            {
                (ActLineType.AddCombatant, (uint?)Boss, Origin.AddSeconds(1)), (ActLineType.CombatantMemory, barrier, Origin.AddSeconds(1)),
                (ActLineType.CombatantMemory, water, Origin.AddSeconds(12)), (ActLineType.MapEffect, 0x80037586, Origin.AddSeconds(22)),
            }));
        });
    }

    [Test]
    public void WhatStoodInAnotherTerritoryIsForgotten()
        => Assert.That(Pulls(Zone(0, Elsewhere), Enemy(1, Boss, 11335, Vector2.Zero, 0f), Zone(5, Top), Combat(10, true, true)).Single().Standing, Is.Empty);

    [Test]
    public void TethersHeadMarkersAndAbilityLocationsAreKept()
    {
        var pull = Pulls(Zone(0, Top), Combat(10, true, true), Tether(11, Boss, Player1, 3), HeadMarker(12, Player1, 79),
                         AbilityAt(13, Boss, 0x4830, Vector2.Zero, 0f), Combat(20, true, false)).Single();

        Assert.That(pull.Lines.Select(l => l.Type), Is.EqualTo(new[] { ActLineType.Tether, ActLineType.HeadMarker, ActLineType.AbilityExtra }));
    }

    [Test]
    public void APlayersAbilityOpensNoPullWithoutCombatFlags()
        => Assert.That(Pulls(Zone(0, Top), Hit(6, Player1, 0x7B55, Boss)), Is.Empty);

    // The plugin's own runs open segments like the first: their enemies' casts never cross the network.
    [Test]
    public void ASegmentWhereNoEnemyCastOrHitShowsNoEnemyActing()
    {
        var pulls = Pulls(Zone(0, Top), Combat(10, true, true), Hit(11, Player1, 0x5EF8, Boss), Combat(20, true, false),
                          Combat(30, true, true), Cast(31, Boss, 0x7B55, 4.7f), Combat(40, true, false),
                          Combat(50, true, true), Hit(51, Boss, 0x7B0C, Player1, aoe: true), Combat(60, true, false));

        Assert.That(pulls.Select(p => p.EnemyActed), Is.EqualTo(new[] { false, true, true }));
    }

    // ACT holds the log it is writing open, as the writer below does.
    [Test]
    public void ALogStillOpenForWritingIsReadToItsEnd()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"Network_{Guid.NewGuid():N}.log");
        try
        {
            using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            foreach (var line in new[] { Zone(0, Top), Combat(10, true, true), Cast(12, Boss, 0x7B55, 4.7f) })
                writer.WriteLine(line);
            writer.Flush();

            Assert.That(ActLog.Pulls(path, Top).Select(p => (p.File, p.Lines.Count)), Is.EqualTo(new[] { (Path.GetFileName(path), 1) }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<ActPull> Pulls(params string[] lines) => ActLog.Pulls("Network_test.log", () => lines, Top).ToList();
}
