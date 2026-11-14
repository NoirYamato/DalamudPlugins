using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

public sealed class TeaP1LivingLiquidSettingsWindow
{
    private static readonly string[] PartLabels = ["Protean 1", "Cascade 2"];
    private static readonly string[] TornadoLabels =
        ["Auto", "Open N", "Open NE", "Open E", "Open SE", "Open S", "Open SW", "Open W", "Open NW"];
    private static readonly string[] DrainageLabels = ["Auto", "The tanks", "Others"];

    public TeaP1LivingLiquidStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        if (ImGui.Button("Auto"))
        {
            Overrides.Tornadoes1 = null;
            Overrides.Tornadoes2 = null;
            Overrides.DrainageStartsOn = null;
            if (solo) Overrides.Throttle.Mine = null;
        }
        if (SettingsGrid.Begin("##tealivingliquid"))
        {
            DrawStartFrom();
            SettingsGrid.Section("Protean 1");
            Overrides.Tornadoes1 = Tornadoes(Overrides.Tornadoes1, "##teallltornadoes1");
            DrawDrainage();
            SettingsGrid.Section("Cascade 2");
            Overrides.Tornadoes2 = Tornadoes(Overrides.Tornadoes2, "##teallltornadoes2");
            if (solo) DrawThrottle();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) Overrides.Throttle.Clear();
        if (SettingsGrid.Begin("##tealivingliquidplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##tealllseat", editingSeat);
            DrawThrottle();
            SettingsGrid.ForcedRecapRow("Throttle set:", Overrides.Throttle);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void DrawStartFrom()
    {
        SettingsGrid.Row("Start from:");
        var part = (int)(Overrides.StartFrom ?? TeaP1LivingLiquidParts.Whole.First) - (int)TeaP1LivingLiquidParts.Whole.First;
        SettingsGrid.ItemWidth(140);
        if (ImGui.Combo("##tealllstart", ref part, PartLabels, PartLabels.Length))
            Overrides.StartFrom = part == 0 ? null : (TeaP1LivingLiquidPart)(part + (int)TeaP1LivingLiquidParts.Whole.First);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A start puts the bosses in the middle on the two tanks and the party on its spots for that second of the fight. Cascade 2 starts with no tornado up.");
    }

    private static TeaP1OpenQuarter? Tornadoes(TeaP1OpenQuarter? current, string id)
    {
        SettingsGrid.Row("Tornadoes:");
        var open = current is { } quarter ? (int)quarter + 1 : 0;
        SettingsGrid.ItemWidth(140);
        if (ImGui.Combo(id, ref open, TornadoLabels, TornadoLabels.Length))
            current = open == 0 ? null : (TeaP1OpenQuarter)(open - 1);
        return current;
    }

    private void DrawDrainage()
    {
        SettingsGrid.Row("Drainage tethers start on:");
        var startsOn = Overrides.DrainageStartsOn is { } forced ? (int)forced + 1 : 0;
        SettingsGrid.ItemWidth(140);
        if (ImGui.Combo("##teallldrainage", ref startsOn, DrainageLabels, DrainageLabels.Length))
            Overrides.DrainageStartsOn = startsOn == 0 ? null : (TeaP1DrainageStartsOn)(startsOn - 1);
    }

    private void DrawThrottle()
    {
        var throttle = Overrides.Throttle.Effective(editingSeat);
        SettingsGrid.Row(PerRole.SeatsActive ? "Throttle:" : "Your Throttle:");
        if (ImGui.RadioButton("Auto##tealllthrottle", throttle == null)) Overrides.Throttle.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##tealllthrottle", throttle == true)) Overrides.Throttle.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##tealllthrottle", throttle == false)) Overrides.Throttle.Set(editingSeat, false);
    }
}
