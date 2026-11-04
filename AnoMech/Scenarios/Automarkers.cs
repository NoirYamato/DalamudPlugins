using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios;

// A party-setup option rather than a roll: the fight is the same either way, and only a strat reads it, to
// place signs.
public enum Automarkers { On, Off }

internal static class AutomarkersRow
{
    public static Automarkers Draw(Automarkers current)
    {
        SettingsGrid.Row("Automarkers:");
        if (ImGui.RadioButton("On##automarkers", current == Automarkers.On)) current = Automarkers.On;
        ImGui.SameLine();
        if (ImGui.RadioButton("Off##automarkers", current == Automarkers.Off)) current = Automarkers.Off;
        return current;
    }
}
