# Action knowledge base: player actions, durations by sync level, shorthand aliases

Research note for MitCue. It is the reference for `data/actions/actions.yaml` (178 actions) and
`data/actions/aliases.yaml` (341 shorthand keys), and for the `ActionCatalog` / `AliasDictionary` loaders
in `src/MitCue.Core/Actions/Catalogs.cs`.

Status: **draft data, generated from game data and checked by script** (section 13). Items marked
**UNVERIFIED** need an in-game check or a decision from the static. Everything else was checked against a
primary source named next to it.

Contents

1. Deliverables
2. Sources and how each value was derived
3. Tooltip macro encoding (level- and job-dependent values)
4. Choosing the right row when names repeat
5. What the Action sheet columns mean
6. `actions.yaml` schema and loader rules
7. Reference tables at sync levels 80 / 90 / 100
8. Level-dependent changes (traits)
9. Upgrades, replacements and follow-up actions
10. Resources and their runtime gauges
11. `aliases.yaml`: schema, normalization, matching, level resolution
12. Token coverage for both example sheets
13. Validation performed and how to regenerate
14. UNVERIFIED items and open questions
15. Pitfalls for implementers

---

## 1. Deliverables

| File | Content |
|---|---|
| `data/actions/actions.yaml` | 178 actions: all four healers' heals, shields, regens and mitigation (oGCD and the GCDs used in plans), fairy/Seraph commands, all four tanks' party, personal, short-cooldown and invulnerability actions, tank and healer role actions, DPS party mitigation (Troubadour, Tactician, Shield Samba, Dismantle, Magick Barrier, Nature's Minne, Improvisation, Tempera, Mantra, Curing Waltz), DPS personal mitigation, melee/caster role mitigation (Feint, Addle, Second Wind, Bloodbath), tank and healer limit breaks 1-3 per job, Sprint. |
| `data/actions/aliases.yaml` | 341 normalized shorthand keys: every action token in both example sheets, the typos seen in them, common community abbreviations, per-job role phrases (`party mit`, `short cd`, `invuln`, `30%`, `filler`, `tank lb3`, `healer lb3`, `lb3`) and note-only phrases (`everything`, `kitchen sink`, `personals`). |
| this file | Sources, encoding, schema, tables, coverage, open items. |

Both YAML files parse with **YamlDotNet 18.1.0** (the version `MitCue.Core` references) into strict typed
classes (unknown keys throw) and with PyYAML. Every alias target resolves to an action in `actions.yaml`, and every
job-scoped alias names an action that job can use (section 13). `MitCue.Core.csproj` embeds everything under
`data/` except `*.md`, so both files become embedded resources (`MitCue.data/actions/...`).

## 2. Sources and how each value was derived

| Source | Version / location | Used for |
|---|---|---|
| `Action.csv`, `ActionTransient.csv`, `ClassJob.csv`, `ClassJobCategory.csv`, `Status.csv` | xivapi/ffxiv-datamining `csv/en`, game version **7.58** (repo commit `52a72b38`, 2026-11-10 "7.58 (#122)"); copies in `scratchpad/research/gamedata/` | ids, names, icons, levels, jobs, cast, base recast, base charges, costs, cooldown groups, targeting flags; level-100 tooltips |
| `Trait.csv`, `TraitTransient.csv`, `GeneralAction.csv`, `ActionCategory.csv` | same repository and commit, from `raw.githubusercontent.com/xivapi/ffxiv-datamining/master/csv/en/` | trait levels for recast, charge, duration and upgrade changes; Sprint's general-action row; category names |
| `csv/ActionTransient.csv` (old export format, raw macros) | same repository at commit **`6a2329d`** "7.4 (#88)" (2025-12-16), the last commit before the repository switched export formats (`d3eb86d`, 2026-01-04) | per-level tooltip values (the macros) |
| `csv/ActionTransient.csv` | same repository at commit `a9b1180` "7.35" | proof that Plenary Indulgence's 10% mitigation arrived in 7.4 |
| `FFXIVClientStructs/FFXIV/Component/Text/MacroDecoder.cs` | `scratchpad/research/repos/clientstructs` | meaning of `PlayerParameter(n)` |
| BossMod `BossMod/ActionQueue/Healers/SCH.cs` | `scratchpad/research/repos/bossmod` @ `6e410ef8` (read with `git show`) | cross-check of SCH ids and pet-action ids |
| Dalamud 15.0.3.9 assemblies | `~/.xlcore/dalamud/Hooks/dev/` | Lumina sheet property names and gauge properties (reflection plus a compiled probe, section 10) |
| Carry the Clear (`1chKttd7…`): tabs `TOP – White Mage`, `TOP – Scholar`, `TOP – WHM + SCH`; LPDU TOP Mitigation Sheet (`1Dlnye-i7…`): all 13 tabs | read with the Google Sheets MCP tools and, with identical content, through the public CSV export (`/export?format=csv&gid=…`) | shorthand actually used (section 12) |

### 2.1 The current tooltip export has no macros

The task brief expected level-dependent values inside macro tags in `ActionTransient.csv`. **The 7.58
`csv/en/ActionTransient.csv` contains none** (0 occurrences of `<`, `If(` or `PlayerParameter`). The new
exporter writes tooltips already evaluated, and turns `<UIForeground>` colour runs into `**`.
xivapi v2 (`v2.xivapi.com/api/sheet/ActionTransient/7535`) behaves the same way: its `@as(raw)` decorator only
disables sheet-link processing, and `@as(html)` gives coloured HTML. Both are evaluated for a level-100
character.

The macros survive in the repository's **old-format export up to patch 7.4** (commit `6a2329d`,
`csv/ActionTransient.csv`, 879 `PlayerParameter` uses). I evaluated each of the 178 actions' 7.4 macro
tooltips at level 100 with the action's own ClassJob id, converted `UIForeground` runs to `**` and `\r\n` to
`\n`, and compared the result with the 7.58 tooltip: **all 178 are byte-identical**. Therefore:

1. the 7.58 export is "the tooltip a level-100 player of that job sees";
2. none of these tooltips changed between 7.4 and 7.58 at level 100;
3. the 7.4 macros are the per-level source. Every level threshold in them that changes a duration, charge
   count or effect matches a **7.58** trait at the same level and job (section 8, checked by script). Lower-level
   branches could in theory have changed without the level-100 text changing. Nothing suggests that, and the
   trait cross-check would catch a moved threshold.

### 2.2 Traits as the second source

`Trait.csv` gives the level, job and description of every trait. Three phrasings matter, all parsed by script:

- `Reduces **X** recast time to N seconds.` gives `recast_by_level` (the base recast is `Recast100ms`);
- `Allows the accumulation of charges for consecutive uses of **X**. … Maximum Charges: N` and
  `Allows a third charge of **X**.` must equal the charge counts in the tooltip macros. All of them do;
- `Upgrades **X** to **Y**.` gives `replaced_by` (level = trait level). Every `replaced_by` in the YAML has such a trait.

## 3. Tooltip macro encoding (level- and job-dependent values)

Grammar used in the 7.4 old-format export (SaintCoinach-style text). These are the only tags in the whole
`ActionTransient` sheet (counts: `UIForeground` 21 208, `UIGlow` 21 208, `If`/`Else`/`/If` 2 660 each):

```
<If(GreaterThanOrEqualTo(PlayerParameter(72),N))> then-text <Else/> else-text </If>
<If(Equal(PlayerParameter(68),J))>                then-text <Else/> else-text </If>
<UIForeground>F201F8</UIForeground> … <UIForeground>01</UIForeground>      colour on / colour reset
<UIGlow>F201F9</UIGlow> … <UIGlow>01</UIGlow>                             glow on / glow reset
```

- The only conditions are `GreaterThanOrEqualTo(PlayerParameter(n),v)` (1 341 uses) and
  `Equal(PlayerParameter(n),v)` (1 319). Both branches are always present (`<Else/>` may be empty). Ifs nest.
- `PlayerParameter(n)` is the game's global text parameter **n−1** (macro numbering is 1-based, the `StdDeque`
  `MacroDecoder.GlobalParameters` is 0-based; table in the source comment of `MacroDecoder.cs`):
  `68` → index 67 "Player ClassJobId"; **`72` → index 71 "Player Synced Level"**; `69` → index 68 "Player Level"
  (only gatherer actions use it); `108` → index 107 "Occult Crescent Support Job Level" (Phantom actions).
  Because the level test reads the **synced** level, the tooltip shows the value that applies inside a
  level-synced ultimate.
- Colour codes in this sheet: `F201F8` for labels such as `Duration:`, `F201FA` for status names, `F201F4` for gauge
  and action names. The 7.58 export turns each `<UIForeground>` (start or reset) into `**`.

Examples (7.4 raw text, colour tags removed):

```
Reprisal (7535):  Duration: <If(GreaterThanOrEqualTo(PlayerParameter(72),98))>
                    <If(Equal(PlayerParameter(68),1))>15<Else/><If(Equal(PlayerParameter(68),19))>15<Else/> …
                    <If(Equal(PlayerParameter(68),37))>15<Else/>10</If>…</If><Else/>10</If>s
Divine Benison (7432): Duration: 15s<If(Equal(PlayerParameter(68),24))><If(GreaterThanOrEqualTo(PlayerParameter(72),88))>
                    Maximum Charges: 2<Else/></If><Else/></If>
Kerachole (24298): … <If(Equal(PlayerParameter(68),40))><If(GreaterThanOrEqualTo(PlayerParameter(72),78))>
                    Additional Effect: Regen  Cure Potency: 100  Duration: 15s<Else/></If><Else/></If> …
```

Role actions gate their level-98 upgrade on a list of ClassJob ids that includes the base classes (Reprisal:
1 GLA, 19 PLD, 3 MRD, 21 WAR, 32 DRK, 37 GNB; Feint: 2, 20, 4, 22, 29, 30, 34, 39, 41; Addle: 7, 25, 26, 27, 35, 36, 42).

How the YAML values were extracted: for every level from the learned level to 100, each tooltip was evaluated with
`PlayerParameter(72)` = that level and `PlayerParameter(68)` = the action's first job. Every
`Duration: Ns` line (including labels such as `Living Dead Duration:`) and every `Maximum Charges: N` was read,
and only the levels where a value changes were kept. Effects that appear at a higher level are appended after the
existing ones, so their position is stable (checked for all 178). Non-numeric durations
(`Duration: Time remaining on original effect` on Deployment Tactics) are not treated as durations.

## 4. Choosing the right row when names repeat

Many names have several `Action` rows: PvP versions, NPC and duty copies, pet executions, follow-up buttons,
legacy rows. The rule below was checked by script for every entry. It returns the chosen id for all of them
except the intentional same-name follow-up Horoscope 16558.

1. `Name` equals the name exactly, `IsPvP = False`, `ClassJobLevel > 0`, and the job's column in the row's
   `ClassJobCategory` is true.
2. Prefer rows whose `ClassJob` is the job or its base class (for example 6 CNJ for Medica II, 1 GLA for Sentinel,
   3 MRD for Holmgang, 26 ACN for Radiant Aegis), or `ClassJob = -1` with `IsRoleAction = True` (role actions).
   `ClassJob = 0` rows are **replacement or follow-up buttons** (Eukrasian Prognosis, Lady of Crowns, Stellar
   Detonation, Horoscope 16558, Liturgy of the Bell 28509, Improvised Finish, Manifestation, Accession, the Bole …).
   `ClassJob = -1` rows without `IsRoleAction` are pet executions or NPC copies.
