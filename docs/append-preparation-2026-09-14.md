# Append preparation and unrestricted catalog follow-up

September 14, 2026. Translator implementation and isolated verification only.
The production database, installed XML, and Aurora-Lights source are unchanged.
The full isolated import and record audit passed, including a repeat import.
Production activation remains pending the Aurora-Lights consumer work.
No commit, push, snapshot, or app bundle was published.

## Implemented in Translator

`ContentPreparation` captures inputs, evaluates the existing protected correction
contract, collects all base/corrected definitions, consolidates identical
declarations with provenance, then applies extensions by exact Aurora ID.
Conflicting same-ID definitions still require an explicit protected correction.
There is no name, publisher-rank, or source-enable fallback for append targets.

`ContentAppendComposer` preserves supports, rules, description fragments, setters,
and a new spellcasting block. Rules retain authored order and multiplicity;
supports use the existing balanced delimiter parser and token union. Description
fragments merge into one container. Conflicting setters, replacement spellcasting,
type mismatches, and unsupported structures fail with file/operation/target
diagnostics. Missing targets retain their operations and diagnostics for authoring
review or binding to intrinsic definitions supplied by an app.

The writer checks every finalized ID and verifies persisted append supports and
grant/select/stat rules before activation. SQLite mirrors the full correction
contract and also stores base/effective XML, all suppliers, operation order,
source provenance, and input hashes. The new data version is 12; the preparation
capability marker specifies contract 1, `unrestricted`, and `materialized`.

Global catalog membership, grant resolution, and option links no longer depend
on `is_enabled`. That field remains transitional app-preference storage. The
read-only `PreparedCatalogReader` optionally filters sources per request and
reconstructs eligible extensions from corrected **base** XML; it never mutates
the complete database or appends onto already extended XML. Host definitions may
supply absent intrinsic IDs but cannot resurrect an excluded catalog definition.

Existing Source declarations now distinguish publisher identity from entry
citations. For example, Ryoko's third-party package stays third-party when a
helper cites the Player's Handbook. The mixed Book of Xellarant collection has
separate `--homebrew` and `--official` package buckets, using its declared source
metadata. Homogeneous package keys stay stable; new split buckets inherit the
old bucket's saved preference values. Ambiguous classifications within a file
still require review. Explicit local/reddit content remains homebrew and Wizards
UA remains first-party. This is independent of publication maturity or enabled
state. Published supplement roots interpret older `homebrew` flags as unofficial
published content (third-party), while explicit first-party Source metadata still
identifies Wizards publications. This covers Improved Backgrounds and the
Wayfinder's files without trusting the DMs Guild folder to imply one publisher.

Multiple valid supports/grants parents are reported as
`multiple-supported-parents`, rather than mislabeled missing parents or assigned
an arbitrary singular parent.

## Protected local aliases prepared

The explicit `ProtectedLocalAliases` authoring helper creates separate local IDs,
rewrites exact references within that local document, and preserves authoritative
IDs. Otherwise unchanged local parents that reference a new local child receive
their own aliases. Root flags and non-declaration operations must be reviewed
separately so the authoring helper cannot discard them.

Three complete protected XML drafts were generated and applied to the isolated
rehearsal copy, **not installed**:

| Local file | Conflicting IDs retained as variants | ID suffix |
| --- | ---: | --- |
| `background-farmer.xml` | 1 | `_LOCAL_PHB24_FARMER` |
| `players-handbook-2024-items-packs.xml` | 31 | `_LOCAL_PHB24_PACKS` |
| `efa-class.xml` | 46 | `_LOCAL_EFA` |

The full plans, alias maps and original/source/output hashes are under
`artifacts/append-full-validation/aliases`. No references from these three local
documents target an ID being aliased in either of the other two documents.
Review-pending `add` corrections preserve the variants beside the originals with
embedded baselines, fingerprints, groups and reasons. Added definitions are
attributed to the local/homebrew supplier in preparation; the authoritative
definitions keep their source attribution.

