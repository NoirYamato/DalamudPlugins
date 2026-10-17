using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P3Monitors;

public sealed class TopP3MonitorsSettingsWindow
{
    public TopP3MonitorsStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        if (ImGui.Button("Auto"))
        {
            Overrides.OmegaMonitor = null;
            if (solo) ResetMine();
        }
        if (SettingsGrid.Begin("##p3monitors"))
        {
            DrawOmegaMonitor();
            if (solo) DrawPlayerRows();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) ResetPerPlayer();
        if (SettingsGrid.Begin("##p3monitorsplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##p3monitorsseat", editingSeat);
            DrawPlayerRows();
            SettingsGrid.ForcedRecapRow("Monitors set:", Overrides.Monitor);
            SettingsGrid.ForcedRecapRow("Sides set:", Overrides.Side);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void DrawPlayerRows()
    {
        DrawMonitor();
        var noMonitor = Overrides.Monitor.Effective(editingSeat) == false;
        if (noMonitor) ImGui.BeginDisabled();
        DrawSide();
        if (noMonitor) ImGui.EndDisabled();
    }

    private void ResetPerPlayer()
    {
        Overrides.Monitor.Clear();
        Overrides.Side.Clear();
    }

    // Solo's own picks only: a host's seat assignments are kept apart.
    private void ResetMine()
    {
        Overrides.Monitor.Mine = null;
        Overrides.Side.Mine = null;
    }

    private void DrawOmegaMonitor()
    {
        var v = Overrides.OmegaMonitor;
        SettingsGrid.Row("Omega's monitor:");
        if (ImGui.RadioButton("Auto##omegamon", v == null))              Overrides.OmegaMonitor = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("West##omegamon", v == OmegaMonitor.West)) Overrides.OmegaMonitor = OmegaMonitor.West;
        ImGui.SameLine();
        if (ImGui.RadioButton("East##omegamon", v == OmegaMonitor.East)) Overrides.OmegaMonitor = OmegaMonitor.East;
    }

    private void DrawMonitor()
    {
        var m = Overrides.Monitor.Effective(editingSeat);
        SettingsGrid.PlayerRow("monitor:");
        if (ImGui.RadioButton("Auto##p3mon", m == null))  Overrides.Monitor.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##p3mon",  m == true))  Overrides.Monitor.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##p3mon",   m == false)) Overrides.Monitor.Set(editingSeat, false);
    }

    private void DrawSide()
    {
        var s = Overrides.Side.Effective(editingSeat);
        SettingsGrid.PlayerRow("monitor side:");
        if (ImGui.RadioButton("Auto##p3side",  s == null))              Overrides.Side.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Left##p3side",  s == MonitorSide.Left))  Overrides.Side.Set(editingSeat, MonitorSide.Left);
        ImGui.SameLine();
        if (ImGui.RadioButton("Right##p3side", s == MonitorSide.Right)) Overrides.Side.Set(editingSeat, MonitorSide.Right);
    }
}
