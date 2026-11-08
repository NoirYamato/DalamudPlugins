using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top;

// The settings rows for TopP6UnlimitedWaveCannon.Roll, the same in every P6 block that rolls one. A grid showing two
// copies gives each its own `id`, which ends their ImGui ids.
internal static class TopP6UnlimitedWaveCannonRows
{
    private static readonly string[] FirstExaflareLabels = ["Auto", "N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    public static int? FirstExaflare(int? current, string id = "")
    {
        SettingsGrid.Row("First exaflare:");
        var point = (current ?? -1) + 1;
        SettingsGrid.ItemWidth(100);
        if (ImGui.Combo($"##p6firstexaflare{id}", ref point, FirstExaflareLabels, FirstExaflareLabels.Length))
            current = point == 0 ? null : point - 1;
        return current;
    }

    public static bool? Rotation(bool? current, string id = "")
    {
        SettingsGrid.Row("Rotation:");
        if (ImGui.RadioButton($"Auto##p6exaflareturn{id}", current == null)) current = null;
        ImGui.SameLine();
        if (ImGui.RadioButton($"Clockwise##p6exaflareturn{id}", current == true)) current = true;
        ImGui.SameLine();
        if (ImGui.RadioButton($"Counter-clockwise##p6exaflareturn{id}", current == false)) current = false;
        return current;
    }
}
