# AuroraTranslator

AuroraTranslator is a .NET 10 console application for translating Aurora Builder XML content into a normalized SQLite runtime model.

The project is aimed at a hybrid migration path:

- Aurora XML remains the authoring and distribution format.
- SQLite becomes the queryable runtime/cache layer.
- The long-term goal is to let a character builder run primarily from SQLite without breaking the existing XML ecosystem.

## Current Status

The project is well past proof-of-concept. It currently provides:

- Aurora XML import into SQLite
- content package and precedence handling
- duplicate Aurora ID resolution through resolved winner caches
- generic storage for rules, setters, descriptions, extracts, and rich raw XML
- source-integrity diagnostics and unresolved-link diagnostics
- a first-party regression baseline workflow for `core` + `supplements`
- a first WPF-authoritative parity baseline workflow for core DB loader surfaces
- builder-facing catalog views
- a first-pass character-state evaluator

Notable runtime capabilities already in place:

- classes, archetypes, races, subraces, race variants, backgrounds, feats, spells, languages, proficiencies, items, sources, and many long-tail families import cleanly
- actionable unresolved importer links are down to zero on the committed first-party baseline
- expression parsing and evaluation are shared between import-time and runtime logic
- package precedence can be inspected, changed, parity-checked, and refreshed from the CLI
- character-state evaluation can resolve active features, grants, and select pools
- ASI/feat selects now expand into second-stage builder choices
- deterministic one-option PHB 2024 feature-pick rows now auto-materialize into runtime state instead of lingering as fake pending choices
- obvious direct state like an already-selected subclass can now satisfy matching generic pending picks instead of lingering as false pending work
- pending multi-pick choices are now counted from explicit applied selections instead of incidental global ownership
- common multi-pick pools now behave more like real builder slots: completed Acolyte/Fighter/Monk choice sets clear their pending rows, and extra selected picks stop applying once the slot limit is full
- multi-select `feature-pick` families such as classic Rogue `Expertise`, Warlock `Eldritch Invocation`, and PHB 2024 Sorcerer `Metamagic` now behave like real builder slots too, and saved feature selections can satisfy those picks from direct state
- broad simple/martial weapon proficiency grants now also imply their concrete weapon proficiency tokens during runtime evaluation, which lets PHB 2024 `Weapon Mastery` resolve into real selectable weapon pools instead of fake zero-option pending rows
- over-picked first-party choice states now fail loudly instead of silently passing through, either as explicit over-selection warnings or immediate `select-full` application errors depending on when the extra pick is detected
- support-driven fixed pools now resolve real element options even when Aurora encoded them only in `supports_text`, which makes nested feat families like PHB 2024 `Magic Initiate` behave like normal builder choices
- broad feat pools now honor support tags and explicit allowlists, so origin-feat slots and general-feat slots no longer bleed into one another
- broad feat pools now exclude already-owned feats unless the feat is the saved selection for that same choice row, and JSON option rows expose an `unavailableReason`
- dynamic language and proficiency pools apply the same slot-aware filtering, so grants and selections from other sources cannot be picked again while saved choices remain replayable
- ritual-only spell picks can now resolve against the global ritual corpus without needing a spell-list owner, which makes PHB 2024 `Ritual Caster` behave like a real builder package
- nested class-feature spell picks such as Cleric `Thaumaturge` now inherit their parent class spell list instead of surfacing as empty pending rows
- broad spell pools now collapse only exact-equivalent spell reprints, using the full stored spell/text/rule shape instead of just name matching, so materially different 2014/2024 variants remain separate choices
- character-state JSON output now includes a `computedCharacter` section with derived scores, traits, pending choices, warnings, and provenance
- application-facing computed output now breaks traits into clearer `movements`, `senses`, and overall `traits` collections
- application-facing runtime output now also includes flat `effectRows` and grouped `spellcastingProfiles` so the app does not have to reconstruct core effects from nested sections
- SQLite now also exposes a higher-level [v_app_effect_rows](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/sqlite-character-loading.sql) summary view so the app can consume deduped effect rows with source counts instead of raw stat/setter duplicates
- SQLite now also exposes a higher-level [v_app_choice_rows](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/sqlite-character-loading.sql) summary view so the app can consume stable choice rows without reverse-engineering `v_choice_templates`; that view now keeps static option counts separate from runtime-resolved choice families so valid zero-count rows are not mistaken for broken data
- committed character-state fixtures now cover baseline, bond/text-choice, partial and completed early fighter flows, PHB 2024 Fighter `Weapon Mastery`, Monk skill/tool/language completion, classic Rogue `Expertise`, Warlock `Eldritch Invocation` plus Pact Magic spell picks, PHB 2024 Sorcerer `Metamagic`, elf darkvision, first-party Cleric `Divine Order` and Druid `Primal Order` flows, PHB 2024 replacement-style feature picks, first-party oversubscribe warning scenarios, first-party fly/climb/swim movement scenarios, and a companion burrow-speed scenario

