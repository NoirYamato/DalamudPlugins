using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P6CosmoMeteor;

public sealed class TopP6CosmoMeteorSettingsWindow
{
    public TopP6CosmoMeteorStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        if (ImGui.Button("Auto") && solo) Overrides.Flare.Mine = null;
        if (SettingsGrid.Begin("##p6cosmometeor"))
        {
            if (solo) DrawFlare();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) Overrides.Flare.Clear();
        if (SettingsGrid.Begin("##p6cosmometeorplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##p6cosmometeorseat", editingSeat);
            DrawFlare();
            SettingsGrid.ForcedRecapRow("Flares set:", Overrides.Flare);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void DrawFlare()
    {
        var f = Overrides.Flare.Effective(editingSeat);
        SettingsGrid.PlayerRow("flare:");
        if (ImGui.RadioButton("Auto##p6flare", f == null))  Overrides.Flare.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##p6flare",  f == true))  Overrides.Flare.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##p6flare",   f == false)) Overrides.Flare.Set(editingSeat, false);
    }
}