3. Then prefer `IsPlayerAction = True`, then the lowest `ClassJobLevel`, then the lowest id.

Exceptions handled explicitly:

- **Limit breaks** are not chosen by name. `ClassJob.LimitBreak1/2/3` give the ids per job: tank LB1/LB2 197/198,
  healer LB1/LB2 206/207; LB3: PLD 199 Last Bastion, WAR 4240 Land Waker, DRK 4241 Dark Force, GNB 17105
  Gunmetal Soul, WHM 208 Pulse of Life, SCH 4247 Angel Feathers, AST 4248 Astral Stasis, SGE 24859 Techne Makre.
  LB rows have `ClassJobCategory = 1` (all), `ActionCategory = 9` and `PrimaryCostType = 11`.
- **Sprint** is `GeneralAction` row 4 → `Action` 3 (`IsPlayerAction = False`, `ActionCategory` 10 "System").
- Several real player actions have `IsPlayerAction = False` because they are follow-up rows (`ClassJob` 0):
  all Eukrasian actions, Lady of Crowns, the AST cards, Manifestation, Accession, Improvised Finish, Rekindle,
  Stellar Detonation. Filtering on `IsPlayerAction` alone would lose them.
- Rows with `IsPlayerAction = True` but `ClassJobLevel = 0` and `ClassJobCategory = 0` (for example Temperance 17794,
  Reprisal 17866, Eukrasia 27828, all with icon 784) cannot be learned by players and are ignored.

Same-name rows that matter at runtime (listed under `related` in the YAML):

| Name | Player row | Other rows with the same name |
|---|---|---|
| Liturgy of the Bell | 25862 | 28509 (ClassJob 0; the second press detonates the remaining stacks) |
| Horoscope | 16557 | 16558 (ClassJob 0; the second press triggers the heal). Both are in the YAML; 16558 has `follow_up_of`. |
| Emergency Tactics | 3586 | 37037 (ClassJob 0, level 100, 1 s recast: the version usable during Seraphism) |
| Whispering Dawn / Fey Illumination / Fey Blessing / Consolation | 16537 / 16538 / 16543 / 16546 | pet executions 803 / 805 / 16544 / 16547 (ClassJob -1); Seraph's versions Angel's Whisper 16550 and Seraphic Illumination 16551 |
| Aetherpact | 7437 | Fey Union 7438 (pet), Dissolve Union 7869 (ends the union) |
| Kardia, Pneuma, Radiant Aegis | 24285, 24318, 25799 | 28119, 27524, 25841 (non-player copies) |

## 5. What the Action sheet columns mean

| Column (CSV name = Lumina property) | Meaning as used here | Evidence |
|---|---|---|
| `ClassJobLevel` | level learned | |
| `ClassJob`, `ClassJobCategory` | owning class or job; the category row has one bool column per class/job abbreviation | `ClassJobCategory.csv` header |
| `ActionCategory` | 2 Spell, 3 Weaponskill, 4 Ability, 9 and 15 Limit Break, 10 System | `ActionCategory.csv` |
| `Cast100ms` | cast time × 10 | |
| `Recast100ms` | **base** recast × 10, *before traits* (Recitation 900, Deployment Tactics 1200, Zoe 1200, Swiftcast 600; the level-100 values are 60, 90, 90 and 40 s) | `Trait.csv` "Reduces **X** recast time to N" |
| `MaxCharges` | **base** charges; traits add more (Divine Benison 1 → 2 at 88). 0 = no charge system | tooltip macros and traits |
| `CooldownGroup` / `AdditionalCooldownGroup` | recast group; 58 = the GCD. Actions in one group share a recast (Raw Intuition, Bloodwhetting and Nascent Flash: group 7) | |
| `PrimaryCostType` / `PrimaryCostValue` | see the table below | tooltip "… Cost:" lines |
| `IsPlayerAction`, `IsPvP`, `IsRoleAction` | as named; section 4 lists the traps | |
| `CanTargetSelf/Party/Hostile`, `TargetArea`, `EffectRange`, `CastType` | targeting; used to check the `target` field | |
| `Icon` | icon id (Lumina `Action.Icon`, `ushort`) | |

GCD = `ActionCategory` 2 or 3 and (`CooldownGroup` or `AdditionalCooldownGroup` = 58). Macrocosmos and
Pneuma are GCD spells with their own long recast (group 23 plus 58). Eukrasian GCDs have `Recast100ms = 15`.

`PrimaryCostType` values seen on these actions:

| Type | Meaning | Actions (value) | Evidence |
|---|---|---|---|
| 3 | MP, value × 100 | Medica 9, Succor 9, The Blackest Night 30, Clemency 40 … | MP-costing spells of every caster |
| 10 | requires a status; value = `Status` row | Divine Caress 3881 Divine Grace, Sun Sign 3895 Suntouched, Lux Solaris 3874 Refulgent Lux | `Status.csv` names |
| 11 | limit break gauge | all LBs (value 0) | |
| 30 | Aetherflow | Energy Drain, Lustrate, Sacred Soil, Indomitability, Excogitation (1) | tooltip "Aetherflow Gauge Cost: 1" |
| 41 | Oath Gauge | Sheltron, Holy Sheltron, Intervention, Cover (50) | tooltip "Oath Gauge Cost: 50" |
| 56 | Blood Lily | Afflatus Misery (3) | tooltip "Can only be executed when the Blood Lily is in full bloom" |
| 57 | Lily | Afflatus Solace, Afflatus Rapture (1) | tooltip "Healing Gauge Cost: 1 Lily" |
| 68 | Addersgall | Druochole, Kerachole, Ixochole, Taurochole (1) | tooltip "Addersgall Cost: 1" |
| 69 | Addersting | Toxikon (not in the KB) | tooltip |
| 70 | **UNVERIFIED** meaning (Eukrasian Diagnosis, Prognosis and Prognosis II: value 8; Eukrasian Dosis: value 4) | | no tooltip text; MitCue does not use it |

Aetherpact's Faerie Gauge cost (10) appears only in its tooltip (`PrimaryCostType` 0); the YAML takes it from there.

## 6. `actions.yaml` schema and loader rules

```yaml
schema: mitcue-actions/1
game_version: "7.58"
actions:
  - id: 7535                  # Action row id (uint)
    name: "Reprisal"          # exact English name (game data)
    jobs: [PLD, WAR, DRK, GNB]
    level: 22                 # learned level (0 for limit breaks and Sprint)
    kind: party_mit           # party_mit|shield|regen|heal|tank_mit|invuln|buff|lb|movement|other
    target: enemy             # self|single|party|ground|enemy
    gcd: false
    cast: 0                   # seconds
    recast: 60                # seconds at level 100, traits applied (per charge)
    cooldown_group: 45        # Action.CooldownGroup (58 = GCD)
    duration: {22: 10, 98: 15}            # main effect, seconds, level thresholds
    sync: {80: 10, 90: 10, 100: 15}       # main effect at the three sync levels; null = not usable there
    effects:                              # every "Duration" in the tooltip (only when >1 or level-dependent)
      - {name: "Reprisal (enemy damage dealt -10%)", duration: {22: 10, 98: 15}}
    icon: 806
```

| Field | Type | Present | Meaning → `ActionInfo` |
|---|---|---|---|
| `id`, `name`, `jobs`, `level`, `kind`, `gcd`, `cast`, `recast` | | always | `Id`, `Name`, `Jobs`, `Level`, `Kind` (snake_case → enum: `party_mit` → `PartyMit`, `tank_mit` → `TankMit`, `lb` → `LimitBreak`), `IsGcd`, `CastTime`, `Recast` |
| `target` | enum string | always | **new**: `self`, `single` (self or one party member), `party` (area around you or around the target), `ground` (placed area), `enemy` (debuff on enemies) |
| `cooldown_group`, `additional_cooldown_group` | int | always / when ≠ 0 | **new**: raw sheet groups (shared recasts; charge systems use 71, 72, 73) |
| `recast_by_level` | {level: s} | only when a trait changes the recast | **new**; `recast` is the level-100 value |
| `charges` | {level: n} | only when > 1 at some level | `ChargesByLevel`; missing = 1 |
| `cost` | {resource, amount} | job resource only | `Cost` (`ResourceCost`) |
| `mp` | int | MP cost | **new** |
| `requires_status` | Status id | PrimaryCostType 10 | **new** |
| `duration` | {level: s} | when the action has a lasting effect at some level | `DurationByLevel`. 0 = no lasting effect from that level on (e.g. Equilibrium `{58: 0, 84: 15}`) |
| `sync` | {80, 90, 100: s or null} | always | copy of `duration` at the three sync levels; `null` = not learned yet, or replaced by an upgrade, at that level |
| `when` | before / after | only when it differs from the kind default | **new**: press before the damage (pre-placed heals: Liturgy of the Bell, Excogitation, Earthly Star, Horoscope, Macrocosmos) or after (healer LBs) |
| `lead` | s | Earthly Star (10) | **new**: minimum seconds before the hit |
| `lb` | 1, 2, 3 | limit breaks | **new**: tier |
| `effects` | list of {name, duration} | several effects, or a level-dependent one | **new**: all durations; `name` is a hand-written label |
| `replaced_by` | {id, name, level, trait} | trait upgrades | `ReplacedBy`, `ReplacedAtLevel` |
| `upgrade_of` | {id, name} | on the upgraded action | **new**: inverse of `replaced_by` |
| `follow_ups` | list of {id, name, when} | | **new**: actions that become usable after this one |
| `follow_up_of` | {id, name} | | **new**: this row only exists after another action |
| `requires`, `related`, `notes` | text / list | | `Notes`; `related` lists non-player rows (pet executions, re-press rows) |

Loader rules:

- **Level maps are thresholds**: the value at level L is the value of the largest key ≤ L
  (`ActionInfo.PickAtLevel`). Every `duration`, `charges` and `recast_by_level` map starts at the learned level, so
  the "no key ≤ level" fallback in `PickAtLevel` (it returns the first value) only triggers below the learned level,
  where `AvailableAt` is already false. Check `AvailableAt(level)` first.
- **Name lookup**: names are unique among entries without `follow_up_of`. When two entries share a name
  (Horoscope 16557/16558), `FindByName` must return the one without `follow_up_of`. Compare names
  case-insensitively with punctuation removed, as `IActionCatalog.FindByName` promises.
- **Upgrades at a sync level**: an action whose `replaced_by.level ≤ sync` resolves to the replacement (repeat while
  possible). An action with `upgrade_of` whose `level > sync` resolves back to the base action (Heart of Corundum in
  TEA → Heart of Stone). `ForJob(job, syncLevel)` returns entries whose `jobs` contain the job, with
  `level ≤ syncLevel`, and not replaced at `syncLevel`.
- Timing defaults (DESIGN §5.1) with `when`: party_mit, tank_mit, shield, regen, invuln and buff are pressed before
  the hit; heal after unless `when: before`; tank LBs before, healer LBs after (`when: after`).
- YamlDotNet: `UnderscoredNamingConvention` maps the snake_case keys to PascalCase properties. Integer-keyed maps
  deserialize into `Dictionary<int, double>` / `Dictionary<int, int>`; `sync` needs `Dictionary<int, double?>`.

Typed DTO that loads the file with YamlDotNet 18.1.0 in strict mode (compiled and run, section 13):

