namespace MitCue.Game;

/// <summary>
/// Pet actions that carry out a player's command (docs/research/clientstructs.md §2.6). Plans name the
/// player's command; the pet's ActionEffect has the pet as caster and the pet's action id.
/// </summary>
internal static class PetActions
{
    private static readonly Dictionary<uint, uint> ToCommand = new()
    {
        [803] = 16537,   // Whispering Dawn (Eos/Selene) -> Whispering Dawn
        [16550] = 16537, // Angel's Whisper (Seraph) -> Whispering Dawn
        [805] = 16538,   // Fey Illumination (Eos/Selene) -> Fey Illumination
        [16551] = 16538, // Seraphic Illumination (Seraph) -> Fey Illumination
        [16544] = 16543, // Fey Blessing (pet) -> Fey Blessing
        [16547] = 16546, // Consolation (Seraph) -> Consolation
    };

    /// <summary>The player's command for a pet action, or null when the action is not a known pet action.</summary>
    public static uint? CommandFor(uint petActionId) => ToCommand.TryGetValue(petActionId, out var id) ? id : null;
}
