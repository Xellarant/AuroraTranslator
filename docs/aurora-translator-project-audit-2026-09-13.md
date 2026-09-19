# Translator import and choice audit — September 13, 2026

This audit follows the clarified identity and publisher policy in section 15 of
[the handoff](aurora-translator-data-handoff.md). It covers content preparation and
activation, publisher classification, full/scoped relationship refresh, support
indexes, generic runtime choice discovery, stored choices, and direct-selection
accounting. The legacy SRD generator and Lights UI were not exercised end to end.
Pre-existing worktree edits were preserved. No installed XML or production
database was changed.

## Reproduced discrepancies and fixes

Each of the ten new audit tests failed before its corresponding fix.

| Regression | Observed behavior | Corrected behavior |
| --- | --- | --- |
| Static option index | A conjunction admitted an excluded option and an option of the wrong type; OR branches were incomplete. | Full and package refreshes evaluate the complete static expression and compatible type. |
| Runtime support discovery | Exclusions were ignored and an OR fallback omitted matching options from another package. | Generic static choices use the same expression evaluator and support contexts as the importer, including on legacy caches. Package affiliation is not eligibility. |
| Explicit item IDs | Missing or disabled inline IDs fell back to a same-named definition. | Inline/extract IDs resolve only to that ID. Authored name-only inline references retain unique-name resolution. |
| Explicit grants | Missing IDs were humanized into name guesses; an alias could then choose by package rank. | Definition grants require their actual IDs. Known intrinsic semantic grants retain their existing handling. |
| Scoped grant refresh | Changing the granting package's rank cleared a valid grant into an unchanged package without restoring it. | Every cleared owner-scope grant is rebound by its actual target ID. Disabled targets remain unresolved. Full/scoped parity now includes parent candidates and selector diagnostics. |
| Stored choice identity | Missing IDs fell through to names; a recycled row ID could defeat the supplied Aurora ID. | Aurora ID is authoritative. A missing ID or ambiguous name does not select a different option. |
| Direct named selection | A name-only reference could select two distinct definitions at once. | Ambiguity produces a diagnostic listing candidate Aurora IDs. |
| Direct choice accounting | An excluded subrace satisfied a choice through a loose support/name fallback. | A direct selection must belong to the complete static pool to satisfy that choice. |
| Primary support marker | The first support token was overwritten with the inferred parent even when the parent ID was a later token. | Only the support link actually identifying the singular parent is marked primary. Other tokens retain their own meanings. |
| Publisher flags | A Source with both `official=true` and `third-party=true` silently became official. | Contradictory publisher flags fail with the source ID/path and a request to correct the metadata. `core` plus `official` remains compatible. |

`AuroraSelectionRules` provides shared support contexts, expression classification
and type compatibility. `AuroraStaticSelectionOptions` replaces loose candidate
membership for generic static selectors. It preserves separately authored inline
choices. Runtime support contexts are reused within one read-only evaluation
connection, rather than reloading every declaration after each choice.

The XML correction contract, embedded baselines, authoritative-download acceptance
boundary, candidate activation, and retirement policy were not changed. Their
existing regression coverage remains part of verification.

## Test-fixture correction

The full suite exposed a previously hidden input error in these three examples:

- `character-state-warlock-invocations-complete-example.json`
- `character-state-warlock-invocations-overpick-example.json`
- `character-state-warlock-spellcasting-overpick-example.json`

They supplied nonexistent `ID_WOTC_PHB_ARCHETYPE_FIEND` and relied on the display
name `The Fiend` to recover. The frozen regression database and existing reviewed
baseline identify `ID_WOTC_PHB_ARCHETYPE_OTHERWORLDLY_PATRON_FIEND` in
`core/players-handbook/warlock-fiend.xml`. Only the mistaken input IDs were changed;
the expected applied choices and overpick assertions were not weakened.

## Content references requiring explicit corrections

