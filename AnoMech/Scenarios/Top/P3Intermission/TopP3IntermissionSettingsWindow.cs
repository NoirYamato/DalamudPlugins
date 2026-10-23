using System;
using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P3Intermission;

public sealed class TopP3IntermissionSettingsWindow
{
    public TopP3IntermissionStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        if (ImGui.Button("Auto"))
        {
            Overrides.FirstArms = null;
            if (solo) Overrides.Debuff.Mine = null;
        }
        if (SettingsGrid.Begin("##p3intermission"))
        {
            DrawFirstArms();
            DrawAutomarkers();
            if (solo) DrawDebuff();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) Overrides.Debuff.Clear();
        if (SettingsGrid.Begin("##p3intermissionplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##p3intermissionseat", editingSeat);
            DrawDebuff();
            SettingsGrid.ForcedRecapRow("Debuffs set:", Overrides.Debuff);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void DrawFirstArms()
    {
        var v = Overrides.FirstArms;
        SettingsGrid.Row("First arms:");
        if (ImGui.RadioButton("Auto##firstarms", v == null)) Overrides.FirstArms = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("North triangle##firstarms", v == FirstArms.NorthTriangle)) Overrides.FirstArms = FirstArms.NorthTriangle;
        ImGui.SameLine();
        if (ImGui.RadioButton("South triangle##firstarms", v == FirstArms.SouthTriangle)) Overrides.FirstArms = FirstArms.SouthTriangle;
    }

    private void DrawAutomarkers()
    {
        var v = Overrides.Automarkers;
        SettingsGrid.Row("Automarkers:");
        if (ImGui.RadioButton("On##automarkers", v == Automarkers.On)) Overrides.Automarkers = Automarkers.On;
        ImGui.SameLine();
        if (ImGui.RadioButton("Off##automarkers", v == Automarkers.Off)) Overrides.Automarkers = Automarkers.Off;
    }

    private void DrawDebuff()
    {
        var v = Overrides.Debuff.Effective(editingSeat);
        SettingsGrid.PlayerRow("debuff:");
        if (ImGui.RadioButton("Auto##sniperdebuff", v == null)) Overrides.Debuff.Set(editingSeat, null);
        foreach (var debuff in Enum.GetValues<SniperDebuff>())
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{debuff}##sniperdebuff", v == debuff)) Overrides.Debuff.Set(editingSeat, debuff);
        }
    }
}