```csharp
var de = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
var file = de.Deserialize<ActionsFile>(yamlText);

class ActionsFile { public string Schema { get; set; } = ""; public string GameVersion { get; set; } = ""; public List<ActionDto> Actions { get; set; } = []; }
class CostDto { public string Resource { get; set; } = ""; public int Amount { get; set; } }
class RefDto { public uint Id { get; set; } public string Name { get; set; } = ""; public int? Level { get; set; } public string? Trait { get; set; } public string? When { get; set; } public string? What { get; set; } }
class EffectDto { public string Name { get; set; } = ""; public Dictionary<int, double> Duration { get; set; } = new(); }
class ActionDto
{
    public uint Id { get; set; }  public string Name { get; set; } = "";  public List<string> Jobs { get; set; } = [];
    public int Level { get; set; }  public string Kind { get; set; } = "";  public string Target { get; set; } = "";
    public bool Gcd { get; set; }  public double Cast { get; set; }  public double Recast { get; set; }
    public int CooldownGroup { get; set; }  public int? AdditionalCooldownGroup { get; set; }
    public Dictionary<int, double>? RecastByLevel { get; set; }  public Dictionary<int, int>? Charges { get; set; }
    public CostDto? Cost { get; set; }  public int? Mp { get; set; }  public uint? RequiresStatus { get; set; }
    public Dictionary<int, double>? Duration { get; set; }  public Dictionary<int, double?> Sync { get; set; } = new();
    public string? When { get; set; }  public double? Lead { get; set; }  public int? Lb { get; set; }
    public List<EffectDto>? Effects { get; set; }  public RefDto? ReplacedBy { get; set; }  public RefDto? UpgradeOf { get; set; }
    public List<RefDto>? FollowUps { get; set; }  public RefDto? FollowUpOf { get; set; }  public string? Requires { get; set; }
    public List<RefDto>? Related { get; set; }  public int Icon { get; set; }  public string? Notes { get; set; }
}
```

### 6.1 How `kind` and `target` were assigned

The game has no such field; both were assigned by hand from the tooltip text:

- `party_mit`: lowers damage for the party, or lowers the enemy's damage (Reprisal, Feint, Addle, Dismantle). Includes
  Fey Illumination (magic −5%), Plenary Indulgence (−10% since 7.4), Collective Unconscious, Sun Sign, Expedient,
  Holos, Kerachole, Sacred Soil, Passage of Arms, Dark Missionary, Heart of Light, Troubadour, Tactician, Shield Samba,
  Magick Barrier.
- `shield`: barriers (Galvanize, the Eukrasian barriers, Divine Benison, Consolation, Panhaima, Haima, Divine Veil,
  Shake It Off, The Blackest Night, Improvised Finish, Tempera, Divine Caress, DPS personal barriers).
- `tank_mit`: damage reduction on yourself or one target, **for any job** (Rampart, the 30%/40% cooldowns, short
  cooldowns, Aquaveil, Taurochole, Exaltation, the Bole, Oblation, Intervention, Riddle of Earth, Third Eye, Tengentsu).
- `regen`: the lasting part is a heal over time (Asylum, Whispering Dawn, Medica II/III, Aspected Helios, Helios
  Conjunction, Physis/II, Aurora, Regen, the Ewer, Improvisation).
- `heal`: instant or delayed heals (`when: before` marks the pre-placed ones). `buff`: healing, potency and HP buffs
  and enablers (Recitation, Dissipation, Protraction, Neutral Sect, Summon Seraph, Seraphism, Zoe, Krasis, Nature's
  Minne, Mantra, Swiftcast …). `invuln`, `lb` and `movement` as named; `other` for the rest.

## 7. Reference tables at sync levels 80 / 90 / 100

Cell format: `main duration / recast` (`·` = no lasting effect, `×N` = charges, `–` = not usable at that level).
TEA = 80, TOP = 90, DMU = 100. Generated from `actions.yaml`.

#### White Mage

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 124 | Medica | WHM | 10 | heal | party | G | 2 | 900 MP | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 133 | Medica II | WHM | 50 | regen | party | G | 2 | 1000 MP | 15s / 2.5s | 15s / 2.5s | – | → Medica III (96) |
| 37010 | Medica III | WHM | 96 | regen | party | G | 2 | 1000 MP | – | – | 15s / 2.5s | upgrade of Medica II |
| 131 | Cure III | WHM | 40 | heal | party | G | 2 | 1500 MP | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 137 | Regen | WHM | 35 | regen | single | G |  | 400 MP | 18s / 2.5s | 18s / 2.5s | 18s / 2.5s |  |
| 136 | Presence of Mind | WHM | 30 | buff | self |  |  |  | 15s / 120s | 15s / 120s | 15s / 120s |  |
| 37008 | Aetherial Shift | WHM | 40 | movement | self |  |  |  | · / 60s | · / 60s | · / 60s |  |
| 140 | Benediction | WHM | 50 | heal | single |  |  |  | · / 180s | · / 180s | · / 180s |  |
| 3569 | Asylum | WHM | 52 | regen | ground |  |  |  | 24s / 90s | 24s / 90s | 24s / 90s |  |
| 16531 | Afflatus Solace | WHM | 52 | heal | single | G |  | 1 Lily | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 3571 | Assize | WHM | 56 | heal | party |  |  |  | · / 40s | · / 40s | · / 40s |  |
| 7430 | Thin Air | WHM | 58 | buff | self |  |  |  | 12s / 60s ×2 | 12s / 60s ×2 | 12s / 60s ×2 |  |
| 3570 | Tetragrammaton | WHM | 60 | heal | single |  |  |  | · / 60s | · / 60s | · / 60s ×2 |  |
| 7432 | Divine Benison | WHM | 66 | shield | single |  |  |  | 15s / 30s | 15s / 30s ×2 | 15s / 30s ×2 |  |
| 7433 | Plenary Indulgence | WHM | 70 | party_mit | party |  |  |  | 10s / 60s | 10s / 60s | 10s / 60s |  |
| 16535 | Afflatus Misery | WHM | 74 | other | enemy | G |  | 3 Blood Lily | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 16534 | Afflatus Rapture | WHM | 76 | heal | party | G |  | 1 Lily | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 16536 | Temperance | WHM | 80 | party_mit | party |  |  |  | 20s / 120s | 20s / 120s | 20s / 120s | then Divine Caress |
| 25861 | Aquaveil | WHM | 86 | tank_mit | single |  |  |  | – | 8s / 60s | 8s / 60s |  |
| 25862 | Liturgy of the Bell | WHM | 90 | heal | ground |  |  |  | – | 20s / 180s | 20s / 180s |  |
| 37011 | Divine Caress | WHM | 100 | shield | party |  |  | status 3881 | – | – | 10s / 1s | follow-up of Temperance |

#### Scholar

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 185 | Adloquium | SCH | 30 | shield | single | G | 2 | 900 MP | 30s / 2.5s | 30s / 2.5s | 30s / 2.5s | then Manifestation |
| 186 | Succor | SCH | 35 | shield | party | G | 2 | 900 MP | 30s / 2.5s | 30s / 2.5s | – | → Concitation (96) |
| 37013 | Concitation | SCH | 96 | shield | party | G | 2 | 900 MP | – | – | 30s / 2.5s | upgrade of Succor; then Accession |
| 37015 | Manifestation | SCH | 100 | shield | single | G |  | 900 MP | – | – | 30s / 2.5s | follow-up of Seraphism |
| 37016 | Accession | SCH | 100 | shield | party | G |  | 900 MP | – | – | 30s / 2.5s | follow-up of Seraphism |
| 16537 | Whispering Dawn | SCH | 20 | regen | party |  |  |  | 21s / 60s | 21s / 60s | 21s / 60s |  |
| 16538 | Fey Illumination | SCH | 40 | party_mit | party |  |  |  | 20s / 120s | 20s / 120s | 20s / 120s |  |
| 166 | Aetherflow | SCH | 45 | other | self |  |  |  | · / 60s | · / 60s | · / 60s |  |
| 167 | Energy Drain | SCH | 45 | other | enemy |  |  | 1 Aetherflow | · / 1s | · / 1s | · / 1s |  |
| 189 | Lustrate | SCH | 45 | heal | single |  |  | 1 Aetherflow | · / 1s | · / 1s | · / 1s |  |
| 188 | Sacred Soil | SCH | 50 | party_mit | ground |  |  | 1 Aetherflow | 15s / 30s | 15s / 30s | 15s / 30s |  |
| 3583 | Indomitability | SCH | 52 | heal | party |  |  | 1 Aetherflow | · / 30s | · / 30s | · / 30s |  |
| 3585 | Deployment Tactics | SCH | 56 | shield | party |  |  |  | · / 120s | · / 90s | · / 90s |  |
| 3586 | Emergency Tactics | SCH | 58 | buff | self |  |  |  | 15s / 15s | 15s / 15s | 15s / 15s |  |
| 3587 | Dissipation | SCH | 60 | buff | self |  |  |  | 30s / 180s | 30s / 180s | 30s / 180s |  |
| 7434 | Excogitation | SCH | 62 | heal | single |  |  | 1 Aetherflow | 45s / 45s | 45s / 45s | 45s / 45s |  |
| 7437 | Aetherpact | SCH | 70 | heal | single |  |  | 10 Faerie Gauge | · / 3s | · / 3s | · / 3s |  |
| 16542 | Recitation | SCH | 74 | buff | self |  |  |  | 15s / 90s | 15s / 90s | 15s / 60s |  |
| 16543 | Fey Blessing | SCH | 76 | heal | party |  |  |  | · / 60s | · / 60s | · / 60s |  |
| 16545 | Summon Seraph | SCH | 80 | buff | self |  |  |  | 22s / 120s | 22s / 120s | 22s / 120s | then Consolation |
| 16546 | Consolation | SCH | 80 | shield | party |  |  |  | 30s / 30s ×2 | 30s / 30s ×2 | 30s / 30s ×2 | follow-up of Summon Seraph |
| 25867 | Protraction | SCH | 86 | buff | single |  |  |  | – | 10s / 60s | 10s / 60s |  |
| 25868 | Expedient | SCH | 90 | party_mit | party |  |  |  | – | 20s / 120s | 20s / 120s |  |
| 37014 | Seraphism | SCH | 100 | buff | party |  |  |  | – | – | 20s / 180s | then Manifestation, Accession |

