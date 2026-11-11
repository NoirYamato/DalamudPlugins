# cactbot timelines: file format, playback semantics, and the TOP / TEA / DMU timelines

Reference for implementing a cactbot-compatible timeline parser and playback engine in C# (MitCue), plus an
analysis of the three ultimate timelines MitCue targets first.

Verification tags used below:

| Tag | Meaning |
|---|---|
| **[code]** | Read in cactbot (or OverlayPlugin / Dalamud / BossMod) source at the commits listed in section 0. |
| **[run]** | Observed by executing cactbot's own `TimelineParser` / `Timeline` classes under Node (section 8). |
| **[probe]** | Compiled against the installed Dalamud + its FFXIVClientStructs (`Build succeeded`). The project brief calls this install 15.0.3.9; `Dalamud.dll` itself reports FileVersion 15.0.4.0 (`15.0.4.0+12e318c`). |
| **[data]** | Looked up in the game-data CSVs (current patch, English). |
| **[doc]** | Stated by `docs/TimelineGuide.md` or `docs/LogGuide.md`, not independently re-derived. |
| **UNVERIFIED** | Could not be proven with the material available here. |
| **[verify …]** | Added or corrected by the independent verification pass (see "Verification" at the end): `run` = cactbot's parser/engine under Node, `probe` = exact-type Dalamud compile probe, `DLL metadata` = field offsets read from `FFXIVClientStructs.dll`. |

---

## 0. Sources

| Source | Version | Used for |
|---|---|---|
| cactbot (`OverlayPlugin/cactbot`), sparse clone | `e79ea7f`, 2026-10-08 | `docs/TimelineGuide.md` (read fully), `docs/LogGuide.md` (all sections for the line types below), `ui/raidboss/timeline_parser.ts` and `ui/raidboss/timeline.ts` (read fully), `resources/netregexes.ts`, `resources/regexes.ts` (`parse`, `anyOf`, `maybeCapture`), `resources/netlog_defs.ts`, `ui/raidboss/popup-text.ts` (combat/wipe handling), `test/helper/test_timeline.ts` (lint rules), `util/logtools/test_timeline.ts` and `make_timeline.ts`, trigger files `the_omega_protocol.ts`, `the_epic_of_alexander.ts`, `dancing_mad.ts` |
| Timelines | same commit | `06-ew/ultimate/the_omega_protocol.txt` (TOP), `05-shb/ultimate/the_epic_of_alexander.txt` (TEA), `07-dt/ultimate/dancing_mad.txt` (DMU), plus `06-ew/ultimate/dragonsongs_reprise_ultimate.txt` (DSR) and `07-dt/ultimate/futures_rewritten.txt` (FRU) |
| OverlayPlugin (`OverlayPlugin/OverlayPlugin`), sparse clone | `43afdd5`, 2026-11-10 | exact formatting of lines 257 MapEffect, 272 SpawnNpcExtra, 273 ActorControlExtra, 260 InCombat |
| BossMod (BSD-3), sparse clone | `6e410ef` | `ActorControlCategory` values, DirectorUpdate / TargetIcon / Tether handling |
| Dalamud source clone + installed assemblies | clone `12e318c` (`DalamudVersion` 15.0.4.0); the installed `Dalamud.dll` is FileVersion `15.0.4.0+12e318c`, the same commit (the brief labels it 15.0.3.9) | `DutyState.cs` (DirectorUpdate argument order); probe build |
| Game data CSVs | current patch | `Action.csv` (cast times), `Status.csv`, `TerritoryType.csv`; `BNpcName.csv` downloaded from xivapi/ffxiv-datamining |

cactbot is Apache-2.0 ([code] `LICENSE`, `package.json`), so the `.txt` timelines can be bundled with attribution.
Zone ids [code `resources/zone_id.ts`] [data `TerritoryType.csv`]: TEA 887, DSR 968, TOP 1122, FRU 1238, DMU 1363.

---

## 1. Key facts for implementers

1. **A timeline is a line-oriented list of timed entries.** An entry has a time, a quoted display name and
   optionally one sync (a log-line pattern) plus `window`, `jump`/`forcejump` and `duration` keywords. Other
   statements are `hideall`, `label`, comments, and a few legacy alert commands. Section 2 has the exact grammar,
   verified against cactbot's parser on an 80-line edge-case file.
2. **Playback is a two-state machine.** The timeline is either stopped at time 0 or running in real time.
   - A sync is "active" while `start <= now < end`. The defaults are `start = t - 2.5` and `end = t + 2.5`.
   - When a game event arrives, the active syncs are scanned in order of window end (ascending), with ties in file
     order, and the **first** one that matches wins. Only one sync can fire per event.
   - The winning sync sets the timeline time to its own time, or to its `jump` target. A target of 0 stops the
     timeline.
   - A `forcejump` jumps unconditionally when its time is reached. It also keeps an "overhang" window open at the
     jump destination.
   - While stopped, every sync whose window contains 0 can start the timeline. This includes the `InCombat` sync
     and all the wide `window N,0` phase syncs.
   - Section 3 has the details, verified by running cactbot's engine.
3. **cactbot's engine has two bugs MitCue should not copy** (section 3.10, both [run]):
   - the force-jump "overshoot" correction is a unit error (seconds are subtracted from milliseconds), and
   - a force jump is not re-armed after a sync moves time past it and a later jump moves time back before it.
4. **Event sources the three fights need** (sections 4.3 and 5):
   - **TOP** needs `Ability` (ActionEffect), `StartsUsing` (cast start) and `InCombat`. Its single `MapEffect`
     sync has a `StartsUsing 7B40` fallback.
   - **TEA** needs the same three. `InCombat` is the only way its P1 can start, and `StartsUsing` is the only wide
     sync for its Transition 2.
   - **DMU** additionally needs **`ActorControl` command `80000027`** (a DirectorUpdate). It is the only wide sync
     for the starts of P2 and P4. Section 6 proposes fallback syncs if MitCue does not implement it.
5. **Match structured events, not log lines.** Each sync parameter is a case-insensitive regex that must match one
   whole field. Section 2.4.3 gives the exact field formats, such as hex padding.
6. **Source names are English display names.** The bundled syncs only work on an English client. For other
   languages, map the actor's `NameId` to the English `BNpcName` row; all the names used resolve (section 4.4).

---

## 2. Grammar

### 2.1 Line processing pipeline [code `timeline_parser.ts` `parse()`; [run] for every case in 2.10]

1. Split the text on `\n`. Line numbers are 1-based. Each line is `trim()`med, which removes spaces, tabs and `\r`,
   so CRLF files work.
2. Skip the line if it is empty or matches `^\s*#`.
3. Otherwise the **first** of these regexes that matches the trimmed line decides what the line is:
   1. `hideall`
   2. `alertall` (legacy)
   3. `define soundalert` (legacy)
   4. `define speaker` (legacy)
   5. `infotext` / `alerttext` / `alarmtext` (legacy)
   6. `label`
   7. entry line
4. If none match, the parser records the error `Invalid format` and skips the line.

The parser's regexes, verbatim (JavaScript syntax; cactbot does **not** apply the `i` flag to these):

