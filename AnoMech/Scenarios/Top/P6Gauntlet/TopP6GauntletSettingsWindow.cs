using System;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Top.P6Gauntlet;

public sealed class TopP6GauntletSettingsWindow
{
    private static readonly Vector4 WarnColor = new(1f, 0.45f, 0.35f, 1f);

    private static readonly string[] PartLabels = ["Cosmo Memory", "Exaflares 1", "Cosmo Arrow 2", "Exaflares 2", "Cosmo Meteor", "Magic Number"];
    private static readonly string[] TankLabels =
        ["Auto", "Cosmo Memory and Magic Number 1", "Cosmo Memory and Magic Number 2", "Magic Number 1", "Magic Number 2"];
    private static readonly string[] MeleeLabels = ["Auto", "First and the enrage", "Second and the enrage", "First only", "Second only"];

    public TopP6GauntletStateOverrides Overrides { get; } = new();

    // Which seat the per-player rows are showing. UI state only; never broadcast.
    private PartyRole editingSeat = PartyRole.MainTank;

    public void Draw()
    {
        var solo = !PerRole.SeatsActive;
        foreach (var problem in TopP6GauntletScenario.ModuleProblems())
            ImGui.TextColored(WarnColor, problem);
        if (ImGui.Button("Auto")) ResetAll(solo);
        if (SettingsGrid.Begin("##p6gauntlet"))
        {
            DrawStartFrom();
            SettingsGrid.Section("Cosmo Memory");
            Overrides.Arrow1InFirst = CosmoArrow(Overrides.Arrow1InFirst, "1");
            SettingsGrid.Section("Exaflares 1");
            Overrides.Exaflares1First = TopP6UnlimitedWaveCannonRows.FirstExaflare(Overrides.Exaflares1First, "1");
            Overrides.Exaflares1Clockwise = TopP6UnlimitedWaveCannonRows.Rotation(Overrides.Exaflares1Clockwise, "1");
            SettingsGrid.Section("Cosmo Arrow 2");
            Overrides.Arrow2InFirst = CosmoArrow(Overrides.Arrow2InFirst, "2");
            SettingsGrid.Section("Exaflares 2");
            Overrides.Exaflares2First = TopP6UnlimitedWaveCannonRows.FirstExaflare(Overrides.Exaflares2First, "2");
            Overrides.Exaflares2Clockwise = TopP6UnlimitedWaveCannonRows.Rotation(Overrides.Exaflares2Clockwise, "2");
            if (solo)
            {
                SettingsGrid.Section("Cosmo Meteor");
                DrawFlare();
            }
            DrawLimitBreaks();
            SettingsGrid.End();
        }
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) Overrides.Flare.Clear();
        if (SettingsGrid.Begin("##p6gauntletplayers"))
        {
            editingSeat = SettingsGrid.SeatRow("##p6gauntletseat", editingSeat);
            DrawFlare();
            SettingsGrid.ForcedRecapRow("Flares set:", Overrides.Flare);
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    private void ResetAll(bool solo)
    {
        Overrides.Arrow1InFirst = null;
        Overrides.Exaflares1First = null;
        Overrides.Exaflares1Clockwise = null;
        Overrides.Arrow2InFirst = null;
        Overrides.Exaflares2First = null;
        Overrides.Exaflares2Clockwise = null;
        if (solo) Overrides.Flare.Mine = null;
        Overrides.TankLimitBreaks = null;
        Overrides.MeleeLimitBreaks = null;
        Overrides.HealerLimitBreaks = null;
    }

    private void DrawStartFrom()
    {
        SettingsGrid.Row("Start from:");
        var part = (int)(Overrides.StartFrom ?? TopP6GauntletPart.CosmoMemory);
        SettingsGrid.ItemWidth(160);
        if (ImGui.Combo("##p6gauntletstart", ref part, PartLabels, PartLabels.Length))
            Overrides.StartFrom = part == 0 ? null : (TopP6GauntletPart)part;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A later start puts Alpha Omega and the party on their spots for the autos, with the gauge and Brilliant or Spark of Dynamis as the run so far would leave them.");
    }

    private static bool? CosmoArrow(bool? current, string id)
    {
        SettingsGrid.Row("Cosmo Arrow:");
        if (ImGui.RadioButton($"Auto##p6gauntletarrow{id}", current == null)) current = null;
        ImGui.SameLine();
        if (ImGui.RadioButton($"In##p6gauntletarrow{id}", current == true)) current = true;
        ImGui.SameLine();
        if (ImGui.RadioButton($"Out##p6gauntletarrow{id}", current == false)) current = false;
        return current;
    }

    private void DrawFlare()
    {
        var f = Overrides.Flare.Effective(editingSeat);
        SettingsGrid.PlayerRow("flare:");
        if (ImGui.RadioButton("Auto##p6gauntletflare", f == null)) Overrides.Flare.Set(editingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##p6gauntletflare", f == true)) Overrides.Flare.Set(editingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##p6gauntletflare", f == false)) Overrides.Flare.Set(editingSeat, false);
    }

    // Only the row of the local player's own role: a job presses limit breaks only for a seat of its role. All three
    // while no player is known.
    private void DrawLimitBreaks()
    {
        var kind = Natives.BattleCharas.LocalPlayer is { Exists: true } player ? TopP6LimitBreaks.KindOf(player.ClassJob) : (TopP6LimitBreakKind?)null;
        if (kind is TopP6LimitBreakKind.PhysicalRanged or TopP6LimitBreakKind.Caster) return;
        SettingsGrid.Section("Limit breaks", "Your own limit breaks 3 when you sit in that role's seat; the other seat of the pair presses the rest. Auto follows LPDU's sheet: tanks and melee by job priority, H1 and H2 by seat.");
        if (kind is null or TopP6LimitBreakKind.Tank)
            Overrides.TankLimitBreaks = Combo("Tank:", "##p6gauntlettank", Overrides.TankLimitBreaks, TankLabels);
        if (kind is null or TopP6LimitBreakKind.Melee)
            Overrides.MeleeLimitBreaks = Combo("Melee:", "##p6gauntletmelee", Overrides.MeleeLimitBreaks, MeleeLabels);
        if (kind is null or TopP6LimitBreakKind.Healer)
            DrawHealer();
    }

    private void DrawHealer()
    {
        var v = Overrides.HealerLimitBreaks;
        SettingsGrid.Row("Healer:");
        if (ImGui.RadioButton("Auto##p6gauntlethealer", v == null)) Overrides.HealerLimitBreaks = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Magic Number 1##p6gauntlethealer", v == TopP6HealerLimitBreaks.MagicNumber1)) Overrides.HealerLimitBreaks = TopP6HealerLimitBreaks.MagicNumber1;
        ImGui.SameLine();
        if (ImGui.RadioButton("Magic Number 2##p6gauntlethealer", v == TopP6HealerLimitBreaks.MagicNumber2)) Overrides.HealerLimitBreaks = TopP6HealerLimitBreaks.MagicNumber2;
    }

    private static T? Combo<T>(string label, string id, T? current, string[] labels) where T : struct, Enum
    {
        SettingsGrid.Row(label);
        var index = current is { } value ? Convert.ToInt32(value) + 1 : 0;
        SettingsGrid.ItemWidth(260);
        if (ImGui.Combo(id, ref index, labels, labels.Length))
            current = index == 0 ? null : (T)Enum.ToObject(typeof(T), index - 1);
        return current;
    }
}
