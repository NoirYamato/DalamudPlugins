using MitCue.Core;

namespace MitCue.Runtime;

/// <summary>
/// The jobs in the player's party as a bit mask (bit n set = the job with ClassJob id n is there), cheap to compare
/// every frame: <see cref="SessionManager"/> rebuilds the session when it changes while the co-healer's calls are shown,
/// and <see cref="PartnerSelection"/> then leaves out the plan's columns for jobs nobody in the party plays.
/// </summary>
internal static class PartyJobs
{
    /// <summary>Adds a party member's ClassJob id to <paramref name="mask"/> (classes, crafters and unknown ids are ignored).</summary>
    public static ulong Add(ulong mask, uint classJobId) =>
        classJobId is > 0 and < 64 && Enum.IsDefined(typeof(Job), (byte)classJobId) ? mask | (1UL << (int)classJobId) : mask;

    /// <summary>The mask of these jobs.</summary>
    public static ulong Mask(IEnumerable<Job> jobs) => jobs.Aggregate(0UL, (mask, job) => Add(mask, (uint)job));

    /// <summary>The jobs in a mask, in ClassJob id order (empty for 0: party unknown).</summary>
    public static List<Job> Of(ulong mask)
    {
        var jobs = new List<Job>();
        for (var id = 1; id < 64 && mask >> id != 0; id++)
        {
            if ((mask & (1UL << id)) != 0)
                jobs.Add((Job)(byte)id);
        }

        return jobs;
    }
}
