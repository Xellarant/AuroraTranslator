# Roadmap

This roadmap reflects the project state as of the current `master` branch after:

- canonical Aurora XML -> SQLite import
- package precedence and parity checks
- source-integrity and unresolved-link diagnostics
- builder-facing SQLite views
- first-pass character-state evaluation
- second-stage ASI/feat choice expansion
- structured computed-character output with pending-choice and provenance reporting
- repeatable character-state regression baselines for first-party runtime fixtures
- first-pass app-facing choice family and granted-spell projections

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
- Iterative choice application and re-evaluation
- Pending multi-pick select counts now key off explicit applied choices rather than unrelated globally owned options
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

Likely target families:

- variant feature replacement flows
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
  - archetype selection
  - language/proficiency selections
  - Human Variant feat flow
  - spell-grant scenarios such as domain or oath spells
- use focused fixtures to lock down computed-trait scenarios such as Darkvision and typed movement entries like fly/climb/swim/burrow speeds
- add guardrails for semantic expansion counts if useful
- add guardrails for computed-character outputs such as:
  - final ability score totals
  - pending blocking choice counts
  - key provenance expectations for canonical first-party states

### 4. Continue builder-facing query refinement

- add more views tailored to real character-building screens
- identify places where the builder should query SQLite directly versus evaluating in code
- tighten distinction between:
  - canonical content storage
  - runtime query projections
  - live character-state evaluation

### 5. Improve trust / app integration surfaces

- align the evaluator output with Aurora App correctness goals
- expose enough metadata and provenance that the app can explain:
  - where a computed trait came from
  - why a choice is pending or blocked
  - which package/source contributed a resolved item
- use the computed-character contract to inform future loader-oriented projection views

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
