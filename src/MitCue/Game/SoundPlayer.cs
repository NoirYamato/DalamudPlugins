using FFXIVClientStructs.FFXIV.Client.UI;
using MitCue.Util;

namespace MitCue.Game;

/// <summary>Plays the game's chat sound effects (&lt;se.1&gt;..&lt;se.16&gt;). Framework thread only.</summary>
internal static unsafe class SoundPlayer
{
    /// <summary>Plays &lt;se.N&gt; via <c>UIGlobals.PlaySoundEffect(36 + N)</c> (clientstructs.md §5).</summary>
    /// <returns>True when the sound was requested from the game.</returns>
    public static bool Play(int effect, ErrorReporter errors)
    {
        if (effect is < Configuration.MinSoundEffect or > Configuration.MaxSoundEffect)
            return false;

        try
        {
            if (UIGlobals.Addresses.PlaySoundEffect.Value == 0)
            {
                errors.Warn("sound", "Playing the reminder sound", "UIGlobals.PlaySoundEffect did not resolve; sounds are off.");
                return false;
            }

            UIGlobals.PlaySoundEffect((uint)(36 + effect));
            return true;
        }
        catch (Exception ex)
        {
            errors.Report("sound", "Playing the reminder sound", ex);
            return false;
        }
    }
}
