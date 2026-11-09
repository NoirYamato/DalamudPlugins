using System.Reflection;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Delta;
using AnoMech.Scenarios.Top.P6Gauntlet;

namespace AnoMech.Tests;

// The main window's setup across game restarts: the Role pick, the seat Rotate last gave, and per scenario its region, its
// strat by name and its settings, in a file of its own.
public class SetupMemoryTests
{
    private string path = "";

    [SetUp]
    public void PickAFile() => path = Path.Combine(Path.GetTempPath(), $"noirmech-setup-{Guid.NewGuid():N}.json");

    [TearDown]
    public void RemoveTheFile() => File.Delete(path);

    // Start from, the limit break rows and a forced flare come back; the internal test pins don't.
    [Test]
    public void TheGauntletsSetupComesBackAfterARestart()
    {
        var scenario = new TopP6GauntletScenario();
        var overrides = (TopP6GauntletStateOverrides)scenario.SettingsOverrides;
        overrides.StartFrom = TopP6GauntletPart.CosmoMeteor;
        overrides.TankLimitBreaks = TopP6TankLimitBreaks.MagicNumber2;
        overrides.MeleeLimitBreaks = TopP6MeleeLimitBreaks.SecondAndTheEnrage;
        overrides.Flare.Mine = true;
        overrides.Parts = (TopP6GauntletPart.CosmoMeteor, TopP6GauntletPart.CosmoMeteor);
        overrides.PlayerPressesItsOwnLimitBreaks = true;
        var memory = new SetupMemory();
        memory.Remember("Rotate", scenario, scenario.AiStrats[0].Group, scenario.AiStrats[0].Name, checkSettings: true);
        memory.Save(path);

        var restarted = new TopP6GauntletScenario();
        var loaded = SetupMemory.Load(path);
        loaded.Find(restarted)!.RestoreSettings(restarted);

        var back = (TopP6GauntletStateOverrides)restarted.SettingsOverrides;
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Role, Is.EqualTo("Rotate"));
            Assert.That(loaded.Find(restarted)!.StratIndex(restarted, "EU"), Is.Zero);
            Assert.That(back.StartFrom, Is.EqualTo(TopP6GauntletPart.CosmoMeteor));
            Assert.That(back.TankLimitBreaks, Is.EqualTo(TopP6TankLimitBreaks.MagicNumber2));
            Assert.That(back.MeleeLimitBreaks, Is.EqualTo(TopP6MeleeLimitBreaks.SecondAndTheEnrage));
            Assert.That(back.Flare.Mine, Is.True);
            Assert.That(back.Parts, Is.Null);
            Assert.That(back.PlayerPressesItsOwnLimitBreaks, Is.False);
        });
    }

    // Every scenario's settings, each set away from its default, are the same after a save and a load.
    [Test]
    public void EveryScenariosSettingsComeBackAsTheyWere()
    {
        var before = ScenarioCatalog.Create().Scenarios;
        var after = ScenarioCatalog.Create().Scenarios;
        var memory = new SetupMemory();
        foreach (var scenario in before)
        {
            if (scenario.SettingsOverrides is { } overrides) MoveOffTheDefaults(overrides);
            memory.Remember(SetupMemory.AutoRole, scenario, null, null, checkSettings: true);
        }
        memory.Save(path);
        var loaded = SetupMemory.Load(path);

        Assert.Multiple(() =>
        {
            foreach (var (saved, restarted) in before.Zip(after))
            {
                loaded.Find(restarted)!.RestoreSettings(restarted);
                Assert.That(ScenarioSettingsSync.Serialize(restarted.SettingsOverrides), Is.EqualTo(ScenarioSettingsSync.Serialize(saved.SettingsOverrides)),
                    saved.FullName());
            }
        });
    }

    // Region buttons pick among the strats, and the name picks inside the region: P5 Delta's EU strat is first, NA's
    // "Standard" second.
    [TestCase("EU", "[LPDU] Light PF", 0)]
    [TestCase("NA", "Standard", 1)]
    [TestCase("EU", "Standard", null)]
    [TestCase("NA", "A strat a later build dropped", null)]
    public void AStratComesBackByNameInItsRegion(string region, string strat, int? index)
        => Assert.That(new ScenarioSetup { Region = region, Strat = strat }.StratIndex(new TopP5DeltaScenario(), region), Is.EqualTo(index));

    [TestCase(null)]
    [TestCase("{ this is not json")]
    [TestCase("{\"Role\":null,\"Scenarios\":null}")]
    [TestCase("{\"RotateSeat\":\"Rotate\"}")]
    public void AMissingOrUnreadableFileGivesTheDefaults(string? text)
    {
        if (text is not null) File.WriteAllText(path, text);
        var rotation = new SeatRotation();

        var loaded = SetupMemory.Load(path);
        loaded.RestoreRotation(rotation);

        Assert.That(loaded.Role, Is.EqualTo(SetupMemory.AutoRole));
        Assert.That(loaded.Scenarios, Is.Empty);
        Assert.That(rotation.Last, Is.Null);
    }

    // After a restart the role of the seat Rotate last gave goes on alternating from it, on any job of the role: a WAR played
    // MT, so a WAR or a DRK gets OT, where Auto would give MT. The other role starts from the seat Auto gives, though it had
    // a turn before: a DRG played M2 gets M2.
    [Test]
    public void RotateGoesOnFromTheSeatItLastGave()
    {
        var rotation = new SeatRotation();
        rotation.Played(rotation.Next((uint)JobId.Dragoon));
        rotation.Played(rotation.Next((uint)JobId.Warrior));
        var memory = new SetupMemory();
        memory.RememberRotation(rotation);
        memory.Save(path);

        var restarted = new SeatRotation();
        SetupMemory.Load(path).RestoreRotation(restarted);

        Assert.Multiple(() =>
        {
            Assert.That(rotation.Next((uint)JobId.Dragoon), Is.EqualTo(PartyRole.MeleeDpsA), "without the restart");
            Assert.That(restarted.Next((uint)JobId.Warrior), Is.EqualTo(PartyRole.OffTank));
            Assert.That(restarted.Next((uint)JobId.DarkKnight), Is.EqualTo(PartyRole.OffTank));
            Assert.That(restarted.Next((uint)JobId.Dragoon), Is.EqualTo(PartyRole.MeleeDpsB));
            Assert.That(restarted.Next((uint)JobId.WhiteMage), Is.EqualTo(PartyRole.RegenHealer));
        });
    }

    [Test]
    public void OnlyANewSeatOfTheRotationIsSaved()
    {
        var rotation = new SeatRotation();
        var memory = new SetupMemory();

        Assert.Multiple(() =>
        {
            Assert.That(memory.RememberRotation(rotation), Is.False, "no seat given yet");
            rotation.Played(PartyRole.OffTank);
            Assert.That(memory.RememberRotation(rotation), Is.True, "a seat given");
            Assert.That(memory.RememberRotation(rotation), Is.False, "nothing changed");
            rotation.Played(PartyRole.RegenHealer);
            Assert.That(memory.RememberRotation(rotation), Is.False, "a healer's seat");
            Assert.That(memory.RotateSeat, Is.EqualTo("OT"));
        });
    }

    [Test]
    public void OnlyAChangeIsSaved()
    {
        var scenario = new TopP6GauntletScenario();
        var overrides = (TopP6GauntletStateOverrides)scenario.SettingsOverrides;
        var memory = new SetupMemory();

        Assert.Multiple(() =>
        {
            Assert.That(memory.Remember("Auto", scenario, null, "x", checkSettings: true), Is.True, "a scenario first seen");
            Assert.That(memory.Remember("Auto", scenario, null, "x", checkSettings: true), Is.False, "nothing changed");
            Assert.That(memory.Remember("MT", null, null, null, checkSettings: true), Is.True, "the Role pick");
            Assert.That(memory.Remember("MT", scenario, null, "y", checkSettings: false), Is.True, "the strat");
            overrides.StartFrom = TopP6GauntletPart.MagicNumber;
            Assert.That(memory.Remember("MT", scenario, null, "y", checkSettings: false), Is.False, "settings left unread");
            Assert.That(memory.Remember("MT", scenario, null, "y", checkSettings: true), Is.True, "the settings");
        });
    }

    // A session seats everyone itself: the Role pick it forces isn't kept, and the window hears once that it ended, to put
    // the kept one back.
    [TestCase(SessionMode.Host)]
    [TestCase(SessionMode.Peer)]
    public void ASessionsRolePickIsNotKept(SessionMode session)
    {
        var memory = new SetupMemory();
        memory.Remember("Rotate", null, null, null, checkSettings: true);

        Assert.Multiple(() =>
        {
            Assert.That(memory.Track(session), Is.Null, "a session begins");
            Assert.That(memory.Remember(SetupMemory.AutoRole, null, null, null, checkSettings: true), Is.False, "the session's pick");
            Assert.That(memory.Role, Is.EqualTo("Rotate"));
            Assert.That(memory.Track(session), Is.Null, "the session goes on");
            Assert.That(memory.Track(SessionMode.Local), Is.EqualTo(session), "the session ends");
            Assert.That(memory.Track(SessionMode.Local), Is.Null, "said once");
            Assert.That(memory.Remember("Rotate", null, null, null, checkSettings: true), Is.False, "the kept pick back");
        });
    }

    // A host picks its own strat and settings in a session, and keeps them.
    [Test]
    public void AHostsPicksInASessionAreKept()
    {
        var scenario = new TopP5DeltaScenario();
        var memory = new SetupMemory();
        memory.Remember("MT", scenario, "EU", "[LPDU] Light PF", checkSettings: true);
        memory.Track(SessionMode.Host);

        ((TopP5DeltaStateOverrides)scenario.SettingsOverrides).Automarkers = Automarkers.Off;

        Assert.That(memory.Remember(SetupMemory.AutoRole, scenario, "NA", "Standard", checkSettings: true), Is.True);
        Assert.That(memory.Find(scenario)!.Strat, Is.EqualTo("Standard"));
        Assert.That(memory.Find(scenario)!.Settings, Is.EqualTo(ScenarioSettingsSync.Serialize(scenario.SettingsOverrides)));
        Assert.That(memory.Role, Is.EqualTo("MT"));
    }

    // A guest plays the host's scenario, strat and settings, none of them its own.
    [Test]
    public void AGuestsSessionLeavesItsScenarioAsKept()
    {
        var scenario = new TopP5DeltaScenario();
        var memory = new SetupMemory();
        memory.Remember("MT", scenario, "EU", "[LPDU] Light PF", checkSettings: true);
        var kept = memory.Find(scenario)!.Settings;
        memory.Track(SessionMode.Peer);

        ((TopP5DeltaStateOverrides)scenario.SettingsOverrides).Automarkers = Automarkers.Off;

        Assert.Multiple(() =>
        {
            Assert.That(memory.Remember(SetupMemory.AutoRole, scenario, "EU", "[LPDU] Light PF", checkSettings: true), Is.False);
            Assert.That(memory.RememberSettings([scenario]), Is.False);
            Assert.That(memory.Find(scenario)!.Settings, Is.EqualTo(kept));
            Assert.That(memory.Track(SessionMode.Local), Is.EqualTo(SessionMode.Peer));
        });
    }

    // Read on unload and after a session: the settings of every scenario kept, which a switch or a session may have left
    // unread, and nothing of a scenario never shown.
    [Test]
    public void EverySettingKeptIsReadAgain()
    {
        var shown = new TopP5DeltaScenario();
        var neverShown = new TopP6GauntletScenario();
        var memory = new SetupMemory();
        memory.Remember(SetupMemory.AutoRole, shown, "EU", "[LPDU] Light PF", checkSettings: false);

        ((TopP5DeltaStateOverrides)shown.SettingsOverrides).Automarkers = Automarkers.Off;
        ((TopP6GauntletStateOverrides)neverShown.SettingsOverrides).StartFrom = TopP6GauntletPart.MagicNumber;

        Assert.Multiple(() =>
        {
            Assert.That(memory.RememberSettings([shown, neverShown]), Is.True);
            Assert.That(memory.RememberSettings([shown, neverShown]), Is.False, "nothing changed since");
            Assert.That(memory.Find(shown)!.Settings, Is.EqualTo(ScenarioSettingsSync.Serialize(shown.SettingsOverrides)));
            Assert.That(memory.Find(shown)!.Strat, Is.EqualTo("[LPDU] Light PF"));
            Assert.That(memory.Find(shown)!.Region, Is.EqualTo("EU"));
            Assert.That(memory.Find(neverShown), Is.Null);
        });
    }

    // Each public setting a peer would be sent, on a value other than a fresh one's: bools on, enums on their last
    // member, numbers on 1, and a per-player setting's own value the same way.
    private static void MoveOffTheDefaults(object overrides)
    {
        foreach (var property in overrides.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (OffDefault(property.PropertyType) is { } value) property.SetValue(overrides, value);
            else if (property.GetValue(overrides) is IPerRoleSetting perRole
                     && perRole.GetType().GetProperty(nameof(PerRoleSetting<bool>.Mine)) is { } mine
                     && OffDefault(mine.PropertyType) is { } own)
                mine.SetValue(perRole, own);
        }
    }

    private static object? OffDefault(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        if (plain == typeof(bool)) return true;
        if (plain == typeof(int)) return 1;
        if (plain == typeof(float)) return 1f;
        if (plain.IsEnum) return Enum.GetValues(plain).GetValue(Enum.GetValues(plain).Length - 1);
        return null;
    }
}
