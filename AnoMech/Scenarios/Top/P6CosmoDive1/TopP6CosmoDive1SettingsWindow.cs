using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P6CosmoDive1;

public sealed class TopP6CosmoDive1SettingsWindow
{
    public TopP6CosmoDive1StateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) Overrides.InFirst = null;
        if (SettingsGrid.Begin("##p6cosmodive1"))
        {
            DrawCosmoArrow();
            SettingsGrid.End();
        }
    }

    private void DrawCosmoArrow()
    {
        var v = Overrides.InFirst;
        SettingsGrid.Row("Cosmo Arrow:");
        if (ImGui.RadioButton("Auto##p6cosmodive1arrow", v == null))  Overrides.InFirst = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("In##p6cosmodive1arrow",   v == true))  Overrides.InFirst = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Out##p6cosmodive1arrow",  v == false)) Overrides.InFirst = false;
    }
}