## Repository Layout

- [AuroraTranslator.sln](/C:/Users/Ralla/source/repos/5eApiTranslator/AuroraTranslator.sln)
- [AuroraTranslator.csproj](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraTranslator.csproj)
- [Program.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Program.cs)
- [AuroraSqliteImporter.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraSqliteImporter.cs)
- [AuroraExpressionEngine.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraExpressionEngine.cs)
- [AuroraCharacterStateEngine.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraCharacterStateEngine.cs)
- [sqlite-character-loading.sql](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/sqlite-character-loading.sql)
- [character-state-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-example.json)
- [character-state-early-fighter-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-early-fighter-example.json)
- [character-state-elf-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-elf-example.json)
- [character-state-life-domain-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-life-domain-example.json)
- [character-state-life-domain-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-life-domain-complete-example.json)
- [character-state-acolyte-magic-initiate-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-acolyte-magic-initiate-example.json)
- [character-state-aarakocra-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-aarakocra-example.json)
- [character-state-tabaxi-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-tabaxi-example.json)
- [character-state-triton-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-triton-example.json)
- [character-state-badger-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-badger-example.json)
- [character-state-high-elf-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-high-elf-example.json)
- [character-state-high-elf-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-high-elf-complete-example.json)
- [character-state-wizard-abjuration-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-wizard-abjuration-example.json)
- [character-state-ritual-caster-direct-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-ritual-caster-direct-example.json)
- [diagnostics-regression-baseline.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/diagnostics-regression-baseline.json)

## Build

```powershell
dotnet build .\AuroraTranslator.sln -v minimal
```

The build may show `NU1900` warnings if NuGet vulnerability metadata cannot be fetched from `api.nuget.org`. In this repo that has usually been an environment/network warning, not a project failure.

## Core Commands

### Import and content generation

```powershell
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- sqlite-import [auroraPath] [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- srd-creatures [jsonPath] [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- generate-xellarant-xml [jsonPath] [sqlitePath] [outputPath]
```

### Package precedence and diagnostics

```powershell
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- packages [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- set-package-enabled [packageKey] [true|false] [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- set-package-rank [packageKey] [rank] [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- refresh-package-resolution [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- refresh-package-admin-views [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- check-package-refresh-parity [packageKey] [rank|enabled] [value] [sqlitePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- summarize-unresolved-links [sqlitePath] [topCount]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- summarize-source-integrity [sqlitePath] [topCount]
```

### Regression baseline workflow

```powershell
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- capture-diagnostics-baseline [sqlitePath] [baselinePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- check-diagnostics-regression [sqlitePath] [baselinePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- capture-first-party-diagnostics-baseline [auroraRoot] [sqlitePath] [baselinePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- capture-character-state-baseline [sqlitePath] [stateJsonPath] [baselinePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- check-character-state-regression [sqlitePath] [stateJsonPath] [baselinePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- capture-wpf-parity-baseline [sqlitePath] [baselinePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- capture-first-party-wpf-parity-baseline [auroraRoot] [sqlitePath] [baselinePath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- check-wpf-parity-regression [sqlitePath] [baselinePath]
```

The committed baseline is meant to represent Wizards first-party `core` + `supplements`, not an arbitrary custom content directory.
The WPF parity baseline is meant to protect the specific normalized DB surfaces the authoritative Aurora Lights XML/WPF runtime relies on today: element/type counts, package ownership, multiclass rows, spellcasting profiles, spell-access rows, companion distributions, and a small curated set of known-good archetype/profile/companion/spell samples.
The committed character-state baselines are meant to represent fixed first-party fixtures such as [character-state-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-example.json), [character-state-early-fighter-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-early-fighter-example.json), [character-state-early-fighter-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-early-fighter-complete-example.json), [character-state-early-fighter-overpick-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-early-fighter-overpick-example.json), the completed player-facing [character-state-fighter-weapon-mastery-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-fighter-weapon-mastery-complete-example.json), the direct-state [character-state-fighter-weapon-mastery-direct-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-fighter-weapon-mastery-direct-example.json), [character-state-monk-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-monk-complete-example.json), [character-state-rogue-classic-expertise-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-rogue-classic-expertise-example.json), [character-state-rogue-classic-expertise-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-rogue-classic-expertise-complete-example.json), [character-state-rogue-classic-expertise-direct-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-rogue-classic-expertise-direct-example.json), [character-state-warlock-invocations-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-warlock-invocations-complete-example.json), [character-state-warlock-invocations-overpick-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-warlock-invocations-overpick-example.json), [character-state-warlock-spellcasting-overpick-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-warlock-spellcasting-overpick-example.json), [character-state-sorcerer-metamagic-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-sorcerer-metamagic-complete-example.json), [character-state-sorcerer-metamagic-overpick-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-sorcerer-metamagic-overpick-example.json), [character-state-high-elf-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-high-elf-example.json), [character-state-high-elf-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-high-elf-complete-example.json), [character-state-wizard-abjuration-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-wizard-abjuration-example.json), [character-state-ritual-caster-direct-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-ritual-caster-direct-example.json), [character-state-elf-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-elf-example.json), the granted-spell [character-state-life-domain-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-life-domain-example.json), the completed player-facing [character-state-life-domain-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-life-domain-complete-example.json), the direct-state [character-state-druid-primal-order-direct-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-druid-primal-order-direct-example.json), the completed player-facing [character-state-druid-primal-order-complete-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-druid-primal-order-complete-example.json), the Magic Initiate [character-state-acolyte-magic-initiate-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-acolyte-magic-initiate-example.json), the oversubscribe-warning [character-state-life-domain-oversubscribe-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-life-domain-oversubscribe-example.json), the PHB 2024 replacement-style [character-state-monk-focus-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-monk-focus-example.json), and the movement-focused [character-state-aarakocra-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-aarakocra-example.json), [character-state-tabaxi-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-tabaxi-example.json), [character-state-triton-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-triton-example.json), and [character-state-badger-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-badger-example.json) evaluated against the first-party regression DB.

