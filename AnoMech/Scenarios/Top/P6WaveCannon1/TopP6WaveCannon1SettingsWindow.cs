using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P6WaveCannon1;

public sealed class TopP6WaveCannon1SettingsWindow
{
    private static readonly string[] CompassPoints = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    public TopP6WaveCannon1StateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) ResetAll();
        if (SettingsGrid.Begin("##p6wavecannon1"))
        {
            SettingsGrid.FightOnlyNote();
            DrawFirstExaflare();
            DrawExaflareTurn();
            SettingsGrid.End();
        }
    }

    private void ResetAll()
    {
        Overrides.FirstExaflare = null;
        Overrides.Clockwise = null;
    }

    private void DrawFirstExaflare()
    {
        var v = Overrides.FirstExaflare;
        SettingsGrid.Row("First exaflare:");
        if (ImGui.RadioButton("Auto##firstexaflare", v == null)) Overrides.FirstExaflare = null;
        for (var point = 0; point < CompassPoints.Length; point++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{CompassPoints[point]}##firstexaflare", v == point)) Overrides.FirstExaflare = point;
        }
    }

    private void DrawExaflareTurn()
    {
        var v = Overrides.Clockwise;
        SettingsGrid.Row("Rotation:");
        if (ImGui.RadioButton("Auto##exaflareturn",              v == null))  Overrides.Clockwise = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Clockwise##exaflareturn",         v == true))  Overrides.Clockwise = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Counter-clockwise##exaflareturn", v == false)) Overrides.Clockwise = false;
    }
}