#### Astrologian

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 3600 | Helios | AST | 10 | heal | party | G | 1.5 | 700 MP | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 3601 | Aspected Helios | AST | 40 | regen | party | G | 1.5 | 800 MP | 15s / 2.5s | 15s / 2.5s | – | → Helios Conjunction (96) |
| 37030 | Helios Conjunction | AST | 96 | regen | party | G | 1.5 | 800 MP | – | – | 15s / 2.5s | upgrade of Aspected Helios |
| 3595 | Aspected Benefic | AST | 34 | regen | single | G |  | 400 MP | 15s / 2.5s | 15s / 2.5s | 15s / 2.5s |  |
| 3606 | Lightspeed | AST | 6 | buff | self |  |  |  | 15s / 60s ×2 | 15s / 60s ×2 | 15s / 60s ×2 |  |
| 3614 | Essential Dignity | AST | 15 | heal | single |  |  |  | · / 40s ×2 | · / 40s ×2 | · / 40s ×3 |  |
| 3612 | Synastry | AST | 50 | buff | single |  |  |  | 20s / 120s | 20s / 120s | 20s / 120s |  |
| 3613 | Collective Unconscious | AST | 58 | party_mit | party |  |  |  | 10s / 60s | 10s / 60s | 10s / 60s |  |
| 16553 | Celestial Opposition | AST | 60 | heal | party |  |  |  | 15s / 60s | 15s / 60s | 15s / 60s |  |
| 7439 | Earthly Star | AST | 62 | heal | ground |  |  |  | 20s / 60s | 20s / 60s | 20s / 60s | then Stellar Detonation |
| 8324 | Stellar Detonation | AST | 62 | heal | party |  |  |  | · / 3s | · / 3s | · / 3s | follow-up of Earthly Star |
| 37022 | Minor Arcana | AST | 70 | other | self |  |  |  | · / 1s | · / 1s | · / 1s | then Lady of Crowns |
| 7445 | Lady of Crowns | AST | 70 | heal | party |  |  |  | · / 1s | · / 1s | · / 1s | follow-up of Minor Arcana |
| 16556 | Celestial Intersection | AST | 74 | shield | single |  |  |  | 30s / 30s | 30s / 30s ×2 | 30s / 30s ×2 |  |
| 16557 | Horoscope | AST | 76 | heal | party |  |  |  | 10s / 60s | 10s / 60s | 10s / 60s | then Horoscope |
| 16558 | Horoscope | AST | 76 | heal | party |  |  |  | · / 1s | · / 1s | · / 1s | follow-up of Horoscope |
| 16559 | Neutral Sect | AST | 80 | buff | self |  |  |  | 20s / 120s | 20s / 120s | 20s / 120s | then Sun Sign |
| 37031 | Sun Sign | AST | 100 | party_mit | party |  |  | status 3895 | – | – | 15s / 1s | follow-up of Neutral Sect |
| 25873 | Exaltation | AST | 86 | tank_mit | single |  |  |  | – | 8s / 60s | 8s / 60s |  |
| 25874 | Macrocosmos | AST | 90 | heal | party | G |  | 600 MP | – | 15s / 180s | 15s / 180s | then Microcosmos |
| 25875 | Microcosmos | AST | 90 | heal | party |  |  |  | – | · / 1s | · / 1s | follow-up of Macrocosmos |
| 37020 | Play II | AST | 30 | other | self |  |  |  | · / 1s | · / 1s | · / 1s | then the Bole, the Arrow |
| 37021 | Play III | AST | 30 | other | self |  |  |  | · / 1s | · / 1s | · / 1s | then the Spire, the Ewer |
| 37027 | the Bole | AST | 30 | tank_mit | single |  |  |  | 15s / 1s | 15s / 1s | 15s / 1s | follow-up of Play II |
| 37024 | the Arrow | AST | 30 | buff | single |  |  |  | 15s / 1s | 15s / 1s | 15s / 1s | follow-up of Play II |
| 37025 | the Spire | AST | 30 | shield | single |  |  |  | 30s / 1s | 30s / 1s | 30s / 1s | follow-up of Play III |
| 37028 | the Ewer | AST | 30 | regen | single |  |  |  | 15s / 1s | 15s / 1s | 15s / 1s | follow-up of Play III |

#### Sage

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 24286 | Prognosis | SGE | 10 | heal | party | G | 2 | 700 MP | · / 2.5s | · / 2.5s | · / 2.5s | then Eukrasian Prognosis, Eukrasian Prognosis II |
| 24290 | Eukrasia | SGE | 30 | other | self | G |  |  | · / 2.5s | · / 2.5s | · / 2.5s | then Eukrasian Diagnosis, Eukrasian Prognosis, Eukrasian Prognosis II |
| 24291 | Eukrasian Diagnosis | SGE | 30 | shield | single | G |  |  | 30s / 1.5s | 30s / 1.5s | 30s / 1.5s | follow-up of Eukrasia |
| 24292 | Eukrasian Prognosis | SGE | 30 | shield | party | G |  |  | 30s / 1.5s | 30s / 1.5s | – | → Eukrasian Prognosis II (96); follow-up of Eukrasia |
| 37034 | Eukrasian Prognosis II | SGE | 96 | shield | party | G |  |  | – | – | 30s / 1.5s | upgrade of Eukrasian Prognosis; follow-up of Eukrasia |
| 24285 | Kardia | SGE | 4 | buff | single |  |  |  | · / 5s | · / 5s | · / 5s |  |
| 24288 | Physis | SGE | 20 | regen | party |  |  |  | – | – | – | → Physis II (60) |
| 24302 | Physis II | SGE | 60 | regen | party |  |  |  | 15s / 60s | 15s / 60s | 15s / 60s | upgrade of Physis |
| 24294 | Soteria | SGE | 35 | buff | self |  |  |  | 15s / 90s | 15s / 90s | 15s / 60s |  |
| 24295 | Icarus | SGE | 40 | movement | single |  |  |  | · / 45s | · / 45s | · / 45s |  |
| 24296 | Druochole | SGE | 45 | heal | single |  |  | 1 Addersgall | · / 1s | · / 1s | · / 1s |  |
| 24298 | Kerachole | SGE | 50 | party_mit | party |  |  | 1 Addersgall | 15s / 30s | 15s / 30s | 15s / 30s |  |
| 24299 | Ixochole | SGE | 52 | heal | party |  |  | 1 Addersgall | · / 30s | · / 30s | · / 30s |  |
| 24300 | Zoe | SGE | 56 | buff | self |  |  |  | 30s / 120s | 30s / 90s | 30s / 90s |  |
| 24301 | Pepsis | SGE | 58 | heal | party |  |  |  | · / 30s | · / 30s | · / 30s |  |
| 24303 | Taurochole | SGE | 62 | tank_mit | single |  |  | 1 Addersgall | 15s / 45s | 15s / 45s | 15s / 45s |  |
| 24305 | Haima | SGE | 70 | shield | single |  |  |  | 15s / 120s | 15s / 120s | 15s / 120s |  |
| 24309 | Rhizomata | SGE | 74 | other | self |  |  |  | · / 90s | · / 90s | · / 90s |  |
| 24310 | Holos | SGE | 76 | party_mit | party |  |  |  | 20s / 120s | 20s / 120s | 20s / 120s |  |
| 24311 | Panhaima | SGE | 80 | shield | party |  |  |  | 15s / 120s | 15s / 120s | 15s / 120s |  |
| 24317 | Krasis | SGE | 86 | buff | single |  |  |  | – | 10s / 60s | 10s / 60s |  |
| 24318 | Pneuma | SGE | 90 | heal | party | G | 1.5 | 700 MP | – | · / 120s | · / 120s |  |
| 37035 | Philosophia | SGE | 100 | buff | party |  |  |  | – | – | 20s / 180s |  |

#### Role actions

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 7531 | Rampart | PLD,WAR,DRK,GNB | 8 | tank_mit | self |  |  |  | 20s / 90s | 20s / 90s | 20s / 90s |  |
| 7535 | Reprisal | PLD,WAR,DRK,GNB | 22 | party_mit | enemy |  |  |  | 10s / 60s | 10s / 60s | 15s / 60s |  |
| 7548 | Arm's Length | tanks, melee, phys. ranged | 32 | other | self |  |  |  | 6s / 120s | 6s / 120s | 6s / 120s |  |
| 7533 | Provoke | PLD,WAR,DRK,GNB | 15 | other | enemy |  |  |  | · / 30s | · / 30s | · / 30s |  |
| 7537 | Shirk | PLD,WAR,DRK,GNB | 48 | other | single |  |  |  | · / 120s | · / 120s | · / 120s |  |
| 7549 | Feint | melee | 22 | party_mit | enemy |  |  |  | 10s / 90s | 10s / 90s | 15s / 90s |  |
| 7560 | Addle | BLM,SMN,RDM,PCT | 8 | party_mit | enemy |  |  |  | 10s / 90s | 10s / 90s | 15s / 90s |  |
| 7541 | Second Wind | melee, phys. ranged | 8 | heal | self |  |  |  | · / 120s | · / 120s | · / 120s |  |
| 7542 | Bloodbath | melee | 12 | buff | self |  |  |  | 20s / 90s | 20s / 90s | 20s / 90s |  |
| 7559 | Surecast | healers, casters | 44 | other | self |  |  |  | 6s / 120s | 6s / 120s | 6s / 120s |  |
| 7561 | Swiftcast | healers, casters | 18 | buff | self |  |  |  | 10s / 60s | 10s / 60s | 10s / 40s |  |
| 7562 | Lucid Dreaming | healers, casters | 14 | other | self |  |  |  | 21s / 60s | 21s / 60s | 21s / 60s |  |
| 7568 | Esuna | WHM,SCH,AST,SGE | 10 | other | single | G |  | 400 MP | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 7571 | Rescue | WHM,SCH,AST,SGE | 48 | other | single |  |  |  | · / 120s | · / 120s | · / 120s |  |

#### Paladin

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 17 | Sentinel | PLD | 38 | tank_mit | self |  |  |  | 15s / 120s | 15s / 120s | – | → Guardian (92) |
| 36920 | Guardian | PLD | 92 | tank_mit | self |  |  |  | – | – | 15s / 120s | upgrade of Sentinel |
| 3542 | Sheltron | PLD | 35 | tank_mit | self |  |  | 50 Oath Gauge | 6s / 5s | – | – | → Holy Sheltron (82) |
| 25746 | Holy Sheltron | PLD | 82 | tank_mit | self |  |  | 50 Oath Gauge | – | 8s / 5s | 8s / 5s | upgrade of Sheltron |
| 27 | Cover | PLD | 45 | other | single |  |  | 50 Oath Gauge | 12s / 120s | 12s / 120s | 12s / 120s |  |
| 30 | Hallowed Ground | PLD | 50 | invuln | self |  |  |  | 10s / 420s | 10s / 420s | 10s / 420s |  |
| 22 | Bulwark | PLD | 52 | tank_mit | self |  |  |  | 10s / 90s | 10s / 90s | 10s / 90s |  |
| 3540 | Divine Veil | PLD | 56 | shield | party |  |  |  | 30s / 90s | 30s / 90s | 30s / 90s |  |
| 3541 | Clemency | PLD | 58 | heal | single | G | 1.5 | 4000 MP | · / 2.5s | · / 2.5s | · / 2.5s |  |
| 7382 | Intervention | PLD | 62 | tank_mit | single |  |  | 50 Oath Gauge | 6s / 10s | 8s / 10s | 8s / 10s |  |
| 7385 | Passage of Arms | PLD | 70 | party_mit | party |  |  |  | 18s / 120s | 18s / 120s | 18s / 120s |  |

#### Warrior

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 40 | Thrill of Battle | WAR | 30 | tank_mit | self |  |  |  | 10s / 90s | 10s / 90s | 10s / 90s |  |
| 44 | Vengeance | WAR | 38 | tank_mit | self |  |  |  | 15s / 120s | 15s / 120s | – | → Damnation (92) |
| 36923 | Damnation | WAR | 92 | tank_mit | self |  |  |  | – | – | 15s / 120s | upgrade of Vengeance |
| 43 | Holmgang | WAR | 42 | invuln | self |  |  |  | 10s / 240s | 10s / 240s | 10s / 240s |  |
| 3551 | Raw Intuition | WAR | 56 | tank_mit | self |  |  |  | 6s / 25s | – | – | → Bloodwhetting (82) |
| 25751 | Bloodwhetting | WAR | 82 | tank_mit | self |  |  |  | – | 8s / 25s | 8s / 25s | upgrade of Raw Intuition |
| 3552 | Equilibrium | WAR | 58 | heal | self |  |  |  | · / 60s | 15s / 60s | 15s / 60s |  |
| 7388 | Shake It Off | WAR | 68 | shield | party |  |  |  | 30s / 90s | 30s / 90s | 30s / 90s |  |
| 16464 | Nascent Flash | WAR | 76 | tank_mit | single |  |  |  | 6s / 25s | 8s / 25s | 8s / 25s |  |