### Expression and character-state evaluation

```powershell
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- eval-expression [expressionText] [contextJsonPath]
dotnet run --project .\5eApiTranslator\AuroraTranslator.csproj -- evaluate-character-state [sqlitePath] [stateJsonPath]
```

The example state file at [character-state-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-example.json) is a good starting point for smoke testing.

## Character-State Layer

The current character-state evaluator is intentionally builder-oriented rather than UI-oriented. It can:

- resolve direct character selections
- activate class/archetype/background/race/feat feature rows
- evaluate grant requirements
- evaluate select availability
- handle broad language/proficiency pools with dedicated logic
- handle broad spell pools with spellcasting-profile-aware logic
- surface selects that live on granted rule-owning elements such as racial traits and nested archetype features
- handle null-profile and constrained spell-pick pools such as High Elf Wizard cantrips and Abjurer school-restricted spell picks
- resolve support-driven fixed pools that Aurora encoded only in `supports_text`, such as PHB 2024 `Magic Initiate` spellcasting-ability picks
- honor feat support tags and ritual-only spell constraints for nested feat packages such as PHB 2024 `Ritual Caster`
- infer parent class spell lists for nested class-feature spell picks such as Cleric `Thaumaturge`
- treat ASI-style selects semantically
- auto-materialize deterministic one-option feature-pick selects that act like implicit class-feature defaults in first-party PHB 2024 content
- satisfy explicit multi-select feature picks from saved state when matching feature elements are already present
- resolve support-tag-driven `feature-pick` families such as PHB 2024 Sorcerer `Metamagic` into real option pools
- expand ASI choices into:
  - `+2` to one ability
  - `+1/+1` to two abilities
- expand feat-enabled ASI choices into a filtered feat pool
- apply selected choices back into the working state and re-evaluate iteratively
- emit a structured `computedCharacter` view for application consumption, including:
  - final ability scores
  - derived proficiencies and languages
  - granted spells with `spellcastingName` / `isPrepared`
  - active feats and features
  - applied text/list choices
  - derived traits such as size, vision grants like Darkvision, and typed movement traits like fly/climb/swim speeds
  - dedicated `movements` and `senses` collections for clearer app consumption
  - pending choices
  - warnings
  - provenance for explainability
- emit app-facing contract rows for:
  - choice families
  - pending choice rows
  - granted spell rows
  - normalized effect rows
  - grouped spellcasting profiles
- expose SQLite app-facing projections for:
  - `v_choice_templates`
  - `v_app_choice_rows`
  - `v_granted_spells`
  - `v_spellcasting_profiles`
  - `v_app_effect_rows`
- capture/check runtime regression baselines for:
  - direct selections
  - active features
  - computed ability scores
  - derived traits/proficiencies/languages/features
  - pending choices and warnings

This is the beginning of a builder backend, not the finished app runtime. More choice families still need second-stage resolution over time.

## Data Philosophy

Aurora content behaves like both:

- structured content
- a rules DSL

Because of that, AuroraTranslator preserves:

- normalized relational projections for common builder queries
- raw XML for rule and description fidelity
- parsed expression trees for requirement/support evaluation

The project does not assume that every important Aurora construct can be flattened into a single simple table.

## What Is Still In Progress

The importer/runtime foundation is strong, but several areas are still evolving:

- richer second-stage select resolution beyond ASI/feat
- stricter builder-state filtering for already-owned or mutually-exclusive choices
- broader character-state semantics for dynamic pools
- more builder-facing query surfaces
- eventual profile-aware package precedence

For the detailed implementation direction, see [ROADMAP.md](/C:/Users/Ralla/source/repos/5eApiTranslator/ROADMAP.md).
