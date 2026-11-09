using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.TopP6HumanMitigation;
using static AnoMech.Tests.TopP6TankHit;
using Actions = AnoMech.Scenarios.Top.TopP6Actions;

namespace AnoMech.Tests;

// A human tank's own mitigation under a Cosmo Dive buster and at Wild Charge's front, in the sim's terms
// (Mitigation.Effective) 0.38 and 0.45, checked as an EnemyAction's MinMit is: the human's alone, and only with "Require
// tank mitigation" and "Resolve your own actions" on. The human main tank presses its set, each action as its client
// plays it, 5 s before the hit unless a test says otherwise, so the 4 s that Stem the Flow, Clarity of Corundum and
// Knight's Resolve add are over:
//   Cosmo Dive, cast at 2.0 (its bar ends at 7.6): its busters land at 10.0 on the two closest, the MT at (-4.6, -4.6) and
//   the OT at (4.6, -4.6); the six share the stack at (7.5, 7.5).
//   Wild Charge at 6.0 toward the MT: both tanks at the front of its line at (0, 6.5), the six behind at (0, 10.5); whom it
//   kills dies at 6.8.
public class TopP6TankMitigationTests
{
    private const float DiveAt = 2f, DiveLandsAt = 10f;
    private const float WildChargeAt = 6f, WildChargeKillsAt = 6.78f;
    private const float AheadOfTheHit = 5f, InsideTheFourSeconds = 3f;

