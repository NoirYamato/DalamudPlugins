using AnoMech.Core.Native.Interfaces;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Tests;

// A test plays the player's client through Act: each step reaches whoever listens to Acted, as it is.
public class FakeUserActionsTests
{
    [Test]
    public void ActRaisesActedWithEachStep()
    {
        var userActions = new FakeUserActions();
        var heard = new List<PlayerAction>();
        userActions.Acted += heard.Add;
        PlayerAction[] steps =
        [
            new(PlayerActionStep.CastStarted, ActionType.Action, 4242, 0x40001234, 4.5f),
            new(PlayerActionStep.Interrupted, ActionType.Action, 4242, 0x40001234),
            new(PlayerActionStep.Resolved, ActionType.Action, 4240, 0),
        ];

        foreach (var step in steps) userActions.Act(step);

        Assert.That(heard, Is.EqualTo(steps));
    }

    [Test]
    public void NobodyListeningIsFine()
        => Assert.DoesNotThrow(() => new FakeUserActions().Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, 4240, 0)));
}
