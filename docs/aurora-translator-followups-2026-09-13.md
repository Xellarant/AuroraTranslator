# Runtime selection and grant-reference follow-ups — September 13, 2026

This follows the [project audit](aurora-translator-project-audit-2026-09-13.md).
Source changes remain local and uncommitted. The initial follow-up pass changed
no installed XML. The subsequently authorized
[Stoneheart local correction](stoneheart-local-correction-2026-09-14.md) was
installed on September 14. No production database, Lights bundle, snapshot or
sibling implementation was changed by this work.

## Runtime selection behavior

Generic, language, proficiency and feat support filters now evaluate the complete
expression against the candidate's Aurora ID and declared supports. They no
longer flatten OR/conjunction/exclusion expressions into an unordered list of
tags, infer feat families from prompt names, or match proficiency substrings.
Independently authored inline options remain supported. Direct selections must
belong to the resulting pool before they can satisfy a pending choice.

Character macros bind against the candidate's tokens. Bracket conditions read
character numeric/scalar values. Character-owned tokens are not added to the
candidate's support set. The existing expression evaluator now accepts bound leaf
values and propagates unknown values through boolean operations: negating an
unbound macro cannot turn it into permission. A definite OR branch can still
match. Missing bindings and invalid expressions produce character warnings.

Spell filters use the same parsed expression tree. They preserve correlated
branches such as `(Wizard, 0) || (Cleric, 1)` and nested profile-list predicates.
Candidate facts include level, school, ritual status, list membership and casting
time. Slot bounds come from active owners; unrelated installed classes cannot
raise the cap. Existing source restrictions, requirements, repeatability and
stored-choice availability rules still apply.

The importer continues to defer character-dependent parent relationships. Static
parent candidates describe structural eligibility, not a character's ownership.

## Spellcasting ownership

Implicit spellcasting now follows active grant edges and validated applied
choices, including choices already reflected in the saved character. Direct
class-ID supports also provide explicit ancestry. Traversal tracks visited IDs
and terminates cycles. It does not walk every installed grant or every possible
selector, or use database row order to select a Class.

An explicit profile on the selector takes priority. Otherwise, the character must
have one active owning Class and one applicable active non-extension profile with
a declared list. Multiple owners or profiles produce warnings and leave the
unresolved list binding unavailable. Warnings identify the candidate Class IDs
when ownership is ambiguous. The engine does not invent a saved choice's origin
from a display name. Older direct feature/subclass references without sufficient
ownership evidence may therefore need explicit choice/profile information.

## Review-pending content corrections

Eight missing grant IDs have concrete proposed targets:

| Missing ID | Proposed target ID |
| --- | --- |
| `ID_PHB_SPELL_CAUSE_FEAR` | `ID_XGTE_SPELL_CAUSE_FEAR` |
| `ID_PHB_SPELL_TELEPATHIC_BOND` | `ID_PHB_SPELL_RARYS_TELEPATHIC_BOND` |
| `ID_PHB_SPELL_BANISH` | `ID_PHB_SPELL_BANISHMENT` |
| `ID_PHB_SPELL_WALL_OF_FLAME` | `ID_PHB_SPELL_WALL_OF_FIRE` |
| `ID_PHB_SPELL_ANIMATE_OBJECT` | `ID_PHB_SPELL_ANIMATE_OBJECTS` |
| `ID_PHB_SPELL_SUMMON_LESSER_DEMONS` | `ID_XGTE_SPELL_SUMMON_LESSER_DEMONS` |
| `ID_PHB_SPELL_SUMMON_GREATER_DEMONS` | `ID_XGTE_SPELL_SUMMON_GREATER_DEMON` |
| `ID_GFP_PHB_SPELL_POISON_SPRAY` | `ID_PHB_SPELL_POISON_SPRAY` |