    private static readonly PartyRole[] TheSix = [RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static readonly Dictionary<PartyRole, Vector2> ForTheDive = new Dictionary<PartyRole, Vector2>
    {
        [MainTank] = new(-4.6f, -4.6f),
        [OffTank] = new(4.6f, -4.6f),
    }.Concat(TheSix.Select(role => KeyValuePair.Create(role, new Vector2(7.5f, 7.5f)))).ToDictionary();

    private static readonly Dictionary<PartyRole, Vector2> ForTheWildCharge = PerRole.All.ToDictionary(
        role => role, role => role.IsTank() ? new Vector2(0f, 6.5f) : new Vector2(0f, 10.5f));

    private static readonly TimelineRunOptions HumanMainTank = new() { PlayerRole = MainTank, HumanPlayer = true, UserActionsEnabled = true };

    private static string Cause(TopP6TankHit hit)
        => IsCosmoDive(hit)
            ? $"Cosmo Dive ({TopP6AlphaOmega.CosmoDiveBusterWithoutBigCooldowns})"
            : $"Wave Cannon ({Actions.WildCharge.DeathExplanation}; not enough mitigation)";

    private static IEnumerable<TestCaseData> EveryLpduSet()
        => from job in TankJobs
           from hit in Enum.GetValues<TopP6TankHit>()
           from set in LpduSets(job, hit)
           select new TestCaseData(job, hit, set).SetName($"{{m}}({job}, {hit}, {Describe(set)})");

    [TestCaseSource(nameof(EveryLpduSet))]
    public void EachJobsLpduSetLivesThroughItsHit(JobId job, TopP6TankHit hit, uint[] set)
    {
        var run = Play(hit, job, set);

        Assert.That(run.Passed, run.ToString);
    }

    // Rampart and one short cooldown: short of a buster, and of Wild Charge even inside the 4 s that Stem the Flow, Clarity
    // of Corundum and Knight's Resolve add.
    private static IEnumerable<TestCaseData> RampartAndOneShortCooldown()
    {
        (JobId Job, TopP6TankHit Hit, uint Short, float Before)[] cases =
        [
            (JobId.Warrior, CosmoDive1, ThrillOfBattle, AheadOfTheHit), (JobId.DarkKnight, CosmoDive1, TheBlackestNight, AheadOfTheHit),
            (JobId.Gunbreaker, CosmoDive1, Camouflage, AheadOfTheHit), (JobId.Paladin, CosmoDive1, Bulwark, AheadOfTheHit),
            (JobId.Warrior, WildCharge1, Bloodwhetting, InsideTheFourSeconds), (JobId.DarkKnight, WildCharge1, DarkMind, InsideTheFourSeconds),
            (JobId.Gunbreaker, WildCharge1, HeartOfCorundum, InsideTheFourSeconds), (JobId.Paladin, WildCharge1, HolySheltron, InsideTheFourSeconds),
        ];
        return cases.Select(c => new TestCaseData(c.Job, c.Hit, new[] { Rampart, c.Short }, c.Before)
            .SetName($"{{m}}({c.Job}, {(IsCosmoDive(c.Hit) ? "Cosmo Dive" : "Wild Charge")}, {Describe([Rampart, c.Short])}, {c.Before:0} s ahead)"));
    }

    [TestCaseSource(nameof(RampartAndOneShortCooldown))]
    public void RampartAndOneShortCooldownDies(JobId job, TopP6TankHit hit, uint[] set, float before)
    {
        var run = Play(hit, job, set, before);

        Assert.That(run.Deaths.Select(d => (d.Role, d.Cause)), Is.EqualTo(new[] { (MainTank, Cause(hit)) }), run.ToString);
        Assert.That(run.Deaths[0].Time, Is.EqualTo(IsCosmoDive(hit) ? DiveLandsAt : WildChargeKillsAt).Within(TimelineRun.FrameLate), run.ToString);
    }

    [Test]
    public void AnInvulnLivesThroughEither([Values(JobId.Warrior, JobId.DarkKnight, JobId.Gunbreaker, JobId.Paladin)] JobId job,
        [Values(CosmoDive1, WildCharge1)] TopP6TankHit hit)
    {
        var invuln = job switch
        {
            JobId.Warrior => Holmgang,
            JobId.DarkKnight => LivingDead,
            JobId.Gunbreaker => Superbolide,
            _ => HallowedGround,
        };

        var run = Play(hit, job, [invuln]);

        Assert.That(run.Passed, run.ToString);
    }

    // What dies with the check on lives with it off, or without the module that plays the human's own actions.
    [TestCaseSource(nameof(RampartAndOneShortCooldown))]
    public void WithTankMitigationOffNothingIsChecked(JobId job, TopP6TankHit hit, uint[] set, float before)
    {
        var withItOff = Play(hit, job, set, before, HumanMainTank with { Configure = config => config.EnableTankMitigation = false });
        var withoutTheModule = Play(hit, job, set, before, HumanMainTank with { UserActionsEnabled = false });

        Assert.That(withItOff.Passed, withItOff.ToString);
        Assert.That(withoutTheModule.Passed, withoutTheModule.ToString);
    }

    // Godmode deals no death: the main tank is reported once, for the rule it broke.
    [TestCaseSource(nameof(RampartAndOneShortCooldown))]
    public void GodmodeReportsTheTankOnce(JobId job, TopP6TankHit hit, uint[] set, float before)
    {
        var run = Play(hit, job, set, before, HumanMainTank with { GodMode = true });

        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.That(run.Chat.Where(line => line.Contains(" died: ")).ToList(),
                    Has.Count.EqualTo(1).And.All.EndsWith($" died: {Cause(hit)}"), () => string.Join(Environment.NewLine, run.Chat));
    }

    // The off-tank's buster and its place at the front of the line are checked as the main tank's are.
    [TestCase(CosmoDive1)]
    [TestCase(WildCharge1)]
    public void AHumanOffTankIsCheckedToo(TopP6TankHit hit)
    {
        var run = Play(hit, JobId.Paladin, [Rampart, Bulwark], options: HumanMainTank with { PlayerRole = OffTank });

        Assert.That(run.Deaths.Select(d => (d.Role, d.Cause)), Is.EqualTo(new[] { (OffTank, Cause(hit)) }), run.ToString);
    }

    // With the human in a healer seat, both tanks are bots and press nothing; and a player the debug driver walks is
    // checked no more than a bot.
    [TestCase(CosmoDive1, RegenHealer, true)]
    [TestCase(WildCharge1, RegenHealer, true)]
    [TestCase(CosmoDive1, MainTank, false)]
    [TestCase(WildCharge1, MainTank, false)]
    public void ABotTankIsNeverChecked(TopP6TankHit hit, PartyRole player, bool human)
    {
        var run = Play(hit, null, [], options: HumanMainTank with { PlayerRole = player, HumanPlayer = human });

        Assert.That(run.Passed, run.ToString);
    }

    // The off-tank closes in to 3.6 y of the main tank, each in the other's buster: that is what kills them, though the
    // human main tank pressed nothing either.
    [Test]
    public void ATankInTheOthersBusterHearsOfItsSpotFirst()
    {
        var run = Play(CosmoDive1, JobId.Warrior, [], spots: new Dictionary<PartyRole, Vector2>(ForTheDive) { [OffTank] = new(-1f, -4.6f) });

        Assert.That(run.Deaths.Select(d => (d.Role, d.Cause)), Is.EquivalentTo(new[]
        {
            (MainTank, $"Cosmo Dive ({TopP6AlphaOmega.InSomeoneElsesCosmoDiveBuster})"),
            (OffTank, $"Cosmo Dive ({TopP6AlphaOmega.InSomeoneElsesCosmoDiveBuster})"),
        }), run.ToString);
    }

    // The human's job, or the seat's own with none; everyone on their spot at 0.5.
    private static TimelineRunResult Play(TopP6TankHit hit, JobId? job, uint[] set, float before = AheadOfTheHit, TimelineRunOptions? options = null,
        IReadOnlyDictionary<PartyRole, Vector2>? spots = null)
        => TimelineRun.Execute(world =>
        {
            if (job is { } humans) ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)humans;
            var alphaOmega = new TopP6AlphaOmega(world);
            world.Events.Add(0f, alphaOmega.Spawn);
            world.Events.Add(0.5f, () => TimelineRun.Place(world, spots ?? (IsCosmoDive(hit) ? ForTheDive : ForTheWildCharge)));
            if (IsCosmoDive(hit))
            {
                Press(world, DiveLandsAt - before, set);
                alphaOmega.CosmoDive(DiveAt);
            }
            else
            {
                Press(world, WildChargeAt - before, set);
                world.Events.Add(WildChargeAt, () => alphaOmega.Cast(Actions.WildCharge, world.Party.Get(MainTank)));
            }
        }, options ?? HumanMainTank);
}
