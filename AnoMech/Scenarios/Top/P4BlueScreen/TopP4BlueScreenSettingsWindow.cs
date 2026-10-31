using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P4BlueScreen;

public sealed class TopP4BlueScreenSettingsWindow
{
    public TopP4BlueScreenStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    // Every setting is per player. Solo, the player's own picks sit in this panel.
    public void Draw()
    {
        if (PerRole.SeatsActive)
        {
            ImGui.TextDisabled("Everything here is per player -- use the button below.");
            return;
        }
        if (ImGui.Button("Auto")) ResetMine();
        if (SettingsGrid.Begin("##p4bluescreen"))
        {
            DrawPlayerRows();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) ResetPerPlayer();
        if (SettingsGrid.Begin("##p4bluescreenplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##p4bluescreenseat", editingSeat);
            DrawPlayerRows();
            for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
                SettingsGrid.ForcedRecapRow($"Set {set + 1} markers set:", Overrides.MarkerInSet(set));
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void DrawPlayerRows()
    {
        for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
            DrawMarker(set);
    }

    private void ResetPerPlayer()
    {
        for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
            Overrides.MarkerInSet(set).Clear();
    }

    // Solo's own picks only: a host's seat assignments are kept apart.
    private void ResetMine()
    {
        for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
            Overrides.MarkerInSet(set).Mine = null;
    }

    private void DrawMarker(int set)
    {
        var marker = Overrides.MarkerInSet(set);
        var m = marker.Effective(editingSeat);
        var id = $"##p4marker{set}";
        SettingsGrid.PlayerRow($"marker in set {set + 1}:");
        if (ImGui.RadioButton($"Auto{id}", m == null))  marker.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton($"Yes{id}",  m == true))  marker.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton($"No{id}",   m == false)) marker.Set(editingSeat, false);
    }
}
