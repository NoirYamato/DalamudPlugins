using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// The presses the run makes for a seat no human presses for: its job's limit break 3, cast for as long as the sheet
// says, once nothing else is casting and the gauge is full, whatever "Require tank mitigation" says. Alpha Omega stands
// at the centre; the human sits in the off-tank seat unless a test says otherwise, driven as a bot.
public class TopP6BotLimitBreakTests
{
    private static uint Job(PartyRole seat) => PartyPresets.Standard[(int)seat].ClassJob;

    private static uint LimitBreak3(PartyRole seat) => LimitBreakHandler.ActionId(Job(seat), 3);

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    // A tank's goes off at once; a healer's casts for 2 s; the melee, ranged and caster ones for 4.5 s.
    [TestCase(MainTank, 0f)]
    [TestCase(RegenHealer, 2f)]
    [TestCase(MeleeDpsA, 4.5f)]
    [TestCase(PhysRangedDps, 4.5f)]
    [TestCase(CasterDps, 4.5f)]
    public void EachCastsForItsSheetTime(PartyRole seat, float castSeconds)
    {
        var run = Press(seat);
        var casts = run.Sent("cast", LimitBreak3(seat));

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Sent("ability", LimitBreak3(seat)).Select(a => a.Time), Is.EqualTo(new[] { 1f + castSeconds }).Within(TimelineRun.FrameLate));
        if (castSeconds == 0f)
        {
            Assert.That(casts, Is.Empty);
            return;
        }
        Assert.That(casts.Select(c => c.Time), Is.EqualTo(new[] { 1f }).Within(TimelineRun.FrameLate));
        var bar = run.Rows.Single(r => r["kind"] == "cast" && r["id"] == $"{LimitBreak3(seat)}")["detail"];
        Assert.That(float.Parse(Regex.Match(bar, @"cast=([0-9.]+)s").Groups[1].Value, CultureInfo.InvariantCulture), Is.EqualTo(castSeconds).Within(0.01f));
    }

    // What the party sees of each (PartyLimitBreakCastTests) follows the press: its cast as it starts, its resolve as it
    // lands, and a targeted one aimed at its target.
    [TestCase(MeleeDpsA)]
    [TestCase(PhysRangedDps)]
    public void ATargetedOneIsAimedAtItsTarget(PartyRole seat)
    {
        string? alphaOmega = null;
        var run = Press(seat, (world, boss) => alphaOmega = boss.Enemy!.EntityId.ToString("X8"));

        Assert.That(run.Sent("ability", LimitBreak3(seat)).Single().Target, Is.EqualTo(alphaOmega));
        Assert.That(run.Sent("cast", LimitBreak3(seat)).Single().Target, Is.EqualTo(alphaOmega));
        Assert.That(ActorControls(run, "0x47").Select(Time).ToList(), Has.Count.EqualTo(8).And.All.EqualTo(1f).Within(TimelineRun.FrameLate));
        Assert.That(ActorControls(run, "0x48").Select(Time).ToList(), Has.Count.EqualTo(8).And.All.EqualTo(5.5f).Within(TimelineRun.FrameLate));
    }

    [Test]
    public void ItWaitsForAFullGauge()
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(2.5f));
            world.Events.Add(1f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
            world.Events.Add(3f, () => limitBreaks.SetGauge(3f));
            world.Events.Add(9f, () => { });
        }, Options);

        Assert.That(run.Sent("cast", LimitBreak3(MeleeDpsA)).Select(c => c.Time), Is.EqualTo(new[] { 3f }).Within(TimelineRun.FrameLate));
    }

    // M2's press waits out M1's cast; M1's Brilliant Dynamis then fills the gauge again for it.
    [Test]
    public void ItWaitsForAnotherLimitBreaksCast()
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                world.Party.Get(MeleeDpsA)!.AddStatus(StatusId.BrilliantDynamis);
            });
            world.Events.Add(1f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
            world.Events.Add(2f, () => limitBreaks.Press(MeleeDpsB, Job(MeleeDpsB), alphaOmega.Enemy));
        }, Options);

        Assert.That(run.Sent("cast", LimitBreak3(MeleeDpsB)).Select(c => c.Time), Is.EqualTo(new[] { 5.5f }).Within(TimelineRun.FrameLate));
    }

    // Still waiting for the gauge once its time is up at 3.0, it is dropped at the end of that frame, so a check deciding
    // then still counts it as owed; it never goes out, though the gauge fills at 4.0.
    [Test]
    public void APressStillWaitingAfterItsTimeIsUpIsDropped()
    {
        var owed = new List<bool>();
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(1f));
            world.Events.Add(1f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy, until: 3f));
            world.Events.Add(3f, () => owed.Add(limitBreaks.StarvedOfGauge));
            world.Events.Add(3.1f, () => owed.Add(limitBreaks.StarvedOfGauge));
            world.Events.Add(4f, () => limitBreaks.SetGauge(3f));
            world.Events.Add(9f, () => { });
        }, Options);

        Assert.That(run.Passed, run.ToString);
        Assert.That(owed, Is.EqualTo(new[] { true, false }), "owed as its time is up, dropped after");
        Assert.That(run.Sent("cast", LimitBreak3(MeleeDpsA)), Is.Empty, "never pressed once the gauge filled");
    }

    // Unlike a bot's mitigation, it doesn't wait for "Require tank mitigation" or "Resolve your own actions".
    [Test]
    public void ItPressesWhateverTheMitigationSettingsSay()
    {
        var run = Press(MainTank, options: Options with { UserActionsEnabled = false, Configure = config => config.EnableTankMitigation = false });
        var covered = new List<bool>();
        var statuses = TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            world.Events.Add(1f, () => limitBreaks.Press(MainTank, Job(MainTank)));
            world.Events.Add(1.5f, () => covered.AddRange(Enum.GetValues<PartyRole>().Select(r => world.Party.Get(r)!.HasStatus(863))));
        }, Options with { UserActionsEnabled = false, Configure = config => config.EnableTankMitigation = false });

        Assert.That(run.Sent("ability", LimitBreak3(MainTank)), Has.Count.EqualTo(1));
        Assert.That(statuses.Passed, statuses.ToString);
        Assert.That(covered, Has.Count.EqualTo(8).And.All.True, "Land Waker on everyone");
    }

    // The bot-driven local player's seat: the press takes effect, but the client, which plays its own actions, is sent
    // nothing.
    [Test]
    public void ForTheBotDrivenPlayerSeatItOnlyTakesEffect()
    {
        var after = (Gauge: -1f, Covered: false);
        var run = TopP6LimitBreakRun.Execute((world, _, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            world.Events.Add(1f, () => limitBreaks.Press(MainTank, Job(MainTank)));
            world.Events.Add(1.5f, () => after = (limitBreaks.Gauge, world.Party.Get(OffTank)!.HasStatus(863)));
        }, Options with { PlayerRole = MainTank });

        Assert.That(run.Passed, run.ToString);
        Assert.That(after, Is.EqualTo((0f, true)));
        Assert.That(run.Sent("ability", LimitBreak3(MainTank)), Is.Empty);
        Assert.That(ActorControls(run, "0x48"), Is.Empty);
    }

    // Sent on at 2.0 mid-cast, M1 stays put until its cast ends at 5.5, then walks on.
    [Test]
    public void ItKeepsStillThroughItsCast()
    {
        var spots = new Dictionary<float, Vector2>();
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                TimelineRun.Place(world, MeleeDpsA, 0f, 6f);
            });
            world.Events.Add(1f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
            world.Events.Add(2f, () => world.Party.Get(MeleeDpsA)!.MoveTo(new Vector3(0f, 0f, 16f)));
            foreach (var at in new[] { 5.4f, 9f })
                world.Events.Add(at, () => spots[at] = Flat(world.Party.Get(MeleeDpsA)!.Position));
        }, Options);

        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(spots[5.4f], new Vector2(0f, 6f)), Is.LessThan(0.01f), "still casting");
        Assert.That(Vector2.Distance(spots[9f], new Vector2(0f, 16f)), Is.LessThan(0.5f), "walked on after");
    }

    // M1 dies at 2.0, mid-cast: its limit break never resolves and the gauge stays full.
    [Test]
    public void ADeadBotsCastNeverResolves()
    {
        TopP6LimitBreakPress? press = null;
        var gauge = -1f;
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            world.Events.Add(1f, () => limitBreaks.Press(MeleeDpsA, Job(MeleeDpsA), alphaOmega.Enemy));
            world.Events.Add(2f, () => world.Party.Get(MeleeDpsA)!.Die(SimCharacterDeathExtensions.Environment, "test"));
            world.Events.Add(6f, () =>
            {
                press = limitBreaks.Presses.Single();
                gauge = limitBreaks.Gauge;
            });
        }, Options);

        Assert.That(run.Sent("ability", LimitBreak3(MeleeDpsA)), Is.Empty);
        Assert.That(press!.ResolvedAt, Is.Null);
        Assert.That(press.InterruptedAt, Is.EqualTo(2f).Within(TimelineRun.FrameLate));
        Assert.That(gauge, Is.EqualTo(3f));
    }

    [Test]
    public void ItIsLoggedAsTheRunsPress()
    {
        TopP6LimitBreakPress? press = null;
        ulong alphaOmegaId = 0;
        TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            world.Events.Add(1f, () =>
            {
                limitBreaks.Press(PhysRangedDps, Job(PhysRangedDps), alphaOmega.Enemy);
                alphaOmegaId = (ulong)alphaOmega.Enemy!.GameObjectId;
            });
            world.Events.Add(6f, () => press = limitBreaks.Presses.Single());
        }, Options);

        Assert.That(press, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(press!.Seat, Is.EqualTo(PhysRangedDps));
            Assert.That(press.ByHuman, Is.False);
            Assert.That(press.Kind, Is.EqualTo(TopP6LimitBreakKind.PhysicalRanged));
            Assert.That(press.Level, Is.EqualTo(3));
            Assert.That(press.ActionId, Is.EqualTo(LimitBreak3(PhysRangedDps)));
            Assert.That(press.TargetId, Is.EqualTo(alphaOmegaId));
            Assert.That(press.CastStartedAt, Is.EqualTo(1f).Within(TimelineRun.FrameLate));
            Assert.That(press.CastSeconds, Is.EqualTo(4.5f).Within(0.01f));
            Assert.That(press.ResolvedAt, Is.EqualTo(5.5f).Within(TimelineRun.FrameLate));
        });
    }

    private static readonly TimelineRunOptions Options = new() { PlayerRole = OffTank, UserActionsEnabled = true };

    private static TimelineRunResult Press(PartyRole seat, Action<SimWorld, TopP6AlphaOmega>? atThePress = null, TimelineRunOptions? options = null)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            world.Events.Add(1f, () =>
            {
                atThePress?.Invoke(world, alphaOmega);
                limitBreaks.Press(seat, Job(seat), seat.IsTank() || seat is RegenHealer or ShieldHealer ? null : alphaOmega.Enemy);
            });
        }, options ?? Options);

    private static List<Dictionary<string, string>> ActorControls(TimelineRunResult run, string category)
        => run.Rows.Where(r => r["kind"] is "ac" or "ac_self" && r["id"] == category).ToList();

    private static float Time(Dictionary<string, string> row) => float.Parse(row["t"], CultureInfo.InvariantCulture);

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