**Reminder for the user:** the Farmer, equipment-pack, and EFA variants still
need coordinated installation and review. Aliasing preserves both versions; it
does not choose which version a character should use or assert that the local
variant is the publisher's intended version.
Any character intended to use a local variant will need its new `_LOCAL_...` ID;
references to an original ID remain references to the authoritative definition.
Character files and external references were not automatically remapped.
The existing Stoneheart correction
and earlier protected corrections remain installed and pending. The other eight
grant-repair drafts have not been installed. No matching file or successful
rehearsal constitutes acceptance, and no file was retired. Verified authoritative
download evidence remains outside this slice.

## Verification

All **65 console regressions passed**, exit 0, including six append/alias
regressions. These cover correction-before-extension ordering, repeat import,
extension removal, source-filter isolation, exact IDs, retained missing-target
operations, intrinsic host definitions, failed-candidate preservation, local
supplier attribution, parent alias closure, and protected variants surviving an
authoritative update. The revised publisher-classification regression covers
reprinted helpers, mixed collections, and preserved preference values. The final
published-homebrew classification refinement passed its focused regression after
that full run and was verified in the repeat full-catalog import.

Commands and evidence:

- `dotnet run --project AuroraTranslator.Tests/AuroraTranslator.Tests.csproj --no-restore`
  (build and full regression run); the final repeat used `--no-build`.
- `artifacts/append-suite-final-20260914.log`: 65 PASS lines, zero FAIL lines.
- `artifacts/append-focused-final-20260914.log`: four append tests passed.
- `artifacts/source-classification-final.log`: classification regression passed.
- `artifacts/generic-types-regression.log` and `artifacts/spell-rules-regression.log`:
  the two regressions added after the initial append tests.
- `git diff --check` passed. The proposed Aurora-Lights patch passed
  `git apply --check` against copies of the originals inside Translator's
  workspace; it has not been applied or built in Aurora-Lights.

A full CLI import from the isolated copy of all **1,190 installed XML files**
passed preparation and candidate validation after the alias drafts and fixes.
It retained **1,215 append operations**: **1,194 applied**, while 21 operations
target 13 absent XML IDs. One target is the intrinsic
`ID_INTERNAL_GRANTS_CHARACTER_BASE`; the others are explicit unresolved references
in Psion, Ryoko and Swordmage content. They were retained with actionable
diagnostics, never redirected by name.

An earlier attempt correctly rejected a candidate because the existing type
registry omitted these declarations. The approved generic-record fix now retains
all three, with diagnostics:

| Aurora ID | Authored type | File |
| --- | --- | --- |
| `ID_RGTTYR_ACTION_BRACE` | `Action` | `barbarian-kaiju.xml` |
| `ID_RGTTYR_FEAT_FOCUSED_DISCIPLINE_FEATURES` | `Feat Features` | `feats.xml` |
| `ID_RGTTYR_WEAPON_CATEGORY_MAGITECH_FIREARM` | `Weapon Category` | `items-weapons.xml` |

The user approved preserving these as generic typed records with diagnostics.
`AuroraGenericTypes` now registers the exact authored type with
`loader_family='generic-unrecognized'`; shared text, supports and rules are stored,
while type-specific semantics are not inferred. The
`v_unrecognized_element_type_diagnostics` view retains actionable IDs, types and
file paths. Plural `Feat Features` stays plural. Newly registered types force a
reimport of affected files even when an older writer cached their unchanged
hashes after omitting those records. A new regression verifies retention, repeat
import, diagnostics, and that migration case.

Validation also caught the old spell path dropping rules, including Find
Familiar's companion selection. `AuroraSpell` now reuses the shared Aurora element
parser/writer for rules, requirements, extracts, additional blocks, spellcasting,
sheet and prerequisite content. An omitted compendium display flag now retains
the normal true default. Spell descriptions still use the existing spell-specific
description/summary storage, with no duplicate description rows. The new regression
verifies the selection pool, appended grant/stat rules and replay exactly once.

