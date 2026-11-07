using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P6CosmoDive2;

public sealed class TopP6CosmoDive2SettingsWindow
{
    private static readonly string[] FirstExaflareLabels = ["Auto", "N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    public TopP6CosmoDive2StateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) ResetAll();
        if (SettingsGrid.Begin("##p6cosmodive2"))
        {
            DrawFirstExaflare();
            DrawRotation();
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
        SettingsGrid.Row("First exaflare:");
        var point = (Overrides.FirstExaflare ?? -1) + 1;
        SettingsGrid.ItemWidth(100);
        if (ImGui.Combo("##p6d2first", ref point, FirstExaflareLabels, FirstExaflareLabels.Length))
            Overrides.FirstExaflare = point == 0 ? null : point - 1;
    }

    private void DrawRotation()
    {
        var v = Overrides.Clockwise;
        SettingsGrid.Row("Exaflares turn:");
        if (ImGui.RadioButton("Auto##p6d2turn", v == null)) Overrides.Clockwise = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("CW##p6d2turn", v == true)) Overrides.Clockwise = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("CCW##p6d2turn", v == false)) Overrides.Clockwise = false;
    }
}
