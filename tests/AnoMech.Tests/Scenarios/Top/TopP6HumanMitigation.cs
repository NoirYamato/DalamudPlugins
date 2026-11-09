using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Tests.TopP6TankHit;

namespace AnoMech.Tests;

// The hits of P6 a tank's own mitigation is checked at, in the order they come.
public enum TopP6TankHit { CosmoDive1, WildCharge1, WildCharge2, CosmoDive2 }

// The human tank pressing its own mitigation as its client does: each action's resolve through IUserActions.Acted, then
// what the client's module does with it, the action's JobActions statuses. A dead player presses nothing. The actions
// are the ones TOP's level 90 sync leaves.
internal static class TopP6HumanMitigation
{
    public const uint Rampart = 7531;
    public const uint ThrillOfBattle = 40, Bloodwhetting = 25751, Holmgang = 43, Vengeance = 44;
    public const uint DarkMind = 3634, Oblation = 25754, TheBlackestNight = 7393, LivingDead = 3638, ShadowWall = 3636;
    public const uint Camouflage = 16140, HeartOfCorundum = 25758, Nebula = 16148, Superbolide = 16152;
    public const uint Bulwark = 22, HolySheltron = 25746, Sentinel = 17, HallowedGround = 30;

    public static readonly JobId[] TankJobs = [JobId.Warrior, JobId.DarkKnight, JobId.Gunbreaker, JobId.Paladin];

    // Each job's own sets at the hit in LPDU's plan, the first the one a human suite presses. A dark knight takes Living
    // Dead at one of Cosmo Dive 2 and Wild Charge 2, by its partner's job, and its Rampart set at the other.
    public static IReadOnlyList<uint[]> LpduSets(JobId job, TopP6TankHit hit) => (job, hit) switch
    {
        (JobId.Warrior, CosmoDive1 or CosmoDive2) => [[Rampart, ThrillOfBattle, Bloodwhetting]],
        (JobId.Warrior, WildCharge1) => [[Holmgang]],
        (JobId.Warrior, WildCharge2) => [[Vengeance, Bloodwhetting]],
        (JobId.DarkKnight, CosmoDive1) => [[Rampart, DarkMind, Oblation, TheBlackestNight]],
        (JobId.DarkKnight, WildCharge1) => [[ShadowWall, Oblation, TheBlackestNight]],
        (JobId.DarkKnight, _) => [[Rampart, DarkMind, Oblation, TheBlackestNight], [LivingDead]],
        (JobId.Gunbreaker, CosmoDive1 or CosmoDive2) => [[Rampart, Camouflage, HeartOfCorundum]],
        (JobId.Gunbreaker, WildCharge1) => [[Nebula, HeartOfCorundum]],
        (JobId.Gunbreaker, WildCharge2) => [[Superbolide]],
        (JobId.Paladin, CosmoDive1 or CosmoDive2) => [[Rampart, Bulwark, HolySheltron]],
        (JobId.Paladin, _) => [[Sentinel, HolySheltron], [HallowedGround]],
        _ => throw new ArgumentOutOfRangeException(nameof(job), job, "not a tank job"),
    };

    public static uint[] LpduSet(JobId job, TopP6TankHit hit) => LpduSets(job, hit)[0];

    public static bool IsCosmoDive(TopP6TankHit hit) => hit is CosmoDive1 or CosmoDive2;

    public static void Press(SimWorld world, float at, IReadOnlyCollection<uint> actionIds) => world.Events.Add(at, () => PressNow(world, actionIds));

    // A human suite's tank, whose job is its seat's own.
    public static void PressTheSeatsLpduSetNow(SimWorld world, PartyRole seat, TopP6TankHit hit)
        => PressNow(world, LpduSet((JobId)PartyPresets.Standard[(int)seat].ClassJob, hit));

    public static void PressNow(SimWorld world, IReadOnlyCollection<uint> actionIds)
    {
        if (world.Party.Player is not { } player || !player.IsAlive()) return;
        var userActions = (FakeUserActions)Natives.UserActions;
        var self = (ulong)player.GameObjectId;
        foreach (var actionId in actionIds)
        {
            userActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, actionId, self));
            JobActions.ApplyEffects(player, actionId, self, new Random(1));
        }
    }

    // For test names, which are built before any game data is installed.
    public static string Describe(IEnumerable<uint> actionIds) => string.Join(" + ", actionIds.Select(id => Names[id]));

    private static readonly Dictionary<uint, string> Names = new()
    {
        [Rampart] = "Rampart",
        [ThrillOfBattle] = "Thrill of Battle", [Bloodwhetting] = "Bloodwhetting", [Holmgang] = "Holmgang", [Vengeance] = "Vengeance",
        [DarkMind] = "Dark Mind", [Oblation] = "Oblation", [TheBlackestNight] = "The Blackest Night", [LivingDead] = "Living Dead",
        [ShadowWall] = "Shadow Wall",
        [Camouflage] = "Camouflage", [HeartOfCorundum] = "Heart of Corundum", [Nebula] = "Nebula", [Superbolide] = "Superbolide",
        [Bulwark] = "Bulwark", [HolySheltron] = "Holy Sheltron", [Sentinel] = "Sentinel", [HallowedGround] = "Hallowed Ground",
    };
}
