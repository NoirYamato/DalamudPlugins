using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P6WaveCannon1;

public sealed class TopP6WaveCannon1SettingsWindow
{
    public TopP6WaveCannon1StateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) ResetAll();
        if (SettingsGrid.Begin("##p6wavecannon1"))
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
