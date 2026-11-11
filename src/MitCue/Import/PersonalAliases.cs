using MitCue.Core;
using MitCue.Core.Actions;

namespace MitCue.Import;

/// <summary>
/// Review fixes remembered as personal aliases (DESIGN §3.3 and §6.8): when the player replaces a note the importer
/// could not resolve ("Spreadlo") with the action it stands for, the shorthand is saved in
/// <see cref="Configuration.UserAliases"/> for that job, and later imports resolve it.
/// </summary>
internal static class PersonalAliases
{
    /// <summary>Longest note text saved as an alias: longer text is a sentence, not shorthand.</summary>
    public const int MaxTokenLength = 40;

    /// <summary>The alias "<paramref name="noteText"/> = <paramref name="actionName"/>" for a job, or null when the text is not shorthand.</summary>
    public static UserAliasEntry? From(string? noteText, string actionName, Job job)
    {
        var token = noteText?.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength || AliasDictionary.Normalize(token).Length == 0 || string.IsNullOrWhiteSpace(actionName))
            return null;
        return new UserAliasEntry { Token = token, Actions = [actionName.Trim()], Job = job == Job.None ? null : job.ToString() };
    }

    /// <summary>Adds the alias, or replaces the one with the same shorthand and job. False when it was already there.</summary>
    public static bool Upsert(List<UserAliasEntry> aliases, UserAliasEntry alias)
    {
        var key = AliasDictionary.Normalize(alias.Token);
        var index = aliases.FindIndex(a =>
            a != null && AliasDictionary.Normalize(a.Token) == key && string.Equals(a.Job ?? string.Empty, alias.Job ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            aliases.Add(alias);
            return true;
        }

        var old = aliases[index];
        if (!old.NoteOnly && !old.Sequence && old.Actions.SequenceEqual(alias.Actions, StringComparer.OrdinalIgnoreCase))
            return false;
        aliases[index] = alias;
        return true;
    }

    /// <summary>Removes the alias with the same shorthand and job. False when there was none.</summary>
    public static bool Remove(List<UserAliasEntry> aliases, UserAliasEntry alias)
    {
        var key = AliasDictionary.Normalize(alias.Token);
        return aliases.RemoveAll(a =>
            a != null && AliasDictionary.Normalize(a.Token) == key && string.Equals(a.Job ?? string.Empty, alias.Job ?? string.Empty, StringComparison.OrdinalIgnoreCase)) > 0;
    }
}
