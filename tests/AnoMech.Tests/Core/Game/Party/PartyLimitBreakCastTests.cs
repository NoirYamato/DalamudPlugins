using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// A limit break the sim casts for a party seat, shown as the server shows a player's: started at 1.0 for 4.5 s and
// resolved at 5.5, aimed at the main tank. The off-tank is the local player, driven as a bot.
public class PartyLimitBreakCastTests
{
    private const float StartsAt = 1f, CastSeconds = 4.5f, ResolvesAt = StartsAt + CastSeconds;

    private static uint LimitBreak3(PartyRole seat) => LimitBreakHandler.ActionId(PartyPresets.Standard[(int)seat].ClassJob, 3);

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    // Its cast ActorControl to every member and its cast bar at its target as it starts; its ActionEffect at the target and
    // the resolve ActorControl to every member as it resolves.
    [Test]
    public void ThePartySeesItsCastAndItsResolve()
    {
        var mainTank = "";
        var run = Cast(MeleeDpsA, world => mainTank = world.Party.Get(MainTank)!.EntityId.ToString("X8"));
        var casts = run.Sent("cast", LimitBreak3(MeleeDpsA));
        var effects = run.Sent("ability", LimitBreak3(MeleeDpsA));

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(ActorControls(run, "0x47").Select(Time).ToList(), Has.Count.EqualTo(8).And.All.EqualTo(StartsAt).Within(TimelineRun.FrameLate),
                        "its cast, to every member");
            Assert.That(casts.Select(c => (c.Target, MathF.Round(c.Time, 1))), Is.EqualTo(new[] { (mainTank, StartsAt) }), "its bar, at its target");
            Assert.That(effects.Select(e => (e.Target, MathF.Round(e.Time, 1))), Is.EqualTo(new[] { (mainTank, ResolvesAt) }), "its ActionEffect, at its target");
            Assert.That(ActorControls(run, "0x48").Select(Time).ToList(), Has.Count.EqualTo(8).And.All.EqualTo(ResolvesAt).Within(TimelineRun.FrameLate),
                        "its resolve, to every member");
        });
    }

    // A ground-targeted one goes down where its target stands, aimed at no one.
    [Test]
    public void AGroundTargetedOneGoesDownWhereItsTargetStands()
    {
        var run = Cast(CasterDps, world => TimelineRun.Place(world, MainTank, 4f, -6f));
        var cast = run.Sent("cast", LimitBreak3(CasterDps)).Single();

        Assert.That(cast.Target, Is.Empty);
        Assert.That(Vector2.Distance(new Vector2(cast.Position.X, cast.Position.Z), new Vector2(4f, -6f)), Is.LessThan(0.01f));
    }

    // As it resolves it takes its JobActions effects: the main tank's Land Waker covers everyone.
    [Test]
    public void ItTakesItsEffectsAsItResolves()
    {
        var covered = new Dictionary<float, int>();
        var run = TimelineRun.Execute(world =>
        {
            world.Events.Add(StartsAt, () => PartyLimitBreakCast.Resolve(world.Party, world.Party.Get(MainTank)!, LimitBreak3(MainTank), world.Party.Get(MainTank)!));
            foreach (var at in new[] { StartsAt - 0.1f, StartsAt + 0.1f })
                world.Events.Add(at, () => covered[at] = Enum.GetValues<PartyRole>().Count(r => world.Party.Get(r)!.HasStatus(LandWaker)));
        }, new TimelineRunOptions { PlayerRole = OffTank });

        Assert.That(run.Passed, run.ToString);
        Assert.That(covered, Is.EqualTo(new Dictionary<float, int> { [StartsAt - 0.1f] = 0, [StartsAt + 0.1f] = 8 }));
        Assert.That(run.Sent("ability", LimitBreak3(MainTank)), Has.Count.EqualTo(1));
    }

    // The local player's own client plays its actions: for the player's seat nothing is sent, no cast, no bar, no
    // ActionEffect and no resolve, but its effects are taken: the off-tank's Last Bastion covers everyone.
    [Test]
    public void ForThePlayersSeatItOnlyTakesItsEffects()
    {
        var covered = -1;
        var run = TimelineRun.Execute(world =>
        {
            world.Events.Add(StartsAt, () =>
            {
                var player = world.Party.Get(OffTank)!;
                PartyLimitBreakCast.Start(world.Party, player, LimitBreak3(OffTank), player, CastSeconds);
            });
            world.Events.Add(ResolvesAt, () =>
            {
                var player = world.Party.Get(OffTank)!;
                PartyLimitBreakCast.Resolve(world.Party, player, LimitBreak3(OffTank), player);
            });
            world.Events.Add(ResolvesAt + 0.1f, () => covered = Enum.GetValues<PartyRole>().Count(r => world.Party.Get(r)!.HasStatus(LastBastion)));
        }, new TimelineRunOptions { PlayerRole = OffTank });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(covered, Is.EqualTo(8), "Last Bastion on everyone");
            Assert.That(ActorControls(run, "0x47").Concat(ActorControls(run, "0x48")), Is.Empty, "no ActorControls");
            Assert.That(run.Sent("cast", LimitBreak3(OffTank)).Concat(run.Sent("ability", LimitBreak3(OffTank))), Is.Empty, "no bar, no ActionEffect");
        });
    }

    // Interrupted at 2.0, its bar is called off at once.
    [Test]
    public void AnInterruptionCallsItsBarOff()
    {
        var casting = new Dictionary<float, bool>();
        var run = TimelineRun.Execute(world =>
        {
            world.Events.Add(StartsAt, () => PartyLimitBreakCast.Start(world.Party, world.Party.Get(MeleeDpsA)!, LimitBreak3(MeleeDpsA), world.Party.Get(MainTank)!, CastSeconds));
            world.Events.Add(2f, () => PartyLimitBreakCast.Interrupt(world.Party.Get(MeleeDpsA)!));
            foreach (var at in new[] { 1.9f, 2.1f })
                world.Events.Add(at, () => casting[at] = world.Party.Get(MeleeDpsA)!.Casting.IsCasting);
        }, new TimelineRunOptions { PlayerRole = OffTank });

        Assert.That(run.Passed, run.ToString);
        Assert.That(ActorControls(run, "0xF").Select(Time), Is.EqualTo(new[] { 2f }).Within(TimelineRun.FrameLate), "the bar called off");
        Assert.That(casting, Is.EqualTo(new Dictionary<float, bool> { [1.9f] = true, [2.1f] = false }));
    }

    private const ushort LandWaker = 863, LastBastion = 196;

    // Started at 1.0 for `seat` at the main tank, resolved at 5.5; `before` runs just before it starts.
    private static TimelineRunResult Cast(PartyRole seat, Action<SimWorld>? before = null)
        => TimelineRun.Execute(world =>
        {
            world.Events.Add(StartsAt, () =>
            {
                before?.Invoke(world);
                PartyLimitBreakCast.Start(world.Party, world.Party.Get(seat)!, LimitBreak3(seat), world.Party.Get(MainTank)!, CastSeconds);
            });
            world.Events.Add(ResolvesAt, () => PartyLimitBreakCast.Resolve(world.Party, world.Party.Get(seat)!, LimitBreak3(seat), world.Party.Get(MainTank)!));
        }, new TimelineRunOptions { PlayerRole = OffTank });

    private static List<Dictionary<string, string>> ActorControls(TimelineRunResult run, string category)
        => run.Rows.Where(r => r["kind"] is "ac" or "ac_self" && r["id"] == category).ToList();

    private static float Time(Dictionary<string, string> row) => float.Parse(row["t"], CultureInfo.InvariantCulture);
}