```js
comment:          /^\s*#/
commentLine:      /#.*$/
ignore:           /^hideall\s+\"(?<id>[^"]+)\"(?:\s*#.*)?$/
tts:              /^alertall\s+"(?<id>[^"]*)"\s+before\s+(?<beforeSeconds>-?[0-9]+(?:\.[0-9]+)?)\s+(?<command>sound|speak\s+"[^"]*")\s+"(?<text>[^"]*)"$/
soundAlert:       /^define\s+soundalert\s+"[^"]*"\s+"[^"]*"$/
speaker:          /define speaker "[^"]*"(\s+"[^"]*")?\s+(-?[0-9]+(?:\.[0-9]+)?)\s+(-?[0-9]+(?:\.[0-9]+)?)/   // note: not anchored
popupText:        /^(?<type>info|alert|alarm)text\s+\"(?<id>[^"]+)\"\s+before\s+(?<beforeSeconds>-?[0-9]+(?:\.[0-9]+)?)(?:\s+\"(?<text>[^"]+)\")?$/
label:            /^(?<time>[0-9]+(?:\.[0-9]+)?)\s+(?<text>label\s+"(?<label>[^"]*)")\s*$/
line:             /^(?<text>(?<time>[0-9]+(?:\.[0-9]+)?)\s+"(?<name>.*?)")(\s+(.*))?/
durationCommand:  /(?:[^#]*?\s)?(?<text>duration\s+(?<seconds>[0-9]+(?:\.[0-9]+)?))(\s.*)?$/
syncRegexCommand: /(?:[^#]*?\s)?(?<text>sync\s*\/(?<regex>.*)\/)(?<args>\s.*)?$/
syncNetRegex:     new RegExp(`(?:[^#]*?\\s)?(?<netRegexType>${Object.keys(logDefinitions).join('|')})\\s*(?<netRegex>\\{.*\\})(?<args>\\s.*)?$`)
windowCommand:    /(?:[^#]*?\s)?(?<text>window\s+(?:(?<start>[0-9]+(?:\.[0-9]+)?),)?(?<end>[0-9]+(?:\.[0-9]+)?))(?:\s.*)?$/
jumpCommand:      /(?:[^#]*?\s)?(?<text>(?<command>(?:force|)jump)\s+(?:"(?<label>[^"]*)"|(?<seconds>[0-9]+(?:\.[0-9]+)?)))(?:\s.*)?$/
```

### 2.2 Statement kinds

```ebnf
number     = digit+ [ "." digit+ ] ;            (* no sign, no exponent, no leading "."; any number of decimals *)
ws         = (" " | TAB)+ ;
comment    = "#" { any } ;                       (* whole line, or the rest of an entry line *)

hideall    = "hideall" ws '"' nonEmptyNoQuote '"' [ ws? comment ] ;
label      = number ws "label" ws '"' noQuote '"' [ ws ] ;          (* NOTHING may follow, not even a comment *)
entry      = number ws '"' noQuote '"' [ [ ws ] rest ] ;             (* name may be empty, may contain '#'; no space is needed
                                                                    after the closing quote: `2.1 "N"Ability {...}` parses [verify run] *)
legacy     = alertall | infotext | alerttext | alarmtext | "define soundalert ..." | "define speaker ..." ;
```

`rest` is not a fixed grammar. Section 2.3 describes how it is taken apart.

**Entry name.** The name is everything between the first two `"` after the time.
- It may be empty: `2.1 ""` is accepted [run].
- It may contain `#`: `"Name with # hash"` is accepted, because the name is extracted before comments are stripped
  [run].
- It cannot contain `"`, and there is no escape for it.
- The raw name is what `hideall`, timeline triggers and translations key on. cactbot only translates the displayed
  text.

### 2.3 How an entry's `rest` is processed [code; [run]]

The steps run in this order on the text after `time "name"`:

1. **Strip the comment.** Everything from the first `#` to the end of the line is removed. As a result:
   - `#Ability { ... }`, the usual way to disable a sync, leaves a display-only entry.
   - A `#` inside a sync value cuts off the closing `}`, so the entry is rejected with "Extra text" [run,
     `id: "1#2"`].
2. **Extract `duration`.** One `duration N` followed by whitespace or end of line is removed and stored. `duration`
   may therefore appear **before or after** the sync [run, edge-case entries at times 2.6 and 2.7].
   - Exact cactbot behaviour (the regex is unanchored, with an optional lazy prefix tried first): it takes the first
     `duration N` that is preceded by whitespace; failing that, a `duration N` at the start or even glued to the
     preceding text (`Ability { id: "1" }duration 3` keeps both the sync and the duration) [verify run].
   - With two `duration`s on one line, only one is removed and the other is left over, so the entry is rejected with
     "Extra text" [verify run]. None of the five files do either.
3. **Old-style sync `sync /regex/`.** The regex runs from the first `/` after `sync` to the **last** `/` on the line
   (greedy). The text after it is `args`.
4. **Network sync `Type { ... }`.** The object runs from the first `{` to the **last** `}` on the line (greedy). The
   text after it is `args`.
   - `Type` must be followed (optionally after whitespace) by `{`.
   - `Type` is case-sensitive: `ability { }` is rejected as "Extra text" [run].
5. **Extract `window` and `jump`/`forcejump` from `args` only.** Two consequences:
   - These keywords are **not recognised before the sync**: `window 1,1 Ability {...}` gives "Extra text" [run].
   - Their order among themselves does not matter. `jump 50 window 20,20` parses [run].
6. **Check for leftovers.** Any remaining text that is not a comment gives the error `Extra text`, and the entry is
   dropped.
   - Quirk: the entry's sync has already been registered at this point, so it stays active without an event
     [run "ORPHAN-SYNC"].
   - MitCue should drop the sync too.

### 2.4 Network-regex syncs: `Type { field: "value", ... }`

#### 2.4.1 Types and parameters

- **`Type`** is any key of `netlog_defs.ts`, case-sensitive [code, dumped with Node]:
  `GameLog ChangeZone ChangedPlayer AddedCombatant RemovedCombatant PartyList PlayerStats StartsUsing Ability
  NetworkAOEAbility NetworkCancelAbility NetworkDoT WasDefeated GainsEffect HeadMarker NetworkRaidMarker
  NetworkTargetMarker LosesEffect NetworkGauge NetworkWorld ActorControl NameToggle Tether LimitBreak
  NetworkEffectResult StatusEffect NetworkUpdateHP Map SystemLogMessage StatusList3 StatusEffectListForay3 ParserInfo
  ProcessInfo Debug PacketDump Version Error None LineRegistration MapEffect FateDirector CEDirector InCombat
  CombatantMemory RSVData StartsUsingExtra AbilityExtra ContentFinderSettings NpcYell BattleTalk2 Countdown
  CountdownCancel ActorMove ActorSetPos SpawnNpcExtra ActorControlExtra ActorControlSelfExtra`.
  - Unknown types give "Extra text" [run `Abilityy`].
  - `Ability` covers both line 21 and line 22. `NetworkAOEAbility` is line 22 only.
- **The parameter block** is parsed with **JSON5** [code `JSON5.parse`]. All of these are accepted [run]:
  - bare keys (`id:`), double-quoted keys (`"id":`) and single-quoted keys (`'source':`);
  - double- or single-quoted string values (DMU uses `HeadMarker { id: '0103' }`);
  - trailing commas;
  - no spaces at all (`Ability{id:"1"}`).
- **Values** must be a string or an array of strings.
  - A number (`{ id: 1 }`), an unknown key, or the keys `capture` and `timestamp` give the error
    `Invalid NetRegex arguments`. The **entry is still kept, without a sync** [run].
  - Malformed JSON5 inside a complete `{...}` (for example `{ id: "1", }}`) gives `Invalid NetRegex JSON`, with the
    entry likewise kept without a sync [run].
  - An unclosed `{` does not count as a sync at all, so the entry is rejected with "Extra text" [run].
  - The key `type` is allowed and replaces the line-type pattern: `Ability { type: "22", id: "1" }` produces
    `^22\|...` [run].
  - The only object-array value is the repeating field `pair` of `CombatantMemory`. None of the five timelines use it.
- **JSON5 escaping.** `"E\\.D"` becomes the regex `E\.D`, a literal dot. `"E\.D"` becomes `E.D`, because JSON5 drops
  the backslash before an unknown escape [run]. TimelineGuide says the same thing: two backslashes are needed.

#### 2.4.2 How the regex is built [code `netregexes.ts` `parseHelper`, `regexes.ts`; [run]]

cactbot builds one regex against the pipe-separated **network log line**:

1. The regex starts with `^`, then the line-type field. That field is `2[12]` for `Ability` and the decimal type id
   for every other type (`20`, `33`, `257`, ...).
2. Fields are emitted **in field-index order**, each followed by `\|`. For each field:
   - a parameter that names the field puts its **value inserted verbatim** in that position;
   - an array value becomes `(?:a|b|c)`, and a single-element array becomes `a` with no group;
   - a field not mentioned becomes `[^|]*`.
3. Index gaps in the definition become `(?:[^|]*\|)` or `(?:[^|]*\|){n}`.
4. Emission **stops after the highest-index field that has a parameter**, so there is no `$` anchor.
5. The regex is compiled case-insensitive (`i`) [code `Regexes.parse`: "All regexes in cactbot are case
   insensitive"]. `\y{...}` macros are expanded at the same time.

Examples generated by cactbot for lines of these files [run]:

```text
TOP L14  StartsUsing { id: "7B03", source: "Omega" }              /^20\|[^|]*\|[^|]*\|Omega\|7B03\|/i
TOP L58  Ability { id: ["7B2A", "7B2D"], source: "Omega-F" }      /^2[12]\|[^|]*\|[^|]*\|Omega-F\|(?:7B2A|7B2D)\|/i
TOP L95  Ability { id: "7B4E", source: ["Left Arm Unit", "Right Arm Unit"] }
                                                                  /^2[12]\|[^|]*\|[^|]*\|(?:Left Arm Unit|Right Arm Unit)\|7B4E\|/i
TOP L230 Ability { id: "7B0[45]", source: "Omega" }               /^2[12]\|[^|]*\|[^|]*\|Omega\|7B0[45]\|/i
TOP L12  InCombat { inGameCombat: "1" }                           /^260\|[^|]*\|[^|]*\|1\|/i
TOP L48  MapEffect { flags: "00020001", location: "0A" }          /^257\|[^|]*\|[^|]*\|00020001\|0A\|/i
TOP L9   ActorControl { command: "4000000F" }                     /^33\|[^|]*\|[^|]*\|4000000F\|/i
TEA L49  RemovedCombatant { name: "Liquid Hand" }                 /^04\|[^|]*\|[^|]*\|Liquid Hand\|/i
DMU L18  ActorControl { command: "80000027", data0: "00" }        /^33\|[^|]*\|[^|]*\|80000027\|00\|/i
DMU L106 HeadMarker { id: '0103' }                                /^27\|[^|]*\|[^|]*\|[^|]*\|(?:[^|]*\|){2}0103\|/i
DMU L161 GainsEffect { effectId: '306' }                          /^26\|[^|]*\|306\|/i
DMU L174 LosesEffect { effectId: ['1061', '1063'] }               /^30\|[^|]*\|(?:1061|1063)\|/i
DMU L240 SpawnNpcExtra { tetherId: "0054" }                       /^272\|[^|]*\|[^|]*\|[^|]*\|0054\|/i
DMU L371 Ability { id: ["BB1C", "BB1D"] }                         /^2[12]\|[^|]*\|[^|]*\|[^|]*\|(?:BB1C|BB1D)\|/i
DMU L448 ActorControlExtra { category: "019D", param1: "10", param2: "20" }
                                                                  /^273\|[^|]*\|[^|]*\|019D\|10\|20\|/i
```

**What this means for a structured matcher.**
- Each value sits between `\|` delimiters, so it must match the **whole field**. The C# equivalent is
  `Regex("^(?:" + value + ")$", IgnoreCase | CultureInvariant)`, with arrays joined by `|`.
- Difference from cactbot: cactbot does not wrap single values in a group, so a value with a top-level `|` such as
  `id: "1|2"` produces a broken alternation (`...\|1|2\|`) [run]. None of the five timelines contain a `|` in a
  value [run], so wrapping in `(?:...)` is safe and more sensible.
- In the five files, the only regex metacharacters in values are character classes in ids [run]:
  - TOP: `7B0[45]` (1);
  - TEA: 16, e.g. `482[BC]`, `487[EF]`, `488[AB]`, `49A[ED]`;
  - DSR: 8, e.g. `63C[01]`.

  The .NET and JS regex syntaxes agree for these.

**Translations.** cactbot passes the parameters through the trigger file's `timelineReplace[].replaceSync`, for the
keys `ability`, `name`, `source`, `target` and `line` only [code `keysThatRequireTranslation`]. Nothing is replaced
for English. For other locales the replacements are themselves regexes; for example TOP German has
`'Left Arm Unit': 'link(?:e|er|es|en) Arm'` [code].

#### 2.4.3 Field formats

A matcher that builds its own field strings must use these formats. The last column says where each format is
established. Values that are pure hex literals can instead be compared numerically, which avoids padding problems.

| Type (line) | Field | Format | Evidence |
|---|---|---|---|
| `Ability` (21/22), `StartsUsing` (20), `NetworkCancelAbility` (23) | `id` | uppercase hex action id, no `0x`, at least 2 digits | [doc] LogGuide examples `F67`, `07` (Attack), `03` (Sprint), `BA` (Succor), `13D0`. The ACT plugin is closed source, so the padding rule is UNVERIFIED. All boss ids here have 3–4 digits. |
| same | `source` | caster display name in the client language, possibly empty | [doc] |
| `GainsEffect` (26), `LosesEffect` (30) | `effectId` | hex status id | [doc] examples `35`, `13B`, `D2`, `8D1`. [data] `306` = Status 774 "Down for the Count", `1061`/`1063` = 4193/4195 "Epic Villain"/"Fated Villain", `1043` = 4163 "Stun" |
| `HeadMarker` (27) | `id` | 4 hex digits, zero-padded | [doc] `0057`, `0117` |
| `Tether` (35) | `id` | 4 hex digits | [doc] `0001`, `006E` |
| `ActorControl` (33) | `instance`, `command` | 8 hex digits | [doc] `80034E6C`, `4000000F` |
| same | `data0`..`data3` | hex, at least 2 digits | [doc] `B5D`, `16`, `00`, `FFFFFFFF`; DMU uses `00`, `0C`, `2E`. UNVERIFIED rule. |
| `MapEffect` (257) | `instance` / `flags` / `location` | `{instance:X8}` / `{flag2:X4}{flag1:X4}` / `{index:X2}` | [code] OverlayPlugin `LineMapEffect.cs` line 478 |
| `InCombat` (260) | `inGameCombat` | `0` or `1` | [doc] |
| `SpawnNpcExtra` (272) | `id` / `parentId` / `tetherId` / `animationState` | `X8` / `X8` / `X4` / `X2` | [code] OverlayPlugin `LineSpawnNpcExtra.cs` |
| `ActorControlExtra` (273) | `id` / `category` / `param1`..`param4` | `X8` / `X4` / `X`, **no padding** | [code] OverlayPlugin `LineActorControlExtra.cs` |
| `RemovedCombatant` (04), `WasDefeated` (25) | `name` / `target` | display name | [doc] |

### 2.5 Old-style sync: `sync /regex/`

- The regex text between the slashes is used as-is: spaces are kept, and `\y{...}` macros (`Timestamp`,
  `NetTimestamp`, `NetField`, `LogType`, `AbilityCode`, `ObjectId`, `Name`, `Float`) are expanded. It is compiled
  case-insensitive.
- `window` and `jump` follow the closing slash.

  Example [run]: `3.9 "Old sync" sync / 15:........:Foo:1234:/ window 5,5 jump 10` gives `/ 15:........:Foo:1234:/i`,
  window `[-1.1, 8.9)`, jump to 10.
- These are `'parsed'` syncs. cactbot tests them only against **ACT parsed log lines** (`onLogEvent`, colon-separated,
  hex type), never against network lines [code `OnLogLine` versus `OnNetLogLine`].
- cactbot's lint forbids them in shipped files [code test "should use netregex parameter style"]. **None of the five
  timelines use them.** MitCue cannot reproduce ACT parsed lines, so it should parse these, keep the entry as
  display-only, and warn.

### 2.6 Keywords

| Keyword | Syntax | Semantics / parse details |
|---|---|---|
| `window` | `window B,A` or `window W` | The sync is active for `[t − B, t + A)`. A single value is split evenly: `window 10` is ±5 [run]. **Default without `window`: `2.5,2.5`** [code `start: seconds - 2.5, end: seconds + 2.5`]. There must be no space around the comma: `window 10, 10` gives "Extra text" [run]. Decimals are accepted (`window 1.5,0.5` [run]; TEA has `window 10,2.5`). `window 0,0` produces an empty, never-active window [run]. Only valid after a sync. |
| `jump` | `jump N` or `jump "label"` | When the sync fires, timeline time becomes the target (section 3.4). The target needs no entry at that time [doc]. A target of 0 stops playback (section 3.4). |
| `forcejump` | `forcejump N` or `forcejump "label"` | Behaves like `jump` if the sync fires, **and** jumps unconditionally when time reaches the entry (section 3.5). It requires a sync on the same line: `33 "x" forcejump 2` gives "Extra text" [run]. |
| `jump` together with `forcejump` | — | Mutually exclusive. Both on one line gives "Extra text" [run]. |
| `duration` | `duration N` | Display only: once the entry's time passes, cactbot shows "Active: name" until `t + N`. It needs no sync and may appear before or after the sync. It is ignored for syncing. |
| `label` | `T label "name"` | Its own statement, not an entry and never displayed. It maps a name to time `T`.<br>• Duplicate names are an error, and the **last** definition wins [run].<br>• Jumps resolve labels after the whole file is parsed, so forward references are fine [code].<br>• An unresolved label is an error; the sync then behaves as a plain sync [run]. A `forcejump` to a missing label throws at runtime in cactbot [code `UnreachableCode`], so MitCue should reject it at parse time.<br>• A label at time 0 gives `jump = 0`, which stops playback [run]. |
| `hideall` | `hideall "Name"` | Hides every entry whose **raw name is exactly** `Name` (case-sensitive) from display only. Those entries still sync, and timeline triggers still match them [doc; code: `ignores` is only read when adding display bars]. All five files have `hideall "--Reset--"` and `hideall "--sync--"`. |

All keywords are **lowercase only**: `JUMP 50` gives "Extra text" and `HIDEALL "X"` gives "Invalid format" [run].
Numbers use `[0-9]+(\.[0-9]+)?`: `.5`, `-1` and `1e3` are invalid; `1.25` is accepted by the parser (only the lint
forbids it) [run].

### 2.7 Legacy alert commands

`infotext|alerttext|alarmtext "Name" before S ["text"]`, `alertall "Name" before S sound|speak "voice" "text"`,
`define soundalert "a" "b"` and `define speaker "a" ["b"] N N` are parsed and either turned into popup texts or
ignored [code].
- None of the five files use them.
- cactbot recommends timeline triggers instead [doc].
- MitCue should accept them and ignore them, so that user files with them still load.

### 2.8 Comments and header hints

- **`#` comments.** A `#` starts a comment anywhere except inside the entry name. Inline comments after a sync are
  common: `203.4 "--sync--" StartsUsing { id: "7B40", source: "Omega" } window 210,5 # extra sync due to map effect sync`.
- **Disabled syncs.** `NN.N "Name" #Ability { ... }` keeps the entry for display without syncing. This is used for
  multi-hit follow-ups and "?" previews.
- **Header hints.** These are `make_timeline.ts` arguments recorded as comments, and the parser ignores them
  [code `make_timeline.ts`]:
  - `-p ID:time` gives the phase-anchor abilities and their timeline times;
  - `-ii` lists ignored ability ids;
  - `-ia` lists ignored ability names;
  - `-ic` / `-oc` ignore combatants or keep only the listed ones;
  - `-it` adds targetable lines for the named mobs;
  - `-la` appends an ability table.

  The hints in these files:

  ```text
  TOP: # -p 7B03:15 7B40:206.4 7B13:400 7B47:600 7B7C:700 7F72:1100
       # -it "Omega" "Omega-M" "Omega-F"
  TEA: # -p 49B0:11.4 4830:200 -ii 4978 4979 497B 497A 481E 4B4E 4820
  DMU: # ZoneId: 1363
       # -p C403:15.6 C24C:320.1 C3F7:600.0 C2DC:1100.0 BB40:1400.0
       # -it Kefka Chaos Exdeath
  ```

  The `-p` lists are useful metadata: the authors' own phase anchors.
- **Lint switches.** `#cactbot-timeline-lint-disable-sync-order` and `#cactbot-timeline-lint-enable-sync-order` are
  read only by the linter.
- **DMU dialogue notes.** DMU annotates its dialogue syncs with the line they correspond to, for example
  `# en: 'Yes... I am filled with glorious purpose!'`.

### 2.9 Lint-only rules for shipped files [code `test/helper/test_timeline.ts`]

The parser accepts everything below; only cactbot's test suite enforces it. MitCue should treat violations as
warnings at most.
- Times must be integers or have exactly one decimal.
- Entries must be in time order, except inside the lint-disable block.
- No leading or trailing whitespace, and no double spaces outside quotes.
- Keyword order must be `duration`, `window`, `jump`, `forcejump`.
- `window` must be written `#[.#],#[.#]`.
- No needless `.0` on keyword parameters.
- Every sync must use the network-regex style.

### 2.10 Verified edge cases [run]

The file `cases/grammar.txt` was fed to cactbot's `TimelineParser`. Selected results:

| Input (after the time) | cactbot result |
|---|---|
| `""` | entry with an empty name |
| `"N" Ability { id: "1" } # c` | sync; the comment is ignored |
| `"N" #Ability { id: "1" }` | display-only entry |
| `"Dur" duration 3 Ability { id: "3" }` | duration 3 plus a sync |
| `"N" Ability { id: "5" } window 10` | window `[t−5, t+5)` |
| `"N" Ability { id: "C" } jump 50 window 20,20` | jump 50, window ±20 |
| `"N" Ability { id: "E" } jump "nope"` | error "No label named nope"; the entry is kept as a plain sync |
| `label "x" # comment` | `Invalid format` |
| `"N" Ability { id: "1", source: "window 5,5" }` | correct; text inside the braces is not taken as a keyword |
| `"N" Ability { id: "1" } StartsUsing { id: "2" }` | "Extra text"; an orphan sync for the **second** type stays registered (the optional prefix group makes the regex prefer a later type) |
| tabs between tokens, multiple spaces, a trailing `\r` | accepted |

### 2.11 Summary: numbers, quoting, escaping, case

| Aspect | Rule |
|---|---|
| Times and keyword numbers | `[0-9]+(\.[0-9]+)?`, parsed as a double (`parseFloat`). No sign, exponent, leading `.` or thousands separator. In C#: validate with the regex, then `double.Parse(s, CultureInfo.InvariantCulture)`. |
| Entry names, labels, `hideall` | Double quotes only, with no escapes and no `"` inside. Names and labels may be empty; `hideall` needs a non-empty name. |
| Sync parameters | JSON5: single or double quotes, bare or quoted keys, trailing commas. Standard JSON5 string escapes apply:<br>• `\\` gives `\`;<br>• `\b \f \n \r \t \v \0 \xHH \uHHHH \' \"` are real escapes, so a regex `\b` must be written `\\b`;<br>• a backslash before any other character is dropped (`\.` gives `.`, `\d` gives `d`). |
| Old-style sync | `/.../`, greedy to the last `/`, with no escaping of `/` needed or possible. |
| Case | Keywords and type names are case-sensitive and lowercase (types are PascalCase). `hideall` and `label` names match exactly. **Sync values match case-insensitively.** |
| Equal times | Entries with equal times keep file order [doc; code: sorted by time, then by file order]. |

### 2.12 Naming conventions for display names [doc TimelineGuide "Common Timeline Edits"]

These are conventions, not grammar, but plan-to-timeline matching will depend on them:

| Pattern | Meaning |
|---|---|
| `--sync--`, `--Reset--` | Invisible helper entries (hidden by `hideall`). |
| `--targetable--`, `--untargetable--`, `--middle--`, `--knockback--`, `--center--` | Positional or state markers, usually without a damage ability. |
| `A/B` | One of the alternatives will happen, e.g. `Superliminal Steel/Optimized Blizzard III`. Simultaneous abilities get separate lines at the same time. |
| `Name?` | Might not happen. Also used for previews after conditional jumps: `Ultimate Embrace?`. |
| `Name xN` plus `duration` | A multi-hit ability; the follow-up hits are usually commented-out lines, e.g. `Sable Thread x6`, `Splash x6`. |
| `Name (cast)` / `(castbar)` | A cast whose damage lands later under another entry. |
| `Name N` | The Nth occurrence, for mitigation planning, e.g. `Latent Defect 3`, `Ultima 6`. |
| `... Enrage` | Hard or soft enrage. |

---

## 3. Playback semantics [code `timeline.ts`; [run] where noted]

### 3.1 State and clock

- **State.** `timebase` holds the wall-clock milliseconds that correspond to timeline time 0. `timebase == 0` means
  **stopped**. The constructor calls `Stop()`, so a new timeline starts stopped at time 0.
- **Current time.** While running, `fightNow = (now − timebase) / 1000` seconds, and time advances in real time. While
  stopped, `fightNow` is 0.
- **Rounding artifact.** cactbot's update tick rounds `fightNow` **up** to 1/32 s:
  `Math.ceil(32 * elapsedMs / 1000) / 32`. Windows can therefore open or close up to about 31 ms early [run: a line
  at 7.499 s matched a window starting at 7.5]. MitCue does not need to copy this.
- **Time of an event.** The time used for a sync is the moment the log line is **processed** (`Date.now()`), not the
  line's own timestamp. For MitCue that is the moment the hook or poll observes the event.

### 3.2 Which syncs are active

Each sync has `start = t − before` and `end = t + after`. **A sync is active iff `start <= fightNow < end`**, so the
start is inclusive and the end exclusive [code `_AdvanceTimeTo` drops syncs whose `end <= fightNow`;
`_CollectActiveSyncs` requires `start <= fightNow`].

The active list is rebuilt:
- in `Stop()`, in `SyncTo()`, and on every update tick;
- cactbot schedules ticks at every sync start and end boundary.

It is **ordered by `end` ascending, with ties in file order** (stable sort; [run] list order `L6[49.5,54.5)` before
`L5[-50,150)`). A force-jump "overhang" sync (section 3.5), if present, is appended **last**.

### 3.3 Matching an event: several candidates

For each incoming line, cactbot walks the active list in that order and **stops at the first sync whose regex
matches**. At most one sync fires per line [code `break`].
- [run] example: `50.0 "Wide" ... window 100,100` and `52.0 "Narrow"` with the same id, line at 51 s → `Narrow`
  wins (end 54.5 < 150), so time becomes 52.0. With only `Wide` active, at 40 s, `Wide` wins and time becomes 50.0.
- Practical effect: the sync whose window closes soonest wins. That is normally the nearest upcoming entry, while
  wide "phase catch-all" windows lose to local ones.
- When repeated hits of one id are 3–5 s apart, the default ±2.5 s windows overlap. The **earlier** entry then wins
  in the overlap (for example TEA `#1 Alpha Sword` at 207.4 and `#3` at 212.1). Section 5 lists these overlaps for
  each fight.
- cactbot sees one 21/22 line **per target**. The repeated lines from one action resync to the same time, and the
  2 ms dedupe in 3.4 absorbs them. MitCue gets one `ActionEffect` call per action, so it has no duplicates to absorb.

### 3.4 What a match does (`OnLogLineJump` → `SyncTo`)

```text
if sync has a jump (number, or label resolved to a number):
    if jump == 0:  SyncTo(0) then Stop()            -> stopped at 0
    else:          SyncTo(jump, now)                -> timeline time := jump, exactly, at this instant
else:              SyncTo(sync.time, now)           -> timeline time := the entry's own time
```

- **A jump lands exactly on its target.** It does not add the event's offset within the window: a line at 60.4 for a
  `60.0 ... jump 200` entry sets time to exactly 200.0 [run]. Entries after a jump target are therefore authored
  relative to "the sync entry happens at the target time".
- **`SyncTo(t, now)` in detail:**
  1. It clears the force-jump overhang.
  2. It computes `newTimebase = now − t·1000`.
  3. **If `|newTimebase − timebase| <= 2 ms`, it returns** without doing anything else. This is the dedupe for
     multi-target lines; it also means a no-op sync still clears the overhang.
  4. Otherwise it sets `timebase` and rebuilds the event, text and sync cursors for time `t`. Texts and triggers
     between the old and new time are **skipped, not fired**.
- **A sync may move time backwards or forwards by any amount.** This is how drift is corrected and how phases are
  pushed. [run]: with the timeline at 200.0, a matching line for `400.0 ... window 400,0` snapped the time to 400.0.
- **A sync on an entry that has a jump:**
  - The entry is displayed like any other.
  - The jump happens only if its sync matches inside its window.
  - If the entry's time passes unmatched, a plain `jump` does nothing and playback continues linearly. This is why
    authors follow a conditional jump with `?`-suffixed preview entries, for example DMU's
    `202.4 "Light of Judgment (Enrage)?"`.
  - A `forcejump` jumps anyway (3.5).

### 3.5 `forcejump`

On every tick, cactbot checks the next armed force jump (`forceJumps` sorted by time, with cursor `nextForceJump`).
When `fightNow >= its time`:

1. It fires pending texts up to the jump source.
2. It calls `SyncTo(jumpDest, currentTime − offset)` with `offset = fightNow − jumpSource`. The documented intent is
   "jump as if it had synced exactly on time" [doc]. **In practice the offset is in seconds and is subtracted from
   milliseconds**, so the timeline lands on `jumpDest` at the moment the tick runs (section 3.10, Q1).
3. It creates an **overhang sync**: a copy of the force-jump sync with
   `time = start = jumpDest`, `end = jumpDest + (end − time)` and jump type normal.
   - If the real event arrives late, inside `[jumpDest, jumpDest + after)`, it resyncs to `jumpDest`.
   - It is cleared by any `SyncTo`, and dropped once `fightNow >= end`.
   - [run]: `310.0 "LoopEnd" ... window 5,5 forcejump "loop"` (label at 300) → after passing 310 the overhang was
     `[300,305)`. A late `FFFF` line at 300.1 resynced to 300.0.
4. If the force-jump entry's sync matches **before** its time, it acts as a normal `jump` [run: a match at 308
   jumped to 300].

After a backward jump, the force jump stays armed and fires again on the next pass, which is how loops work [run].

**Display lookahead.** cactbot's upcoming-events list continues through force jumps: it shows the events after the
destination, shifted in time [code `_AddUpcomingTimers`]. Ordinary `jump`s get no lookahead.

### 3.6 Labels

Labels are resolved at load time to plain numbers (2.6). At runtime a label jump is identical to a numeric jump.

### 3.7 Stopped state, start and restart

- **`Stop()`:** sets `timebase = 0`, resets all cursors (including `nextForceJump`), and makes active every sync with
  `start <= 0 < end`.
- **Starting.** Any of those syncs matching starts playback at its own time, or at its jump target [run: the
  `InCombat` line started at 0.0; while stopped, a `7B13` line started playback at 400.0 via
  `400.0 ... window 400,0`].
- **Consequences:**
  - Every `window N,0` phase sync, and every `window B,A` with `B >= t`, can restart a stopped timeline mid-fight.
    Section 5 lists them for each fight.
  - Conversely, the `InCombat` sync (`0.0 ... window 0,1`) is active only while stopped or during the first second.
    A combat flag turning on mid-fight while stopped therefore restarts the timeline at **0.0**; the next phase sync
    then corrects it.
- **What does not stop playback.** A sync whose **own time** is 0 without a jump (the `InCombat` sync) does not stop;
  it starts playback at 0. Only `jump 0` stops.

### 3.8 Stop and reset conditions

1. **A sync with jump 0.** TOP, DMU and DSR have
   `0.0 "--Reset--" ActorControl { command: "4000000F" } window 0,100000 jump 0`. This is the DirectorUpdate "fade
   in" sent on a wipe [doc LogGuide line 33]. TEA has `hideall "--Reset--"` but **no** `--Reset--` entry.
2. **Out of combat.** `TimelineController.SetInCombat(false)` calls `Stop()`.
   - It is called by `PopupText.SetInCombat` when OverlayPlugin's `onInCombatChangedEvent.inGameCombat` turns false.
   - This only happens if the trigger set's `resetWhenOutOfCombat` is not false. TOP, TEA, DMU, DSR and FRU do not set
     it, so the default `true` applies [code].
3. **Wipe lines.** `PopupText.OnNetLog` treats these as a wipe [code `isWipe`]:
   - an ActorControl line with command `40000010` or `4000000F`;
   - `/echo cactbot wipe...`;
   - `/echo end`.

   On a wipe it calls `SetInCombat(true)` (only if not already in combat) then `SetInCombat(false)`, which stops the
   timeline. This path is gated by `resetWhenOutOfCombat` too: `SetInCombat` returns before stopping anything when it
   is false [code `popup-text.ts`]. For the five files the default `true` applies.
4. **Zone change** (and job change). The timeline is reloaded, which starts it stopped. The Dalamud equivalents are
   `IClientState.TerritoryChanged`, whose handler takes a `uint` territory id, and `IClientState.TerritoryType`
   [probe]. Load the timeline for territory 1122, 887 or 1363 and stop it on leaving.

### 3.9 After the last entry

Nothing special happens: the timeline keeps running with nothing left to show. Syncs whose windows have not ended
still match [verify run, parser dump + `Timeline` sim]:
- **TOP** (last entry 1421.9): `--Reset--` (`[0,100000)`), plus the 1391.9 `StartsUsing 7BA0` sync until 1441.9.
- **DMU**: `--Reset--`, plus the 1087.7 `ActorControl { command: "80000027", data0: "2E" }` sync. Its window
  `[-0.3,15087.7)` stays open through P5, after P5's end (1617.8) and past the file's last entry (15005.0). In the sim, a
  repeated `80000027`/`2E` line at 1620 pulled the timeline back to 1087.7. Whether that director update can recur
  after P4 starts is UNVERIFIED.
- **TEA** has no `--Reset--`. Only the last entry's own ±2.5 s window (1247.6 `4897`, until 1250.1) outlives it.

Playback ends only through 3.8.

### 3.10 cactbot quirks and what MitCue should do

| # | cactbot behaviour | Evidence | MitCue recommendation |
|---|---|---|---|
| Q1 | The force-jump overshoot `offset` (seconds) is subtracted from `currentTime` (ms), so the jump lands on `jumpDest` at tick time, losing the overshoot. Hidden force-jump entries (no display bar) are only processed at the next scheduled tick, so they can land late. | [run] the would-be time 310.5 became **300.001** instead of 300.5 | Implement the documented intent: `time = jumpDest + (now − jumpTime)`. Check force jumps every frame. |
| Q2 | `SyncTo` never resets `nextForceJump`. Scenario: a forward sync moves time past a force jump, then a later jump moves time back before it. The force jump is **not re-armed**. | [run] time passed 106 with `F1` at 100 still unfired | After every time change, set the cursor to the first force jump with `time > newTime`. None of TOP/TEA/DMU can hit this today: DMU's only force jump goes forward. |
| Q3 | Unwrapped values: `id: "1|2"` produces a broken alternation. | [run] | Wrap each value in `(?:…)`. |
| Q4 | A sync on a line with "Extra text" stays active although its entry is dropped. | [run] | Drop both. |
| Q5 | 1/32 s ceiling on `fightNow`. | [code], [run] | Optional. |
| Q6 | Texts and triggers are skipped on forward syncs, but **re-fire after a backward resync** (`nextText` is reset to 0 and re-advanced). | [code `SyncTo`] | Deduplicate reminders by entry and occurrence. Decide explicitly whether a forward jump should show the reminders it skipped; cactbot does not. |
| Q7 | The TEA source `"Liquid Rage "` (trailing space, line 39) produces a never-matching sync. | [run] regex `\|Liquid Rage \|` | Keep the exact semantics but lint-warn, or trim. Either way, 113.0 "Rage Wave 1" has no effective sync today. |

### 3.11 Reference algorithm (C#-oriented)

```csharp
// Parsed, immutable:
//   Entries     sorted by (Time, Line)
//   SyncsByEnd  sorted by (End, Line)   -- the cactbot priority order
//   ForceJumps  sorted by Time
// Times are double seconds.
sealed class TimelinePlayer
{
    bool running; long timebaseTicks; Sync? overhang; int nextForce;

    double Now(long t) => running ? (t - timebaseTicks) / (double)TimeSpan.TicksPerSecond : 0.0;

    public void OnEvent(GameEvent ev, long t)          // one call per observed game event
    {
        double now = Now(t);
        foreach (var s in SyncsByEnd)                   // first match wins
            if (s.Start <= now && now < s.End && s.Matches(ev)) { Fire(s, t); return; }
        if (overhang is { } o && o.Start <= now && now < o.End && o.Matches(ev)) Fire(o, t);
    }

    void Fire(Sync s, long t)
    {
        if (s.Jump is double dest) { if (dest == 0) Stop(); else SetTime(dest, t); }
        else SetTime(s.Time, t);
    }

    void SetTime(double timelineSeconds, long t)
    {
        overhang = null;
        long newBase = t - (long)(timelineSeconds * TimeSpan.TicksPerSecond);
        if (running && Math.Abs(newBase - timebaseTicks) <= 2 * TimeSpan.TicksPerMillisecond)
            return;                                     // cactbot's 2 ms dedupe
        timebaseTicks = newBase; running = true;
        nextForce = FirstIndex(ForceJumps, f => f.Time > timelineSeconds);   // Q2 fix: re-arm
    }

    public void Tick(long t)                            // every IFramework.Update
    {
        while (running && nextForce < ForceJumps.Count && ForceJumps[nextForce].Time <= Now(t))
        {
            var f = ForceJumps[nextForce];
            double overshoot = Now(t) - f.Time;         // Q1 fix
            SetTime(f.Jump!.Value + overshoot, t);
            overhang = f with { Time = f.Jump.Value, Start = f.Jump.Value,
                                End = f.Jump.Value + (f.End - f.Time), IsForce = false };
        }
    }

    public void Stop() { running = false; overhang = null; nextForce = 0; }
    // Call Stop() on: a jump-0 sync, combat ending (section 3.8 #2), a wipe, or a territory change.
}
```

"Upcoming entries" for reminders follow the same rules as cactbot's display:
- walk `Entries` from `now`;
- when an entry carries a **force** jump, continue from the destination with the time offset
  `entry.Time − dest`;
- skip entries at or before a jump destination.

---

## 4. Sync-type inventory

### 4.1 Active syncs by type [run, counted from the parsed timelines]

| Type (line) | TOP | TEA | DMU | DSR | FRU | Fields used |
|---|---:|---:|---:|---:|---:|---|
| `Ability` (21/22) | 189 | 141 | 214 | 214 | 170 | `id` always; `source` on all except DMU 3 and FRU 26 |
| `StartsUsing` (20) | 15 | 1 | 15 | 9 | 6 | `id`; `source` on all except FRU 1 |
| `InCombat` (260) | 1 | 1 | 1 | – | 1 | `inGameCombat: "1"` |
| `ActorControl` (33) | 1 | – | 10 | 1 | – | `command` (`4000000F`, `80000027`); `data0` (DMU 9) |
| `MapEffect` (257) | 1 | – | – | – | 1 | `flags`, `location` |
| `RemovedCombatant` (04) | – | 1 | – | – | – | `name` |
| `HeadMarker` (27) | – | – | 3 | – | – | `id` |
| `GainsEffect` (26) | – | – | 1 | – | 1 | `effectId` |
| `LosesEffect` (30) | – | – | 1 | – | – | `effectId` (array) |
| `SpawnNpcExtra` (272) | – | – | 4 | – | – | `tetherId` |
| `ActorControlExtra` (273) | – | – | 1 | – | 1 | `category`, `param1` (+ `param2` in DMU) |
| `NetworkCancelAbility` (23) | – | – | – | 1 | – | `id`, `source` |
| `WasDefeated` (25) | – | – | – | – | 1 | `target` |
| **Total syncs** | **207** | **144** | **250** | **225** | **181** | |
| Entries (with or without sync) | 282 | 225 | 417 | 367 | 227 | |
| Commented-out syncs, inline `#Type{}` | Ability 59 | Ability 40 | Ability 137, GainsEffect 5, Tether 2, ActorControlExtra 2 | Ability 104 | Ability 27 | |
| Commented-out syncs, whole line | Ability 4 | – | ActorControl 1, Ability 1 | – | – | |

Array-valued fields [run]:
- TOP: `id` 7, `source` 2;
- TEA: none;
- DMU: `id` 60, `source` 7, `effectId` 1;
- DSR: `id` 16, `source` 6;
- FRU: `id` 19, `source` 3.

No file uses `NameToggle` (targetability), `AddedCombatant`, `GameLog`, `SystemLogMessage`, `NpcYell` or
`BattleTalk2`. The `--targetable--` and `--untargetable--` entries are display-only. The one targetability-named
entry with a sync is DMU's `675.0 "--both targetable--"`, and it syncs on `LosesEffect` (Epic/Fated Villain), not on
targetability [verify run].

### 4.2 Per type: log line, game event, and how a Dalamud plugin observes it

The Dalamud and ClientStructs members named in this table were compiled in the probe
`/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/cactbot-sync-events/Plugin.cs`
[probe]. `docs/research/clientstructs.md` has fuller hook layouts and caveats (sections 2, 6.2–6.4).

| Type | Game event behind the line | Dalamud observation |
|---|---|---|
| `Ability` | An ActionEffect packet: the action resolves. ACT writes one 21/22 line per target, and still writes one line (target `E0000000`) when nothing is hit [doc]. | Hook `ActionEffectHandler.Receive`: `IGameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(ActionEffectHandler.Addresses.Receive.Value, detour)`. The detour signature is `(uint casterEntityId, Character* caster, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)`. Use `header->ActionId`. Get the caster name from the object table or from `NameId` (4.4). One call per action. Caveat from BossMod, which hooks this same function: the action id is already unscrambled here, and the function is **not called if the caster object does not exist** client-side [code BossMod `ActionManagerEx.cs` `ProcessPacketActionEffectDetour` comment]. |
| `StartsUsing` | An ActorCast packet: the cast bar starts [doc]. | Each `IFramework.Update`, iterate `IObjectTable` and, for each `IBattleChara`, read `IsCasting`, `CastActionId`, `CastActionType`, `CurrentCastTime`, `TotalCastTime`, `Name`, `NameId` and `EntityId`. Edge-detect a new cast when `IsCasting` turns true, `CastActionId` changes, or `CurrentCastTime` decreases. Latency is one frame. Alternative: the `PacketDispatcher.HandleActorCastPacket(uint casterEntityId, ActorCastPacket* packet)` hook (clientstructs.md 6.4). Its signature compiled in the verification probe; `ActorCastPacket` has `SpellId @0x0`, `ActionType @0x2` and `ActionId @0x4` [DLL metadata]. BossMod's `IDScramble.cs` says the ActorCast packet's action id is scrambled on the wire, and BossMod reads cast ids from the character's `CastInfo`, not from this packet. So whether `ActionId` is already unscrambled at this hook is UNVERIFIED; the polling approach avoids the question. Whether every invisible helper caster appears in `IObjectTable` is UNVERIFIED. |
| `InCombat` | OverlayPlugin reads an in-game "in combat" byte by memory signature [code `InCombatMemory73.cs`] and writes a 260 line whenever ACT or game combat changes. | `ICondition[ConditionFlag.InCombat]`, false→true edge. That it is the same flag OverlayPlugin reads is UNVERIFIED; it has the same meaning (the local player's combat state). |
| `ActorControl` | ActorControl(Self) category **0x6D DirectorUpdate**. cactbot's `instance` is `arg1` and `command` is `arg2` [code Dalamud `DutyState.cs`: `category == 0x6D`, `switch (arg2)`, `CreateEventArgs(arg1)`]. `data0`..`data3` = `arg3`..`arg6` is inferred from position (UNVERIFIED). | Hook `PacketDispatcher.HandleActorControlPacket(uint entityId, uint category, uint arg1, …, uint arg8, GameObjectId targetId, bool isRecorded)` via `PacketDispatcher.Delegates/Addresses.HandleActorControlPacket`. Filter `category == 0x6D`. For the wipe, `IDutyState.DutyWiped` exists (1-argument delegate `IDutyState.DutyWipedDelegate(IDutyStateEventArgs args)`; `args.TerritoryType` is a Lumina `RowRef<TerritoryType>`, so use `.RowId` [verify probe]), but it fires on `0x40000005`, not on cactbot's `0x4000000F` [code]. |
| `MapEffect` | A MapEffect packet. OverlayPlugin formats `flags = {flag2:X4}{flag1:X4}` and `location = {index:X2}` from struct fields `uint instanceContentID @0, ushort flags1 @4, ushort flags2 @6, byte index @8` [code `LineMapEffect.cs`]. | Hook `PacketDispatcher.HandleMapEffectPacket(MapEffectPacket*)`. ClientStructs `MapEffectPacket` (size 16) has `uint EventId @0, ushort State @4, ushort TimelineIndex @6, byte Index @8` (names and exact types compiled [probe]; offsets read from `FFXIVClientStructs.dll` metadata [verify]). So cactbot `flags` = `TimelineIndex:X4` + `State:X4` and `location` = `Index:X2`, by offset correspondence; that the ClientStructs fields mean the same thing is UNVERIFIED. OverlayPlugin also emits lines for the multi-entry MapEffect4/8/12 packets, which this hook does not see. Whether TOP's P2 effect is single-entry is UNVERIFIED. |
| `RemovedCombatant` | An actor leaves the scene: removed, died, out of range, or zone change [doc]. | Diff `IObjectTable` between frames (by `EntityId`) and match the name. |
| `HeadMarker` | ActorControl category **TargetIcon = 34**, `p1` = marker id. BossMod: `OpIcon(actorID, p1 - Network.IDScramble.Delta, p2)` with `Delta = 0` at commit `6e410ef` [code]. | `HandleActorControlPacket`, `category == 34`, `arg1`; format as `X4`. UNVERIFIED in game. |
| `GainsEffect` / `LosesEffect` | A status is added or removed. A refresh produces another 26 line [doc]. | Diff `IBattleChara.StatusList` (`StatusId`) per actor between frames. |
| `SpawnNpcExtra` | The `tetherId` and `animationState` from an NpcSpawn packet. OverlayPlugin's struct `Server_NpcSpawn_Global_6_51` reads `tetherId` at offset 0x7C [code]. | No Dalamud service exists, but ClientStructs has `PacketDispatcher.HandleSpawnNpcPacket(uint entityId, SpawnNpcPacket* packet)`; the hook compiled in the verification probe. `SpawnNpcPacket.Common` (`CommonSpawnData @0x10`) has `uint TetherTargetId @0x48`, `ushort TetherId @0x6C` and `byte AnimationState @0x85`. Those are packet offsets 0x58 / 0x7C / 0x95, exactly the offsets OverlayPlugin reads for `parentId` / `tetherId` / `animationState` [DLL metadata + code]. That they carry the same values in game is UNVERIFIED. Not needed: DMU only uses it for display labels (section 5.3). |
| `ActorControlExtra` | ActorControl packets in OverlayPlugin's allow-list: 0x31, 0x3E SetAnimationState, 0x3F SetModelState, 0xB8, **0x197 PlayActionTimeline (407)**, **0x19D EObjAnimation (413)**, 0x834 DisplayPublicContentTextMessage [code `LineActorControlCommon.cs`, matches BossMod]. `id` is the actor. | `HandleActorControlPacket` with that `category`; `param1..4` = `arg1..arg4` is inferred (UNVERIFIED). |
| `NetworkCancelAbility` (DSR) | A cast is cancelled or interrupted [doc]. | Heuristic: `IsCasting` turns false with no ActionEffect for that action. UNVERIFIED. A likely more direct source: BossMod lists ActorControl category `15 CancelCast` ("machina calls it CancelAbility") [code BossMod `ServerIPC.cs`], seen through `HandleActorControlPacket`. Also UNVERIFIED. |
| `WasDefeated` (FRU) | An actor is defeated [doc]. | `IGameObject.IsDead` false→true edge, or ActorControl category 6 (Death, per BossMod). UNVERIFIED equivalence. |
| `Tether` (only commented out, in DMU) | ActorControl category Tether = 35 (BossMod `OpTether(actorID, new(p2, p3))`). | Not needed. |

### 4.3 Criticality for TOP, TEA and DMU, and the available fallbacks

| Type | TOP | TEA | DMU |
|---|---|---|---|
| `Ability` | **critical**. It starts P3, P4, P5 and P6 through wide self-cast syncs: `7B13`, `7B47`, `7B7C` and `7F72`, each `window N,0`. | **critical**. It starts Transition 1 (`4830`, `window 200,0`), P2 (`4854`, `window 250,5`) and P4 (`4A8B`, `window 900,0`). | **critical**. It starts P3 (`C3F7`, `window 600,20`) and the P4→P5 jump (`C3FD`, `window 90,5`). |
| `StartsUsing` | high. P1 fallback start `7B03` (`window 20,20`, active while stopped); P2 `7B40` (`window 210,5`); enrage syncs. | high. **Only** wide sync for Transition 2: `485A` Temporal Stasis, `window 500,0`. | **critical**. All enrage branches (`BABB`, `BAE1`, `C258`/`C259`/`C61E`/`C61F`), the P2→P3 jump (`C3F7`, `window 485,5`) and P5 (`BB40`, `window 1400,0`). |
| `InCombat` | medium. `StartsUsing 7B03` also starts P1, about 11 s in. | **critical**. It is the only sync that can start P1: P1's first sync, 11.4 `49B0` (`window 10,2.5`), is not active while stopped. | low–medium. Fallbacks are `ActorControl ... 00` (`window 1,5`) and `StartsUsing C403` (`window 20,10`). |
| `ActorControl` | low. Only `--Reset--`; combat end or wipe detection covers it. | – | **critical**. The **P2 start** (297.4/297.6/306.9, `window 300,5`/`307,5`) and the **P4 start** (1087.7, `window 1088,14000`) have no wide Ability or StartsUsing alternative (section 6). Also P1 0.5, and mid-phase 189.4, 306.8, 633.2 and 1366.1. |
| `MapEffect` | low. P2 200.0; fallback `StartsUsing 7B40` at 203.4 (`window 210,5`), which syncs 3.4 s later but still 3.0 s before Firewall (206.4) and 11.1 s before the first P2 hit, `Solar Ray` (214.5). | – | – |
| `RemovedCombatant` | – | low. 196.0 `window 50,0` opens at 146.0, after P1's hard enrage (142.1), so it can only match if the timeline has already run past 146. Corroboration: BossMod's TEA module ends P1 when Living Liquid dies and expects the first Hawk Blaster 5.2 s later [code BossMod `TEAStates.cs`]. The timeline puts the `Liquid Hand` removal 4.0 s before Hawk Blaster, so it lands about 1.2 s after the push, always before 146. The real Transition-1 sync is 200.0 `Hawk Blaster`. | – |
| `HeadMarker`, `GainsEffect`, `LosesEffect`, `SpawnNpcExtra`, `ActorControlExtra` | – | – | low. All are mid-phase with default windows, mostly on display markers such as `--1 tether--`, `--both targetable--` and `--1st tower--`. |

### 4.4 Source-name matching and client language

- **What `source` matches.** It is the caster's display name, compared case-insensitively. For non-English clients
  cactbot rewrites it through `replaceSync`.
- **What MitCue can do.** Dalamud exposes `IClientState.ClientLanguage` and `ICharacter.NameId`, and
  `IDataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>(ClientLanguage.English).GetRowOrDefault(nameId)?.Singular.ExtractText()`
  compiles [probe]. Every source, name and target string used by a TOP, TEA or DMU sync exists case-insensitively as
  an English `BNpcName` singular [data, `BNpcName.csv`]:
  - TOP: `Omega` → rows 7635, 7636, 7666, 7695; `Omega-M` → 7633, 12257; `Left Arm Unit` → 7637.
  - TEA: `Living Liquid` → 3765, 9211.
  - DMU: `Kefka` → 7131; `Chaos` → 7691; `Exdeath` → 6052; `Neo Exdeath` → 6055; `Graven Image` → 7132;
    `Black Hole` → 6054, 8343.
  - The only exception is the TEA typo `"Liquid Rage "`.
  - Many singulars are lowercase in the sheet (`left arm unit`), which is fine because matching is case-insensitive.
  - Recommendation: match the regex against the English `BNpcName` singular of the caster's `NameId`, falling back to
    the display name.
- **Ignoring `source` is low risk for these files.** [run] Overlapping windows on the same type and id, but with a
  different source, occur once in TOP and never in TEA or DMU. The TOP case is 1172.8 `Flash Gale 1` (`Omega-F`)
  against 1176.0 `Flash Gale 2` (`Alpha Omega`).
- **Some lines omit `source` on purpose because the logged source is unreliable.** DMU: "These can be incorrectly
  attributed to any boss". FRU: "Source actors are elided on some lines where stale data can make P1 actors fill it
  in".

---

## 5. Per-fight analysis

All times are timeline seconds. "Active while stopped" means the window contains 0, so the sync can (re)start a
stopped timeline (3.7).

### 5.1 TOP: The Omega Protocol (Ultimate), territory 1122 (`the_omega_protocol.txt`)

**Structure.** TOP is linear: there are **no labels, jumps or forcejumps** apart from `--Reset--`'s `jump 0`. Each
phase is a block placed at a round number (200, 400, 600, 700, 1100) and entered through a wide phase sync.

| Phase | Range | Starting sync | Other syncs active while stopped / fallbacks | Ends with (enrage) |
|---|---|---|---|---|
| P1 Omega ("Beetle") | 0.0 – 127.7 | `0.0 "--sync--" InCombat { inGameCombat: "1" } window 0,1` | `11.0 "--sync--" StartsUsing { id: "7B03", source: "Omega" } window 20,20` → `[-9,31)` | 127.7 `Atomic Ray Enrage` (7AF8) |
| P2 Omega-M / Omega-F | 200.0 – 347.4 | `200.0 "--sync--" MapEffect { flags: "00020001", location: "0A" } window 200,0` | `203.4 "--sync--" StartsUsing { id: "7B40", source: "Omega" } window 210,5 # extra sync due to map effect sync` → `[-6.6,208.4)` | 347.4 `Laser Shower Enrage` (7B45, a 60 s cast [data]) |
| P3 "Final" Omega | 400.0 – 587.4 | `400.0 "--sync--" Ability { id: "7B13", source: "Omega" } window 400,0` | `436.3 StartsUsing 7B55 window 40,40` (Hello, World) | 587.4 `Ion Efflux Enrage` (7B48) |
| P4 Blue Screen | 600.0 – 661.6 | `600.0 "--sync--" Ability { id: "7B47", source: "Omega" } window 600,0` | – | 661.6 `Blue Screen Enrage` (7B7B) |
| P5 Run: Dynamis (Delta 715–789, Sigma 791–883, Omega 885–1000) | 700.0 – 999.7 | `700.0 "--sync--" Ability { id: "7B7C", source: "Omega" } window 700,0` ("Blue Screen DPS check passed") | – | 999.7 `Blind Faith Enrage` (7F73) |
| P6 Alpha Omega | 1100.0 – 1421.9 | `1100.0 "--sync--" Ability { id: "7F72", source: "Omega-F" } window 1100,0` ("Blind Faith DPS check passed") | `1391.9 StartsUsing 7BA0 window 50,50` | 1421.9 `Run: ****mi* Enrage` (7BA0: 16.0 s cast + 14.4 s extra = 30.4 s [data], matching the file's comment "~30s in practice") |

These phase anchors match the trigger file's own phase tracker: `StartsUsing 7B40` for P2, and `Ability` `7B13`,
`7B47`, `7B7C` and `7F72` for P3–P6 [code `the_omega_protocol.ts`].

**Candidate phase zero points** for converting phase-relative plan times into timeline times (all are entries in the
file):

| Phase | Phase sync | Other candidates |
|---|---|---|
| P1 | 0.0 (pull) | — |
| P2 | 200.0 MapEffect | 202.3 `--targetable--`, 203.4 Firewall cast start, 206.4 `Firewall` |
| P3 | 400.0 `7B13` | 432.3 `--targetable--` |
| P4 | 600.0 `7B47` | 607.1 `--targetable--` |
| P5 | 700.0 `7B7C` | 715.6 `--targetable--` (Delta) |
| P6 | 1100.0 `7F72` (Blind Faith) | 1154.7 `--targetable--` |

**Random or variable mechanics.** These are handled with id alternatives, not branches:
- `Superliminal Steel/Optimized Blizzard III` `["7B2A", "7B2D"]`
- `Efficient Bladework/Beyond Strength` `["7B25", "7B26"]`
- `Oversampled Wave Cannon` `["7B6B", "7B6C"]`
- `Swivel Cannon` `["7B94", "7B95"]`
- `Diffuse Wave Cannon` `["7B9B", "7B9C"]` and `["7B78", "7B77"]`
- `Storage Violation x5 / Storage Violation x6` `"7B0[45]"`
- `Colossal Blow` with source `["Left Arm Unit", "Right Arm Unit"]` and `window 1,1`

**Display-only entries (no sync).** Their times rest on the previous sync:
- 108.0 `Diffuse Wave Cannon Kyrios x5` (`duration 8`, `#7B10`)
- P3 427.3 `High-powered Sniper Cannon x2`, 428.4 `Sniper Cannon x4` ("sniper cannon times are a little sloppy")
- P3 `Patch x2` / `Patch` (466.9, 487.9, 509.0, 527.5, 533.5) ("people break both patches here whenever")
- P5 `Hyper Pulse` follow-ups (771.8–774.0)
- P5 `Hello, Near/Distant World 2/3` (788–789, 881–883, 946–947, 964–965)
- P5 `Patch` (761.2, 764.9, 782.0, 784.0)
- P6 `Cosmo Dive Far` / `Near x2` (1199.3, 1312.4)
- P6 `Wave Cannon Puddle 1–6` (1223.4–1232.9, 1299.7–1309.2), `Wave Cannon 1/2` (1236.0/1238.0, 1269.9/1271.9) and
  `Wave Cannon (Wild Charge)` (1244.3, 1278.2)
- P6 `Cosmo Meteor 1/2 x4` (1336.7/1337.7, 1342.7/1343.7)
- the marker `--adds targetable--` (1339.3)

**Overlapping same-id windows** [run]: `Wave Cannon B`/`Wave Cannon A` `7F16` (626.7 / 631.5), and in P6 every
`7C03` pair and `Flash Gale 1/2` `7DDF` pair. The earlier entry wins in each overlap.

**Constructs TOP needs.** Entries with no sync; `duration`; `Ability` and `StartsUsing` with string and array `id`
and `source`; character-class ids; `window B,A` with `A = 0` or `B > t`; `InCombat`; `MapEffect` (optional);
`ActorControl` + `jump 0` (optional); `hideall`; inline comments. TOP needs no labels, jumps or forcejumps.

### 5.2 TEA: The Epic of Alexander (Ultimate), territory 887 (`the_epic_of_alexander.txt`)

**Structure.** TEA is linear: **no labels, jumps, forcejumps or `--Reset--` entry**. Only 5 syncs are active while
stopped [run]:

| Phase | Range | Starting sync | Notes |
|---|---|---|---|
| P1 Living Liquid | 0 – 142.1 | `0.0 "--sync--" InCombat { inGameCombat: "1" } window 0,1` | `0 "Start"` is a display entry. The first ability sync, `11.4 "Fluid Swing" Ability { id: "49B0", source: "Living Liquid" } window 10,2.5`, has window `[1.4,13.9)`, so **only InCombat can start P1**. Ends with 142.1 `Cascade Enrage` (49B3). |
| Transition 1, Cruise Chaser limit cut | 196.0 – 222.9 | `200.0 "Hawk Blaster 1" Ability { id: "4830", source: "Cruise Chaser" } window 200,0` | `196.0 "--sync--" RemovedCombatant { name: "Liquid Hand" } window 50,0` is effectively inert (4.3). `Hawk Blaster 2–4` and `Middle Blaster` have no syncs. |
| P2 Cruise Chaser + Brute Justice | 226.1 – 423.7 | `226.1 "J Kick" Ability { id: "4854", source: "Brute Justice" } window 250,5` (`[-23.9,231.1)`) | Ends with 423.7 `Eternal Darkness Enrage` (483A). |
| Transition 2 / P3 Alexander Prime (Inception 537.9, Wormhole 629.3) | 492.0 – 690.0 | `492.0 "--sync--" StartsUsing { id: "485A", source: "Alexander Prime" } window 500,0` | Temporal Stasis is an 8.0 s cast [data], hence 492.0 → 500.0. P3 parts 1 and 2 continue linearly with default windows. |
| Transition 3: DPS checks / Alexander | 699.4 – 787.4 | default windows; `771.9 "Divine Judgment Enrage" Ability { id: "4879", source: "Alexander Prime" } window 67,5` | `787.4 "Down for the Count" duration 57` is display-only. |
| P4 Perfect Alexander (Fate α 900–999, Fate β 1008–1116, Almighty Judgment 1126–) | 845.4 – 1249.8 | `900.0 "--sync--" Ability { id: "4A8B", source: "Perfect Alexander" } window 900,0` | Ends with 1247.6 `Temporal Prison Enrage` (4897, `duration 9`) and the `N Players Remaining` countdown entries (1216.4–1249.8, no syncs). |

Targetable markers: P2 229.1, Transition 2 511.4, P3 600.3 / 674.2, P4 845.4.

**Random or variable mechanics.** Handled with character-class ids:
- `Hand of Prayer/Parting` `482[BC]`
- `Ordained Motion/Stillness` `487[EF]`, `4B0[DE]`, `489[9A]`, `49A[BC]`
- `Optical Sight` `488[AB]`, `48A[01]`, `49A[ED]`
- `Individual/Collective Reprobation` `488[CD]`, `48A[23]`

**Display-only entries relevant to mitigation timing:**
- `Splash x6` (81.1 and 127.4, `duration 5.6`)
- Transition 1 `Hawk Blaster 1–4` and `Middle Blaster` (202.2–219.9)
- `Apocalyptic Ray x5` (384.4 `duration 4.4`; 653.3 `duration 5`)
- `Down for the Count` (787.4, `duration 57`)
- `Fate: Sacrament x3` (975.9) and `Sacrament x3` (993.8), each `duration 1.5`
- `Almighty Judgment Reveal x3` (1130.8 and 1176.3, `duration 6`)
- `Fluid Strike` (37.7, 56.7), `Chakrams` (649.5) and `Fate: Surety and Solidarity` (1047.7), which are
  commented-out syncs

**Overlapping same-id windows** [run]:
- `Protean Wave 2/3` (4823)
- Transition 1 `Alpha Sword` 4834 #1/#3/#5/#7 and `Super Blassty Charge` 4B4F #2/#4/#6/#8 (about 4.7 s apart)
- Wormhole `Super Blassty Charge` 49C3
- P4 `Ordained Motion/Stillness` 49A[BC] (989.8 / 993.8)

**Constructs needed beyond TOP's:**
- the `RemovedCombatant` type (it can be treated as optional);
- decimal window values (`window 10,2.5`, `window 67,5`);
- an integer time (`0 "Start"`);
- display-only entries carrying only `duration`;
- the dead sync with a trailing-space source (Q7);
- no `--Reset--` entry, so playback relies on combat end or wipe detection to stop (3.8).

### 5.3 DMU: Dancing Mad (Ultimate), territory 1363 (`dancing_mad.txt`)

**Structure.** DMU is **branching**: 8 labels, 10 normal jump syncs (one of them is `--Reset--`'s `jump 0`) and
1 forcejump [run]. The HP-gated transitions and the
enrages are encoded as conditional jumps. The enrage sequences live at 10000–15005.

```text
labels: p2-success=597, p4-success=1355.6, p1-and-p4-enrage=10000, p2-enrage=11000,
        p3-enrage=12000, p3-exdeath-enrage=13000, p3-chaos-enrage=14000, p4-enrage=15000 (unused)
```

| Phase | Range | Entry sync(s) | Exits |
|---|---|---|---|
| P1 Kefka | 0.0 – ~197.5 | `0.0 InCombat window 0,1`; `0.5 "--sync--" ActorControl { command: "80000027", data0: "00" } window 1,5` (`# en (auto-translate): 'This is my first time, so please take it easy!'`); `10.6 "--sync--" StartsUsing { id: "C403", source: "Kefka" } window 20,10`. All three are active while stopped. | Enrage: `197.5 "--sync--" StartsUsing { id: "BABB", source: "Kefka" } jump "p1-and-p4-enrage"` → 10000, then `10005.0 "Light of Judgment (Enrage)"`. Otherwise the timeline runs on into the `?` previews: 202.4 `Light of Judgment (Enrage)?` (the P1 enrage hit), then 207.6 `--targetable?--`, 220.1 `Ultimate Embrace?`, 235.3 `Forsaken?`, 248.5 `The Path of Light 1?`. The last four are exactly the P2 entries 307.6–348.5 shifted by −100 s. |
| P2 God Kefka | 297.4 – 486.2 | `297.4 "--sync--" ActorControl { command: "80000027", data0: "05" } window 300,5 # Only if Kefka <15% HP` ("Fake Duty Complete Path"); `297.6 … data0: "0D" … window 300,5` ("True Duty Complete Path"); `306.8 … "06"` (default window); `306.9 … "0E" window 307,5`. Then `315.1 HeadMarker { id: '0103' }` and `320.1 "Ultimate Embrace" Ability C24C`, both with default windows. | Enrage: `480.1 StartsUsing BAE1 jump "p2-enrage"` → 11000. Success: `483.2 "--sync--" StartsUsing { id: "C3F7", source: "Kefka" } window 485,5 jump "p2-success" # Kefka will skip mechanics at 0% HP`, plus `486.2 "Aero III Assault?" Ability { id: "C3F7", source: "Kefka" } forcejump "p2-success"` → 597.0. |
| P3 Chaos + Exdeath | 597.0 – 951.8 | `597.0 label "p2-success"`; `600.0 "Aero III Assault" Ability { id: "C3F7", source: "Kefka" } window 600,20` (active while stopped); `600.8 "Down for the Count" GainsEffect { effectId: '306' } duration 32.5`. | Four syncs at 941.8:<br>• `StartsUsing C61F (Chaos) window 940,5 jump "p3-chaos-enrage"`: Exdeath died first → 14000.<br>• `StartsUsing C61E (Exdeath) window 940,5 jump "p3-exdeath-enrage"`: Chaos died first → 13000.<br>• `StartsUsing C258 (Exdeath)` and `C259 (Chaos) jump "p3-enrage"`: both alive → 12000. |
| P4 Kefka + Chaos + Neo Exdeath | 1087.7 – ~1259.7 | `1087.7 "--sync--" ActorControl { command: "80000027", data0: "2E" } window 1088,14000 # Escape from the p3 exdeath enrage or chaos` (`# en: 'Boring!'`). Its window `[-0.3,15087.7)` lets it pull the timeline back from the 13000/14000 branches. It also stays open through P5 and after it (3.9). | Enrage: `1213.2 StartsUsing BABB jump "p1-and-p4-enrage"` → 10000; the label is shared with P1. Success: `1215.3 "--middle?--" Ability { id: "C3FD", source: "Kefka" } window 90,5 jump "p4-success" # Kefka <20% HP` → 1355.6. |
| P5 Kefka | 1355.6 – 1617.8 | `1355.6 label "p4-success"`; `1366.1 ActorControl … "22"` (default window); `1396.2 "--sync--" StartsUsing { id: "BB40", source: "Kefka" } window 1400,0` (active while stopped). | Ends with 1613.4 `Forsaken Null (Enrage)` (BB3A, a 26.0 s cast [data]), still linear. |

Targetable markers: P2 307.6, P3 640.3 (`--both targetable--`), P4 1089.9, P5 1393.0.

**ActorControl `80000027` is load-bearing in DMU.** The P2 start (297.4–306.9) and the P4 start (1087.7) have no
wide Ability or StartsUsing sync.
- Without it, after a P1 push the timeline keeps playing P1's tail. P2's first syncs (`315.1`, `320.1`, `335.3`,
  `348.5` …) all have ±2.5 s windows about 100 s away, so P2 never syncs.
- After a P3 one-boss-dead branch (13000/14000), nothing returns the timeline to P4.
- What `80000027` / `data0` mean is not documented in cactbot. The file's comments tie each `data0` to a Kefka
  dialogue line, which suggests it is a director-driven dialogue cue (UNVERIFIED). It arrives as DirectorUpdate
  `command = arg2 = 0x80000027`, `data0 = arg3` (4.2). See section 6 for fallbacks.

**Variable mechanics** (array ids, 60 in total):
- `Spelldriver/Spellscatter/Spellwave` `["BAC0", "BAC1", "BAC2"]` ("can be resolved in different orders")
- `Future's End/Past's End` `["BAD2", "BAD3"]`
- `Intemperate Will/Gravitational Wave` `["BAB2", "BAB1"]`
- `Stray Flames/Stray Spray` (682.0, 709.0) and `Inferno/Tsunami` (683.0) with source `["Chaos", "Kefka"]`. The
  comment "Supposed to be from Chaos, but can bug" is on the `Inferno/Tsunami` line.
- `Death Surge` / `Death Shriek` / `Death Wave` with **no source** ("can be incorrectly attributed to any boss")
- `Fell Forces` `["C654", "C653", "C654"]` (a harmless duplicate element)

**Display-only and `?` entries.** The file holds many (full list in `nosync.out`, section 8). Groups relevant to
mitigation timing:
- P1 `Hyperdrive 1–3` (65.8–69.8, 135.6–139.7), the `Blizzard III Blowout` / `Thrumming Thunder III` variants, and
  `Gravity III (Pop Window)` (117.0 `duration 6`)
- P2 `All Things Ending` sets 1–3 (369.1, 390.4, 411.2; set 4 at 432.1 is synced) and `Trine 1–3` (465.2–469.2)
- P3 `Ultima Blaster 1–8` (both sets), `Thunder III 1/2`, `Shockwave 1/2`, `Slap Happy 1–3`, `Stomp-a-Mole 1–4`, the
  `Primordial Crust Quake` series, and `Accretion Earthquake 1/2?`
- P4 `Death Bolt` / `Death Wave` (1164.5, 1189.4) and the debuff markers `--first debuffs--` …
- P5 `Ultima Repeater 1–4`, `Flood 1–4`, `Chaotic Flood x4`, `Stray Apocalypse N x6` (`duration 2.4`, with
  `window 2,2` syncs on BB3C "Default window leads to timeline sync backs"), and `Forsaken Ground/Forsaken Bonds 1–4`
- the `?` previews after each conditional jump (P1 tail, 485.0, 946.8, 1218.1–1259.7)

**Overlapping same-id windows** [run]: 21 pairs, for example `Tele-trouncing 1/2` BABA, `Blizzard III 1/2` BB0D,
`Holy` BB54, the `C652` / `Fell Forces` series, and BB3C. One pair is notable:
`486.2 "Aero III Assault?"` `[483.7,488.7)` overlaps `600.0 "Aero III Assault"` `[0,620)`. The 486.2 forcejump
entry wins inside its window because its end is earlier.

**Constructs needed beyond TOP's and TEA's:**
- `label`; `jump "label"` (forward); two jump syncs sharing a label (`p1-and-p4-enrage`); four jump syncs at the
  same time (941.8);
- `forcejump "label"` with an overhang window;
- windows with a negative start or a huge end (`window 1088,14000`);
- `ActorControl` with `data0`; `HeadMarker`; `GainsEffect` / `LosesEffect` (array `effectId`); `SpawnNpcExtra`;
  `ActorControlExtra`;
- `Ability` syncs without `source`;
- single-quoted JSON5 strings;
- `StartsUsing { ... } duration 5` (duration after the sync);
- an unused label;
- far-away enrage blocks (10000+) that are only reachable by jumps.

### 5.4 DSR and FRU: additional constructs

**DSR:**
- No `InCombat` sync ("No engage sync, due to checkpoint"). P1 is started by
  `6.0 StartsUsing 62D4 window 10,10`.
- `993.1 "--sync--" NetworkCancelAbility { id: "63C3", source: "King Thordan" } window 300,0` ("If Aetheric Burst
  gets interrupted").
- Numeric jumps:
  - **backwards** from P4.5 to P2: `2055.9 "--sync--" StartsUsing { id: "63C8", source: "King Thordan" } window 60,200 jump 550` ("if Haurchefant dies and it loops");
  - a 3.1 s self-loop at the final enrage, done with a plain jump: `4251.8 "--sync--" Ability { id: "6E2F", ... } jump 4248.7` ("This seems to loop indefinitely").
- An after-window larger than the before-window (`window 60,200`).
- `StartsUsing` as a displayed entry with a duration: `500.0 "Pure of Heart" StartsUsing { id: "62E4", ... } duration 36`.

**FRU:**
- `WasDefeated { target: 'Ice Veil' } window 488.8,5`
- `GainsEffect { effectId: "1043" }` (Stun) as a displayed entry
- `ActorControlExtra { category: "0197", param1: "1E43" } window 150,5 # limited lookbehind due to re-use`
  (PlayActionTimeline)
- `MapEffect` location `"17"`
- windows whose before-part equals the entry time, such as `window 424.4,2.5` (start exactly 0)
- 26 `Ability` syncs without source ("Source actors are elided on some lines where stale data can make P1 actors
  fill it in")
- the source written `"Usurper Of Frost"`, which works only because matching is case-insensitive

---

## 6. Proposed MitCue-side fallback syncs (UNVERIFIED against real logs)

These apply if MitCue does not implement the DMU DirectorUpdate (`ActorControl 80000027`). Add them as extra
timeline lines; they use the same grammar. The times come from the file's own `-p` anchors and from cast times
[data `Action.csv`].

```text
# DMU P2: Ultimate Embrace C24C is a 5.0 s cast, so the cast starts at 320.1 - 5.0 = 315.1 (same time as HeadMarker 0103).
# Window ends at 320.1, before the second Ultimate Embrace (cast start about 471.7).
315.1 "--sync--" StartsUsing { id: "C24C", source: "Kefka" } window 320,5
# DMU P4: Kefka Says C2DC is a 5.0 s cast (1095.0 -> 1100.0). Escapes the 12000/13000/14000 enrage branches like the 2E line does.
1095.0 "--sync--" StartsUsing { id: "C2DC", source: "Kefka" } window 1095,14000
```

Supporting evidence for the choice of ids: cactbot's own DMU trigger file tracks phases with exactly these casts. Its
`DMU Phase 1-4 Tracker` trigger is `StartsUsing` on `C24C` → p2, `C3F7` → p3 and `C2DC` → p4 [code `dancing_mad.ts`].
The timeline already contains `1095.0 "--sync--" StartsUsing { id: "C2DC", source: "Kefka" }` (line 341) with the
default window, so the P4 fallback amounts to widening that line's window. The times and windows above are still
unverified against logs.

Cost compared with ActorControl:
- P2 syncs about 18 s later (315.1 instead of 297.4), giving only 5 s of lead before the first P2 tankbuster
  (320.1).
- P4 syncs about 7 s later.

Both proposed windows are active while stopped. A stopped timeline (for example after an out-of-combat reset)
would therefore restart at these points if the cast appears, which is the intended self-healing behaviour (3.7).

TOP and TEA need no fallbacks, provided `Ability`, `StartsUsing` and `InCombat` are implemented.

---

## 7. UNVERIFIED items

1. The ACT FFXIV plugin's exact padding rules for lines 20/21/22/23/26/27/30/33 (closed source). The formats in
   2.4.3 are inferred from LogGuide examples. Comparing hex literals numerically avoids depending on them.
2. That `ConditionFlag.InCombat` is the same flag as OverlayPlugin's `inGameCombat` memory byte. They have the same
   meaning.
3. The DirectorUpdate argument order beyond `arg1`/`arg2` (`data0` = `arg3`, ...). The ActorControlExtra
   `param1..4` = `arg1..4`, and the HeadMarker = category 34 `arg1` mapping, rest on BossMod and on field position.
4. The meaning of DMU director command `0x80000027` and its `data0` values. Inferred as a dialogue cue only from the
   timeline's comments.
5. Whether TOP's P2 `MapEffect` arrives in a single-entry packet, which is what `HandleMapEffectPacket` sees.
6. Whether every invisible caster is present in `IObjectTable`, so that `StartsUsing` can be polled.
7. Whether the combat flag drops during long transitions (TEA "Down for the Count", DMU P2→P3). cactbot would stop
   on that, then restart via `InCombat` at 0 and be corrected by the next phase sync (3.7).
8. The section 6 fallback lines. They are derived from cast data and `-p` anchors, not from logs.
9. That `NetworkCancelAbility` and `WasDefeated` (DSR/FRU only) can be detected with the heuristics in 4.2.
10. Whether `ActorCastPacket.ActionId` is already unscrambled at the `HandleActorCastPacket` hook. BossMod says the
    wire value is scrambled and avoids the question by reading `CastInfo`.
11. That `SpawnNpcPacket.Common.TetherId` / `AnimationState` / `TetherTargetId` carry the values OverlayPlugin logs for
    `SpawnNpcExtra`. The offsets match; the values were not observed in game.
12. Whether DMU's `80000027`/`2E` director update can recur after P4 starts. Its sync stays active until 15087.7 (3.9).

---

## 8. Reproduction

All of these live in the session scratchpad; they are not committed.

**Node harness** (`npm i esbuild@0.24.0 json5@2.2.3`; `raidboss_options.ts` is stubbed with its default values) in
`/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/cactbot-run/`:

| File | Purpose |
|---|---|
| `src/dump.ts` | Parse a timeline into JSON: events, syncs, windows, jumps, labels, errors. Output is in `out/*.json`. |
| `src/probe.ts` | Per-line parse report. Used for the edge-case file `cases/grammar.txt`. |
| `src/sim.ts` | Drives cactbot's real `Timeline` with synthetic network lines and a fake clock: `cases/simA.*`, `simB.*` (Q2), `simC.*` (forcejump and overhang, Q1). |
| `src/defs.ts` | Dumps `netlog_defs.ts`. |

Build with `node build.mjs src/X.ts dist/X.mjs`, then run `node dist/X.mjs <args>`.

**Analysis scripts** in `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/scripts-tl/`:
- `inventory.py`: type and field counts;
- `special.py`: non-Ability syncs, jumps and windows;
- `overlap.py`: overlapping same-id windows;
- `nosync.py`, with its output in `nosync.out`: phase headers and display-only entries.

**Dalamud probe:** `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/cactbot-sync-events/`.
Build with `dotnet build -c Release` (succeeded).

---

## Verification

An independent pass on 2026-11-11 re-derived the report's claims from the primary sources: cactbot `e79ea7f`,
OverlayPlugin `43afdd5`, BossMod `6e410ef`, the Dalamud clone `12e318c`, the installed Dalamud/ClientStructs
assemblies, and the game-data CSVs. It used its own scripts and probes, not the researcher's. All artefacts live in the
session scratchpad:

| Artefact | Purpose |
|---|---|
| `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/verify-cactbot-run/src/inv.ts`, `src/syncdump.ts` (output `inv.out`, `syncdump.json`) | Ran cactbot's `TimelineParser` on all five timelines: counts, windows, jumps, labels, stopped-active syncs, overlaps, full sync and event dump. |
| `verify-cactbot-run/src/vprobe.ts` with `cases/vgrammar.txt` (44 lines) and `cases/vgrammar2.txt` | Grammar edge cases, written fresh. |
| `verify-cactbot-run/src/vsim.ts` | cactbot's real `Timeline` class driven with a fake clock and fake timers: ordering, jumps, jump 0, forcejump overshoot and overhang, Q2, the 2 ms dedupe, and stopped-state starts on the real TOP and DMU files. |
| `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/verify-cactbot-timelines/Plugin.cs` | Dalamud probe using exact-type checks: `Exact<T>(ref x)` and method-group-to-delegate conversions, so no implicit widening is accepted. `Build succeeded`. |
| `probes/verify-cactbot-timelines-neg/` | Negative control. `Action<ushort>` for `TerritoryChanged`, `ushort` for `TerritoryType`/`StatusId`, `int` for `CastActionType` and `uint` for `MapEffectPacket.State` all fail to compile, which proves the positive probe's types are exact. |
| `probes/verify-cactbot-timelines-meta/` | Reads struct field offsets from `FFXIVClientStructs.dll` metadata (`System.Reflection.Metadata`). |
| `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/vct-bnpc/BNpcName.csv` | A fresh download of xivapi `BNpcName.csv`, byte-identical to the researcher's copy. |

### What was checked and confirmed

- **Parser source.** Every regex in 2.1 matches `timeline_parser.ts` verbatim, and so does the statement order
  (hideall, tts, soundalert, speaker, popup text, label, entry). Confirmed: the 57 netlog type names, the JSON5 parsing
  with its error paths ("Invalid NetRegex arguments" or "Invalid NetRegex JSON" keep the entry without a sync), and the
  label resolution (the last duplicate wins; an unresolved label leaves a plain sync, or a runtime throw for
  `forcejump`).
- **Grammar edge cases, all reproduced.** Empty name; `#` in a name; `#` inside a value; `duration` before and after
  the sync; lowercase type; keywords before the sync; `window 10, 10`; number and unknown-key values; `capture`;
  `type: "22"`; JSON5 escaping (`"E\\.D"` gives `E\.D`, `"E\.D"` gives `E.D`); uppercase keywords; `window 0,0`;
  `forcejump` without a sync; `jump` plus `forcejump`; a label with a comment; a keyword inside a value; two types on
  one line (an orphan sync of the second type); tabs and `\r`; invalid numbers; `1.25`; `1|2`; a label at 0; a missing
  label; the old-style sync with its window and jump.
- **Netregex construction.** `parseHelper`, `anyOf`, `Regexes.parse` (`i` flag) and the `NetField` gap macros all
  match. The 15 example regexes in 2.4.2 are identical to the dumped ones. Field indices for all 13 types used were
  checked in `netlog_defs.ts`. Values contain no `|`. The metacharacter counts are TOP 1, TEA 16, DSR 8.
- **Playback** (code read in full, then simulated):
  - activity is `start <= now < end`, and the list is ordered by end with ties in file order;
  - the first match wins;
  - a jump lands exactly (60.4 → 200.000), and `jump 0` stops;
  - **Q1** reproduces (a late tick at 310.5 lands at 300.001);
  - the overhang is `[300,305)`, appended last, and a late line resyncs it to 300.000;
  - a match before the forcejump's time acts as a jump, and the loop re-arms;
  - **Q2** reproduces (`nextForceJump` stays past F1 after a backward jump, and F1 never fires);
  - the 2 ms dedupe holds;
  - while stopped, TOP `7B13` starts at 400.0, TOP's MapEffect at 200.0 and DMU `80000027`/`2E` at 1087.7.
- **Stop conditions.** `isWipe` is ActorControl `40000010`/`4000000F`, `/echo cactbot wipe…` or `/echo end`.
  `resetWhenOutOfCombat` is set in none of the five trigger files. Zone and job changes reload the timeline.
- **Inventory, all independently re-counted.** Per-type sync counts, entry counts (282/225/417/367/227), totals
  (207/144/250/225/181), sourceless counts (DMU 3, FRU 26 Ability and 1 StartsUsing), array counts, commented-out
  counts (inline and whole-line), labels, the 10 normal jumps plus 1 forcejump in DMU, DSR's jumps, stopped-active
  sync lists, overlap lists (TOP 11, TEA 12, DMU 21) and wide-window lists.
  - "Different-source overlaps: TOP 1, TEA/DMU 0" also holds with a stricter test (intersecting id sets after
    expanding `7B0[45]`-style classes).
- **Per-fight lines.** All cited lines, times, windows, ids, sources, comments and `-p` headers in TOP, TEA, DMU, DSR
  and FRU were spot-checked against the `.txt` files. That includes every phase start and exit, every enrage, the TEA
  typo on line 39, DMU's P1-tail previews (exactly the P2 entries shifted by −100 s), and the display-only lists.
- **Game data.**
  - `Action.csv` cast times: `7B45` 60.0 s; `7BA0` 16.0 + 14.4 s; `485A` 8.0 s; `C24C` 5.0 s; `C2DC` 5.0 s; `BB3A`
    26.0 s; `C403`, `7B40`, `7B03`, `7AF8`, `7B48`, `C3F7` and `BB40` are consistent with their cast-start/ability
    pairs.
  - `Status.csv`: `306` = 774 Down for the Count; `1061`/`1063` = 4193/4195 Epic/Fated Villain; `1043` = 4163 Stun.
  - `TerritoryType.csv` with `ContentFinderCondition.csv`: 887 TEA, 968 DSR, 1122 TOP, 1238 FRU, 1363 DMU.
  - `BNpcName`: every one of the 27 distinct source/name strings in TOP/TEA/DMU syncs exists as an English singular,
    except `"Liquid Rage "`. The row ids quoted in 4.4 are correct.
- **Dalamud / ClientStructs, compiled with exact types.**
  - Hook signatures for `ActionEffectHandler.Receive`, `PacketDispatcher.HandleActorControlPacket`,
    `HandleMapEffectPacket`, `HandleActorCastPacket` and `HandleSpawnNpcPacket`, each through `HookFromAddress` with
    `Addresses.*.Value`.
  - Member types: `header->ActionId` uint; `IBattleChara.IsCasting` bool, `CastActionId` uint, `CastActionType` byte,
    `CurrentCastTime`/`TotalCastTime` float, `EntityId` uint; `ICharacter.NameId` uint; `IGameObject.IsDead` bool;
    `StatusList` `StatusId` uint; `ICondition[ConditionFlag.InCombat]` bool.
  - `IClientState.TerritoryChanged` is `Action<uint>`, `TerritoryType` is uint and `ClientLanguage` is
    `ClientLanguage`.
  - `IDutyState.DutyWipedDelegate(IDutyStateEventArgs)`, whose `TerritoryType` is a `RowRef<TerritoryType>`.
  - The English `BNpcName` lookup.
  - The researcher's own probe also rebuilds cleanly.
- **Dalamud source.** `DutyState.cs`: category `0x6D`, `switch (arg2)`, `CreateEventArgs(arg1)`, and `DutyWiped` on
  `0x4000_0005`.
- **OverlayPlugin.**
  - MapEffect is `{flag2:X4}{flag1:X4}|{index:X2}` at `LineMapEffect.cs` line 478; the struct offsets are 0/4/6/8; the
    multi-entry MapEffect4/8/12 packets exist and leave the instance field empty.
  - SpawnNpcExtra is `{X8}|{X8}|{X4}|{X2}`, with `tetherId` at 0x7C.
  - ActorControlExtra is `{X8}|{category:X4}|{param:X}…`, with the 7-category allow-list as listed.
  - The 260 line is written on any ACT or game combat change (`LineInCombat.cs`), and `InCombatMemory73.cs` holds the
    signature.
- **BossMod.** `TargetIcon = 34`, `Tether = 35`, `DirectorUpdate = 109` (0x6D), `Death = 6`; `OpIcon(actorID, p1 -
  IDScramble.Delta, p2)` with `Delta = 0`; `OpTether(actorID, new(p2, p3))`.
- **Docs.** TimelineGuide: default window 2.5,2.5; single-value window; no space after the comma; "jump as if it had
  synced exactly on time"; overhang; `jump 0` stops; jump and forcejump mutually exclusive; two backslashes; hideall
  with timeline triggers. LogGuide: line 33 fade in/out, 21/22 lines per target, `E0000000`, the example field
  values, and RemoveCombatant.
- **Lint rules.** All seven in 2.9 match `test/helper/test_timeline.ts`, including the keyword order duration,
  window, jump, forcejump.
- **The researcher's data files.** All 1518 entries in `cactbot-run/out/*.json` (all five files) match the
  independent parse in name, time, window start and end, regex, jump and jump type. All 259 rows of
  `scripts-tl/nosync.out` match (time, line, name, duration), with no rows missing or extra.

### What was wrong or incomplete, and what was changed

| # | Section | Problem | Correction |
|---|---|---|---|
| 1 | 3.9 | "Syncs still active after the last entry: in TOP and DMU that is only `--Reset--`" is false. | DMU's 1087.7 `80000027`/`2E` sync stays active until 15087.7, through P5 and past the last entry; in the sim, a repeat at 1620 pulled the timeline back to 1087.7. TOP's 1391.9 `7BA0` stays active until 1441.9. Rewrote 3.9, added a pointer in the 5.3 P4 row, and added UNVERIFIED item 12. |
| 2 | 0, tags | The installed `Dalamud.dll` and the source clone are both `15.0.4.0+12e318c`; the label "15.0.3.9" comes from the brief. | Sources row and `[probe]` tag now state the real version. No API conclusion changes. |
| 3 | 4.2 SpawnNpcExtra | "Would need an NpcSpawn hook, which is not researched here." ClientStructs does have one. | Added `PacketDispatcher.HandleSpawnNpcPacket(uint, SpawnNpcPacket*)` (compiled), whose `Common.TetherTargetId` / `TetherId` / `AnimationState` sit at the same offsets OverlayPlugin reads. Value equivalence is UNVERIFIED (item 11). |
| 4 | 2.2 | The EBNF required whitespace between the name's closing quote and the rest. | cactbot accepts `2.1 "N"Ability {...}`; the EBNF now makes that whitespace optional. |
| 5 | 2.3 step 2 | "`duration` at the start or preceded by whitespace" was imprecise. | It also strips a glued `}duration 3`; two `duration`s give "Extra text". |
| 6 | 2.5 | The `\y{}` macro list omitted `NetTimestamp`. | Added. |
| 7 | 3.8 #3 | The wipe path was presented as unconditional. | `SetInCombat(true)` runs only if not already in combat, and the stop is also gated by `resetWhenOutOfCombat`. |
| 8 | 5.3 | The quote "Supposed to be from Chaos, but can bug" was attributed to `Stray Flames/Stray Spray`. | It belongs to `Inferno/Tsunami` (683.0), which also has `["Chaos", "Kefka"]`. The P1 `0.5` comment is `# en (auto-translate): …`. |
| 9 | 4.1 | "`--targetable--`/`--untargetable--` are display-only" did not mention DMU's synced `675.0 --both targetable--` (LosesEffect). | Clarified. |
| 10 | 4.2 Ability / StartsUsing / ActorControl / MapEffect / NetworkCancelAbility | Missing caveats or precision. | Added: BossMod notes that `ActionEffectHandler.Receive` is not called when the caster object does not exist (the id is already unscrambled there). `HandleActorCastPacket` compiled, with the `ActorCastPacket` offsets and the scramble caveat (item 10). `DutyWiped` args type. `MapEffectPacket` offsets and types from DLL metadata. ActorControl category 15 CancelCast as a candidate for `NetworkCancelAbility`. |
| 11 | 4.3 TEA RemovedCombatant | The "effectively inert" judgment rested on window arithmetic only. | Corroborated with BossMod's TEA state machine (Hawk Blaster about 5.2 s after Living Liquid dies, so the removal comes about 1.2 s after the push, before 146). |
| 12 | 6 | The fallback lines carried no supporting evidence, and the existing `1095.0 StartsUsing C2DC` line was not mentioned. | Added that cactbot's own `DMU Phase 1-4 Tracker` uses `StartsUsing` C24C/C3F7/C2DC, and that the P4 fallback amounts to widening line 341's window. Times and windows remain UNVERIFIED against logs. |

No other errors were found. In particular, the 16 "key facts" in the researcher's summary hold: those marked verified
re-verified, and those marked UNVERIFIED are correctly marked (the ACT padding rules, the meaning of `80000027`/`data0`,
the `data0 = arg3` mapping, and the section 6 fallbacks).
