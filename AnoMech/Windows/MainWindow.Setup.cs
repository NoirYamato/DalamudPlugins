using System;
using System.Collections.Generic;
using System.IO;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Windows;

// The seat the Role pick gives, and the setup SetupMemory keeps across game restarts.
public unsafe partial class MainWindow
{
    private const int RotateRoleLabel = 1;
    private const int FirstSeatRoleLabel = 2;
    private const long SettingsCheckMs = 500;

    private const string RotateHelp =
        "Rotate swaps tanks between MT and OT and melee between M1 and M2 every run; a replayed roll keeps its seat.";

    // The Role pick Rotate: RunController.Seats gives each run's seat.
    private bool _rotateSeats;

    private SetupMemory _setup = new();
    private string? _setupPath;
    private long _nextSettingsCheck;

    // The Role pick as an index into RoleLabels.
    private int RolePick
    {
        get => _rotateSeats ? RotateRoleLabel : _roleOverride is { } role ? (int)role + FirstSeatRoleLabel : 0;
        set
        {
            _rotateSeats = value == RotateRoleLabel;
            _roleOverride = value >= FirstSeatRoleLabel && value < RoleLabels.Length ? (PartyRole)(value - FirstSeatRoleLabel) : null;
        }
    }

    // The seat the next Start gives the player on their current job; null with no character.
    private PartyRole? NextSeat()
    {
        var player = Natives.BattleCharas.LocalPlayer;
        if (!player.Exists) return null;
        if (_rotateSeats) return plugin.Runs.Seats.Next(player.ClassJob);
        return _roleOverride ?? PartyPresets.SkipRoleForJob(player.ClassJob);
    }

    // During a run its seat, and with Rotate the next fresh roll's unless Drill will play this roll again.
    private void DrawSeatLabel()
    {
        var game = Plugin.Game;
        if (game.ActiveScenario is not null && game.World.Party.Player is not null)
        {
            var seat = game.World.Party.PlayerRole;
            var next = _rotateSeats && !Plugin.Practice.Rolls.DrillReplays ? NextSeat() : null;
            ImGui.SameLine();
            ImGui.TextDisabled(next is { } after && after != seat
                ? $"You: {SettingsGrid.RoleLabel(seat)} (next {SettingsGrid.RoleLabel(after)})"
                : $"You: {SettingsGrid.RoleLabel(seat)}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_rotateSeats ? $"Your seat in this run, then in the next fresh roll.\n{RotateHelp}" : "Your seat in this run.");
            return;
        }
        if (NextSeat() is not { } nextSeat) return;
        ImGui.SameLine();
        ImGui.TextDisabled($"You: {SettingsGrid.RoleLabel(nextSeat)}");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_rotateSeats
                ? $"Your seat in the next Start. Retry plays the seat its run gave you.\n{RotateHelp}"
                : "Your seat in the next Start. Retry plays the seat its run gave you.");
    }

    // At plugin load, before the last scenario is selected again.
    private void RestoreSetup()
    {
        try
        {
            _setupPath = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), SetupMemory.FileName);
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[MainWindow] No config folder for {SetupMemory.FileName}, so the setup isn't kept: {e.Message}");
        }
        _setup = SetupMemory.Load(_setupPath);
        RestoreRolePick();
        foreach (var scenario in Plugin.Game.Catalog.Scenarios)
        {
            if (_setup.Find(scenario) is not { } kept) continue;
            if (kept.Region is { } region) _stratGroupMemory[scenario] = region;
            kept.RestoreSettings(scenario);
        }
    }

    private void RestoreRolePick() => RolePick = Math.Max(0, Array.IndexOf(RoleLabels, _setup.Role));

    // A strat the scenario no longer has in the region leaves the region's first.
    private void RestoreStratPick(IScenario scenario)
    {
        if (_setup.Find(scenario)?.StratIndex(scenario, _selectedStratGroup) is { } index) _selectedStrat = index;
    }

    private string? SelectedStratName =>
        _selectedScenario is { AiStrats: var strats } && _selectedStrat >= 0 && _selectedStrat < strats.Count ? strats[_selectedStrat].Name : null;

    private SessionMode SetupSession => !InSession ? SessionMode.Local : IsGuest ? SessionMode.Peer : SessionMode.Host;

    // Every frame, and before the selected scenario changes, so a setting changed just before is kept for the right one.
    private void RememberSetup(bool checkSettingsNow = false)
    {
        if (_setup.Track(SetupSession) is { } ended) TakeBackFromSession(ended);
        var now = Environment.TickCount64;
        var checkSettings = checkSettingsNow || now >= _nextSettingsCheck;
        if (checkSettings) _nextSettingsCheck = now + SettingsCheckMs;
        if (_setup.Remember(RoleLabels[RolePick], _selectedScenario, _selectedStratGroup, SelectedStratName, checkSettings))
            _setup.Save(_setupPath);
    }

    // What a session overrode comes back as kept: the Role pick, and a guest's region and strat. A guest's own settings
    // are back from MultiplayerManager, and one changed just before the session may not have been read, so every kept
    // scenario's settings are read again.
    private void TakeBackFromSession(SessionMode ended)
    {
        RestoreRolePick();
        if (ended == SessionMode.Peer && _selectedScenario is { } selected)
        {
            _selectedStratGroup = _stratGroupMemory.GetValueOrDefault(selected);
            ReconcileStrat();
            RestoreStratPick(selected);
        }
        RememberEverySetting();
    }

    private void RememberEverySetting()
    {
        if (_setup.RememberSettings(Plugin.Game.Catalog.Scenarios)) _setup.Save(_setupPath);
    }

    private void KeepSetupOnUnload()
    {
        RememberSetup(checkSettingsNow: true);
        RememberEverySetting();
    }
}
