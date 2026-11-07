using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P6CosmoDive2;

public sealed class TopP6CosmoDive2SettingsWindow
{
    public TopP6CosmoDive2StateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) ResetAll();
        if (SettingsGrid.Begin("##p6cosmodive2"))
        {
            Overrides.FirstExaflare = TopP6UnlimitedWaveCannonRows.FirstExaflare(Overrides.FirstExaflare);
            Overrides.Clockwise = TopP6UnlimitedWaveCannonRows.Rotation(Overrides.Clockwise);
            SettingsGrid.End();
        }
    }

    private void ResetAll()
    {
        Overrides.FirstExaflare = null;
        Overrides.Clockwise = null;
    }
}
