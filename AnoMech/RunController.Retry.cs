using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Implementations;
using AnoMech.Scenarios;

namespace AnoMech;

// Retry: the last local run's roll played again, started as a Start starts a fresh one.
internal sealed partial class RunController
{
    // Null when Retry can play the last local run's roll again now: a session's run plays on the host for everyone. A
    // conflict only the live settings have refuses nothing, since the replay puts its run's own back before it starts.
    public string? RetryRefusal
    {
        get
        {
            if (Plugin.AnoMechPresence.Refusal is { } anoMech) return anoMech;
            if (multiplayer.InSession) return RollReplay.MultiplayerRefusal;
            var rolls = Plugin.Practice.Rolls;
            if (rolls.RetryRefusal is { } refusal) return refusal;
            var last = rolls.Last!;
            var scenario = last.AsPlayed.Scenario;
            // EnterAndStart would refuse another zone's run only after the click.
            if (game.Zone is { } zone && zone != scenario.Phase.Zone)
                return $"Leave {zone.Name} first: the roll plays in {scenario.Phase.Zone.Name}.";
            var liveSettingsDecide = scenario.SettingsConflicts.Count == 0
                                     || ScenarioSettingsSync.Serialize(scenario.SettingsOverrides) == last.Settings;
            return StartRefusal(SelectionOf(last.AsPlayed), IsSolo(last.AsPlayed), liveSettingsDecide);
        }
    }

    public void Retry()
    {
        if (RetryRefusal is { } refusal)
        {
            Game.Print(refusal);
            return;
        }
        var p = Plugin.Practice.Rolls.Replay();
        // The run's own settings are back, so every conflict counts now, before anything zones in.
        if (StartRefusal(SelectionOf(p), IsSolo(p)) is { } conflict)
        {
            Game.Print(conflict);
            return;
        }
        void Refused(string? why)
        {
            if (why != null) Game.Print($"Retry refused: {why}.");
        }
        if (ZoneControl.StartBlockedReason(out var settling) != null && settling != null)
        {
            startWait.Hold(settling, () => game.EnterAndStart(p, SessionMode.Local, Refused));
            return;
        }
        game.EnterAndStart(p, SessionMode.Local, Refused);
    }

    private static ScenarioSelection SelectionOf(RunScenarioParams p)
        => new(p.Scenario, p.SelectedAi, p.SelectedWaymark, p.Seed, (p.Party as LocalPartyProvider)?.RoleOverride);

    private static bool IsSolo(RunScenarioParams p) => p.Party is LocalPartyProvider { Solo: true };
}
