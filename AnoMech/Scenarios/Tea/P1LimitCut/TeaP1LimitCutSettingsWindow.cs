using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Tea.P1LimitCut;

public sealed class TeaP1LimitCutSettingsWindow
{
    public TeaP1LimitCutStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    private static readonly string[] FirstBlasterLabels = ["Auto", "W-E", "NW-SE", "N-S", "NE-SW"];
    private static readonly string[] NumberLabels = ["Auto", "1", "2", "3", "4", "5", "6", "7", "8"];

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        if (ImGui.Button("Auto"))
        {
            Overrides.FirstBlasters = null;
            if (solo) Overrides.Number.Mine = null;
        }
        if (SettingsGrid.Begin("##tealimitcut"))
        {
            SettingsGrid.Row("First blasters:");
            var first = Overrides.FirstBlasters is { } axis ? (int)axis + 1 : 0;
            SettingsGrid.ItemWidth(140);
            if (ImGui.Combo("##tealcfirstblasters", ref first, FirstBlasterLabels, FirstBlasterLabels.Length))
                Overrides.FirstBlasters = first == 0 ? null : (TeaP1FirstBlasters)(first - 1);

            if (solo) DrawNumber();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) Overrides.Number.Clear();
        if (SettingsGrid.Begin("##tealimitcutplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##tealcseat", editingSeat);
            DrawNumber();
            SettingsGrid.ForcedRecapRow("Numbers set:", Overrides.Number);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void DrawNumber()
    {
        SettingsGrid.Row(PerRole.SeatsActive ? "Number:" : "Your number:");
        var number = Overrides.Number.Effective(editingSeat) ?? 0;
        SettingsGrid.ItemWidth(140);
        if (ImGui.Combo("##tealcnumber", ref number, NumberLabels, NumberLabels.Length))
            Overrides.Number.Set(editingSeat, number == 0 ? null : number);
    }
}