These are explicit authoring corrections, not importer aliases or automatic
acceptance. The Psion's `banish` and `wall of flame` wording is itself imprecise;
those proposed PHB corrections retain manual-review confidence. Existing spell
levels, spellcasting labels, `known`, `prepared` and requirements are preserved.

The checked-in [repair records](../AuroraTranslator.Tests/Fixtures/GrantReferenceRepairs/repairs.json)
reuse XMLHelper's diagnostic and `set-rule-attribute` repair fields, including
current/replacement values, target node XML and samples. Each file's SHA-256 binds
the review to the inspected input. Extracted owner and target declarations are
regression fixtures, not deployable source files.

Full-file protected XML drafts were generated separately with the existing
`LocalCorrectionDocument.Create` and `Evaluate` APIs in
`artifacts/grant-reference-review/user/local`. Four files contain seven replacement
operations covering the eight grant edits; Telepathy's two grants share an owner
operation. Every operation is `review-pending`, with the complete original file
embedded as its baseline and an original declaration fingerprint. The generation
refused stale input hashes and checked installed source hashes again afterward.
`artifacts/grant-reference-review/evidence.json` records source/output hashes.
These ignored local artifacts were not installed or automatically accepted.

The user's subsequent clarification retains both Erupting Earth definitions:
`ID_POTA_SPELL_ERUPTINGEARTH` and `ID_XGTE_SPELL_ERUPTING_EARTH` are distinct IDs
with distinct source attribution. A Xanathar's preference was authorized only if
these were the same Aurora ID; that condition does not apply.

Stoneheart's `ID_PHB_SPELL_ERUPTING_EARTH` is a third, nonexistent ID. It was
initially recorded as an unresolved reference with two candidate edits. The user
subsequently authorized the explicit XGTE target for this homebrew grant; the
[installed correction record](stoneheart-local-correction-2026-09-14.md) supersedes
that unresolved status. The repair manifest now contains nine corrections. Both
real definitions remain in the fixtures and import independently.

## Verification

The first five runtime regressions reproduced the old behavior before fixes.
Seven follow-up regressions now pass, covering dynamic bindings and exclusions,
language/proficiency/feat expressions, correlated spell branches, conditional
grant ownership, applied-choice ownership, active slot/profile bindings, and all
eight protected grant-reference repairs through preparation and SQLite import.
The repair test compares unchanged grant attributes, declaration fingerprints,
embedded baselines, resolved target IDs and review-pending mirror state.

Final verification passed **all 58 console regressions** (exit 0). The focused
command was `dotnet run --project AuroraTranslator.Tests/AuroraTranslator.Tests.csproj
--no-restore -- "follow-up"`; the full run used `--no-build --no-restore` without
the filter. The final log is
`C:\Users\Ralla\AppData\Local\Temp\translator-followups-final-20260913.log`.
The build completed with zero warnings/errors. `git diff --check` passed.

## Distinct-ID clarification and verification

The clarification exposed a remaining runtime collapse: spell candidates with
equal names, levels and content signatures were reduced to one representative by
package preference/rank. That filter has been removed. Distinct Aurora IDs remain
independently selectable even when their wording and mechanics match exactly.
Source restrictions still apply to each definition independently. The new
regression failed before the fix, then verifies both IDs, saved picks of either
ID, and independent restrictions by source. This is an identity-preservation fix,
not the canonical uniqueness migration.

Focused verification passed **15 spell regressions** with filter `"spell"` and
the updated protected-repair regression with filter `"grant repairs"` (both exit
0). The latter imports both actual Erupting Earth declarations and verifies that
Stoneheart's missing ID is not silently rebound. `git diff --check` passed. The
spell log is `C:\Users\Ralla\AppData\Local\Temp\translator-distinct-spells-20260913.log`.
The full harness now registers 59 tests; it was not rerun in full for this bounded
change. The earlier 58-test result is historical verification before this fix.

Canonical uniqueness, advanced review UI, shared packaging and verified-download
automatic acceptance remain outside this slice. This work does not claim to
exhaustively validate every legacy character document.