#### Dark Knight

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 3636 | Shadow Wall | DRK | 38 | tank_mit | self |  |  |  | 15s / 120s | 15s / 120s | – | → Shadowed Vigil (92) |
| 36927 | Shadowed Vigil | DRK | 92 | tank_mit | self |  |  |  | – | – | 15s / 120s | upgrade of Shadow Wall |
| 3634 | Dark Mind | DRK | 45 | tank_mit | self |  |  |  | 10s / 60s | 10s / 60s | 10s / 60s |  |
| 3638 | Living Dead | DRK | 50 | invuln | self |  |  |  | 10s / 300s | 10s / 300s | 10s / 300s |  |
| 16471 | Dark Missionary | DRK | 66 | party_mit | party |  |  |  | 15s / 90s | 15s / 90s | 15s / 90s |  |
| 7393 | The Blackest Night | DRK | 70 | shield | single |  |  | 3000 MP | 7s / 15s | 7s / 15s | 7s / 15s |  |
| 25754 | Oblation | DRK | 82 | tank_mit | single |  |  |  | – | 10s / 60s ×2 | 10s / 60s ×2 |  |

#### Gunbreaker

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 16140 | Camouflage | GNB | 6 | tank_mit | self |  |  |  | 20s / 90s | 20s / 90s | 20s / 90s |  |
| 16148 | Nebula | GNB | 38 | tank_mit | self |  |  |  | 15s / 120s | 15s / 120s | – | → Great Nebula (92) |
| 36935 | Great Nebula | GNB | 92 | tank_mit | self |  |  |  | – | – | 15s / 120s | upgrade of Nebula |
| 16151 | Aurora | GNB | 45 | regen | single |  |  |  | 18s / 60s | 18s / 60s ×2 | 18s / 60s ×2 |  |
| 16152 | Superbolide | GNB | 50 | invuln | self |  |  |  | 10s / 360s | 10s / 360s | 10s / 360s |  |
| 16160 | Heart of Light | GNB | 64 | party_mit | party |  |  |  | 15s / 90s | 15s / 90s | 15s / 90s |  |
| 16161 | Heart of Stone | GNB | 68 | tank_mit | single |  |  |  | 7s / 25s | – | – | → Heart of Corundum (82) |
| 25758 | Heart of Corundum | GNB | 82 | tank_mit | single |  |  |  | – | 8s / 25s | 8s / 25s | upgrade of Heart of Stone |

#### DPS

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 7405 | Troubadour | BRD | 62 | party_mit | party |  |  |  | 15s / 120s | 15s / 90s | 15s / 90s |  |
| 7408 | Nature's Minne | BRD | 66 | buff | party |  |  |  | 15s / 120s | 15s / 120s | 15s / 120s |  |
| 16889 | Tactician | MCH | 56 | party_mit | party |  |  |  | 15s / 120s | 15s / 90s | 15s / 90s |  |
| 2887 | Dismantle | MCH | 62 | party_mit | enemy |  |  |  | 10s / 120s | 10s / 120s | 10s / 120s |  |
| 16012 | Shield Samba | DNC | 56 | party_mit | party |  |  |  | 15s / 120s | 15s / 90s | 15s / 90s |  |
| 16015 | Curing Waltz | DNC | 52 | heal | party |  |  |  | · / 60s | · / 60s | · / 60s |  |
| 16014 | Improvisation | DNC | 80 | regen | party |  |  |  | 15s / 120s | 15s / 120s | 15s / 120s | then Improvised Finish |
| 25789 | Improvised Finish | DNC | 80 | shield | party |  |  |  | 30s / 1.5s | 30s / 1.5s | 30s / 1.5s | follow-up of Improvisation |
| 25857 | Magick Barrier | RDM | 86 | party_mit | party |  |  |  | – | 10s / 120s | 10s / 120s |  |
| 34685 | Tempera Coat | PCT | 10 | shield | self |  |  |  | 10s / 120s | 10s / 120s | 10s / 120s | then Tempera Grassa |
| 34686 | Tempera Grassa | PCT | 88 | shield | party |  |  |  | – | 10s / 1s | 10s / 1s | follow-up of Tempera Coat |
| 65 | Mantra | MNK | 42 | buff | party |  |  |  | 15s / 90s | 15s / 90s | 15s / 90s |  |
| 7394 | Riddle of Earth | MNK | 64 | tank_mit | self |  |  |  | 10s / 120s | 10s / 120s | 10s / 120s |  |
| 24404 | Arcane Crest | RPR | 40 | shield | self |  |  |  | 5s / 30s | 5s / 30s | 5s / 30s |  |
| 7498 | Third Eye | SAM | 6 | tank_mit | self |  |  |  | 4s / 15s | – | – | → Tengentsu (82) |
| 36962 | Tengentsu | SAM | 82 | tank_mit | self |  |  |  | – | 4s / 15s | 4s / 15s | upgrade of Third Eye |
| 2241 | Shade Shift | NIN | 2 | shield | self |  |  |  | 20s / 120s | 20s / 120s | 20s / 120s |  |
| 157 | Manaward | BLM | 30 | shield | self |  |  |  | 20s / 120s | 20s / 120s | 20s / 120s |  |
| 25799 | Radiant Aegis | SMN | 2 | shield | self |  |  |  | 30s / 60s | 30s / 60s ×2 | 30s / 60s ×2 |  |
| 36997 | Lux Solaris | SMN | 100 | heal | party |  |  | status 3874 | – | – | · / 60s |  |
| 25830 | Rekindle | SMN | 80 | heal | single |  |  |  | 30s / 20s | 30s / 20s | 30s / 20s |  |

#### Limit breaks

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 197 | Shield Wall | PLD,WAR,DRK,GNB | 0 | lb | party |  |  |  | 10s / 0s | 10s / 0s | 10s / 0s |  |
| 198 | Stronghold | PLD,WAR,DRK,GNB | 0 | lb | party |  |  |  | 15s / 0s | 15s / 0s | 15s / 0s |  |
| 199 | Last Bastion | PLD | 0 | lb | party |  |  |  | 12s / 0s | 12s / 0s | 12s / 0s |  |
| 4240 | Land Waker | WAR | 0 | lb | party |  |  |  | 12s / 0s | 12s / 0s | 12s / 0s |  |
| 4241 | Dark Force | DRK | 0 | lb | party |  |  |  | 12s / 0s | 12s / 0s | 12s / 0s |  |
| 17105 | Gunmetal Soul | GNB | 0 | lb | party |  |  |  | 12s / 0s | 12s / 0s | 12s / 0s |  |
| 206 | Healing Wind | WHM,SCH,AST,SGE | 0 | lb | party |  | 2 |  | · / 0s | · / 0s | · / 0s |  |
| 207 | Breath of the Earth | WHM,SCH,AST,SGE | 0 | lb | party |  | 2 |  | · / 0s | · / 0s | · / 0s |  |
| 208 | Pulse of Life | WHM | 0 | lb | party |  | 2 |  | · / 0s | · / 0s | · / 0s |  |
| 4247 | Angel Feathers | SCH | 0 | lb | party |  | 2 |  | · / 0s | · / 0s | · / 0s |  |
| 4248 | Astral Stasis | AST | 0 | lb | party |  | 2 |  | · / 0s | · / 0s | · / 0s |  |
| 24859 | Techne Makre | SGE | 0 | lb | party |  | 2 |  | · / 0s | · / 0s | · / 0s |  |

#### General

| id | Action | Jobs | Lv | Kind | Target | GCD | Cast | Cost | L80 | L90 | L100 | Upgrade / follow-up |
|---:|---|---|---:|---|---|:-:|---:|---|---|---|---|---|
| 3 | Sprint | all | 0 | movement | self |  |  |  | 10s / 60s | 10s / 60s | 10s / 60s |  |

## 8. Level-dependent changes (traits)

All values below are in `actions.yaml`. A script matched every row to a 7.58 trait at the same level (the trait id
is given). Levels that matter for the three ultimates are in **bold**.

| Action | Change | Level | Trait (7.58) |
|---|---|---|---|
| Reprisal | duration 10 → 15 s | **98** | 640 Enhanced Reprisal |
| Feint | duration 10 → 15 s | **98** | 641 Enhanced Feint |
| Addle | duration 10 → 15 s | **98** | 643 Enhanced Addle |
| Troubadour / Tactician / Shield Samba | recast 120 → 90 s | **88** | 447 / 452 / 456 |
| Troubadour / Tactician / Shield Samba | damage reduction 10% → 15% (duration unchanged) | **98** | 601 / 607 / 611 |
| Divine Benison | charges 1 → 2 | **88** | 490 |
| Tetragrammaton | charges 1 → 2 | **98** | 625 |
| Essential Dignity | charges 1 → 2 → 3 | 78 / **98** | 316 / 634 |
| Celestial Intersection | charges 1 → 2 | **88** | 500 |
| Aurora | charges 1 → 2 | **84** | 425 |
| Radiant Aegis | charges 1 → 2 | **88** | 480 |
| Lightspeed | recast 90 → 60 s (2 charges at every level) | 68 | 189 Hyper Lightspeed |
| Deployment Tactics | recast 120 → 90 s | **88** | 494 |
| Recitation | recast 90 → 60 s | **98** | 629 |
| Zoe | recast 120 → 90 s | **88** | 378 |
| Soteria | recast 90 → 60 s | **94** | 636 |
| Swiftcast | recast 60 → 40 s | **94** | 644 |
| Sheltron | duration 4 → 6 s | 74 | 262 |
| Intervention | 6 → 8 s; adds Knight's Resolve (−10%, 4 s) and Knight's Benediction (regen, 12 s) | **82** | 413 |
| Nascent Flash | 6 → 8 s; adds Stem the Flow (−10%, 4 s) and Stem the Tide (barrier, 20 s) | **82** | 419 |
| Physis II | healing-received buff 10 → 15 s | **98** | 638 |
| Kerachole | adds a 15 s regen | 78 | 375 |
| Sacred Soil | adds a regen while in the area | 78 | 313 |
| Asylum | adds healing received +10% in the area | 78 | 310 |
| Temperance | adds Divine Grace, 30 s (enables Divine Caress) | **100** | 626 |
| Neutral Sect | adds Suntouched, 30 s (enables Sun Sign) | **100** | 635 |
| Presence of Mind | adds Sacred Sight (3 stacks, 30 s) | **92** | 623 |
| Equilibrium | adds a 15 s regen | **84** | 420 |
| Arcane Crest | adds a 15 s party regen when the barrier breaks | **84** | 385 |
| Rampart | adds healing received +15% | **94** | 639 |
| Thrill of Battle | adds healing received +20% | 78 | 269 |
| Shake It Off | adds a 300-potency heal | 76 | 417 |
| Divine Veil | adds a 400-potency heal | **88** | 416 |
| Plenary Indulgence | 10% damage reduction for 10 s, at every level | patch **7.4** | not a trait: absent from the 7.35 export (`a9b1180`), present in 7.4 and 7.58 |