The final read-only audit (`artifacts/append-full-validation/audit.py`,
`evidence.json`, and `audit.log`) confirms:

- **20,954 unique elements**, all present in the unrestricted resolution cache;
  zero duplicate Aurora IDs or mismatched resolved grant IDs.
- **12,612 grants**, **4,201 selects**, and **46,069 option links**.
- SQLite integrity `ok`, no foreign-key failures, and no unlinked grant whose
  requested exact ID exists in the catalog.
- **1,068 definitions** from disabled packages remain in the complete catalog.
  All pre-existing package enable/rank preferences are unchanged.
- Both Staff of Flowers item IDs and both real Erupting Earth spell IDs remain.
  Stoneheart grants XGTE Erupting Earth; Devout and Tatsumi link to repaired IDs.
  All **40** Blazing Dawn infusion memberships remain.
- Acid Splash includes `UA Artificer`; martial ranged proficiency includes the
  appended firearms grants. Repeat import reports **0 changed files / 1,190
  unchanged**, with no duplicated append effects.
- **10** protected local files and **90** review-pending corrections are mirrored:
  79 additions (78 aliases plus Farmer's existing unique Tough feature), seven
  renames, three replacements and one removal. No acceptance or retirement.

The provenance-rich rehearsal database is 212,873,216 bytes (about 203 MiB),
compared with the 81,666,048-byte input database. It stores base/effective XML and
supplier/operation evidence in addition to normalized records; storage and app
startup costs need measurement during the consumer integration.

The failed attempts preserved their working target before the successful rebuild.
Production SHA-256 remains
`D60707B06A9804CF2386DCB963786C802B6F4280A3787E4D33EFB6B4A2609E8C`.
All 1,190 installed XML hashes still match the captured input set.

## Remaining content review

The final catalog has **nine missing grant references**: the eight previously
drafted spell-reference repairs and Ryoko's missing
`ID_RGTTYR_FEATURE_REPLACEMENT_BENDER_EXTRA_ATTACK`. These remain explicit
diagnostics. There are also the 21 unresolved append operations described above
and the three preserved generic-type diagnostics. Review exact target IDs and
author intent before writing protected reference/type corrections.

The authoritative PHB24 Guard equipment pack references
`ID_WOTC_PHB24_WEAPON_CROSSBOW_LIGHT`, while the actual definition is
`ID_WOTC_PHB24_WEAPON_LIGHT_CROSSBOW`. Its local aliased variant already uses the
existing ID. Recommend a protected reference correction for the authoritative
pack, added to the same managed pack file so correction files do not overlap;
do not merge the distinct variants. This is the tenth actionable loader-link
diagnostic (alongside the nine grants).

Another **1,554** relationship diagnostics represent multiple supported parents,
not missing targets; **64** represent option pools. They do not justify choosing
a parent by name or precedence. Full row-level evidence is in `evidence.json`.

## Aurora-Lights coordination

The user requires cross-project implementation to remain in its own task.
No Aurora-Lights source edits were applied. The proposed consumer adapter is
available as a [handoff with a copyable prompt](proposals/reflections-prepared-content-v12.md)
and an [eight-file patch](proposals/reflections-prepared-content-v12.patch).
It is **uncompiled and untested in Aurora-Lights**. Its purpose is to consume
prepared data once, apply app preferences in an app view, and prevent the older
bundled writer from replacing preparation/provenance data.

Legacy's existing exact-ID XML append path was inspected and left unchanged.
Reflections currently has additional XML overlay/fallback paths; those must be
coordinated with v12 so materialized effects are not applied twice. Current
installed binaries have not acquired the proposed adapter. Production activation
and local-plan installation remain pending that coordinated rollout. The rehearsal
database records rehearsal XML paths: **do not activate it by copying it over
production**. After reviewing/installing the protected plans, rerun the guarded
Translator import against the actual installed content root. No shared-library
packaging or canonical uniqueness migration was performed.
