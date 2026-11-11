using MitCue.Core.Actions;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

public class ReminderBuildTests
{
    [Fact]
    public void ExplicitOffset_PressTimeIsHitPlusOffset()
    {
        var entry = Kit.Entry("p1.raidwide", -3.3, Kit.Do(Kit.Temperance));
        var s = Kit.Scheduler(entry);

        var r = Assert.Single(s.All);
        Assert.Same(entry, r.Entry);
        Assert.Same(entry.Do[0], r.Item);
        Assert.Equal(0, r.ItemIndex);
        Assert.Equal("p1.raidwide", r.Mechanic.Id);
        Assert.Equal(100, r.Hit, Kit.Tolerance);
        Assert.Equal(96.7, r.PressAt, Kit.Tolerance);
        Assert.Equal(81, r.Earliest, Kit.Tolerance);
        Assert.Equal(99.7, r.Latest, Kit.Tolerance);
        Assert.Equal(ReminderState.Pending, r.State);
        Assert.Null(r.FinishedAt);
        Assert.Empty(s.Problems);
        Assert.Empty(s.RecentUses);
    }

    [Theory]
    [InlineData(16536u, "p1.raidwide", 94.0)]   // Temperance: 20 s party mit → 6 s before
    [InlineData(7432u, "p1.raidwide", 96.0)]    // Divine Benison: instant shield → 4 s before
    [InlineData(185u, "p1.raidwide", 95.5)]     // Adloquium: 2 s cast shield → 4.5 s before
    [InlineData(3570u, "p1.raidwide", 101.0)]   // Tetragrammaton: heal → 1 s after
    [InlineData(133u, "p1.raidwide", 95.5)]     // Medica II: 15 s regen → 4.5 s before
    [InlineData(7531u, "p1.buster", 124.0)]     // Rampart: 20 s tank mit → 6 s before
    [InlineData(30u, "p1.buster", 128.0)]       // Hallowed Ground: invuln → 2 s before
    [InlineData(7561u, "p1.raidwide", 97.0)]    // Swiftcast: buff → 3 s before
    [InlineData(999999u, "p1.raidwide", 97.0)]  // not in the catalog → 3 s before
    public void WithoutOffset_PressTimeComesFromTheAction(uint actionId, string mechanic, double expectedPressAt)
    {
        var s = Kit.Scheduler(Kit.Entry(mechanic, null, PlanItem.ForAction(actionId, "x")));
        Assert.Equal(expectedPressAt, Assert.Single(s.All).PressAt, Kit.Tolerance);
    }