Upgrades by trait (`replaced_by`) are in section 9.

## 9. Upgrades, replacements and follow-up actions

Permanent upgrades (trait "Upgrades **X** to **Y**", VERIFIED in `Trait.csv`). The base action is not usable from
that level on:

| Base (id) | Upgrade (id) | Level | Trait | TEA 80 | TOP 90 | DMU 100 |
|---|---|---|---|---|---|---|
| Physis (24288) | Physis II (24302) | 60 | 510 Physis Mastery | Physis II | Physis II | Physis II |
| Sheltron (3542) | Holy Sheltron (25746) | 82 | 412 | Sheltron | Holy Sheltron | Holy Sheltron |
| Raw Intuition (3551) | Bloodwhetting (25751) | 82 | 418 | Raw Intuition | Bloodwhetting | Bloodwhetting |
| Heart of Stone (16161) | Heart of Corundum (25758) | 82 | 424 | Heart of Stone | Heart of Corundum | Heart of Corundum |
| Third Eye (7498) | Tengentsu (36962) | 82 | 589 | Third Eye | Tengentsu | Tengentsu |
| Sentinel (17) | Guardian (36920) | 92 | 564 | Sentinel | Sentinel | Guardian |
| Vengeance (44) | Damnation (36923) | 92 | 567 | Vengeance | Vengeance | Damnation |
| Shadow Wall (3636) | Shadowed Vigil (36927) | 92 | 571 | Shadow Wall | Shadow Wall | Shadowed Vigil |
| Nebula (16148) | Great Nebula (36935) | 92 | 574 | Nebula | Nebula | Great Nebula |
| Medica II (133) | Medica III (37010) | 96 | 624 | Medica II | Medica II | Medica III |
| Succor (186) | Concitation (37013) | 96 | 628 | Succor | Succor | Concitation |
| Aspected Helios (3601) | Helios Conjunction (37030) | 96 | 633 | Aspected Helios | Aspected Helios | Helios Conjunction |
| Eukrasian Prognosis (24292) | Eukrasian Prognosis II (37034) | 96 | 637 | E. Prognosis | E. Prognosis | E. Prognosis II |

Follow-up actions (a button that becomes usable, or changes, after another action):

| Trigger (id) | Follow-up (id) | Condition | Evidence |
|---|---|---|---|
| Temperance (16536) | Divine Caress (37011) | Divine Grace (level 100, 30 s) | Divine Caress tooltip "Can only be executed while Divine Grace is active"; `PrimaryCostType` 10 = status 3881 |
| Neutral Sect (16559) | Sun Sign (37031) | Suntouched (level 100, 30 s) | tooltip; cost type 10 = status 3895 |
| Summon Seraph (16545) | Consolation (16546): 2 charges, 30 s recast | Seraph present (22 s) | Consolation tooltip "Orders Seraph to execute Consolation" |
| Seraphism (37014) | Manifestation (37015) replaces Adloquium; Accession (37016) replaces Concitation | Seraphism active (20 s) | tooltips "※Adloquium changes to Manifestation …", "※Concitation changes to Accession …" |
| Eukrasia (24290) | Eukrasian Diagnosis (24291), Eukrasian Prognosis (24292) / II (37034) | next GCD | tooltips "※Prognosis changes to Eukrasian Prognosis …" |
| Earthly Star (7439) | Stellar Detonation (8324) | star placed | tooltip |
| Horoscope (16557) | Horoscope (16558) | Horoscope active | ClassJob-0 row with the same name |
| Macrocosmos (25874) | Microcosmos (25875) | Macrocosmos active (15 s) | tooltip |
| Minor Arcana (37022) | Lady of Crowns (7445) | Umbral Draw | tooltip "※Minor Arcana changes to Lady of Crowns …" |
| Play II (37020) / Play III (37021) | the Bole (37027), the Arrow (37024) / the Spire (37025), the Ewer (37028) | Umbral or Astral Draw | card tooltips "※Play II changes to The Bole …" |
| Improvisation (16014) | Improvised Finish (25789) | dancing | tooltip "※Improvisation changes to Improvised Finish …" |
| Tempera Coat (34685) | Tempera Grassa (34686) | Tempera Coat active, level 88 | tooltip; trait 541 |
| Liturgy of the Bell (25862) | Liturgy of the Bell (28509) | bell placed | ClassJob-0 row |
| Aetherpact (7437) | Dissolve Union (7869) | union active | BossMod `DissolveUnion = 7869` |

Whether the trigger's *hotbar button* turns into the follow-up (what `ActionManager.GetAdjustedActionId` returns)
is **UNVERIFIED** for Summon Seraph → Consolation, Temperance → Divine Caress, Neutral Sect → Sun Sign and
Macrocosmos → Microcosmos: those follow-ups are ordinary player rows (`ClassJob` = job, `IsPlayerAction` True)
without a "※X changes to Y" line. The pairs that do have that line are VERIFIED as button changes by the game text.
`docs/research/clientstructs.md` §3.7 describes the runtime check.

## 10. Resources and their runtime gauges

Resource names used in `cost.resource`, and the Dalamud 15 gauge property that reports each one. Property names and
types were read from `Dalamud.dll` by reflection and used in a probe plugin that compiles against Dalamud 15.0.3.9
(`scratchpad/actkb/probe/Plugin.cs`, `dotnet build -c Release`: 0 errors, 0 warnings):

| `cost.resource` | Gauge (via `IJobGauges.Get<T>()`) | Type | Notes |
|---|---|---|---|
| Lily | `WHMGauge.Lily` | `byte` | also `LilyTimer` (`short`) |
| Blood Lily | `WHMGauge.BloodLily` | `byte` | Afflatus Misery needs 3 |
| Aetherflow | `SCHGauge.Aetherflow` | `byte` | Dissipation and Aetherflow refill it to 3 |
| Faerie Gauge | `SCHGauge.FairyGauge` | `byte` | also `SeraphTimer` (`short`) and `DismissedFairy` (Dissipation) |
| Addersgall | `SGEGauge.Addersgall` | `byte` | also `AddersgallTimer`, `Addersting`, `Eukrasia` (`bool`) |
| Oath Gauge | `PLDGauge.OathGauge` | `byte` | Sheltron, Holy Sheltron, Intervention and Cover cost 50 |
| (TBN refund) | `DRKGauge.HasDarkArts` | `bool` | The Blackest Night costs 3000 MP (`mp`) |

Lumina sheet access used by the probe (all of it compiles): `IDataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()`,
`.TryGetRow(id, out var row)`, `.GetRowOrDefault(id)`, `.GetRow(id)`; `Action` properties `Name`
(`ReadOnlySeString`, `.ExtractText()`), `Icon` (`ushort`), `ClassJobLevel` (`byte`), `Recast100ms` and `Cast100ms`
(`ushort`), `MaxCharges`, `PrimaryCostType`, `CooldownGroup`, `AdditionalCooldownGroup` (`byte`),
`PrimaryCostValue` (`ushort`), `IsPlayerAction`, `IsPvP`, `IsRoleAction` (`bool`), `ClassJob`,
`ClassJobCategory`, `ActionCategory` (`RowRef<…>`, `.RowId`, `.Value`); `ClassJobCategory.WHM` and so on (`bool`);
`ActionTransient.Description`; `ClassJob.LimitBreak3` (`RowRef<Action>`), `ClassJob.IsLimitedJob`;
`GeneralAction.Action`; `Trait.Level`, `Trait.ClassJob`, `Trait.Value`, `TraitTransient.Description`;
`ActionManager.GetMaxCharges(uint actionId, uint level)` (static, returns `ushort`) and
`am->GetAdjustedActionId(uint)` (instance). Runtime use of `ActionManager` is in
`docs/research/clientstructs.md` §3: only the local player's cooldowns are readable, so the co-healer's come from
this KB.

## 11. `aliases.yaml`: schema, normalization, matching, level resolution

```yaml
schema: mitcue-aliases/1
aliases:
  kera: Kerachole                                   # string: one action, any job that has it
  zoe eprog: {actions: [Zoe, Eukrasian Prognosis], sequence: true, note: "…"}
  wings:                                            # list: one expansion per job
    - {job: WHM, actions: [Temperance]}
    - {job: PLD, actions: [Passage of Arms]}
  short cd:
    - {job: WAR, actions: [Bloodwhetting], targeted: [Nascent Flash]}
  everything: {note_only: true, note: "Use every party mitigation and shield available."}
```

- Mapping keys: `actions` (list of exact action names), `job` (abbreviation), `sequence` (pressed in this order),
  `note_only` plus `note`, and **`targeted`**: a proposed addition to `AliasExpansion` holding the actions to use
  when the item names another player ("Short CD to R1", "Nascant Co-Tank"). Bloodwhetting and Holy Sheltron are
  self-only, so the targeted short cooldowns are Nascent Flash and Intervention.
- Loading with YamlDotNet: deserialize `Dictionary<string, object>`. Each value is a `string`, a
  `Dictionary<object, object>` or a `List<object>`; scalars inside come back as strings (`"true"`).
