using System;
using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

public sealed class TopP3HelloWorldSettingsWindow
{
    public TopP3HelloWorldStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        if (ImGui.Button("Auto"))
        {
            ResetFight();
            if (solo) Overrides.Kit.Mine = null;
        }
        if (SettingsGrid.Begin("##p3helloworld"))
        {
            DrawDefamationColour();
            DrawTowerLayout();
            if (solo) DrawKit();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) Overrides.Kit.Clear();
        if (SettingsGrid.Begin("##p3helloworldplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##p3helloworldseat", editingSeat);
            DrawKit();
            SettingsGrid.ForcedRecapRow("Kits set:", Overrides.Kit);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void ResetFight()
    {
        Overrides.DefamationColour = null;
        Overrides.SameLayoutEverySet = null;
    }

    private void DrawDefamationColour()
    {
        var v = Overrides.DefamationColour;
        SettingsGrid.Row("Defamation rot:");
        if (ImGui.RadioButton("Auto##defamationrot", v == null)) Overrides.DefamationColour = null;
        foreach (var colour in Enum.GetValues<RotColour>())
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{colour}##defamationrot", v == colour)) Overrides.DefamationColour = colour;
        }
    }

    private void DrawTowerLayout()
    {
        var same = Overrides.SameLayoutEverySet == true;
        SettingsGrid.Row("Towers:");
        if (ImGui.RadioButton("Move every set##towers", !same)) Overrides.SameLayoutEverySet = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Same every set##towers", same)) Overrides.SameLayoutEverySet = true;
    }

    private void DrawKit()
    {
        var v = Overrides.Kit.Effective(editingSeat);
        SettingsGrid.PlayerRow("kit:");
        if (ImGui.RadioButton("Auto##kit", v == null)) Overrides.Kit.Set(editingSeat, null);
        foreach (var kit in Enum.GetValues<HelloWorldKit>())
        {
            ImGui.SameLine();
            var label = char.ToUpperInvariant(TopP3HelloWorldStateOverrides.KitName(kit)[0]) + TopP3HelloWorldStateOverrides.KitName(kit)[1..];
            if (ImGui.RadioButton($"{label}##kit", v == kit)) Overrides.Kit.Set(editingSeat, kit);
        }
    }
}
