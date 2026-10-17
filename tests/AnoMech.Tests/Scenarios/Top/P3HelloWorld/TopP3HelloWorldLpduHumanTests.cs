using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3HelloWorld;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Tests;

// The sunny-day soak drives the player's seat like a bot. These keep the seat on the slideshow's
// plan where the bots add steps of their own, and check the bots still never cost a human the run.
// A `Bot` takeover of the player's own seat leaves it on the AI, so it only holds the seat in place
// until the AI's next order.
public class TopP3HelloWorldLpduHumanTests
{
    private const int Seeds = 6;
    private const float LooseSpreadRadius = 8.5f;
    private const float RunSpeed = 6.5f;
    private static readonly float[] SetStarts = [19.1f, 40.1f, 61.2f, 82.5f];
    private const float PassOrder = 10.3f;
    private const float LateBy = 1f;

    private static readonly Dictionary<PartyRole, float> LooseSpreadBearings = new()
    {
        [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
    };

    private static IEnumerable<PartyRole> Seats => PerRole.All;

    // The bots re-spread by debuff at 5 s; a human who stays on the slideshow's spread instead must
    // not be brought a rot before the towers.
    [TestCaseSource(nameof(Seats))]
    public void HoldingTheLooseSpreadSpotCatchesNoRot(PartyRole seat)
    {
        for (var i = 0; i < Seeds; i++)
        {
            var onlyTheDealtRots = false;
            var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
            {
                PlayerRole = seat,
                StopAt = SetStarts[0],
                Takeovers = [new Takeover(5.05f, LooseSpreadSpot(seat), Bot: seat)],
                Probe = p =>
                {
                    if (p.Crossed(SetStarts[0] - 0.1f)) onlyTheDealtRots = OnlyTheDealtRots(p);
                },
            });

            Assert.That(run.Passed, run.ToString);
            Assert.That(onlyTheDealtRots, $"a rot changed hands before the towers (seed {run.Seed}, {seat} held its spread spot)");
        }
    }

    // A pass a second behind the bots still lands inside the window, and that rot then goes off a
    // second later in the next set, or in the last set as the bots line up for Monitors.
    [TestCase(1, HelloWorldKit.RemoteFirst)]
    [TestCase(1, HelloWorldKit.LocalFirst)]
    [TestCase(2, HelloWorldKit.Defamation)]
    [TestCase(2, HelloWorldKit.Stack)]
    [TestCase(3, HelloWorldKit.LocalFirst)]
    [TestCase(3, HelloWorldKit.RemoteFirst)]
    public void PassingASecondLateKillsNobody(int set, HelloWorldKit kit)
    {
        var passOrder = SetStarts[set - 1] + PassOrder;
        for (var i = 0; i < Seeds; i++)
        {
            var seat = PerRole.All[Random.Shared.Next(PerRole.All.Length)];
            Vector3? pass = null;
            float? caught = null;
            var run = ScenarioRun.Execute(typeof(TopP3HelloWorldScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
            {
                PlayerRole = seat,
                Overrides = o => ((TopP3HelloWorldStateOverrides)o).Kit[seat] = kit,
                Probe = p =>
                {
                    var me = p.Member(seat)!;
                    if (p.Crossed(passOrder))
                    {
                        pass = me.MoveDestination;
                        me.StopMoving();
                    }
                    if (p.Crossed(passOrder + LateBy) && pass is { } to) me.MoveTo(to, RunSpeed);
                    if (caught is null && p.Time > passOrder && (me.HasStatus(P3StatusId.RedRot) || me.HasStatus(P3StatusId.BlueRot)))
                        caught = p.Time;
                },
            });

            Assert.That(run.Passed, run.ToString);
            Assert.That(caught, Is.GreaterThan(passOrder + LateBy), $"{seat} should have taken its rot late (seed {run.Seed})");
        }
    }

    private static Vector2 LooseSpreadSpot(PartyRole seat)
    {
        var bearing = LooseSpreadBearings[seat] * MathF.PI / 180f;
        return new(LooseSpreadRadius * MathF.Sin(bearing), -LooseSpreadRadius * MathF.Cos(bearing));
    }

    // Each defamation and stack holder still holds just the rot it was dealt, and nobody else holds one.
    private static bool OnlyTheDealtRots(ScenarioProbe p)
        => PerRole.All.All(role =>
        {
            var member = p.Member(role)!;
            var dealt = member.HasStatus(P3StatusId.CriticalOverflowBug) || member.HasStatus(P3StatusId.CriticalSynchronizationBug);
            var rots = (member.HasStatus(P3StatusId.RedRot) ? 1 : 0) + (member.HasStatus(P3StatusId.BlueRot) ? 1 : 0);
            return rots == (dealt ? 1 : 0);
        });
}
