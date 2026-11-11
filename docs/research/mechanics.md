# Mechanic catalogs: TOP (verified), TEA and DMU (first drafts)

Research note for MitCue's fight catalogs (`data/fights/*.yaml`, schema in `docs/DESIGN.md` §3.1), written
2026-11-11. Everything below was checked against the primary sources in §1. Anything that could not be checked
is marked **UNVERIFIED**. Sync types and playback are covered in `docs/research/cactbot-timelines.md`; sheet
fixtures and readers in `docs/research/sheets-import.md`.

| File | Content |
|---|---|
| `data/fights/top.yaml` | The Omega Protocol (Ultimate): 79 mechanics (68 for damage, enrages and dodge rows, plus 11 markers), 323 aliases (16 marked unverified), and the `carry_the_clear` calibration. Complete and verified. |
| `data/fights/tea.yaml` | The Epic of Alexander (Ultimate): 61 mechanics (3 markers), 153 aliases (21 unverified). First draft, no calibration. |
| `data/fights/dmu.yaml` | Dancing Mad (Ultimate): 63 mechanics (1 marker), 145 aliases (12 unverified). First draft, no calibration. |

## 0. Results

- **Timeline checks (all three files).**
  - Every `hit`/`entry` pair is an entry of the cactbot timeline (same name, same time ±0.05 s).
  - Every value in `hits` is the time of a timeline entry.
  - `hits[0] == hit`, and `hits` is ascending and has no duplicates.
  - Every hit lies inside its mechanic's phase (`PhaseAt(hit) == phase`).
  - Ids are unique and carry their phase as a prefix, and mechanics are ordered by `hit`.
  - The files load with a YAML parser (PyYAML 6.0.3) and use only the §3.1 keys: `fight, name, territory,
    sync_level, timeline, phases, mechanics, calibration`; phases `id, name, start`; mechanics `id, name, phase,
    hit, hits, entry, kind, aliases`.
- **TOP sheet coverage.**
  - All 47 mechanic rows of Carry the Clear map to a TOP mechanic. Their columns A and B are identical in the three
    tabs.
  - All 98 labelled mechanic rows of the LPDU sheet map to a TOP mechanic: 12 plan tabs (the 13th, `ALOO`, is a
    title page).
  - Each row label is an alias of the mechanic it maps to: Carry the Clear labels verbatim, LPDU labels with
    surrounding spaces trimmed.
  - Every other row is a phase header, a notes row or empty (§4.4).
  - A simulated resolver (phase + alias + row order, §4.5) reproduces the intended mapping for every row of both
    sheets.
- **Carry the Clear calibration** (timeline seconds of sheet 0:00): `p1 0.9, p2 201.8, p3 399.4, p4 606.7, p5 714.7, p6 1099.3`.
  - **P1–P4:** the spread is 0.6–0.9 s. That is whole-second quantisation, not disagreement: one zero point fits
    every row of each phase exactly (§4.2).
  - **P5 and P6:** the offset falls by about 0.75 (P5) and 0.52 (P6) s per 100 s of sheet time
    (spreads 2.4 s and 1.6 s), so the sheet's late rows sit 1–2 s later than cactbot's. The fitted intercepts
    match the sheet's own zero definitions: 715.66 against P5 boss targetable at 715.6, and 1100.28 against the
    Blind Faith sync at 1100.0.
  - **Use per-row offsets** (an action's time minus its own row's time) whenever a row has a time. The phase zero
    is only a fallback (§4.3).
- **Outliers explained (§4.2).** Carry the Clear times sometimes the boss cast and sometimes the damage. The
  quantisation test identifies which, row by row:
  - **Damage timed:** Cosmo Dive (the 7BA7/7BA8 hits, not the 7BA6 cast end 2.4 s earlier), Blue Screen (the 7B7C
    hit, 1.0 s after the 7B7B cast end) and the P5 Omega cones.
  - **Cast end timed:** P2 Cosmo Memory (7B22, 1.4 s before the 7B24 raidwide), the four Hello World rounds (the
    Latent Defect cast end, 1.0 s before the towers), and Blind Faith (the 7B87 cast end, 1.2 s before the hit).
  - **P6 enrage:** cactbot puts the effect 30 s after cast start, while the sheet times the end of the 16 s cast
    bar.
  - **Enrage rows** carry times that no kill log reaches.
- **Sync levels verified** in `ContentFinderCondition.csv`: TOP 90, TEA 80, DMU 100 (§8).
- **Sync-engine findings (§5.5):**
  - DMU enters P2 and P4 only through `ActorControl 80000027` syncs.
  - TOP's P2 start is a `MapEffect` with a `StartsUsing` fallback.
  - TEA's P2 start is preceded by an inert `RemovedCombatant`.
  - Many DMU boss action names are `_rsv_` placeholders in the game data (§7).

## 1. Sources

| Source | Version / location | Used for |
|---|---|---|
| cactbot timelines | `OverlayPlugin/cactbot` @ `e79ea7f16093be5b80cda0e2d5bc8eb899d6fb4b` (2026-10-08): `ui/raidboss/data/06-ew/ultimate/the_omega_protocol.txt`, `05-shb/ultimate/the_epic_of_alexander.txt`, `07-dt/ultimate/dancing_mad.txt`. `data/timelines/*.txt` are byte-identical to them (`cmp`). | entries, times, syncs, ability ids, cactbot comments |
| cactbot triggers | same commit: the matching `*.ts` files; `resources/zone_id.ts` | raidwide/tankbuster trigger outputs (`Responses.aoe()`, `bigAoe()`, `tankBuster()` …), zone ids |
| BossMod (BSD-3) | `awgil/ffxiv_bossmod` @ `6e410ef8bb912fc70e3c1f97eedfb77a7dc4dfc0` (2026-11-03): `BossMod.Ultimate/Endwalker/Ultimate/TOP`, `…/Shadowbringers/Ultimate/TEA`, `…/Dawntrail/Ultimate/UMAD` (`*Enums.cs`, `*States.cs`, components) | which id is the damage, cast vs hit, state delays used to time hits that cactbot does not list, `Raidwide`/`Tankbuster` state hints |
| Game data | xivapi/ffxiv-datamining `Action.csv`, `ContentFinderCondition.csv`, `TerritoryType.csv` (current English export) | action names, `Cast100ms`, `CastType`, `EffectRange`, sync levels |
| LPDU TOP Mitigation Sheet | `1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU`, all 13 tabs | row labels (the sheet has no times) |
| Carry the Clear: TOP Healing Plan | `1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s`, tabs `TOP – White Mage` (gid 777100), `TOP – Scholar` (777200), `TOP – WHM + SCH` (777300) | row labels and times ("FFLogs phase timer, checked against the #1 log") |

**How the sheets were read.**
- Both spreadsheets were read in full with the Google Sheets MCP tools: `get_spreadsheet` for the tab list and
  grid sizes, then `get_values` over each tab's whole grid. LPDU tabs are 9–25 rows; the Carry the Clear tabs
  have 1032 rows but data only in rows 1–68.
- The scripted checks read CSV copies of the 16 tabs.
  - Every cell of those copies equals the repository fixtures `tests/fixtures/sheets/**/*.json`: 1189 non-empty
    cells compared, 0 differences.
  - The copies were also compared with the `get_values` output: line by line for the 13 LPDU tabs and the White
    Mage tab, by spot checks for the Scholar and WHM + SCH tabs.
- Carry the Clear's columns A (time) and B (label) are identical in all three tabs from row 7 down (248 cells
  compared). Only row 6 differs: `Mitigation Key:` in the Scholar and WHM + SCH tabs.
- **Row numbers in this note are 1-based sheet rows** (A1 notation), i.e. `rows[row − 1]` in the fixture JSON.

The checks in §0 were run by throwaway scripts: a timeline reader, a catalog validator, a resolver simulation and
the calibration analysis. They are described precisely enough to become the data-validation tests planned in
`docs/DESIGN.md` §8.

## 2. How to read the catalogs

- **`hit` and `entry`.**
  - `hit` is the time of the timeline entry the mechanic is anchored on; `entry` is that entry's name.
  - The anchor is the **damage** a mitigation must cover.
  - Where cactbot has no entry for the damage itself (it is on the timeline's `-ii` ignore line, or not in the file
    at all), the anchor is the entry that marks it: usually the boss cast end.
  - The YAML comment `# anchor:` and §5.1 give the delay to the real hit. Examples: Latent Defect +1.0 s,
    P2 Cosmo Memory +1.4 s, Blue Screen +1.0 s.
- **`hits`** lists every damage time that is itself a timeline entry, so "after the n-th hit" hints can use
  `Mechanic.HitAt(n)`. Sub-hits that cactbot does not list are not invented. Examples: the Splash sub-hits in TEA,
  and the four later Diffuse Wave Cannon Kyrios cleaves in TOP.
- **Umbrella and part mechanics.**
  - Where one sheet row covers several hits that another sheet lists one by one, both exist: an umbrella (for
    example `p1.pantokrator`, all four stacks) and its parts (`p1.panto_stack1..4`).
  - They can share a `hit`; the umbrella is listed first.
  - TOP umbrellas: `p1.program_loop`, `p1.pantokrator`, `p2.limitless`, `p4.wave_cannon`, `p5.delta_all` and
    `p5.omega_all`.
  - `p5.omega_all` starts with the Omega cone dodges because the Carry the Clear row's first time is theirs.
