using System.Numerics;
using AnoMech.Scenarios;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PracticeLoop = AnoMech.Core.Game.PracticeLoop;

namespace AnoMech.Windows;

// Retry and Drill.
public unsafe partial class MainWindow
{
    private static readonly Vector4 RetryColor = new(0.17f, 0.30f, 0.48f, 0.92f);

    // Beside Start, or beside Stop and Pause during a run.
    private void DrawRetryButton()
    {
        var uiScale = ImGuiHelpers.GlobalScale;
        var refusal = plugin.Runs.RetryRefusal;
        ImGui.SameLine();
        ImGui.BeginDisabled(refusal != null);
        if (DrawSemanticButton("Retry this roll###retry", new Vector2(120f * uiScale, 32f * uiScale), RetryColor))
            Retry();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(refusal ?? RetryTooltip());
    }

    private static string RetryTooltip()
        => $"Play the last run's roll of {Plugin.Practice.Rolls.Last!.AsPlayed.Scenario.DisplayName()} again: its seed, your seat and its settings as they were.\n"
           + "A replayed roll doesn't extend the streak. Also /noir retry.";

    // The window shows the scenario the replay plays, as it does for a Start.
    internal void Retry()
    {
        if (plugin.Runs.RetryRefusal is null && Plugin.Practice.Rolls.Last?.AsPlayed.Scenario is { } scenario && scenario != _selectedScenario)
            SelectScenario(scenario);
        plugin.Runs.Retry();
    }

    // Solo-only, like Auto-restart, and forced off in a session so a stale value can't apply.
    private static void DrawDrillOption(PracticeLoop practice, bool mpActive)
    {
        var rolls = practice.Rolls;
        ImGui.SameLine();
        if (mpActive) rolls.Drill = false;
        ImGui.BeginDisabled(mpActive);
        var drill = rolls.Drill;
        if (ImGui.Checkbox("Drill", ref drill)) rolls.Drill = drill;
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(mpActive
                ? MpDisabledReason
                : "After a run in which anyone died, play the same roll again once it freezes; after a clean run, a fresh one.\n"
                  + "A death doesn't turn this off, and a replayed roll doesn't extend the streak.");
    }
}
