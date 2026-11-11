using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Plans;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>
/// Calls of a share code or plan file that the runtime never shows, although the review's rules let them pass: the plan
/// checks report them (review round 3: the old PlanValidator-based checks did, the review-based ones did not).
/// </summary>
public class PlanChecksNeverShownTests
{
    /// <summary>A plan with a press time that is not a number and a job-less call for a slot that is no party slot.</summary>
    private static MitPlan Probe() => new()
    {
        Id = "never",
        Name = "Never",
        Fight = "top",
        Entries =
        [
            new PlanEntry { Id = "e1", Mechanic = "p1.pantokrator", Offset = double.NaN, Who = new WhoSpec { Job = Job.SCH }, Do = [PlanItem.ForAction(Kit.SacredSoil, "Sacred Soil")] },
            new PlanEntry { Id = "e2", Mechanic = "p1.pantokrator", Offset = -3, Who = new WhoSpec { Slot = "H3" }, Do = [PlanItem.ForAction(Kit.Temperance, "Temperance")] },
            new PlanEntry { Id = "e3", Mechanic = "p1.pantokrator", Offset = double.PositiveInfinity, Who = new WhoSpec { Job = Job.SCH }, Do = [PlanItem.ForAction(Kit.SacredSoil, "Sacred Soil")] },
        ],
    };

    [Fact]
    public void CallsThatAreNeverShownAreReported()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Data.EnsureLoaded();
        var plan = PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(Probe())); // share codes carry named floats
        Assert.True(double.IsNaN(plan.Entries[0].Offset!.Value));

        // e1 never becomes upcoming or due in the whole fight, and no player context selects e2.
        var fight = parts.Data.GetFight("top")!;
        var scheduler = new ReminderScheduler(fight, [plan.Entries[0]], parts.Data.Actions, new ReminderOptions());
        for (var t = 0.0; t < 1200; t += 0.5)
            Assert.Empty(scheduler.Update(t));
        foreach (var job in Jobs.All)
        {
            foreach (var slot in Slots.Standard.Append(null))
                Assert.DoesNotContain(PlanSelection.For(plan, new PlayerContext(job, 90, slot)), e => e.Id == "e2");
        }

        var problems = PlanChecks.Find(plan, parts.Data);

        Assert.Equal(
            [
                "Entry 'e1': the offset NaN is not a number of seconds, so the call is never shown.",
                "Entry 'e2': \"H3\" is not a party slot (MT, OT, H1, H2, M1, M2, R1, R2) and the call is for no job, so it is never shown.",
                "Entry 'e3': the offset Infinity is not a number of seconds, so the call is never shown.",
            ],
            problems);
    }

    [Fact]
    public void TheShareCodesMessageNamesThem()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Data.EnsureLoaded();
        parts.Plans.Check = plan => PlanChecks.Find(plan, parts.Data);
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(Probe()), out _, out var message), message);

        Assert.Contains("3 problems: Entry 'e1': the offset NaN is not a number of seconds, so the call is never shown", message, StringComparison.Ordinal);
        Assert.Contains(parts.Errors.Snapshot(), p => p.Key == "plans.load.check.never" && p.Message.Contains("\"H3\" is not a party slot", StringComparison.Ordinal));
    }

    [Fact]
    public void ASlotSynonymOrACallForAJobIsNotAProblem()
    {
        // "D3" is R1 (written so when the plan is read); a Scholar's call for an odd slot is shown to a Scholar without a slot.
        var parts = SessionManagerTests.NewManager();
        parts.Data.EnsureLoaded();
        var plan = new MitPlan
        {
            Id = "fine",
            Fight = "top",
            Entries =
            [
                new PlanEntry { Id = "e1", Mechanic = "p1.pantokrator", Offset = -3, Who = new WhoSpec { Slot = "D3" }, Do = [PlanItem.ForAction(7560, "Addle")] },
                new PlanEntry { Id = "e2", Mechanic = "p1.pantokrator", Offset = -3, Who = new WhoSpec { Slot = "H3", Job = Job.SCH }, Do = [PlanItem.ForAction(Kit.SacredSoil, "Sacred Soil")] },
            ],
        };

        Assert.Empty(PlanChecks.Find(PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(plan)), parts.Data));
    }
}
