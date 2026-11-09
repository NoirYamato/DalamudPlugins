using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Implementations;
using AnoMech.Multiplayer;

namespace AnoMech;

// The user's Start / Reset / Leave (main window, /ano), routed to the local Game or, for a
// multiplayer peer, to the host.
internal sealed class RunController
{
    private readonly Game game;
    private readonly MultiplayerManager multiplayer;

    // Solo Start only: a restart inside a loaded zone never settles, and the multiplayer host
    // holds its own.
    private readonly SettleWait startWait = new();

    public RunController(Game game, MultiplayerManager multiplayer)
    {
        this.game = game;
        this.multiplayer = multiplayer;
    }

    public string? StartWaitingOn => startWait.WaitingOn;

    public void Tick() => startWait.Tick();

    public string? StartRefusal(ScenarioSelection? selection, bool solo)
    {
        if (selection is not { Scenario: var scenario }) return "No scenario selected.";
        if (solo && !scenario.SupportsSolo) return $"{scenario.Name} does not support Solo mode.";
        // Starting here bypasses MultiplayerManager: a host would run the fight without a
        // StartMessage, a peer would start a second independent simulation.
        if (scenario.SupportsMultiplayer && multiplayer.IsConnected)
            return "In a multiplayer session -- only the host's Start button runs it.";
        // ScenarioSession refuses these too, but only after zoning in and seating the party.
        if (scenario.SettingsConflicts is [var conflict, ..]) return conflict;
        if (startWait.WaitingOn is { } waiting) return $"Waiting for {waiting} to settle before starting...";
        // A settle only delays the start; Start waits it out.
        if (ZoneControl.StartBlockedReason(out var settling) is { } blocked && settling == null)
            return $"Cannot start: {blocked}.";
        if (!solo && selection.Strat is null) return "No strat available for this region yet.";
        return null;
    }

    public void Start(ScenarioSelection? selection, bool solo)
    {
        if (StartRefusal(selection, solo) is { } refusal)
        {
            Plugin.Log.Warning(refusal);
            return;
        }
        var party = new LocalPartyProvider { RoleOverride = selection!.RoleOverride, Solo = solo };
        var p = new RunScenarioParams(selection.Scenario, party, solo ? null : selection.Strat, selection.Waymark, selection.Seed);
        if (ZoneControl.StartBlockedReason(out var settling) != null && settling != null)
        {
            DiagnosticLog.Info($"[RunController] Start waiting for {settling} to settle.");
            startWait.Hold(settling, () => game.EnterAndStart(p, SessionMode.Local));
            return;
        }
        game.EnterAndStart(p, SessionMode.Local);
    }

    public void Reset()
    {
        startWait.Cancel();
        if (!multiplayer.RequestReset())
            game.Reset();
    }

    public void Leave()
    {
        startWait.Cancel();
        if (!game.World.Map.IsInInstance) return;
        if (!multiplayer.RequestLeaveInstance())
            game.Leave();
    }

    // A session's run plays on the host for everyone, so nobody pauses it alone.
    public string? PauseRefusal => multiplayer.InSession ? "Pause is not available in multiplayer." : game.PauseRefusal;

    public void Pause()
    {
        if (multiplayer.InSession) Game.Print(PauseRefusal!);
        else game.Pause();
    }

    public void Resume() => game.Resume();

    public void TogglePause()
    {
        if (multiplayer.InSession && !game.UserPaused) Game.Print(PauseRefusal!);
        else game.TogglePause();
    }

    // Null when Retry can play the last local run's roll again now: a session's run plays on the host for everyone, and
    // the replay starts as a Start does.
    public string? RetryRefusal
    {
        get
        {
            if (multiplayer.InSession) return RollReplay.MultiplayerRefusal;
            var rolls = Plugin.Practice.Rolls;
            if (rolls.RetryRefusal is { } refusal) return refusal;
            var last = rolls.Last!.AsPlayed;
            // EnterAndStart would refuse another zone's run only after the click.
            if (game.Zone is { } zone && zone != last.Scenario.Phase.Zone)
                return $"Leave {zone.Name} first: the roll plays in {last.Scenario.Phase.Zone.Name}.";
            var party = last.Party as LocalPartyProvider;
            var selection = new ScenarioSelection(last.Scenario, last.SelectedAi, last.SelectedWaymark, last.Seed, party?.RoleOverride);
            return StartRefusal(selection, party?.Solo ?? false);
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
}
