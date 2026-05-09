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
- builder-facing catalog views
- a first-pass character-state evaluator

Notable runtime capabilities already in place:

- classes, archetypes, races, subraces, race variants, backgrounds, feats, spells, languages, proficiencies, items, sources, and many long-tail families import cleanly
- actionable unresolved importer links are down to zero on the committed first-party baseline
- expression parsing and evaluation are shared between import-time and runtime logic
- package precedence can be inspected, changed, parity-checked, and refreshed from the CLI
- character-state evaluation can resolve active features, grants, and select pools
- ASI/feat selects now expand into second-stage builder choices
- character-state JSON output now includes a `computedCharacter` section with derived scores, traits, pending choices, warnings, and provenance

## Repository Layout

- [AuroraTranslator.sln](/C:/Users/Ralla/source/repos/5eApiTranslator/AuroraTranslator.sln)
- [AuroraTranslator.csproj](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraTranslator.csproj)
- [Program.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Program.cs)
- [AuroraSqliteImporter.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraSqliteImporter.cs)
- [AuroraExpressionEngine.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraExpressionEngine.cs)
- [AuroraCharacterStateEngine.cs](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/AuroraCharacterStateEngine.cs)
- [sqlite-character-loading.sql](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/sqlite-character-loading.sql)
- [character-state-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-example.json)
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
```

The committed baseline is meant to represent Wizards first-party `core` + `supplements`, not an arbitrary custom content directory.
The committed character-state baseline is meant to represent the example first-party fixture at [character-state-example.json](/C:/Users/Ralla/source/repos/5eApiTranslator/5eApiTranslator/Data/character-state-example.json) evaluated against the first-party regression DB.

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
- treat ASI-style selects semantically
- expand ASI choices into:
  - `+2` to one ability
  - `+1/+1` to two abilities
- expand feat-enabled ASI choices into a filtered feat pool
- apply selected choices back into the working state and re-evaluate iteratively
- emit a structured `computedCharacter` view for application consumption, including:
  - final ability scores
  - derived proficiencies and languages
  - active feats and features
  - applied text/list choices
  - semantic traits such as size
  - pending choices
  - warnings
  - provenance for explainability
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
