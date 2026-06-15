# Roadmap

This roadmap reflects the project state as of the current `master` branch after:

- canonical Aurora XML -> SQLite import
- package precedence and parity checks
- source-integrity and unresolved-link diagnostics
- first WPF-authoritative parity baselines for core DB loader surfaces
- builder-facing SQLite views
- first-pass character-state evaluation
- second-stage ASI/feat choice expansion
- structured computed-character output with pending-choice and provenance reporting
- repeatable character-state regression baselines for first-party runtime fixtures
- first-pass app-facing choice family and granted-spell projections
- first-pass flat effect-row and spellcasting-profile projections for the app
- deduped SQLite app-effect summary rows layered on top of lower-level effect templates

## Completed Foundations

### Canonical import and storage

- Import Aurora XML content into SQLite
- Preserve rules, setters, requirements, supports, spellcasting, multiclass, extracts, and additional structural blocks
- Preserve raw XML for descriptions, sheets, grants, selects, and stats
- Persist expression trees and expression usages
- Support broad content coverage beyond only the original high-value element families

### Resolution and diagnostics

- Content packages and precedence ranks
- Resolved winner caches for duplicate Aurora IDs
- Scoped precedence refresh with parity checking against full rebuilds
- Admin/debug views for package resolution and unresolved links
- Repeatable unresolved-link summaries
- Source-integrity summaries
- Committed first-party diagnostics baseline for `core` + `supplements`
- Committed first-pass WPF parity baseline for first-party `core` + `supplements`, covering:
  - total/type/package counts
  - multiclass rows
  - spellcasting profile surfaces
  - spell-access rows
  - companion distributions
  - a curated set of known-good archetype/profile/companion/spell samples

### Builder-facing data layer

- Class feature progression view
- Archetype feature progression view
- Archetype slot view
- Background/race/subrace/race-variant core views
- Granted proficiency/language views
- Selectable option view

### Runtime evaluation layer

- Shared Aurora expression parsing/evaluation
- Character-state document loading
- Direct element resolution from state
- Active feature and grant resolution
- Select-policy handling for:
  - generic fixed pools
  - text-choice pools
  - broad language pools
  - broad proficiency pools
  - ASI feature pools
- Second-stage semantic expansion for:
  - `Ability Score Improvement`
  - `Feat`
- Deterministic first-party PHB 2024 one-option `feature-pick` rows now auto-materialize into runtime state instead of surfacing as fake pending choices
- Existing direct state such as an already-selected subclass can now satisfy matching generic pending picks instead of surfacing as false pending work
- Iterative choice application and re-evaluation
- Pending multi-pick select counts now key off explicit applied choices rather than unrelated globally owned options
- Common first-party multi-pick pools now behave like real slots across Acolyte/Fighter/Monk flows, and extra selected picks stop applying once a pool is full
- Multi-select `feature-pick` families such as classic Rogue `Expertise`, Warlock `Eldritch Invocation`, and PHB 2024 Sorcerer `Metamagic` now behave like real builder slots, including saved-state satisfaction from direct feature elements
- Classic Rogue `Expertise` is now covered by partial, direct-state, and fully completed first-party fixtures so we can detect regressions across the whole progression
- Warlock Pact Magic spell-pick rows now resolve through spellcasting profiles, fill real spell option pools, and participate in slot-aware over-pick validation
- Granted rule-owning elements such as racial traits and nested archetype features now participate in select evaluation, which lets builder flows surface picks like High Elf Wizard cantrips and Abjurer school-restricted spells
- Null-profile and constrained `spell-pick` rows now resolve real option pools instead of falling back to empty generic behavior
- Support-driven fixed pools now resolve real element options even when Aurora encoded them only in `supports_text`, which makes nested feat families such as PHB 2024 `Magic Initiate` behave like normal builder choices
- Broad feat pools now honor support tags and explicit allowlists, so origin-feat slots and general-feat slots do not bleed into one another
- Ritual-only spell picks can now resolve against the global ritual corpus without a spell-list owner, which makes PHB 2024 `Ritual Caster` behave like a real feat package
- Nested class-feature spell picks now inherit their parent class spell list when Aurora encoded them without a local profile, which makes early PHB 2024 flows like Cleric `Thaumaturge` complete cleanly
- Broad spell pools now collapse only exact-equivalent spell reprints, using the stored spell/text/rule shape instead of plain name matching, so distinct 2014/2024 spell variants remain separate
- Over-selected first-party choice states now fail loudly, either as explicit over-selection warnings or immediate `select-full` application errors
- Derived character output for:
  - computed ability scores
  - proficiencies and languages
  - granted spell rows with spellcasting/prepared metadata
  - feats and features
  - applied text/list selections
  - semantic, grant-derived, and stat-derived traits
  - explicit movement and sense groupings for app-facing consumption
  - pending choices
  - warnings
  - provenance / explainability
- app-facing contract rows for:
  - normalized choice families
  - pending choice rows
  - granted spell rows
  - runtime-resolved spell-pick rows
  - flat effect rows
  - grouped spellcasting profiles