- **`kind`.**
  - `kind` takes the §3.1 values, plus `marker`.
  - `marker` is `MechanicKind.Marker` in `src/MitCue.Core/Fights/FightDefinition.cs` ("Not damage: pull, phase
    start, targetable... (anchors only)"). It is used for the pull, the phase starts, the boss-targetable times and
    (TEA) the death of Alexander Prime: the pseudo-mechanics of `docs/DESIGN.md` §3.1.
  - `marker` is not in the kind list of the task's schema comment, so a loader written against that list must
    accept it (or map it to `other`). Every marker has no damage and `hits == [hit]`.
  - With YamlDotNet 18.1.0 (MitCue.Core's YAML library), a typed deserializer maps `marker` to
    `MechanicKind.Marker` by default, but `lb_check` fails ("Requested value 'lb_check' was not found") unless the
    builder calls `.WithEnumNamingConvention(UnderscoredNamingConvention.Instance)`. With that call all ten values,
    and all three catalogs, deserialize (compiled and run against MitCue.Core's `MechanicKind`).
  - `spread` means every player takes their own hit: proteans, spreads, towers soaked by everyone.
  - `mixed` means different players take different hits at the same moment: towers + tethers, tankbusters + stack.
  - `other` is a dodge that a sheet still lists as a row.
  - The task's example gives the P1 loop sets `kind: raidwide`. They are `mixed` here because Storage Violation
    (7B04, `EffectRange` 3) hits only the tower soaker, and the Blaster AoE (7B0A, `EffectRange` 15) only players
    near a tether target.
- **Aliases**, in order:
  1. Every sheet label mapped to the mechanic, verbatim. Carry the Clear labels keep their damage numbers and
     stars; LPDU labels are trimmed.
  2. Usually the cactbot entry name. It is left out where cactbot reuses it for several mechanics of the phase
     (every P5 Solar Ray's first hit is "Solar Ray 1"). In P6, `Wave Cannon 1`/`Wave Cannon 2` are aliases of the
     1st/2nd Wave Cannon mechanic, although cactbot uses both names for the two protean sets inside each one.
  3. BossMod state names (`Trio 1`–`Trio 3` for the Run: \*\*\*\*mi\* raidwides).
  4. A few plain-text forms.
  5. Community shorthand that appears in neither sheet. It is marked `# unverified` (16 in TOP:
     `Panto`, `DWC`, `PS`, `Synergy`, `PS stacks`, `LS`, `CM`, `Intermission`, `P3 Intermission`, `HW`, `CE`, `OWC`, `BF`, `MN1`, `MN2` (`CM` twice: P2 and P6)).
- **Matching** must normalise alias and label the same way:
  - replace EN and EM dashes (U+2013, U+2014) with `-`;
  - drop `★` (U+2605);
  - collapse whitespace;
  - trim;
  - lower-case;
  - drop a trailing `:`.
  The verbatim aliases then match both sheets exactly; other sheets fall through to the importer's fuzzy matching.

## 3. TOP: candidate damage events (step 1)

Every named entry of `the_omega_protocol.txt`, with its ability id(s), whether the line syncs, the `Action.csv`
facts for each id, and what `top.yaml` does with it. Hidden `--sync--`/`--targetable--` lines are shown only
where they are anchors.

- In "none (`#Ability` comment)" rows, the id comes from the `#Ability {...}` comment. cactbot ignores everything
  after `#`, so these lines are entries without a sync, and their times are cactbot's estimates.
- `Action.csv` cast times show which entries are visual casts and which are the hits. A visual cast has
  `EffectRange` 0 and does no damage. A hit has a range. The cast end of a range-100 *circle* cast (`CastType` 2)
  is usually itself the raidwide; exceptions here are Firewall 7B40 (applies Packet Filter) and Discharger 7B2E
  (knockback). Range-100 crosses, lines and cones (`CastType` 11, 12 and 13: Optimized Blizzard III 7B2D; Optical
  Laser 7B21, Optimized Sagittarius Arrow 7B33, Hyper Pulse 7B70–7B72; the Omega cones 7B79) hit only the players
  inside them.

| Time | Entry | Ability id(s) | Sync | Action.csv: name (cast time, EffectRange) | Role in `top.yaml` |
|---|---|---|---|---|---|
| | **P1: Omega** | | | | |
| 0.0 | --sync-- |  | InCombat |  | ANCHOR of `p1.pull` |
| 15.0 | Program Loop | 7B03 | Ability | 7B03 Program Loop (4 s, r0) | visual cast (mechanic start) |
| 29.1 | Storage Violation 1 | 7B04 | Ability | 7B04 Storage Violation (0 s, r3) | ANCHOR of `p1.program_loop`, `p1.loop_set1` |
| 29.1 | Blaster 1 | 7B08 | Ability | 7B08 Blaster (0 s, r0) | hit of `p1.program_loop`, `p1.loop_set1` |
| 38.1 | Storage Violation 2 | 7B04 | Ability | 7B04 Storage Violation (0 s, r3) | ANCHOR of `p1.loop_set2` (also a hit of `p1.program_loop`) |
| 38.1 | Blaster 2 | 7B08 | Ability | 7B08 Blaster (0 s, r0) | hit of `p1.program_loop`, `p1.loop_set2` |
| 47.1 | Storage Violation 3 | 7B04 | Ability | 7B04 Storage Violation (0 s, r3) | ANCHOR of `p1.loop_set3` (also a hit of `p1.program_loop`) |
| 47.1 | Blaster 3 | 7B08 | Ability | 7B08 Blaster (0 s, r0) | hit of `p1.program_loop`, `p1.loop_set3` |
| 56.1 | Storage Violation 4 | 7B04 | Ability | 7B04 Storage Violation (0 s, r3) | ANCHOR of `p1.loop_set4` (also a hit of `p1.program_loop`) |
| 56.1 | Blaster 4 | 7B09 | Ability | 7B09 Blaster (0 s, r0) | hit of `p1.program_loop`, `p1.loop_set4` |
| 69.2 | Pantokrator | 7B0B | Ability | 7B0B Pantokrator (5 s, r0) | visual cast (mechanic start) |
| 75.3 | Flame Thrower | 7B0D | Ability | 7B0D Flame Thrower (4 s, r65) | dodge (cones) |
| 81.3 | Condensed Wave Cannon Kyrios 1 | 7B0F | Ability | 7B0F Condensed Wave Cannon Kyrios (0 s, r50) | ANCHOR of `p1.pantokrator`, `p1.panto_stack1` |
| 81.3 | Guided Missile Kyrios 1 | 7B0E | Ability | 7B0E Guided Missile Kyrios (0 s, r5) | hit of `p1.pantokrator`, `p1.panto_stack1` |
| 87.3 | Condensed Wave Cannon Kyrios 2 | 7B0F | Ability | 7B0F Condensed Wave Cannon Kyrios (0 s, r50) | ANCHOR of `p1.panto_stack2` (also a hit of `p1.pantokrator`) |
| 87.3 | Guided Missile Kyrios 2 | 7B0E | Ability | 7B0E Guided Missile Kyrios (0 s, r5) | hit of `p1.pantokrator`, `p1.panto_stack2` |
| 93.3 | Condensed Wave Cannon Kyrios 3 | 7B0F | Ability | 7B0F Condensed Wave Cannon Kyrios (0 s, r50) | ANCHOR of `p1.panto_stack3` (also a hit of `p1.pantokrator`) |
| 93.3 | Guided Missile Kyrios 3 | 7B0E | Ability | 7B0E Guided Missile Kyrios (0 s, r5) | hit of `p1.pantokrator`, `p1.panto_stack3` |
| 99.3 | Condensed Wave Cannon Kyrios 4 | 7B0F | Ability | 7B0F Condensed Wave Cannon Kyrios (0 s, r50) | ANCHOR of `p1.panto_stack4` (also a hit of `p1.pantokrator`) |
| 99.3 | Guided Missile Kyrios 4 | 7B0E | Ability | 7B0E Guided Missile Kyrios (0 s, r5) | hit of `p1.pantokrator`, `p1.panto_stack4` |
| 108.0 | Diffuse Wave Cannon Kyrios x5 | 7B10 | none (`#Ability` comment) | 7B10 Diffuse Wave Cannon Kyrios (0 s, r60) | hit of `p1.wave_cannon_kyrios` |
| 108.0 | Wave Cannon Kyrios 1 | 7B11 | Ability | 7B11 Wave Cannon Kyrios (0 s, r50) | ANCHOR of `p1.wave_cannon_kyrios` |
| 116.8 | Wave Cannon Kyrios 2 | 7B11 | Ability | 7B11 Wave Cannon Kyrios (0 s, r50) | hit of `p1.wave_cannon_kyrios` |
| 127.7 | Atomic Ray Enrage | 7AF8 | Ability | 7AF8 Atomic Ray (5 s, r100) | ANCHOR of `p1.atomic_ray` |
| | **P2: Omega-M/F** | | | | |
| 200.0 | --sync-- |  | MapEffect |  | ANCHOR of `p2.start` |
| 202.3 | --targetable-- |  | none |  | ANCHOR of `p2.targetable` |
| 206.4 | Firewall | 7B40 | Ability | 7B40 Firewall (3 s, r100) | applies Packet Filter (no damage is planned for it) |
| 214.5 | Solar Ray | 7E6A | Ability | 7E6A Solar Ray (5 s, r5) | ANCHOR of `p2.solar_ray` |
| 230.9 | Party Synergy | 7B3E | Ability | 7B3E Party Synergy (3 s, r0) | visual cast |
| 241.3 | Superliminal Steel/Optimized Blizzard III | 7B2A, 7B2D | Ability | 7B2A Superliminal Steel (1.5 s, r0); 7B2D Optimized Blizzard III (1.5 s, r100) | dodge |
| 241.3 | Efficient Bladework/Beyond Strength | 7B25, 7B26 | Ability | 7B25 Beyond Strength (1.5 s, r40); 7B26 Efficient Bladework (1.5 s, r10) | dodge |
| 247.7 | Optimized Fire III | 7B2F | Ability | 7B2F Optimized Fire III (0 s, r7) | ANCHOR of `p2.party_synergy_spreads` |
| 248.0 | Optical Laser | 7B21 | Ability | 7B21 Optical Laser (1.3 s, r100) | dodge |
| 254.9 | Discharger | 7B2E | Ability | 7B2E Discharger (0 s, r100) | knockback (not anchored; damage UNVERIFIED) |
| 259.1 | Spotlight x2 | 7B30 | Ability | 7B30 Spotlight (0 s, r6) | ANCHOR of `p2.party_synergy_stacks` |
| 259.1 | Efficient Bladework | 7B26 | Ability | 7B26 Efficient Bladework (1.5 s, r10) | dodge, at the same time as the Spotlight stacks (`p2.party_synergy_stacks`) |
| 272.2 | Synthetic Shield | 7E71 | Ability | 7E71 Synthetic Shield (1 s, r0) | visual |
| 282.4 | Limitless Synergy | 7B38 | Ability | 7B38 Limitless Synergy (5 s, r0) | visual cast |
| 287.4 | Optimized Passage of Arms | 7B44 | Ability | 7B44 Optimized Passage of Arms (0 s, r0) | visual (Omega-F invulnerable) |
| 288.6 | Synthetic Shield | 7E71 | Ability | 7E71 Synthetic Shield (1 s, r0) | visual |
| 295.6 | Optimized Bladedance x2 | 7B34 | Ability | 7B34 Optimized Bladedance (7.9 s, r0) | ANCHOR of `p2.bladedance` |
| 295.7 | Optimized Sagittarius Arrow | 7B33 | Ability | 7B33 Optimized Sagittarius Arrow (8 s, r100) | hit of `p2.bladedance` |
| 306.7 | Beyond Defense | 7B27 | Ability | 7B27 Beyond Defense (4.9 s, r0) | jump on one of the two closest players (hit 7B28 on the -ii line); not anchored |
| 309.7 | Optimized Meteor | 7B31 | Ability | 7B31 Optimized Meteor (8 s, r0) | ANCHOR of `p2.limitless`, `p2.flares` |
| 310.0 | Pile Pitch | 7B29 | Ability | 7B29 Pile Pitch (0 s, r6) | hit of `p2.flares` |
| 318.8 | Cosmo Memory | 7B22 | Ability | 7B22 Cosmo Memory (5 s, r0) | ANCHOR of `p2.cosmo_memory` (also a hit of `p2.limitless`) |
| 347.4 | Laser Shower Enrage | 7B45 | Ability | 7B45 Laser Shower (60 s, r100) | ANCHOR of `p2.laser_shower` |
| | **P3: Omega Reconfigured** | | | | |
| 400.0 | --sync-- | 7B13 | Ability | 7B13 (no name) (0 s, r0) | ANCHOR of `p3.start` |
| 414.3 | Wave Repeater 1 | 7B4F | Ability | 7B4F Wave Repeater (5 s, r6) | dodge (rings) |
| 416.4 | Wave Repeater 2 | 7B50 | Ability | 7B50 Wave Repeater (0 s, r12) | dodge (rings) |
| 418.4 | Wave Repeater 3 | 7B51 | Ability | 7B51 Wave Repeater (0 s, r18) | dodge (rings) |
| 420.4 | Wave Repeater 4 | 7B52 | Ability | 7B52 Wave Repeater (0 s, r24) | dodge (rings) |
| 422.4 | Wave Repeater 1 | 7B4F | Ability | 7B4F Wave Repeater (5 s, r6) | dodge (rings) |
| 424.5 | Wave Repeater 2 | 7B50 | Ability | 7B50 Wave Repeater (0 s, r12) | dodge (rings) |
| 426.4 | Colossal Blow | 7B4E | Ability | 7B4E Colossal Blow (2 s, r11) | dodge |
| 426.6 | Wave Repeater 3 | 7B51 | Ability | 7B51 Wave Repeater (0 s, r18) | dodge (rings) |
| 427.3 | High-powered Sniper Cannon x2 | 7B54 | none (`#Ability` comment) | 7B54 High-powered Sniper Cannon (0 s, r6) | ANCHOR of `p3.transition` |
| 428.4 | Sniper Cannon x4 | 7B53 | none (`#Ability` comment) | 7B53 Sniper Cannon (0 s, r6) | hit of `p3.transition` |
| 428.7 | Wave Repeater 4 | 7B52 | Ability | 7B52 Wave Repeater (0 s, r24) | dodge (rings) |
| 428.9 | Colossal Blow | 7B4E | Ability | 7B4E Colossal Blow (2 s, r11) | dodge |
| 432.3 | --targetable-- |  | none |  | ANCHOR of `p3.targetable` |
| 441.3 | Hello, World | 7B55 | Ability | 7B55 Hello, World (5 s, r100) | ANCHOR of `p3.hello_world` |
| 464.4 | Latent Defect 1 | 7B6F | Ability | 7B6F Latent Defect (9 s, r0) | ANCHOR of `p3.latent_1` |
| 466.9 | Patch x2 | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | hit of `p3.latent_1` |
| 485.4 | Latent Defect 2 | 7B6F | Ability | 7B6F Latent Defect (9 s, r0) | ANCHOR of `p3.latent_2` |
| 487.9 | Patch x2 | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | hit of `p3.latent_2` |
| 506.5 | Latent Defect 3 | 7B6F | Ability | 7B6F Latent Defect (9 s, r0) | ANCHOR of `p3.latent_3` |
| 509.0 | Patch x2 | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | hit of `p3.latent_3` |
| 527.5 | Patch | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | tether break raidwide, unsynced (see the P3 rounds / Delta rows) |
| 527.8 | Latent Defect 4 | 7B6F | Ability | 7B6F Latent Defect (9 s, r0) | ANCHOR of `p3.latent_4` |
| 533.5 | Patch | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | hit of `p3.latent_4` |
| 550.1 | Critical Error | 7B64 | Ability | 7B64 Critical Error (8 s, r100) | ANCHOR of `p3.critical_error` |
| 571.4 | Oversampled Wave Cannon | 7B6B, 7B6C | Ability | 7B6B Oversampled Wave Cannon (10 s, r0); 7B6C Oversampled Wave Cannon (10 s, r0) | ANCHOR of `p3.monitors` |
| 587.4 | Ion Efflux Enrage | 7B48 | Ability | 7B48 Ion Efflux (10 s, r100) | ANCHOR of `p3.ion_efflux` |
| | **P4: Blue Screen** | | | | |
| 600.0 | --sync-- | 7B47 | Ability | 7B47 (no name) (0 s, r0) | ANCHOR of `p4.start` |
| 607.1 | --targetable-- |  | none |  | ANCHOR of `p4.targetable` |
| 621.4 | Wave Cannon A | 7B81 | Ability | 7B81 Wave Cannon (5 s, r0) | ANCHOR of `p4.wave_cannon`, `p4.protean_1` |
| 626.7 | Wave Cannon B | 7F16 | Ability | 7F16 Wave Cannon (0 s, r0) | boss visual; the baited laser 7B80 and the stack 7B7F land at about this time |
| 627.0 | Wave Cannon (stacks) | 7B7F | Ability | 7B7F Wave Cannon (0 s, r100) | ANCHOR of `p4.stack_1` (also a hit of `p4.wave_cannon`) |
| 629.4 | Wave Repeater 1 | 7B4F | Ability | 7B4F Wave Repeater (5 s, r6) | dodge (rings) |
| 631.5 | Wave Repeater 2 | 7B50 | Ability | 7B50 Wave Repeater (0 s, r12) | dodge (rings), at the same time as `Wave Cannon A` (`p4.protean_2`) |
| 631.5 | Wave Cannon A | 7F16 | Ability | 7F16 Wave Cannon (0 s, r0) | ANCHOR of `p4.protean_2` (also a hit of `p4.wave_cannon`) |
| 633.5 | Wave Repeater 3 | 7B51 | Ability | 7B51 Wave Repeater (0 s, r18) | dodge (rings) |
| 635.5 | Wave Repeater 4 | 7B52 | Ability | 7B52 Wave Repeater (0 s, r24) | dodge (rings) |
| 636.6 | Wave Cannon B | 7B82 | Ability | 7B82 Wave Cannon (0 s, r0) | boss visual; the baited laser 7B80 and the stack 7B7F land at about this time |
| 636.8 | Wave Cannon (stacks) | 7B7F | Ability | 7B7F Wave Cannon (0 s, r100) | ANCHOR of `p4.stack_2` (also a hit of `p4.wave_cannon`) |
| 641.6 | Wave Cannon A | 7B83 | Ability | 7B83 Wave Cannon (0 s, r0) | ANCHOR of `p4.protean_3` (also a hit of `p4.wave_cannon`) |
| 646.6 | Wave Repeater 1 | 7B4F | Ability | 7B4F Wave Repeater (5 s, r6) | dodge (rings) |
| 646.7 | Wave Cannon B | 7B84 | Ability | 7B84 Wave Cannon (0 s, r0) | boss visual; the baited laser 7B80 and the stack 7B7F land at about this time |
| 646.9 | Wave Cannon (stacks) | 7B7F | Ability | 7B7F Wave Cannon (0 s, r100) | ANCHOR of `p4.stack_3` (also a hit of `p4.wave_cannon`) |
| 648.6 | Wave Repeater 2 | 7B50 | Ability | 7B50 Wave Repeater (0 s, r12) | dodge (rings) |
| 650.6 | Wave Repeater 3 | 7B51 | Ability | 7B51 Wave Repeater (0 s, r18) | dodge (rings) |
| 652.6 | Wave Repeater 4 | 7B52 | Ability | 7B52 Wave Repeater (0 s, r24) | dodge (rings) |
| 661.6 | Blue Screen Enrage | 7B7B | Ability | 7B7B Blue Screen (8 s, r0) | ANCHOR of `p4.blue_screen` |
| | **P5: Run: Dynamis** | | | | |
| 700.0 | --sync-- | 7B7C | Ability | 7B7C Blue Screen (1 s, r100) | ANCHOR of `p5.start` |
| 715.6 | --targetable-- |  | none |  | ANCHOR of `p5.targetable` |
| 723.6 | Solar Ray 1 | 81AC | Ability | 81AC Solar Ray (5 s, r5) | ANCHOR of `p5.solar_ray_1` |
| 726.8 | Solar Ray 2 | 7B01 | Ability | 7B01 Solar Ray (0 s, r5) | hit of `p5.solar_ray_1` |
| 740.0 | Run: \*\*\*\*mi\* (Delta Version) | 7B88 | Ability | 7B88 Run: \*\*\*\*mi\* (Delta Version) (5 s, r100) | ANCHOR of `p5.delta` |
| 750.2 | Peripheral Synthesis | 7B8C | Ability | 7B8C Peripheral Synthesis (0 s, r0) | visual |
| 753.2 | Archive Peripheral | 7F76 | Ability | 7F76 Archive Peripheral (0 s, r0) | visual |
| 761.2 | Patch | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | ANCHOR of `p5.delta_all`, `p5.delta_patch` |
| 763.2 | Optical Laser | 7B21 | Ability | 7B21 Optical Laser (1.3 s, r100) | dodge |
| 764.9 | Patch | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | hit of `p5.delta_all`, `p5.delta_patch` |
| 766.4 | Explosion | 7AFA | Ability | 7AFA Explosion (3 s, r3) | rocket punch puddles (bait/dodge) |
| 768.5 | Beyond Defense | 7B27 | Ability | 7B27 Beyond Defense (4.9 s, r0) | jump on one of the two closest players (hit 7B28 on the -ii line); not anchored |
| 771.1 | Hyper Pulse | 7B70 | Ability | 7B70 Hyper Pulse (2.5 s, r100) | ANCHOR of `p5.delta_monitors` (also a hit of `p5.delta_all`) |
| 771.8 | Hyper Pulse | 7B71 | none (`#Ability` comment) | 7B71 Hyper Pulse (0 s, r100) | rotating laser repeats (part of p5.delta_monitors) |
| 772.4 | Hyper Pulse | 7B71 | none (`#Ability` comment) | 7B71 Hyper Pulse (0 s, r100) | rotating laser repeats (part of p5.delta_monitors) |
| 773.0 | Hyper Pulse | 7B71 | none (`#Ability` comment) | 7B71 Hyper Pulse (0 s, r100) | rotating laser repeats (part of p5.delta_monitors) |
| 773.6 | Hyper Pulse | 7B71 | none (`#Ability` comment) | 7B71 Hyper Pulse (0 s, r100) | hit of `p5.delta_all`, `p5.delta_monitors` |
| 773.6 | Oversampled Wave Cannon | 7B6D | Ability | 7B6D Oversampled Wave Cannon (0 s, r7) | hit of `p5.delta_all`, `p5.delta_monitors` |
| 774.0 | Pile Pitch | 7B29 | Ability | 7B29 Pile Pitch (0 s, r6) | hit of `p5.delta_all`, `p5.delta_monitors` |
| 774.0 | Hyper Pulse | 7B71 | none (`#Ability` comment) | 7B71 Hyper Pulse (0 s, r100) | hit of `p5.delta_all`, `p5.delta_monitors` |
| 782.0 | Patch | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | hit of `p5.delta_all`, `p5.delta_patch` |
| 784.0 | Patch | 7B63 | none (`#Ability` comment) | 7B63 Patch (0 s, r100) | hit of `p5.delta_all`, `p5.delta_patch` |
| 786.4 | Swivel Cannon | 7B94, 7B95 | Ability | 7B94 Swivel Cannon (10 s, r60); 7B95 Swivel Cannon (10 s, r60) | dodge (cleave) |
| 787.0 | Hello, Distant World 1 | 8110 | Ability | 8110 Hello, Distant World (0 s, r8) | ANCHOR of `p5.delta_hello_world` (also a hit of `p5.delta_all`) |
| 787.0 | Hello, Near World 1 | 7B89 | Ability | 7B89 Hello, Near World (0 s, r8) | hit of `p5.delta_all`, `p5.delta_hello_world` |
| 788.0 | Hello, Distant World 2 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.delta_all`, `p5.delta_hello_world` |
| 788.0 | Hello, Near World 2 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.delta_all`, `p5.delta_hello_world` |
| 789.0 | Hello, Distant World 3 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.delta_all`, `p5.delta_hello_world` |
| 789.0 | Hello, Near World 3 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.delta_all`, `p5.delta_hello_world` |
| 805.2 | Solar Ray 1 | 81AC | Ability | 81AC Solar Ray (5 s, r5) | ANCHOR of `p5.solar_ray_2` |
| 808.5 | Solar Ray 2 | 7B01 | Ability | 7B01 Solar Ray (0 s, r5) | hit of `p5.solar_ray_2` |
| 821.7 | Run: \*\*\*\*mi\* (Sigma Version) | 8014 | Ability | 8014 Run: \*\*\*\*mi\* (Sigma Version) (5 s, r100) | ANCHOR of `p5.sigma` |
| 839.0 | Subject Simulation F | 7F2F | Ability | 7F2F Subject Simulation F (0 s, r0) | visual |
| 840.4 | Program Loop | 7B98 | Ability | 7B98 Program Loop (0 s, r0) | visual cast (mechanic start) |
| 843.8 | Hyper Pulse | 7B72 | Ability | 7B72 Hyper Pulse (0 s, r100) | ANCHOR of `p5.sigma_proteans` |
| 844.0 | Wave Cannon | 7B73 | Ability | 7B73 Wave Cannon (8 s, r0) | hit of `p5.sigma_proteans` |
| 852.4 | Discharger | 7B2E | Ability | 7B2E Discharger (0 s, r100) | knockback (not anchored; damage UNVERIFIED) |
| 856.4 | Storage Violation x5 / Storage Violation x6 | 7B0[45] | Ability | 7B04 Storage Violation (0 s, r3); 7B05 Storage Violation (0 s, r3) | ANCHOR of `p5.sigma_towers` |
| 870.6 | Rear Lasers x14 | 7B8F | Ability | 7B8F Rear Lasers (3 s, r50) | dodge |
| 873.7 | Superliminal Steel/Optimized Blizzard III | 7B2A, 7B2D | Ability | 7B2A Superliminal Steel (1.5 s, r0); 7B2D Optimized Blizzard III (1.5 s, r100) | dodge |
| 880.6 | Hello, Near World 1 | 7B89 | Ability | 7B89 Hello, Near World (0 s, r8) | ANCHOR of `p5.sigma_hello_world` |
| 880.6 | Hello, Distant World 1 | 8110 | Ability | 8110 Hello, Distant World (0 s, r8) | hit of `p5.sigma_hello_world` |
| 880.6 | Hyper Pulse | 7B72 | Ability | 7B72 Hyper Pulse (0 s, r100) | hit of `p5.sigma_hello_world` |
| 881.6 | Hello, Near World 2 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.sigma_hello_world` |
| 881.6 | Hello, Distant World 2 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.sigma_hello_world` |
| 882.6 | Hello, Near World 3 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.sigma_hello_world` |
| 882.6 | Hello, Distant World 3 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.sigma_hello_world` |
| 894.3 | Solar Ray 1 | 81AD | Ability | 81AD Solar Ray (5 s, r5) | ANCHOR of `p5.solar_ray_3` |
| 897.4 | Solar Ray 2 | 7B02 | Ability | 7B02 Solar Ray (0 s, r5) | hit of `p5.solar_ray_3` |
| 910.7 | Run: \*\*\*\*mi\* (Omega Version) | 8015 | Ability | 8015 Run: \*\*\*\*mi\* (Omega Version) (5 s, r100) | ANCHOR of `p5.omega` |
| 927.8 | Diffuse Wave Cannon | 7B9B, 7B9C | Ability | 7B9B Diffuse Wave Cannon (8 s, r0); 7B9C Diffuse Wave Cannon (8 s, r0) | ANCHOR of `p5.omega_all`, `p5.omega_cannons` |
| 931.9 | Diffuse Wave Cannon | 7B78, 7B77 | Ability | 7B78 Diffuse Wave Cannon (0 s, r0); 7B77 Diffuse Wave Cannon (0 s, r0) | hit of `p5.omega_all`, `p5.omega_cannons` |
| 945.4 | Hello, Distant World 1 | 8110 | Ability | 8110 Hello, Distant World (0 s, r8) | ANCHOR of `p5.omega_hello_world_1` (also a hit of `p5.omega_all`) |
| 945.4 | Hello, Near World 1 | 7B89 | Ability | 7B89 Hello, Near World (0 s, r8) | hit of `p5.omega_all`, `p5.omega_hello_world_1` |
| 945.8 | Oversampled Wave Cannon | 7B6D | Ability | 7B6D Oversampled Wave Cannon (0 s, r7) | hit of `p5.omega_all`, `p5.omega_hello_world_1` |
| 946.4 | Hello, Distant World 2 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_1` |
| 946.4 | Hello, Near World 2 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_1` |
| 947.4 | Hello, Distant World 3 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_1` |
| 947.4 | Hello, Near World 3 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_1` |
| 961.6 | Blaster x2 | 7E75 | Ability | 7E75 Blaster (0 s, r15) | ANCHOR of `p5.omega_hello_world_2` (also a hit of `p5.omega_all`) |
| 963.4 | Hello, Distant World 1 | 8110 | Ability | 8110 Hello, Distant World (0 s, r8) | hit of `p5.omega_all`, `p5.omega_hello_world_2` |
| 963.4 | Hello, Near World 1 | 7B89 | Ability | 7B89 Hello, Near World (0 s, r8) | hit of `p5.omega_all`, `p5.omega_hello_world_2` |
| 964.4 | Hello, Distant World 2 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_2` |
| 964.4 | Hello, Near World 2 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_2` |
| 965.4 | Hello, Distant World 3 | 8111 | none (`#Ability` comment) | 8111 Hello, Distant World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_2` |
| 965.4 | Hello, Near World 3 | 7B8A | none (`#Ability` comment) | 7B8A Hello, Near World (0 s, r4) | hit of `p5.omega_all`, `p5.omega_hello_world_2` |
| 976.8 | Solar Ray 1 | 81AD | Ability | 81AD Solar Ray (5 s, r5) | ANCHOR of `p5.solar_ray_4` |
| 979.9 | Solar Ray 2 | 7B02 | Ability | 7B02 Solar Ray (0 s, r5) | hit of `p5.solar_ray_4` |
| 998.5 | Blind Faith | 7B87 | Ability | 7B87 Blind Faith (10 s, r0) | visual cast end; the hit is 1.2 s later (p5.blind_faith) |
| 999.7 | Blind Faith Enrage | 7F73 | Ability | 7F73 Blind Faith (1.2 s, r100) | ANCHOR of `p5.blind_faith` |
| | **P6: Alpha Omega** | | | | |
| 1100.0 | --sync-- | 7F72 | Ability | 7F72 Blind Faith (1.2 s, r100) | ANCHOR of `p6.start` |
| 1154.7 | --targetable-- |  | none |  | ANCHOR of `p6.targetable` |
| 1167.8 | Cosmo Memory | 7BA1 | Ability | 7BA1 Cosmo Memory (6 s, r100) | ANCHOR of `p6.cosmo_memory` |
| 1172.8 | Flash Gale 1 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1176.0 | Flash Gale 2 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1182.2 | Cosmo Arrow | 7BA2 | Ability | 7BA2 Cosmo Arrow (6 s, r0) | ANCHOR of `p6.cosmo_arrow_1` |
| 1196.9 | Cosmo Dive | 7BA6 | Ability | 7BA6 Cosmo Dive (5.6 s, r0) | visual cast end; the hits are 2.4 s later (p6.cosmo_dive_N) |
| 1199.3 | Cosmo Dive Far | 7BA8 | none (`#Ability` comment) | 7BA8 Cosmo Dive (0 s, r6) | ANCHOR of `p6.cosmo_dive_1` |
| 1199.3 | Cosmo Dive Near x2 | 7BA7 | none (`#Ability` comment) | 7BA7 Cosmo Dive (0 s, r8) | hit of `p6.cosmo_dive_1` |
| 1207.0 | Flash Gale 1 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1210.2 | Flash Gale 2 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1215.4 | Unlimited Wave Cannon | 7BAC | Ability | 7BAC Unlimited Wave Cannon (5 s, r0) | visual cast (exaflares + baited puddles follow) |
| 1223.4 | Wave Cannon Puddle 1 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | baited puddle (dodge) |
| 1225.2 | Wave Cannon Puddle 2 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | baited puddle (dodge) |
| 1227.1 | Wave Cannon Puddle 3 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | baited puddle (dodge) |
| 1229.0 | Wave Cannon Puddle 4 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | baited puddle (dodge) |
| 1230.9 | Wave Cannon Puddle 5 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | baited puddle (dodge) |
| 1232.9 | Wave Cannon Puddle 6 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | baited puddle (dodge) |
| 1236.0 | Wave Cannon 1 | 7BAB | none (`#Ability` comment) | 7BAB Wave Cannon (0 s, r100) | ANCHOR of `p6.wave_cannon_1` |
| 1238.0 | Wave Cannon 2 | 7BAB | none (`#Ability` comment) | 7BAB Wave Cannon (0 s, r100) | hit of `p6.wave_cannon_1` |
| 1244.3 | Wave Cannon (Wild Charge) | 7BAA | none (`#Ability` comment) | 7BAA Wave Cannon (0 s, r100) | hit of `p6.wave_cannon_1` |
| 1249.3 | Flash Gale 1 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1252.5 | Flash Gale 2 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1258.7 | Cosmo Arrow | 7BA2 | Ability | 7BA2 Cosmo Arrow (6 s, r0) | visual cast end; exasquares (dodge) and the Wave Cannon 2 proteans follow (CtC "Cosmo Arrow 2" = `p6.wave_cannon_2`) |
| 1269.9 | Wave Cannon 1 | 7BAB | none (`#Ability` comment) | 7BAB Wave Cannon (0 s, r100) | ANCHOR of `p6.wave_cannon_2` |
| 1271.9 | Wave Cannon 2 | 7BAB | none (`#Ability` comment) | 7BAB Wave Cannon (0 s, r100) | hit of `p6.wave_cannon_2` |
| 1278.2 | Wave Cannon (Wild Charge) | 7BAA | none (`#Ability` comment) | 7BAA Wave Cannon (0 s, r100) | hit of `p6.wave_cannon_2` |
| 1283.2 | Flash Gale 1 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1286.4 | Flash Gale 2 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1291.7 | Unlimited Wave Cannon | 7BAC | Ability | 7BAC Unlimited Wave Cannon (5 s, r0) | visual cast (exaflares + baited puddles follow) |
| 1299.7 | Wave Cannon Puddle 1 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | ANCHOR of `p6.unlimited_wave_cannon_2` |
| 1301.5 | Wave Cannon Puddle 2 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | hit of `p6.unlimited_wave_cannon_2` |
| 1303.4 | Wave Cannon Puddle 3 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | hit of `p6.unlimited_wave_cannon_2` |
| 1305.3 | Wave Cannon Puddle 4 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | hit of `p6.unlimited_wave_cannon_2` |
| 1307.2 | Wave Cannon Puddle 5 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | hit of `p6.unlimited_wave_cannon_2` |
| 1309.2 | Wave Cannon Puddle 6 | 7BAF | none (`#Ability` comment) | 7BAF Wave Cannon (3 s, r6) | hit of `p6.unlimited_wave_cannon_2` |
| 1310.0 | Cosmo Dive | 7BA6 | Ability | 7BA6 Cosmo Dive (5.6 s, r0) | visual cast end; the hits are 2.4 s later (p6.cosmo_dive_N) |
| 1312.4 | Cosmo Dive Far | 7BA8 | none (`#Ability` comment) | 7BA8 Cosmo Dive (0 s, r6) | ANCHOR of `p6.cosmo_dive_2` |
| 1312.4 | Cosmo Dive Near x2 | 7BA7 | none (`#Ability` comment) | 7BA7 Cosmo Dive (0 s, r8) | hit of `p6.cosmo_dive_2` |
| 1320.1 | Flash Gale 1 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1323.2 | Flash Gale 2 | 7DDF | Ability | 7DDF (no name) (0 s, r5) | tank auto-attacks (main target + farthest); not anchored |
| 1331.6 | Cosmo Meteor | 7BB0 | Ability | 7BB0 Cosmo Meteor (5 s, r0) | visual cast / visual end (no damage) |
| 1335.6 | Cosmo Meteor Baits | 7BB2 | Ability | 7BB2 Cosmo Meteor (4 s, r10) | ANCHOR of `p6.cosmo_meteor` |
| 1336.7 | Cosmo Meteor 1 x4 | 7FBB | none (`#Ability` comment) | 7FBB Cosmo Meteor (0 s, r5) | hit of `p6.cosmo_meteor` |
| 1337.7 | Cosmo Meteor 2 x4 | 7FBB | none (`#Ability` comment) | 7FBB Cosmo Meteor (0 s, r5) | hit of `p6.cosmo_meteor` |
| 1342.7 | Cosmo Meteor 1 x4 | 7FBB | none (`#Ability` comment) | 7FBB Cosmo Meteor (0 s, r5) | hit of `p6.cosmo_meteor` |
| 1343.7 | Cosmo Meteor 2 x4 | 7FBB | none (`#Ability` comment) | 7FBB Cosmo Meteor (0 s, r5) | hit of `p6.cosmo_meteor` |
| 1354.7 | Cosmo Meteor Stack | 7BB3 | Ability | 7BB3 Cosmo Meteor (0 s, r6) | hit of `p6.flares` |
| 1354.7 | Cosmo Meteor Flare x3 | 7BB4 | Ability | 7BB4 Cosmo Meteor (0 s, r100) | ANCHOR of `p6.flares` |
| 1355.7 | Cosmo Meteor | 7BB1 | Ability | 7BB1 Cosmo Meteor (0 s, r0) | visual cast / visual end (no damage) |
| 1357.0 | Cosmo Meteor Enrage | 7BB5 | Ability | 7BB5 Cosmo Meteor (0 s, r100) | comet enrage (only if the comets survive) |
| 1366.7 | Magic Number | 7BB6 | Ability | 7BB6 Magic Number (5 s, r100) | ANCHOR of `p6.magic_number_1` |
| 1382.8 | Magic Number | 7BB6 | Ability | 7BB6 Magic Number (5 s, r100) | ANCHOR of `p6.magic_number_2` |
| 1421.9 | Run: \*\*\*\*mi\* Enrage | 7BA0 | Ability | 7BA0 Run: \*\*\*\*mi\* (16 s, r100) | ANCHOR of `p6.run_mi_enrage` |

**Damage on the `-ii` ignore line** (timeline header, line 3) that matters for mitigation, and the anchors that
stand in for it. Ids and roles are from BossMod `TOPEnums.cs` and `Action.csv`. The rest of that line is
auto-attacks, visual casts, dodges (Flame Thrower repeats, Ballistic Impact, Superliminal Steel halves, exaflare
and Rear Lasers repeats) and fail effects (Unmitigated Explosion, Patch and Hello World wipes, Blue Screen fail).

| Id | Action.csv | BossMod | Stands in for it |
|---|---|---|---|
| 7B0A | Blaster, EffectRange 15 | `BlasterAOE`, aoe around each tether target | `Blaster N` / `p1.loop_setN` |
| 7E6B | Solar Ray (Omega-F), 5 s cast | `SolarRayF`, tankbuster | `Solar Ray` 7E6A, same time |
| 7F75 | Optimized Bladedance, EffectRange 100 | `OptimizedBladedanceAOE`, tethered cone | `Optimized Bladedance x2`, same time |
| 7B28 | Beyond Defense, EffectRange 5 | jump on one of the two closest players | not anchored |
| 7B32 | Optimized Meteor, EffectRange 100 | flares, raidwide with falloff | `Optimized Meteor`, same time |
| 7B23/7B24 | Cosmo Memory (7B24: 1.5 s cast, EffectRange 100) | 7B24 = `CosmoMemoryAOE`, raidwide | `Cosmo Memory` 7B22, about 1.4 s earlier |
| 7B56/7B57 | Critical Synchronization Bug / Critical Overflow Bug | Hello World stacks / defamations | `Latent Defect N`, 1.0 s earlier |
| 7B5A/7B5B | Critical Underflow / Performance Bug | rot explosions | – |
| 7B5F/7B60 | Cascading Latent Defect / Latent Performance Defect | red/blue towers | `Latent Defect N`, 1.0 s earlier |
| 7B6D | Oversampled Wave Cannon, EffectRange 7 | monitor spreads | P3 `Oversampled Wave Cannon` (+0.1 s); synced in P5 |
| 7B7E/7B80 | Wave Cannon | P4 protean lasers / baited lasers | `Wave Cannon A` (+0.5–0.9 s) / `Wave Cannon (stacks)` (−0.2 s) |
| 7B74 | Wave Cannon (cone) | Sigma protean cones | Sigma `Wave Cannon` 7B73, same time |
| 7B79 | Diffuse Wave Cannon (1 s cast) | Omega cones | `Diffuse Wave Cannon` 7B9B/7B9C, +1.1 s |

Flash Gale (P6 tank auto-attacks, 7DDF) is synced but not anchored: neither sheet plans for it. The Carry the
Clear White Mage notes mention "tank autos at 1:13 and 1:16", which are the 1172.8/1176.0 Flash Gales.

## 4. TOP: sheet mapping and calibration (step 2)

### 4.1 Carry the Clear: every mechanic row with its offset

Rows 17–68. The three tabs (White Mage, Scholar, WHM + SCH) have identical columns A and B (§1), so each row is
listed once for all three.

- `Sheet time` is the row's time cell. Multi-time cells (`a / b / c`) and ranges (`a – b`) give one line per
  time; row 40's six times are in its label.
- `Offset = timeline time − sheet time` within the row's phase.
- **Q** grades the pair and decides whether it is used for calibration:
  - **A**: synced cactbot entry.
  - **B**: unsynced (`#Ability`) entry.
  - **C**: approximate entry (cactbot calls it sloppy or player-timed).
  - **X**: the sheet times a different event than the catalog anchor (cast end vs damage, §4.2); excluded.
  - **E**: enrage row, a time no kill log reaches; excluded.
  - **D**: dodge row; excluded.
  - **R**: range end without a timeline entry; excluded.

| Sheet | Tabs | Row | Row label (verbatim) | Sheet time | Mechanic id | Timeline time | Offset | Q | Note |
|---|---|---|---|---|---|---|---|---|---|
| Carry the Clear | White Mage, Scholar, WHM + SCH | 17 | Program Loop: towers (36k) + blasters (HP to 1%) ★ | 0:28 | `p1.program_loop` | 29.1 | 1.1 | A | set 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:37 | `p1.program_loop` | 38.1 | 1.1 | A | set 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:46 | `p1.program_loop` | 47.1 | 1.1 | A | set 3 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:55 | `p1.program_loop` | 56.1 | 1.1 | A | set 4 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 18 | Pantokrator: line stacks + missiles (65k) ★★★ (multi-hit) | 1:21 | `p1.pantokrator` | 81.3 | 0.3 | A | stack 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:27 | `p1.pantokrator` | 87.3 | 0.3 | A | stack 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:33 | `p1.pantokrator` | 93.3 | 0.3 | A | stack 3 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:39 | `p1.pantokrator` | 99.3 | 0.3 | A | stack 4 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 19 | Wave Cannon Kyrios (35k) + Diffuse Wave Cannon Kyrios tank cleaves (60k ×5) ★★ | 1:47 | `p1.wave_cannon_kyrios` | 108.0 | 1.0 | A | range start = Wave Cannon Kyrios 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:56 | `p1.wave_cannon_kyrios` | 116.8 | 0.8 | A | range end = Wave Cannon Kyrios 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 20 | Atomic Ray (P1 enrage — push before) | 2:08 | `p1.atomic_ray` | 127.7 | -0.3 | E | enrage (no kill log reaches it) |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 22 | Solar Ray ×2 (180k on both tanks) ★★ (tankbuster) | 0:12 | `p2.solar_ray` | 214.5 | 202.5 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 23 | Party Synergy: Optimized Fire III spreads (35k) | 0:46 | `p2.party_synergy_spreads` | 247.7 | 201.7 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 24 | Efficient Bladework + Spotlight stacks (60k, 3 players) ★ | 0:57 | `p2.party_synergy_stacks` | 259.1 | 202.1 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 25 | Limitless Synergy: Bladedance (tanks) + Sagittarius Arrow (55k) ★ | 1:34 | `p2.bladedance` | 295.6 | 201.6 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 26 | Optimized Meteor flares + Pile Pitch ★★★ | 1:48 | `p2.flares` | 309.7 | 201.7 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 27 | Cosmo Memory ×2 (100k raidwide) ★★★ | 1:57 | `p2.cosmo_memory` | 318.8 | 201.8 | A | cast end; the 7B24 raidwide (~320.2) would give 203.2, see 4.2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 28 | Laser Shower (P2 enrage — push before) | 2:25 | `p2.laser_shower` | 347.4 | 202.4 | E | enrage |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 30 | Transition: spreads (74k) + stacks (78k) ★★ | 0:28 | `p3.transition` | 427.3 | 399.3 | C | High-powered Sniper Cannon (unsynced, "sloppy"); Sniper Cannon x4 @428.4 gives 400.4 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 31 | Hello, World (62k raidwide) ★★ | 0:42 | `p3.hello_world` | 441.3 | 399.3 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 32 | Round 1: towers, defamations (40k), stacks (41k), tethers (21k ×2) ★★ | 1:05 | `p3.latent_1` | 464.4 | 399.4 | A | range start = Latent Defect cast end |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:12 | `p3.latent_1` | - | - | R | range end: rot explosions, not an entry |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 33 | Round 2 ★★ | 1:26 | `p3.latent_2` | 485.4 | 399.4 | A | range start |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:35 | `p3.latent_2` | - | - | R | range end |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 34 | Round 3 ★★ | 1:47 | `p3.latent_3` | 506.5 | 399.5 | A | range start |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:57 | `p3.latent_3` | - | - | R | range end |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 35 | Round 4 (party shares the stacks) ★★ | 2:08 | `p3.latent_4` | 527.8 | 399.8 | A | range start |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 2:18 | `p3.latent_4` | - | - | R | range end |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 36 | Critical Error (89k raidwide) ★★★ | 2:30 | `p3.critical_error` | 550.1 | 400.1 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 37 | Oversampled Wave Cannon: monitors (52k) ★ | 2:52 | `p3.monitors` | 571.4 | 399.4 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 38 | Ion Efflux (P3 enrage — push before) | 3:08 | `p3.ion_efflux` | 587.4 | 399.4 | E | enrage |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 40 | Wave Cannon ×6: lasers on everyone (36k) at 0:15 / 0:25 / 0:35, stacks (56k) at 0:20 / 0:30 / 0:40 ★★ (multi-hit) | 0:15 | `p4.wave_cannon` | 621.4 | 606.4 | A | lasers 1 ("Wave Cannon A" visual; lasers ~+0.6 s) |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:20 | `p4.wave_cannon` | 627.0 | 607.0 | A | stack 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:25 | `p4.wave_cannon` | 631.5 | 606.5 | A | lasers 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:30 | `p4.wave_cannon` | 636.8 | 606.8 | A | stack 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:35 | `p4.wave_cannon` | 641.6 | 606.6 | A | lasers 3 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:40 | `p4.wave_cannon` | 646.9 | 606.9 | A | stack 3 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 41 | Blue Screen (76k, phase end) ★ | 0:56 | `p4.blue_screen` | 661.6 | 605.6 | X | entry = 7B7B cast end; the hit 7B7C lands 1.0 s later (gives 606.6) |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 43 | Solar Ray ×2 (290k tankbuster) ★★★ | 0:08 | `p5.solar_ray_1` | 723.6 | 715.6 | A | hit 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 0:11 | `p5.solar_ray_1` | 726.8 | 715.8 | A | hit 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 44 | Run: \*\*\*\*mi\* (Delta): 140k raidwide ★★★ | 0:25 | `p5.delta` | 740.0 | 715.0 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 45 | Delta: tethers (26k), monitors + Hyper Pulse + Pile Pitch (50k), Hello Near/Distant World (50k) ★ | 0:46 | `p5.delta_all` | 761.2 | 715.2 | C | first Patch (unsynced, player-timed) |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:14 | `p5.delta_all` | 789.0 | 715.0 | B | range end = Hello Near/Distant World 3 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 46 | Solar Ray ×2 ★★★ | 1:30 | `p5.solar_ray_2` | 805.2 | 715.2 | A | hit 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:33 | `p5.solar_ray_2` | 808.5 | 715.5 | A | hit 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 47 | Run: \*\*\*\*mi\* (Sigma): 140k raidwide ★★★ | 1:47 | `p5.sigma` | 821.7 | 714.7 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 48 | Sigma: Hyper Pulse (68k) + Wave Cannon (73k) ★★ | 2:09 | `p5.sigma_proteans` | 843.8 | 714.8 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 49 | Storage Violation towers (33k) | 2:22 | `p5.sigma_towers` | 856.4 | 714.4 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 50 | Hello Near/Distant World (50k) + Hyper Pulse (68k) ★★ | 2:46 | `p5.sigma_hello_world` | 880.6 | 714.6 | A | range start |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 2:48 | `p5.sigma_hello_world` | 882.6 | 714.6 | B | range end = 3rd jump |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 51 | Solar Ray ×2 ★★★ | 3:00 | `p5.solar_ray_3` | 894.3 | 714.3 | A | hit 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 3:03 | `p5.solar_ray_3` | 897.4 | 714.4 | A | hit 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 52 | Run: \*\*\*\*mi\* (Omega): 140k raidwide ★★★ | 3:17 | `p5.omega` | 910.7 | 713.7 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 53 | Omega: Diffuse Wave Cannon, monitors + Hello World (50k), blasters (50k) ★ | 3:35 | `p5.omega_all` | 927.8 | 712.8 | X | entry = 8 s cast end; the cones land 1.1 s later (gives 713.9) |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 4:12 | `p5.omega_all` | 965.4 | 713.4 | B | range end = last Hello World jump |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 54 | Solar Ray ×2 ★★★ | 4:23 | `p5.solar_ray_4` | 976.8 | 713.8 | A | hit 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 4:26 | `p5.solar_ray_4` | 979.9 | 713.9 | A | hit 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 55 | Blind Faith (72k + knockback, phase end) ★ | 4:45 | `p5.blind_faith` | 999.7 | 714.7 | X | anchor = the hit (7F72/7F73); the sheet time matches the 7B87 cast end 998.5 (gives 713.5), see 4.2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 57 | Cosmo Memory (405k raidwide) ★★★★ | 1:08 | `p6.cosmo_memory` | 1167.8 | 1099.8 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 58 | Cosmo Arrow (exaflares — dodge) | 1:22 | `p6.cosmo_arrow_1` | 1182.2 | 1100.2 | D | dodge row start = Cosmo Arrow cast end |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 1:38 | `p6.cosmo_arrow_1` | 1196.9 | 1098.9 | D | dodge row end = Cosmo Dive cast end |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 59 | Cosmo Dive (274k) ★★★ | 1:39 | `p6.cosmo_dive_1` | 1199.3 | 1100.3 | B | Cosmo Dive Far/Near hits; the 7BA6 cast end 1196.9 would give 1097.9 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 60 | Unlimited Wave Cannon: proteans (90k ×2) + Wild Charge (100–220k) ★★★ | 2:17 | `p6.wave_cannon_1` | 1236.0 | 1099.0 | B | proteans 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 2:19 | `p6.wave_cannon_1` | 1238.0 | 1099.0 | B | proteans 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 2:25 | `p6.wave_cannon_1` | 1244.3 | 1099.3 | B | Wild Charge |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 61 | Cosmo Arrow 2: proteans (90k ×2) + Wild Charge ★★★ | 2:50 | `p6.wave_cannon_2` | 1269.9 | 1099.9 | B | proteans 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 2:52 | `p6.wave_cannon_2` | 1271.9 | 1099.9 | B | proteans 2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 2:59 | `p6.wave_cannon_2` | 1278.2 | 1099.2 | B | Wild Charge |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 62 | Unlimited Wave Cannon 2 (exaflares + puddles — dodge) | 3:20 | `p6.unlimited_wave_cannon_2` | 1299.7 | 1099.7 | D | dodge row start = puddle 1 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 3:30 | `p6.unlimited_wave_cannon_2` | 1309.2 | 1099.2 | D | dodge row end = puddle 6 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 63 | Cosmo Dive 2 (274k) ★★★ | 3:33 | `p6.cosmo_dive_2` | 1312.4 | 1099.4 | B | hits; the 7BA6 cast end 1310.0 would give 1097.0 |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 64 | Cosmo Meteor (105k raidwide) + meteor baits ★★★ | 3:56 | `p6.cosmo_meteor` | 1335.6 | 1099.6 | A | big meteors 7BB2 |
| Carry the Clear | White Mage, Scholar, WHM + SCH |  | ″ | 4:05 | `p6.cosmo_meteor` | 1343.7 | 1098.7 | B | range end = last spread |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 65 | Flares (78k) ★★ | 4:16 | `p6.flares` | 1354.7 | 1098.7 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 66 | Magic Number (252k) ★★★★ | 4:28 | `p6.magic_number_1` | 1366.7 | 1098.7 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 67 | Magic Number (252k) ★★★★ | 4:44 | `p6.magic_number_2` | 1382.8 | 1098.8 | A |  |
| Carry the Clear | White Mage, Scholar, WHM + SCH | 68 | Run: \*\*\*\*mi\* (enrage — top groups kill at 5:08–5:22) | 5:09 | `p6.run_mi_enrage` | 1421.9 | 1112.9 | E | cactbot places the effect at +30 s; the 16 s cast bar ends at 1407.9 (gives 1098.9) |

### 4.2 Calibration per phase

**Offsets per phase** (A, B and C points of §4.1). The YAML value is the median, rounded half-up to 0.1 s:

| Phase | Sheet zero (CtC header) | n (A+B+C) | Median offset | Min | Max | Spread | n (A) | Median (A) | Spread (A) | YAML value |
|---|---|---|---|---|---|---|---|---|---|---|
| p1 | pull | 10 | 0.90 | 0.3 | 1.1 | 0.8 | 10 | 0.90 | 0.8 | 0.9 |
| p2 | phase start | 6 | 201.75 | 201.6 | 202.5 | 0.9 | 6 | 201.75 | 0.9 | 201.8 |
| p3 | phase start | 8 | 399.40 | 399.3 | 400.1 | 0.8 | 7 | 399.40 | 0.8 | 399.4 |
| p4 | phase start | 6 | 606.70 | 606.4 | 607.0 | 0.6 | 6 | 606.70 | 0.6 | 606.7 |
| p5 | boss targetable | 18 | 714.65 | 713.4 | 715.8 | 2.4 | 14 | 714.65 | 2.1 | 714.7 |
| p6 | Blind Faith | 14 | 1099.25 | 1098.7 | 1100.3 | 1.6 | 5 | 1098.80 | 1.1 | 1099.3 |

**Quantisation and drift.** Carry the Clear gives whole seconds, so a row can sit up to 1 s from the true time
without any disagreement. Two tests separate quantisation from real disagreement:

- *Single zero.* Find a zero point `z` for which every A/B/C row of the phase satisfies one rounding convention:
  - **floor** (the sheet truncates FFLogs times): `sheet = floor(t − z)`, i.e. `z ∈ (t − s − 1, t − s]`;
  - **round**: `z ∈ (t − s − 0.5, t − s + 0.5]`.
  A non-empty interval means the whole phase is consistent with one zero point.
- *Linear fit* of `offset = c + k · sheet time` (least squares). A non-zero `k` that the single-zero test cannot
  absorb is drift.

| Phase | Points | Intercept c | Slope k (s per 100 s) | Max abs. residual | Single zero, floor: feasible interval | Single zero, round: feasible interval |
|---|---|---|---|---|---|---|
| p1 | 10 | 1.29 | -0.74 | 0.50 | (0.1, 0.3] | (0.6, 0.8] |
| p2 | 6 | 202.35 | -0.63 | 0.37 | (201.5, 201.6] | (202.0, 202.1] |
| p3 | 8 | 399.19 | +0.34 | 0.40 | (399.1, 399.3] | (399.6, 399.8] |
| p4 | 6 | 606.45 | +0.91 | 0.37 | (606.0, 606.4] | (606.5, 606.9] |
| p5 | 18 | 715.66 | -0.75 | 0.54 | empty: row 43 needs > 714.8, row 53 needs <= 713.4 | empty: row 43 needs > 715.3, row 53 needs <= 713.9 |
| p6 | 14 | 1100.28 | -0.52 | 0.56 | empty: row 59 needs > 1099.3, row 64 needs <= 1098.7 | empty: row 59 needs > 1099.8, row 64 needs <= 1099.2 |

- **P1–P4 are exactly consistent** with one zero point under either convention, so their 0.6–0.9 s spreads are
  quantisation.
  - P1 shows it most clearly. Its rows form two clusters, towers +1.1 and Pantokrator +0.3, yet any zero in
    (0.1, 0.3] (floor) or (0.6, 0.8] (round) reproduces all ten times.
  - The P1–P4 slopes are fit noise: residuals ≤ 0.5 s, phases ≤ 3 min.
- **P5 and P6 drift.** No single zero fits them: the sheet's late rows sit 1–2 s later than cactbot's. The fitted
  intercepts land on the sheet's own zero definitions:
  - P5 715.66: "from boss targetable", timeline `--targetable--` at 715.6.
  - P6 1100.28: "from Blind Faith", the P6 sync at 1100.0.
- An independent check of the P5 drift: the sheet says P5 is "always 4:46 long", 286 s, but cactbot's P5 runs
  284.1 s from boss targetable (715.6) to the Blind Faith hit (999.7). The source of the drift is **UNVERIFIED**.
  cactbot builds timelines from logs and syncs often, so the sheet's reference times are the likelier side.
- The medians in `top.yaml` minimise the error across each phase. With per-row offsets (§4.3) the drift does not
  matter.

**Alternative readings for the questionable rows.** Each reading's offset is compared with the phase fit and the
single-zero interval; "-" means the phase has no single zero (P5/P6), so only the residual decides:

| Row | Phase | Sheet time | Reading | Timeline time | Offset | Residual to the phase fit | Fits the single-zero interval (floor / round) |
|---|---|---|---|---|---|---|---|
| 27 | p2 | 1:57 | catalog anchor | 318.8 | 201.8 | +0.18 | yes / yes |
| 27 | p2 | 1:57 | 7B24 raidwide (~+1.4 s) | 320.2 | 203.2 | +1.58 | no / no |
| 32 | p3 | 1:05 | catalog anchor | 464.4 | 399.4 | -0.02 | yes / yes |
| 32 | p3 | 1:05 | towers/stacks (+1.0 s) | 465.4 | 400.4 | +0.98 | no / no |
| 33 | p3 | 1:26 | catalog anchor | 485.4 | 399.4 | -0.09 | yes / yes |
| 33 | p3 | 1:26 | towers/stacks (+1.0 s) | 486.4 | 400.4 | +0.91 | no / no |
| 34 | p3 | 1:47 | catalog anchor | 506.5 | 399.5 | -0.06 | yes / yes |
| 34 | p3 | 1:47 | towers/stacks (+1.0 s) | 507.5 | 400.5 | +0.94 | no / no |
| 35 | p3 | 2:08 | catalog anchor | 527.8 | 399.8 | +0.17 | yes / yes |
| 35 | p3 | 2:08 | towers/stacks (+1.0 s) | 528.8 | 400.8 | +1.17 | no / no |
| 41 | p4 | 0:56 | catalog anchor (X) | 661.6 | 605.6 | -1.36 | no / no |
| 41 | p4 | 0:56 | 7B7C hit (+1.0 s) | 662.6 | 606.6 | -0.36 | yes / yes |
| 53 | p5 | 3:35 | catalog anchor (X) | 927.8 | 712.8 | -1.25 | - / - |
| 53 | p5 | 3:35 | cones 7B79 (+1.1 s) | 928.9 | 713.9 | -0.15 | - / - |
| 55 | p5 | 4:45 | catalog anchor (X) | 999.7 | 714.7 | +1.18 | - / - |
| 55 | p5 | 4:45 | 7B87 cast end (-1.2 s) | 998.5 | 713.5 | -0.02 | - / - |
| 59 | p6 | 1:39 | catalog anchor | 1199.3 | 1100.3 | +0.54 | - / - |
| 59 | p6 | 1:39 | 7BA6 cast end (-2.4 s) | 1196.9 | 1097.9 | -1.86 | - / - |
| 63 | p6 | 3:33 | catalog anchor | 1312.4 | 1099.4 | +0.23 | - / - |
| 63 | p6 | 3:33 | 7BA6 cast end (-2.4 s) | 1310.0 | 1097.0 | -2.17 | - / - |

Conclusions. A reading matches when its residual is no larger than the largest residual of the phase's A/B/C rows
(0.37–0.56 s, §4.2 fit table); the rejected readings are 0.9–2.2 s off.

| Row | Mechanic | The sheet times … | Evidence |
|---|---|---|---|
| 59, 63 Cosmo Dive | `p6.cosmo_dive_1/2` | the **damage** (7BA7 Near tankbusters / 7BA8 Far stack, 2.4 s after the 7BA6 cast end). Anchored on the damage entries `Cosmo Dive Far` (unsynced) | residuals +0.54/+0.23 against −1.86/−2.17 for the cast end; timeline lines 294–296 and 335–337; BossMod `P6CosmoDive` resolves 2.5 s after the cast end |
| 41 Blue Screen | `p4.blue_screen` | the **hit** 7B7C (1.0 s cast after the 8 s 7B7B), which is also the P5 sync. The anchor stays on the 7B7B cast end (661.6) because the hit makes the timeline jump (§5.2) | only the hit fits the P4 single-zero interval; BossMod `P4BlueScreen` counts 7B7C 1 s after the cast |
| 53 Omega (start) | `p5.omega_all` | the **cones** 7B79 (1 s cast), 1.1 s after the 8 s 7B9B/7B9C cast end. The cones are dodges; the first party damage of the row is 945.4 | residual −0.15 against −1.25 for the cast end; BossMod `P5OmegaDiffuseWaveCannon` activates the cones 1.1 s after the cast end |
| 27 Cosmo Memory (P2) | `p2.cosmo_memory` | the **7B22 cast end**, not the 7B24 raidwide about 1.4 s later. The anchor is the cast end, so sheet-relative plans place mitigation correctly; the real hit is ~1.4 s after `hit` | only the cast end fits the P2 single-zero interval; BossMod: raidwide 10.2 s after Pile Pitch (= 320.2), 7B24 has a 1.5 s cast |
| 32–35 Hello World rounds | `p3.latent_1..4` | the **Latent Defect cast end**, not the towers/stacks 1.0 s later | only the cast ends fit the P3 interval; cactbot comment "These casts go off 1 second after Latent Defect" (`the_omega_protocol.ts`) |
| 55 Blind Faith | `p5.blind_faith` | the **7B87 cast end** (998.5), 1.2 s before the hit. The anchor stays on the hit (999.7) | residual −0.02 against +1.18 for the hit (neighbours: Solar Ray 4 at 713.8/713.9) |
| 68 Run: \*\*\*\*mi\* | `p6.run_mi_enrage` | the end of the **16 s cast bar** (1407.9 gives 1098.9). cactbot places the enrage effect 30 s after cast start ("The cast bar and logs say 16 seconds but it appears to be ~30s in practice") | timeline lines 367–369; `Action.csv` 7BA0 `Cast100ms` 160 |
| 20, 28, 38 enrages | `p1.atomic_ray`, `p2.laser_shower`, `p3.ion_efflux` | times that no kill log reaches (labels "(… enrage — push before)") | excluded (E) |
| 30 P3 transition | `p3.transition` | 427.3 (High-powered Sniper Cannon) or up to 428.3: the P3 interval allows (427.1, 428.3] | both entries are unsynced: "sniper cannon times are a little sloppy and have inconsistent actor names" |
| 45 Delta (start) | `p5.delta_all` | the first `Patch`, which is unsynced and player-timed | timeline lines 187–201; graded C |

**Cross-checks against the sheet's own phase notes:**
- **P1 "timer from pull":** the zero point is 0.1–0.8 s after cactbot's InCombat sync, depending on the convention.
- **P2 "from phase start":** zero 201.5–202.1, 0.2–0.8 s before Omega-M/F become targetable at 202.3.
- **P3 "boss targetable ≈ 0:33":** zero (399.1, 399.8] + 33 = 432.1–432.8. The timeline's `--targetable--` is
  432.3.
- **P4 "always 1:12 long":**
  - The P4 zero is (606.0, 606.9]; the Blue Screen hit at sheet 0:56 is 662.6 in pre-jump timeline time.
  - The P5 boss becomes targetable 15.6 s after that hit (700.0 → 715.6), i.e. at P4 time 1:11.3–1:12.2.
  - So the sheet's P4 runs until P5's boss is targetable, which is where its P5 zero starts.
- **P5 "from boss targetable":** intercept 715.66 against `--targetable--` 715.6.
- **P6 "from Blind Faith; boss targetable ≈ 0:55":** intercept 1100.28 against the Blind Faith sync 1100.0, and
  1100.0 + 54.7 = the timeline's `--targetable--` at 1154.7.

### 4.3 What the importer should do with times

1. Resolve the row's mechanic (§4.5).
2. For an action with explicit time `t_a` on a row whose own time `t_r` matches hit `k`, store
   `offset = t_a − t_r` relative to `HitAt(k)`.
   - `k` is the index of the time in a multi-time cell; a range's start is hit 0 and its end the last hit.
   - This is exact to the sheet's whole-second rounding and immune to the P5/P6 drift.
   - It also absorbs the cast-vs-damage differences of §4.2, because the sheet's action times are relative to the
     sheet's own row time.
3. Only for actions on rows without their own time, use `offset = (zero + t_a) − hit`. Fit `zero` as the median of
   `hit − t_r` over the sheet's A/B/C rows of the phase, falling back to the `calibration` preset.
4. In the review screen, show `max |(zero + t_r) − hit|` per phase. For Carry the Clear with the YAML medians it
   is ≤ 0.7 s in P1–P4, 1.3 s in P5 and 1.0 s in P6.

### 4.4 LPDU: every labelled row

LPDU has no times, so `Timeline time` is the anchor's `hit`. "Candidates" counts the mechanics of the row's phase
whose aliases contain the normalised label; it is more than one only for the repeated labels of §4.5.

| Sheet | Tab | Row | Row label (verbatim) | Sheet time | Mechanic id | Timeline time (hit) | Offset | Candidates in phase |
|---|---|---|---|---|---|---|---|---|
| LPDU | P1/P2 | 2 | `Looper Set 3` | - | `p1.loop_set3` | 47.1 | - | 1 |
| LPDU | P1/P2 | 3 | `Looper Set 4` | - | `p1.loop_set4` | 56.1 | - | 1 |
| LPDU | P1/P2 | 4 | `Pantokrator ` | - | `p1.pantokrator` | 81.3 | - | 1 |
| LPDU | P1/P2 | 5 | `1st Stack` | - | `p1.panto_stack1` | 81.3 | - | 1 |
| LPDU | P1/P2 | 6 | `2nd Stack` | - | `p1.panto_stack2` | 87.3 | - | 1 |
| LPDU | P1/P2 | 7 | `3rd Stack` | - | `p1.panto_stack3` | 93.3 | - | 1 |
| LPDU | P1/P2 | 8 | `4th Stack` | - | `p1.panto_stack4` | 99.3 | - | 1 |
| LPDU | P1/P2 | 10 | `Solar Ray` | - | `p2.solar_ray` | 214.5 | - | 1 |
| LPDU | P1/P2 | 11 | `Limitless ` | - | `p2.limitless` | 309.7 | - | 1 |
| LPDU | P3 | 2 | `Hello World` | - | `p3.hello_world` | 441.3 | - | 1 |
| LPDU | P3 | 3 | `Latent 1` | - | `p3.latent_1` | 464.4 | - | 1 |
| LPDU | P3 | 4 | `Latent 2` | - | `p3.latent_2` | 485.4 | - | 1 |
| LPDU | P3 | 5 | `Latent 3` | - | `p3.latent_3` | 506.5 | - | 1 |
| LPDU | P3 | 6 | `Latent 4` | - | `p3.latent_4` | 527.8 | - | 1 |
| LPDU | P3 | 7 | `Critical Error` | - | `p3.critical_error` | 550.1 | - | 1 |
| LPDU | P4 | 2 | `Protean 1` | - | `p4.protean_1` | 621.4 | - | 1 |
| LPDU | P4 | 3 | `Stack 1` | - | `p4.stack_1` | 627.0 | - | 1 |
| LPDU | P4 | 4 | `Protean 2` | - | `p4.protean_2` | 631.5 | - | 1 |
| LPDU | P4 | 5 | `Stack 2` | - | `p4.stack_2` | 636.8 | - | 1 |
| LPDU | P4 | 6 | `Protean 3` | - | `p4.protean_3` | 641.6 | - | 1 |
| LPDU | P4 | 7 | `Stack 3` | - | `p4.stack_3` | 646.9 | - | 1 |
| LPDU | P4 | 8 | `Blue Screen` | - | `p4.blue_screen` | 661.6 | - | 1 |
| LPDU | P5 | 2 | `Solar Ray 1` | - | `p5.solar_ray_1` | 723.6 | - | 1 |
| LPDU | P5 | 3 | `Delta` | - | `p5.delta` | 740.0 | - | 1 |
| LPDU | P5 | 4 | `Patch` | - | `p5.delta_patch` | 761.2 | - | 1 |
| LPDU | P5 | 5 | `Hello World` | - | `p5.delta_hello_world` | 787.0 | - | 4 |
| LPDU | P5 | 6 | `Solar Ray 2` | - | `p5.solar_ray_2` | 805.2 | - | 1 |
| LPDU | P5 | 7 | `Sigma` | - | `p5.sigma` | 821.7 | - | 1 |
| LPDU | P5 | 8 | `Solar Ray 3` | - | `p5.solar_ray_3` | 894.3 | - | 1 |
| LPDU | P5 | 9 | `Omega` | - | `p5.omega` | 910.7 | - | 1 |
| LPDU | P5 | 10 | `Hello World` | - | `p5.omega_hello_world_1` | 945.4 | - | 4 |
| LPDU | P5 | 11 | `Solar Ray 4` | - | `p5.solar_ray_4` | 976.8 | - | 1 |
| LPDU | P5 | 12 | `Blind Faith` | - | `p5.blind_faith` | 999.7 | - | 1 |
| LPDU | P6 | 2 | `Cosmo Memory` | - | `p6.cosmo_memory` | 1167.8 | - | 1 |
| LPDU | P6 | 3 | `CD1` | - | `p6.cosmo_dive_1` | 1199.3 | - | 1 |
| LPDU | P6 | 4 | `WC1` | - | `p6.wave_cannon_1` | 1236.0 | - | 1 |
| LPDU | P6 | 5 | `WC2` | - | `p6.wave_cannon_2` | 1269.9 | - | 1 |
| LPDU | P6 | 6 | `CD2` | - | `p6.cosmo_dive_2` | 1312.4 | - | 1 |
| LPDU | P6 | 7 | `Meteors` | - | `p6.cosmo_meteor` | 1335.6 | - | 1 |
| LPDU | P6 | 8 | `Flares` | - | `p6.flares` | 1354.7 | - | 1 |
| LPDU | P6 | 9 | `Magic Number` | - | `p6.magic_number_1` | 1366.7 | - | 2 |
| LPDU | P6 | 10 | `Magic Number` | - | `p6.magic_number_2` | 1382.8 | - | 2 |
| LPDU | General Tank Mit til P5 | 2 | `Pantokrator` | - | `p1.pantokrator` | 81.3 | - | 1 |
| LPDU | General Tank Mit til P5 | 5 | `Solar Ray` | - | `p2.solar_ray` | 214.5 | - | 1 |
| LPDU | General Tank Mit til P5 | 6 | `Limitless` | - | `p2.limitless` | 309.7 | - | 1 |
| LPDU | General Tank Mit til P5 | 8 | `Latent 1` | - | `p3.latent_1` | 464.4 | - | 1 |
| LPDU | General Tank Mit til P5 | 9 | `Latent 2` | - | `p3.latent_2` | 485.4 | - | 1 |
| LPDU | General Tank Mit til P5 | 10 | `Latent 3` | - | `p3.latent_3` | 506.5 | - | 1 |
| LPDU | General Tank Mit til P5 | 11 | `Latent 4` | - | `p3.latent_4` | 527.8 | - | 1 |
| LPDU | General Tank Mit til P5 | 13 | `Proteans` | - | `p4.wave_cannon` | 621.4 | - | 1 |
| LPDU | WAR/DRK | 2 | `Solar Ray 1` | - | `p5.solar_ray_1` | 723.6 | - | 1 |
| LPDU | WAR/DRK | 4 | `Solar Ray 2` | - | `p5.solar_ray_2` | 805.2 | - | 1 |
| LPDU | WAR/DRK | 6 | `Solar Ray 3` | - | `p5.solar_ray_3` | 894.3 | - | 1 |
| LPDU | WAR/DRK | 8 | `Solar Ray 4` | - | `p5.solar_ray_4` | 976.8 | - | 1 |
| LPDU | WAR/DRK | 10 | `CD1` | - | `p6.cosmo_dive_1` | 1199.3 | - | 1 |
| LPDU | WAR/DRK | 11 | `WC1` | - | `p6.wave_cannon_1` | 1236.0 | - | 1 |
| LPDU | WAR/DRK | 12 | `WC2` | - | `p6.wave_cannon_2` | 1269.9 | - | 1 |
| LPDU | WAR/DRK | 13 | `CD2` | - | `p6.cosmo_dive_2` | 1312.4 | - | 1 |
| LPDU | WAR/GNB | 2 | `Solar Ray 1` | - | `p5.solar_ray_1` | 723.6 | - | 1 |
| LPDU | WAR/GNB | 4 | `Solar Ray 2` | - | `p5.solar_ray_2` | 805.2 | - | 1 |
| LPDU | WAR/GNB | 6 | `Solar Ray 3` | - | `p5.solar_ray_3` | 894.3 | - | 1 |
| LPDU | WAR/GNB | 8 | `Solar Ray 4` | - | `p5.solar_ray_4` | 976.8 | - | 1 |
| LPDU | WAR/GNB | 10 | `CD1` | - | `p6.cosmo_dive_1` | 1199.3 | - | 1 |
| LPDU | WAR/GNB | 11 | `WC1` | - | `p6.wave_cannon_1` | 1236.0 | - | 1 |
| LPDU | WAR/GNB | 12 | `WC2` | - | `p6.wave_cannon_2` | 1269.9 | - | 1 |
| LPDU | WAR/GNB | 13 | `CD2` | - | `p6.cosmo_dive_2` | 1312.4 | - | 1 |
| LPDU | WAR/PLD | 2 | `Solar Ray 1` | - | `p5.solar_ray_1` | 723.6 | - | 1 |
| LPDU | WAR/PLD | 4 | `Solar Ray 2` | - | `p5.solar_ray_2` | 805.2 | - | 1 |
| LPDU | WAR/PLD | 6 | `Solar Ray 3` | - | `p5.solar_ray_3` | 894.3 | - | 1 |
| LPDU | WAR/PLD | 8 | `Solar Ray 4` | - | `p5.solar_ray_4` | 976.8 | - | 1 |
| LPDU | WAR/PLD | 10 | `CD1` | - | `p6.cosmo_dive_1` | 1199.3 | - | 1 |
| LPDU | WAR/PLD | 11 | `WC1` | - | `p6.wave_cannon_1` | 1236.0 | - | 1 |
| LPDU | WAR/PLD | 12 | `WC2` | - | `p6.wave_cannon_2` | 1269.9 | - | 1 |
| LPDU | WAR/PLD | 13 | `CD2` | - | `p6.cosmo_dive_2` | 1312.4 | - | 1 |
| LPDU | DRK/GNB | 2 | `Solar Ray 1` | - | `p5.solar_ray_1` | 723.6 | - | 1 |
| LPDU | DRK/GNB | 4 | `Solar Ray 2` | - | `p5.solar_ray_2` | 805.2 | - | 1 |
| LPDU | DRK/GNB | 6 | `Solar Ray 3` | - | `p5.solar_ray_3` | 894.3 | - | 1 |
| LPDU | DRK/GNB | 8 | `Solar Ray 4` | - | `p5.solar_ray_4` | 976.8 | - | 1 |
| LPDU | DRK/GNB | 10 | `CD1` | - | `p6.cosmo_dive_1` | 1199.3 | - | 1 |
| LPDU | DRK/GNB | 11 | `WC1` | - | `p6.wave_cannon_1` | 1236.0 | - | 1 |
| LPDU | DRK/GNB | 12 | `WC2` | - | `p6.wave_cannon_2` | 1269.9 | - | 1 |
| LPDU | DRK/GNB | 13 | `CD2` | - | `p6.cosmo_dive_2` | 1312.4 | - | 1 |
| LPDU | DRK/PLD | 2 | `Solar Ray 1` | - | `p5.solar_ray_1` | 723.6 | - | 1 |
| LPDU | DRK/PLD | 4 | `Solar Ray 2` | - | `p5.solar_ray_2` | 805.2 | - | 1 |
| LPDU | DRK/PLD | 6 | `Solar Ray 3` | - | `p5.solar_ray_3` | 894.3 | - | 1 |
| LPDU | DRK/PLD | 8 | `Solar Ray 4` | - | `p5.solar_ray_4` | 976.8 | - | 1 |
| LPDU | DRK/PLD | 10 | `CD1` | - | `p6.cosmo_dive_1` | 1199.3 | - | 1 |
| LPDU | DRK/PLD | 11 | `WC1` | - | `p6.wave_cannon_1` | 1236.0 | - | 1 |
| LPDU | DRK/PLD | 12 | `WC2` | - | `p6.wave_cannon_2` | 1269.9 | - | 1 |
| LPDU | DRK/PLD | 13 | `CD2` | - | `p6.cosmo_dive_2` | 1312.4 | - | 1 |
| LPDU | GNB/PLD | 2 | `Solar Ray 1` | - | `p5.solar_ray_1` | 723.6 | - | 1 |
| LPDU | GNB/PLD | 4 | `Solar Ray 2` | - | `p5.solar_ray_2` | 805.2 | - | 1 |
| LPDU | GNB/PLD | 6 | `Solar Ray 3` | - | `p5.solar_ray_3` | 894.3 | - | 1 |
| LPDU | GNB/PLD | 8 | `Solar Ray 4` | - | `p5.solar_ray_4` | 976.8 | - | 1 |
| LPDU | GNB/PLD | 10 | `CD1` | - | `p6.cosmo_dive_1` | 1199.3 | - | 1 |
| LPDU | GNB/PLD | 11 | `WC1` | - | `p6.wave_cannon_1` | 1236.0 | - | 1 |
| LPDU | GNB/PLD | 12 | `WC2` | - | `p6.wave_cannon_2` | 1269.9 | - | 1 |
| LPDU | GNB/PLD | 13 | `CD2` | - | `p6.cosmo_dive_2` | 1312.4 | - | 1 |

**Rows that are not mechanics (LPDU).** The `ALOO` tab is a title and credits page with no table. Its non-empty
rows are 11 ("LPDU General TOP Mitigation Guideline"), 16 and 17 (Discord pointers) and 20–22 (credits).

| Tab | Row | Column A | Why not a mechanic |
|---|---|---|---|
| P1/P2 | 1 | `P1 ` | phase header |
| P1/P2 | 9 | `P2` | phase header |
| P1/P2 | 12 | (empty) | no label; other cells: 15 sec Mit is used as the clone jumps / Addle and Feint may be used on Female to cover Autos after Party Synergy |
| P3 | 1 | `P3` | phase header |
| P3 | 8 | (empty) | no label; other cells: WAR can use Shake as P3 dies for 1st P4 Proteans. |
| P3 | 9 | (empty) | empty row |
| P3 | 10 | (empty) | empty row |
| P4 | 1 | `P4` | phase header |
| P4 | 9 | (empty) | no label; other cells: Shields bevor the boss is targetable |
| P5 | 1 | `P5` | phase header |
| P5 | 13 | (empty) | no label; other cells: Every damage instance has a GCD shield (Sigma Raidwide can usually be skipped) |
| P6 | 1 | `P6` | phase header |
| P6 | 11 | (empty) | no label; other cells: \*WAR/PLD use Mit after Protean befor Stack / Discuss who goes mid to heal during Meteors and LB3 order during the cutscene!! / >>Holy Melee LB3 Bible<< |
| P6 | 12 | (empty) | no label; other cells: Tanks Follow LB3 Prio!!! / AST can shift Neutralsect to CD1 and Meteors, mostly done with SCH / Discuss Feints and LB3s during the cutscene!! |
| P6 | 13 | (empty) | no label; other cells: Adjust Rep order accordingly / For Tanks and DPS: / Use personals and self healing all during Meteors! |
| P6 | 14 | (empty) | no label; other cells: WAR > DRK > PLD > GNB / \*Press 10s Mit when doing the side dodge to catch both Proteans and the Stack |
| P6 | 15 | (empty) | no label; other cells: Talk About LB3 order during the cutscene!! / or about 1/5th into the cast, never too early or you won't catch the Stack! |
| General Tank Mit til P5 | 1 | `P1` | phase header |
| General Tank Mit til P5 | 3 | (empty) | no label; other cells: Rampart as P1 dies / Rampart as P1 dies |
| General Tank Mit til P5 | 4 | `P2` | phase header |
| General Tank Mit til P5 | 7 | `P3` | phase header |
| General Tank Mit til P5 | 12 | `P4` | phase header |
| WAR/DRK | 1 | `P5` | phase header |
| WAR/DRK | 3 | (empty) | empty row |
| WAR/DRK | 5 | (empty) | empty row |
| WAR/DRK | 7 | (empty) | empty row |
| WAR/DRK | 9 | `P6` | phase header |
| WAR/GNB | 1 | `P5` | phase header |
| WAR/GNB | 3 | (empty) | empty row |
| WAR/GNB | 5 | (empty) | empty row |
| WAR/GNB | 7 | (empty) | empty row |
| WAR/GNB | 9 | `P6` | phase header |
| WAR/PLD | 1 | `P5` | phase header |
| WAR/PLD | 3 | (empty) | no label; other cells: and Holmgang Solar Ray 2, |
| WAR/PLD | 5 | (empty) | no label; other cells: Prepop Rampart and use fillers 90s 1st GCD |
| WAR/PLD | 7 | (empty) | no label; other cells: Healer Help required for Solar Ray 3 |
| WAR/PLD | 9 | `P6` | phase header |
| DRK/GNB | 1 | `P5` | phase header |
| DRK/GNB | 3 | (empty) | empty row |
| DRK/GNB | 5 | (empty) | empty row |
| DRK/GNB | 7 | (empty) | empty row |
| DRK/GNB | 9 | `P6` | phase header |
| DRK/PLD | 1 | `P5` | phase header |
| DRK/PLD | 3 | (empty) | no label; other cells: and Living Dead Solar Ray 2, |
| DRK/PLD | 5 | (empty) | no label; other cells: Prepop Rampart and use fillers 90s 1st GCD |
| DRK/PLD | 7 | (empty) | no label; other cells: Healer help required for Solar Ray 3 |
| DRK/PLD | 9 | `P6` | phase header |
| GNB/PLD | 1 | `P5` | phase header |
| GNB/PLD | 3 | (empty) | empty row |
| GNB/PLD | 5 | (empty) | no label; other cells: Prepop Rampart and use fillers 90s 1st GCD. |
| GNB/PLD | 7 | (empty) | no label; other cells: Healer Help required for Solar Ray 3 |
| GNB/PLD | 9 | `P6` | phase header |

**Carry the Clear rows that are not mechanics** (the same in all three tabs):
- Rows 1–5 are empty; row 6 is `Mitigation Key:` (Scholar and WHM + SCH tabs only).
- Rows 7–13 are the severity legend (`Damage` / `Mits Suggested to Live`, `Super Lethal ★★★★` 5+, `Very Lethal ★★★`
  3-4, `Lethal in bis ★★` 2, `Lethal week 1 ★` 1, `Non-lethal`, `Mechanic (no damage)`), with the tab title and
  method notes in column C.
- Row 14 is the kit and patch note; row 15 holds the column headers.
- The phase section rows are:
  - 16: `Damage Events` + `P1 — Omega  (timer from pull)`
  - 21: `P2 — Omega-M/F  (from phase start)`
  - 29: `P3 — Omega Reconfigured  (from phase start; boss targetable ≈ 0:33)`
  - 39: `P4 — Blue Screen  (from phase start; always 1:12 long)`
  - 42: `P5 — Run: Dynamis  (from boss targetable; always 4:46 long)`
  - 56: `P6 — Alpha Omega  (from Blind Faith; boss targetable ≈ 0:55)`

**Phase detection** used in the simulation:
- An LPDU label cell that is exactly `P1`..`P6` after trimming starts a section:
  - the `P1/P2` tab switches to P2 at row 9;
  - the six tank tabs switch from P5 to P6 at row 9;
  - `General Tank Mit til P5` has P1, P2, P3 and P4 sections.
- A Carry the Clear section row has column B starting with `P<n> —` (EM DASH) and no time in column A.

### 4.5 Repeated labels and the resolution rule

A label that means different mechanics in different phases is an alias of each of them, and the phase decides.
Cross-phase aliases in `top.yaml`:

- `cm`: p2, p6
- `cosmo memory`: p2, p6
- `diffuse wave cannon`: p1, p5
- `flares`: p2, p6
- `hello world`: p3, p5
- `monitors`: p3, p5
- `oversampled wave cannon`: p3, p5
- `solar ray`: p2, p5
- `wave cannon`: p4, p6

Normalised aliases shared by several mechanics **inside one phase**:

- p5 `hello near/distant world`: `p5.delta_hello_world`, `p5.sigma_hello_world`, `p5.omega_hello_world_1`, `p5.omega_hello_world_2`
- p5 `hello world`: `p5.delta_hello_world`, `p5.sigma_hello_world`, `p5.omega_hello_world_1`, `p5.omega_hello_world_2`
- p5 `monitors`: `p5.delta_monitors`, `p5.omega_hello_world_1`
- p5 `oversampled wave cannon`: `p5.delta_monitors`, `p5.omega_hello_world_1`
- p5 `solar ray`: `p5.solar_ray_1`, `p5.solar_ray_2`, `p5.solar_ray_3`, `p5.solar_ray_4`
- p5 `solar ray ×2`: `p5.solar_ray_2`, `p5.solar_ray_3`, `p5.solar_ray_4`
- p6 `cosmo dive`: `p6.cosmo_dive_1`, `p6.cosmo_dive_2`
- p6 `magic number`: `p6.magic_number_1`, `p6.magic_number_2`
- p6 `magic number (252k)`: `p6.magic_number_1`, `p6.magic_number_2`
- p6 `wave cannon`: `p6.wave_cannon_1`, `p6.wave_cannon_2`

Of these, only four are actual sheet labels, and row order disambiguates them:
- LPDU P5 `Hello World` (×2);
- LPDU P6 `Magic Number` (×2);
- Carry the Clear P5 `Solar Ray ×2 ★★★` (×3);
- Carry the Clear P6 `Magic Number (252k) ★★★★` (×2).

**Resolution rule.** The simulation reproduces the intended mapping for all 47 + 98 rows, and the time-based
variant of step 3 (with the YAML zeros) gives the same result:

1. Track the phase per tab (tab name or section rows, §4.4). At each section start set `prev = phase.start` and
   empty the `used` set.
2. `candidates` = the mechanics of the current phase whose normalised aliases contain the normalised label.
3. If the row has a time, a phase zero is known and there is more than one candidate, take the candidate with a
   hit closest to `zero + time`.
4. Otherwise take the earliest candidate with `hit ≥ prev` that is not in `used`. Use `≥`, not `>`: an umbrella and
   its first part share a hit, e.g. LPDU `Pantokrator` then `1st Stack`, both 81.3.
5. Set `prev = chosen.hit` and add the mechanic to `used`.

Examples:
- LPDU P5 `Hello World` after `Patch` (761.2) → `p5.delta_hello_world` (787.0); after `Omega` (910.7) →
  `p5.omega_hello_world_1` (945.4).
- LPDU P6 `Magic Number` → `p6.magic_number_1`, then (MN1 used) `p6.magic_number_2`.
- Carry the Clear `Solar Ray ×2 ★★★` rows 46, 51 and 54 → `p5.solar_ray_2`, `_3`, `_4`.

### 4.6 Judgment calls (review with the plan authors if possible)

1. **LPDU `Limitless` → `p2.limitless`** (flares 309.7 + Cosmo Memory 318.8), not the Bladedance tankbusters
   (295.6).
   - The P1/P2 row assigns party mitigation, healer "Everything" and Magick Barrier.
   - Its note row says "15 sec Mit is used as the clone jumps". Beyond Defense is at 306.7, and 15 s from there
     covers the flares and the 7B24 Cosmo Memory raidwide (~320.2).
   - `General Tank Mit til P5` row `Limitless` (Rampart + 30% + Short CD) resolves to the same mechanic. Whether
     LPDU meant Bladedance for the tanks is **UNVERIFIED**.
2. **`General Tank Mit til P5` row `Pantokrator`** says "Invuln Diffuse Wave Cannon".
   - The label resolves to `p1.pantokrator` (81.3), but the target is `p1.wave_cannon_kyrios` (108.0).
   - `Diffuse Wave Cannon` is an alias of that mechanic. The importer should re-anchor a cell whose text names
     another mechanic of the same phase, and show it in review.
3. **LPDU P5 second `Hello World` → `p5.omega_hello_world_1`** (945.4, with the monitors).
   - Omega has two Hello World sets: 945.4, and 963.4 with the blasters. The order rule takes the first after the
     Omega raidwide.
   - The row's actions (WHM "Lilybell + Wings", SCH "Expedient") are 20 s effects on 120–180 s recasts
     (`data/actions/actions.yaml`: Temperance, Liturgy of the Bell and Expedient's Desperate Measures last 20 s).
     Pressed just before 945.4 they last until about 965, so they also cover the blasters (961.6) and the second
     set's first jump (963.4), though not its last two (964.4, 965.4). Pressed for the second set (about 961) they
     would miss the first set entirely. This favours the first set but does not prove it.
   - The intent is **UNVERIFIED**.
4. **LPDU `Delta`, `Sigma`, `Omega` → the three Run: \*\*\*\*mi\* raidwides** (740.0, 821.7, 910.7). The rows hold
   Reprisal/Addle/Feint/Dismantle and shields, and LPDU's P5 note says "Sigma Raidwide can usually be skipped".
   `Patch` → `p5.delta_patch` (first tether break, 761.2, approximate).
5. **Carry the Clear rows that span several hits map to umbrellas**: `p1.program_loop`, `p1.pantokrator`,
   `p4.wave_cannon`, `p5.delta_all`, `p5.omega_all` (plus `p1.wave_cannon_kyrios`, a two-hit mechanic with no
   parts, so its timed actions always stay on it with a hit index). A timed action in an umbrella row can
   either:
   - stay on the umbrella, with the hit index of §4.3; or
   - move to the part whose first hit is the first at or after the press time: `p5.delta_patch` /
     `delta_monitors` / `delta_hello_world`, `p5.omega_cannons` / `omega_hello_world_1` / `omega_hello_world_2`,
     `p4.protean_n` / `stack_n`, `p1.loop_setn`, `p1.panto_stackn`.
6. **Actions tied to a phase ending cannot be timed from the catalog.**
   - The Scholar's "Protraction + Recit → Adlo → Deploy (≈3:04, just before P3 ends)" sits on the Ion Efflux
     enrage row. But P3 ends when the boss dies, before the enrage, and the timeline then jumps to 600.0.
   - The same applies to the LPDU notes "WAR can use Shake as P3 dies for 1st P4 Proteans" and "Rampart as P1
     dies".
   - The markers `p2.start` and `p4.start` exist, but a reminder *before* an unpredictable jump needs another
     trigger (boss HP). Flag such entries in review.
7. **Dodge rows** (`Cosmo Arrow (exaflares — dodge)`, `Unlimited Wave Cannon 2 (exaflares + puddles — dodge)`)
   are `kind: other` mechanics so their labels resolve. They carry no damage; their notes mention Sprint and
   Swiftcast only.

## 5. Anchoring caveats for the engine and the importer

### 5.1 Anchors that are not the damage itself

| Mechanic | Anchor entry (hit) | Relation to the real damage |
|---|---|---|
| `p2.cosmo_memory` | Cosmo Memory (318.8) | 7B22 cast end; the raidwide 7B24 (1.5 s cast) lands about 1.4 s later (BossMod: 10.2 s after Pile Pitch) |
| `p3.latent_1` | Latent Defect 1 (464.4) | Latent Defect cast end; towers, stacks and defamations land 1.0 s later, rots about 6 s after that |
| `p3.latent_2` | Latent Defect 2 (485.4) | Latent Defect cast end; the hits land 1.0 s later |
| `p3.latent_3` | Latent Defect 3 (506.5) | Latent Defect cast end; the hits land 1.0 s later |
| `p3.latent_4` | Latent Defect 4 (527.8) | Latent Defect cast end; the hits land 1.0 s later |
| `p3.monitors` | Oversampled Wave Cannon (571.4) | cast end; the 7B6D spreads land 0.1 s later (BossMod) |
| `p4.wave_cannon` | Wave Cannon A (621.4) | boss visual; the first lasers 7B7E land about 0.6 s later (BossMod) |
| `p4.protean_1` | Wave Cannon A (621.4) | boss visual; the 7B7E lasers land about 0.6 s later (BossMod) |
| `p4.protean_2` | Wave Cannon A (631.5) | boss visual; the 7B7E lasers land about 0.7 s later (BossMod) |
| `p4.protean_3` | Wave Cannon A (641.6) | boss visual; the 7B7E lasers land about 0.5-0.9 s later |
| `p4.blue_screen` | Blue Screen Enrage (661.6) | 7B7B cast end; the raidwide 7B7C lands 1.0 s later and makes the timeline jump to 700.0 |

Other anchors on a visual cast end (`EffectRange` 0), where the hit resolves with it or the row is a dodge:
- `p2.bladedance`: the 7F75 cleaves resolve with the 7B34 cast end (BossMod: 8.4 s after the Laser Shower cast
  start = 295.8).
- `p2.flares` and `p2.limitless`: the 7B32 flares resolve with the 7B31 cast end (BossMod: 0.3 s before Pile
  Pitch).
- `p5.omega_cannons` / `p5.omega_all`: dodges; the cones land 1.1 s later.
- `p6.cosmo_arrow_1`: dodge row; the exasquares follow every 2 s.

Every other non-marker anchor is the hit itself: its id has a non-zero `EffectRange` in `Action.csv` (§3). The
build check flags any anchor on an `EffectRange` 0 id that is not documented here.

Mitigation pressed for these anchors is early by the delay, which is harmless for effects of 10 s or more. Heals
planned "after the hit" should add the delay.

### 5.2 Hits that end a phase

`p4.blue_screen` and `p5.blind_faith` land exactly when the timeline jumps (7B7C → 700.0, 7F72 → 1100.0).

- Anything planned **after** them must be anchored to `p5.start` / `p6.start` with a positive offset, because a
  press time after the pre-jump `hit` is never reached.
- Example, from the Carry the Clear Scholar note on Blue Screen: "Top SCHs often Emergency Tactics + Succor right
  after it".
- The Blind Faith hit (7F72 on success) is also the P6 sync, so `p6.start` is exactly that hit.

### 5.3 Unsynced anchors and hits

`#Ability` lines are entries without a sync: cactbot's estimate of the time relative to the last sync.

Unsynced anchors in `top.yaml`:
- `p3.transition`;
- `p5.delta_all` and `p5.delta_patch` (Patch);
- `p6.cosmo_dive_1/2`;
- `p6.wave_cannon_1/2`;
- `p6.unlimited_wave_cannon_2`;
- the `--targetable--` markers.

Unsynced later hits:
- the Hello World jumps in P5;
- the Patch tether breaks in P3;
- the P6 meteor spreads.

They are still the right anchors, because the synced neighbours are casts, and the syncs around them keep the
clock right. Example: P6 Wild Charge (1244.3) is 0.4 s after the synced 7BA9 cast end (1243.9), the same delay
BossMod uses.

### 5.4 TOP phase starts and markers

| Phase | `start` | Sync at the start | Markers |
|---|---|---|---|
| p1 | 0.0 | InCombat | `p1.pull` 0.0 |
| p2 | 200.0 | MapEffect `00020001`/`0A`, window 200,0 — not a v1 sync type. The v1 engine enters P2 with `StartsUsing 7B40` (Firewall) at 203.4, window 210,5 ("extra sync due to map effect sync") | `p2.start` 200.0, `p2.targetable` 202.3 |
| p3 | 400.0 | Ability 7B13 (BossMod `DieF`, the Omega-M/F death visual), window 400,0 | `p3.start` 400.0, `p3.targetable` 432.3 |
| p4 | 600.0 | Ability 7B47 (BossMod `P3End`), window 600,0 | `p4.start` 600.0, `p4.targetable` 607.1. LPDU P4 note: "Shields bevor the boss is targetable" |
| p5 | 700.0 | Ability 7B7C = the Blue Screen hit, window 700,0 | `p5.start` 700.0, `p5.targetable` 715.6 (Carry the Clear P5 zero) |
| p6 | 1100.0 | Ability 7F72 = the Blind Faith hit, window 1100,0 | `p6.start` 1100.0 (Carry the Clear P6 zero), `p6.targetable` 1154.7 |

With the v1 engine (no `MapEffect`), P2 is entered by the forward sync to 203.4, so `p2.start` (200.0) and
`p2.targetable` (202.3) are jumped over. cactbot skips the entries between the old and the new time
(`docs/research/cactbot-timelines.md` §3.4), and MitCue's `ReminderScheduler.OnTimelineJump` marks reminders whose
press time lies in that span as Skipped. Entries anchored to these two markers therefore need a press time after
203.4. Carry the Clear's earliest P2 action is at 0:07 (about 208.8), so its plans are unaffected. The other TOP
phase syncs (P3–P6) are `Ability` lines that land exactly on the phase start.

### 5.5 Sync types the other fights need

Details and fallbacks are in `docs/research/cactbot-timelines.md` §4.3, §5.2, §5.3 and §6.

- **DMU** enters P2 and P4 only through `ActorControl` lines:
  - P2: `ActorControl { command: "80000027", data0: "05"/"0D" }`, window 300,5, only when Kefka is below 15% HP.
  - P4: `data0: "2E"`, window 1088,14000.
  - With the v1 types (InCombat, StartsUsing, Ability, wipe), the next syncs are `Ultimate Embrace` (C24C, 320.1)
    and `Kefka Says` (C2DC, 1095.0/1100.0), both with the default window, so a push would not be followed.
  - DMU also branches with labels and `jump`/`forcejump`:
    - `p2-success` 597.0, reached via StartsUsing C3F7 at 483.2;
    - `p4-success` 1355.6, reached via Ability C3FD at 1215.3, window 90,5;
    - enrage labels at 10000+.
- **TEA**:
  - P2 starts with `RemovedCombatant { name: "Liquid Hand" }` at 196.0. Its window is 50,0, it is not a v1 type,
    and it is inert per cactbot-timelines.md §4.3.
  - It is followed by `Ability 4830` (Hawk Blaster) at 200.0, window 200,0, which is enough.
  - P4 is entered by Perfect Alexander's first auto-attack (4A8B = 19083 in `Action.csv`) at 900.0, window 900,0.
- **TOP**: the `MapEffect` at the P2 start has the fallback above.

## 6. TEA (first draft)

Territory 887, `ClassJobLevelSync` 80 (§8). Phases follow cactbot's sections and BossMod's four phases (`P1:
Living Liquid`, `P2: BJ+CC`, `P3: Alex Prime`, `P4: Perfect Alex`):
- BossMod puts the Limit Cut transition in P2 and Temporal Stasis in P3, as here.
- BossMod starts its P4 when Alexander Prime dies, so the Divine Judgment tank-LB3 raidwide falls in BossMod's P4.
  With cactbot's sync points it falls in `p3` here, after the marker `p3.alexander_down`.

| id | name | start | sync point |
|---|---|---|---|
| p1 | P1: Living Liquid | 0.0 | InCombat (pull) |
| p2 | P2: Cruise Chaser + Brute Justice (Limit Cut transition first) | 200.0 | Ability 4830 Hawk Blaster, window 200,0 (preceded by RemovedCombatant "Liquid Hand" @196.0, not a v1 sync type) |
| p3 | P3: Alexander Prime (Temporal Stasis, Inception, Wormhole, DPS check) | 492.0 | StartsUsing 485A Temporal Stasis, window 500,0 |
| p4 | P4: Perfect Alexander | 900.0 | Ability 4A8B (Perfect Alexander auto-attack), window 900,0 |

Damage events kept: raidwides, tankbusters, multi-hits, stacks, the LB3 check and enrages.

Left out:
- **Dodges:** Protean Wave, Sluice, Rage Wave, Exhaust, Hawk Blaster puddles, Chakrams, Spin Crusher, Flarethrower
  baits, Propeller Wind (line of sight), Judgment Crystal, Sacrament crosses, Radiant Sacrament, Almighty Judgment
  exatrines.
- **Casts that apply debuffs or check positions without damage on success:** Throttle (`Liquid Gaol`), Judgment
  Nisi, Link-Up, Enumeration (BossMod: "does nothing on success"), Plaint of Surety, The Final Word, Ordained
  Motion/Stillness.

| id | kind | hit | hits | entry (sync) | Action.csv: name (cast time, EffectRange) | aliases |
|---|---|---|---|---|---|---|
| `p1.pull` | marker | 0.0 | 0.0 | --sync-- (InCombat) |  | Pull, Prepull |
| `p1.fluid_swing_1` | tankbuster | 11.4 | 11.4 | Fluid Swing (Ability) | 49B0 Fluid Swing (0 s, r8) | Fluid Swing, Fluid Swing 1 |
| `p1.cascade_1` | raidwide | 19.5 | 19.5 | Cascade (Ability) | 4826 Cascade (4 s, r80) | Cascade, Cascade 1 |
| `p1.hand_1` | raidwide | 35.8 | 35.8 | Hand of Prayer/Parting (Ability) | 482B Hand of Prayer (0 s, r80); 482C Hand of Parting (0 s, r80) | Hand of Prayer/Parting, Hand of Parting/Prayer, Hand of Prayer, Hand of Parting |
| `p1.fluid_swing_2` | tankbuster | 37.7 | 37.7 | Fluid Swing (Ability) | 49B0 Fluid Swing (0 s, r8) | Fluid Swing, Fluid Strike, Fluid Swing 2, Cleaves (unverified) |
| `p1.hand_of_pain_1` | raidwide | 42.8 | 42.8 | Hand of Pain (Ability) | 482D Hand of Pain (3 s, r80) | Hand of Pain, Hand of Pain 1 |
| `p1.fluid_swing_3` | tankbuster | 56.7 | 56.7 | Fluid Swing (Ability) | 49B0 Fluid Swing (0 s, r8) | Fluid Swing, Fluid Strike, Fluid Swing 3 |
| `p1.hand_of_pain_2` | raidwide | 70.0 | 70.0 | Hand of Pain (Ability) | 482D Hand of Pain (3 s, r80) | Hand of Pain, Hand of Pain 2 |
| `p1.splash_1` | multihit | 81.1 | 81.1 | Splash x6 (unsynced) |  | Splash, Splash x6, Splash 1 |
| `p1.drainage` | tankbuster | 86.6 | 86.6 | Drainage (Ability) | 4827 Drainage (0 s, r6) | Drainage |
| `p1.hand_of_pain_3` | raidwide | 89.1 | 89.1 | Hand of Pain (Ability) | 482D Hand of Pain (3 s, r80) | Hand of Pain, Hand of Pain 3 |
| `p1.cascade_2` | raidwide | 91.7 | 91.7 | Cascade (Ability) | 4826 Cascade (4 s, r80) | Cascade, Cascade 2 |
| `p1.hand_of_pain_4` | raidwide | 109.0 | 109.0 | Hand of Pain (Ability) | 482D Hand of Pain (3 s, r80) | Hand of Pain, Hand of Pain 4 |
| `p1.hand_2` | raidwide | 124.2 | 124.2 | Hand of Prayer/Parting (Ability) | 482B Hand of Prayer (0 s, r80); 482C Hand of Parting (0 s, r80) | Hand of Prayer/Parting, Hand of Parting/Prayer, Hand of Prayer, Hand of Parting |
| `p1.splash_2` | multihit | 127.4 | 127.4 | Splash x6 (unsynced) |  | Splash, Splash x6, Splash 2 |
| `p1.hand_of_pain_5` | raidwide | 129.3 | 129.3 | Hand of Pain (Ability) | 482D Hand of Pain (3 s, r80) | Hand of Pain, Hand of Pain 5 |
| `p1.fluid_swing_4` | tankbuster | 134.1 | 134.1 | Fluid Swing (Ability) | 49B0 Fluid Swing (0 s, r8) | Fluid Swing, Fluid Swing 4 |
| `p1.cascade_enrage` | enrage | 142.1 | 142.1 | Cascade Enrage (Ability) | 49B3 Cascade (4 s, r80) | Cascade Enrage, P1 Enrage |
| `p2.limit_cut` | multihit | 207.4 | 207.4, 208.9, 212.1, 213.6, 216.8, 218.3, 221.4, 222.9 | #1 Alpha Sword (Ability) | 4834 Alpha Sword (0 s, r25) | Limit Cut, LC (unverified) |
| `p2.j_kick` | raidwide | 226.1 | 226.1 | J Kick (Ability) | 4854 J Kick (0 s, r80) | J Kick |
| `p2.whirlwind_1` | raidwide | 238.3 | 238.3 | Whirlwind (Ability) | 49C2 Whirlwind (4 s, r50) | Whirlwind, Whirlwind 1 |
| `p2.photon_1` | raidwide | 262.5 | 262.5 | Photon (Ability) | 4836 Photon (3 s, r0) | Photon, Photon 1 |
| `p2.water_thunder_1` | stack | 277.3 | 277.3 | Water and Thunder (Ability) | 4841 Crashing Thunder (0 s, r8) | Water and Thunder, Water and Thunder 1, Water/Lightning 1 (unverified) |
| `p2.water_thunder_2` | stack | 306.7 | 306.7 | Water and Thunder (Ability) | 4841 Crashing Thunder (0 s, r8) | Water and Thunder, Water and Thunder 2, Water/Lightning 2 (unverified) |
| `p2.whirlwind_2` | raidwide | 327.6 | 327.6 | Whirlwind (Ability) | 49C2 Whirlwind (4 s, r50) | Whirlwind, Whirlwind 2 |
| `p2.water_thunder_3` | stack | 336.2 | 336.2 | Water and Thunder (Ability) | 4841 Crashing Thunder (0 s, r8) | Water and Thunder, Water and Thunder 3, Water/Lightning 3 (unverified) |
| `p2.gavel` | raidwide | 356.5 | 356.5 | Gavel (Ability) | 483C Gavel (5 s, r80) | Gavel |
| `p2.photon_2` | raidwide | 366.8 | 366.8 | Photon (Ability) | 4836 Photon (3 s, r0) | Photon, Photon 2 |
| `p2.double_rocket_punch` | tankbuster | 374.8 | 374.8 | Double Rocket Punch (Ability) | 4847 Double Rocket Punch (4 s, r3) | Double Rocket Punch, DRP (unverified) |
| `p2.super_jump` | other | 382.1 | 382.1, 384.4 | Super Jump (Ability) | 484A Super Jump (0 s, r10) | Super Jump, Apocalyptic Ray |
| `p2.whirlwind_3` | raidwide | 395.0 | 395.0 | Whirlwind (Ability) | 49C2 Whirlwind (4 s, r50) | Whirlwind, Whirlwind 3 |
| `p2.whirlwind_4` | raidwide | 403.1 | 403.1 | Whirlwind (Ability) | 49C2 Whirlwind (4 s, r50) | Whirlwind, Whirlwind 4 |
| `p2.final_sentence` | enrage | 413.7 | 413.7 | Final Sentence (Ability) | 4856 Final Sentence (8 s, r0) | Final Sentence |
| `p2.eternal_darkness` | enrage | 423.7 | 423.7 | Eternal Darkness Enrage (Ability) | 483A Eternal Darkness (8 s, r100) | Eternal Darkness, P2 Enrage |
| `p3.temporal_stasis` | mixed | 503.1 | 503.1, 505.1, 505.2, 506.1, 507.2, 507.4 | Surety and Severity (Ability) | 4861 Plaint of Severity (0 s, r0) | Temporal Stasis, TS (unverified) |
| `p3.chastening_heat_1` | tankbuster | 521.5 | 521.5 | Chastening Heat (Ability) | 4A80 Chastening Heat (5 s, r5) | Chastening Heat, Chastening Heat 1 |
| `p3.divine_spear_1` | tankbuster | 524.6 | 524.6, 526.7, 528.8 | Divine Spear 1 (unsynced) | 4A82 Divine Spear (0 s, r17) | Divine Spear, Divine Spear 1 |
| `p3.inception` | mixed | 589.1 | 589.1, 595.3, 596.4, 597.5, 598.3 | Surety, Solidarity and Severity (Ability) | 4861 Plaint of Severity (0 s, r0) | Inception, Inception Formation |
| `p3.chastening_heat_2` | tankbuster | 610.9 | 610.9 | Chastening Heat (Ability) | 4A80 Chastening Heat (5 s, r5) | Chastening Heat, Chastening Heat 2 |
| `p3.divine_spear_2` | tankbuster | 616.0 | 616.0, 618.1, 620.2 | Divine Spear 1 (unsynced) | 4A82 Divine Spear (0 s, r17) | Divine Spear, Divine Spear 2 |
| `p3.wormhole` | multihit | 650.9 | 650.9, 652.4, 653.3, 655.2, 655.6, 656.7, 659.5, 659.7, 660.8, 663.6, 664.0, 665.1 | Super Jump (Ability) | 484A Super Jump (0 s, r10) | Wormhole, Wormhole Formation, WH (unverified) |
| `p3.incinerating_heat` | stack | 670.0 | 670.0 | Incinerating Heat (Ability) | 4A51 Incinerating Heat (5 s, r5) | Incinerating Heat |
| `p3.mega_holy_1` | raidwide | 682.9 | 682.9 | Mega Holy (Ability) | 4A83 Mega Holy (4 s, r80) | Mega Holy, Mega Holy 1 |
| `p3.mega_holy_2` | raidwide | 690.0 | 690.0 | Mega Holy (Ability) | 4A83 Mega Holy (4 s, r80) | Mega Holy, Mega Holy 2 |
| `p3.j_storm` | multihit | 705.7 | 705.7 | J Storm + Waves x16 (Ability) | 4876 J Storm (0 s, r80) | J Storm, J Waves, J Storm + Waves, DPS Check (unverified) |
| `p3.eternal_darkness` | enrage | 731.7 | 731.7 | Eternal Darkness Enrage (Ability) | 4875 Eternal Darkness (25 s, r100) | Eternal Darkness |
| `p3.alexander_down` | marker | 771.9 | 771.9 | Divine Judgment Enrage (Ability) | 4879 Divine Judgment (0 s, r0) | Alexander Prime Down |
| `p3.divine_judgment` | lb_check | 787.3 | 787.3 | Divine Judgment (Ability) | 487A Divine Judgment (0 s, r80) | Divine Judgment, Tank LB3, DJ (unverified) |
| `p4.start` | marker | 900.0 | 900.0 | --sync-- (Ability) | 4A8B (no name) (0 s, r0) | P4 Start |
| `p4.optical_sight` | mixed | 938.6 | 938.6, 944.7 | Individual/Collective Reprobation (Ability) | 488C Individual Reprobation (0 s, r6); 488D Collective Reprobation (0 s, r6) | Optical Sight, Individual/Collective Reprobation, Reprobation |
| `p4.fate_alpha` | mixed | 992.8 | 992.8, 993.8 | Obloquy, Solidarity and 3x Severity (Ability) | 4861 Plaint of Severity (0 s, r0) | Fate Calibration α, Fate Projection α, Fate Calibration Alpha, Fate Projection Alpha, Alpha, Fate Alpha (unverified) |
| `p4.ocp_1` | tankbuster | 1011.1 | 1011.1, 1012.2, 1013.3 | Ordained Capital Punishment 1 (unsynced) | 4893 Ordained Capital Punishment (0 s, r4) | Ordained Capital Punishment, Ordained Capital Punishment 1, OCP (unverified), OCP 1 (unverified) |
| `p4.op_1` | tankbuster | 1017.2 | 1017.2 | Ordained Punishment (Ability) | 4891 Ordained Punishment (5 s, r5) | Ordained Punishment, Ordained Punishment 1, OP 1 (unverified) |
| `p4.fate_beta` | mixed | 1081.5 | 1081.5, 1082.5, 1088.6 | Surety and Solidarity (Ability) | 4863 Plaint of Surety (0 s, r80) | Fate Calibration β, Fate Projection β, Fate Calibration Beta, Fate Projection Beta, Beta, Fate Beta (unverified) |
| `p4.ocp_2` | tankbuster | 1109.9 | 1109.9, 1111.0, 1112.1 | Ordained Capital Punishment 1 (unsynced) | 4893 Ordained Capital Punishment (0 s, r4) | Ordained Capital Punishment, Ordained Capital Punishment 2, OCP (unverified), OCP 2 (unverified) |
| `p4.op_2` | tankbuster | 1116.0 | 1116.0 | Ordained Punishment (Ability) | 4891 Ordained Punishment (5 s, r5) | Ordained Punishment, Ordained Punishment 2, OP 2 (unverified) |
| `p4.irresistible_grace_1` | stack | 1142.5 | 1142.5 | Irresistible Grace (Ability) | 4894 Irresistible Grace (5 s, r6) | Irresistible Grace, Irresistible Grace 1, Almighty Judgment, Almighty Judgment 1 |
| `p4.ocp_3` | tankbuster | 1155.7 | 1155.7, 1156.8, 1157.9 | Ordained Capital Punishment 1 (unsynced) | 4893 Ordained Capital Punishment (0 s, r4) | Ordained Capital Punishment, Ordained Capital Punishment 3, OCP (unverified), OCP 3 (unverified) |
| `p4.op_3` | tankbuster | 1161.8 | 1161.8 | Ordained Punishment (Ability) | 4891 Ordained Punishment (5 s, r5) | Ordained Punishment, Ordained Punishment 3, OP 3 (unverified) |
| `p4.irresistible_grace_2` | stack | 1187.9 | 1187.9 | Irresistible Grace (Ability) | 4894 Irresistible Grace (5 s, r6) | Irresistible Grace, Irresistible Grace 2, Almighty Judgment, Almighty Judgment 2 |
| `p4.temporal_prison` | enrage | 1247.6 | 1247.6 | Temporal Prison Enrage (Ability) | 4897 Temporal Prison (42 s, r100) | Temporal Prison, Temporal Interference, Enrage |

Evidence and **UNVERIFIED** points:
- **Damage nature:**
  - cactbot triggers: `TEA Cascade`, `TEA J Kick` and `TEA Whirlwind` use `Responses.aoe()`; `TEA Mega Holy` says
    "big aoe"; `TEA Fluid Swing`, `TEA Chastening Heat` and `TEA Ordained Punishment` give tankbuster output;
    `TEA Double Rocket Punch` and `TEA Ordained Capital Punishment` say "Shared Tankbuster";
    `TEA Irresistible Grace` uses `stackMarkerOn`; `TEA Divine Judgment` says "TANK LB!!".
  - BossMod `TEAEnums.cs` comments, e.g. `HandOfPain` "raidwide (huge damage if hp diff is large)", `JStorm`
    "range 80+R circle", `DivineJudgmentRaidwide` "raidwide requiring tank LB3", `PhotonAOE` "set hp to 1 for each
    player".
  - BossMod `TEAStates.cs` `Raidwide`/`Tankbuster` hints.
  - `Action.csv` `EffectRange` 80 for Cascade, Hand of Prayer/Parting/Pain, J Kick, J Storm, Mega Holy and Divine
    Judgment.
- `p2.gavel` as a raidwide is **UNVERIFIED**. BossMod's enum says "??? raidwide, nisi resolve" but its states give
  no Raidwide hint; `Action.csv` 483C has a 5 s cast and `EffectRange` 80.
- `p1.splash_2`: cactbot names it "Splash x6", but BossMod's states count 4 hits there (6 at `p1.splash_1`). Which
  is right is **UNVERIFIED**.
- `p2.photon_1/2` set every player to 1 HP (4837 `PhotonAOE`). Mitigation has no effect; the anchors are for heals.
  BossMod's second Photon state carries a Tankbuster hint, probably because Double Rocket Punch follows.
- `p2.super_jump` (Super Jump + Apocalyptic Ray): who takes the hits is **UNVERIFIED** (BossMod: the jump is baited
  on the farthest player), so it is kind `other`.
- `p3.alexander_down` is a marker. cactbot names the line "Divine Judgment Enrage", but it syncs on 4879, which
  BossMod calls `DivineJudgmentSuccess` ("visual (if enrage is interrupted by killing)") and `Action.csv` lists as
  `EffectRange` 0. The tank-LB3 raidwide 487A follows 15.4 s later, in line with BossMod's 2.1 + 13.5 s.
- `p2.water_thunder_n` sync on 4841, which `Action.csv` names "Crashing Thunder": BossMod's 2-person lightning
  stack. BossMod's 3+ person water stack 4840 lands at the same time.
- Community shorthand is marked `# unverified`: `LC`, `TS`, `WH`, `DRP`, `OCP`, `OP`, `DJ`, `Cleaves`,
  `Water/Lightning n`, `Fate Alpha/Beta`, `DPS Check`.

## 7. DMU (first draft)

Territory 1363, `ClassJobLevelSync` 100 (§8). Phases follow cactbot's five sections and BossMod's five phases.
Each `start` is the phase's first sync point or jump label. P3 and P5 start at labels, which are not entries, so
they have no start marker. P3's label `p2-success` (597.0) comes before cactbot's `### Phase 3` header (633.2,
`ActorControl` data0 "14"), so Aero III Assault (600.0), which cactbot lists in its Phase 2 section, falls in `p3`.
BossMod agrees: Aero III Assault is the first cast of its P3 (`KefkaP3`, state "Knockback").

| id | name | start | sync point |
|---|---|---|---|
| p1 | P1: Kefka | 0.0 | InCombat (pull) |
| p2 | P2: God Kefka | 297.4 | ActorControl 80000027 data0 05/0D, window 300,5 (Kefka <15% HP); not a v1 sync type, next v1 sync is Ability C24C @320.1 (default window) |
| p3 | P3: Chaos + Exdeath | 597.0 | label "p2-success": StartsUsing C3F7 @483.2 (window 485,5) jumps here; Ability C3F7 @600.0, window 600,20 |
| p4 | P4: Kefka + Chaos + Neo Exdeath | 1087.7 | ActorControl 80000027 data0 2E, window 1088,14000; not a v1 sync type, next v1 syncs are StartsUsing/Ability C2DC @1095.0/1100.0 (default window) |
| p5 | P5: Kefka | 1355.6 | label "p4-success": Ability C3FD @1215.3 (window 90,5) jumps here; StartsUsing BB40 @1396.2, window 1400,0 |

| id | kind | hit | hits | entry (sync) | Action.csv: name (cast time, EffectRange) | aliases |
|---|---|---|---|---|---|---|
| `p1.pull` | marker | 0.0 | 0.0 | --sync-- (InCombat) |  | Pull, Prepull |
| `p1.revolting_ruin_1` | tankbuster | 15.6 | 15.6, 18.7 | Revolting Ruin III 1 (Ability) | C403 Revolting Ruin III (5 s, r100) | Revolting Ruin III, Revolting Ruin, Revolting Ruin III 1 |
| `p1.flagrant_fire_1` | mixed | 38.3 | 38.3 | Flagrant Fire III (Ability) | BAA2 Flagrant Fire III (0 s, r5); BAA3 Flagrant Fire III (0 s, r6) | Flagrant Fire III, Flagrant Fire, Flagrant Fire III 1 |
| `p1.wave_cannon` | mixed | 42.5 | 42.5, 46.0 | Wave Cannon (Ability) | BAA8 Wave Cannon (0 s, r100) | Wave Cannon, Explosion |
| `p1.confetti_1` | stack | 49.7 | 49.7 | Double-trouble Trap 2 (Ability) | BAA7 Double-trouble Trap (0 s, r6) | Double-trouble Trap, Double-trouble Trap 2, Confetti 1, Confetti |
| `p1.light_of_judgment_1` | raidwide | 62.7 | 62.7 | Light of Judgment (Ability) | C622 Light of Judgment (5 s, r100) | Light of Judgment, Light of Judgment 1 |
| `p1.hyperdrive_1` | tankbuster | 65.8 | 65.8, 67.8, 69.8 | Hyperdrive 1 (unsynced) | C24B Hyperdrive (0 s, r5) | Hyperdrive, Hyperdrive 1 |
| `p1.gravitas_1` | mixed | 87.2 | 87.2, 91.2 | Gravitas (Ability) | BAAC Gravitas (0 s, r5) | Gravitas, Gravitas 1, Vitrophyre |
| `p1.revolting_ruin_2` | tankbuster | 97.1 | 97.1, 100.2 | Revolting Ruin III 1 (Ability) | C403 Revolting Ruin III (5 s, r100) | Revolting Ruin III, Revolting Ruin, Revolting Ruin III 2 |
| `p1.gravitas_2` | mixed | 105.8 | 105.8, 109.8 | Gravitas (Ability) | BAAC Gravitas (0 s, r5) | Gravitas, Gravitas 2, Vitrophyre |
| `p1.confetti_2` | stack | 118.1 | 118.1 | Double-trouble Trap 3 (Ability) | BAA7 Double-trouble Trap (0 s, r6) | Double-trouble Trap, Double-trouble Trap 3, Confetti 2, Confetti |
| `p1.light_of_judgment_2` | raidwide | 132.4 | 132.4 | Light of Judgment (Ability) | C622 Light of Judgment (5 s, r100) | Light of Judgment, Light of Judgment 2 |
| `p1.hyperdrive_2` | tankbuster | 135.6 | 135.6, 137.7, 139.7 | Hyperdrive 1 (unsynced) | C24B Hyperdrive (0 s, r5) | Hyperdrive, Hyperdrive 2 |
| `p1.flagrant_fire_2` | mixed | 187.1 | 187.1 | Flagrant Fire III (Ability) | BAA2 Flagrant Fire III (0 s, r5); BAA3 Flagrant Fire III (0 s, r6) | Flagrant Fire III, Flagrant Fire, Flagrant Fire III 2 |
| `p2.ultimate_embrace_1` | tankbuster | 320.1 | 320.1 | Ultimate Embrace (Ability) | C24C Ultimate Embrace (5 s, r5) | Ultimate Embrace, Ultimate Embrace 1 |
| `p2.forsaken` | raidwide | 335.3 | 335.3 | Forsaken (Ability) | BABC Forsaken (7 s, r100) | Forsaken |
| `p2.path_of_light_1` | multihit | 348.5 | 348.5, 349.1, 357.8, 358.4, 359.1 | The Path of Light 1 (Ability) | BABE the Path of Light (0 s, r4) | The Path of Light 1, Path of Light 1, Set 1 (unverified) |
| `p2.path_of_light_2` | multihit | 369.4 | 369.4, 370.0, 378.7, 379.4, 380.1 | The Path of Light 3 (Ability) | BABE the Path of Light (0 s, r4) | The Path of Light 3, Path of Light 2, Set 2 (unverified) |
| `p2.path_of_light_3` | multihit | 390.5 | 390.5, 391.2, 400.0, 400.5, 401.1 | The Path of Light 5 (Ability) | BABE the Path of Light (0 s, r4) | The Path of Light 5, Path of Light 3, Set 3 (unverified) |
| `p2.path_of_light_4` | multihit | 411.4 | 411.4, 412.0, 420.7, 421.4, 422.0 | The Path of Light 7 (Ability) | BABE the Path of Light (0 s, r4) | The Path of Light 7, Path of Light 4, Set 4 (unverified) |
| `p2.light_of_judgment` | raidwide | 441.1 | 441.1 | Light of Judgment (Ability) | BABD Light of Judgment (5 s, r100) | Light of Judgment |
| `p2.wings_of_destruction` | tankbuster | 469.5 | 469.5 | Wings of Destruction (Ability) | C487 Wings of Destruction (4 s, r0) | Wings of Destruction |
| `p2.ultimate_embrace_2` | tankbuster | 476.7 | 476.7 | Ultimate Embrace (Ability) | C24C Ultimate Embrace (5 s, r5) | Ultimate Embrace, Ultimate Embrace 2 |
| `p3.aero_iii_assault` | raidwide | 600.0 | 600.0 | Aero III Assault (Ability) | C3F7 Aero III Assault (3 s, r40) | Aero III Assault |
| `p3.bowels_of_agony` | raidwide | 662.9 | 662.9 | Bowels of Agony (Ability) | BAF2 _rsv_ (5 s, r100) | Bowels of Agony |
| `p3.elements_1` | mixed | 682.0 | 682.0, 683.0, 684.7 | Stray Flames/Stray Spray (Ability) | BAF3 _rsv_ (0 s, r5); BAF6 _rsv_ (0 s, r10) | Stray Flames/Stray Spray, Inferno/Tsunami, Cyclone, Elements 1 (unverified) |
| `p3.thunder_buster_1` | tankbuster | 691.3 | 691.3, 694.3 | Thunder III 1 (unsynced) | BB0C _rsv_ (0 s, r5) | Thunder III, Thunder III 1 |
| `p3.elements_2` | mixed | 709.0 | 709.0, 710.0, 711.7 | Stray Spray/Stray Flames (Ability) | BAF6 _rsv_ (0 s, r10); BAF3 _rsv_ (0 s, r5) | Stray Spray/Stray Flames, Tsunami/Inferno, Cyclone, Elements 2 (unverified) |
| `p3.ultima_blaster` | multihit | 720.1 | 720.1, 722.1, 724.1, 724.2, 726.1, 727.4, 728.2, 730.2, 732.2, 734.2 | Ultima Blaster 1 (unsynced) | BAE3 _rsv_ (0 s, r100) | Ultima Blaster |
| `p3.thunder_buster_2` | tankbuster | 749.7 | 749.7, 752.7 | Thunder III 1 (unsynced) | BB0C _rsv_ (0 s, r5) | Thunder III, Thunder III 2 |
| `p3.thunder_buster_3` | tankbuster | 766.9 | 766.9, 769.9 | Thunder III 1 (unsynced) | BB0C _rsv_ (0 s, r5) | Thunder III, Thunder III 3 |
| `p3.earthquake` | raidwide | 772.0 | 772.0 | Earthquake (Ability) | C571 _rsv_ (5 s, r0) | Earthquake |
| `p3.crust_quakes` | multihit | 777.1 | 777.1, 781.2, 806.9, 830.3, 835.8, 840.5, 864.6, 869.7, 874.8, 898.2, 905.4 | Accretion Earthquake 1? (unsynced) | BAFA _rsv_ (0 s, r100) | Accretion Earthquake, Primordial Crust Quake, Quakes (unverified) |
| `p3.slap_happy_1` | stack | 790.8 | 790.8 | Shocking Impact/Shockwave (unsynced) | BAEA _rsv_ (0 s, r100); BAEB _rsv_ (0 s, r100) | Slap Happy, Slap Happy 1, Shocking Impact/Shockwave |
| `p3.thunder_buster_4` | tankbuster | 808.5 | 808.5, 811.5 | Thunder III 1 (unsynced) | BB0C _rsv_ (0 s, r5) | Thunder III, Thunder III 4 |
| `p3.slap_happy_2` | stack | 821.2 | 821.2 | Shocking Impact/Shockwave (unsynced) | BAEA _rsv_ (0 s, r100); BAEB _rsv_ (0 s, r100) | Slap Happy, Slap Happy 2, Shocking Impact/Shockwave |
| `p3.thunder_buster_5` | tankbuster | 849.8 | 849.8, 852.8 | Thunder III 1 (unsynced) | BB0C _rsv_ (0 s, r5) | Thunder III, Thunder III 5 |
| `p3.slap_happy_3` | stack | 889.2 | 889.2 | Shocking Impact/Shockwave (unsynced) | BAEA _rsv_ (0 s, r100); BAEB _rsv_ (0 s, r100) | Slap Happy, Slap Happy 3, Shocking Impact/Shockwave |
| `p3.knock_down` | stack | 918.3 | 918.3, 923.8 | Knock Down 1 (Ability) | BB03 _rsv_ (0 s, r6) | Knock Down, Stomp-a-Mole (unverified) |
| `p3.deep_freeze` | other | 927.0 | 927.0 | Blizzard III (Ability) | BB11 _rsv_ (4 s, r100) | Deep Freeze, Blizzard III |
| `p4.grand_cross_1` | raidwide | 1113.9 | 1113.9 | Grand Cross (Ability) | BB14 _rsv_ (9 s, r100) | Grand Cross, Grand Cross 1 |
| `p4.inferno_tsunami_1` | raidwide | 1118.9 | 1118.9 | Inferno/Tsunami (Ability) | BB20 _rsv_ (9 s, r100); BB21 _rsv_ (9 s, r100) | Inferno/Tsunami, Inferno, Tsunami, Fire/Water 1 (unverified) |
| `p4.grand_cross_2` | raidwide | 1128.7 | 1128.7 | Grand Cross (Ability) | BB14 _rsv_ (9 s, r100) | Grand Cross, Grand Cross 2 |
| `p4.inferno_tsunami_2` | raidwide | 1133.7 | 1133.7 | Tsunami/Inferno (Ability) | BB21 _rsv_ (9 s, r100); BB20 _rsv_ (9 s, r100) | Tsunami/Inferno, Inferno, Tsunami, Fire/Water 2 (unverified) |
| `p4.grand_cross_3` | raidwide | 1143.6 | 1143.6 | Grand Cross (Ability) | BB14 _rsv_ (9 s, r100) | Grand Cross, Grand Cross 3 |
| `p4.death_surge` | raidwide | 1159.9 | 1159.9 | Death Surge (Ability) | BB1C _rsv_ (0 s, r100); BB1D _rsv_ (0 s, r100) | Death Surge |
| `p4.death_debuffs_1` | mixed | 1164.5 | 1164.5 | Death Wave (unsynced) | BB1A _rsv_ (0 s, r8); BB1B _rsv_ (0 s, r8) | Death Bolt, Death Wave, Stack/Spread 1 (unverified) |
| `p4.ultima_upsurge_1` | raidwide | 1182.5 | 1182.5 | Ultima Upsurge (Ability) | C24A Ultima Upsurge (5 s, r100) | Ultima Upsurge, Ultima Upsurge 1 |
| `p4.death_debuffs_2` | mixed | 1189.4 | 1189.4 | Death Wave (Ability) | BB1A _rsv_ (0 s, r8); BB1B _rsv_ (0 s, r8) | Death Wave, Death Bolt, Stack/Spread 2 (unverified) |
| `p5.ultima_upsurge` | raidwide | 1362.0 | 1362.0 | Ultima Upsurge (Ability) | C24A Ultima Upsurge (5 s, r100) | Ultima Upsurge, Ultima Upsurge 2 |
| `p5.ultima_repeater_1` | multihit | 1401.3 | 1401.3, 1402.1, 1402.8, 1403.6 | Ultima Repeater 1 (unsynced) | BB41 _rsv_ (0 s, r100) | Ultima Repeater, Ultima Repeater 1 |
| `p5.fell_forces_1` | multihit | 1406.2 | 1406.2, 1409.4, 1412.5 | Fell Forces 1 (Ability) | C654 Fell Forces (0 s, r5); C653 Fell Forces (0 s, r3); C654 Fell Forces (0 s, r5) | Fell Forces, Fell Forces 1 |
| `p5.flood` | stack | 1419.0 | 1419.0 | Chaotic Flood x4 (unsynced) | BB4F _rsv_ (0 s, r6) | Flood, Chaotic Flood |
| `p5.maddening_orchestra_1` | mixed | 1431.9 | 1431.9, 1435.1, 1438.6 | Holy (Ability) | BB54 _rsv_ (0 s, r5) | Maddening Orchestra, Maddening Orchestra 1 |
| `p5.fell_forces_2` | multihit | 1443.2 | 1443.2, 1446.3 | Fell Forces 1 (Ability) | C654 Fell Forces (0 s, r5); C653 Fell Forces (0 s, r3); C654 Fell Forces (0 s, r5) | Fell Forces, Fell Forces 2 |
| `p5.celestriad` | mixed | 1461.8 | 1461.8, 1467.8, 1473.8 | Fire III/Blizzard III/Thunder III (unsynced) | BB43 _rsv_ (0 s, r3); BB44 _rsv_ (0 s, r3); BB45 _rsv_ (0 s, r3) | Celestriad |
| `p5.ultima_repeater_2` | multihit | 1483.2 | 1483.2, 1484.0, 1484.7, 1485.5 | Ultima Repeater 1 (unsynced) | BB41 _rsv_ (0 s, r100) | Ultima Repeater, Ultima Repeater 2 |
| `p5.fell_forces_3` | multihit | 1488.1 | 1488.1, 1491.3 | Fell Forces 1 (Ability) | C654 Fell Forces (0 s, r5); C653 Fell Forces (0 s, r3); C654 Fell Forces (0 s, r5) | Fell Forces, Fell Forces 3 |
| `p5.stray_entropy` | spread | 1514.7 | 1514.7 | Stray Entropy (Ability) | BB3F _rsv_ (0 s, r5) | Stray Entropy, Stray Apocalypse |
| `p5.maddening_orchestra_2` | mixed | 1523.9 | 1523.9, 1527.1, 1530.6 | Holy (Ability) | BB54 _rsv_ (0 s, r5) | Maddening Orchestra, Maddening Orchestra 2 |
| `p5.fell_forces_4` | multihit | 1535.2 | 1535.2, 1538.3, 1541.4 | Fell Forces 1 (Ability) | C654 Fell Forces (0 s, r5); C653 Fell Forces (0 s, r3); C654 Fell Forces (0 s, r5) | Fell Forces, Fell Forces 4 |
| `p5.forsaken` | multihit | 1552.7 | 1552.7, 1558.7, 1560.9, 1565.9, 1569.0, 1574.0, 1577.2, 1582.2 | Forsaken 1 (Ability) | BB35 _rsv_ (10 s, r100) | Forsaken, P5 Forsaken |
| `p5.forsaken_null` | enrage | 1613.4 | 1613.4 | Forsaken Null (Enrage) (Ability) | BB3A _rsv_ (26 s, r100) | Forsaken Null, Enrage |

Evidence and **UNVERIFIED** points:
- **Damage nature:**
  - cactbot triggers: `DMU P1 Light of Judgment`, `DMU P2 Forsaken`, `DMU P2 Light of Judgment` and
    `DMU P5 Ultima Upsurge` use `Responses.bigAoe()`; `DMU P4 Ultima Upsurge` builds its own `infoText` that
    starts with the `bigAoe` output (followed by the player's debuff call); `DMU P3 Bowels of Agony` and
    `DMU P4 Grand Cross` use `Responses.aoe()`; `DMU P1 Hyperdrive` uses `tankBuster()`;
    `DMU P2 Ultimate Embrace` uses `sharedTankBuster()`; `DMU P3 Thunder III Tankbuster` calls tank cleaves.
  - BossMod `UMADStates.cs` hints and components, e.g. `P2UltimateEmbrace` = `CastSharedTankbuster`,
    `P3EarthquakeRaidwide` = `RaidwideCast`, `P1Hyperdrive` tankbuster bait; `UMADEnums.cs` comments.
  - `Action.csv` ranges.
- Many DMU boss actions are named `_rsv_<id>_…` in `Action.csv`, for example BAF2 Bowels of Agony, BB14 Grand
  Cross and BB41 Ultima Repeater. The client gets these names at run time, so the plugin should not take boss
  ability names from Lumina for this fight; use the catalog and cactbot names.
  - Dalamud 15.0.3.9 wires an internal resolver into Lumina for this: `Dalamud.dll` hooks the game's
    `AddRsvString` and sets Lumina's `LuminaOptions.RsvResolver`. A plugin cannot call `Dalamud.Data.RsvResolver`
    itself (a compiled probe fails with CS0122, inaccessible). A name read through `IDataManager` can therefore
    resolve only after the client has received it in the duty (run-time behaviour **UNVERIFIED**), and is still
    the `_rsv_` placeholder before that, for example at plan import.
  - `Lumina.Extensions.RsvExtensions.IsRsv(ReadOnlySeString)` is public (compiled) and detects the placeholder.
- `p3.earthquake`:
  - cactbot's ability list calls C572 "Earthquake: VFX". BossMod calls it `EarthquakeRaidwide` (a `RaidwideCast`
    component), and `Action.csv` gives C572 a 5 s cast with `EffectRange` 100.
  - It is anchored on the C571 cast end (772.0), with kind `raidwide` per BossMod.
- `p3.crust_quakes` (BAFA, `EffectRange` 100) is **UNVERIFIED** as party damage.
  - cactbot: "Cast when Accretion or Primordial Crust are cleansed … Determined by healing player to full".
  - Its times are player-dependent estimates, most marked `?` in the timeline.
- `p3.aero_iii_assault`: damage **UNVERIFIED**. BossMod models it only as a knockback, though `Action.csv` gives it
  `EffectRange` 40.
- `p4.death_surge` (BB1C/BB1D, range-100 debuff explosions) as a party-wide hit is **UNVERIFIED**, and so is
  `p3.deep_freeze` (BB11, a 4 s range-100 cast that BossMod labels "Deep freeze" without a raidwide hint).
- `p1.confetti_1/2` (Double-trouble Trap; BossMod: `UniformStackSpread` with stack radius 6 and at least 4
  players): mitigation relevance is **UNVERIFIED**.
  cactbot notes the second one happens "If it was passed after first set".
- `p5.ultima_upsurge` (1362.0) is in cactbot's P5 section (cactbot even has a `DMU P5 Ultima Upsurge` trigger), but
  BossMod treats it as the last P4 cast.
- The P1, P2, P3 and P4 enrages are not in the draft. Their real entries sit behind jump labels (10000+). The main
  timeline only has `?` preview entries for them: 202.4, 485.0, 946.8 and 1218.1 are unsynced, while the two
  951.8 lines (C61F, C61E) do sync but are reached only if the 941.8 jumps do not fire.
- P3's White Hole (BD66, BossMod "HP check") is not anchored.
- No community DMU shorthand was checked against a source; the few names used are marked `# unverified`.

## 8. Sync level and territory (verified)

| Fight | TerritoryType row | TerritoryType.ContentFinderCondition | CFC Name | CFC.TerritoryType | ClassJobLevelRequired | ClassJobLevelSync | ItemLevelSync | cactbot `ZoneId` |
|---|---|---|---|---|---|---|---|---|
| TOP | 1122 (`z3oz`) | 908 | the Omega Protocol (Ultimate) | 1122 | 90 | **90** | 635 | `TheOmegaProtocolUltimate: 1122` |
| TEA | 887 (`d2az`) | 694 | the Epic of Alexander (Ultimate) | 887 | 80 | **80** | 475 | `TheEpicOfAlexanderUltimate: 887` |
| DMU | 1363 (`z3oy`) | 1094 | Dancing Mad (Ultimate) | 1363 | 100 | **100** | 0 | `DancingMadUltimate: 1363` |

DMU's `ItemLevelSync` is 0 in this export (TOP 635, TEA 475): either no item-level sync, or the field is not
filled. This is **UNVERIFIED** and irrelevant to the catalog.

## 9. Open questions

1. LPDU's intent for the tank `Limitless` row (Bladedance vs flares/Cosmo Memory) and for the second P5
   `Hello World` (first vs second Omega set), §4.6.
2. Whether `FightLoader` accepts `kind: marker` (it is in `MechanicKind`, not in the task's kind list). With
   YamlDotNet the real pitfall is `lb_check`, which needs an underscored enum naming convention (§2).
3. `ActorControl` support for DMU (§5.5), and how to trigger reminders "before the phase ends" (§4.6 item 6).
4. Why the Carry the Clear P5/P6 times drift against cactbot by 0.5–0.75 s per 100 s (§4.2).
5. TEA/DMU: confirm the UNVERIFIED damage events with a log or a real plan before shipping plans for them.

## Appendix: TOP mechanics (summary of `top.yaml`)

| id | phase | kind | hit | hits | entry | aliases (count) |
|---|---|---|---|---|---|---|
| `p1.pull` | p1 | marker | 0.0 | 0.0 | --sync-- | 3 |
| `p1.program_loop` | p1 | multihit | 29.1 | 29.1, 38.1, 47.1, 56.1 | Storage Violation 1 | 4 |
| `p1.loop_set1` | p1 | mixed | 29.1 | 29.1 | Storage Violation 1 | 6 |
| `p1.loop_set2` | p1 | mixed | 38.1 | 38.1 | Storage Violation 2 | 6 |
| `p1.loop_set3` | p1 | mixed | 47.1 | 47.1 | Storage Violation 3 | 6 |
| `p1.loop_set4` | p1 | mixed | 56.1 | 56.1 | Storage Violation 4 | 6 |
| `p1.pantokrator` | p1 | multihit | 81.3 | 81.3, 87.3, 93.3, 99.3 | Condensed Wave Cannon Kyrios 1 | 3 |
| `p1.panto_stack1` | p1 | stack | 81.3 | 81.3 | Condensed Wave Cannon Kyrios 1 | 5 |
| `p1.panto_stack2` | p1 | stack | 87.3 | 87.3 | Condensed Wave Cannon Kyrios 2 | 5 |
| `p1.panto_stack3` | p1 | stack | 93.3 | 93.3 | Condensed Wave Cannon Kyrios 3 | 5 |
| `p1.panto_stack4` | p1 | stack | 99.3 | 99.3 | Condensed Wave Cannon Kyrios 4 | 5 |
| `p1.wave_cannon_kyrios` | p1 | mixed | 108.0 | 108.0, 116.8 | Wave Cannon Kyrios 1 | 7 |
| `p1.atomic_ray` | p1 | enrage | 127.7 | 127.7 | Atomic Ray Enrage | 4 |
| `p2.start` | p2 | marker | 200.0 | 200.0 | --sync-- | 2 |
| `p2.targetable` | p2 | marker | 202.3 | 202.3 | --targetable-- | 1 |
| `p2.solar_ray` | p2 | tankbuster | 214.5 | 214.5 | Solar Ray | 3 |
| `p2.party_synergy_spreads` | p2 | spread | 247.7 | 247.7 | Optimized Fire III | 6 |
| `p2.party_synergy_stacks` | p2 | stack | 259.1 | 259.1 | Spotlight x2 | 5 |
| `p2.bladedance` | p2 | mixed | 295.6 | 295.6, 295.7 | Optimized Bladedance x2 | 6 |
| `p2.limitless` | p2 | multihit | 309.7 | 309.7, 318.8 | Optimized Meteor | 3 |
| `p2.flares` | p2 | mixed | 309.7 | 309.7, 310.0 | Optimized Meteor | 5 |
| `p2.cosmo_memory` | p2 | raidwide | 318.8 | 318.8 | Cosmo Memory | 4 |
| `p2.laser_shower` | p2 | enrage | 347.4 | 347.4 | Laser Shower Enrage | 4 |
| `p3.start` | p3 | marker | 400.0 | 400.0 | --sync-- | 2 |
| `p3.transition` | p3 | mixed | 427.3 | 427.3, 428.4 | High-powered Sniper Cannon x2 | 7 |
| `p3.targetable` | p3 | marker | 432.3 | 432.3 | --targetable-- | 1 |
| `p3.hello_world` | p3 | raidwide | 441.3 | 441.3 | Hello, World | 5 |
| `p3.latent_1` | p3 | mixed | 464.4 | 464.4, 466.9 | Latent Defect 1 | 5 |
| `p3.latent_2` | p3 | mixed | 485.4 | 485.4, 487.9 | Latent Defect 2 | 5 |
| `p3.latent_3` | p3 | mixed | 506.5 | 506.5, 509.0 | Latent Defect 3 | 5 |
| `p3.latent_4` | p3 | mixed | 527.8 | 527.8, 533.5 | Latent Defect 4 | 5 |
| `p3.critical_error` | p3 | raidwide | 550.1 | 550.1 | Critical Error | 3 |
| `p3.monitors` | p3 | spread | 571.4 | 571.4 | Oversampled Wave Cannon | 5 |
| `p3.ion_efflux` | p3 | enrage | 587.4 | 587.4 | Ion Efflux Enrage | 4 |
| `p4.start` | p4 | marker | 600.0 | 600.0 | --sync-- | 2 |
| `p4.targetable` | p4 | marker | 607.1 | 607.1 | --targetable-- | 1 |
| `p4.wave_cannon` | p4 | multihit | 621.4 | 621.4, 627.0, 631.5, 636.8, 641.6, 646.9 | Wave Cannon A | 5 |
| `p4.protean_1` | p4 | spread | 621.4 | 621.4 | Wave Cannon A | 3 |
| `p4.stack_1` | p4 | stack | 627.0 | 627.0 | Wave Cannon (stacks) | 3 |
| `p4.protean_2` | p4 | spread | 631.5 | 631.5 | Wave Cannon A | 3 |
| `p4.stack_2` | p4 | stack | 636.8 | 636.8 | Wave Cannon (stacks) | 3 |
| `p4.protean_3` | p4 | spread | 641.6 | 641.6 | Wave Cannon A | 3 |
| `p4.stack_3` | p4 | stack | 646.9 | 646.9 | Wave Cannon (stacks) | 3 |
| `p4.blue_screen` | p4 | raidwide | 661.6 | 661.6 | Blue Screen Enrage | 3 |
| `p5.start` | p5 | marker | 700.0 | 700.0 | --sync-- | 2 |
| `p5.targetable` | p5 | marker | 715.6 | 715.6 | --targetable-- | 1 |
| `p5.solar_ray_1` | p5 | tankbuster | 723.6 | 723.6, 726.8 | Solar Ray 1 | 4 |
| `p5.delta` | p5 | raidwide | 740.0 | 740.0 | Run: \*\*\*\*mi\* (Delta Version) | 6 |
| `p5.delta_all` | p5 | multihit | 761.2 | 761.2, 764.9, 771.1, 773.6, 774.0, 782.0, 784.0, 787.0, 788.0, 789.0 | Patch | 3 |
| `p5.delta_patch` | p5 | raidwide | 761.2 | 761.2, 764.9, 782.0, 784.0 | Patch | 3 |
| `p5.delta_monitors` | p5 | mixed | 771.1 | 771.1, 773.6, 774.0 | Hyper Pulse | 3 |
| `p5.delta_hello_world` | p5 | mixed | 787.0 | 787.0, 788.0, 789.0 | Hello, Distant World 1 | 5 |
| `p5.solar_ray_2` | p5 | tankbuster | 805.2 | 805.2, 808.5 | Solar Ray 1 | 4 |
| `p5.sigma` | p5 | raidwide | 821.7 | 821.7 | Run: \*\*\*\*mi\* (Sigma Version) | 6 |
| `p5.sigma_proteans` | p5 | mixed | 843.8 | 843.8, 844.0 | Hyper Pulse | 4 |
| `p5.sigma_towers` | p5 | spread | 856.4 | 856.4 | Storage Violation x5 / Storage Violation x6 | 3 |
| `p5.sigma_hello_world` | p5 | mixed | 880.6 | 880.6, 881.6, 882.6 | Hello, Near World 1 | 4 |
| `p5.solar_ray_3` | p5 | tankbuster | 894.3 | 894.3, 897.4 | Solar Ray 1 | 4 |
| `p5.omega` | p5 | raidwide | 910.7 | 910.7 | Run: \*\*\*\*mi\* (Omega Version) | 6 |
| `p5.omega_all` | p5 | multihit | 927.8 | 927.8, 931.9, 945.4, 945.8, 946.4, 947.4, 961.6, 963.4, 964.4, 965.4 | Diffuse Wave Cannon | 3 |
| `p5.omega_cannons` | p5 | other | 927.8 | 927.8, 931.9 | Diffuse Wave Cannon | 2 |
| `p5.omega_hello_world_1` | p5 | mixed | 945.4 | 945.4, 945.8, 946.4, 947.4 | Hello, Distant World 1 | 6 |
| `p5.omega_hello_world_2` | p5 | mixed | 961.6 | 961.6, 963.4, 964.4, 965.4 | Blaster x2 | 6 |
| `p5.solar_ray_4` | p5 | tankbuster | 976.8 | 976.8, 979.9 | Solar Ray 1 | 4 |
| `p5.blind_faith` | p5 | raidwide | 999.7 | 999.7 | Blind Faith Enrage | 4 |
| `p6.start` | p6 | marker | 1100.0 | 1100.0 | --sync-- | 2 |
| `p6.targetable` | p6 | marker | 1154.7 | 1154.7 | --targetable-- | 1 |
| `p6.cosmo_memory` | p6 | lb_check | 1167.8 | 1167.8 | Cosmo Memory | 4 |
| `p6.cosmo_arrow_1` | p6 | other | 1182.2 | 1182.2 | Cosmo Arrow | 4 |
| `p6.cosmo_dive_1` | p6 | mixed | 1199.3 | 1199.3 | Cosmo Dive Far | 6 |
| `p6.wave_cannon_1` | p6 | mixed | 1236.0 | 1236.0, 1238.0, 1244.3 | Wave Cannon 1 | 7 |
| `p6.wave_cannon_2` | p6 | mixed | 1269.9 | 1269.9, 1271.9, 1278.2 | Wave Cannon 1 | 6 |
| `p6.unlimited_wave_cannon_2` | p6 | other | 1299.7 | 1299.7, 1301.5, 1303.4, 1305.3, 1307.2, 1309.2 | Wave Cannon Puddle 1 | 2 |
| `p6.cosmo_dive_2` | p6 | mixed | 1312.4 | 1312.4 | Cosmo Dive Far | 4 |
| `p6.cosmo_meteor` | p6 | multihit | 1335.6 | 1335.6, 1336.7, 1337.7, 1342.7, 1343.7 | Cosmo Meteor Baits | 5 |
| `p6.flares` | p6 | mixed | 1354.7 | 1354.7 | Cosmo Meteor Flare x3 | 5 |
| `p6.magic_number_1` | p6 | lb_check | 1366.7 | 1366.7 | Magic Number | 4 |
| `p6.magic_number_2` | p6 | lb_check | 1382.8 | 1382.8 | Magic Number | 4 |
| `p6.run_mi_enrage` | p6 | enrage | 1421.9 | 1421.9 | Run: \*\*\*\*mi\* Enrage | 6 |

## Verification

An independent pass on 2026-11-11 re-derived this note's claims from the primary sources: the cactbot clone
`e79ea7f1`, the BossMod clone `6e410ef8`, the installed Dalamud 15.0.3.9 assemblies and XML docs, the game-data CSVs,
and both spreadsheets read again with the Google Sheets tools. It used its own scripts and probes, not the
researcher's. They are in `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/verify-mechanics/`:

| Artefact | Purpose |
|---|---|
| `vtl.py` | Own timeline reader and catalog checker: every `hit`/`entry` pair, every `hits` value, phase containment of every hit, ids, order, keys, counts. |
| `vact.py` | Every `XXXX Name (N s, rR)` claim in this note against `Action.csv` (179 distinct claims), plus the ids of the `-ii` table and the sync ids. |
| `api/*.json`, `cmp_fixtures.py` | `get_values` output for all 16 tabs, compared cell by cell with `tests/fixtures/sheets/**/*.json`. |
| `vcal.py` | Recomputes the §4.1 offsets, the §4.2 statistics, fits and single-zero intervals. |
| `vres.py` | Own implementation of the §4.5 resolution rule over the fixtures. |
| `probe/` | `Dalamud.NET.Sdk/15.0.0` probe compiled against a copy of the MitCue.Core sources plus Lumina's `Action` sheet and `RsvExtensions.IsRsv`. Negative controls: `HitAtX` fails with CS1061; `typeof(Dalamud.Data.RsvResolver)` fails with CS0122. |
| `yamlcheck/` | YamlDotNet 18.1.0 (MitCue.Core's YAML library): parses and deserializes the three catalogs into MitCue.Core's `MechanicKind`. |

### What was checked and confirmed

- **Timelines and catalogs.**
  - `data/timelines/*.txt` are byte-identical to the clone (`cmp`).
  - All three catalogs pass the checks of §0: 0 errors in 79 + 61 + 63 mechanics. The counts are confirmed: markers
    11/3/1 and `# unverified` aliases 16/21/12. TOP had 321 aliases before the fix below.
  - PyYAML 6.0.3 and YamlDotNet 18.1.0 both parse all three files.
- **§3 (TOP candidate events).**
  - All 207 named timeline entries are listed, with the right ids and sync types.
  - Every "ANCHOR of"/"hit of" statement agrees with `top.yaml`, and every YAML hit is described.
  - All 179 distinct `Action.csv` facts in §3, §6 and §7 (name, `Cast100ms`, `EffectRange`) match.
  - The `-ii` table ids match `Action.csv` and BossMod `TOPEnums.cs`.
  - §5.1 is complete: every non-marker anchor whose ids all have `EffectRange` 0 is listed there, and every listed
    one has range 0.
- **BossMod timings and hints**, read in `TOPStates.cs`, `TEAStates.cs`, `UMADStates.cs` and their components:
  - TOP:
    - the 7B24 raidwide comes 10.2 s after "Flares + Stack" (≈ Pile Pitch 310.0), i.e. ≈ 320.2;
    - the towers come 1 s after Latent Defect, the P3 monitors 0.1 s after the cast;
    - the P4 protean lasers come 0.6 s and 0.7 s after the visuals;
    - `P4BlueScreen` counts 7B7C 1 s after the 8 s cast;
    - the Omega cones are at `CastFinishAt(spell, 1.1f)`;
    - Cosmo Dive resolves 2.5 s after its cast end, Wild Charge 0.4 s after 7BA9;
    - the Sigma cones land 0.2 s after Hyper Pulse;
    - `DieF` = 7B13 and `P3End` = 7B47.
  - TEA:
    - the first Splash has 6 hits and the second 4;
    - Gavel has no hint, and the second Photon carries a Tankbuster hint;
    - the tank-LB raidwide is in BossMod's P4 intermission;
    - `AutoAttackP4` = 4A8B.
  - DMU:
    - `UltimaBlasterRaidwide` = BAE3, so the draft anchors on the raidwides, not the BAE4 charges;
    - the Wings of Destruction buster lands at the C487 cast end;
    - the Earthquake raidwide lands with the C571 cast end;
    - Aero III Assault is the first cast of BossMod's P3 (a knockback component);
    - BossMod uses `UniformStackSpread(6, 0, 4)`, `CastSharedTankbuster`, `RaidwideCast` and `GenericBaitAway` as
      stated;
    - White Hole is labelled "HP check", and Ultima Upsurge is the last P4 cast;
    - Slap Happy is two role stacks or one 8-player stack, so `kind: stack` is right.
- **cactbot triggers.**
  - All 11 TEA trigger outputs quoted in §6 match.
  - 9 of the 10 DMU ones in §7 match (one correction below).
  - The TOP comment "These casts go off 1 second after Latent Defect" is at `the_omega_protocol.ts:1296`.
- **Sheets.**
  - Confirmed the titles, tab lists, grid sizes and gids.
  - The `get_values` output of every tab equals the fixtures in all 1189 non-empty cells (LPDU 513, Carry the Clear
    676). This includes the Scholar and WHM + SCH tabs, which the researcher only spot-checked.
  - Columns A and B are identical across the three Carry the Clear tabs from row 7; only row 6 differs.
  - Every row number, label and time in §4.1 and §4.4 matches the live sheets.
- **Calibration (§4.1–§4.3).**
  - Recomputed exactly: every offset, the per-phase medians, min/max, spreads, A-only statistics, the
    least-squares fits (c, k, max residual), and the floor and round single-zero intervals.
  - The alternative-readings table (residuals and fits) and the §4.3 review bounds (0.7 / 1.3 / 1.0 s) reproduce.
  - The cross-checks against the sheet's phase notes hold.
- **Resolution (§4.4–§4.5).** The independent resolver reproduces:
  - all 98 LPDU rows, including the "Candidates in phase" counts;
  - all 47 Carry the Clear rows in each of the three tabs, under both the order rule and the time rule.
  - Both shared-alias lists of §4.5 match.
- **§8.** Confirmed the `TerritoryType` → `ContentFinderCondition` rows 908/694/1094, `ClassJobLevelSync` 90/80/100,
  `ItemLevelSync` 635/475/0, cactbot `zone_id.ts` and BossMod's TOP `GroupID = 908`.
- **Code names.** These compile in the Dalamud probe: `MechanicKind.Marker`, `MechanicKind.LbCheck`,
  `Mechanic.HitAt(int)`, `FightDefinition.PhaseAt(double)` and `FightLoader.Load(string)` (as a
  `Func<string, FightDefinition>`). `FightLoader.Load` currently throws `NotImplementedException`.

### What was wrong or incomplete, and what was changed

| # | Where | Problem | Change |
|---|---|---|---|
| 1 | `top.yaml`; §0, §2, header table, appendix | Carry the Clear rows 33 and 34 (`Round 2 ★★`, `Round 3 ★★`) were **not** aliases, so "Carry the Clear labels verbatim" was false. They resolved only after normalisation, through `Round 2`/`Round 3`. Cause: the generator's `add_alias` drops a label whose normalised form is already present. An exact-match data test written from §0 would have failed. | Added both labels to `p3.latent_2`/`p3.latent_3`. TOP now has 323 aliases, and the appendix counts are 5. Re-run: 0 errors, every Carry the Clear label is an exact alias, and the resolver results are unchanged. |
| 2 | §4.6 item 3 | "20 s cooldowns … pressed for 945.4 cover only that set" was wrong. These are 20 s effects on 120–180 s recasts (`data/actions/actions.yaml`). Pressed just before 945.4 they last until about 965, so they also cover 961.6 and 963.4. | Rewritten with the durations. The mapping and its UNVERIFIED status are kept. |
| 3 | §3 intro | "The cast end of a range-100 cast is itself the raidwide" was too broad. Range-100 crosses, lines and cones (`CastType` 11/12/13: 7B2D; 7B21, 7B33, 7B70–7B72; 7B79) hit only players inside them. Firewall 7B40 and Discharger 7B2E are range-100 circles without planned damage. | Qualified. |
| 4 | §4.6 item 5 | Listed `p1.wave_cannon_kyrios` as an umbrella, but it has no parts (§2's list correctly omits it). | Reworded. |
| 5 | §5.4 (omission) | In the v1 engine P2 is entered by the forward sync to 203.4. That skips `p2.start` (200.0) and `p2.targetable` (202.3), and `ReminderScheduler.OnTimelineJump` marks reminders in the span as Skipped. | Added a paragraph. Carry the Clear's earliest P2 action (0:07) is unaffected. |
| 6 | §7 intro | "Phases are cactbot's five sections … the section's first sync point or jump label" did not hold for P3. Its label `p2-success` (597.0) is in cactbot's Phase 2 section; the `### Phase 3` header is at 633.2. | Reworded, with BossMod's P3 (which starts with Aero III Assault) as the justification. |
| 7 | §7 | `DMU P4 Ultima Upsurge` does not use `Responses.bigAoe()`. It builds an `infoText` that starts with `output.bigAoe`. | Corrected. |
| 8 | §7 | "Enrages reachable only through jump labels" was wrong. The main timeline has `?` preview entries at 202.4, 485.0, 946.8 and 1218.1 (unsynced), and two synced ones at 951.8. | Corrected. They are still not in the draft. |
| 9 | §7 (precision) | The `_rsv_` explanation lacked the mechanism. | Added: Dalamud hooks `AddRsvString` and sets Lumina's `LuminaOptions.RsvResolver` (the resolver class is internal, CS0122), and `RsvExtensions.IsRsv` is public. Run-time resolution is **UNVERIFIED**; the recommendation is unchanged. |
| 10 | §2, §9 (new finding) | With YamlDotNet 18.1.0, `kind: lb_check` fails to deserialize into `MechanicKind` unless the builder uses `WithEnumNamingConvention(UnderscoredNamingConvention.Instance)`. `marker` maps by default. | Added to §2 and §9 item 2. |

**Checked and left as they are (judgment calls):**
- LPDU tank `Limitless` → `p2.limitless` (§4.6 item 1). It fits the party row. For the `General Tank Mit til P5` row,
  "Rampart + 30% + Short CD" on both tanks fits the two Bladedance tankbusters (295.6) at least as well. The users
  are healers who plan from the party tabs, and the item stays **UNVERIFIED** (§9 item 1).
- `Panto Stack 1–4` and `HW Round 1–4` contain shorthand that is marked `# unverified` elsewhere (`Panto`, `HW`), but
  they are not marked themselves. This is cosmetic, because YAML comments do not reach the loader.
- The TEA and DMU UNVERIFIED items are correctly flagged. The evidence quoted for them (BossMod enums, states and
  components; cactbot comments; `Action.csv`) is accurate.
- The DMU draft has no `lb_check`. cactbot's config option "P3 Bowels of Agony Strategy" (`dancing_mad.ts`, id
  `boa`) offers a "Tank LB3" strategy, so some groups may plan a tank LB3 around `p3.bowels_of_agony` (662.9). This
  depends on the strategy, and the draft does not need to change before a DMU plan exists.