A refreshed temporary copy of the priority-impact database exposes nine grant
references that previously resolved through name aliases. Their raw declarations
remain intact. These references now appear in unresolved-link diagnostics. A
content-authoring correction should choose and record the intended real ID;
the importer no longer supplies an arbitrary replacement.

| Content file, relative to the content root | Missing grant ID |
| --- | --- |
| `reddit/reddit-unearthed-arcana/kibblestasty/psion/psionic-disciplines.xml` | `ID_PHB_SPELL_CAUSE_FEAR` |
| Same file | `ID_PHB_SPELL_TELEPATHIC_BOND` |
| Same file | `ID_PHB_SPELL_BANISH` |
| Same file | `ID_PHB_SPELL_WALL_OF_FLAME` |
| `reddit/reddit-unearthed-arcana/kibblestasty/sorcerer-stoneheart.xml` | `ID_PHB_SPELL_ERUPTING_EARTH` |
| `third-party/dms-guild/class-spiritualist.xml` | `ID_PHB_SPELL_ANIMATE_OBJECT` |
| `third-party/dms-guild/the-great-dale/wizard-demonbinder.xml` | `ID_PHB_SPELL_SUMMON_LESSER_DEMONS` |
| Same file | `ID_PHB_SPELL_SUMMON_GREATER_DEMONS` |
| `third-party/genuine-fantasy-press/forgotten-secrets/race-imperial.xml` | `ID_GFP_PHB_SPELL_POISON_SPRAY` |

## Verification and remaining boundaries

The isolated database refresh passed integrity and foreign-key checks. All 40
recorded selection memberships for the four Blazing Dawn infusions remained
identical. Elements, raw grant and select declarations, and both correction
mirrors were unchanged. The original frozen database's SHA-256 was unchanged.
Nine inferred grant links became unresolved. The selectable index changed from
33,696 to 34,917 rows as complete expressions replaced partial/name membership;
those totals are index rows, not unique choices or a gameplay correctness score.
The refresh took about 3.7 seconds on this machine, not a controlled benchmark.

Temporary evidence:
`C:\Users\Ralla\AppData\Local\Temp\translator-project-audit-u7oofzdp\evidence.json`.

Final verification passed **all 51 console regressions**, including the ten new
audit regressions, five parent/source tests, eleven correction lifecycle tests,
and existing ownership, source-restriction, choice and spellcasting coverage.
The scoped grant test also compares full/scoped refresh results, including both
new parent tables. The three corrected Warlock inputs retain the existing
assertions. `git diff --check` passed.

Commands: `dotnet run --project AuroraTranslator.Tests/AuroraTranslator.Tests.csproj
--no-restore -- "audit scoped"` for the final targeted fix, then
`dotnet run --project AuroraTranslator.Tests/AuroraTranslator.Tests.csproj
--no-build --no-restore` for the full suite (exit 0). The captured full-suite log is
`C:\Users\Ralla\AppData\Local\Temp\translator-final-audit-20260913.log`.

At the close of this audit, two older runtime paths required a separate,
character-focused review. The subsequent implementation and remaining boundaries
are recorded in [the follow-up report](aurora-translator-followups-2026-09-13.md):

- Character-dependent selectors and specialized spell/feat/language/proficiency
  pools have additional semantics. The generic dynamic fallback still extracts
  support atoms, and dynamic direct-selection accounting still has legacy
  fallbacks. This audit does not establish their complete expression fidelity.
- `ResolveImplicitSpellListNameFromOwnerChain` returns the first Class reached
  through grants/selection links. Shared features and multiclass ownership need
  dedicated runtime fixtures before this can be treated as an authoritative
  owner. The new static parent candidate table does not resolve that question.

These are identified review boundaries, not claims of demonstrated failure for a
particular live character. Canonical uniqueness, advanced review UI, shared-library
packaging, and publication remain outside this pass. All changes remain local.