- SQLite-side app-facing projection views for:
  - `v_choice_templates`
  - `v_app_choice_rows`
  - explicit static option counts plus runtime-resolution metadata for dynamic choice families
  - `v_granted_spells`
  - `v_spellcasting_profiles`
  - `v_app_effect_rows`

## Current Milestone

The project is now in the transition from "translator and diagnostic backbone" to "real builder backend."

That means current work should prioritize:

- turning builder-relevant semantics into explicit runtime choices
- keeping those semantics testable and inspectable
- avoiding regressions in import fidelity while expanding runtime behavior

## Next Recommended Milestones

### 1. Expand second-stage choice resolution

High priority:

- tighten feat follow-up filtering
  - optionally exclude already-owned feats from available pools
  - surface unavailable reasons more explicitly
- add more semantic choice families where raw support links are too broad
- distinguish follow-up actions from follow-up element picks more clearly if the consumer needs it
- broaden choice application beyond the current ASI / feat-first flows
- continue tightening replacement-style feature semantics beyond the current deterministic PHB 2024 one-option default picks

Likely target families:

- variant feature replacement flows with explicit suppression/replacement semantics
- optional class/archetype family picks
- more dynamic language/proficiency sub-pools

### 2. Improve character-state semantics

- represent more builder options directly in the state document
- widen support for dynamic Aurora macros/tokens in runtime evaluation
- add richer owner-context-aware filtering for selects
- decide where hard validation belongs for caps, exclusivity, and replacement rules
- continue tightening computed-character aggregation so the app can rely on it as a runtime summary

### 3. Add regression coverage for runtime evaluation

- commit targeted character-state smoke cases
- keep expanding beyond the current committed first-party example baseline
- add more fixtures for:
  - Fighter ASI with an applied choice
  - Fighter Fighting Style
  - language/proficiency selections
  - Human Variant feat flow
  - duplicate-pick validation for selected-choice flows
  - additional PHB 2024 replacement-style families beyond Cleric/Monk
  - spell-grant scenarios such as domain or oath spells
  - completed player-facing class/race/background packages beyond the new Life Domain + Human + Acolyte fixture
  - more `feature-pick` families such as Weapon Mastery and Eldritch Adept
  - more `spell-pick` families beyond Magic Initiate and Ritual Caster, especially broader subclass spell-selection cases
  - explicit duplicate-pick and over-pick validation scenarios beyond the current first-party language oversubscribe fixture
- use focused fixtures to lock down computed-trait scenarios such as Darkvision and typed movement entries like fly/climb/swim/burrow speeds
- add guardrails for semantic expansion counts if useful
- add guardrails for computed-character outputs such as:
  - final ability score totals
  - pending blocking choice counts
  - key provenance expectations for canonical first-party states

### 4. Continue builder-facing query refinement

- add more views tailored to real character-building screens
- identify places where the builder should query SQLite directly versus evaluating in code
- ensure app-facing choice/projection views preserve the mutual-exclusion rule between 2014 racial ability score increases and 2024 background ability score increases; a character should resolve only one origin ASI source
- tighten distinction between:
  - canonical content storage
  - runtime query projections
  - live character-state evaluation

### 5. Deepen app-facing effect projections

- align the evaluator output with Aurora App correctness goals
- continue flattening nested runtime summaries into stable app contracts
- widen DB-side candidate views like `v_effect_templates` and `v_spellcasting_profiles` where static content can be projected safely
- expose enough metadata and provenance that the app can explain:
  - where a computed trait came from
  - why a choice is pending or blocked
  - which package/source contributed a resolved item
- use the computed-character contract to inform future loader-oriented projection views and effect summaries

### 5.5. Broaden WPF-authoritative parity coverage

- extend the new parity harness beyond first-pass structural surfaces
- add more curated authoritative cases for:
  - spellcasting extension lists
  - append/overlay behavior where the source corpus uses it
  - companion reconstruction edge cases
  - race/subrace/variant parent relationships
  - representative feat/background/class progression families
- keep the parity harness aimed at Aurora Lights WPF behavior first, with Aurora App loader concerns treated as secondary follow-up checks

### 6. Eventually add profile-aware precedence

Current precedence is global/package-based. A later phase should support multiple active content profiles such as:

- core only
- first-party only
- campaign-specific content sets
- custom local/homebrew overlays

That likely means:

- profile tables
- profile/package membership
- profile-specific resolved winner caches or views

## Nice-To-Have Work

- a small admin UI for package precedence and diagnostics
- richer source-integrity classification buckets
- explicit explanation data for why an option is excluded
- more ergonomic CLI reporting for deep follow-up choices

## Working Principles

When choosing what to do next, prefer work that improves at least one of these without destabilizing the others:

- import fidelity
- runtime correctness
- diagnosability
- builder usefulness

The project is in a good place now to favor builder usefulness, as long as the regression and diagnostics workflows continue to stay healthy.