- **Normalization** (keys are stored normalized; `AliasDictionary.Normalize` must do the same): lowercase
  (invariant culture); delete `'` and `’`; replace every character that is not a letter, digit or `%` with a space;
  collapse spaces; trim. "Nature's Minne" → `natures minne`, "Co-Tank" → `co tank`, "30%" → `30%`,
  "FeyIllum" → `feyillum`. (The stub's comment says "punctuation removed", but `%` must survive for `30%`/`40%`.)
- **Matching an item** (after the cell parser has split on ` / `, `+`, `,`, `→`): take the **longest alias key that
  equals the item or is followed by a space**; the rest is a hint or a target ("Lilybell pop after 1st Hit" →
  `lilybell` + "pop after 1st hit"; "Short CD to R1" → `short cd` + target R1; "Soil 57" → `soil` + a count).
  Extra rules these sheets need: strip a leading job abbreviation as a job filter ("WHM Bell", "SCH Soil";
  `pld wings` is also a key because it appears in non-PLD columns); treat a run of 3 or more spaces as a separator
  ("Party Mit        Dismantle", LPDU P6 J7); "Co-Tank", "to/on R1", "to 4" and "R1/2" are targets.
- **Job filter**: an expansion with `job` applies only to that job; one without `job` applies to any job whose
  `jobs` contain every action. Several expansions may exist (`lb3`, `party mit`): use the one for the assignee's job.
- **Level resolution**: resolve each name, then apply section 6's upgrade rules at the plan's sync level:
  `succor` in DMU → Concitation; `eprog` in DMU → Eukrasian Prognosis II; `physis` → Physis II (60+);
  `hoc`, `hs`, `bw` in TEA → Heart of Stone, Sheltron, Raw Intuition; `30%` in DMU → Guardian, Damnation,
  Shadowed Vigil, Great Nebula. A name that is not available at the sync level (Expedient, Aquaveil, Liturgy of the
  Bell, Macrocosmos, Magick Barrier, Protraction … in TEA) must produce a diagnostic, not a silent drop.

Meanings that are conventions rather than game data (all taken from the two sheets; confirm with the static):

| Key | Meaning used | Evidence |
|---|---|---|
| `wings` | WHM: Temperance; PLD: Passage of Arms | LPDU H1 WHM column "Wings" on a ~120 s cadence at the mechanics where Carry the Clear puts Temperance (P3 round 2, P4 first stack, P5 Sigma); "PLD Wings" in tank columns (LPDU P5 Delta, P6 WC2). **Inferred** for WHM. |
| `bene` | Benediction (not Divine Benison) | CtC WHM row 19: plan "Benediction (1:57)", note "Bene right after the last cleave", count "Bene 25"; "Benison" is used separately |
| `tetra` | Tetragrammaton | CtC WHM row 17 note "Bene / Tetra for emergencies"; combined tab D17 "Tetragrammaton (0:49)" |
| `spreadlo` | Adloquium → Deployment Tactics | LPDU SCH column; CtC writes the same play as "Recit → Adlo → Deploy" |
| `seraph` | Summon Seraph (two Consolations follow) | CtC SCH "Seraph (1:19)" with the note "Consolation ×2 around 1:30" |
| `2nd seraph charge` | Consolation | LPDU P3 Latent 3 "FeyIllum + 2nd Seraph Charge" (Consolation has 2 charges) |
| `filler` | PLD Bulwark, WAR Thrill of Battle, DRK Dark Mind, GNB Camouflage | **Inferred**: the LPDU tank tabs always pair "Rampart + {Bullwark, Thrill, Dark Mind, Camo} + {HS, BW, TBN, HoC}", and "General Tank Mit" writes "Filler + Short CD" next to "Rampart + 30% + Short CD" |
| `short cd` | PLD Holy Sheltron (targeted: Intervention), WAR Bloodwhetting (Nascent Flash), DRK The Blackest Night, GNB Heart of Corundum | the same tank tabs; "Short CD to R1/2" means the targeted version |
| `30%` / `40%` | Sentinel, Vengeance, Shadow Wall, Nebula / their level-92 upgrades | tooltips "Reduces damage taken by 30%" / "by 40%" |
| `invuln` | Hallowed Ground, Holmgang, Living Dead, Superbolide | tooltips |
| `party mit` | PLD Divine Veil, WAR Shake It Off, DRK Dark Missionary, GNB Heart of Light, BRD Troubadour, MCH Tactician, DNC Shield Samba, RDM Magick Barrier, PCT Tempera Coat → Tempera Grassa | LPDU tank and R1 columns ("Party Mit if MCH"). Melee, BLM, SMN and healers have no mapping (their plans name actions) |
| `everything`, `kitchen sink`, `personals` | note only | LPDU P1/P2, the tank tabs, the P6 note |
| typos | `nascant`, `vengance`, `bullwark`, `sentinal`, `super bolide` | LPDU tank tabs |

## 12. Token coverage for both example sheets

Method: every non-empty cell of the plan columns was split on `/ + , → -> · ( )` and newlines, times were removed,
and each item was matched with the longest-prefix rule of section 11. Plan columns: CtC column C and the count
column E (combined tab: C and D); LPDU: every assignment column of the 6 phase tabs and columns B–C of the 7 tank
tabs. **Every action item resolves.** The alias keys hit (spellings include trailing hints and the count column's
numbers; cells are A1 references):

| Alias key | Times | Spellings seen (incl. trailing hints / counts) | Example cells | Resolves to |
|---|---:|---|---|---|
| `2nd seraph charge` | 1 | 2nd seraph charge | LPDU P3 F5 | Consolation |
| `30%` | 3 | 30% | LPDU 'General Tank Mit til P5' B6; LPDU 'General Tank Mit til P5' C6 | PLD: Sentinel; WAR: Vengeance; DRK: Shadow Wall; GNB: Nebula |
| `addle` | 8 | addle | LPDU 'P1/P2' I7; LPDU P3 K2 | Addle |
| `adlo` | 17 | adlo; adlo 41 | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C25 | Adloquium |
| `aquaveil` | 18 | aquaveil; aquaveil 17; aquaveil 21; aquaveil 14 | CtC 'TOP – White Mage' C22; CtC 'TOP – White Mage' C25 | Aquaveil |
| `asylum` | 30 | asylum; asylum 28; asylum 26; asylum 30 | CtC 'TOP – White Mage' C17; CtC 'TOP – White Mage' C24 | Asylum |
| `bell` | 15 | bell; bell 35; bell 25; bell 32 | CtC 'TOP – White Mage' C18; CtC 'TOP – White Mage' C34 | Liturgy of the Bell |
| `bene` | 3 | bene 25; bene 24 | CtC 'TOP – White Mage' E19; CtC 'TOP – White Mage' E51 | Benediction |
| `benediction` | 6 | benediction | CtC 'TOP – White Mage' C19; CtC 'TOP – White Mage' C51 | Benediction |
| `benison` | 30 | benison; benison 16; benison 34; benison 28 | CtC 'TOP – White Mage' C17; CtC 'TOP – White Mage' C22 | Divine Benison |
| `blessing` | 23 | blessing; blessing 33; blessing 37; blessing 44 | CtC 'TOP – Scholar' C17; CtC 'TOP – Scholar' C19 | Fey Blessing |
| `bullwark` | 7 | bullwark | LPDU 'WAR/PLD' C10; LPDU 'WAR/PLD' C13 | Bulwark |
| `bw` | 9 | bw | LPDU 'WAR/DRK' B10; LPDU 'WAR/DRK' B12 | Bloodwhetting |
| `camo` | 7 | camo | LPDU 'WAR/GNB' C10; LPDU 'WAR/GNB' C13 | Camouflage |
| `cu` | 10 | cu | LPDU P3 E4; LPDU P3 E7 | Collective Unconscious |
| `dark mind` | 6 | dark mind | LPDU 'WAR/DRK' C10; LPDU 'WAR/DRK' C13 | Dark Mind |
| `deploy` | 24 | deploy; deploy 30; deploy 35; deploy 47 | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C25 | Deployment Tactics |
| `dismantle` | 4 | dismantle | LPDU 'P1/P2' H7; LPDU P3 J6 | Dismantle |
| `dissipate` | 2 | dissipate | LPDU P6 F2; LPDU P6 F9 | Dissipation |
| `dissipation` | 3 | dissipation 52; dissipation 44; dissipation 32 | CtC 'TOP – Scholar' E17; CtC 'TOP – Scholar' E24 | Dissipation |
| `eprog` | 6 | eprog | LPDU P6 G3; LPDU P6 G4 | Eukrasian Prognosis |
| `everything` | 4 | everything | LPDU 'P1/P2' D4; LPDU 'P1/P2' E4 | note only |
| `excog` | 6 | excog; excog 33; excog 41 | CtC 'TOP – Scholar' C17; CtC 'TOP – Scholar' C53 | Excogitation |
| `exp` | 8 | exp 27; exp 46; exp 56; exp 58 | CtC 'TOP – Scholar' E18; CtC 'TOP – Scholar' E26 | Expedient |
| `expedient` | 22 | expedient | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C26 | Expedient |
| `feint` | 14 | feint; feint autos omega uptime | LPDU 'P1/P2' F5; LPDU 'P1/P2' G7 | Feint |
| `feyillum` | 4 | feyillum | LPDU P3 F5; LPDU P4 F7 | Fey Illumination |
| `filler` | 3 | filler | LPDU 'General Tank Mit til P5' B5; LPDU 'General Tank Mit til P5' C5 | PLD: Bulwark; WAR: Thrill of Battle; DRK: Dark Mind; GNB: Camouflage |
| `hallowed ground` | 6 | hallowed ground | LPDU 'WAR/PLD' C2; LPDU 'WAR/PLD' C12 | Hallowed Ground |
| `healer lb3` | 8 | healer lb3 | CtC 'TOP – White Mage' C66; CtC 'TOP – Scholar' C67 | WHM: Pulse of Life; SCH: Angel Feathers; AST: Astral Stasis; SGE: Techne Makre |
| `hoc` | 13 | hoc; hoc co-tank | LPDU 'WAR/GNB' C10; LPDU 'WAR/GNB' C11 | Heart of Corundum |
| `holmgang` | 6 | holmgang | LPDU 'WAR/DRK' B6; LPDU 'WAR/DRK' B11 | Holmgang |
| `holos` | 5 | holos; holos befor stack hits | LPDU P3 G5; LPDU P5 G6 | Holos |
| `hs` | 10 | hs | LPDU 'WAR/PLD' C10; LPDU 'WAR/PLD' C11 | Holy Sheltron |
| `illum` | 6 | illum 55; illum 23; illum 49; illum 42 | CtC 'TOP – Scholar' E18; CtC 'TOP – Scholar' E26 | Fey Illumination |
| `illumination` | 12 | illumination | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C26 | Fey Illumination |
| `indomitability` | 1 | indomitability | CtC 'TOP – Scholar' C14 | Indomitability |
| `intervention` | 5 | intervention co-tank | LPDU 'WAR/PLD' C12; LPDU 'DRK/PLD' C12 | Intervention |
| `invuln` | 2 | invuln diffuse wave cannon | LPDU 'General Tank Mit til P5' B2; LPDU 'General Tank Mit til P5' C2 | PLD: Hallowed Ground; WAR: Holmgang; DRK: Living Dead; GNB: Superbolide |
| `kera` | 19 | kera; kera after 1st hit | LPDU 'P1/P2' E2; LPDU 'P1/P2' E10 | Kerachole |
| `kitchen sink` | 22 | kitchen sink | LPDU 'WAR/DRK' B2; LPDU 'WAR/DRK' C2 | note only |
| `lilybell` | 4 | lilybell; lilybell debuff at ~3 secs; lilybell pop after 1st hit | LPDU P3 D5; LPDU P5 D10 | Liturgy of the Bell |
| `living dead` | 6 | living dead | LPDU 'WAR/DRK' C4; LPDU 'WAR/DRK' C12 | Living Dead |
| `lucid` | 1 | lucid | CtC 'TOP – White Mage' C14 | Lucid Dreaming |
| `lustrate` | 1 | lustrate | CtC 'TOP – Scholar' C14 | Lustrate |
| `macrocosmos` | 3 | macrocosmos | LPDU P3 E5; LPDU P5 E4 | Macrocosmos |
| `magick barrier` | 6 | magick barrier | LPDU 'P1/P2' I4; LPDU 'P1/P2' I11 | Magick Barrier |
| `nascant` | 3 | nascant co-tank | LPDU 'WAR/DRK' B11; LPDU 'WAR/GNB' B11 | Nascent Flash |
| `nebula` | 3 | nebula | LPDU 'WAR/GNB' C11; LPDU 'DRK/GNB' C11 | Nebula |
| `neutralsect` | 6 | neutralsect; neutralsect no skip; neutralsect if p4 skip; neutralsect shields for proteans | LPDU P3 E4; LPDU P4 E2 | Neutral Sect |
| `oblation` | 12 | oblation; oblation co-tank | LPDU 'WAR/DRK' C10; LPDU 'WAR/DRK' C11 | Oblation |
| `panhaima` | 6 | panhaima | LPDU P3 G4; LPDU P4 G3 | Panhaima |
| `party mit` | 28 | party mit; party mit on cd; party mit if mch; party mit dismantle | LPDU 'P1/P2' B4; LPDU 'P1/P2' C4 | PLD: Divine Veil; WAR: Shake It Off; DRK: Dark Missionary; GNB: Heart of Light; BRD: Troubadour; MCH: Tactician; DNC: Shield Samba; RDM: Magick Barrier; PCT: Tempera Coat → Tempera Grassa |
| `physis` | 1 | physis | LPDU P4 G3 | Physis |
| `pld wings` | 2 | pld wings | LPDU P5 C3; LPDU P6 C5 | PLD: Passage of Arms |
| `plenary` | 30 | plenary | CtC 'TOP – White Mage' C13; CtC 'TOP – White Mage' C18 | Plenary Indulgence |
| `presence of mind` | 1 | presence of mind | CtC 'TOP – White Mage' C14 | Presence of Mind |
| `protraction` | 27 | protraction; protraction 41; protraction 33; protraction 34 | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C22 | Protraction |
| `rampart` | 32 | rampart; rampart as p1 dies; rampart when ever ready; rampart use during hw cast | LPDU 'General Tank Mit til P5' B3; LPDU 'General Tank Mit til P5' C3 | Rampart |
| `recit` | 17 | recit; recit 34 | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C25 | Recitation |
| `rep` | 22 | rep | LPDU 'P1/P2' B5; LPDU 'P1/P2' C7 | Reprisal |
| `sch lb3` | 2 | sch lb3 35; sch lb3 53 | CtC 'TOP – White Mage' E67; CtC 'TOP – Scholar' E67 | SCH: Angel Feathers |
| `sentinal` | 1 | sentinal | LPDU 'GNB/PLD' C12 | Sentinel |
| `sentinel` | 2 | sentinel | LPDU 'WAR/PLD' C11; LPDU 'DRK/PLD' C11 | Sentinel |
| `seraph` | 30 | seraph; seraph 36; seraph 51; seraph 45 | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C25 | Summon Seraph |
| `shadow wall` | 3 | shadow wall | LPDU 'WAR/DRK' C11; LPDU 'DRK/GNB' B11 | Shadow Wall |
| `short cd` | 12 | short cd; short cd to 4; short cd to r2; short cd to r1 | LPDU 'P1/P2' B3; LPDU 'P1/P2' C3 | PLD: Holy Sheltron (targeted: Intervention); WAR: Bloodwhetting (targeted: Nascent Flash); DRK: The Blackest Night (targeted: The Blackest Night); GNB: Heart of Corundum (targeted: Heart of Corundum) |
| `soil` | 74 | soil; soil 51; soil 42; soil 54 | CtC 'TOP – Scholar' C18; CtC 'TOP – Scholar' C22 | Sacred Soil |
| `solace` | 1 | solace | CtC 'TOP – White Mage' C17 | Afflatus Solace |
| `spreadlo` | 6 | spreadlo; spreadlo stack | LPDU P3 F3; LPDU P4 F2 | Adloquium → Deployment Tactics |
| `sprint` | 4 | sprint 26; sprint 40; sprint 31; sprint 19 | CtC 'TOP – White Mage' E30; CtC 'TOP – White Mage' E60 | Sprint |
| `succor` | 6 | succor; succor proteans | LPDU P3 F7; LPDU P6 F3 | Succor |
| `super bolide` | 1 | super bolide | LPDU 'WAR/GNB' C4 | Superbolide |
| `superbolide` | 5 | superbolide | LPDU 'WAR/GNB' C12; LPDU 'DRK/GNB' C4 | Superbolide |
| `swiftcast` | 1 | swiftcast 26 | CtC 'TOP – White Mage' E23 | Swiftcast |
| `tank lb3` | 4 | tank lb3; tank lb3 36 | CtC 'TOP – White Mage' E57; LPDU P6 B2 | PLD: Last Bastion; WAR: Land Waker; DRK: Dark Force; GNB: Gunmetal Soul |
| `tbn` | 12 | tbn | LPDU 'WAR/DRK' C10; LPDU 'WAR/DRK' C11 | The Blackest Night |
| `temp` | 9 | temp; temp 36; temp 33; temp 16 | CtC 'TOP – White Mage' C18; CtC 'TOP – White Mage' E18 | Temperance |
| `temperance` | 15 | temperance | CtC 'TOP – White Mage' C26; CtC 'TOP – White Mage' C33 | Temperance |
| `tetra` | 1 | tetra 25 | CtC 'TOP – White Mage' E17 | Tetragrammaton |
| `tetragrammaton` | 1 | tetragrammaton | CtC 'TOP – WHM + SCH' D17 | Tetragrammaton |
| `thrill` | 6 | thrill | LPDU 'WAR/DRK' B10; LPDU 'WAR/DRK' B13 | Thrill of Battle |
| `vengance` | 3 | vengance | LPDU 'WAR/DRK' B12; LPDU 'WAR/GNB' B12 | Vengeance |
| `wd` | 6 | wd 29; wd 35; wd 41; wd 37 | CtC 'TOP – Scholar' E23; CtC 'TOP – Scholar' E40 | Whispering Dawn |
| `whispering dawn` | 12 | whispering dawn | CtC 'TOP – Scholar' C23; CtC 'TOP – Scholar' C40 | Whispering Dawn |
| `whm lb3` | 2 | whm lb3 31; whm lb3 51 | CtC 'TOP – White Mage' E66; CtC 'TOP – Scholar' E66 | WHM: Pulse of Life |
| `wings` | 5 | wings | LPDU P3 D4; LPDU P4 D3 | WHM: Temperance; PLD: Passage of Arms |
| `zoe eprog` | 4 | zoe eprog | LPDU P3 G3; LPDU P3 G7 | Zoe → Eukrasian Prognosis |

Items that resolve to no alias; all of them are meant to become targets, hints or notes:

- slot and column headers: `MT`, `OT`, `H1`, `H2`, `H1 WHM`, `H1 AST`, `H2 SCH`, `H2 SGE`, `M1`, `M2`, `R1`, `R2`,
  `CM LB Tank`, `CM Rep Tank`, `WAR`, `DRK`, `PLD`, `GNB`, `White Mage`, `Scholar`, `Top WHMs`, `Top SCHs`;
- targets: `your 3rd/4th-in-line player`, `2` (from "Short CD to R1/2");
- timing hints: `after the hit`, `right after the 1st stack`, `just before P3 ends`, `Debuffs at 13 secs`,
  `when rots explode`;
- legend and note text in the CtC header rows (9–15) and the LPDU note rows (for example "WAR can use Shake as P3 dies
  for 1st P4 Proteans", "Press 10s Mit when doing the side dodge …", "Use personals and self healing all during
  Meteors!", "15 sec Mit is used as the clone jumps").

Action words in the CtC free-text notes are keys too: Rapture, Medica II, Cure III, Consolation, Emergency Tactics,
Succor, Swiftcast, Sprint, Dissipate(s), Lucid, Presence of Mind, Assize, Lustrate, Indomitability, Shake, Tank LB3,
healer LB3 (checked by script over the notes columns).

## 13. Validation performed and how to regenerate

Checks run, all passing:

1. 7.4 macro tooltips evaluated at level 100 equal the 7.58 tooltips: 178 of 178 identical.
2. Charge counts from the tooltip macros equal the trait-derived charges for all 7 trait-charged actions; every
   `replaced_by` has an "Upgrades X to Y" trait at that level; every duration or effect threshold has a 7.58 trait at
   the same level.
3. The cost labels in the tooltips (`Healing Gauge Cost`, `Aetherflow Gauge Cost`, `Addersgall Cost`,
   `Oath Gauge Cost`) agree with `PrimaryCostType/Value`.
4. The row selection rule (section 4) reproduces every chosen id.
5. `actions.yaml`: ids are unique; every name equals the `Action.csv` `Name` of its id; names are unique among
   entries without `follow_up_of`.
6. `aliases.yaml`: every key is normalized; every action and `targeted` name exists in `actions.yaml` and in
   `Action.csv`; every job-scoped expansion names actions listed for that job.
7. Both files load with **YamlDotNet 18.1.0** into the strict typed DTO of section 6 (a .NET 10 console app,
   `scratchpad/actkb/yamlcheck`), with spot checks (Reprisal 10 s at 90 and 15 s at 100, Troubadour 120 s recast at
   80, Intervention 6 s at 80 and 8 s at 90), and with PyYAML.
8. Token coverage (section 12).

Regeneration: the scripts are in `scratchpad/actkb/` (the scratchpad is not part of the repository): `ids.py` (id
list), `meta.py` (hand-written kind, target, labels and relations), `macro.py` (macro evaluator), `extract.py`,
`build.py` → `entries.json`, `emit.py <out.yaml>`, `validate.py`, `coverage.py`, `tables.py`. Inputs:
`research/gamedata/*.csv` (7.58), `actkb/gamedata2/Trait*.csv` (7.58) and `actkb/dl/old74/ActionTransient.csv` (7.4,
from `git show 6a2329d:csv/ActionTransient.csv` in a blobless clone of xivapi/ffxiv-datamining). For a future patch:
refresh the current CSVs, re-run the level-100 equivalence check, and edit `meta.py` only for new or changed
actions. If the old-format history ever stops matching, the trait sheet alone covers every level change in section 8.

## 14. UNVERIFIED items and open questions

1. **Job LB3 durations** of Land Waker, Dark Force and Gunmetal Soul: their tooltips are empty in the game data (so
   are Angel Feathers, Astral Stasis and Techne Makre). The YAML uses Last Bastion's 12 s for every tank LB3; the
   statuses (863, 864, 1931) only say "Damage taken is reduced".
2. The meaning of `PrimaryCostType` 70 (Eukrasian actions).
3. Hotbar button replacement for Summon Seraph → Consolation, Temperance → Divine Caress, Neutral Sect → Sun Sign
   and Macrocosmos → Microcosmos (section 9).
4. Which action id(s) the ActionEffect hook reports for fairy commands: the player's command (16537 …) or only the
   pet's execution (803, 16550 …) with the pet as the source. The usage tracker should accept both, and the pet's
   owner.
5. The inferred shorthand meanings: `wings` = Temperance for WHM, `filler`, `short cd` (section 11).
6. Lower-level tooltip branches come from 7.4; equivalence was proven at level 100 only (section 2.1).
7. DPS limit breaks are not in the KB (`melee lb3` is note-only). Add them if a plan assigns DPS LBs.

## 15. Pitfalls for implementers

- Don't treat `Recast100ms` or `MaxCharges` as level-100 values: traits change them (section 8). In game, the local
  player's real values come from `ActionManager` (`clientstructs.md`); the co-healer's come from this KB.
- Don't filter player actions on `IsPlayerAction` alone (section 4).
- Divine Benison has **1 charge in TEA** and 2 in TOP and DMU; Tetragrammaton has 2 only in DMU; Reprisal, Feint and
  Addle last 10 s in TEA and TOP; Troubadour, Tactician and Shield Samba have a 120 s recast in TEA and give 10% until 98.
- Plenary Indulgence has been a 10% party mitigation since 7.4, at every level. Older plans and logs treat it as a heal.
- Sacred Soil, Asylum, Earthly Star and Liturgy of the Bell are ground-targeted: the reminder should say where.
- Liturgy of the Bell, Earthly Star, Horoscope, Macrocosmos and Excogitation are heals pressed before the damage.
- Consolation needs Seraph. Whispering Dawn, Fey Illumination, Fey Blessing and Aetherpact need the fairy (or Seraph)
  and are unavailable for 30 s after Dissipation. Plans that say "Seraph" expect two Consolations.
- Same-name rows: Horoscope 16557/16558 and Liturgy of the Bell 25862/28509 (a re-press is not a second use).
