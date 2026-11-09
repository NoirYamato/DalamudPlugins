using System;
using System.Collections.Generic;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game;

// A local run as it played. Fresh is what it was started from, which a new roll of it starts from again; AsPlayed pins the
// seed it played and the seat it gave the player; Settings are its scenario's overrides as it rolled with them.
public sealed record PlayedRun(RunScenarioParams Fresh, RunScenarioParams AsPlayed, string? Settings);

// Retry and Drill: a local run's roll played again, set up as it was. PracticeLoop reports each run's start, and its end
// while Drill is on.
public sealed class RollReplay
{
    public const string MultiplayerRefusal = "Retry is not available in multiplayer.";

    private readonly Game game;
    // Replays asked for and not started yet, by the params each was asked with: RunStarted hands those back.
    private readonly Dictionary<RunScenarioParams, PlayedRun> pending = new(ReferenceEqualityComparer.Instance);
    private bool localRun;
    private bool repeatAsked;

    public RollReplay(Game game) => this.game = game;

    // The last local run as it played.
    public PlayedRun? Last { get; private set; }

    // The run on now plays an earlier run's roll.
    public bool Replaying { get; private set; }

    // A run in which anyone died plays its roll again: once it freezes, or once it ends when godmode kept everyone up.
    // A clean run moves on to a fresh roll.
    public bool Drill { get; set; }

    // Drill will play the roll of the run on now again: someone in it died, or godmode reported a death.
    public bool DrillReplays => Drill && localRun
        && game.Status is SessionStatus.Failed or SessionStatus.Frozen or SessionStatus.RunningWithMistake or SessionStatus.CompletedWithMistake;

    // Null when Retry can play the last local run's roll again.
    public string? RetryRefusal =>
        game.Mode is SessionMode.Host or SessionMode.Peer ? MultiplayerRefusal
        : Last is null ? "No run to retry yet."
        : null;

    // Returns, for a local run, what a fresh roll of it starts from.
    internal RunScenarioParams? Started(RunScenarioParams p, bool local)
    {
        localRun = local;
        repeatAsked = false;
        PlayedRun? replayed = null;
        Replaying = local && pending.Remove(p, out replayed);
        if (!local) return null;
        if (replayed is not null)
        {
            Last = replayed;
            return replayed.Fresh;
        }
        var seat = game.World.Party.PlayerRole;
        if (p.Party is RotatingPartyProvider rotating) rotating.Rotation.Played(seat);
        var solo = p.Party is LocalPartyProvider { Solo: true } or RotatingPartyProvider { Solo: true };
        var asPlayed = p with { Seed = game.World.Rng.Seed, Party = new LocalPartyProvider { RoleOverride = seat, Solo = solo } };
        Last = new PlayedRun(p, asPlayed, ScenarioSettingsSync.Serialize(p.Scenario.SettingsOverrides));
        return p;
    }

    // The last run's roll to start, its settings put back as they were.
    public RunScenarioParams Replay()
    {
        var last = Last ?? throw new InvalidOperationException("No run to replay.");
        DiagnosticLog.Info($"[RollReplay] Playing {last.AsPlayed.Scenario.Name}'s seed {last.AsPlayed.Seed} again, as {last.AsPlayed.Party}, on the settings it played.");
        if (last.Settings is { } settings && last.AsPlayed.Scenario.SettingsOverrides is { } overrides)
            ScenarioSettingsSync.Apply(overrides, settings);
        var p = last.AsPlayed with { };
        pending[p] = last;
        return p;
    }

    // Drill's next run after one that played to its end.
    internal void RestartAfter(bool clean)
    {
        if (!localRun || Last is not { } last) return;
        game.StartScenario(clean ? last.Fresh : Replay());
    }

    // Each frame, after Game's: with Drill on, a frozen run plays its roll again.
    public void Tick()
    {
        if (!Drill || !localRun || repeatAsked || !game.Paused) return;
        repeatAsked = true;
        game.StartScenario(Replay());
    }
}