    [Fact]
    public void WithoutOffset_NoteIsThreeSecondsBefore()
    {
        var r = Assert.Single(Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Note("Stack middle"))).All);
        Assert.Equal(97, r.PressAt, Kit.Tolerance);
        Assert.Equal(87, r.Earliest, Kit.Tolerance);
        Assert.Equal(99.7, r.Latest, Kit.Tolerance);
    }

    [Fact]
    public void ItemsOfAnEntry_ShareThePressTime_SetByTheFirstResolvableAction()
    {
        var entry = Kit.Entry("p1.raidwide", null, Kit.Note("Everything"), Kit.Do(Kit.Temperance), Kit.Do(Kit.Tetragrammaton));
        var s = Kit.Scheduler(entry);

        Assert.Equal([0, 1, 2], s.All.Select(r => r.ItemIndex));
        Assert.All(s.All, r => Assert.Equal(94, r.PressAt, Kit.Tolerance)); // Temperance's −6, not the note's −3

        // each item keeps its own use window
        var (note, temperance, tetragrammaton) = (s.All[0], s.All[1], s.All[2]);
        Assert.Equal(84, note.Earliest, Kit.Tolerance);
        Assert.Equal(99.7, note.Latest, Kit.Tolerance);
        Assert.Equal(81, temperance.Earliest, Kit.Tolerance);
        Assert.Equal(99.7, temperance.Latest, Kit.Tolerance);
        Assert.Equal(84, tetragrammaton.Earliest, Kit.Tolerance);
        Assert.Equal(99, tetragrammaton.Latest, Kit.Tolerance); // heal: press + 5
    }

    [Fact]
    public void SequenceEntry_KeepsItemOrder()
    {
        var entry = Kit.Entry("p1.raidwide", -5, Kit.Do(Kit.Adloquium), Kit.Do(Kit.Swiftcast), Kit.Do(Kit.Adloquium));
        entry.Seq = true;
        var s = Kit.Scheduler(entry);

        Assert.Equal([0, 1, 2], s.All.Select(r => r.ItemIndex));
        Assert.Equal([Kit.Adloquium.Id, Kit.Swiftcast.Id, Kit.Adloquium.Id], s.All.Select(r => r.ActionIds[0]));
        Assert.All(s.All, r => Assert.Equal(95, r.PressAt, Kit.Tolerance));
    }

    [Fact]
    public void EitherOr_IsTimedByItsFirstResolvableOption()
    {
        var s = Kit.Scheduler(
            Kit.Entry("p1.raidwide", null, Kit.Either(Kit.Unlisted, Kit.DivineBenison)),
            Kit.Entry("p1.buster", null, Kit.Either(Kit.Temperance, Kit.DivineBenison)));

        var unlistedFirst = s.All[0];
        Assert.Equal(96, unlistedFirst.PressAt, Kit.Tolerance);   // Divine Benison: −4
        Assert.Equal(86, unlistedFirst.Earliest, Kit.Tolerance);  // 100 − 15 + 1
        Assert.Equal([Kit.Unlisted.Id, Kit.DivineBenison.Id], unlistedFirst.ActionIds);

        Assert.Equal(124, s.All[1].PressAt, Kit.Tolerance);       // Temperance: −6 at the 130 buster
    }

    [Fact]
    public void UnresolvedAction_IsTimedLikeANote_ButStillTrackedAsAnAction()
    {
        var r = Assert.Single(Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Unlisted))).All);
        Assert.Equal(97, r.PressAt, Kit.Tolerance);
        Assert.Equal(87, r.Earliest, Kit.Tolerance);
        Assert.Equal(99.7, r.Latest, Kit.Tolerance);
        Assert.True(r.IsAction);
        Assert.Equal([Kit.Unlisted.Id], r.ActionIds);
    }

    [Fact]
    public void ActionIdsAndIsAction_DescribeTheItem()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", -3, Kit.Do(Kit.Temperance), Kit.Either(Kit.Tetragrammaton, Kit.DivineBenison), Kit.Note("Everything")));

        Assert.Equal([Kit.Temperance.Id], s.All[0].ActionIds);
        Assert.True(s.All[0].IsAction);
        Assert.Equal([Kit.Tetragrammaton.Id, Kit.DivineBenison.Id], s.All[1].ActionIds);
        Assert.True(s.All[1].IsAction);
        Assert.Empty(s.All[2].ActionIds);
        Assert.False(s.All[2].IsAction);
    }

    [Theory]
    [InlineData(null, 160.0)]
    [InlineData(0, 160.0)]
    [InlineData(1, 163.0)]
    [InlineData(2, 166.0)]
    [InlineData(7, 166.0)]    // beyond the last hit: the last hit
    [InlineData(-1, 160.0)]
    public void HitIndex_AnchorsToTheNthHitOfAMultiHitMechanic(int? hitIndex, double expectedHit)
    {
        var r = Assert.Single(Kit.Scheduler(Kit.Entry("p1.triple", -2, Kit.Do(Kit.Temperance)).AtHit(hitIndex)).All);
        Assert.Equal(expectedHit, r.Hit, Kit.Tolerance);
        Assert.Equal(expectedHit - 2, r.PressAt, Kit.Tolerance);
        Assert.Equal(expectedHit - 0.3, r.Latest, Kit.Tolerance);
    }

    [Fact]
    public void HitIndex_OnASingleHitMechanic_UsesItsHit()
    {
        var r = Assert.Single(Kit.Scheduler(Kit.Entry("p1.raidwide", -2, Kit.Do(Kit.Temperance)).AtHit(3)).All);
        Assert.Equal(100, r.Hit, Kit.Tolerance);
    }

    [Fact]
    public void All_IsOrderedByPressTime_ThenEntryOrder_ThenItemIndex()
    {
        var late = Kit.Entry("p1.buster", -3, Kit.Do(Kit.Rampart));                                     // 127
        var pair = Kit.Entry("p1.raidwide", -2, Kit.Do(Kit.Temperance), Kit.Do(Kit.Tetragrammaton));    // 98, 98
        var same = Kit.Entry("p1.raidwide", -2, Kit.Do(Kit.Swiftcast));                                 // 98
        var early = Kit.Entry("p1.raidwide", -10, Kit.Do(Kit.MedicaII));                                // 90
        var s = Kit.Scheduler(late, pair, same, early);

        Assert.Equal(
            [(early, 0), (pair, 0), (pair, 1), (same, 0), (late, 0)],
            s.All.Select(r => (r.Entry, r.ItemIndex)));
    }

    [Fact]
    public void UnknownMechanic_EntryIsSkippedAndReported()
    {
        var bad = Kit.Entry("p9.nope", -3, Kit.Do(Kit.Temperance), Kit.Do(Kit.Swiftcast));
        bad.Id = "e042";
        bad.Src = "'TOP – White Mage'!C18";
        var good = Kit.Entry("p1.raidwide", -3, Kit.Do(Kit.Temperance));
        var s = Kit.Scheduler(bad, good);

        Assert.Same(good, Assert.Single(s.All).Entry);
        var problem = Assert.Single(s.Problems);
        Assert.Contains("e042", problem, StringComparison.Ordinal);
        Assert.Contains("p9.nope", problem, StringComparison.Ordinal);
        Assert.Contains("C18", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMechanic_WithoutEntryId_IsReportedByPosition()
    {
        var unnamed = Kit.Entry("", -3, Kit.Do(Kit.Temperance));
        unnamed.Id = "";
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", -3, Kit.Do(Kit.Temperance)), unnamed);

        Assert.Single(s.All);
        Assert.Contains("#2", Assert.Single(s.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void MechanicIds_MatchCaseInsensitively()
    {
        var s = Kit.Scheduler(Kit.Entry("P1.Raidwide", -3, Kit.Do(Kit.Temperance)));
        Assert.Equal(100, Assert.Single(s.All).Hit, Kit.Tolerance);
        Assert.Empty(s.Problems);
    }

    [Fact]
    public void EntryWithoutItems_ProducesNoReminderAndNoProblem()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", -3));
        Assert.Empty(s.All);
        Assert.Empty(s.Problems);
    }

    [Theory]
    [InlineData(80, 97.0)]   // 10 s at level 80 → 3 s before
    [InlineData(90, 94.0)]   // 20 s at level 90 → 6 s before
    public void Durations_AreTakenAtTheFightsSyncLevel(int syncLevel, double expectedPressAt)
    {
        var action = new ActionInfo
        {
            Id = 1,
            Name = "Scaling Mit",
            Kind = ActionKind.PartyMit,
            DurationByLevel = new Dictionary<int, double> { [50] = 10, [90] = 20 },
        };
        var fight = Kit.Fight(syncLevel, Kit.Mech("p1.raidwide", 100));
        var s = new ReminderScheduler(fight, [Kit.Entry("p1.raidwide", null, Kit.Do(action))], new FakeCatalog([action]), new ReminderOptions());

        Assert.Equal(expectedPressAt, Assert.Single(s.All).PressAt, Kit.Tolerance);
    }

    [Fact]
    public void WindowOptions_AreAppliedWhenBuilding()
    {
        var options = new ReminderOptions { MitigationLatestBeforeHit = 1, HealLateWindow = 8 };
        var s = Kit.Scheduler(options, Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance)), Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Tetragrammaton)));

        Assert.Equal(99, s.All[0].Latest, Kit.Tolerance);
        Assert.Equal(109, s.All[1].Latest, Kit.Tolerance);
        Assert.Same(options, s.Options);
    }

    [Fact]
    public void Entries_AreEnumeratedOnce()
    {
        var enumerations = 0;
        IEnumerable<PlanEntry> Lazy()
        {
            enumerations++;
            yield return Kit.Entry("p1.raidwide", -3, Kit.Do(Kit.Temperance));
            yield return Kit.Entry("p1.buster", -3, Kit.Do(Kit.Rampart));
        }

        var s = new ReminderScheduler(Kit.TestFight(), Lazy(), new FakeCatalog(Kit.Catalogued), new ReminderOptions());
        Assert.Equal(2, s.All.Count);
        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var fight = Kit.TestFight();
        var catalog = new FakeCatalog(Kit.Catalogued);
        var options = new ReminderOptions();
        Assert.Throws<ArgumentNullException>(() => new ReminderScheduler(null!, [], catalog, options));
        Assert.Throws<ArgumentNullException>(() => new ReminderScheduler(fight, null!, catalog, options));
        Assert.Throws<ArgumentNullException>(() => new ReminderScheduler(fight, [], null!, options));
        Assert.Throws<ArgumentNullException>(() => new ReminderScheduler(fight, [], catalog, null!));
    }

    [Fact]
    public void All_IsReadOnly()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", -3, Kit.Do(Kit.Temperance)));
        Assert.False(s.All is Reminder[], "All must not expose the internal array");
        Assert.Throws<NotSupportedException>(() => ((IList<Reminder>)s.All).Add(s.All[0]));
    }
}
